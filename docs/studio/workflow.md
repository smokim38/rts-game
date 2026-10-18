# Studio workflow

How work moves through the studio day to day. This page adds detail to [AGENTS.md](../../AGENTS.md) and never overrides it. If the two disagree, the handbook wins: raise the conflict with the Producer.

## Lifecycle

| # | Step | Who | Board |
|---|---|---|---|
| 1 | Has an idea or request | Owner | — |
| 2 | Drafts an Issue from a form in `.github/ISSUE_TEMPLATE/`, and applies the `role:*`, `type:*` and `tier:*` labels at intake (AGENTS.md §3) and the Size field | Producer | Backlog |
| 3 | Reads the Issue and adds the `approved` label | Owner | — |
| 4 | Confirms the labels and Size (a tier may only be raised), checks Ready (below), assigns the agent | Producer | Ready |
| 5 | Works on one branch with one PR (`<type>/<issue>-<short-name>`), and reports to the Producer when the PR is open | Assigned agent | In progress → In review |
| 6 | Cross-vendor review by tier (below) | Reviewer | In review |
| 7 | QA check, for gameplay features only (below) | qa-test-engineer | In review |
| 8 | Merges the PR | Owner | — |
| 9 | Closes the Issue, checks the board, reports to the owner | Producer | Done |

Rules that follow from the handbook:
- The owner approves the Issue *as written*. After `approved`, the Producer may set labels and fields but must not change the goal or the acceptance criteria. A scope change goes back to the owner for approval.
- The Producer may raise a tier after approval. Lowering a tier after approval needs the owner.
- Nobody starts work before step 4. An Issue without `approved` is only a suggestion (AGENTS.md §3).

### Review (step 6)

| Tier | Reviewer |
|---|---|
| Light | No agent review. The author self-checks. |
| Owner | The owner, plus an agent only if the owner asks |
| Standard | One reviewer from the other vendor: Codex work → Claude `code-reviewer`; Claude work → Codex |
| Critical | The other vendor, in depth. The owner reads the review before merging. |

There is one consolidated review per round, with a verdict of Approve, Request changes or Escalate. Two rounds at most. After that the Producer escalates to the owner and moves the card to Blocked (AGENTS.md §6).

**Every verdict names the commit SHA it reviewed.** An Approve covers that commit only. Any later push, including fixes after a QA failure, needs a new review of the new head, and that review counts as a round.

### QA (step 7)

For `type:feature` Issues that change gameplay, the Producer asks `qa-test-engineer` to check the PR once the review approves it. The card stays in In review. QA is part of the same Issue, so it doesn't count against anyone's WIP limit. QA:
- runs the relevant tests and a performance check on the PR's head commit
- posts a PR comment starting with `QA: pass` or `QA: fail`, followed by the SHA it tested and what it ran
- never fixes code itself (AGENTS.md §1)

When QA fails:
1. **Failures within the Issue's acceptance criteria:** QA lists each one in its `QA: fail` comment. That comment is the bug report, and these failures don't need a new Issue. The Producer moves the card back to In progress, and the same author fixes them on the same branch under the same Issue.
2. After the fix, the PR is reviewed again (step 6, a new round) and QA checks again, both on the new head.
3. **Bugs outside the Issue** (pre-existing, unrelated, or needing new scope): QA files them as new Issues with the bug form. They aren't work until the owner adds `approved`, and they don't block this PR unless the owner says so.
4. If a fix needs a change to the goal or the acceptance criteria, the Producer asks the owner first (see the rules above).

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
| Approval | `approved` | **Owner only.** Agents never add, remove or create it. |

A `type:spike` Issue is time-boxed research. Its output is a written finding (a comment or a doc), not production code.

## WIP limit

Each engineer (gameplay-programmer, qa-test-engineer, tech-artist) has at most **one open assigned Issue** at a time. An Issue in In progress, In review or Blocked counts. We start with one gameplay-programmer. If an engineer is blocked, the Producer resolves the blocker or asks the owner; the engineer doesn't start a second Issue. Fixes requested by review or QA belong to the same Issue, so they never need a second one.

## Merge policy

The owner merges every PR. Agents never merge (AGENTS.md §0). The Producer asks the owner to merge only when all of these are true:
- [ ] The PR says `Closes #<n>`, and Issue `<n>` has `approved`.
- [ ] The PR's tier matches the Issue's tier label (or is higher).
- [ ] The review for the PR's tier covers the **current head commit**, with no open must-fix items:
  - Light: the author's self-check is noted in the PR. There is no agent review (AGENTS.md §6).
  - Owner: the owner has reviewed it.
  - Standard: an **Approve** from the other vendor that names the head SHA.
  - Critical: an in-depth **Approve** from the other vendor that names the head SHA, ready for the owner to read.
- [ ] Gameplay features have a `QA: pass` comment that names the head SHA.
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
