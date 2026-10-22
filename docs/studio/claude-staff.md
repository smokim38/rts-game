# Claude staff

The Claude Code members of the studio, when to use each one, and the guardrails that keep every Claude session inside the rules. This page adds detail to [AGENTS.md](../../AGENTS.md) and never overrides it.

## Who they are

The Producer is the main Claude Code session. It delegates to three subagents. All four are defined in `.claude/agents/`:

| Agent | Model / effort | Writes | Use it when |
|---|---|---|---|
| `producer` (main session) | `claude-opus-5-5` / high | No files. It creates Issues, comments, branches, commits, PRs, merges and board updates, and starts Codex through the launcher. | Any owner request: intake, planning, delegation, integration, review routing, merging and reports |
| `game-designer` | `claude-opus-5-5` / high | Markdown under `docs/design/` | An authorised Issue needs the GDD, units, factions, economy, balance numbers or acceptance criteria written or updated |
| `tech-architect` | `claude-opus-5-5` / xhigh | Proposed ADRs (`docs/adr/NNNN-title.md`) | A significant technical decision is needed (unit simulation, pathfinding, AI structure, rendering, folder layout, packages, Unity upgrades), or an engineer needs technical guidance |
| `code-reviewer` | `claude-opus-5-5` / high | nothing | A PR needs its tier's review: Codex-written work at Standard or Critical tier (AGENTS.md §6) |

What each one never does:
- **producer** never approves on GitHub, never posts verdict or QA records, never merges outside the merge gate, and doesn't author content. It has no Edit or Write tool, and the guard limits its shell to `git`, `gh`, the merge form and the launcher form. It can delegate only to the three agents below and start Codex only through the launcher. It starts work only on an authorised Issue (AGENTS.md §3).
- **game-designer** never touches code, `Game/` or `Art/`, and has no shell.
- **tech-architect** never implements features, never edits the ADR template or an accepted ADR, and has no shell.
- **code-reviewer** never edits, commits, pushes, approves on GitHub or merges. It runs one read command per call (`gh pr view|diff|checks`, `gh issue view`, `git diff|log|show`), and posts its verdict record only with `gh pr comment <n> --body-file -` and a quoted here-document.

The game-designer and tech-architect have no shell, so the Producer creates the branch, commits their docs and opens the PR (one Issue, one branch, one PR).

Codex roles (gameplay-programmer, qa-test-engineer, tech-artist, and the read-only `reviewer`) run only through the studio launcher, `node tools/codex-launch.js <role> <issue> [<pr>]`, which only the Producer may start. See "The Codex launcher" below. PRs are reviewed by tier (AGENTS.md §6):
- At Standard and Critical tier, Codex-written work gets the `code-reviewer`, and Claude-written work gets the launcher's Codex `reviewer`. Mixed work gets both.
- A Light-tier PR (docs only) gets only the author's self-check.
- An Owner-tier PR gets the owner's "approved", relayed by the Producer.

### The Codex launcher
`tools/codex-launch.js` (Node.js built-ins only; tests: `node --test tools/codex-launch.test.js`):
1. Checks the inputs (fixed roles; `qa-test-engineer` and `reviewer` also need a PR), the authority rule (open, opened by `smokim38`, an `### Owner instruction` with a quote and a date, no body edits by other accounts) and the role's WIP slot. Lock files under `.studio/locks/` that name the launcher's process make the slot claim atomic and allow one run per worktree at a time. A lock left by a process that has ended is taken over under a second, short-lived lock, so only one run recovers it; a run that finds a recovery under way refuses. A run removes only its own lock.
2. Reads the role file from `main` on GitHub, so a local edit can't change a role's model, effort or instructions.
3. Prepares a worktree under `.studio/worktrees/` (git-ignored): the Issue's branch for implementation roles, or a detached checkout of the PR's exact head for QA and review.
4. Runs Codex unattended through `codex app-server`: `workspace-write` (`read-only` for `reviewer`), approval policy `on-request`, approvals reviewer `auto_review`, the role's model and effort. Before the turn starts, it checks Codex's own thread/start reply reports exactly these settings, and stops if any differ. Later in the run, a settings change that departs from them, or a reroute to another model, also stops the run, and nothing is posted. Codex reports `auto_review` under its legacy name `guardian_subagent` in some versions, and the launcher accepts both. Any approval request that reaches the launcher (auto-review didn't answer it) is declined. The launcher never approves anything.
   - Codex, git and gh run by absolute path, from absolute PATH entries outside the repository, and never through a shell. On Windows the launcher runs the npm package's entry script (`@openai/codex/bin/codex.js`) with node, because Windows would otherwise look for a program in the worktree, which holds PR content, before PATH.
5. Logs the run under `.studio/logs/`, one JSON line per event as it happens: the settings Codex reported, model reroutes, every auto-review decision, commands and file changes, and:
   - `token-usage`: each token report from Codex for the run's thread or a helper agent's thread (input, cached input, cache write, output, reasoning output, total, and the context window)
   - `helper-started`, `helper-updated`, `helper-ended` and `helper-resumed`: each helper agent Codex starts, by thread id, with its name, model and effort when Codex gives them. The model and effort Codex reports it used replace the ones the spawn call asked for. Whether a helper is working or finished follows only Codex's activity reports for it, each counted once: `started` or `interacted` after a finish is logged as resumed, and `completed` or `interrupted` as ended. Collab calls (messages, input, waits, failed calls) never change it, because their state snapshots can be stale. A helper still working when the run ends finishes with it (`run-ended`). `helper-ended` carries the helper's latest token total.
   - `run-tokens`, just before `end`: the run's total and each helper's total

   A count Codex doesn't report is `null`, never estimated. These entries hold only identifiers, names, model, effort and numbers, never prompts, messages or model output.
6. Prints a short summary, including the run's tokens and each helper's ("not reported" when Codex gave none). For QA and review runs, it posts the record for the commit it checked out.

Verified on 2026-10-20 with Codex CLI 0.163.0: thread/start reported the requested model, effort, `workspaceWrite` with no network, `on-request` and `auto_review`, and a network escalation was answered by auto-review (approved, low risk) with nothing reaching the launcher.

### Session records
Claude Code runs `.claude/hooks/session-marker.js` when a session starts and when it ends (the `SessionStart` and `SessionEnd` hooks in `.claude/settings.json`; tests: `node --test .claude/hooks/session-marker.test.js`).
- **The record:** each event appends one line to `.studio/sessions/<session id>.jsonl`. The line holds:
  - the session id
  - the agent name (the agent type for a session started with `--agent`, such as `producer`, otherwise `main session`)
  - `start` or `end`, and the time
  - the process id of the Claude Code session, so a reader can check whether it is still running
- **When:** a session also gets a start record when it is resumed, cleared or compacted. Subagents get none.
- **What it never writes:** prompts, transcript paths, the working folder, user names or any other text.
- **Where:** only inside `.studio/sessions/`, and only when `.studio/.gitignore` exists (the launcher creates it), so the records are never committed. A session id that isn't a plain id, or a folder that is a link, gets no record.
- **Never in the way:** it prints nothing and always exits successfully. It never delays a session by more than 1 second: Claude Code stops the hook after 1 second (`"timeout": 1` in `.claude/settings.json`), and the script normally stops itself sooner.
- **A record can be missing:** when the timeout stops the hook, for example while the disk is stalled, that start or end record is missing or its line is incomplete. A reader should skip lines it can't parse, and treat a session with no end record as running only while its process id is alive.

### How to start the Producer
From the repository root:
```sh
claude --agent producer --effort high
```
The agent file sets the model and tools for the whole session, but under `--agent` Claude Code doesn't apply the file's `effort`. Without `--effort high` the session runs at the model's default (medium for Opus 5.5). Tell it what you want in plain language. It ends each session with the status report from `docs/studio/workflow.md`.

### Example prompts for the Producer
- "Use the game-designer agent on Issue #12: draft the Allied infantry roster in `docs/design/units.md`."
- "Use the tech-architect agent on Issue #15: write an ADR that compares DOTS/ECS with pooled GameObjects for 5,000 units."
- "Use the code-reviewer agent on PR #20 for Issue #18." It posts its verdict record on the PR.

The model, effort and tools for each agent live in its frontmatter. Change them only with a Critical-tier PR. After an agent file changes, restart the session (or check `/agents`) so the new definition loads.

## Guardrails

| Layer | Where | What it does |
|---|---|---|
| Guard hook | Managed: `C:\Program Files\ClaudeCode\` (source: `.claude/hooks/guard.js`) | Checks every shell command and file edit before it runs, blocks what AGENTS.md §5 forbids, and keeps each agent in its lane. It runs as a managed hook with `onFailure: "block"`, so it fails closed. |
| Permission mode | `.claude/settings.json` | `defaultMode: default`, so anything not on the allow list asks the owner first. Bypass mode is disabled; auto mode is allowed. |
| Allow list | `.claude/settings.json` | Routine reads run without a prompt: `git status/diff/log/show`, `gh issue list/view`, `gh pr list/view/diff/checks`, `gh project item-list`. The guard still checks them. |
| Deny list | `.claude/settings.json` | Simple patterns for what AGENTS.md §5 forbids, for both the Bash and the PowerShell tool. |
| Git hooks | `.githooks/` | gitleaks scans every commit and merge commit (see `docs/setup-git-hooks.md`). |

### Why the guard is installed outside the repo
A guard in the repo could be edited by the agents it restrains, and Claude Code reloads project settings as soon as they change. So the guard in force is an owner-installed managed hook:
- Only administrators can write `C:\Program Files\ClaudeCode\`.
- Project and local settings can't disable a managed hook.
- `.claude/hooks/guard.js` in the repo is only the reviewed source. Changing it is a proposal, and nothing changes until the owner reinstalls after the merge.

The guard applies to every Claude Code session on the PC. A session opened in another folder can still reach this repository by path, so there is no scope check to get around. Other projects get the same rules.

**Install or update (owner only, as administrator, after each merged guard change):**
```powershell
pwsh -File tools/install-claude-guard.ps1                     # installs from origin/main
pwsh -File tools/install-claude-guard.ps1 -Ref origin/<branch>  # try a PR before merging
```
The script installs the committed version from the ref, never the working tree, and checks that the installed file has the same git object ID as the commit. It requires Claude Code 2.1.295 or later and node under Program Files, and it is idempotent. Restart Claude Code afterwards.

### What the guard blocks
**Owner-only files:**
- `.claude/settings.json` and `.claude/settings.local.json`
- `.githooks/`, `.gitleaksignore`, `.git/config` and `.git/hooks/`
- `.mcp.json`
- `~/.claude/settings.json`, `~/.claude.json` and `~/.gitconfig`
- shell profiles (`~/.bashrc`, PowerShell `$PROFILE` and similar) and Claude's session environment file, which set the environment of later calls
- the managed folder

Reading them is fine, except `~/.claude.json` and other credential files, which agents never read (AGENTS.md §5). Agents describe proposed changes in the PR, and the owner makes them by hand.

**Skipping hooks:** `--no-verify` or any abbreviation git accepts, down to `--no-v` (which also blocks a few prefixes of `--no-verbose`), and `git commit -n`, including clusters like `-anm`.

**Commands that run the git hooks** (`git commit`, `merge`, `pull`, `push`) must be the only command in their call, with nothing before them. Tool calls don't keep environment changes, so nothing can change the environment git and gitleaks start with. Run `git add` and `git commit` as two calls.

**Pushes:** the only form allowed is `git push [-u] origin <branch>:refs/heads/<branch>`, never to `main`.
- Example: `git push -u origin HEAD:refs/heads/feat/42-box-selection`.
- Bare or implicit pushes, force pushes, deletions, and pushes to other remotes are blocked.

**git:**
- any config change: `git -c` (also attached, `-ckey=value`), `--config-env`, config writes, `GIT_*` environment variables, and commands that write the global config (`git maintenance` other than `run`, `git lfs install|uninstall`)
- remote changes: only `git remote -v`, `git remote get-url` and `git remote show` are allowed
- aliases and non-built-in git commands
- forms that run other programs: `rebase -x`, `bisect run`, `submodule foreach`, `--ext-diff`, `--textconv`, `--upload-pack`, `--receive-pack`, `--exec`, `ls-remote -u`, `difftool -x`, `grep -O`
- `git clone` and `git init` with `-c`, `-u`, `--config` or `--template` (a template installs git hooks)
- `git diff|log|show --output`
- Options are matched in every form git accepts: attached values (`-ux`), clusters (`-qu x`) and abbreviated long options (`--upl=x`).

**gh:**
- `gh pr merge` in every form except the Producer's merge form (below), any `gh label` command, `gh pr review --approve`, and editing or deleting comments. Combined short flags are read the way gh reads them (`-iX PUT` is `-i -X PUT`).
- record markers (`studio-record`) in comments, except a `verdict` record with `vendor=claude` from the code-reviewer and an `owner-approval` record from the Producer, each as the first line of a single comment. An owner-approval record also needs its second line, quoting the owner's reply and its date (workflow.md).
- `gh auth` and `gh secret`, repository settings, and gh config, alias and extension changes
- aliases and extensions
- arguments after `--`, which gh passes on to git or ssh (`gh repo clone … -- --upload-pack=x`)
- `gh codespace`, `gh copilot` and `gh agent-task`, which open remote shells or start other AI agents
- any `gh api` call that writes:
  - non-GET methods and fields that read files (`--input`, `=@file`)
  - GraphQL, except a literal read query

**Arguments the guard can't read:**
- ANSI-C quoting (`$'…'`)
- variables or expansions in git or gh arguments, or as a command name, including Bash brace expansions (`{a,b}`) and unquoted wildcards. A simple placeholder like `repos/{owner}/{repo}` is fine in Bash; in PowerShell, quote it (`'repos/{owner}/{repo}'`), because PowerShell reads unquoted braces as a script block.
- PowerShell splatting
- encoded PowerShell commands, `cmd /c` and WSL
- git, gh or downloads inside code passed to another shell or interpreter (`bash -c`, `pwsh -Command`, `node -e`, `iex`)

**Downloads and the secret scan:**
- piping a download into a shell
- setting `GITLEAKS_*`, `GIT_*` or `PATH` in any form the guard knows: assignments, `export`, `$env:`, the PowerShell `Env:` drive and Environment provider, `setx`, registry changes, and new PowerShell drives

**Codex, Claude and indirect execution (every session):**
- `codex` by any name or path (`codex.exe`, `codex.js`, `codex@<version>`, `npx codex`), and the `@openai/codex` package by any path, except through the Producer's launcher form
- nested Claude sessions (`claude …`, `claude@<version>`, the `@anthropic-ai/claude-code` package by any path), so no session can act as the Producer
- an assignment or option whose value names either program in any word, also once quotes and escapes are removed (`EDITOR=codex git commit`, `PAGER=claude git log`, `GH_BROWSER="npx @anthropic-ai/claude-code" gh browse`, `GH_BROWSER='npx cl""aude' gh browse`)
- the launcher itself, for anyone but the Producer
- read-command options that run other programs: `rg --pre` and `--pre-glob`, `find -exec`, `-execdir`, `-ok` and `-okdir`, and setting `LESSOPEN` or `LESSCLOSE`
- rewriting refs: `git update-ref`, `git replace` and `git symbolic-ref` writes

When the guard blocks a call, Claude sees the reason and the AGENTS.md section, and it must not retry in another form.

**Lanes:** the guard reads `agent_type` from each call. The game-designer, tech-architect and code-reviewer have lanes, and the guard applies them even when the Producer spawns them. The hooks reference says `agent_type` is present "when the session uses `--agent`", so the Producer's main session carries `agent_type: producer`.

**The Producer (`agent_type: producer`):**
- Its shell runs only `git`, `gh` and `cd` to the repository root, with no wrappers, variable assignments, file redirects or other programs. It reads files with its Read, Grep and Glob tools. All the general rules above still apply.
- Its comments are a literal `--body` or a quoted here-document, so the guard can read them. `--body-file <path>`, `--editor` and `--web` are blocked.
- **The merge form**, as a call of its own: `gh pr merge <n> --repo smokim38/rts-game --squash --delete-branch --match-head-commit <40-hex sha>`. Before allowing it, the guard asks GitHub (read-only, the fixed repository, every page) and blocks unless the AGENTS.md §6 merge gate holds. A failed or incomplete lookup blocks. The linked Issue's labels and body edits are read in one page of 100 each, so an Issue with more than 100 of either blocks the merge.
- **The launcher form**, as a call of its own from the repository root: `node tools/codex-launch.js <role> <issue> [<pr>]`. Before allowing it, the guard checks that `tools/codex-launch.js` is a real file under the project root (no symlink or junction) and that its git blob hash matches the file on `main`, fetched from the GitHub API. It never trusts a local ref such as `origin/main`.
- Tests pin both what it may do and what stays blocked. The GitHub lookups are stubbed in the tests.

### Known limits
- **The guard is a guardrail, not a sandbox.** A session with no role that writes a script file and runs it (`node x.js`, `python x.py`) isn't inspected. Default mode prompts the owner for that, because it isn't allow-listed. Read those prompts. The Producer can't run scripts at all.
- **One account, one Windows user.** The merge gate checks evidence that agents could fabricate by a route the guard can't see. This is an accepted risk (AGENTS.md §5).
- **The merge gate needs the network.** It and the launcher check call GitHub from the hook within its 15-second timeout. If GitHub is slow or down, they block, by design.
- **Owner-only files are protected by name, not by path, in shell commands.** The guard reads file names in a command as text. A wildcard (`set?ings.json`) or a recursive delete of a parent folder (`Remove-Item -Recurse .claude`) can still reach them. Edit and Write calls are checked by their real path. This is an accepted limitation: the owner decided against protecting these files with Windows permissions (Issue #11, closed), so it rests on the guard, plus the CI scan and the `main` ruleset for anything that reaches GitHub.
- **The environment rules are partly best-effort.** What holds is structural:
  - The hook-running git commands run alone, and calls don't keep environment changes.
  - The gitleaks hook itself ignores `GITLEAKS_*`, pins its config and checks the gitleaks binary (`docs/setup-git-hooks.md`).
- **Server-side controls give the hard guarantees:**
  - The CI gitleaks scan (`.github/workflows/gitleaks.yml`) runs on every PR and push to `main`.
  - Making it a required check, and adding a `main` ruleset (PR required, no direct or force pushes, no deletion), are owner settings. Both are described in `docs/setup-git-hooks.md`.
- **Some legitimate commands are blocked too.** The guard prefers a false block over a miss. Ask the owner when a block is wrong. The fix is a reviewed change to the guard, never a workaround.
- **Labels never authorise work.** The `### Owner instruction` section does (AGENTS.md §3), and the launcher checks the Issue's body edits.

## How to test the guardrails

1. **Unit tests.** They cover every finding from the PR #9 review rounds, except the known limit above, the Producer's commands, the merge gate and the launcher check (with GitHub stubbed), and the launcher's own checks:
   ```sh
   node --test .claude/hooks/guard.test.js
   node --test tools/codex-launch.test.js
   ```
2. **Failure mode.** With `onFailure: "block"`, Claude Code 2.1.303 blocked the call when the hook's executable was missing, when the script had a syntax error, and when the script timed out. These were tested live while preparing PR #9.
3. **Live check after installing.** In a new Claude Code session, ask Claude to run each of these, through both the Bash and the PowerShell tool. Each should be refused with `Blocked by the studio guard: …`:
   - `git commit --allow-empty -m test --no-verify`
   - `git push origin HEAD:main`
   - `gh label list`
   - `gh pr merge 1 --squash`
   - `codex --version`
   - an edit to `.claude/settings.json`
4. **Lanes.**
   - Ask the game-designer to write `docs/design/x.cs`.
   - Ask the tech-architect to edit `docs/adr/0000-adr-template.md`.
   - Ask the code-reviewer to run `git status`.

   All three should be blocked.
5. **Producer startup.** Start `claude --agent producer --effort high` on the branch to check, and confirm:
   - `/model` or `/status` shows `claude-opus-5-5` at high effort.
   - It has no Edit or Write tool. Check what Claude Code itself loads, not what the agent reports:
     ```powershell
     claude -p "Reply with OK." --agent producer --effort high --max-turns 1 --output-format stream-json --verbose | Select-Object -First 1
     ```
     The first line is the `init` event.
     - Its `tools` list has Read, Grep, Glob, Bash and `Task` (the Agent tool's older name), and no Edit, Write or NotebookEdit.
     - Its `agents` field lists every defined agent. The three-agent limit applies when the Producer tries to spawn one, so check it with the next item.
     - The event includes local paths with your user name; don't paste it into GitHub.

     Asked to edit a file, the session refuses.
   - It can delegate only to game-designer, tech-architect and code-reviewer: asking it to spawn `general-purpose` or `Explore` is refused.
   - **`agent_type` reaches the guard:** asking it to run `ls` is blocked with the Producer's shell message. If `ls` runs, the hook isn't seeing `agent_type: producer`; stop using the merge and launcher forms and report it.
   - Asking it to merge a PR without a review record is blocked by the merge gate, with the missing evidence named.
6. **Configuration.**
   - `/agents` lists the four agents.
   - `/hooks` shows the managed PreToolUse guard.
   - `/permissions` shows the allow and deny rules.
   - `git check-ignore .claude/settings.local.json` prints the path.
