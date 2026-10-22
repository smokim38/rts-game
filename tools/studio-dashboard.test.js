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
// A time `s` seconds from now, for records that must come in order after the dashboard opened.
const later = (s) => new Date(Date.now() + s * 1000).toISOString();

function folders() {
  const base = fs.mkdtempSync(path.join(os.tmpdir(), 'dashboard-test-'));
  const claudeDir = path.join(base, 'claude');
  const codexDir = path.join(base, 'logs');
  const sessionsDir = path.join(base, 'sessions');
  for (const dir of [claudeDir, codexDir, sessionsDir]) fs.mkdirSync(dir);
  // The temporary folder stands in for both the repository and the user folder, and the dashboard
  // counts as opened when the test began: records timed since then are after it opened.
  const opened = Date.now();
  const dirs = { claudeDir, codexDir, sessionsDir, repoDir: base, homeDir: base, now: () => opened };
  return { base, claudeDir, codexDir, sessionsDir, dirs, done: () => fs.rmSync(base, { recursive: true, force: true }) };
}

// Session records as #50's session-marker writes them.
const record = (session, event, pid, agent = 'main session', at = new Date().toISOString()) => ({ session, agent, event, at, pid });
const sessionLog = (f, id, records) => fs.appendFileSync(path.join(f.sessionsDir, `${id}.jsonl`), jsonl(records));
const byName = (s) => Object.fromEntries(s.claude.map((a) => [a.name, a.state]));

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

    // A read that finds no complete line hasn't reached the end yet.
    const big = path.join(f.base, 'big.jsonl');
    fs.writeFileSync(big, `${'x'.repeat(40)}\n{"e":5}\n`);
    const small = D.tailer(big, f.base, { chunk: 16 });
    assert.deepStrictEqual(small.read(), []);
    assert.strictEqual(small.atEnd, false);
    const got = [];
    do got.push(...small.read()); while (!small.atEnd);
    assert.deepStrictEqual(got, [{ e: 5 }]);
  } finally {
    f.done();
  }
});

test('the tailer drops an unfinished line once it passes the limit, and carries on after it', () => {
  const f = folders();
  try {
    const file = path.join(f.base, 'long.jsonl');
    fs.writeFileSync(file, `{"a":1}\n{"long":"${'y'.repeat(100)}"}\n{"b":2}\n`);
    const tail = D.tailer(file, f.base, { chunk: 16, maxLine: 32 });
    const got = [];
    do got.push(...tail.read()); while (!tail.atEnd);
    assert.deepStrictEqual(got, [{ a: 1 }, { b: 2 }]);
  } finally {
    f.done();
  }
});

test('a process id counts as running only while that process exists', () => {
  assert.strictEqual(D.pidAlive(process.pid), true);
  for (const pid of [0, -1, null, undefined, 1.5, '12', 2 ** 53]) assert.strictEqual(D.pidAlive(pid), false);
});

test('Claude: sessions run from their start record until their end record or their process is gone', () => {
  const f = folders();
  try {
    const at = (name) => path.join(f.claudeDir, name);
    const live = new Set([101, 104]);
    const watch = () => D.studio({ ...f.dirs, alive: (pid) => live.has(pid) });
    // The Producer session, running, with a running subagent and one that finished earlier.
    sessionLog(f, 'live', [record('live', 'start', 101, 'producer')]);
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
    // A session that ended before the dashboard opened, and one with a log written just now but no
    // start record: neither is shown. A session whose process is gone with no end record shows as
    // finished.
    sessionLog(f, 'ended', [record('ended', 'start', 102, 'main session', ago(120e3).toISOString()), record('ended', 'end', 102, 'main session', ago(60e3).toISOString())]);
    fs.writeFileSync(at('ended.jsonl'), jsonl([prompt()]));
    sessionLog(f, 'gone', [record('gone', 'start', 103)]);
    fs.writeFileSync(at('untracked.jsonl'), jsonl([prompt(), ...reply('u1', usage(5, 5, 5, 5))]));
    // A session from before the dashboard opened, compacted since: a second start record from the
    // same process shows it, with that start's time.
    const compactedAt = new Date().toISOString();
    sessionLog(f, 'compacted', [record('compacted', 'start', 104, 'art-lead', ago(600e3).toISOString()), record('compacted', 'start', 104, 'art-lead', compactedAt)]);

    const w = watch();
    w.poll();
    let s = w.state();
    assert.deepStrictEqual(byName(s), { producer: 'running', 'game-designer': 'running', 'tech-architect': 'finished', 'main session': 'finished', 'art-lead': 'running' });
    const producer = s.claude.find((a) => a.name === 'producer');
    const designer = s.claude.find((a) => a.name === 'game-designer');
    assert.deepStrictEqual(producer.tokens, { input: 11, output: 6, cache: 120, total: 137 });
    assert.strictEqual(producer.model, 'claude-opus-5-5');
    assert.strictEqual(designer.parent, producer.key);
    assert.deepStrictEqual(designer.tokens, { input: 3, output: 2, cache: 1, total: 6 });
    assert.strictEqual(s.claude.find((a) => a.name === 'art-lead').startedAt, compactedAt);

    // A reply still being written counts only once it is complete; a malformed line is skipped.
    const [block, ...more] = reply('m3', usage(100, 0, 0, 0));
    const line = JSON.stringify(block);
    fs.appendFileSync(at('live.jsonl'), `not json\n${line.slice(0, 40)}`);
    w.poll();
    assert.strictEqual(w.state().claude.find((a) => a.name === 'producer').tokens.input, 11);
    fs.appendFileSync(at('live.jsonl'), `${line.slice(40)}\n${jsonl(more)}`);
    w.poll();
    assert.strictEqual(w.state().claude.find((a) => a.name === 'producer').tokens.input, 111);

    // The ended session is resumed, a new session starts, the subagent finishes, and the
    // compacted session's process goes away without an end record.
    sessionLog(f, 'ended', [record('ended', 'start', 105, 'qa-lead')]);
    sessionLog(f, 'new', [record('new', 'start', 106, 'ops-lead')]);
    live.add(105).add(106).delete(104);
    fs.writeFileSync(path.join(subs, 'agent-a1.meta.json'), JSON.stringify({ agentType: 'game-designer', finished: true }));
    w.poll();
    s = w.state();
    assert.deepStrictEqual(byName(s), { producer: 'running', 'game-designer': 'finished', 'tech-architect': 'finished', 'main session': 'finished', 'art-lead': 'finished', 'qa-lead': 'running', 'ops-lead': 'running' });

    // The Producer session ends, taking a subagent started since with it; the compacted
    // session's process id is reused by another program, and the session stays finished.
    fs.writeFileSync(path.join(subs, 'agent-a2.jsonl'), jsonl([prompt()]));
    fs.writeFileSync(path.join(subs, 'agent-a2.meta.json'), JSON.stringify({ agentType: 'code-reviewer', finished: false }));
    w.poll();
    assert.strictEqual(byName(w.state())['code-reviewer'], 'running');
    sessionLog(f, 'live', [record('live', 'end', 101, 'producer')]);
    live.add(104);
    w.poll();
    s = w.state();
    assert.deepStrictEqual([byName(s).producer, byName(s)['code-reviewer'], byName(s)['art-lead']], ['finished', 'finished', 'finished']);
    assert.strictEqual(s.claude.length, 8);
    assert.ok(!JSON.stringify(s).includes(PLANTED));
  } finally {
    f.done();
  }
});

test('Claude: a session that starts and ends between two polls still appears, as finished', () => {
  const f = folders();
  try {
    const live = new Set();
    const watch = D.studio({ ...f.dirs, alive: (pid) => live.has(pid) });
    watch.poll();
    // One session starts and ends, and another starts and loses its process, both before the next poll.
    sessionLog(f, 'brief', [record('brief', 'start', 301, 'qa-lead'), record('brief', 'end', 301, 'qa-lead')]);
    sessionLog(f, 'crashed', [record('crashed', 'start', 302, 'tech-architect')]);
    watch.poll();
    assert.deepStrictEqual(byName(watch.state()), { 'qa-lead': 'finished', 'tech-architect': 'finished' });
  } finally {
    f.done();
  }
});

test('Claude: a resumed session keeps its earlier subagents finished until they really do something', () => {
  const f = folders();
  try {
    const live = new Set([401]);
    const watch = D.studio({ ...f.dirs, alive: (pid) => live.has(pid) });
    const subs = path.join(f.claudeDir, 'res', 'subagents');
    fs.mkdirSync(subs, { recursive: true });
    const subagent = (id, type, at) => {
      fs.writeFileSync(path.join(subs, `${id}.jsonl`), jsonl([prompt(at)]));
      fs.writeFileSync(path.join(subs, `${id}.meta.json`), JSON.stringify({ agentType: type, finished: false }));
    };
    // A run before the dashboard opened, with a subagent from it: that subagent is never shown.
    sessionLog(f, 'res', [record('res', 'start', 400, 'producer', ago(600e3).toISOString()), record('res', 'end', 400, 'producer', ago(500e3).toISOString())]);
    subagent('agent-old', 'tech-architect', ago(550e3).toISOString());
    sessionLog(f, 'res', [record('res', 'start', 401, 'producer', later(1))]);
    subagent('agent-r1', 'game-designer', later(2));
    watch.poll();
    assert.deepStrictEqual(byName(watch.state()), { producer: 'running', 'game-designer': 'running' });

    // The session ends without the subagent finishing, then is resumed: the subagent stays finished.
    sessionLog(f, 'res', [record('res', 'end', 401, 'producer', later(3))]);
    watch.poll();
    assert.deepStrictEqual(byName(watch.state()), { producer: 'finished', 'game-designer': 'finished' });
    sessionLog(f, 'res', [record('res', 'start', 402, 'producer', later(4))]);
    live.add(402);
    watch.poll();
    assert.deepStrictEqual(byName(watch.state()), { producer: 'running', 'game-designer': 'finished' });

    // Its process disappears and it is resumed again within one poll: still finished.
    sessionLog(f, 'res', [record('res', 'start', 403, 'producer', later(5))]);
    live.delete(402);
    live.add(403);
    watch.poll();
    assert.deepStrictEqual(byName(watch.state()), { producer: 'running', 'game-designer': 'finished' });

    // The subagent really does something in this run: running again.
    fs.appendFileSync(path.join(subs, 'agent-r1.jsonl'), jsonl([prompt(later(6))]));
    watch.poll();
    assert.deepStrictEqual(byName(watch.state()), { producer: 'running', 'game-designer': 'running' });
  } finally {
    f.done();
  }
});

test('links: nothing outside the allowed folders is read', (t) => {
  const f = folders();
  try {
    // Lookalike logs outside every allowed folder, full of planted text and tokens.
    const outside = path.join(f.base, 'outside');
    fs.mkdirSync(path.join(outside, 'subagents'), { recursive: true });
    fs.writeFileSync(path.join(outside, 'subagents', 'agent-x.jsonl'), jsonl([prompt(), ...reply('x1', usage(999, 0, 0, 0))]));
    fs.writeFileSync(path.join(outside, 'subagents', 'agent-x.meta.json'), JSON.stringify({ agentType: `${PLANTED}-AGENT`, finished: false }));
    fs.writeFileSync(path.join(outside, 'transcript.jsonl'), jsonl([prompt(), ...reply('x2', usage(999, 0, 0, 0))]));
    fs.writeFileSync(path.join(outside, 'run.jsonl'), jsonl([{ at: new Date().toISOString(), event: 'start', role: 'reviewer', issue: 1, pr: 2 }]));
    fs.writeFileSync(path.join(outside, 'records.jsonl'), jsonl([record('linked3', 'start', 201, `${PLANTED}-AGENT`)]));

    // A running session whose subagent folder is a junction to the outside folder.
    sessionLog(f, 'linked', [record('linked', 'start', 201)]);
    fs.writeFileSync(path.join(f.claudeDir, 'linked.jsonl'), jsonl([prompt()]));
    fs.symlinkSync(outside, path.join(f.claudeDir, 'linked'), 'junction');
    // File links, where this PC allows them without extra rights.
    let fileLinks = true;
    try {
      fs.symlinkSync(path.join(outside, 'transcript.jsonl'), path.join(f.claudeDir, 'linked2.jsonl'), 'file');
      fs.symlinkSync(path.join(outside, 'run.jsonl'), path.join(f.codexDir, 'linked.jsonl'), 'file');
      fs.symlinkSync(path.join(outside, 'records.jsonl'), path.join(f.sessionsDir, 'linked3.jsonl'), 'file');
      sessionLog(f, 'linked2', [record('linked2', 'start', 201)]);
    } catch (err) {
      if (err.code !== 'EPERM') throw err;
      fileLinks = false;
      t.diagnostic('file links need extra rights on this PC, so only the folder link was tested');
    }

    const watch = D.studio({ ...f.dirs, alive: (pid) => pid === 201 });
    watch.poll();
    watch.poll();
    const s = watch.state();
    assert.deepStrictEqual(s.claude.map((a) => [a.kind, a.name, a.tokens.total]), fileLinks ? [['session', 'main session', 0], ['session', 'main session', 0]] : [['session', 'main session', 0]]);
    assert.deepStrictEqual(s.codex, []);
    assert.ok(!JSON.stringify(s).includes(PLANTED));
  } finally {
    f.done();
  }
});

test('links: an allowed folder that is itself a link to elsewhere is not read', () => {
  const f = folders();
  try {
    // Each allowed folder is replaced by a junction to a lookalike folder outside it.
    const outside = (dir, file, records) => {
      const target = path.join(f.base, `outside-${path.basename(dir)}`);
      fs.mkdirSync(target);
      fs.writeFileSync(path.join(target, file), jsonl(records));
      fs.rmdirSync(dir);
      fs.symlinkSync(target, dir, 'junction');
    };
    outside(f.codexDir, 'run.jsonl', [{ at: new Date().toISOString(), event: 'start', role: 'reviewer', issue: 1, pr: 2 }]);
    outside(f.sessionsDir, 's.jsonl', [record('s', 'start', 501, `${PLANTED}-AGENT`)]);
    const watch = D.studio({ ...f.dirs, alive: (pid) => pid === 501 });
    watch.poll();
    watch.poll();
    assert.deepStrictEqual(watch.state().codex, []);
    assert.deepStrictEqual(watch.state().claude, []);

    // With real record folders but a linked Claude folder, the session shows without its log.
    const g = folders();
    try {
      sessionLog(g, 's', [record('s', 'start', 501)]);
      const target = path.join(g.base, 'outside-claude');
      fs.mkdirSync(target);
      fs.writeFileSync(path.join(target, 's.jsonl'), jsonl([prompt(), ...reply('x1', usage(999, 0, 0, 0))]));
      fs.rmdirSync(g.claudeDir);
      fs.symlinkSync(target, g.claudeDir, 'junction');
      const w = D.studio({ ...g.dirs, alive: (pid) => pid === 501 });
      w.poll();
      w.poll();
      assert.deepStrictEqual(w.state().claude.map((a) => [a.name, a.tokens.total, a.model]), [['main session', 0, null]]);
      for (const s of [watch.state(), w.state()]) assert.ok(!JSON.stringify(s).includes(PLANTED));
    } finally {
      g.done();
    }
  } finally {
    f.done();
  }
});

test('Codex: live launcher runs and their helper agents, from the launcher log entries', () => {
  const f = folders();
  try {
    const log = (name, events) => {
      const file = path.join(f.codexDir, name);
      fs.writeFileSync(file, jsonl(events));
      return file;
    };
    const base = Date.now();
    const t = (s) => new Date(base - s * 1000).toISOString();
    const tokens = (total) => ({ input: total - 10, cachedInput: 5, cacheWrite: 0, output: 10, reasoningOutput: 2, total });
    // Runs that started before the dashboard opened, 200 seconds before `base`: not shown.
    log('ended.jsonl', [{ at: t(600), event: 'start', role: 'tech-artist', issue: 9 }, { at: t(500), event: 'end', error: null }]);
    log('stale.jsonl', [{ at: t(6 * 3600), event: 'start', role: 'qa-test-engineer', issue: 8, pr: 7 }]);
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
    const watch = D.studio({ ...f.dirs, now: () => base - 200e3 });
    watch.poll();
    let [run, ...others] = watch.state().codex;
    assert.deepStrictEqual(others, []);
    assert.deepStrictEqual([run.role, run.issue, run.pr, run.state, run.endedAt, run.startedAt], ['reviewer', 46, 48, 'running', null, t(120)]);
    assert.deepStrictEqual(run.tokens, { input: 1190, cachedInput: 5, output: 10, total: 1200 });
    assert.deepStrictEqual(run.helpers.map((h) => [h.name, h.model, h.effort, h.state, h.tokens?.total ?? null, h.startedAt]), [
      ['worker', 'gpt-mini-r', 'high', 'finished', 120, t(90)],
      ['helper', null, null, 'running', null, t(60)],
    ]);

    // The finished helper is given more work and finishes again, the run ends, and a new run starts.
    fs.appendFileSync(open, jsonl([
      { at: t(0), event: 'helper-resumed', threadId: 'h1', name: 'root/worker', model: null, effort: null, status: null },
      { at: t(0), event: 'helper-ended', threadId: 'h1', status: 'completed', tokens: tokens(150) },
      { at: t(0), event: 'helper-ended', threadId: 'h2', status: 'run-ended', tokens: null },
      { at: t(0), event: 'end', error: null },
    ]));
    log('next.jsonl', [{ at: t(0), event: 'start', role: 'gameplay-programmer', issue: 50 }]);
    watch.poll();
    const s = watch.state();
    [run] = s.codex;
    assert.deepStrictEqual(run.helpers.map((h) => [h.name, h.model, h.effort, h.state, h.tokens?.total ?? null, h.endedAt]), [['worker', 'gpt-mini-r', 'high', 'finished', 150, t(0)], ['helper', null, null, 'finished', null, t(0)]]);
    assert.strictEqual(run.state, 'finished');
    assert.ok(run.endedAt);
    assert.deepStrictEqual(s.codex.map((r) => [r.role, r.issue, r.state]), [['reviewer', 46, 'finished'], ['gameplay-programmer', 50, 'running']]);
    assert.ok(!JSON.stringify(s).includes(PLANTED));
  } finally {
    f.done();
  }
});

test('Codex: a log that starts with a long unreadable stretch is read through to its records', () => {
  const f = folders();
  try {
    // More than one read's worth of a broken line before the records.
    const junk = `${'x'.repeat(9 * 1024 * 1024)}\n`;
    const start = { at: ago(60e3).toISOString(), event: 'start', role: 'reviewer', issue: 46, pr: 48 };
    fs.writeFileSync(path.join(f.codexDir, 'ended.jsonl'), junk + jsonl([start, { at: ago(30e3).toISOString(), event: 'end', error: null }]));
    fs.writeFileSync(path.join(f.codexDir, 'open.jsonl'), junk + jsonl([{ ...start, at: new Date().toISOString(), issue: 47, pr: 49 }]));
    const watch = D.studio({ ...f.dirs });
    watch.poll();
    watch.poll();
    assert.deepStrictEqual(watch.state().codex.map((r) => [r.issue, r.state]), [[47, 'running']]);
  } finally {
    f.done();
  }
});

// Live only: for each kind of agent, lines added after it ended never make it appear; only a real
// new start or real new activity does.
test('live only: a Codex run that ended before the dashboard opened stays hidden after later lines', () => {
  const f = folders();
  try {
    const now = () => new Date().toISOString();
    const ended = path.join(f.codexDir, 'ended.jsonl');
    fs.writeFileSync(ended, jsonl([{ at: ago(60e3).toISOString(), event: 'start', role: 'reviewer', issue: 46, pr: 48 }, { at: ago(30e3).toISOString(), event: 'end', error: null }]));
    const stale = path.join(f.codexDir, 'stale.jsonl');
    fs.writeFileSync(stale, jsonl([{ at: ago(6 * 3600e3).toISOString(), event: 'start', role: 'qa-test-engineer', issue: 8, pr: 7 }]));
    const watch = D.studio({ ...f.dirs });
    watch.poll();
    assert.deepStrictEqual(watch.state().codex, []);
    // The launcher posts its record after the end line; a stopped run's log gets a late line too.
    fs.appendFileSync(ended, jsonl([{ at: now(), event: 'record-posted', record: 'verdict' }]));
    fs.appendFileSync(stale, jsonl([{ at: now(), event: 'record-posted', record: 'qa' }, { at: now(), event: 'token-usage', threadId: 't1', total: { total: 5 } }]));
    watch.poll();
    assert.deepStrictEqual(watch.state().codex, []);
    // A real new run appears.
    fs.writeFileSync(path.join(f.codexDir, 'new.jsonl'), jsonl([{ at: now(), event: 'start', role: 'tech-artist', issue: 9 }]));
    watch.poll();
    assert.deepStrictEqual(watch.state().codex.map((r) => [r.issue, r.state]), [[9, 'running']]);
  } finally {
    f.done();
  }
});

test('subagents and helpers appear only under shown sessions and runs', () => {
  const f = folders();
  try {
    // A session and a run that started before the dashboard opened and are still running, each
    // with a subagent or helper active since: none of them is shown.
    sessionLog(f, 'early', [record('early', 'start', process.pid, 'producer', ago(600e3).toISOString())]);
    const subs = path.join(f.claudeDir, 'early', 'subagents');
    fs.mkdirSync(subs, { recursive: true });
    fs.writeFileSync(path.join(subs, 'agent-e.jsonl'), jsonl([prompt()]));
    fs.writeFileSync(path.join(subs, 'agent-e.meta.json'), JSON.stringify({ agentType: 'game-designer', finished: false }));
    const early = path.join(f.codexDir, 'early.jsonl');
    fs.writeFileSync(early, jsonl([
      { at: ago(600e3).toISOString(), event: 'start', role: 'reviewer', issue: 46, pr: 48 },
      { at: new Date().toISOString(), event: 'helper-started', threadId: 'h1', name: 'root/worker', model: null, effort: null, status: null },
    ]));
    // A shown session whose subagent's only message is from before the session was shown.
    sessionLog(f, 'shown', [record('shown', 'start', process.pid, 'qa-lead', later(1))]);
    const shownSubs = path.join(f.claudeDir, 'shown', 'subagents');
    fs.mkdirSync(shownSubs, { recursive: true });
    fs.writeFileSync(path.join(shownSubs, 'agent-old.jsonl'), jsonl([prompt(ago(300e3).toISOString())]));
    fs.writeFileSync(path.join(shownSubs, 'agent-old.meta.json'), JSON.stringify({ agentType: 'tech-architect', finished: false }));
    // A shown run's helpers are shown with it.
    fs.writeFileSync(path.join(f.codexDir, 'shown.jsonl'), jsonl([
      { at: later(1), event: 'start', role: 'tech-artist', issue: 9 },
      { at: later(2), event: 'helper-started', threadId: 'h2', name: 'root/painter', model: null, effort: null, status: null },
    ]));
    const watch = D.studio({ ...f.dirs });
    watch.poll();
    assert.deepStrictEqual(byName(watch.state()), { 'qa-lead': 'running' });
    assert.deepStrictEqual(watch.state().codex.map((r) => [r.issue, r.helpers.map((h) => h.name)]), [[9, ['painter']]]);

    // The early session and run carry on and end: still not shown, nor their children.
    fs.appendFileSync(path.join(subs, 'agent-e.jsonl'), jsonl([prompt(later(2))]));
    sessionLog(f, 'early', [record('early', 'end', process.pid, 'producer', later(3))]);
    fs.appendFileSync(early, jsonl([{ at: later(3), event: 'helper-ended', threadId: 'h1', status: 'completed', tokens: null }, { at: later(3), event: 'end', error: null }]));
    watch.poll();
    assert.deepStrictEqual(byName(watch.state()), { 'qa-lead': 'running' });
    assert.deepStrictEqual(watch.state().codex.map((r) => r.issue), [9]);
  } finally {
    f.done();
  }
});

test('live only: a Claude session that ended before the dashboard opened stays hidden after later lines', () => {
  const f = folders();
  try {
    const live = new Set([601]);
    sessionLog(f, 'done', [record('done', 'start', 600, 'main session', ago(120e3).toISOString()), record('done', 'end', 600, 'main session', ago(60e3).toISOString())]);
    sessionLog(f, 'lost', [record('lost', 'start', 602, 'tech-architect', ago(120e3).toISOString())]);
    const watch = D.studio({ ...f.dirs, alive: (pid) => live.has(pid) });
    watch.poll();
    assert.deepStrictEqual(watch.state().claude, []);
    // Repeated end records, and activity in their logs, don't show them.
    sessionLog(f, 'done', [record('done', 'end', 600)]);
    sessionLog(f, 'lost', [record('lost', 'end', 602, 'tech-architect')]);
    fs.writeFileSync(path.join(f.claudeDir, 'done.jsonl'), jsonl([prompt()]));
    fs.writeFileSync(path.join(f.claudeDir, 'lost.jsonl'), jsonl([prompt()]));
    watch.poll();
    assert.deepStrictEqual(watch.state().claude, []);
    // A real new start shows it.
    sessionLog(f, 'done', [record('done', 'start', 601, 'qa-lead')]);
    watch.poll();
    assert.deepStrictEqual(byName(watch.state()), { 'qa-lead': 'running' });
  } finally {
    f.done();
  }
});

test('live only: a finished Claude subagent stays hidden after later lines until it really does something', () => {
  const f = folders();
  try {
    const live = new Set([701, 702]);
    sessionLog(f, 'p', [record('p', 'start', 701, 'producer', later(1))]);
    const subs = path.join(f.claudeDir, 'p', 'subagents');
    fs.mkdirSync(subs, { recursive: true });
    const subagent = (id, type, finished, records) => {
      fs.writeFileSync(path.join(subs, `${id}.jsonl`), jsonl(records));
      fs.writeFileSync(path.join(subs, `${id}.meta.json`), JSON.stringify({ agentType: type, finished }));
    };
    subagent('agent-fin', 'tech-architect', true, [prompt(ago(300e3).toISOString())]);
    subagent('agent-run', 'game-designer', false, [prompt(later(2))]);
    const watch = D.studio({ ...f.dirs, alive: (pid) => live.has(pid) });
    watch.poll();
    assert.deepStrictEqual(byName(watch.state()), { producer: 'running', 'game-designer': 'running' });

    // Lines that aren't activity, and activity timed before the dashboard opened, don't show it.
    const late = [{ type: 'system', timestamp: new Date().toISOString(), content: `${PLANTED}-SYSTEM` }, { type: 'summary', summary: `${PLANTED}-SUMMARY` }, prompt(ago(250e3).toISOString())];
    fs.appendFileSync(path.join(subs, 'agent-fin.jsonl'), jsonl(late));
    watch.poll();
    assert.deepStrictEqual(byName(watch.state()), { producer: 'running', 'game-designer': 'running' });

    // A shown subagent that ended with its session stays finished after the same kind of lines.
    sessionLog(f, 'p', [record('p', 'end', 701, 'producer', later(3)), record('p', 'start', 702, 'producer', later(4))]);
    fs.appendFileSync(path.join(subs, 'agent-run.jsonl'), jsonl(late));
    watch.poll();
    assert.deepStrictEqual(byName(watch.state()), { producer: 'running', 'game-designer': 'finished' });

    // Real new activity shows the finished subagent again.
    fs.writeFileSync(path.join(subs, 'agent-fin.meta.json'), JSON.stringify({ agentType: 'tech-architect', finished: false }));
    fs.appendFileSync(path.join(subs, 'agent-fin.jsonl'), jsonl([prompt(later(5))]));
    watch.poll();
    assert.deepStrictEqual(byName(watch.state()), { producer: 'running', 'game-designer': 'finished', 'tech-architect': 'running' });
    assert.ok(!JSON.stringify(watch.state()).includes(PLANTED));
  } finally {
    f.done();
  }
});

test('Claude: a subagent from an earlier run whose log was touched later is not shown', () => {
  const f = folders();
  try {
    const live = new Set([801]);
    sessionLog(f, 'p', [
      record('p', 'start', 800, 'producer', ago(900e3).toISOString()),
      record('p', 'end', 800, 'producer', ago(800e3).toISOString()),
      record('p', 'start', 801, 'producer', later(1)),
    ]);
    const subs = path.join(f.claudeDir, 'p', 'subagents');
    fs.mkdirSync(subs, { recursive: true });
    // Unfinished, last message in the earlier run, and a line without a message written just now.
    fs.writeFileSync(path.join(subs, 'agent-old.jsonl'), jsonl([prompt(ago(850e3).toISOString()), { type: 'system', timestamp: new Date().toISOString() }]));
    fs.writeFileSync(path.join(subs, 'agent-old.meta.json'), JSON.stringify({ agentType: 'tech-architect', finished: false }));
    const watch = D.studio({ ...f.dirs, alive: (pid) => live.has(pid) });
    watch.poll();
    assert.deepStrictEqual(byName(watch.state()), { producer: 'running' });
  } finally {
    f.done();
  }
});

test('Claude: a subagent from a run that ended and was resumed between two polls is shown, finished', () => {
  const f = folders();
  try {
    const live = new Set([812]);
    const watch = D.studio({ ...f.dirs, alive: (pid) => live.has(pid) });
    watch.poll();
    const at = (ms) => new Date(Date.now() + ms).toISOString();
    const subs = path.join(f.claudeDir, 'p', 'subagents');
    fs.mkdirSync(subs, { recursive: true });
    sessionLog(f, 'p', [record('p', 'start', 811, 'producer', at(1000))]);
    fs.writeFileSync(path.join(subs, 'agent-s.jsonl'), jsonl([prompt(at(2000)), ...reply('s1', usage(4, 4, 0, 0), at(2000))]));
    fs.writeFileSync(path.join(subs, 'agent-s.meta.json'), JSON.stringify({ agentType: 'game-designer', finished: false }));
    sessionLog(f, 'p', [record('p', 'end', 811, 'producer', at(3000)), record('p', 'start', 812, 'producer', at(4000))]);
    watch.poll();
    const s = watch.state();
    assert.deepStrictEqual(byName(s), { producer: 'running', 'game-designer': 'finished' });
    assert.strictEqual(s.claude.find((a) => a.name === 'game-designer').tokens.total, 8);
  } finally {
    f.done();
  }
});

test('Claude: a late end record from an earlier process does not end the resumed session', () => {
  const f = folders();
  try {
    const live = new Set([821, 822]);
    sessionLog(f, 's', [record('s', 'start', 821, 'qa-lead')]);
    const watch = D.studio({ ...f.dirs, alive: (pid) => live.has(pid) });
    watch.poll();
    // Resumed in a new process, then the first process's end record arrives late.
    sessionLog(f, 's', [record('s', 'start', 822, 'qa-lead'), record('s', 'end', 821, 'qa-lead')]);
    live.delete(821);
    watch.poll();
    assert.deepStrictEqual(byName(watch.state()), { 'qa-lead': 'running' });
    sessionLog(f, 's', [record('s', 'end', 822, 'qa-lead')]);
    watch.poll();
    assert.deepStrictEqual(byName(watch.state()), { 'qa-lead': 'finished' });
  } finally {
    f.done();
  }
});

test('Claude: a new start that reuses the process id of a session seen gone is a new run', () => {
  const f = folders();
  try {
    const live = new Set([831]);
    sessionLog(f, 's', [record('s', 'start', 831, 'qa-lead', later(1))]);
    const watch = D.studio({ ...f.dirs, alive: (pid) => live.has(pid) });
    watch.poll();
    live.delete(831);
    watch.poll();
    assert.deepStrictEqual(byName(watch.state()), { 'qa-lead': 'finished' });
    const again = later(2);
    sessionLog(f, 's', [record('s', 'start', 831, 'qa-lead', again)]);
    live.add(831);
    watch.poll();
    const [session] = watch.state().claude;
    assert.deepStrictEqual([session.state, session.startedAt], ['running', again]);
  } finally {
    f.done();
  }
});

test('Claude: a subagent keeps its last known state while its info file is unreadable', () => {
  const f = folders();
  try {
    sessionLog(f, 'p', [record('p', 'start', process.pid, 'producer', later(1))]);
    const subs = path.join(f.claudeDir, 'p', 'subagents');
    fs.mkdirSync(subs, { recursive: true });
    const meta = path.join(subs, 'agent-a.meta.json');
    fs.writeFileSync(path.join(subs, 'agent-a.jsonl'), jsonl([prompt(later(2))]));
    fs.writeFileSync(meta, JSON.stringify({ agentType: 'game-designer', finished: false }));
    const watch = D.studio({ ...f.dirs });
    watch.poll();
    fs.writeFileSync(meta, JSON.stringify({ agentType: 'game-designer', finished: true }));
    watch.poll();
    assert.deepStrictEqual(byName(watch.state()), { producer: 'running', 'game-designer': 'finished' });
    // Half written, then gone for a moment.
    fs.writeFileSync(meta, '{"agentType":"game-des');
    watch.poll();
    assert.deepStrictEqual(byName(watch.state()), { producer: 'running', 'game-designer': 'finished' });
    fs.rmSync(meta);
    watch.poll();
    assert.deepStrictEqual(byName(watch.state()), { producer: 'running', 'game-designer': 'finished' });
  } finally {
    f.done();
  }
});

test('Codex: a run already stopped before the dashboard opened stays hidden despite a recent line', () => {
  const f = folders();
  try {
    // Started 5 hours ago, past a run's time limit, with no end; a line was added just now.
    fs.writeFileSync(path.join(f.codexDir, 'old.jsonl'), jsonl([
      { at: ago(5 * 3600e3).toISOString(), event: 'start', role: 'qa-test-engineer', issue: 8, pr: 7 },
      { at: new Date().toISOString(), event: 'record-posted', url: `${PLANTED}-URL` },
    ]));
    const watch = D.studio({ ...f.dirs });
    watch.poll();
    watch.poll();
    assert.deepStrictEqual(watch.state().codex, []);
  } finally {
    f.done();
  }
});

test('Codex: helpers stop with their run when it passes its time limit', () => {
  const f = folders();
  try {
    const base = Date.now();
    const t = (s) => new Date(base - s * 1000).toISOString();
    fs.writeFileSync(path.join(f.codexDir, 'open.jsonl'), jsonl([
      { at: t(3600), event: 'start', role: 'gameplay-programmer', issue: 50 },
      { at: t(3500), event: 'helper-started', threadId: 'h1', name: 'root/worker', model: null, effort: null, status: null },
      { at: t(3400), event: 'helper-started', threadId: 'h2', name: 'root/done', model: null, effort: null, status: null },
      { at: t(0), event: 'helper-ended', threadId: 'h2', status: 'completed', tokens: null },
    ]));
    // The dashboard opened just before the run started; it is an hour later now.
    let clock = base - 3700e3;
    const watch = D.studio({ ...f.dirs, now: () => clock });
    clock = base;
    watch.poll();
    let [run] = watch.state().codex;
    assert.deepStrictEqual([run.state, ...run.helpers.map((h) => h.state)], ['running', 'running', 'finished']);
    clock += D.RUN_LIMIT_MS;
    [run] = watch.state().codex;
    assert.deepStrictEqual([run.state, ...run.helpers.map((h) => h.state)], ['stopped', 'stopped', 'finished']);
  } finally {
    f.done();
  }
});

test('Codex: a failed turn shows the run as failed, without its error text', () => {
  const f = folders();
  try {
    const watch = D.studio({ ...f.dirs });
    watch.poll();
    const now = () => new Date().toISOString();
    // The launcher's sequence for a failed turn: turn-completed with status failed, then end with no error.
    fs.writeFileSync(path.join(f.codexDir, 'failed.jsonl'), jsonl([
      { at: now(), event: 'start', role: 'gameplay-programmer', issue: 50 },
      { at: now(), event: 'thread-started', threadId: 't1' },
      { at: now(), event: 'turn-completed', status: 'failed', error: { message: `${PLANTED}-ERROR`, codexErrorInfo: `${PLANTED}-INFO` } },
      { at: now(), event: 'run-tokens', threadId: 't1', total: null, helpers: [] },
      { at: now(), event: 'end', error: null },
    ]));
    fs.writeFileSync(path.join(f.codexDir, 'ok.jsonl'), jsonl([
      { at: now(), event: 'start', role: 'reviewer', issue: 47, pr: 49 },
      { at: now(), event: 'turn-completed', status: 'completed', error: null },
      { at: now(), event: 'end', error: null },
    ]));
    watch.poll();
    const s = watch.state();
    assert.deepStrictEqual(s.codex.map((r) => [r.issue, r.state]).sort(), [[47, 'finished'], [50, 'failed']]);
    assert.ok(!JSON.stringify(s).includes(PLANTED));
  } finally {
    f.done();
  }
});

test('large logs: a big backlog shows its current tokens within moments', async () => {
  const f = folders();
  let stop = () => {};
  try {
    sessionLog(f, 'big', [record('big', 'start', process.pid, 'producer')]);
    const replies = [];
    for (let i = 0; i < 400; i++) replies.push(...reply(`m${i}`, usage(1, 1, 0, 0)));
    fs.writeFileSync(path.join(f.claudeDir, 'big.jsonl'), jsonl(replies));
    // Small chunks stand in for a log many times the real 8 MiB chunk: hundreds of reads to catch up.
    const watch = D.studio({ ...f.dirs, chunk: 1024 });
    const started = Date.now();
    stop = D.follow(watch, { interval: 1000 });
    const total = () => watch.state().claude[0]?.tokens.input ?? 0;
    while (total() < 400 && Date.now() - started < 10000) await new Promise((r) => setTimeout(r, 20));
    assert.strictEqual(total(), 400);
    assert.ok(Date.now() - started < 5000, `took ${Date.now() - started} ms`);
  } finally {
    stop();
    f.done();
  }
});

// The dashboard notes its start time before its first read of the logs. Agents that start in
// between are new, even if they have finished by the time of that first read.
test('startup: a Claude session that starts and ends just before the first read is kept, with its subagent', () => {
  const f = folders();
  try {
    const base = Date.now();
    const at = (s) => new Date(base + s * 1000).toISOString();
    sessionLog(f, 'quick', [record('quick', 'start', 901, 'qa-lead', at(1)), record('quick', 'end', 901, 'qa-lead', at(4))]);
    const subs = path.join(f.claudeDir, 'quick', 'subagents');
    fs.mkdirSync(subs, { recursive: true });
    fs.writeFileSync(path.join(subs, 'agent-q.jsonl'), jsonl([prompt(at(2)), ...reply('q1', usage(2, 3, 0, 0), at(3))]));
    fs.writeFileSync(path.join(subs, 'agent-q.meta.json'), JSON.stringify({ agentType: 'game-designer', finished: true }));
    const watch = D.studio({ ...f.dirs, now: () => base, alive: () => false });
    watch.poll();
    const s = watch.state();
    assert.deepStrictEqual(byName(s), { 'qa-lead': 'finished', 'game-designer': 'finished' });
    assert.strictEqual(s.claude.find((a) => a.name === 'game-designer').tokens.total, 5);
  } finally {
    f.done();
  }
});

test('startup: a Codex run that starts and ends just before the first read is kept, with its helper', () => {
  const f = folders();
  try {
    const base = Date.now();
    const at = (s) => new Date(base + s * 1000).toISOString();
    fs.writeFileSync(path.join(f.codexDir, 'quick.jsonl'), jsonl([
      { at: at(1), event: 'start', role: 'reviewer', issue: 47, pr: 49 },
      { at: at(2), event: 'helper-started', threadId: 'h1', name: 'root/worker', model: null, effort: null, status: null },
      { at: at(3), event: 'helper-ended', threadId: 'h1', status: 'completed', tokens: { total: 9 } },
      { at: at(4), event: 'end', error: null },
      { at: at(5), event: 'record-posted', url: `${PLANTED}-URL` },
    ]));
    const watch = D.studio({ ...f.dirs, now: () => base });
    watch.poll();
    const s = watch.state();
    assert.deepStrictEqual(s.codex.map((r) => [r.issue, r.state, r.helpers.map((h) => [h.name, h.state, h.tokens?.total])]), [[47, 'finished', [['worker', 'finished', 9]]]]);
    assert.ok(!JSON.stringify(s).includes(PLANTED));
  } finally {
    f.done();
  }
});

test('startup: agents that ended before the dashboard started still stay hidden', () => {
  const f = folders();
  try {
    const base = Date.now();
    const at = (s) => new Date(base + s * 1000).toISOString();
    sessionLog(f, 'old', [record('old', 'start', 911, 'qa-lead', at(-60)), record('old', 'end', 911, 'qa-lead', at(-30))]);
    const subs = path.join(f.claudeDir, 'old', 'subagents');
    fs.mkdirSync(subs, { recursive: true });
    fs.writeFileSync(path.join(subs, 'agent-o.jsonl'), jsonl([prompt(at(-50))]));
    fs.writeFileSync(path.join(subs, 'agent-o.meta.json'), JSON.stringify({ agentType: 'game-designer', finished: true }));
    fs.writeFileSync(path.join(f.codexDir, 'old.jsonl'), jsonl([
      { at: at(-60), event: 'start', role: 'reviewer', issue: 46, pr: 48 },
      { at: at(-50), event: 'helper-started', threadId: 'h1', name: 'root/worker', model: null, effort: null, status: null },
      { at: at(-40), event: 'helper-ended', threadId: 'h1', status: 'completed', tokens: null },
      { at: at(-30), event: 'end', error: null },
      { at: at(1), event: 'record-posted', url: 'x' },
    ]));
    const watch = D.studio({ ...f.dirs, now: () => base, alive: () => false });
    watch.poll();
    watch.poll();
    assert.deepStrictEqual([watch.state().claude, watch.state().codex], [[], []]);
  } finally {
    f.done();
  }
});

test('startup: an agent that started before the dashboard is not shown, still running or ending just after', () => {
  const f = folders();
  try {
    const base = Date.now();
    const at = (s) => new Date(base + s * 1000).toISOString();
    sessionLog(f, 'running', [record('running', 'start', 921, 'qa-lead', at(-60))]);
    sessionLog(f, 'ending', [record('ending', 'start', 922, 'ops-lead', at(-60)), record('ending', 'end', 922, 'ops-lead', at(1))]);
    fs.writeFileSync(path.join(f.codexDir, 'running.jsonl'), jsonl([
      { at: at(-60), event: 'start', role: 'reviewer', issue: 46, pr: 48 },
      { at: at(1), event: 'helper-started', threadId: 'h1', name: 'root/worker', model: null, effort: null, status: null },
    ]));
    fs.writeFileSync(path.join(f.codexDir, 'ending.jsonl'), jsonl([
      { at: at(-60), event: 'start', role: 'reviewer', issue: 47, pr: 49 },
      { at: at(1), event: 'end', error: null },
      { at: at(2), event: 'record-posted', url: 'x' },
    ]));
    const watch = D.studio({ ...f.dirs, now: () => base, alive: (pid) => pid === 921 });
    watch.poll();
    watch.poll();
    assert.deepStrictEqual([watch.state().claude, watch.state().codex], [[], []]);
  } finally {
    f.done();
  }
});

test('startup: a session that started before the dashboard is shown from its resume or compaction after it', () => {
  const f = folders();
  try {
    const base = Date.now();
    const at = (s) => new Date(base + s * 1000).toISOString();
    const live = new Set([932, 933]);
    // Ended before the dashboard opened and resumed after, in a new process.
    sessionLog(f, 'r', [record('r', 'start', 931, 'qa-lead', at(-60)), record('r', 'end', 931, 'qa-lead', at(-30)), record('r', 'start', 932, 'qa-lead', at(5))]);
    // Running since before the dashboard opened, compacted after (a start record from the same process).
    sessionLog(f, 'c', [record('c', 'start', 933, 'ops-lead', at(-60)), record('c', 'start', 933, 'ops-lead', at(3))]);
    const watch = D.studio({ ...f.dirs, now: () => base, alive: (pid) => live.has(pid) });
    watch.poll();
    const s = watch.state();
    assert.deepStrictEqual(byName(s), { 'qa-lead': 'running', 'ops-lead': 'running' });
    assert.strictEqual(s.claude.find((a) => a.name === 'qa-lead').startedAt, at(5));
    assert.strictEqual(s.claude.find((a) => a.name === 'ops-lead').startedAt, at(3));
  } finally {
    f.done();
  }
});

test('a shown session and its subagents count only activity since the session is shown from', () => {
  const f = folders();
  try {
    const base = Date.now();
    const at = (s) => new Date(base + s * 1000).toISOString();
    // Running since before the dashboard opened, with earlier activity in its log and a subagent's.
    sessionLog(f, 's', [record('s', 'start', process.pid, 'producer', at(-600)), record('s', 'start', process.pid, 'producer', at(1))]);
    fs.writeFileSync(path.join(f.claudeDir, 's.jsonl'), jsonl([prompt(at(-500)), ...reply('h1', usage(999, 999, 999, 999), at(-500)), prompt(at(2)), ...reply('n1', usage(2, 0, 0, 0), at(2))]));
    const subs = path.join(f.claudeDir, 's', 'subagents');
    fs.mkdirSync(subs, { recursive: true });
    fs.writeFileSync(path.join(subs, 'agent-a.jsonl'), jsonl([prompt(at(-400)), ...reply('h2', usage(888, 888, 888, 888), at(-400)), prompt(at(3)), ...reply('n2', usage(3, 0, 0, 0), at(3))]));
    fs.writeFileSync(path.join(subs, 'agent-a.meta.json'), JSON.stringify({ agentType: 'game-designer', finished: false }));
    const watch = D.studio({ ...f.dirs, now: () => base });
    watch.poll();
    watch.poll();
    const s = watch.state();
    const session = s.claude.find((a) => a.name === 'producer');
    const subagent = s.claude.find((a) => a.name === 'game-designer');
    assert.deepStrictEqual([session.tokens.total, session.startedAt, session.lastActivityAt], [2, at(1), at(2)]);
    assert.deepStrictEqual([subagent.tokens.total, subagent.startedAt, subagent.lastActivityAt, subagent.state], [3, at(3), at(3), 'running']);
  } finally {
    f.done();
  }
});

test('a record file that briefly cannot be checked keeps its place and the session its state', () => {
  const f = folders();
  const statSync = fs.statSync;
  let failing = false;
  try {
    fs.statSync = (file, ...rest) => {
      if (failing && String(file).endsWith('s.jsonl')) throw Object.assign(new Error('EBUSY: resource busy or locked'), { code: 'EBUSY' });
      return statSync(file, ...rest);
    };
    // The tailer reads on from where it was, not from the start.
    const file = path.join(f.base, 's.jsonl');
    fs.writeFileSync(file, '{"a":1}\n');
    const tail = D.tailer(file);
    assert.deepStrictEqual(tail.read(), [{ a: 1 }]);
    failing = true;
    assert.deepStrictEqual(tail.read(), []);
    failing = false;
    fs.appendFileSync(file, '{"b":2}\n');
    assert.deepStrictEqual(tail.read(), [{ b: 2 }]);

    // A finished session doesn't come back when its old start record could be read again.
    const live = new Set([961]);
    sessionLog(f, 's', [record('s', 'start', 961, 'qa-lead', later(1))]);
    const watch = D.studio({ ...f.dirs, alive: (pid) => live.has(pid) });
    watch.poll();
    live.delete(961);
    watch.poll();
    assert.deepStrictEqual(byName(watch.state()), { 'qa-lead': 'finished' });
    failing = true;
    watch.poll();
    failing = false;
    live.add(961);
    watch.poll();
    assert.deepStrictEqual(byName(watch.state()), { 'qa-lead': 'finished' });
  } finally {
    fs.statSync = statSync;
    f.done();
  }
});

test("a session's own agent name for its current run replaces the marker's main session", () => {
  const f = folders();
  try {
    const live = new Set([process.pid, 972]);
    // Started without --agent, but its log names the Producer for this run.
    sessionLog(f, 'p', [record('p', 'start', process.pid, 'main session', later(1))]);
    fs.writeFileSync(path.join(f.claudeDir, 'p.jsonl'), jsonl([{ type: 'agent-setting', agentSetting: 'producer', sessionId: 'p' }, prompt(later(2))]));
    // Named in an earlier run only, then resumed: the earlier name doesn't count.
    sessionLog(f, 'q', [record('q', 'start', 971, 'main session', later(1)), record('q', 'end', 971, 'main session', later(2)), record('q', 'start', 972, 'main session', later(3))]);
    fs.writeFileSync(path.join(f.claudeDir, 'q.jsonl'), jsonl([{ type: 'agent-setting', agentSetting: 'tech-architect', sessionId: 'q' }, prompt(later(1.5)), prompt(later(4))]));
    const watch = D.studio({ ...f.dirs, alive: (pid) => live.has(pid) });
    watch.poll();
    assert.deepStrictEqual(byName(watch.state()), { producer: 'running', 'main session': 'running' });
    // A name in its current run does count.
    fs.appendFileSync(path.join(f.claudeDir, 'q.jsonl'), jsonl([{ type: 'agent-setting', agentSetting: 'qa-lead', sessionId: 'q' }, prompt(later(5))]));
    watch.poll();
    assert.deepStrictEqual(byName(watch.state()), { producer: 'running', 'qa-lead': 'running' });
  } finally {
    f.done();
  }
});

test('one-letter agent names are accepted, paths and commands still are not', () => {
  const f = folders();
  try {
    const names = { a: 'x', b: '7', c: 'C:x', d: 'a/b', e: 'rm x', f: 'codex:codex-rescue' };
    for (const [id, agent] of Object.entries(names)) sessionLog(f, id, [record(id, 'start', process.pid, agent, later(1))]);
    const watch = D.studio({ ...f.dirs });
    watch.poll();
    assert.deepStrictEqual(watch.state().claude.map((a) => a.name).sort(), ['7', 'codex:codex-rescue', 'main session', 'main session', 'main session', 'x']);
  } finally {
    f.done();
  }
});

test('known sessions are still checked when their records cannot be listed or are gone', () => {
  const f = folders();
  try {
    const live = new Set([951, 952]);
    sessionLog(f, 'a', [record('a', 'start', 951, 'qa-lead', later(1))]);
    sessionLog(f, 'b', [record('b', 'start', 952, 'ops-lead', later(1))]);
    const watch = D.studio({ ...f.dirs, alive: (pid) => live.has(pid) });
    watch.poll();
    assert.deepStrictEqual(byName(watch.state()), { 'qa-lead': 'running', 'ops-lead': 'running' });
    // One record file disappears, then its process exits.
    fs.rmSync(path.join(f.sessionsDir, 'b.jsonl'));
    live.delete(952);
    watch.poll();
    assert.deepStrictEqual(byName(watch.state()), { 'qa-lead': 'running', 'ops-lead': 'finished' });
    // The whole records folder can't be listed any more, then the other process exits.
    fs.renameSync(f.sessionsDir, `${f.sessionsDir}-moved`);
    live.delete(951);
    watch.poll();
    assert.deepStrictEqual(byName(watch.state()), { 'qa-lead': 'finished', 'ops-lead': 'finished' });
  } finally {
    f.done();
  }
});

test('a log that cannot be read at first and is read later does not change which agents are shown', () => {
  const f = folders();
  const openSync = fs.openSync;
  try {
    const base = Date.now();
    const at = (s) => new Date(base + s * 1000).toISOString();
    sessionLog(f, 'before', [record('before', 'start', process.pid, 'qa-lead', at(-60))]);
    sessionLog(f, 'after', [record('after', 'start', process.pid, 'ops-lead', at(1)), record('after', 'end', process.pid, 'ops-lead', at(2))]);
    fs.writeFileSync(path.join(f.codexDir, 'before.jsonl'), jsonl([{ at: at(-60), event: 'start', role: 'reviewer', issue: 46, pr: 48 }]));
    fs.writeFileSync(path.join(f.codexDir, 'after.jsonl'), jsonl([{ at: at(1), event: 'start', role: 'reviewer', issue: 47, pr: 49 }, { at: at(2), event: 'end', error: null }]));
    const summary = (w) => JSON.stringify([w.state().claude.map((a) => [a.name, a.state]).sort(), w.state().codex.map((r) => [r.issue, r.state]).sort()]);
    const direct = D.studio({ ...f.dirs, now: () => base });
    direct.poll();
    // Every log fails to open for the first few reads, then can be read.
    let blocked = true;
    fs.openSync = (...args) => {
      if (blocked) throw Object.assign(new Error('EBUSY: resource busy or locked'), { code: 'EBUSY' });
      return openSync(...args);
    };
    const late = D.studio({ ...f.dirs, now: () => base });
    for (let i = 0; i < 3; i++) late.poll();
    assert.deepStrictEqual([late.state().claude, late.state().codex], [[], []]);
    blocked = false;
    late.poll();
    assert.strictEqual(summary(late), summary(direct));
    assert.strictEqual(summary(late), JSON.stringify([[['ops-lead', 'finished']], [[47, 'finished']]]));
  } finally {
    fs.openSync = openSync;
    f.done();
  }
});

test('a subagent keeps its finished state when its info file holds no valid finished value', () => {
  const f = folders();
  try {
    sessionLog(f, 'p', [record('p', 'start', process.pid, 'producer', later(1))]);
    const subs = path.join(f.claudeDir, 'p', 'subagents');
    fs.mkdirSync(subs, { recursive: true });
    const meta = path.join(subs, 'agent-a.meta.json');
    fs.writeFileSync(path.join(subs, 'agent-a.jsonl'), jsonl([prompt(later(2))]));
    fs.writeFileSync(meta, JSON.stringify({ agentType: 'game-designer', finished: false }));
    const watch = D.studio({ ...f.dirs });
    watch.poll();
    fs.writeFileSync(meta, JSON.stringify({ agentType: 'game-designer', finished: true }));
    watch.poll();
    for (const broken of ['{}', '[]', 'null', '{"agentType":"game-designer","finished":"no"}']) {
      fs.writeFileSync(meta, broken);
      watch.poll();
      assert.deepStrictEqual(byName(watch.state()), { producer: 'running', 'game-designer': 'finished' }, broken);
    }
  } finally {
    f.done();
  }
});

test('one unreadable log does not stop the others, at startup or later', () => {
  const f = folders();
  const openSync = fs.openSync;
  try {
    const at = new Date().toISOString();
    for (const issue of [46, 47]) fs.writeFileSync(path.join(f.codexDir, `run-${issue}.jsonl`), jsonl([{ at, event: 'start', role: 'reviewer', issue, pr: issue + 2 }]));
    sessionLog(f, 's', [record('s', 'start', process.pid, 'producer')]);
    fs.writeFileSync(path.join(f.claudeDir, 's.jsonl'), jsonl([prompt(), ...reply('m1', usage(1, 1, 0, 0))]));
    // run-46.jsonl can't be opened (locked, or removed between listing and opening).
    fs.openSync = (file, ...rest) => {
      if (String(file).endsWith('run-46.jsonl')) throw Object.assign(new Error('EBUSY: resource busy or locked'), { code: 'EBUSY' });
      return openSync(file, ...rest);
    };
    const watch = D.studio({ ...f.dirs });
    watch.poll();
    assert.deepStrictEqual(watch.state().codex.map((r) => r.issue), [47]);
    assert.strictEqual(watch.state().claude[0].tokens.total, 2);
    // It can be read again later, and then shows.
    fs.openSync = openSync;
    fs.appendFileSync(path.join(f.claudeDir, 's.jsonl'), jsonl(reply('m2', usage(1, 1, 0, 0))));
    watch.poll();
    assert.strictEqual(watch.state().claude[0].tokens.total, 4);
  } finally {
    fs.openSync = openSync;
    f.done();
  }
});

test('read-only: polling writes, changes and deletes nothing', () => {
  const f = folders();
  const names = ['writeFileSync', 'appendFileSync', 'mkdirSync', 'rmSync', 'unlinkSync', 'renameSync', 'copyFileSync', 'truncateSync', 'utimesSync', 'writeSync', 'writeFile', 'appendFile'];
  const saved = Object.fromEntries([...names, 'openSync'].map((n) => [n, fs[n]]));
  try {
    sessionLog(f, 's', [record('s', 'start', process.pid)]);
    fs.writeFileSync(path.join(f.claudeDir, 's.jsonl'), jsonl([prompt(), ...reply('m1', usage(1, 1, 1, 1))]));
    fs.writeFileSync(path.join(f.codexDir, 'r.jsonl'), jsonl([{ at: new Date().toISOString(), event: 'start', role: 'reviewer', issue: 1, pr: 2 }]));
    for (const n of names) fs[n] = () => assert.fail(`the dashboard called fs.${n}`);
    fs.openSync = (file, flags, ...rest) => (flags === 'r' ? saved.openSync(file, flags, ...rest) : assert.fail(`the dashboard opened ${file} with ${flags}`));
    const watch = D.studio({ ...f.dirs });
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
  sessionLog(f, 's', [record('s', 'start', process.pid, 'producer')]);
  fs.writeFileSync(path.join(f.claudeDir, 's.jsonl'), jsonl([{ type: 'agent-setting', agentSetting: 'producer' }, prompt(), ...reply('m1', usage(1, 2, 3, 4))]));
  // Name, model and effort fields holding file paths, commands and email addresses, next to real
  // names that must still pass.
  sessionLog(f, 'bad', [record('bad', 'start', process.pid, 'someone@example.test')]);
  fs.writeFileSync(path.join(f.claudeDir, 'bad.jsonl'), jsonl([
    { type: 'agent-setting', agentSetting: 'C:/outside/private.txt' },
    { type: 'assistant', timestamp: new Date().toISOString(), message: { id: 'b1', model: 'notes.txt', usage: usage(1, 1, 0, 0) } },
  ]));
  const subs = path.join(f.claudeDir, 's', 'subagents');
  fs.mkdirSync(subs, { recursive: true });
  for (const [id, type] of [['agent-a', '/home/someone/.ssh/id_rsa'], ['agent-b', 'codex:codex-rescue'], ['agent-c', 'rm -rf x']]) {
    fs.writeFileSync(path.join(subs, `${id}.jsonl`), jsonl([prompt()]));
    fs.writeFileSync(path.join(subs, `${id}.meta.json`), JSON.stringify({ agentType: type, finished: false }));
  }
  const at = new Date().toISOString();
  fs.writeFileSync(path.join(f.codexDir, 'run.jsonl'), jsonl([
    { at, event: 'start', role: 'C:/outside/role.txt', issue: 47, pr: 49 },
    { at, event: 'helper-started', threadId: 'h1', name: 'C:/outside/private.txt', model: 'cat private.txt', effort: 'someone@example.test', status: null },
    { at, event: 'helper-started', threadId: 'h2', name: 'root/explorer_1', model: 'gpt-6.1-sol', effort: 'xhigh', status: null },
  ]));
  const watch = D.studio({ ...f.dirs });
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
    const s = JSON.parse(state.body);
    assert.deepStrictEqual(s.claude.find((a) => a.name === 'producer').tokens, { input: 1, output: 2, cache: 7, total: 10 });
    for (const bad of ['private.txt', 'cat ', '@example', 'C:/', '/home/', 'id_rsa', 'notes.txt', 'rm -rf', 'role.txt']) {
      assert.ok(!state.body.includes(bad), `${bad} reached the page`);
    }
    assert.deepStrictEqual(s.claude.map((a) => [a.name, a.model]).sort(), [['codex:codex-rescue', null], ['main session', null], ['producer', 'claude-opus-5-5'], ['subagent', null], ['subagent', null]]);
    assert.deepStrictEqual(s.codex.map((r) => [r.role, r.helpers.map((h) => [h.name, h.model, h.effort])]), [[null, [['helper', null, null], ['explorer_1', 'gpt-6.1-sol', 'xhigh']]]]);
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
