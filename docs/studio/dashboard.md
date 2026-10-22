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

- **Live only.** An agent appears if it is running when the dashboard starts, or if it starts later, even if it has already finished by the dashboard's next read. Nothing that ended before the dashboard started is shown, and nothing is kept after you stop it.
  - A line added to a log after its agent ended never brings the agent back. Examples are the record the launcher posts after a run, or metadata Claude Code adds.
  - Only a real new start brings an agent back: a session's start record, a run's start, a helper being resumed. Real new activity does too: a message in a subagent's log.
  - "Last activity" is the time of the last message.
- **Claude:** each session, with its subagents under it. Each row shows:
  - the name: the agent type for a session started with `--agent` (such as `producer`) or for a subagent, otherwise "main session"
  - the model, the start time and the last activity time
  - the state:
    - **running**: a session is running from its start record until an end record from the same Claude Code process, or until that process is gone. Both records are written by the session hooks (#50, `docs/studio/claude-staff.md`).
      - A late end record from an earlier process doesn't end a resumed session.
      - A session that starts after the dashboard opened always appears, even if it ended before the next update.
      - A subagent is running until it finishes or its session does. A subagent that ran after the dashboard opened always appears, even if its session ended and was resumed before the next update.
      - When a session is resumed, subagents from its earlier runs stay finished unless they do something new. If a subagent's info file can't be read for a moment, it keeps its last known state.
    - **finished**: a session that has ended or whose process is gone, or a subagent that has finished
  - the tokens: input, output, cache (read and written) and total. Claude Code writes one reply as several log records, so each reply counts once.
- **Sessions without a start record don't appear.** That covers sessions opened before the session hooks were installed: restart them to see them. A record the hook's 1-second timeout cut off is missing too.
- **Codex:** each launcher run, with its role, Issue and PR, start and end times, state and token totals.
  - The state is running, finished, failed, or stopped. Failed means the run ended with an error or its turn didn't complete, and no error text is shown. Stopped means it passed the launcher's time limit with no end, so its launcher went away.
  - Each helper agent Codex starts is shown under its run, with its name, model and effort, times, state and tokens. A helper stops when its run does.
  - A helper that finished before the dashboard started is left out unless Codex gives it more work.
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
- It answers only on `127.0.0.1`, so other devices can't reach it. It also refuses requests that don't address it by `127.0.0.1` or `localhost`, which stops websites from reading it through your browser.
