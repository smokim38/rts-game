# Claude staff

The Claude Code members of the studio, when to use each one, and the guardrails that keep every Claude session inside the rules. This page adds detail to [AGENTS.md](../../AGENTS.md) and never overrides it.

## Who they are

The Producer is the main Claude Code session. It delegates to three subagents defined in `.claude/agents/`:

| Agent | Model / effort | Writes | Use it when |
|---|---|---|---|
| `game-designer` | `claude-opus-5-5` / high | `docs/design/` only | An approved Issue needs the GDD, units, factions, economy, balance numbers or acceptance criteria written or updated |
| `tech-architect` | `claude-opus-5-5` / xhigh | `docs/adr/` only | A significant technical decision is needed (unit simulation, pathfinding, AI structure, rendering, folder layout, packages, Unity upgrades), or an engineer needs technical guidance |
| `code-reviewer` | `claude-opus-5-5` / high | nothing | A PR needs its tier's review: Codex-written work at Standard or Critical tier (AGENTS.md §6) |

What each one never does:
- **game-designer** never touches code, `Game/` or `Art/`, and has no shell.
- **tech-architect** never implements features, never edits an accepted ADR, and has no shell.
- **code-reviewer** never edits, commits, pushes, approves on GitHub or merges. Its shell is limited to `gh pr view|diff|checks|comment`, `gh issue view` and `git diff|log|show`.

The game-designer and tech-architect have no shell, so the Producer creates the branch, commits their docs and opens the PR (one Issue, one branch, one PR).

### Example prompts for the Producer
- "Use the game-designer agent on Issue #12: draft the Allied infantry roster in `docs/design/units.md`."
- "Use the tech-architect agent on Issue #15: write an ADR that compares DOTS/ECS with pooled GameObjects for 5,000 units."
- "Use the code-reviewer agent on PR #20 for Issue #18. Return the review to me; don't post it yet."

The model, effort and tools for each agent live in its frontmatter. Change them only with a Critical-tier PR. After an agent file changes, restart the session (or check `/agents`) so the new definition loads.

## Guardrails

Several layers apply to every Claude session in this repo, subagents included:

| Layer | Where | What it does |
|---|---|---|
| Permission mode | `.claude/settings.json` | `defaultMode: default`, so anything not on the allow list asks the owner first. Bypass mode and auto mode are disabled. |
| Allow list | `.claude/settings.json` | Routine reads run without a prompt: `git status/diff/log/show`, `gh issue list/view`, `gh pr list/view/diff/checks`, `gh project item-list`. |
| Deny list | `.claude/settings.json` | Simple patterns for what AGENTS.md §5 forbids. Each shell rule exists for both the Bash and the PowerShell tool, because Bash rules don't cover PowerShell. |
| Guard hook | `.claude/hooks/guard.js` | A PreToolUse hook that parses each command and blocks forbidden actions, however they are written (details below). It also enforces each agent's lane. |
| Git hooks | `.githooks/` | gitleaks scans every commit and merge commit (see `docs/setup-git-hooks.md`). |

The guard blocks:
- skipping hooks: `--no-verify` or any abbreviation of it, and `git commit -n`, including clusters like `-anm`
- force pushes (`--force*`, `-f`, `+ref`), `--mirror`, `--all`, and remote branch deletion (`--delete`, `-d`, `:branch`)
- pushing to `main`, including a bare `git push` while `main` is checked out
- git config changes, including `git -c core.hooksPath=…` and `GIT_CONFIG_*` variables
- `gh pr merge`, any `gh label` command, adding or removing the `approved` label, and `gh pr review --approve`
- `gh auth`, `gh secret`, repository settings, and any non-GET call through `gh api`
- piping a download into a shell, and encoded PowerShell commands
- any change to `.gitleaksignore`, `.git/config` or `.git/hooks/` (reading them is fine)

The guard sees through quoting, escapes, env prefixes, `git -C`/`-c`, full paths to `git.exe`, the PowerShell `&` operator, and commands nested in `sh -c`, `pwsh -Command` or `$(…)`. When it blocks a call, Claude sees the reason and the AGENTS.md section. Bad input also blocks, so the guard fails closed.

### Known limits
- A command built at run time (string concatenation, variables assembled across commands) or an existing git or gh alias can still get past the guard. Default mode still prompts for anything that isn't allow-listed, so the owner sees these commands.
- Some legitimate commands are blocked too. The guard prefers a false block over a miss. For example, quoted text that runs as code is checked as a command, and the settings deny list blocks any command containing `--no-verify`, even in a commit message. Reword the message, or ask the owner.
- The hook needs `node` on PATH. If node is missing, the hook can't run and doesn't block, and only the settings deny list applies.
- Branch protection for `main` on GitHub is the server-side backstop. It's an owner setting.

## How to test the guardrails

1. **Unit tests** (about 140 cases, around 5 seconds):
   ```sh
   node --test .claude/hooks/guard.test.js
   ```
2. **Live check.** In a new Claude Code session, ask Claude to run each of these. Each should be refused with a reason that starts with `Blocked by .claude/hooks/guard.js` (or by a deny rule):
   - `git commit --allow-empty -m test --no-verify`
   - `git push -f origin <your branch>`
   - `gh label list`
   - `gh issue edit <n> --add-label approved`
   - an edit to `.gitleaksignore`

   Ask for each one through both the Bash and the PowerShell tool.
3. **Lanes.** Ask the game-designer to write a file under `docs/adr/`, and ask the code-reviewer to run `git status`. Both should be blocked.
4. **Configuration.**
   - `/agents` lists the three agents.
   - `/permissions` shows the allow and deny rules.
   - `/hooks` shows the PreToolUse guard.
   - `git check-ignore .claude/settings.local.json` prints the path.
