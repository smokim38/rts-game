'use strict';
// Runs session-marker.js the way Claude Code does (JSON on stdin), from a copy inside a
// throwaway repository, and checks what it writes. Every run must exit 0 and print nothing.
// Run with: node --test .claude/hooks/session-marker.test.js

const test = require('node:test');
const assert = require('node:assert');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const { spawn, spawnSync } = require('node:child_process');

const SCRIPT = path.join(__dirname, 'session-marker.js');
const ID = 'a1b2c3d4-0000-4000-8000-123456789abc';
const KEYS = ['session', 'agent', 'event', 'at', 'pid'];

const temps = [];
const tempDir = () => {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'session-marker-test-'));
  temps.push(dir);
  return dir;
};
test.after(() => {
  for (const dir of temps) fs.rmSync(dir, { recursive: true, force: true });
});

// A repository with its own copy of the script, and .studio/ as the launcher leaves it.
function repo({ gitignore = true } = {}) {
  const root = tempDir();
  fs.mkdirSync(path.join(root, '.claude', 'hooks'), { recursive: true });
  fs.copyFileSync(SCRIPT, path.join(root, '.claude', 'hooks', 'session-marker.js'));
  fs.mkdirSync(path.join(root, '.studio'));
  if (gitignore) fs.writeFileSync(path.join(root, '.studio', '.gitignore'), '*\n');
  return root;
}

// This test may itself run under Claude Code, so CLAUDE_PID is always set or removed explicitly.
function env(pid) {
  const vars = { ...process.env };
  delete vars.CLAUDE_PID;
  if (pid !== null) vars.CLAUDE_PID = pid;
  return vars;
}

const scriptIn = (root) => path.join(root, '.claude', 'hooks', 'session-marker.js');

function run(root, input, pid = '4242') {
  const result = spawnSync(process.execPath, [scriptIn(root)], {
    input: typeof input === 'string' ? input : JSON.stringify(input),
    encoding: 'utf8',
    env: env(pid),
    timeout: 5000,
  });
  assert.strictEqual(result.status, 0);
  assert.strictEqual(result.stdout, '');
  assert.strictEqual(result.stderr, '');
}

const studioDir = (root) => path.join(root, '.studio');
const sessionsDir = (root) => path.join(root, '.studio', 'sessions');
const raw = (root, id = ID) => fs.readFileSync(path.join(sessionsDir(root), `${id}.jsonl`), 'utf8');
const records = (root, id = ID) => raw(root, id).trim().split('\n').map((line) => JSON.parse(line));
const hook = (name, extra = {}) => ({ session_id: ID, hook_event_name: name, ...extra });

test('writes a start and an end record for a session', () => {
  const root = repo();
  run(root, hook('SessionStart', { agent_type: 'producer', source: 'startup' }));
  run(root, hook('SessionEnd', { reason: 'other' }));
  const [start, end] = records(root);
  assert.deepStrictEqual(Object.keys(start), KEYS);
  assert.deepStrictEqual(Object.keys(end), KEYS);
  assert.deepStrictEqual([start.session, start.agent, start.event, start.pid], [ID, 'producer', 'start', 4242]);
  assert.deepStrictEqual([end.session, end.agent, end.event, end.pid], [ID, 'main session', 'end', 4242]);
  for (const rec of [start, end]) assert.strictEqual(new Date(rec.at).toISOString(), rec.at);

  run(root, hook('SessionStart', { session_id: 'other-session', agent_type: 'not a name' }));
  assert.strictEqual(records(root, 'other-session')[0].agent, 'main session');
});

test('uses the parent process id when CLAUDE_PID is missing or not a number', () => {
  const root = repo();
  run(root, hook('SessionStart'), null);
  run(root, hook('SessionStart'), '12abc');
  run(root, hook('SessionStart'), '0');
  assert.deepStrictEqual(records(root).map((rec) => rec.pid), [process.pid, process.pid, process.pid]);
});

test('keeps only the allowed fields', () => {
  const root = repo();
  const planted = {
    prompt: 'PLANTED prompt',
    transcript_path: 'C:\\PLANTED\\transcript.jsonl',
    cwd: 'C:\\PLANTED\\repo',
    session_title: 'PLANTED title',
    model: 'PLANTED-model',
    permission_mode: 'PLANTED',
    source: 'PLANTED',
    reason: 'PLANTED',
  };
  run(root, hook('SessionStart', planted));
  run(root, hook('SessionEnd', planted));
  assert.ok(!raw(root).includes('PLANTED'));
  for (const rec of records(root)) assert.deepStrictEqual(Object.keys(rec), KEYS);
});

test('ignores a session id that is not a plain id', () => {
  const root = repo();
  for (const id of ['../x', '..\\x', 'a/b', 'a.b', 'a b', '', 'x'.repeat(129), 42, null]) {
    run(root, hook('SessionStart', { session_id: id }));
  }
  run(root, { hook_event_name: 'SessionStart' });
  assert.deepStrictEqual(fs.readdirSync(studioDir(root)), ['.gitignore']);
  assert.deepStrictEqual(fs.readdirSync(root), ['.claude', '.studio']);
});

test('writes nothing without .studio/.gitignore', () => {
  const root = repo({ gitignore: false });
  run(root, hook('SessionStart'));
  assert.deepStrictEqual(fs.readdirSync(studioDir(root)), []);

  fs.rmdirSync(studioDir(root));
  run(root, hook('SessionStart'));
  assert.deepStrictEqual(fs.readdirSync(root), ['.claude']);
});

test('ignores subagents and other events', () => {
  const root = repo();
  run(root, hook('SessionStart', { agent_id: 'agent-1', agent_type: 'Explore' }));
  run(root, hook('SessionEnd', { agent_id: 'agent-1' }));
  for (const name of ['PreToolUse', 'Stop', 'SubagentStart', 'sessionstart', ['SessionStart'], undefined]) {
    run(root, hook(name));
  }
  assert.deepStrictEqual(fs.readdirSync(studioDir(root)), ['.gitignore']);
});

test('exits 0 and writes nothing when it fails', () => {
  const root = repo();
  for (const input of ['', '{', 'null', '"SessionStart"', JSON.stringify({ ...hook('SessionStart'), prompt: 'x'.repeat(70 * 1024) })]) {
    run(root, input);
  }
  assert.deepStrictEqual(fs.readdirSync(studioDir(root)), ['.gitignore']);

  fs.writeFileSync(sessionsDir(root), 'not a folder');
  run(root, hook('SessionStart'));
  assert.strictEqual(fs.readFileSync(sessionsDir(root), 'utf8'), 'not a folder');

  const other = repo();
  fs.mkdirSync(path.join(sessionsDir(other), `${ID}.jsonl`), { recursive: true });
  run(other, hook('SessionStart'));
  assert.deepStrictEqual(fs.readdirSync(path.join(sessionsDir(other), `${ID}.jsonl`)), []);
});

test('refuses links that lead outside the repository', () => {
  const outside = tempDir();
  const root = repo();
  fs.symlinkSync(outside, sessionsDir(root), 'junction');
  run(root, hook('SessionStart'));
  assert.deepStrictEqual(fs.readdirSync(outside), []);

  const studioOutside = tempDir();
  fs.writeFileSync(path.join(studioOutside, '.gitignore'), '*\n');
  const linked = repo();
  fs.rmSync(studioDir(linked), { recursive: true });
  fs.symlinkSync(studioOutside, studioDir(linked), 'junction');
  run(linked, hook('SessionStart'));
  assert.deepStrictEqual(fs.readdirSync(studioOutside), ['.gitignore']);
});

test('stops within a second when stdin stays open', async () => {
  const root = repo();
  const started = Date.now();
  const child = spawn(process.execPath, [scriptIn(root)], { env: env('4242') });
  let output = '';
  child.stdout.on('data', (chunk) => { output += chunk; });
  child.stderr.on('data', (chunk) => { output += chunk; });
  child.stdin.write(JSON.stringify(hook('SessionStart')).slice(0, 20));
  const code = await new Promise((resolve) => child.on('close', resolve));
  assert.strictEqual(code, 0);
  assert.strictEqual(output, '');
  assert.ok(Date.now() - started < 3000, 'took too long');
  assert.deepStrictEqual(fs.readdirSync(studioDir(root)), ['.gitignore']);
});
