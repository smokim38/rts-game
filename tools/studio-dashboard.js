#!/usr/bin/env node
'use strict';
// The studio's live dashboard (Issue #47): one local page showing the Claude sessions and
// subagents and the Codex launcher runs and helper agents that are running, with their tokens.
//
//   node tools/studio-dashboard.js [--port 4317]
//
// It listens on 127.0.0.1 only and refuses any other address. It is read-only and reads only:
// - .studio/logs/*.jsonl, the launcher's run logs (the entries #46 added)
// - this repo's Claude Code session logs, in the user's .claude/projects/<repo folder>/: the
//   session logs and each session's subagents/ logs and info files
// Live only: an agent appears if it is running when the dashboard starts or starts later.
// Records are reduced to names, times, states and numbers as they are read, so no conversation
// text, prompts, tool input or output, commands or paths reach the page.
// Node.js built-in modules only. Tests: node --test tools/studio-dashboard.test.js

const crypto = require('node:crypto');
const fs = require('node:fs');
const http = require('node:http');
const os = require('node:os');
const path = require('node:path');
const { StringDecoder } = require('node:string_decoder');

const ROOT = path.resolve(__dirname, '..');
const DEFAULT_PORT = 4317;
const LOOPBACK = new Set(['127.0.0.1', 'localhost']);
// A Claude session or subagent counts as running while its log was written this recently; the
// logs have no end record for a session.
const ACTIVE_MS = 5 * 60 * 1000;
// A launcher run lasts at most 4 hours; a log with no end after that belongs to a stopped launcher.
const RUN_LIMIT_MS = 4 * 60 * 60 * 1000 + 10 * 60 * 1000;
const POLL_MS = 1000;
// Large session logs are read in steps, so one poll never holds more than this.
const READ_CHUNK = 8 * 1024 * 1024;

class Refused extends Error {}

// ---------- inputs ----------

function parseArgs(argv) {
  const opts = { port: DEFAULT_PORT, host: '127.0.0.1' };
  for (let i = 0; i < argv.length; i++) {
    const [flag, inline] = argv[i].split(/=(.*)/s);
    const value = inline ?? argv[++i];
    if (flag === '--port' && /^\d+$/.test(value ?? '') && Number(value) > 0 && Number(value) < 65536) opts.port = Number(value);
    else if (flag === '--host' && LOOPBACK.has(value)) opts.host = '127.0.0.1';
    else if (flag === '--host') throw new Refused(`the dashboard listens only on 127.0.0.1, not ${JSON.stringify(value ?? '')}`);
    else throw new Refused(`usage: node tools/studio-dashboard.js [--port <1-65535>]`);
  }
  return opts;
}

// Claude Code names a project's folder after its path, with every other character as `-`.
const claudeProjectDir = (root = ROOT, home = os.homedir()) => path.join(home, '.claude', 'projects', path.resolve(root).replace(/[^A-Za-z0-9]/g, '-'));

// ---------- reading logs ----------

const count = (n) => (Number.isInteger(n) && n >= 0 ? n : null);
const time = (s) => (typeof s === 'string' && !Number.isNaN(Date.parse(s)) ? new Date(s).toISOString() : null);
// Names come from fixed sets in the logs (agent types, models, roles); anything else is dropped.
const name = (s) => (typeof s === 'string' && /^[\w.:/@ -]{1,80}$/.test(s) ? s : null);

const stat = (file) => {
  try {
    return fs.statSync(file);
  } catch {
    return null;
  }
};
const list = (dir) => {
  try {
    return fs.readdirSync(dir);
  } catch {
    return [];
  }
};

// Reads a growing JSON-lines file from where it last stopped. A partly written last line waits
// for the next read; malformed lines are skipped.
function tailer(file) {
  let offset = 0;
  let rest = '';
  let decoder = new StringDecoder('utf8');
  return {
    read() {
      const size = stat(file)?.size ?? 0;
      if (size < offset) {
        offset = 0;
        rest = '';
        decoder = new StringDecoder('utf8');
      }
      if (size === offset) return [];
      const buf = Buffer.alloc(Math.min(size - offset, READ_CHUNK));
      const fd = fs.openSync(file, 'r');
      try {
        offset += fs.readSync(fd, buf, 0, buf.length, offset);
      } finally {
        fs.closeSync(fd);
      }
      const lines = (rest + decoder.write(buf)).split('\n');
      rest = lines.pop();
      const records = [];
      for (const line of lines) {
        try {
          const r = JSON.parse(line);
          if (r && typeof r === 'object') records.push(r);
        } catch {
          // a malformed line
        }
      }
      return records;
    },
  };
}

// ---------- Claude ----------

// One Claude session or subagent. Claude Code writes each reply as one record per content
// block, all with the same message id and usage, so usage counts once per message id.
function claudeAgent(kind, file) {
  const usage = new Map();
  return {
    kind,
    file,
    tail: tailer(file),
    name: null,
    model: null,
    startedAt: null,
    lastAt: null,
    usage,
    add(r) {
      const at = time(r.timestamp);
      if (at) {
        this.startedAt ??= at;
        if (!this.lastAt || at > this.lastAt) this.lastAt = at;
      }
      if (r.type === 'agent-setting') this.name = name(r.agentSetting) ?? this.name;
      if (r.type !== 'assistant' || !r.message || typeof r.message !== 'object') return;
      this.model = name(r.message.model) ?? this.model;
      const u = r.message.usage;
      const id = typeof r.message.id === 'string' ? r.message.id : r.requestId;
      if (u && typeof u === 'object' && typeof id === 'string') {
        usage.set(id, {
          input: count(u.input_tokens) ?? 0,
          output: count(u.output_tokens) ?? 0,
          cache: (count(u.cache_read_input_tokens) ?? 0) + (count(u.cache_creation_input_tokens) ?? 0),
        });
      }
    },
    tokens() {
      const t = { input: 0, output: 0, cache: 0, total: 0 };
      for (const u of usage.values()) {
        t.input += u.input;
        t.output += u.output;
        t.cache += u.cache;
      }
      t.total = t.input + t.output + t.cache;
      return t;
    },
  };
}

// The subagent's info file: only its type and whether it finished are read.
function subagentInfo(file) {
  try {
    const meta = JSON.parse(fs.readFileSync(file, 'utf8'));
    return { type: name(meta.agentType), finished: meta.finished === true };
  } catch {
    return { type: null, finished: false };
  }
}

// ---------- Codex ----------

const tokenTotal = (t) =>
  t && typeof t === 'object' ? { input: count(t.input), cachedInput: count(t.cachedInput), output: count(t.output), total: count(t.total) } : null;

// One launcher run, from the events its log records.
function codexRun(file) {
  return {
    file,
    tail: tailer(file),
    role: null,
    issue: null,
    pr: null,
    threadId: null,
    startedAt: null,
    endedAt: null,
    failed: false,
    tokens: {},
    helpers: new Map(),
    add(r) {
      const at = time(r.at);
      if (r.event === 'start') {
        this.startedAt = at;
        this.role = name(r.role);
        this.issue = count(r.issue);
        this.pr = count(r.pr);
      } else if (r.event === 'thread-started') {
        this.threadId = typeof r.threadId === 'string' ? r.threadId : null;
      } else if (r.event === 'token-usage' && typeof r.threadId === 'string') {
        this.tokens[r.threadId] = tokenTotal(r.total);
      } else if (['helper-started', 'helper-updated', 'helper-resumed', 'helper-ended'].includes(r.event) && typeof r.threadId === 'string') {
        const h = this.helpers.get(r.threadId) ?? { name: null, model: null, effort: null, startedAt: at, endedAt: null };
        this.helpers.set(r.threadId, h);
        if (r.event !== 'helper-ended') {
          h.name = name(r.name) ?? h.name;
          h.model = name(r.model) ?? h.model;
          h.effort = name(r.effort) ?? h.effort;
        }
        if (r.event === 'helper-resumed') h.endedAt = null;
        if (r.event === 'helper-ended') {
          h.endedAt = at;
          if (r.tokens) this.tokens[r.threadId] = tokenTotal(r.tokens);
        }
      } else if (r.event === 'end') {
        this.endedAt = at;
        this.failed = r.error !== null && r.error !== undefined;
      }
    },
  };
}

// ---------- the studio ----------

// Watches both sources. `poll` reads what changed; `state` is what the page shows.
function studio({ codexDir, claudeDir, now = () => Date.now(), activeMs = ACTIVE_MS }) {
  const started = now();
  const claude = new Map(); // file -> agent, once shown
  const codex = new Map(); // file -> run, once shown
  const waiting = new Map(); // file -> size, for logs not shown yet
  let polled = false;
  let keys = 0;
  const keyOf = new Map();
  const key = (id) => {
    if (!keyOf.has(id)) keyOf.set(id, ++keys);
    return keyOf.get(id);
  };

  // Live only. At the first poll a log is shown if what it belongs to is running then; a log
  // first seen later was written after the dashboard started. A log left out is shown once it
  // grows, because what it belongs to has started again.
  const shouldShow = (file, runningAtStart) => {
    const s = stat(file);
    if (!s) return false;
    if (!waiting.has(file)) {
      waiting.set(file, s.size);
      return polled ? s.mtimeMs >= started : runningAtStart(s);
    }
    return s.size !== waiting.get(file);
  };
  const showClaude = (kind, file, parent, info) => {
    const agent = claudeAgent(kind, file);
    Object.assign(agent, { parent, info, finished: false });
    claude.set(file, agent);
    waiting.delete(file);
    return agent;
  };

  const claudeFiles = () => {
    const files = [];
    for (const f of list(claudeDir)) {
      if (!f.endsWith('.jsonl')) continue;
      const session = path.join(claudeDir, f);
      files.push({ kind: 'session', file: session, parent: null });
      const subDir = path.join(claudeDir, f.slice(0, -'.jsonl'.length), 'subagents');
      for (const s of list(subDir)) {
        if (s.endsWith('.jsonl')) files.push({ kind: 'subagent', file: path.join(subDir, s), parent: session, info: path.join(subDir, `${s.slice(0, -'.jsonl'.length)}.meta.json`) });
      }
    }
    return files;
  };

  function poll() {
    const t = now();
    for (const { kind, file, parent, info } of claudeFiles()) {
      let agent = claude.get(file);
      if (!agent) {
        const recent = (s) => t - s.mtimeMs <= activeMs && (kind === 'session' || !subagentInfo(info).finished);
        if (!shouldShow(file, recent)) continue;
        agent = showClaude(kind, file, parent, info);
        // A running subagent's session is running too, even if it wrote nothing recently.
        if (parent && !claude.has(parent)) showClaude('session', parent, null, null);
      }
    }
    for (const agent of claude.values()) {
      for (const r of agent.tail.read()) agent.add(r);
      if (agent.kind === 'subagent') {
        const meta = subagentInfo(agent.info);
        agent.name = meta.type ?? agent.name;
        agent.finished = meta.finished;
      }
    }
    for (const f of list(codexDir)) {
      if (!f.endsWith('.jsonl')) continue;
      const file = path.join(codexDir, f);
      let run = codex.get(file);
      if (!run) {
        // A run is running at start if its log has no end and its launcher could still be alive.
        const open = (s) => {
          if (t - s.mtimeMs > RUN_LIMIT_MS) return false;
          const probe = codexRun(file);
          for (let batch = probe.tail.read(); batch.length; batch = probe.tail.read()) for (const r of batch) probe.add(r);
          return probe.endedAt === null;
        };
        if (!shouldShow(file, open)) continue;
        run = codexRun(file);
        codex.set(file, run);
        waiting.delete(file);
      }
      for (const r of run.tail.read()) run.add(r);
    }
    polled = true;
  }

  function state() {
    const t = now();
    const running = (a) => (a.kind === 'subagent' && a.finished ? false : a.lastAt !== null && t - Date.parse(a.lastAt) <= activeMs);
    const agents = [...claude.values()]
      .map((a) => ({
        key: key(a.file),
        kind: a.kind,
        parent: a.parent ? key(a.parent) : null,
        name: a.name ?? (a.kind === 'session' ? 'main session' : 'subagent'),
        model: a.model,
        startedAt: a.startedAt,
        lastActivityAt: a.lastAt,
        state: a.kind === 'subagent' && a.finished ? 'finished' : running(a) ? 'running' : 'idle',
        tokens: a.tokens(),
      }));
    const runs = [...codex.values()].map((r) => {
      const stopped = r.endedAt === null && r.startedAt !== null && t - Date.parse(r.startedAt) > RUN_LIMIT_MS;
      return {
        key: key(r.file),
        role: r.role,
        issue: r.issue,
        pr: r.pr,
        startedAt: r.startedAt,
        endedAt: r.endedAt,
        state: r.endedAt ? (r.failed ? 'failed' : 'finished') : stopped ? 'stopped' : 'running',
        tokens: (r.threadId && r.tokens[r.threadId]) || null,
        helpers: [...r.helpers.entries()].map(([thread, h]) => ({
          key: key(`${r.file}#${thread}`),
          name: h.name ?? 'helper',
          model: h.model,
          effort: h.effort,
          startedAt: h.startedAt,
          endedAt: h.endedAt,
          state: h.endedAt || r.endedAt ? 'finished' : 'running',
          tokens: r.tokens[thread] ?? null,
        })),
      };
    });
    return { now: new Date(t).toISOString(), since: new Date(started).toISOString(), claude: agents, codex: runs };
  }

  return { poll, state };
}

// ---------- the page ----------

const STYLE = `
:root{--bg:#f6f7f9;--panel:#fff;--text:#1d2330;--muted:#5d6678;--line:#e2e6ec;--run:#1f9d55;--idle:#b7791f;--done:#8a94a6;--fail:#d64545}
@media (prefers-color-scheme:dark){:root{--bg:#12151b;--panel:#1a1f27;--text:#e6e9ef;--muted:#98a2b3;--line:#2a313c;--run:#3ccf7e;--idle:#e0a43a;--done:#6b7587;--fail:#f06a6a}}
*{box-sizing:border-box}body{margin:0;background:var(--bg);color:var(--text);font:14px/1.45 system-ui,-apple-system,"Segoe UI",sans-serif}
main{max-width:1200px;margin:0 auto;padding:20px 16px 40px}
header{display:flex;flex-wrap:wrap;align-items:baseline;gap:8px 20px;margin-bottom:16px}
h1{font-size:20px;margin:0}h2{font-size:15px;margin:0 0 10px}
.muted{color:var(--muted)}.stats{display:flex;flex-wrap:wrap;gap:10px;margin-bottom:16px}
.stat{background:var(--panel);border:1px solid var(--line);border-radius:8px;padding:10px 14px;min-width:150px}
.stat b{display:block;font-size:20px;font-variant-numeric:tabular-nums}
section{background:var(--panel);border:1px solid var(--line);border-radius:8px;padding:14px;margin-bottom:16px;overflow-x:auto}
table{border-collapse:collapse;width:100%;min-width:760px}
th,td{text-align:left;padding:6px 8px;border-bottom:1px solid var(--line);white-space:nowrap}
th{font-weight:600;color:var(--muted);font-size:12px}td.n,th.n{text-align:right;font-variant-numeric:tabular-nums}
tr.child td:first-child{padding-left:28px}tr.child td:first-child::before{content:"└ ";color:var(--muted)}
.dot{display:inline-block;width:8px;height:8px;border-radius:50%;margin-right:6px;vertical-align:middle}
.running{background:var(--run)}.idle{background:var(--idle)}.finished,.stopped{background:var(--done)}.failed{background:var(--fail)}
.empty{color:var(--muted);padding:6px 0}#conn{font-size:12px}#conn.lost{color:var(--fail)}
`;

const SCRIPT = `
const $ = (tag, text, cls) => { const e = document.createElement(tag); if (text !== undefined && text !== null) e.textContent = String(text); if (cls) e.className = cls; return e; };
const num = (n) => (n === null || n === undefined ? '–' : Number(n).toLocaleString());
const clock = (s) => (s ? new Date(s).toLocaleTimeString() : '–');
const since = (from, to, now) => { if (!from) return '–'; const ms = Date.parse(to || now) - Date.parse(from); const m = Math.floor(ms / 60000); return m < 60 ? m + ' min' : Math.floor(m / 60) + ' h ' + (m % 60) + ' min'; };
const stateCell = (s) => { const td = $('td'); td.append($('span', null, 'dot ' + s), document.createTextNode(s)); return td; };
function row(cells, cls) { const tr = $('tr', null, cls); for (const c of cells) tr.append(c instanceof Node ? c : $('td', c)); return tr; }
const n = (v) => $('td', num(v), 'n');
function table(head, rows, empty) {
  if (!rows.length) return $('p', empty, 'empty');
  const t = $('table'), h = $('tr');
  for (const [label, numeric] of head) h.append($('th', label, numeric ? 'n' : ''));
  t.append(h, ...rows);
  return t;
}
function render(s) {
  const running = s.claude.filter((a) => a.state === 'running').length;
  const codexRunning = s.codex.filter((r) => r.state === 'running').length;
  const claudeTokens = s.claude.reduce((a, b) => a + b.tokens.total, 0);
  const codexTokens = s.codex.reduce((a, r) => a + (r.tokens?.total ?? 0) + r.helpers.reduce((x, h) => x + (h.tokens?.total ?? 0), 0), 0);
  const stats = document.getElementById('stats');
  stats.replaceChildren(...[['Claude running', running], ['Codex runs running', codexRunning], ['Claude tokens', claudeTokens], ['Codex tokens', codexTokens]].map(([l, v]) => { const d = $('div', null, 'stat'); d.append($('span', l, 'muted'), $('b', num(v))); return d; }));

  const sessions = s.claude.filter((a) => a.parent === null);
  const orphans = s.claude.filter((a) => a.parent !== null && !sessions.some((x) => x.key === a.parent));
  const claudeRows = [];
  const agentRow = (a, child) => row([a.name, stateCell(a.state), a.model ?? '–', clock(a.startedAt), clock(a.lastActivityAt), n(a.tokens.input), n(a.tokens.output), n(a.tokens.cache), n(a.tokens.total)], child ? 'child' : '');
  for (const sess of sessions) {
    claudeRows.push(agentRow(sess, false));
    for (const sub of s.claude.filter((a) => a.parent === sess.key)) claudeRows.push(agentRow(sub, true));
  }
  for (const o of orphans) claudeRows.push(agentRow(o, true));
  document.getElementById('claude').replaceChildren($('h2', 'Claude'), table([['Agent'], ['State'], ['Model'], ['Started'], ['Last activity'], ['Input', 1], ['Output', 1], ['Cache', 1], ['Total', 1]], claudeRows, 'No Claude session running. Sessions and subagents appear here when they start.'));

  const codexRows = [];
  for (const r of s.codex) {
    const title = (r.role ?? 'run') + (r.issue ? ' · Issue #' + r.issue : '') + (r.pr ? ' · PR #' + r.pr : '');
    codexRows.push(row([title, stateCell(r.state), '–', clock(r.startedAt), clock(r.endedAt), since(r.startedAt, r.endedAt, s.now), n(r.tokens?.input), n(r.tokens?.output), n(r.tokens?.total)], ''));
    for (const h of r.helpers) codexRows.push(row([h.name, stateCell(h.state), [h.model, h.effort].filter(Boolean).join(' / ') || '–', clock(h.startedAt), clock(h.endedAt), since(h.startedAt, h.endedAt || r.endedAt, s.now), n(h.tokens?.input), n(h.tokens?.output), n(h.tokens?.total)], 'child'));
  }
  document.getElementById('codex').replaceChildren($('h2', 'Codex'), table([['Run / helper agent'], ['State'], ['Model / effort'], ['Started'], ['Ended'], ['Duration'], ['Input', 1], ['Output', 1], ['Total', 1]], codexRows, 'No Codex run yet. Launcher runs and their helper agents appear here when they start.'));
  document.getElementById('since').textContent = 'Showing agents running since ' + clock(s.since);
}
async function refresh() {
  const conn = document.getElementById('conn');
  try {
    const res = await fetch('/state', { cache: 'no-store' });
    if (!res.ok) throw new Error(res.status);
    render(await res.json());
    conn.textContent = 'Updated ' + new Date().toLocaleTimeString();
    conn.className = '';
  } catch {
    conn.textContent = 'Dashboard not reachable: is it still running?';
    conn.className = 'lost';
  }
}
refresh();
setInterval(refresh, 2000);
`;

const PAGE = `<!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>Studio live</title><style>${STYLE}</style></head>
<body><main><header><h1>Studio live</h1><span id="since" class="muted"></span><span id="conn" class="muted"></span></header>
<div id="stats" class="stats"></div><section id="claude"></section><section id="codex"></section></main><script>${SCRIPT}</script></body></html>`;

const hash = (s) => `'sha256-${crypto.createHash('sha256').update(s).digest('base64')}'`;
const CSP = `default-src 'none'; script-src ${hash(SCRIPT)}; style-src ${hash(STYLE)}; connect-src 'self'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'`;

// ---------- the server ----------

// Serves the page and the state, to this PC only. The Host check stops other sites from reaching
// it through DNS rebinding.
function server(watch) {
  return http.createServer((req, res) => {
    const send = (status, type, body) => {
      res.writeHead(status, { 'Content-Type': type, 'Cache-Control': 'no-store', 'X-Content-Type-Options': 'nosniff', 'Referrer-Policy': 'no-referrer', 'Content-Security-Policy': CSP });
      res.end(req.method === 'HEAD' ? undefined : body);
    };
    const port = req.socket.localPort;
    if (req.headers.host !== `127.0.0.1:${port}` && req.headers.host !== `localhost:${port}`) return send(403, 'text/plain; charset=utf-8', 'Forbidden');
    if (req.method !== 'GET' && req.method !== 'HEAD') return send(405, 'text/plain; charset=utf-8', 'Method not allowed');
    if (req.url === '/') return send(200, 'text/html; charset=utf-8', PAGE);
    if (req.url === '/state') return send(200, 'application/json; charset=utf-8', JSON.stringify(watch.state()));
    return send(404, 'text/plain; charset=utf-8', 'Not found');
  });
}

// ---------- main ----------

// Listens on the loopback address only; `host` comes from parseArgs, which allows nothing else.
function start(watch, { port, host }, onListening) {
  if (host !== '127.0.0.1') throw new Refused('the dashboard listens only on 127.0.0.1');
  return server(watch).listen(port, host, onListening);
}

function main(argv) {
  const opts = parseArgs(argv);
  const watch = studio({ codexDir: path.join(ROOT, '.studio', 'logs'), claudeDir: claudeProjectDir() });
  watch.poll();
  setInterval(() => {
    try {
      watch.poll();
    } catch (err) {
      process.stderr.write(`Dashboard read error: ${err.message}\n`);
    }
  }, POLL_MS);
  const srv = start(watch, opts, () => process.stdout.write(`Studio dashboard: http://127.0.0.1:${opts.port}/  (this PC only; Ctrl+C to stop)\n`));
  srv.on('error', (err) => {
    process.stderr.write(`The dashboard could not start: ${err.code === 'EADDRINUSE' ? `port ${opts.port} is in use; try --port <another>` : err.message}\n`);
    process.exit(1);
  });
}

if (require.main === module) {
  try {
    main(process.argv.slice(2));
  } catch (err) {
    process.stderr.write(`${err instanceof Refused ? err.message : err.stack}\n`);
    process.exit(1);
  }
}

module.exports = { Refused, parseArgs, claudeProjectDir, tailer, studio, server, start, PAGE, ACTIVE_MS };
