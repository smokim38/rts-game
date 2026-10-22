#!/usr/bin/env node
'use strict';
// The studio's live dashboard (Issue #47): one local page showing the Claude sessions and
// subagents and the Codex launcher runs and helper agents that are running, with their tokens.
//
//   node tools/studio-dashboard.js [--port 4317]
//
// It listens on 127.0.0.1 only and refuses any other address. It is read-only and reads only:
// - .studio/logs/*.jsonl, the launcher's run logs (the entries #46 added)
// - .studio/sessions/*.jsonl, the start and end record of each Claude session (#50)
// - this repo's Claude Code session logs, in the user's .claude/projects/<repo folder>/: the
//   session logs and each session's subagents/ logs and info files
// A file is read only if its real path, after links and junctions, is inside its folder. Beyond
// that, it only checks whether a recorded Claude process id is still running.
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
// A launcher run lasts at most 4 hours; a log with no end after that belongs to a stopped launcher.
const RUN_LIMIT_MS = 4 * 60 * 60 * 1000 + 10 * 60 * 1000;
const POLL_MS = 1000;
// Large session logs are read in steps, so one poll never holds more than this.
const READ_CHUNK = 8 * 1024 * 1024;
// An unfinished line longer than this is dropped, so a line that never ends can't fill memory.
const MAX_LINE = 16 * 1024 * 1024;
// Session ids as #50's session-marker writes them, which are also the Claude log file names.
const SESSION_ID = /^[A-Za-z0-9_-]{1,128}$/;

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

// Whether `p`, after links and junctions, is `root` or inside it, so a link can't lead a read
// out of the folders the dashboard may read.
function inside(root, p) {
  try {
    const base = fs.realpathSync.native(root);
    const real = fs.realpathSync.native(p);
    return real === base || real.startsWith(base.endsWith(path.sep) ? base : `${base}${path.sep}`);
  } catch {
    return false;
  }
}
const stat = (root, file) => {
  if (!inside(root, file)) return null;
  try {
    return fs.statSync(file);
  } catch {
    return null;
  }
};
const list = (root, dir) => {
  if (!inside(root, dir)) return [];
  try {
    return fs.readdirSync(dir);
  } catch {
    return [];
  }
};

// Whether an allowed folder really is at its expected place under `base` (the repository or the
// user folder): its real path must be the base's real path plus the same names, so no folder from
// the base down is a link or junction that leads elsewhere.
function expected(base, dir) {
  const rel = path.relative(base, dir);
  if (!rel || rel.startsWith('..') || path.isAbsolute(rel)) return false;
  try {
    const want = path.join(fs.realpathSync.native(base), rel);
    const real = fs.realpathSync.native(dir);
    return process.platform === 'win32' ? real.toLowerCase() === want.toLowerCase() : real === want;
  } catch {
    return false;
  }
}

// Whether a process with this id is running. Signal 0 only checks that it exists.
function pidAlive(pid) {
  if (!Number.isSafeInteger(pid) || pid <= 0) return false;
  try {
    process.kill(pid, 0);
    return true;
  } catch (err) {
    return err.code === 'EPERM';
  }
}

// Reads a growing JSON-lines file inside `root` from where it last stopped. A partly written last
// line waits for the next read; malformed lines are skipped. `atEnd` says whether the last read
// reached the end of the file, whether or not it found any records.
function tailer(file, root = path.dirname(file), { chunk = READ_CHUNK, maxLine = MAX_LINE } = {}) {
  let offset = 0;
  let size = 0;
  let rest = '';
  let skipping = false;
  let decoder = new StringDecoder('utf8');
  return {
    get atEnd() {
      return offset >= size;
    },
    // Leaves what the file holds now unread; later reads return only lines added after this.
    skipToEnd() {
      size = stat(root, file)?.size ?? 0;
      offset = size;
      rest = '';
      skipping = false;
      decoder = new StringDecoder('utf8');
    },
    read() {
      size = stat(root, file)?.size ?? 0;
      if (size < offset) {
        offset = 0;
        rest = '';
        skipping = false;
        decoder = new StringDecoder('utf8');
      }
      if (size === offset) return [];
      const buf = Buffer.alloc(Math.min(size - offset, chunk));
      const fd = fs.openSync(file, 'r');
      let n;
      try {
        n = fs.readSync(fd, buf, 0, buf.length, offset);
      } finally {
        fs.closeSync(fd);
      }
      offset += n;
      let text = decoder.write(buf.subarray(0, n));
      if (skipping) {
        const newline = text.indexOf('\n');
        if (newline === -1) return [];
        text = text.slice(newline + 1);
        skipping = false;
      }
      const lines = (rest + text).split('\n');
      rest = lines.pop();
      if (rest.length > maxLine) {
        rest = '';
        skipping = true;
      }
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
// Only messages (user and assistant records, tool results included) count as activity, because
// other lines can be added after an agent has ended. `add` returns an activity's time.
function claudeAgent(kind, file, root) {
  const usage = new Map();
  return {
    kind,
    file,
    tail: tailer(file, root),
    name: null,
    model: null,
    startedAt: null,
    lastAt: null,
    usage,
    add(r) {
      const at = time(r.timestamp);
      if (at) this.startedAt ??= at;
      const activity = at !== null && (r.type === 'user' || r.type === 'assistant') ? at : null;
      if (activity && (!this.lastAt || activity > this.lastAt)) this.lastAt = activity;
      if (r.type === 'agent-setting') this.name = name(r.agentSetting) ?? this.name;
      if (r.type === 'assistant' && r.message && typeof r.message === 'object') {
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
      }
      return activity;
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
function subagentInfo(root, file) {
  if (!inside(root, file)) return { type: null, finished: false };
  try {
    const meta = JSON.parse(fs.readFileSync(file, 'utf8'));
    return { type: name(meta.agentType), finished: meta.finished === true };
  } catch {
    return { type: null, finished: false };
  }
}

// One Claude session's start and end records (#50). It is running from a start record until an
// end record, or until its process is gone; a start record with a new process id begins a new run.
// `run` counts the runs, so a resumed session can be told apart from its earlier runs.
function claudeSession(id, file, root) {
  return {
    id,
    tail: tailer(file, root),
    agent: null,
    pid: null,
    open: false,
    gone: false,
    run: 0,
    startedAt: null,
    get running() {
      return this.open && !this.gone;
    },
    // Returns true for a start record.
    add(r) {
      if (r.session !== id) return false;
      const at = time(r.at);
      if (r.event === 'start') {
        const pid = Number.isSafeInteger(r.pid) && r.pid > 0 ? r.pid : null;
        if (!this.open || pid !== this.pid) {
          this.run += 1;
          this.startedAt = at;
          this.gone = false;
        }
        this.open = true;
        this.pid = pid;
        this.agent = name(r.agent) ?? this.agent;
        return true;
      }
      if (r.event === 'end') this.open = false;
      return false;
    },
  };
}

// ---------- Codex ----------

const tokenTotal = (t) =>
  t && typeof t === 'object' ? { input: count(t.input), cachedInput: count(t.cachedInput), output: count(t.output), total: count(t.total) } : null;

// One launcher run, from the events its log records.
function codexRun(file, root) {
  return {
    file,
    tail: tailer(file, root),
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
        const h = this.helpers.get(r.threadId) ?? { name: null, model: null, effort: null, startedAt: at, ended: false, endedAt: null };
        this.helpers.set(r.threadId, h);
        if (r.event !== 'helper-ended') {
          h.name = name(r.name) ?? h.name;
          h.model = name(r.model) ?? h.model;
          h.effort = name(r.effort) ?? h.effort;
        }
        // Only a resume brings a finished helper back; later updates or token reports don't.
        if (r.event === 'helper-resumed') {
          h.ended = false;
          h.endedAt = null;
        }
        if (r.event === 'helper-ended') {
          if (!h.ended) h.endedAt = at;
          h.ended = true;
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

// Watches all sources. `poll` reads what changed; `state` is what the page shows.
function studio({ codexDir, claudeDir, sessionsDir, repoDir = ROOT, homeDir = os.homedir(), now = () => Date.now(), alive = pidAlive }) {
  const started = now();
  const sessions = new Map(); // session id -> its start and end records, for every records file
  const subagents = new Map(); // file -> subagent of a shown session, shown or not
  const runs = new Map(); // file -> run, for every launcher log
  const claude = new Map(); // file -> session or subagent, once shown
  const codex = new Map(); // file -> run, once shown
  let polled = false;
  let keys = 0;
  const keyOf = new Map();
  const key = (id) => {
    if (!keyOf.has(id)) keyOf.set(id, ++keys);
    return keyOf.get(id);
  };

  // Live only, everywhere the dashboard decides what to show: an agent is shown if it is running
  // at the first poll, or later on a real new start or real new activity. A line added after an
  // agent ended (a posted record, metadata) never shows it.
  const claudeOf = (kind, file, extra) => Object.assign(claudeAgent(kind, file, claudeDir), { parent: null, info: null, session: null, finished: false }, extra);

  // A session is shown if it is running at the first poll, or when a start record arrives later,
  // even if it has already ended by then.
  function pollSessions() {
    for (const f of list(sessionsDir, sessionsDir)) {
      const id = f.endsWith('.jsonl') ? f.slice(0, -'.jsonl'.length) : '';
      if (!SESSION_ID.test(id)) continue;
      let session = sessions.get(id);
      if (!session) sessions.set(id, (session = claudeSession(id, path.join(sessionsDir, f), sessionsDir)));
      let startedNow = false;
      do {
        for (const r of session.tail.read()) startedNow = session.add(r) || startedNow;
      } while (!polled && !session.tail.atEnd);
      // Once its process is gone the session stays finished, even if the id is reused later.
      if (session.open && !session.gone && !alive(session.pid)) session.gone = true;
      const file = path.join(claudeDir, `${id}.jsonl`);
      if (!claude.has(file) && (polled ? startedNow : session.running)) claude.set(file, claudeOf('session', file, { session }));
    }
  }

  // A shown session's subagents: at the first poll those not finished that were written during the
  // session's current run. After that, a subagent not shown yet is shown once it records activity
  // timed after the dashboard started and within the session's current run.
  function pollSubagents() {
    for (const parent of [...claude.values()].filter((a) => a.kind === 'session')) {
      const { session } = parent;
      const runStart = session.startedAt === null ? -Infinity : Date.parse(session.startedAt);
      const subDir = path.join(claudeDir, session.id, 'subagents');
      for (const s of list(claudeDir, subDir)) {
        if (!s.endsWith('.jsonl')) continue;
        const file = path.join(subDir, s);
        if (claude.has(file)) continue;
        let agent = subagents.get(file);
        if (!agent) {
          const info = path.join(subDir, `${s.slice(0, -'.jsonl'.length)}.meta.json`);
          subagents.set(file, (agent = claudeOf('subagent', file, { parent: parent.file, info, session, shownRun: session.run })));
          const st = polled ? null : stat(claudeDir, file);
          if (st && st.mtimeMs >= runStart && !subagentInfo(claudeDir, info).finished) {
            claude.set(file, agent);
            continue;
          }
        }
        const since = Math.max(started, runStart);
        let active = false;
        for (const r of agent.tail.read()) {
          const at = agent.add(r);
          if (at && Date.parse(at) >= since) active = true;
        }
        if (active) {
          agent.shownRun = session.run;
          claude.set(file, agent);
        }
      }
    }
  }

  // A run is shown if its log has a start and no end at the first poll, or when a start arrives
  // later. At the first poll, a log untouched for longer than a run can last belongs to a finished
  // or stopped run, so only lines added later are read from it.
  function pollRuns(t) {
    for (const f of list(codexDir, codexDir)) {
      if (!f.endsWith('.jsonl')) continue;
      const file = path.join(codexDir, f);
      let run = runs.get(file);
      if (!run) {
        runs.set(file, (run = codexRun(file, codexDir)));
        const s = polled ? null : stat(codexDir, file);
        if (s && t - s.mtimeMs > RUN_LIMIT_MS) run.tail.skipToEnd();
      }
      // The first poll reads to the end of the file, past any chunk with no complete record.
      let startedNow = false;
      do {
        for (const r of run.tail.read()) {
          if (r.event === 'start') startedNow = true;
          run.add(r);
        }
      } while (!polled && !run.tail.atEnd);
      if (!codex.has(file) && (polled ? startedNow : run.startedAt !== null && run.endedAt === null)) codex.set(file, run);
    }
  }

  function poll() {
    const t = now();
    // An allowed folder that leads elsewhere isn't read. It is checked on every poll, so a folder
    // replaced by a link later is refused too.
    if (expected(repoDir, sessionsDir)) pollSessions();
    if (expected(homeDir, claudeDir)) {
      pollSubagents();
      for (const agent of claude.values()) {
        for (const r of agent.tail.read()) agent.add(r);
        if (agent.kind === 'subagent') {
          const meta = subagentInfo(claudeDir, agent.info);
          agent.name = meta.type ?? agent.name;
          agent.finished = meta.finished;
        }
      }
    }
    if (expected(repoDir, codexDir)) pollRuns(t);
    polled = true;
  }

  // A subagent belongs to its session's current run only if its latest activity is from that run
  // (or, with no activity yet, it appeared during that run). So when a session is resumed, the
  // subagents of its earlier runs stay finished until they really do something new.
  const current = (a) => {
    if (a.kind === 'session') return true;
    if (a.lastAt === null) return a.shownRun === a.session.run;
    return a.session.startedAt === null || a.lastAt >= a.session.startedAt;
  };

  function state() {
    const t = now();
    const agents = [...claude.values()]
      .map((a) => ({
        key: key(a.file),
        kind: a.kind,
        parent: a.parent ? key(a.parent) : null,
        name: (a.kind === 'session' ? a.session.agent : null) ?? a.name ?? (a.kind === 'session' ? 'main session' : 'subagent'),
        model: a.model,
        startedAt: (a.kind === 'session' ? a.session.startedAt : null) ?? a.startedAt,
        lastActivityAt: a.lastAt,
        // A subagent can't outlive its session, or the run it belonged to.
        state: a.session.running && !a.finished && current(a) ? 'running' : 'finished',
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
        // Live only: a helper that finished before the dashboard started is left out unless it resumes.
        // An end with no readable time counts as before the start.
        helpers: [...r.helpers.entries()].filter(([, h]) => !(h.ended && (h.endedAt === null || Date.parse(h.endedAt) < started))).map(([thread, h]) => ({
          key: key(`${r.file}#${thread}`),
          name: h.name ?? 'helper',
          model: h.model,
          effort: h.effort,
          startedAt: h.startedAt,
          endedAt: h.endedAt,
          state: h.ended || r.endedAt ? 'finished' : 'running',
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
:root{--bg:#f6f7f9;--panel:#fff;--text:#1d2330;--muted:#5d6678;--line:#e2e6ec;--run:#1f9d55;--done:#8a94a6;--fail:#d64545}
@media (prefers-color-scheme:dark){:root{--bg:#12151b;--panel:#1a1f27;--text:#e6e9ef;--muted:#98a2b3;--line:#2a313c;--run:#3ccf7e;--done:#6b7587;--fail:#f06a6a}}
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
.running{background:var(--run)}.finished,.stopped{background:var(--done)}.failed{background:var(--fail)}
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
  const claudeRows = [];
  const agentRow = (a, child) => row([a.name, stateCell(a.state), a.model ?? '–', clock(a.startedAt), clock(a.lastActivityAt), n(a.tokens.input), n(a.tokens.output), n(a.tokens.cache), n(a.tokens.total)], child ? 'child' : '');
  for (const sess of sessions) {
    claudeRows.push(agentRow(sess, false));
    for (const sub of s.claude.filter((a) => a.parent === sess.key)) claudeRows.push(agentRow(sub, true));
  }
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
  const watch = studio({ codexDir: path.join(ROOT, '.studio', 'logs'), sessionsDir: path.join(ROOT, '.studio', 'sessions'), claudeDir: claudeProjectDir() });
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

module.exports = { Refused, parseArgs, claudeProjectDir, tailer, pidAlive, studio, server, start, PAGE };
