# Studio workflow

How work moves through the studio day to day. This page adds detail to [AGENTS.md](../../AGENTS.md) and never overrides it. If the two disagree, the handbook wins: raise the conflict with the Producer.

## Lifecycle

| # | Step | Who | Board |
|---|---|---|---|
| 1 | Has an idea or request | Owner | — |
| 2 | Drafts an Issue from a form in `.github/ISSUE_TEMPLATE/`, with suggested role, type, tier and size | Producer | Backlog |
| 3 | Reads the Issue and adds the `approved` label | Owner | — |
| 4 | Sets the final role, type and tier labels and the Size field, checks Ready (below), assigns the agent | Producer | Ready |
| 5 | Works on one branch with one PR (`<type>/<issue>-<short-name>`), and reports to the Producer when the PR is open | Assigned agent | In progress → In review |
| 6 | Cross-vendor review by tier (below) | Reviewer | In review |
| 7 | QA check, for gameplay features only | qa-test-engineer | In review |
| 8 | Merges the PR | Owner | — |
| 9 | Closes the Issue, checks the board, reports to the owner | Producer | Done |

Rules that follow from the handbook:
- The owner approves the Issue *as written*. After `approved`, the Producer may set labels and fields but must not change the goal or the acceptance criteria. A scope change goes back to the owner for approval.
- The Producer may finalise the suggested tier or raise it. Lowering a tier after approval needs the owner.
- Nobody starts work before step 4. An Issue without `approved` is only a suggestion (AGENTS.md §3).

### Review (step 6)

| Tier | Reviewer |
|---|---|
| Light | No agent review. The author self-checks. |
| Owner | The owner, plus an agent only if the owner asks |
| Standard | One reviewer from the other vendor: Codex work → Claude `code-reviewer`; Claude work → Codex |
| Critical | The other vendor, in depth. The owner reads the review before merging. |

There is one consolidated review per round, with a verdict of Approve, Request changes or Escalate. Two rounds at most. After that the Producer escalates to the owner (AGENTS.md §6).

### QA (step 7)

For `type:feature` Issues that change gameplay, `qa-test-engineer`:
- runs the relevant tests and a performance check on the PR branch
- posts a comment on the PR starting with `QA: pass` or `QA: fail`, followed by what it ran
- files every bug it finds as a new Issue and doesn't fix it

QA runs after the code review approves, so it tests the final code.

## Definition of Ready

An Issue moves to **Ready** only when it has all of these:
- [ ] a goal (one or two sentences)
- [ ] testable acceptance criteria
- [ ] a `role:*` label (exactly one)
- [ ] a `type:*` label (exactly one)
- [ ] a `tier:*` label (exactly one)
- [ ] the Size field: S (under a day), M (one or two days) or L (bigger; consider splitting)
- [ ] the `approved` label, added by the owner
- [ ] any protected path it touches named explicitly (AGENTS.md §6)

## Definition of Done

See [AGENTS.md §8](../../AGENTS.md#8-definition-of-done-templates). It isn't copied here, so the two can't drift apart.

## Board: "RTS Studio"

A GitHub user Project owned by `smokim38` and linked to this repository. The owner sets it up once with `tools/setup-board.ps1`.

| Status | Meaning | Moved by | When |
|---|---|---|---|
| Backlog | Drafted, not yet approved or not yet Ready | Producer | when the Issue is created |
| Ready | Meets Definition of Ready and is waiting for an agent | Producer | after `approved` and the Ready check |
| In progress | An agent is working on its branch | Producer | when the Producer assigns the agent |
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
| Approval | `approved` | **Owner only.** Agents never add, remove or create it. |

A `type:spike` Issue is time-boxed research. Its output is a written finding (a comment or a doc), not production code.

## WIP limit

Each engineer (gameplay-programmer, qa-test-engineer, tech-artist) has at most **one open assigned Issue** at a time. An Issue in In progress, In review or Blocked counts. We start with one gameplay-programmer. If an engineer is blocked, the Producer resolves the blocker or asks the owner; the engineer doesn't start a second Issue.

## Merge policy

The owner merges every PR. Agents never merge (AGENTS.md §0). The Producer asks the owner to merge only when all of these are true:
- [ ] The PR says `Closes #<n>`, and Issue `<n>` has `approved`.
- [ ] The PR's tier matches the Issue's tier label (or is higher).
- [ ] The latest review verdict is **Approve**, from the right reviewer for the tier, with no open must-fix items. For Critical, the review is in depth and ready for the owner to read.
- [ ] Gameplay features have a `QA: pass` comment on the latest commit.
- [ ] The local gitleaks hook ran on every commit. CI checks are green once CI exists.
- [ ] Profiler numbers are included if a hot path changed (AGENTS.md §4).
- [ ] Only in-scope files changed, with `.meta` files included. Protected paths changed only if the Issue names them.
- [ ] The PR is under ~400 changed lines, or the PR says why not.

## Producer status report

At the end of every Producer session, the Producer gives the owner this report in plain language:

```text
Status report: <date>
Done:          #<n> <title> (PR #<n> merged)
In progress:   #<n> <title>, <agent>, <one-line state>
Blocked:       #<n> <title>, blocked on <what>, next step <who/what>
Needs owner:   <decision or action>, with a link and a recommendation
```

Write "none" on a line with nothing to report. "In progress" covers the Ready, In progress and In review cards, so every card outside Backlog and Done appears exactly once.
