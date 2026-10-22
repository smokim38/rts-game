#!/usr/bin/env node
'use strict';
// SessionStart and SessionEnd hook (Issue #50). Appends one short record per event to
// .studio/sessions/<session id>.jsonl, so the studio dashboard can tell which Claude sessions are
// running. A record holds only the session id, agent name, event, time and the process id of the
// Claude Code session. Nothing else from the input is kept: no prompts, paths or other text.
// It must never get in a session's way. It prints nothing, because SessionStart output would go
// into the session's context. It stops itself within a second of starting and always exits 0.
// All file work is asynchronous, so the stop timer still fires while the disk is slow.
// Tests: node --test .claude/hooks/session-marker.test.js

const fsp = require('node:fs/promises');
const path = require('node:path');

// The checkout this script lives in, never a path from the input or the environment.
const ROOT = path.resolve(__dirname, '..', '..');
const EVENTS = new Map([['SessionStart', 'start'], ['SessionEnd', 'end']]);
const SESSION_ID = /^[A-Za-z0-9_-]{1,128}$/;
const AGENT_NAME = /^[A-Za-z0-9_-]{1,64}$/;
const PID = /^[1-9][0-9]{0,9}$/;
const MAX_INPUT = 64 * 1024;
// Counted from process start, so node's own startup is inside the one-second budget.
const TIME_LIMIT_MS = 800;

function record(input, env, ppid, now) {
  if (!input || typeof input !== 'object') return null;
  const event = typeof input.hook_event_name === 'string' ? EVENTS.get(input.hook_event_name) : undefined;
  // A subagent runs inside its parent's session, so only the session itself gets records.
  if (!event || input.agent_id != null) return null;
  if (typeof input.session_id !== 'string' || !SESSION_ID.test(input.session_id)) return null;
  const agent = typeof input.agent_type === 'string' && AGENT_NAME.test(input.agent_type) ? input.agent_type : 'main session';
  // Claude Code sets CLAUDE_PID, but the hooks reference doesn't document it. Without it, the
  // parent process is Claude Code itself, because the exec-form hook starts node with no shell.
  const pid = PID.test(env.CLAUDE_PID || '') ? Number(env.CLAUDE_PID) : ppid;
  return { session: input.session_id, agent, event, at: new Date(now).toISOString(), pid };
}

// Writes only inside .studio/sessions/, and only where the launcher has set .studio/ up as
// git-ignored. Links and junctions are refused, so a record can't land outside the repository.
async function write(root, rec) {
  const studio = path.join(root, '.studio');
  if (!(await fsp.lstat(studio)).isDirectory()) return false;
  if (!(await fsp.lstat(path.join(studio, '.gitignore'))).isFile()) return false;
  const dir = path.join(studio, 'sessions');
  try {
    await fsp.mkdir(dir);
  } catch (err) {
    if (err.code !== 'EEXIST') throw err;
  }
  if (!(await fsp.lstat(dir)).isDirectory()) return false;
  const file = path.join(dir, `${rec.session}.jsonl`);
  let stat = null;
  try {
    stat = await fsp.lstat(file);
  } catch (err) {
    if (err.code !== 'ENOENT') throw err;
  }
  if (stat && !stat.isFile()) return false;
  await fsp.appendFile(file, `${JSON.stringify(rec)}\n`);
  return true;
}

function main() {
  const done = () => process.exit(0);
  process.on('uncaughtException', done);
  process.on('unhandledRejection', done);
  setTimeout(done, Math.max(0, TIME_LIMIT_MS - process.uptime() * 1000));
  const chunks = [];
  let size = 0;
  process.stdin.on('error', done);
  process.stdin.on('data', (chunk) => {
    size += chunk.length;
    if (size > MAX_INPUT) done();
    chunks.push(chunk);
  });
  process.stdin.on('end', async () => {
    try {
      const rec = record(JSON.parse(Buffer.concat(chunks).toString('utf8')), process.env, process.ppid, Date.now());
      if (rec) await write(ROOT, rec);
    } catch {
      // A failed record is dropped. The session never sees an error.
    }
    done();
  });
}

if (require.main === module) main();

module.exports = { record, write };
