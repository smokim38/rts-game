# Studio workflow

How work moves through the studio day to day. This page adds detail to [AGENTS.md](../../AGENTS.md) and never overrides it. If the two disagree, the handbook wins: raise the conflict with the Producer.

The owner talks only to the Producer: ideas, changes, answers and "approved". Everything else runs autonomously inside the rules and the guard.

## Lifecycle

| # | Step | Who | Board |
|---|---|---|---|
| 1 | Has an idea, a change or an answer, and tells the Producer | Owner | — |
| 2 | Restates it, then drafts the Issue from a form in `.github/ISSUE_TEMPLATE/`, with an `### Owner instruction` section (the owner's words and the date), the `role:*`, `type:*` and `tier:*` labels and the Size field | Producer | Backlog |
| 3 | Checks Ready (below) and that the Issue is authorised (AGENTS.md §3), then assigns the agent: a Claude subagent, or a Codex role through the launcher | Producer | Ready → In progress |
| 4 | Works on one branch with one PR (`<type>/<issue>-<short-name>`). For Codex, the Producer commits the launcher's output and opens the PR | Assigned agent, Producer | In progress → In review |
| 5 | Cross-vendor review by tier (below) | Reviewer | In review |
| 6 | QA check, for PRs that change anything under `Game/` (below) | qa-test-engineer | In review |
| 7 | Merges through the merge gate (below), or asks the owner first for Owner-tier and security-perimeter PRs | Producer | — |
| 8 | Closes the Issue, checks the board, tells the owner what changed in the game and how to try it | Producer | Done |

Rules that follow from the handbook:
- An Issue's goal and acceptance criteria are frozen once work starts. A scope change goes back to the owner, and the Producer records the owner's answer in the Issue as a new owner instruction.
- The Producer may raise a tier. Lowering a tier needs the owner, and the minimum tiers (AGENTS.md §6) apply anyway.
- Nobody starts work on an Issue that isn't authorised. Bug reports and suggestions from staff are only suggestions until the owner asks the Producer for them.

### Review (step 5)

| Tier | Evidence the merge gate needs |
|---|---|
| Light (docs only) | The author's self-check, noted in the PR. No agent review. |
| Owner | An owner-approval record: the owner said "approved" in the Producer session |
| Standard | An Approve from the other vendor: Codex work → Claude `code-reviewer`; Claude work → the launcher's Codex `reviewer`; mixed work → both |
| Critical | As Standard, in depth, plus an owner-approval record for security-perimeter PRs |

- "Docs only" means every changed file is Markdown, or a text file or image under `docs/`. Any other file, including a script under `docs/`, makes the PR at least Standard.
- An owner-approval record is needed whenever the Issue's tier label or the PR's `Tier:` line says Owner, even if the tier that counts ends up higher, and for every security-perimeter PR.

Who wrote what comes from the commit trailers: the Producer commits launcher output with a `Codex-Run: <run id>` trailer in the commit's final trailer block, and every other commit counts as Claude work. A `Codex-Run:` line anywhere else in a commit message, or more than one, blocks the merge, because the author is unclear. Merge commits from `main` don't count.

There is one consolidated review per round, with a verdict of Approve, Request changes or Escalate. Two rounds at most. After that the Producer takes it to the owner and moves the card to Blocked (AGENTS.md §6).

**Every verdict names the commit SHA it reviewed.** An Approve covers that commit only. Any later push, including fixes after a QA failure, needs a new review of the new head, and that review counts as a round.

For gameplay-programmer and tech-artist fix runs, the launcher automatically includes the latest valid request-changes verdicts per vendor and failed QA record for the open PR's head commit as untrusted feedback; the Issue's acceptance criteria still set the scope. An escalate verdict stops the run for an owner decision.

### QA (step 6)

Every PR that changes anything under `Game/` needs a `QA: pass` record for its head commit, whatever the labels say. The Producer runs `node tools/codex-launch.js qa-test-engineer <issue> <pr>` once the review approves. The launcher checks out the PR's exact head commit, and the record names that commit. If the PR moves while QA runs, the new head has no QA record and can't merge. The check takes the QA engineer's one WIP slot until its result is posted. QA:
- runs the relevant tests and a performance check on that commit
- never fixes code itself (AGENTS.md §1)

When QA fails:
1. **Failures within the Issue's acceptance criteria:** the `QA: fail` record lists each one. That record is the bug report, and these failures don't need a new Issue. The Producer moves the card back to In progress, and the same author fixes them on the same branch under the same Issue.
2. After the fix, the PR is reviewed again (step 5, a new round) and QA checks again, both on the new head.
3. **Bugs outside the Issue** (pre-existing, unrelated, or needing new scope): the Producer drafts them as suggestions with the bug form. They become work only when the owner asks for them, and they don't block this PR unless the owner says so.
4. If a fix needs a change to the goal or the acceptance criteria, the Producer asks the owner first (see the rules above).

### Records

Review, QA and owner-approval evidence lives in PR comments from `smokim38` whose first line is exactly:

```text
<!-- studio-record v1 type=<verdict|qa|owner-approval> pr=<n> sha=<40-hex> result=<value> [vendor=<claude|codex>] -->
```

| Type | Results | Posted by |
|---|---|---|
| `verdict` (with `vendor=claude` or `vendor=codex`) | `approve`, `request-changes`, `escalate` | `code-reviewer` (claude) or the launcher's `reviewer` run (codex) |
| `qa` | `pass`, `fail` | the launcher's `qa-test-engineer` run |
| `owner-approval` | `approved` | the Producer, quoting the owner's reply and its date |

An owner-approval record's second line must be exactly:

```text
Owner approval for PR #<n> at <40-hex sha>: "<the owner's reply, which says approved>" (Producer session, <YYYY-MM-DD>)
```

with the same PR and SHA as its marker.

- Only the newest record of each type (and vendor) for the head SHA counts. A newer `request-changes`, `escalate` or `fail` overrides an older pass.
- A record with a malformed first line, one edited after posting, or one that names another PR blocks the merge. Never edit or delete a record; post a new one.
- The guard stops the Producer from posting verdict or QA records, and stops everyone else from posting owner-approval records.

## Unity work

1. Every Issue that creates or changes Unity assets (scenes, prefabs, ScriptableObjects, materials or settings) lists those asset paths in its acceptance criteria.
2. The Producer doesn't start an Issue whose asset list overlaps that of another open, started Issue.
3. No Unity run (builder, tests or build) happens on the ladder PC while a performance ladder runs ([ADR 0004](../adr/0004-unit-simulation-architecture.md#performance-ladder)).
4. Assets are created and changed by builders run in batch mode, following [ADR 0006](../adr/0006-unity-editor-automation.md#rules-for-agents-working-with-unity) and [AGENTS.md §4](../../AGENTS.md#4-engineering-standards).

## Worktrees

The launcher creates git worktrees under `.studio/worktrees/`:

- Implementation: `<issue>-<role>`, for example `88-gameplay-programmer`, on the Issue's branch.
- QA and review: `<role>-pr<n>`, for example `qa-test-engineer-pr96` or `reviewer-pr96`, at the PR's exact head commit.

Keep one implementation worktree per Issue and reuse it for fixes; two Issues never share a worktree. QA and review use their own separate checkouts.

**Cleanup.** After a PR merges or closes, the Producer removes its implementation, QA and review worktrees with `git worktree remove`, then runs `git worktree prune`. From the repository root, check each worktree first; for example:

```text
git -C .studio/worktrees/88-gameplay-programmer status --short
git worktree remove .studio/worktrees/88-gameplay-programmer
git worktree prune
```

Remove only idle worktrees with no uncommitted changes, including untracked files; never force removal. Check existing QA and review worktrees before a launcher rerun, which recreates them. Never delete a branch you didn't create ([AGENTS.md §5](../../AGENTS.md#5-security-rules)).

**Unity cache.** Each worktree has its own `Game/Library/`, which is never committed ([AGENTS.md §2](../../AGENTS.md#2-repository-map)). Plan for a slow first batch import and disk use per worktree; fresh QA checkouts pay the import cost again. #82 was blocked; its checks were re-run in [#92](https://github.com/smokim38/rts-game/issues/92): cold import from an empty `Library/` took about 68 s, warm import about 6.6 s, with peak memory about 1.35 GiB. `Library/` disk size was not measured.

Do not copy or share a `Library` folder between worktrees: its cached imports can reflect different assets, packages or Editor state. Let each worktree's batch Editor create its own cache.

**Editors and scenes.** Run at most one batch Editor per worktree at a time; the owner never opens a worktree's project in the Editor ([ADR 0006](../adr/0006-unity-editor-automation.md#parallel-engineers), [owner rules](../adr/0006-unity-editor-automation.md#what-the-owner-does)). Separate worktrees do not allow two agents to edit the same scene at once: follow [AGENTS.md §4](../../AGENTS.md#4-engineering-standards) and [Unity work](#unity-work) for asset ownership and ladder exclusions.

## Definition of Ready

An Issue moves to **Ready** only when it has all of these:
- [ ] a goal (one or two sentences)
- [ ] testable acceptance criteria
- [ ] for Unity work, the asset paths it creates or changes
- [ ] an `### Owner instruction` section with the owner's words and the date, and no body edits by another account
- [ ] a `role:*` label (exactly one)
- [ ] a `type:*` label (exactly one)
- [ ] a `tier:*` label (exactly one)
- [ ] the Size field: S (under a day), M (one or two days) or L (bigger; consider splitting)
- [ ] any protected path it touches named explicitly (AGENTS.md §6)

## Definition of Done

See [AGENTS.md §8](../../AGENTS.md#8-definition-of-done-templates). It isn't copied here, so the two can't drift apart.

## Board: "RTS Studio"

A GitHub user Project owned by `smokim38` and linked to this repository. The owner sets it up once with `tools/setup-board.ps1`.

| Status | Meaning | Moved by | When |
|---|---|---|---|
| Backlog | Drafted, not yet Ready | Producer | when the Issue is created |
| Ready | Meets Definition of Ready and is waiting for an agent | Producer | after the Ready check |
| In progress | An agent is working on its branch | Producer | when the Producer assigns the agent, or when review or QA sends the PR back |
| In review | A PR is open and under review or QA | Producer | when the agent reports the PR |
| Blocked | Waiting on a decision, another Issue or an outside fix | Producer | when an agent reports `blocked` or `needs decision` |
| Done | PR merged and Issue closed | GitHub, checked by the Producer | when the Issue closes |

**Only the Producer moves cards.** Staff report status to the Producer with the AGENTS.md §8 report and don't edit the board. Having one writer keeps the board accurate.

## Labels

| Group | Labels | Rule |
|---|---|---|
| Type | `type:feature`, `type:bug`, `type:task`, `type:spike`, `type:docs`, `type:art` | Exactly one per Issue |
| Role | `role:producer`, `role:game-designer`, `role:tech-architect`, `role:code-reviewer`, `role:gameplay-programmer`, `role:qa-test-engineer`, `role:tech-artist` | Exactly one per Issue: who does the work |
| Tier | `tier:light`, `tier:owner`, `tier:standard`, `tier:critical` | Exactly one per Issue (AGENTS.md §6) |

Labels never authorise work; the `### Owner instruction` section does (AGENTS.md §3). The old `approved` label has no meaning any more.

A `type:spike` Issue is time-boxed research. Its output is a written finding (a comment or a doc), not production code.

## WIP limit

The gameplay-programmer role has **two WIP slots**, each held by one open assigned Issue in its own worktree. The two Issues must not share Unity assets (see [Unity work](#unity-work)). The qa-test-engineer and tech-artist roles each keep **one WIP slot**. Each agent works on one Issue at a time.

The launcher enforces the limits with a lock per slot under `.studio/locks/`, which frees when its Issue closes. Slots are checked and claimed atomically under the role's mutex, so two launcher runs can't both take the same slot, and only one run can take over a lock left by a run that crashed. For qa-test-engineer, a QA check fills the slot until its result is posted. If an engineer is blocked, the Producer resolves the blocker or asks the owner; the blocked agent doesn't start another Issue. Fixes requested by review or QA belong to the same Issue and reuse its slot.

## Merge policy

The Producer merges with exactly `gh pr merge <n> --repo smokim38/rts-game --squash --delete-branch --match-head-commit <sha>`, as a call of its own. The guard checks GitHub first and blocks the merge unless (AGENTS.md §6):
- [ ] The PR is open on `main`, its head is `<sha>`, and it closes exactly one authorised Issue.
- [ ] The latest `gitleaks` check on `<sha>` succeeded, and every other latest check succeeded, was neutral or was skipped.
- [ ] The evidence for the tier that counts is recorded for `<sha>` (Review, above), with no newer blocking record.
- [ ] PRs that change `Game/` have a `QA: pass` record for `<sha>`.

Every lookup reads all pages, except the linked Issue's labels and body edits, which are read up to 100 each. An Issue with more than 100 of either blocks the merge.

The guard can't check the rest, so the Producer does before merging:
- [ ] Profiler numbers are included if a hot path changed (AGENTS.md §4).
- [ ] Only in-scope files changed, with `.meta` files included. Protected paths changed only if the Issue names them.
- [ ] The PR is under ~400 changed lines, or the PR says why not.

**Owner-tier and security-perimeter PRs** wait for the owner. The Producer explains the change in plain language, and once the owner replies "approved" in its session, posts the owner-approval record quoting that reply and its date, then merges. For a guard change, it also tells the owner to reinstall the guard (`tools/install-claude-guard.ps1`).

`--match-head-commit` makes GitHub refuse the merge if anyone pushed after the checks, so the merged commit is always the reviewed one.

## Escalation

The Producer escalates to the owner only:
- feature or design decisions, and changes to scope, goals or acceptance criteria
- Owner-tier and security-perimeter approvals
- anything the guard or Codex auto-review denies twice

These are immediate stops, always reported to the owner at once:
- a secret that turns up anywhere (AGENTS.md §5)
- a PR still not approved after the second review round (AGENTS.md §6)

"Denied twice": after a denial, the Producer may try one different approach that follows the rules. It never retries the denied operation in another form. If that approach is also denied, it escalates with both attempts and the reasons.

## Reporting to the owner

The owner hears about features, not implementation. Implementation detail stays in Issues and PRs.
- **Done:** what changed in the game and how to try it, with the PR link for anyone who wants detail.
- **Questions:** feature-level and answerable without reading code, at most three at a time, each with a recommendation.

At the end of every Producer session:

```text
Status report: <date>
Done:          <feature>: what changed in the game, how to try it (PR #<n>)
In progress:   <feature>, <one-line state>
Blocked:       <feature>, blocked on <what>, next step <who/what>
Needs owner:   <decision or approval>, with a recommendation
Tokens:        Codex <total> (<PR or Issue #n>: <tokens>, <runs> runs; ...), Claude not available
```

Write "none" on a line with nothing to report. "In progress" covers the Ready, In progress and In review cards, so every card outside Backlog and Done appears exactly once.

"Tokens" is the session's Codex token total, added up from the launcher's run summaries and broken down by PR or Issue with the number of runs for each. Note any runs that reported no tokens. Claude usage is "not available" unless the Producer has a figure. The line is "none" when no Codex runs happened.

## Sessions and context

The owner's standing rules (2026-10-20). State lives in the repo and on the board, not in chat history.

1. **One Producer session per sitting.** Each sitting starts fresh with `claude --agent producer --effort high`, not by continuing or resuming an old session. The Producer picks up state from the board, Issues and PRs, not from earlier chats. At the end of each sitting, it gives the status report ([Reporting to the owner](#reporting-to-the-owner)) and tells the owner it's a good point to exit.
2. **Long sessions.** When a session is getting long (checked with `/context`, or when the Producer notices it), the Producer tells the owner and gives the status report so the owner can restart, rather than relying on auto-compaction.
3. **One fresh subagent per Issue.** A subagent instance works on one Issue only and is never reused across Issues. Revision rounds on the same Issue use a fresh instance of the same agent type, given the Issue, what was already done and the feedback.
4. **Short reports.** Every delegation asks for a short report in the AGENTS.md §8 "Report to the Producer" format: what was done, the file paths changed, and any open questions. No full documents are pasted back.
5. **No forking.** The Producer doesn't fork the conversation for delegation. It delegates only to the agent types its Agent tool allows.
