# Claude staff

The Claude Code members of the studio, when to use each one, and the guardrails that keep every Claude session inside the rules. This page adds detail to [AGENTS.md](../../AGENTS.md) and never overrides it.

## Who they are

The Producer is the main Claude Code session. It delegates to three subagents. All four are defined in `.claude/agents/`:

| Agent | Model / effort | Writes | Use it when |
|---|---|---|---|
| `producer` (main session) | `claude-opus-5-5` / high | No files. It creates Issues, comments, branches, commits, PRs and board updates. | Any owner request: intake, planning, delegation, integration, review routing and status reports |
| `game-designer` | `claude-opus-5-5` / high | Markdown under `docs/design/` | An approved Issue needs the GDD, units, factions, economy, balance numbers or acceptance criteria written or updated |
| `tech-architect` | `claude-opus-5-5` / xhigh | Proposed ADRs (`docs/adr/NNNN-title.md`) | A significant technical decision is needed (unit simulation, pathfinding, AI structure, rendering, folder layout, packages, Unity upgrades), or an engineer needs technical guidance |
| `code-reviewer` | `claude-opus-5-5` / high | nothing | A PR needs its tier's review: Codex-written work at Standard or Critical tier (AGENTS.md §6) |

What each one never does:
- **producer** never adds, removes or creates the `approved` label, never merges, never approves on GitHub, and doesn't author content. It has no Edit or Write tool and doesn't write files through the shell. It can delegate only to the three agents below. It starts work only on an open Issue with `approved`, and it checks that with `gh issue view`.
- **game-designer** never touches code, `Game/` or `Art/`, and has no shell.
- **tech-architect** never implements features, never edits the ADR template or an accepted ADR, and has no shell.
- **code-reviewer** never edits, commits, pushes, approves on GitHub or merges. It runs one read command per call (`gh pr view|diff|checks`, `gh issue view`, `git diff|log|show`), and posts reviews only with `gh pr comment <n> --body-file -` and a quoted here-document.

The game-designer and tech-architect have no shell, so the Producer creates the branch, commits their docs and opens the PR (one Issue, one branch, one PR).

Codex roles (gameplay-programmer, qa-test-engineer, tech-artist) get a `Codex handoff` comment on their Issue. The owner then starts the named Codex role. The comment never replaces the `approved` label, and the Issue body wins if the two disagree. Claude-written PRs get a read-only review prompt for Codex, which the owner runs.

### How to start the Producer
From the repository root:
```sh
claude --agent producer --effort high
```
The agent file sets the model and tools for the whole session, but under `--agent` Claude Code doesn't apply the file's `effort`. Without `--effort high` the session runs at the model's default (medium for Opus 5.5). Tell it what you want in plain language. It ends each session with the status report from `docs/studio/workflow.md`.

### Example prompts for the Producer
- "Use the game-designer agent on Issue #12: draft the Allied infantry roster in `docs/design/units.md`."
- "Use the tech-architect agent on Issue #15: write an ADR that compares DOTS/ECS with pooled GameObjects for 5,000 units."
- "Use the code-reviewer agent on PR #20 for Issue #18. Return the review to me; don't post it yet."

The model, effort and tools for each agent live in its frontmatter. Change them only with a Critical-tier PR. After an agent file changes, restart the session (or check `/agents`) so the new definition loads.

## Guardrails

| Layer | Where | What it does |
|---|---|---|
| Guard hook | Managed: `C:\Program Files\ClaudeCode\` (source: `.claude/hooks/guard.js`) | Checks every shell command and file edit before it runs, blocks what AGENTS.md §5 forbids, and keeps each agent in its lane. It runs as a managed hook with `onFailure: "block"`, so it fails closed. |
| Permission mode | `.claude/settings.json` | `defaultMode: default`, so anything not on the allow list asks the owner first. Bypass mode and auto mode are disabled. |
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

Reading them is fine. Agents describe proposed changes in the PR, and the owner makes them by hand.

**Skipping hooks:** `--no-verify` or any abbreviation of it, and `git commit -n`, including clusters like `-anm`.

**Commands that run the git hooks** (`git commit`, `merge`, `pull`, `push`) must be the only command in their call, with nothing before them. Tool calls don't keep environment changes, so nothing can change the environment git and gitleaks start with. Run `git add` and `git commit` as two calls.

**Pushes:** the only form allowed is `git push [-u] origin <branch>:refs/heads/<branch>`, never to `main`.
- Example: `git push -u origin HEAD:refs/heads/feat/42-box-selection`.
- Bare or implicit pushes, force pushes, deletions, and pushes to other remotes are blocked.

**git:**
- any config change: `git -c`, `--config-env`, config writes, and `GIT_*` environment variables
- remote changes: only `git remote -v`, `git remote get-url` and `git remote show` are allowed
- aliases and non-built-in git commands
- forms that run other programs: `rebase -x`, `bisect run`, `submodule foreach`, `--ext-diff`, `--upload-pack`
- `git diff|log|show --output`

**gh:**
- `gh pr merge`, any `gh label` command, the `approved` label, `gh pr review --approve`, and editing or deleting comments. Combined short flags are read the way gh reads them (`-iX PUT` is `-i -X PUT`).
- `gh auth` and `gh secret`, repository settings, and gh config, alias and extension changes
- aliases and extensions
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

When the guard blocks a call, Claude sees the reason and the AGENTS.md section, and it must not retry in another form.

**Lanes:** the guard reads `agent_type` from each call. The game-designer, tech-architect and code-reviewer have lanes, and the guard applies them even when the Producer spawns them.
- The Producer (`agent_type: producer`) has no extra lane, so the rules above apply to it as they do to any session.
- Its Issue, comment, board, branch, commit, push and PR commands, and `git merge origin/main`, need no special allowance.
- Tests pin both what it may do and what stays blocked.

### Known limits
- **The guard is a guardrail, not a sandbox.** An agent that writes a script file and runs it (`node x.js`, `python x.py`) isn't inspected. Default mode prompts the owner for that, because it isn't allow-listed. Read those prompts.
- **Owner-only files are protected by name, not by path, in shell commands.** The guard reads file names in a command as text. A wildcard (`set?ings.json`) or a recursive delete of a parent folder (`Remove-Item -Recurse .claude`) can still reach them. Edit and Write calls are checked by their real path. This is an accepted limitation: the owner decided against protecting these files with Windows permissions (Issue #11, closed), so it rests on the guard, plus the CI scan and the `main` ruleset for anything that reaches GitHub.
- **The environment rules are partly best-effort.** What holds is structural:
  - The hook-running git commands run alone, and calls don't keep environment changes.
  - The gitleaks hook itself ignores `GITLEAKS_*`, pins its config and checks the gitleaks binary (`docs/setup-git-hooks.md`).
- **Server-side controls give the hard guarantees:**
  - The CI gitleaks scan (`.github/workflows/gitleaks.yml`) runs on every PR and push to `main`.
  - Making it a required check, and adding a `main` ruleset (PR required, no direct or force pushes, no deletion), are owner settings. Both are described in `docs/setup-git-hooks.md`.
- **Some legitimate commands are blocked too.** The guard prefers a false block over a miss. Ask the owner when a block is wrong. The fix is a reviewed change to the guard, never a workaround.
- **Labels can't be protected on GitHub.** All agents act through the owner's account, so only the guard and the Issue timeline show who added the `approved` label.

## How to test the guardrails

1. **Unit tests** (about 325 cases, around 20 seconds). They cover every finding from the PR #9 review rounds, except the known limit above, and the Producer's commands:
   ```sh
   node --test .claude/hooks/guard.test.js
   ```
2. **Failure mode.** With `onFailure: "block"`, Claude Code 2.1.303 blocked the call when the hook's executable was missing, when the script had a syntax error, and when the script timed out. These were tested live while preparing PR #9.
3. **Live check after installing.** In a new Claude Code session, ask Claude to run each of these, through both the Bash and the PowerShell tool. Each should be refused with `Blocked by the studio guard: …`:
   - `git commit --allow-empty -m test --no-verify`
   - `git push origin HEAD:main`
   - `gh label list`
   - `gh issue edit <n> --add-label approved`
   - an edit to `.claude/settings.json`
4. **Lanes.**
   - Ask the game-designer to write `docs/design/x.cs`.
   - Ask the tech-architect to edit `docs/adr/0000-adr-template.md`.
   - Ask the code-reviewer to run `git status`.

   All three should be blocked.
5. **Producer startup.** Start `claude --agent producer --effort high` on the branch to check, and confirm:
   - `/model` or `/status` shows `claude-opus-5-5` at high effort.
   - It has no Edit or Write tool: asking it to edit a file is refused for lack of the tool, not by the guard.
   - It can delegate only to game-designer, tech-architect and code-reviewer: asking it to spawn `general-purpose` or `Explore` is refused.
   - Asking it to add `approved` to an Issue is blocked by the guard.
6. **Configuration.**
   - `/agents` lists the four agents.
   - `/hooks` shows the managed PreToolUse guard.
   - `/permissions` shows the allow and deny rules.
   - `git check-ignore .claude/settings.local.json` prints the path.
