# Studio live dashboard

A page on this PC that shows, live and in one place, the Claude sessions and subagents and the Codex launcher runs and their helper agents, with token use per agent (Issue #47).

## Run it

From the repository root, in your own terminal:

```
node tools/studio-dashboard.js
```

Open the address it prints, `http://127.0.0.1:4317/`. If that port is taken, add `--port <number>`. Stop it with Ctrl+C.

It uses Node.js only: no packages, and the page loads nothing from the internet.

## What it shows

- **Only agents that start after you open it.** The dashboard shows only agents that start at or after the moment it starts, with their subagents and helpers. Anything that started earlier isn't shown, even if it's still running, and nothing is kept after you stop it.
  - **A Claude session** appears from a start record timed at or after that moment. A session that was already open appears once it writes a new start record: when it's restarted, resumed, cleared or compacted.
  - **A Codex run** appears from a launcher start entry timed at or after that moment.
  - **Subagents and helpers** appear only under a shown session or run. A subagent appears once it has a message timed after its session appeared.
  - **Only record times decide.** Whether an agent appears depends only on the times in its records. It never depends on when a log was read, on file times, or on a log that couldn't be read at first. Lines added after an agent ended, such as the record the launcher posts, never bring it back.
  - "Last activity" is the time of the last message.
- **Claude:** each session, with its subagents under it. Each row shows:
  - the name:
    - the agent type for a session started with `--agent` (such as `producer`) or for a subagent
    - otherwise the agent named in the session's own log for its current run
    - otherwise "main session"
  - the model, the start time and the last activity time. A session that was already open shows the start record it appeared from, such as its compaction.
  - the state:
    - **running**: a session is running from its start record until an end record from the same Claude Code process, or until that process is gone. Both records are written by the session hooks (#50, `docs/studio/claude-staff.md`).
      - A late end record from an earlier process doesn't end a resumed session.
      - A session that starts after the dashboard opened always appears, even if it ended before the next update.
      - A subagent is running until it finishes or its session does. A subagent that ran after its session appeared always appears, even if the session ended and was resumed before the next update.
      - When a session is resumed, subagents from its earlier runs stay finished unless they do something new. If a subagent's info file can't be read for a moment, it keeps its last known state.
    - **finished**: a session that has ended or whose process is gone, or a subagent that has finished
  - the tokens: input, output, cache (read and written) and total. Claude Code writes one reply as several log records, so each reply counts once.
  - Only activity from when the session appeared counts, for the session and its subagents alike. Earlier activity in the same logs isn't counted.
- **Sessions without a start record after the dashboard opened don't appear.** To see a session that was already open, restart or resume it. A record cut off by the hook's 1-second timeout is missing too.
- **Codex:** each launcher run, with its role, Issue and PR, start and end times, state and token totals.
  - The state is running, finished, failed, or stopped. Failed means the run ended with an error or its turn didn't complete, and no error text is shown. Stopped means it passed the launcher's time limit with no end, so its launcher went away.
  - Each helper agent Codex starts is shown under its run, with its name, model and effort, times, state and tokens. A helper stops when its run does.
  - These come from the launcher's log entries (#46). A count Codex didn't report shows as `–`.
- The page updates every 2 seconds. A large log is caught up within moments: while a log has more to read, the dashboard keeps reading in bounded steps and answers the page between them.

## What it reads, and what it never shows

- It reads only:
  - `.studio/logs/` in the repository, for the launcher's run logs
  - `.studio/sessions/` in the repository, for each Claude session's start and end records
  - this repository's Claude Code session logs, in `.claude\projects\<repo folder>\` in your user folder: the session logs and their `subagents\` folders
- It reads a file only if its real location, after following any link or junction, is inside one of those folders.
- It reads a folder only if that folder really is at its place in the repository or your user folder. A folder that is itself a link or junction to somewhere else isn't read.
- It checks whether a recorded Claude process id is still running, and reads nothing else about the process.
- It reads nothing else outside the repository: not `~/.claude.json`, and not Codex's own folder.
- A log line that never ends is dropped once it passes 16 MiB, so memory use stays bounded.
- It is read-only: it writes, changes and deletes nothing.
- It reduces every log record to names, times, states and numbers as it reads it. Conversation text, prompts, tool input and output, commands, file paths and file contents never reach the page.
- Each name has its own strict check, and a value that fails is dropped. So nothing that looks like a file path, a command or an email address is shown:
  - agent names: a plain agent type, optionally with a plugin prefix, or "main session"
  - models: an id where a dot may only sit between digits
  - effort: a known level
  - roles: the four launcher roles
  - Codex helpers: only the last part of the helper's agent path (`root/worker` shows as `worker`)
- One log that can't be read for a moment is tried again on the next read. It doesn't stop the other agents from updating, or the dashboard from starting.
- It answers only on `127.0.0.1`, so other devices can't reach it. It also refuses requests that don't address it by `127.0.0.1` or `localhost`, which stops websites from reading it through your browser.
