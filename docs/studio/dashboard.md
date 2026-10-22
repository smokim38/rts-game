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

- **Live only.** An agent appears if it is running when the dashboard starts, or when it starts later. Nothing that ended before the dashboard started is shown, and nothing is kept after you stop it.
- **Claude:** each session, with its subagents under it. Each row shows:
  - the name: `producer` for the Producer session, the subagent type for a subagent, otherwise "main session"
  - the model, the start time and the last activity time
  - the state:
    - **running**: wrote to its log in the last 5 minutes
    - **idle**: a session, or a subagent that hasn't finished, that hasn't written for 5 minutes. A session's log has no end record, so an open session waiting for you shows as idle.
    - **finished**: a subagent that has finished
  - the tokens: input, output, cache (read and written) and total. Claude Code writes one reply as several log records, so each reply counts once.
- **Codex:** each launcher run, with its role, Issue and PR, start and end times, state (running, finished, failed, or stopped if its launcher went away) and token totals. Each helper agent Codex starts is shown under its run, with its name, model and effort, times, state and tokens. These come from the launcher's log entries (#46). A count Codex didn't report shows as `–`.
- The page updates every 2 seconds.

## What it reads, and what it never shows

- It reads only:
  - `.studio/logs/` in the repository, for the launcher's run logs
  - this repository's Claude Code session logs, in `.claude\projects\<repo folder>\` in your user folder: the session logs and their `subagents\` folders
- It reads nothing else outside the repository: not `~/.claude.json`, and not Codex's own folder.
- It is read-only: it writes, changes and deletes nothing.
- It reduces every log record to names, times, states and numbers as it reads it. Conversation text, prompts, tool input and output, commands, file paths and file contents never reach the page.
- It answers only on `127.0.0.1`, so other devices can't reach it. It also refuses requests that don't address it by `127.0.0.1` or `localhost`, which stops websites from reading it through your browser.
