'use strict';
// Tests for the studio dashboard (Issue #47). Every log here is made up; nothing reads the real
// Claude or launcher logs. Run: node --test tools/studio-dashboard.test.js

const test = require('node:test');
const assert = require('node:assert');
const fs = require('node:fs');
const http = require('node:http');
const os = require('node:os');
const path = require('node:path');
const D = require('./studio-dashboard.js');

const refused = (fn, pattern) => assert.throws(fn, (err) => err instanceof D.Refused && pattern.test(err.message));
// Text that stands for conversation content, prompts, tool input and output, commands and paths.
// None of it may ever reach the page.
const PLANTED = 'PLANTED';

const jsonl = (records) => records.map((r) => `${JSON.stringify(r)}\n`).join('');
const ago = (ms) => new Date(Date.now() - ms);
const setTime = (file, when) => fs.utimesSync(file, when, when);

function folders() {
  const base = fs.mkdtempSync(path.join(os.tmpdir(), 'dashboard-test-'));
  const claudeDir = path.join(base, 'claude');
  const codexDir = path.join(base, 'logs');
  fs.mkdirSync(claudeDir);
  fs.mkdirSync(codexDir);
  return { base, claudeDir, codexDir, done: () => fs.rmSync(base, { recursive: true, force: true }) };
}

// A Claude reply as Claude Code writes it: one record per content block, the same message id and
// usage on each.
const reply = (id, usage, at = new Date().toISOString()) =>
  [
    { type: 'thinking', thinking: `${PLANTED}-THINKING` },
    { type: 'text', text: `${PLANTED}-REPLY` },
    { type: 'tool_use', id: 'toolu_1', name: 'Bash', input: { command: `${PLANTED}-COMMAND ${PLANTED}/Assets/X.cs` } },
  ].map((block) => ({
    type: 'assistant',
    timestamp: at,
    requestId: `req-${id}`,
    message: { id, model: 'claude-opus-5-5', role: 'assistant', content: [block], usage },
  }));
const usage = (input, output, read, write) => ({ input_tokens: input, output_tokens: output, cache_read_input_tokens: read, cache_creation_input_tokens: write, service_tier: 'standard' });
const prompt = (at = new Date().toISOString()) => ({ type: 'user', timestamp: at, message: { role: 'user', content: `${PLANTED}-PROMPT` } });

test('arguments: a port, and only the loopback address', () => {
  assert.deepStrictEqual(D.parseArgs([]), { port: 4317, host: '127.0.0.1' });
  assert.deepStrictEqual(D.parseArgs(['--port', '5000']), { port: 5000, host: '127.0.0.1' });
  assert.deepStrictEqual(D.parseArgs(['--port=5001', '--host', 'localhost']), { port: 5001, host: '127.0.0.1' });
  for (const host of ['0.0.0.0', '192.168.1.5', '::', 'example.com', '']) refused(() => D.parseArgs(['--host', host]), /listens only on 127\.0\.0\.1/);
  for (const bad of [['--port', '0'], ['--port', '70000'], ['--port', 'x'], ['--port'], ['--open']]) refused(() => D.parseArgs(bad), /usage/);
  refused(() => D.start({ state: () => ({}) }, { port: 0, host: '0.0.0.0' }), /only on 127\.0\.0\.1/);
});

test("the Claude folder is this repo's, named the way Claude Code names it", () => {
  const home = path.join(os.tmpdir(), 'home');
  const dir = D.claudeProjectDir(path.join(os.tmpdir(), 'dev', 'rts-game'), home);
  assert.strictEqual(path.dirname(dir), path.join(home, '.claude', 'projects'));
  assert.match(path.basename(dir), /^[A-Za-z0-9-]+-dev-rts-game$/);
  if (process.platform === 'win32') assert.strictEqual(path.basename(D.claudeProjectDir('C:\\dev\\rts-game', home)), 'C--dev-rts-game');
});

test('the tailer reads only complete lines and skips malformed ones', () => {
  const f = folders();
  try {
    const file = path.join(f.base, 'x.jsonl');
    fs.writeFileSync(file, '{"a":1}\nnot json\n{"b":');
    const tail = D.tailer(file);
    assert.deepStrictEqual(tail.read(), [{ a: 1 }]);
    assert.deepStrictEqual(tail.read(), []);
    fs.appendFileSync(file, '2}\n{"c":"é"}\n');
    assert.deepStrictEqual(tail.read(), [{ b: 2 }, { c: 'é' }]);
    fs.writeFileSync(file, '{"d":4}\n'); // replaced by a shorter file
    assert.deepStrictEqual(tail.read(), [{ d: 4 }]);
  } finally {
    f.done();
  }
});

test('Claude: live sessions and subagents with their tokens, counted once per reply', () => {
  const f = folders();
  try {
    const at = (name) => path.join(f.claudeDir, name);
    // A Producer session running now, with a running subagent and one that finished earlier.
    fs.writeFileSync(at('live.jsonl'), jsonl([
      { type: 'agent-setting', agentSetting: 'producer', sessionId: 'live' },
      { type: 'ai-title', aiTitle: `${PLANTED}-TITLE` },
      { type: 'agent-name', agentName: `${PLANTED}-NAME` },
      prompt(),
      ...reply('m1', usage(10, 5, 100, 20)),
      ...reply('m2', usage(1, 1, 0, 0)),
    ]));
    const subs = at(path.join('live', 'subagents'));
    fs.mkdirSync(subs, { recursive: true });
    fs.writeFileSync(path.join(subs, 'agent-a1.jsonl'), jsonl([prompt(), ...reply('s1', usage(3, 2, 1, 0))]));
    fs.writeFileSync(path.join(subs, 'agent-a1.meta.json'), JSON.stringify({ agentType: 'game-designer', finished: false, description: `${PLANTED}-TASK` }));
    fs.writeFileSync(path.join(subs, 'agent-a0.jsonl'), jsonl([...reply('s0', usage(9, 9, 9, 9))]));
    fs.writeFileSync(path.join(subs, 'agent-a0.meta.json'), JSON.stringify({ agentType: 'tech-architect', finished: true }));
    // A session that last wrote an hour ago: ended before the dashboard started.
    fs.writeFileSync(at('old.jsonl'), jsonl([prompt(ago(3600e3).toISOString()), ...reply('o1', usage(7, 7, 0, 0), ago(3600e3).toISOString())]));
    setTime(at('old.jsonl'), ago(3600e3));

    const watch = D.studio({ claudeDir: f.claudeDir, codexDir: f.codexDir });
    watch.poll();
    let s = watch.state();
    assert.deepStrictEqual(s.claude.map((a) => [a.kind, a.name, a.state]), [['session', 'producer', 'running'], ['subagent', 'game-designer', 'running']]);
    const [producer, designer] = s.claude;
    assert.deepStrictEqual(producer.tokens, { input: 11, output: 6, cache: 120, total: 137 });
    assert.strictEqual(producer.model, 'claude-opus-5-5');
    assert.strictEqual(designer.parent, producer.key);
    assert.deepStrictEqual(designer.tokens, { input: 3, output: 2, cache: 1, total: 6 });

    // A reply still being written counts only once it is complete; a malformed line is skipped.
    const [first, ...more] = reply('m3', usage(100, 0, 0, 0));
    const line = JSON.stringify(first);
    fs.appendFileSync(at('live.jsonl'), `not json\n${line.slice(0, 40)}`);
    watch.poll();
    assert.strictEqual(watch.state().claude[0].tokens.input, 11);
    fs.appendFileSync(at('live.jsonl'), `${line.slice(40)}\n${jsonl(more)}`);
    watch.poll();
    assert.strictEqual(watch.state().claude[0].tokens.input, 111);

    // The old session starts again, and a new session starts: both appear.
    fs.appendFileSync(at('old.jsonl'), jsonl([prompt()]));
    fs.writeFileSync(at('new.jsonl'), jsonl([prompt()]));
    // The subagent finishes.
    fs.writeFileSync(path.join(subs, 'agent-a1.meta.json'), JSON.stringify({ agentType: 'game-designer', finished: true }));
    watch.poll();
    s = watch.state();
    assert.deepStrictEqual(s.claude.map((a) => [a.name, a.state]), [['producer', 'running'], ['game-designer', 'finished'], ['main session', 'running'], ['main session', 'running']]);
    assert.ok(!JSON.stringify(s).includes(PLANTED));
  } finally {
    f.done();
  }
});

test('Claude: a running subagent brings its quiet session, and sessions go idle', () => {
  const f = folders();
  try {
    let clock = Date.now();
    const quiet = path.join(f.claudeDir, 'quiet.jsonl');
    fs.writeFileSync(quiet, jsonl([prompt(ago(3600e3).toISOString())]));
    setTime(quiet, ago(3600e3));
    const subs = path.join(f.claudeDir, 'quiet', 'subagents');
    fs.mkdirSync(subs, { recursive: true });
    fs.writeFileSync(path.join(subs, 'agent-b1.jsonl'), jsonl([prompt()]));
    fs.writeFileSync(path.join(subs, 'agent-b1.meta.json'), JSON.stringify({ agentType: 'code-reviewer', finished: false }));
    const watch = D.studio({ claudeDir: f.claudeDir, codexDir: f.codexDir, now: () => clock });
    watch.poll();
    assert.deepStrictEqual(watch.state().claude.map((a) => [a.name, a.state]), [['code-reviewer', 'running'], ['main session', 'idle']]);
    clock += D.ACTIVE_MS + 1000;
    assert.deepStrictEqual(watch.state().claude.map((a) => a.state), ['idle', 'idle']);
  } finally {
    f.done();
  }
});

test('Codex: live launcher runs and their helper agents, from the launcher log entries', () => {
  const f = folders();
  try {
    const log = (name, events, when) => {
      const file = path.join(f.codexDir, name);
      fs.writeFileSync(file, jsonl(events));
      if (when) setTime(file, when);
      return file;
    };
    const base = Date.now();
    const t = (s) => new Date(base - s * 1000).toISOString();
    const tokens = (total) => ({ input: total - 10, cachedInput: 5, cacheWrite: 0, output: 10, reasoningOutput: 2, total });
    log('ended.jsonl', [{ at: t(600), event: 'start', role: 'tech-artist', issue: 9 }, { at: t(500), event: 'end', error: null }]);
    log('stale.jsonl', [{ at: t(6 * 3600), event: 'start', role: 'qa-test-engineer', issue: 8, pr: 7 }], ago(6 * 3600e3));
    const open = log('open.jsonl', [
      { at: t(120), event: 'start', role: 'reviewer', issue: 46, pr: 48, sha: 'a'.repeat(40), branch: `${PLANTED}-branch` },
      { at: t(119), event: 'thread-started', threadId: 't1', model: 'gpt-6.1-sol', effort: 'high' },
      { at: t(100), event: 'command', command: `${PLANTED}-COMMAND`, exitCode: 0 },
      { at: t(99), event: 'file-change', files: [`${PLANTED}/Assets/X.cs`] },
      { at: t(98), event: 'auto-review', status: 'approved', rationale: `${PLANTED}-RATIONALE` },
      { at: t(90), event: 'helper-started', threadId: 'h1', name: null, model: 'gpt-mini', effort: 'low', status: null },
      { at: t(89), event: 'helper-updated', threadId: 'h1', name: 'root/worker', model: 'gpt-mini-r', effort: 'high', status: null },
      { at: t(80), event: 'token-usage', threadId: 'h1', helper: true, total: tokens(120), last: null, contextWindow: 1000 },
      { at: t(70), event: 'helper-ended', threadId: 'h1', status: 'completed', tokens: tokens(120) },
      { at: t(60), event: 'helper-started', threadId: 'h2', name: null, model: null, effort: null, status: null },
      { at: t(50), event: 'token-usage', threadId: 't1', helper: false, total: tokens(1200), last: null, contextWindow: null },
    ]);
    const watch = D.studio({ claudeDir: f.claudeDir, codexDir: f.codexDir });
    watch.poll();
    let [run, ...others] = watch.state().codex;
    assert.deepStrictEqual(others, []);
    assert.deepStrictEqual([run.role, run.issue, run.pr, run.state, run.endedAt], ['reviewer', 46, 48, 'running', null]);
    assert.deepStrictEqual(run.tokens, { input: 1190, cachedInput: 5, output: 10, total: 1200 });
    assert.deepStrictEqual(run.helpers.map((h) => [h.name, h.model, h.effort, h.state, h.tokens?.total ?? null]), [['root/worker', 'gpt-mini-r', 'high', 'finished', 120], ['helper', null, null, 'running', null]]);
    assert.deepStrictEqual([run.startedAt, run.helpers[0].startedAt, run.helpers[0].endedAt], [t(120), t(90), t(70)]);

    // The run ends, and a new run starts.
    fs.appendFileSync(open, jsonl([{ at: t(0), event: 'helper-ended', threadId: 'h2', status: 'run-ended', tokens: null }, { at: t(0), event: 'end', error: null }]));
    log('next.jsonl', [{ at: t(0), event: 'start', role: 'gameplay-programmer', issue: 50 }]);
    watch.poll();
    const s = watch.state();
    [run] = s.codex;
    assert.deepStrictEqual([run.state, run.helpers[1].state], ['finished', 'finished']);
    assert.ok(run.endedAt);
    assert.deepStrictEqual(s.codex.map((r) => [r.role, r.issue, r.state]), [['reviewer', 46, 'finished'], ['gameplay-programmer', 50, 'running']]);
    assert.ok(!JSON.stringify(s).includes(PLANTED));
  } finally {
    f.done();
  }
});

test('read-only: polling writes, changes and deletes nothing', () => {
  const f = folders();
  const names = ['writeFileSync', 'appendFileSync', 'mkdirSync', 'rmSync', 'unlinkSync', 'renameSync', 'copyFileSync', 'truncateSync', 'utimesSync', 'writeSync', 'writeFile', 'appendFile'];
  const saved = Object.fromEntries([...names, 'openSync'].map((n) => [n, fs[n]]));
  try {
    fs.writeFileSync(path.join(f.claudeDir, 's.jsonl'), jsonl([prompt(), ...reply('m1', usage(1, 1, 1, 1))]));
    fs.writeFileSync(path.join(f.codexDir, 'r.jsonl'), jsonl([{ at: new Date().toISOString(), event: 'start', role: 'reviewer', issue: 1, pr: 2 }]));
    for (const n of names) fs[n] = () => assert.fail(`the dashboard called fs.${n}`);
    fs.openSync = (file, flags, ...rest) => (flags === 'r' ? saved.openSync(file, flags, ...rest) : assert.fail(`the dashboard opened ${file} with ${flags}`));
    const watch = D.studio({ claudeDir: f.claudeDir, codexDir: f.codexDir });
    watch.poll();
    watch.poll();
    assert.strictEqual(watch.state().claude.length, 1);
    assert.strictEqual(watch.state().codex.length, 1);
  } finally {
    Object.assign(fs, saved);
    f.done();
  }
});

test('the server: this PC only, GET only, and only names, times, states and numbers', async () => {
  const f = folders();
  fs.writeFileSync(path.join(f.claudeDir, 's.jsonl'), jsonl([{ type: 'agent-setting', agentSetting: 'producer' }, prompt(), ...reply('m1', usage(1, 2, 3, 4))]));
  const watch = D.studio({ claudeDir: f.claudeDir, codexDir: f.codexDir });
  watch.poll();
  const srv = D.start(watch, { port: 0, host: '127.0.0.1' });
  await new Promise((r) => srv.once('listening', r));
  const { address, port } = srv.address();
  const request = (method, url, host = `127.0.0.1:${port}`) =>
    new Promise((resolve, reject) => {
      const req = http.request({ host: '127.0.0.1', port, method, path: url, headers: { host } }, (res) => {
        let body = '';
        res.on('data', (d) => (body += d));
        res.on('end', () => resolve({ status: res.statusCode, headers: res.headers, body }));
      });
      req.on('error', reject);
      req.end();
    });
  try {
    assert.strictEqual(address, '127.0.0.1');
    const page = await request('GET', '/');
    assert.strictEqual(page.status, 200);
    assert.match(page.headers['content-security-policy'], /default-src 'none'; script-src 'sha256-/);
    assert.ok(!/<script[^>]+src=|<link|@import|https?:\/\//i.test(page.body), 'the page loads nothing from elsewhere');
    const state = await request('GET', '/state', `localhost:${port}`);
    assert.strictEqual(state.status, 200);
    assert.deepStrictEqual(JSON.parse(state.body).claude[0].tokens, { input: 1, output: 2, cache: 7, total: 10 });
    assert.strictEqual((await request('GET', '/state', 'example.com')).status, 403);
    assert.strictEqual((await request('GET', '/', `evil.example:${port}`)).status, 403);
    assert.strictEqual((await request('POST', '/state')).status, 405);
    assert.strictEqual((await request('GET', '/x')).status, 404);
    for (const r of [page, state]) assert.ok(!r.body.includes(PLANTED), 'no conversation text in any response');
  } finally {
    await new Promise((r) => srv.close(r));
    f.done();
  }
});
