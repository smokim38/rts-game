# ADR 0012: Move check in parallel passes

## Status

Accepted

## Date

2026-10-29

## Context

[ADR 0008](0008-movement-and-pathfinding.md) ends each tick with a move check. A unit's new pose is accepted only if its footprint overlaps no other unit's footprint more than at the start of the tick, and its disc reaches no further into a blocked cell. A unit that fails tries its new position with its old heading, then goes back to its start pose with zero velocity. After a parallel first round, units next to a fallback are checked again in serial rounds inside one single job, until none falls back. A lever, `maxSerialFallbacks`, caps that serial work. At the cap, the units still to check, and every unit whose disc could touch one of them, directly or through others, go back to their start poses. [ADR 0011](0011-flow-field-storage.md) follow-up 3 told the movement core (#143) to build the cap and set its default so that the serial rounds' p99 is at most 0.5 ms at 2,000 units.

**What #143 found.** At a cap of 8, the rollback set became the whole crowd, and the crowd froze. At caps 8 to 24, the serial job's p99 was still 0.535 to 0.704 ms, over its 0.5 ms share. #143 merged with a cap of 1,792, which never fired. So the merged check is the exact rounds with no working cap.

**What #148 measured.** It ran 48 development Players (three per scene, unit count and check) on the ladder PC (i7-12700KF, 12 cores, not reference-class), at #143's merge commit 75aa082, uncapped. It compared the merged exact rounds with a one-pass check. In the one-pass check, each unit's candidates are tested against each neighbour's start pose, corrected new pose, and corrected position with old heading. Every unit is decided in parallel from immutable poses and writes only its own row, with no serial job, cap or rollback.

| Scene, units | Move check p99, exact / one-pass (ms) | Chain p99 (ms) | Chain max (ms) |
|---|---|---|---|
| Crossing, 1,000 | 0.559 / 0.191 | 0.747 / 0.377 | 1.148 / 0.764 |
| Crossing, 2,000 | 0.787 / 0.335 | 1.072 / 0.619 | 1.659 / 2.304 |
| Gap, 1,000 | 0.663 / 0.204 | 0.812 / 0.398 | 2.176 / 0.710 |
| Gap, 2,000 | 1.235 / 0.408 | 1.594 / 0.754 | 3.889 / 1.030 |
| Dense, 1,000 | 0.794 / 0.196 | 0.941 / 0.428 | 2.365 / 1.121 |
| Dense, 2,000 | 1.438 / 0.380 | 1.776 / 0.732 | 3.069 / 3.025 |
| Wall, 1,000 | 0.993 / 0.177 | 1.125 / 0.403 | 1.726 / 0.731 |
| Wall, 2,000 | 1.572 / 0.285 | 1.885 / 0.587 | 3.182 / 1.442 |

| Units, check | Crossing advance, first / second side (cells/s) | Gap throughput per 60 s |
|---|---|---|
| 1,000, exact | 0.161 / 0.017 | 0 |
| 1,000, one-pass | 0.404 / 0.028 | 5 |
| 2,000, exact | 0.050 / 0.000 | 0 |
| 2,000, one-pass | 0.249 / 0.010 | 0 |

- No run had a footprint overlap, a static regression or a GC allocation.
- Most units stood still for more than 5 s with either check. The core has no ORCA, pace or slots yet, so units press into each other by direct seek.
- A focused test confirmed what the one-pass check costs. A follower waits a tick for space its neighbour leaves in that tick, and a move is rejected if any of a neighbour's possible poses would conflict.

**The split frame.** #143's driver schedules a frame's ticks at the end of the frame and completes them at the start of the next one, before other scripts read simulation state. That completion is ADR 0004's one sync point. Uncapped, at about 1,000 fps, the next frame starts before the chain ends, so the sync point waits for most of the chain. ADR 0011 counts main-thread time "including any wait there", so it counts the chain's wall time a second time. #148's uncapped totals had a p99 of 0.62 to 2.43 ms, mostly that wait, while the driver's own work had a p99 of 0.0064 to 0.0084 ms.

**Constraints**
- ADR 0004's hot-path rules, and ADR 0008's guarantee that no tick adds a footprint overlap.
- ADR 0008's budget values at 2,000 units: 3 ms of chain wall time, and 0.5 ms of main-thread time on tick frames.
- No new package.
- The engineers are AI agents, each on one Issue at a time. The check should be one job with a rule that a test can check without a scene.

**Evidence gaps.** The ladder PC isn't reference-class. The quality numbers come from a core without ORCA. Nobody has measured more than one pass, the main-thread total at a 60 fps cap, or options C and D below.

## Decision

**Replace the serial rounds with a fixed number of parallel passes, `moveCheckPasses`, 1 by default. Pass 1 is #148's one-pass check. Remove `maxSerialFallbacks` and its rollback set. The acceptance rule, the order of fallbacks and the guarantee stay.**

### The passes

Each unit has three possible poses for the tick, one per level:
- 0: its corrected pose, after the overlap pass
- 1: its corrected position with its start heading
- 2: its start pose, with zero velocity

Before pass 1, every unit is at level 2. Each pass is one parallel job over all units. It reads the previous pass's levels and writes only its own unit's level:
- A unit at level 0 keeps it.
- Any other unit tries each level below its own, 0 first. It takes the first candidate that meets two conditions:
  - Its disc reaches no further into a blocked cell than at the start pose (ADR 0008).
  - It is tested against every unit whose footprint could touch it (never capped, ADR 0008 rule 3). For each such neighbour, it overlaps the neighbour's pose at every level from 0 to the neighbour's previous level no more than the two start poses overlap. Footprints use ADR 0008's separating axis test ([OBBTree][obbtree]).
- If no candidate passes, the unit keeps its level.

After the last pass, each unit takes its level's pose. Level 2 zeroes the velocity, as in ADR 0008.

**Why no tick adds an overlap.**
- Before pass 1, every pair is at its start poses.
- In a pass, a unit that changes level was tested against every pose its neighbour can hold after that pass: its current pose, and any lower level it might take.
- A unit that doesn't change keeps a pose that was already safe against its neighbour's current pose. If the neighbour changes, the neighbour's new pose was tested against it.
- So after every pass, and so after the last, no pair overlaps more than at the start of the tick, and no disc reaches further into a blocked cell.

The proof needs no order between units, so the result doesn't depend on workers or batch sizes, and stopping after any pass is safe.

**Pass 1 is #148's one-pass check.** With every level at 2, each candidate is tested against all three of each neighbour's poses.

**Following conflicts.** Pass 1 never lets a unit move into space a neighbour leaves in the same tick. It also rejects a move that conflicts with a pose the neighbour doesn't end at.
- In multi-agent pathfinding, the first case is called a *following conflict*: "one agent is planned to occupy a vertex that was occupied by another agent in the previous time step" ([Stern et al.][mapf]). Pebble-motion formulations forbid it.
- That is the price of a check that holds whatever each neighbour ends with. k-robust plans pay a similar price to stay collision-free "even if a limited number of such delays occur" ([Atzmon et al.][robust]).
- A later pass lets a unit follow a neighbour whose move is already final. So a queue in contact gains one more moving unit per pass.

The default accepts forbidding following conflicts, because:
- They only affect a unit that is less than one step from a neighbour. That is 0.133 cells for the small cube at 4 cells/s. ORCA's disc spacing usually leaves more room: small cubes at disc spacing, facing the same way, are 0.21 cells apart.
- From the same tick inputs, no number of passes ends a unit at a better level than the exact rounds would. Each pass tests every pose the rounds can test, and more. Even so, one-pass advanced faster in #148's crossing, and it got 5 units through the gap at 1,000 against 0. Those scenes press units together without ORCA, so this isn't a general advantage, but no loss was measured.
- If Issue 5 shows a loss, `moveCheckPasses` is the lever (Consequences).

### Budgets and how they are measured

The values stay. At 2,000 units, the movement chain takes at most 3 ms of wall time per tick, and the driver at most 0.5 ms of main-thread time per tick frame (ADR 0008). The whole move check now has the 0.5 ms share that the serial rounds had. This section sets the statistic for each budget, and replaces ADR 0011's main-thread pass rule.

**Runs.** ADR 0011's setup: a Windows x64 development Player, 10 s of warm-up, a 60 s window and three runs. They use the four scenes of ADR 0011: the ladder scenario, the 3-cell gap, dense contact and wall contact.
- *Uncapped,* at 1,000 and 2,000 units: VSync off and `targetFrameRate` −1, as on the ladder (ADR 0004).
- *Capped,* at 2,000 units: VSync off and `targetFrameRate` 60. On desktop, `targetFrameRate` then "chooses a target frame rate for the application" ([targetFrameRate][target-fps]).

**Times**
- *Chain:* per tick, from the start of its first job to the end of its last, read from timestamp jobs in the chain, as #143's harness does.
- *Move check:* per tick, from the end of the overlap pass to the end of the last pass.
- *Main thread:* the driver wraps its movement work in one marker (ADR 0011). It wraps the completion call at the sync point in a second marker nested inside the first. Both are summed per frame with `SumAllSamplesInFrame` ([SumAllSamplesInFrame][sum-samples]).
  - *Wait* is the nested marker's sum.
  - *Work* is the outer marker's sum minus the wait.
  - *Total* is the outer marker's sum.

**Frame sets.** Each frame's row records the ticks that frame completed at its sync point and the ticks it scheduled.
- *S:* frames that scheduled at least one tick
- *C:* frames that completed at least one tick
- *U:* frames in S or C. These are the tick frames.
- Other frames are in none of the three.

**Statistic.** Each run gives a 99th percentile by nearest rank (ADR 0004) over its window. A budget passes when the median of the three runs' values is within it.

| At 2,000 units, in every scene | Runs | Over | p99 at most |
|---|---|---|---|
| Chain | Uncapped | Ticks | 3 ms |
| Move check | Uncapped | Ticks | 0.5 ms |
| Main-thread work | Uncapped | U, S and C, each | 0.5 ms |
| Main-thread total | Capped | U, S and C, each | 0.5 ms |

ADR 0011's GC check applies to every run. These are reported but not gated: each row's median and maximum, the wait's median, p99 and maximum in uncapped runs, the maximum on other frames, and the 1,000-unit runs.

Why:
- *p99, not the maximum.* It matches ADR 0004's 1% low, so one hiccup can't decide a result. In the dense scene at 2,000 units, #148's one-pass chain had a p99 of 0.73 ms and a maximum of 3.0 ms.
- *Work in uncapped runs.* There, the wait is the chain's own wall time, which the 3 ms budget already covers. Counting it again made ADR 0011's number measure the chain, not the driver.
- *Total at 60 fps.* With a whole frame to run in, the chain should finish before the sync point. Any wait left over, from a chain or a field build that overruns, is time the main thread really loses, so it counts.
- *Each of U, S and C.* Scheduling costs land on S frames and completion costs on C frames. A cost on a small part of one set can sit under U's 99th percentile, so each set is gated on its own.
- *Where.* Later Issues measure on the ladder PC, as #141, #143 and #148 did. ADR 0004's certification on reference-class hardware still decides the vision target.

### Rules for hot paths

These continue rules 1 to 12 (ADR 0008 and ADR 0011) and add to ADR 0004's rules.

13. **Passes, not rounds.** The move check is `moveCheckPasses` parallel jobs. It has no single-threaded part, no cap and no rollback set. Each pass reads the previous levels from one persistent byte array and writes its own unit's level to a second one. The chain alternates the two arrays.
14. **Test every pose a neighbour can hold.** A candidate is tested against each neighbour's pose at every level from 0 to that neighbour's previous level, all three before pass 1. The limit is the pair's overlap at their start poses. The neighbour search stays uncapped (rule 3).
15. **The pass count is fixed at match start.** It is between 1 and 4, checked at match start like the other settings in rule 6. The chain always schedules exactly that many passes, and nothing reads levels before the sync point (ADR 0004 rule 5).

### What this ADR supersedes

From ADR 0008, only the move-check rounds and the cap, in these places:
1. In Local movement, stage 5: the bullets on later rounds ("Units next to a fallback are checked again, in rounds" and "These later rounds loop inside one single job"). The passes replace them. In Determinism, "each round of the move check" now reads "each pass".
2. The `CheckMovesJob` row of the job table. It becomes: parallel, one job per pass. It reads the grid, the start and corrected poses, the static maps and the previous levels, and it writes levels.
3. In Cost at 2,000 units: both move-check rows, and "plus the fallback rounds" in the Total row and in "Against the pass rule". Consequences below gives the new cost.
4. In Budget: the fallback rounds' 0.5 ms share and the sentence that applies the cap. The whole move check takes over the share. This ADR also sets the statistic for the 3 ms budget, so the chain's timestamp jobs replace "read from the Profiler's timeline" for the chain.
5. Rule 2's "the move check's later rounds" in its list of single-threaded jobs.
6. In Levers: `maxSerialFallbacks` and its rollback set.

From ADR 0011, only these parts:
1. In "Measuring main-thread time and GC over the whole window": "including any wait there", for the ordinary chain, and the main-thread items of "Report and pass". Main-thread time is now the work, and the wait is reported separately. A wait that is left at a 60 fps cap, from a chain or a field build that overruns, still counts, through the total. The section above replaces these parts.
2. In follow-up 3: the cap's scope, its target (the serial rounds' p99 at most 0.5 ms, starting from 32), its cap test and its serial-rounds measurement. #143 has merged, so this only records that the cap is gone.

Everything else stays in force, including:
- the acceptance rule and the order of fallbacks
- the separating axis test, and the uncapped neighbour search (rule 3)
- the guarantee and its static half
- the chain's other jobs
- the 3 ms and 0.5 ms values
- rules 1 to 12
- ADR 0011's run setup and its GC check

ADR 0008's and ADR 0011's text isn't edited (AGENTS.md §9). This section is the record of what is superseded.

### Packages

None are added.

## Alternatives considered

| Option | Move check p99 (ms) | Serial work | Overlap safety | Freezes and stalls | Movement quality | Work for the team |
|---|---|---|---|---|---|---|
| **A. Parallel passes, 1 by default (chosen)** | 0.29–0.41 for 1 pass at 2,000, measured | None | Holds after every pass (proof above) | No rollback. Forbidding following conflicts makes a unit wait a tick | Higher in #148: crossing 0.249 / 0.010 at 2,000, gap 5 at 1,000 | Small: one job replaces two |
| **B. Exact rounds, no cap** (#143 as merged) | 0.79–1.57 at 2,000, measured | Unbounded: up to 2N fallbacks, one round per link | Holds | No rollback | The best possible per tick. Measured 0.050 / 0.000 and gap 0 | None |
| **C. Exact rounds, capped, with a footprint-based rollback set** | Not measured | The cap, plus a closure as serial as the rounds | Holds | The set stalls. Smaller than E's, size unmeasured | Below B | Medium |
| **D. Exact rounds as parallel jobs** | Not measured | None, but a fixed number of rounds | Only with C's closure, or a fallback to A when the rounds run out | As C or A | Up to B when the rounds are enough | High |
| **E. #143's cap, with ADR 0008's disc-based rollback set** | Serial job alone: 0.535–0.704 at caps 8–24 (#143) | The cap, plus a pass over the set | Holds | At cap 8, the whole crowd froze | The worst | Already built |

- **B. Exact rounds, no cap.**
  - *How it works:* this is the check #143 merged. It is safe, and it gives the best result per tick.
  - *Cost:* the rounds run in one single job, which "processes all loop elements in a single worker thread" ([Schedule][schedule]), so no extra core shortens them. A chain down a dense queue costs one round per link. ADR 0008 put the bound, 2N fallbacks, at about 20–40 ms at 2,000 units.
  - *Measured:* its move check took 2.3 to 5.6 times the one-pass check's p99, and it was over the 0.5 ms share in every scene at 1,000 and 2,000 units. The chain still has to fit ORCA, pace and slots. ADR 0008 estimated ORCA alone at 2–6 ms of worker time, so a serial share that grows with crowd density can't stay.
  - *Not chosen.* It stays the quality reference, in #143's code at 75aa082.
- **C. Exact rounds, capped, with a footprint-based rollback set.**
  - *How it works:* at the cap, an outside unit joins the set only when its pose overlaps a restored member's start pose more than at the start of the tick, not when their discs merely touch. That is safe, and the set would be smaller than E's.
  - *Cost:* building the set takes a neighbour query and footprint tests for each member, the same work as a round. In a dense queue, it runs down the same chain. So it bounds nothing that the rounds don't, and it adds a stall for the set. Unmeasured.
  - *Not chosen.*
- **D. Exact rounds as parallel jobs.**
  - *How it works:* each round becomes its own parallel job over the units next to the last round's fallbacks. `IJobParallelForDefer` can take its length from an earlier job "when the number of work items is not known at Schedule time" ([IJobParallelForDefer][defer]).
  - *Limits:* the number of rounds must still be fixed when the chain is scheduled, because nothing may read a job's output before the sync point (ADR 0004 rule 5). A chain needs one round per link, whichever thread runs it. Rounds hold only tens of units, so running them in parallel gains little, and every scheduled round costs a job even when it's empty.
  - *Safety:* an exact state cut off before the rounds finish isn't safe. It would still need C's closure, or a fallback to A for the whole tick.
  - *Not chosen.* Its safe form is A's later passes, which start from a safe state and only improve it.
- **E. #143's cap.** It froze the crowd at cap 8 and missed the 0.5 ms share at caps 8 to 24. Removed.

## Consequences

- **Benefits.**
  - At the 99th percentile, the move check is 2.3 to 5.6 times cheaper than the exact rounds (#148). It runs on all cores and has no serial job.
  - There's no rollback set, so no rollback can freeze the crowd. The proof shows that no overlap is added, not that units make progress: moves that depend on each other, such as a ring of units in contact that each need the next one's space, can still stall.
  - The guarantee holds after every pass, by a short proof that needs no order between units.
  - There's less code: one job replaces two, and three per-unit index arrays go.
- **Tradeoffs.**
  - Forbidding following conflicts. A unit that falls back also restarts from rest (ADR 0008). At the current acceleration of 16 cells/s², a small cube takes 0.25 s to get back to 4 cells/s, and ends about 0.6 cells further behind a leader that kept going (arithmetic). So a queue that starts in contact spreads out as it moves off.
  - Every pass after the first adds one job to the chain.
- **Cost at 2,000 units.**
  - Pass 1 has a p99 of 0.29–0.41 ms on the ladder PC (#148).
  - In later passes, units at level 0 return at once. Each later pass costs one job plus the units still at level 1 or 2. Unmeasured.
  - Memory: one more byte per unit for the second level array, and three `int` arrays fewer.
  - No allocation and no GPU cost.
- **Risks.**
  - The ladder PC has 12 cores. A 6-core reference CPU runs parallel jobs more slowly, so pass 1 may use more of its 0.5 ms share there. Unmeasured.
  - With ORCA, forbidding following conflicts may slow units pushing through an arrived block (S3.5) or queueing at the gap (S3.3). For example, an idle unit stepping aside frees space that the mover wants in that same tick. Issue 5 measures this at 1 and 2 passes.
  - The 60 fps total and the cost of later passes stay unmeasured until Issue A.
- **Reversibility.** Easy. The check is one job with the same inputs (start and corrected poses) and the same output (levels) as before, and the exact rounds are in #143's code at 75aa082.
- **Triggers for a superseding ADR.**
  - With the full chain, S3.3, S3.4 or S3.5 still fails after ADR 0008's levers, including `moveCheckPasses` at 4, and the stalled units end their ticks at level 2: revisit the check.
  - At the pass count the S3 tests need, the move check's p99 is over 0.5 ms at 2,000 units.
  - ADR 0008's triggers on the chain budget, on avoidance and on unit speed stay.
- **Levers.** This replaces ADR 0008's `maxSerialFallbacks` lever. It is tuning and needs no new ADR:
  - `moveCheckPasses`, from 1 to 4. A change shows its ladder entry and a move-check p99 within 0.5 ms at 2,000 units in all four scenes.
- **Dependencies and owner decisions.** No package, no Unity change and no handbook change. No owner decision is needed.
- **Follow-up Issues.**
  - **A. Move check in passes** (gameplay-programmer). It comes before ADR 0008's Issues 4 and 5.
    - *Code and assets:*
      - One pass job replaces `InitialMoveCheckJob` and `SerialMoveCheckJob`. Delete `SerialMoveCheckJob.cs` and its `.meta`. `MoveCheckData` tests candidates as The passes says.
      - `MovementPipeline` schedules the passes over two level arrays, and drops `Affected`, `Changed`, `Marks` and the rollback. `CommitMovementJob` reads the last pass's levels.
      - `maxSerialFallbacks` becomes `moveCheckPasses` (1 by default, with 1 to 4 checked at match start) in `MovementParameters`, `MovementSettings`, `Game/Assets/Data/MovementSettings.asset` and `MovementCoreProfileBuilder`.
      - `MovementMeasurement` and `MovementCoreProfile` record, per tick: the units ending at levels 1 and 2 instead of the serial, round and rollback counts, and the move check's time as defined above. They record the pass count instead of the cap.
    - *Tests,* replacing the two cap tests:
      - The squeezed crowd, the wall and the concave corner add no footprint overlap and no static regression on any tick, at 1 and 2 passes.
      - A follower in contact with a leader that moves stays at its start pose with zero velocity at 1 pass, and moves at 2.
      - A move that conflicts only with a neighbour's old-heading pose is rejected at 1 pass, and accepted at 2 when the neighbour ends at level 0.
      - Results are bit-identical with 0, 1 and all workers and with different batch sizes, at 1 and 2 passes.
      - With 0 or 5 passes, the match doesn't start. `SandboxSceneBuilderTests` uses the new field.
    - *Measurements:*
      - every budget row and reported value above: uncapped at 1 and 2 passes, and capped at 1 pass
      - the units ending at levels 1 and 2 per tick, and the units still for more than 5 s
      - crossing advance and gap throughput, measured as #148 did
      - its ladder entry (ADR 0004)
  - **ADR 0008's Issue 4, groups and arrival.** These changes add to ADR 0011's:
    - Its budgets and its whole-window method are this ADR's.
    - A new test: a block of small cubes arrives into slots at `slotSpacing` 1.25. Facing squares are then 0.125 cells apart, less than one full-speed step. It checks S2.2 to S2.4 at the default pass count, and its measurements report the units ending at level 2 per tick during arrival.
  - **ADR 0008's Issue 5, ORCA and making way.** These changes add to ADR 0011's:
    - `moveCheckPasses` replaces `maxSerialFallbacks` among the levers it tunes before an avoidance trigger counts.
    - With the full chain, it measures 1 and 2 passes (more if needed) in the four scenes and in the S3.3, S3.4 and S3.5 tests. It measures crossing advance, gap throughput, units standing still for more than 5 s, and units ending at level 2.
    - It sets the `moveCheckPasses` default to the lowest count that passes those tests within the 0.5 ms share.
    - Its budgets and its whole-window method are this ADR's.
  - **Suggested:** add rules 13 to 15 to the gameplay-programmer and code-reviewer role files, together with ADR 0008's follow-up 6 and ADR 0011's rules 10 to 12. These are protected paths, so that Issue must name them.

[obbtree]: https://doi.org/10.1145/237170.237244
[mapf]: https://arxiv.org/abs/1906.08291
[robust]: https://ojs.aaai.org/index.php/SOCS/article/view/18445
[schedule]: https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Unity.Jobs.IJobForExtensions.Schedule.html
[defer]: https://docs.unity3d.com/Packages/com.unity.collections@2.6/api/Unity.Jobs.IJobParallelForDefer.html
[target-fps]: https://docs.unity.com/en-us/engine/6000.3/script-reference/unityengine/application/targetframerate
[sum-samples]: https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Unity.Profiling.ProfilerRecorderOptions.SumAllSamplesInFrame.html
