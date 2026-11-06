# ADR 0017: Ladder armies pass on offset lanes, and the ladder reports movement cost

## Status

Accepted

## Date

2026-11-05

## Context

PR #242 (Issue #232) gives the performance ladder moving units. The verdict record on PR #242 at commit 5294ae3 escalated two problems. Both were checked against the branch and its run files, except where marked.

**The layout can't do what ADR 0004 says.**
- Speeds are 4 and 2.5 cells/s (`MovementSettings.asset`), so a 20 s cycle gives 80 and 50 cells of free travel and closes 130 cells. Main's blocks at x = ±80 are 160 apart and can't meet in a cycle. "Opposite ends", the 20 s cycle and these speeds don't fit together.
- The branch moved the blocks to x = ±30, where the sampled pair seems to stop at contact. At 10 units, tick 600 has them at x = 5.2 and 6.5, against +50 and −20 unimpeded. These are the review's figures, from one sampled unit per side, and weren't re-run here. No log holds full-army positions.
- A stop is what the code should do. The simulation has no avoidance yet: ORCA and the right-turn rule are ADR 0008 Issue 5, and neither is in `Game/Assets/Code/Simulation`. [ADR 0012](0012-move-check-passes.md) says units "press into each other by direct seek". So units that meet head-on stop today (inference).
- The branch's test can't tell. `LadderFrameBufferTests.cs` lines 63, 64 and 88 pass if one unit of each side is past x = 0 at any tick.
- Lanes have two traps. Block width depends on N: about 20 and 40 cells at 2,000 units, about 1 and 2 at 10 (`BlockPlacement`). And units steer straight at one goal point ([ADR 0013](0013-open-ground-field-directions.md)), so they converge. Without crowding, each cycle halves a first-side unit's distance from its goal's z and takes a third off a second-side unit's (80 of 160 cells, 50 of 145; arithmetic). A fixed offset would leave low rungs, and later cycles at any rung, with armies that never come near each other.

**Frame time doesn't show movement cost.**
- From the tank entry to the branch's moving entry, 2,000 units went from 0.844 to 0.874 ms and 10 units from 0.733 to 0.761 ms. The rise is the same at both rungs, so it isn't per-unit cost.
- `MatchDriver` schedules jobs at the end of `LateUpdate` and completes them at the next frame's start. Workers overlap rendering, and frame time sees only what is left: the `Northwatch.Simulation.Wait` marker.
- Nothing reports worker time. `LadderProfileAnalysis` reads thread 0 only.

**Owner decisions** (Producer session, 2026-11-05; Issues #232 and #243). This ADR records them and doesn't reopen them:
1. The 20 s order cycle stays, and the armies use offset lanes so they can pass.
2. The ladder also reports worker-thread time at 2,000 units, and the pass rule doesn't change.
3. A unit whose order the game's rules end gets a new order within one simulation tick.

**Evidence gap.** Nobody has run a lane layout. Every lane figure below is arithmetic or judgment until #232's trial, and says which.

## Decision

**Each army keeps to its own lane, one each side of the plane's centre line, with the two goal lines close enough to stay in reach. In each complete order cycle, 90% of each side must pass the other army, and at least one spawn row's worth of each side must come within 2 cells of it. Entries report both shares, and worker job time and the wait at 2,000 units. The pass rule doesn't change. The new series is scenario version 3.**

### What this supersedes

Only these words of [ADR 0004](0004-unit-simulation-architecture.md#performance-ladder), whose text isn't edited (AGENTS.md §9):

1. Test scene, the scenario line: "spawn in blocks at opposite ends. Both sides are ordered to the far end on a fixed cycle, so the groups keep crossing in the middle." The line now reads:

   > the scenario: N/2 units per faction spawn in blocks on offset lanes, one each side of the plane's centre line. Each side is ordered along its own lane to alternating far ends on a fixed cycle, so the armies pass each other once per cycle. Once combat exists, units fight but can't die, so the count stays at N. The random seed is fixed.

2. Active unit, third item: "it has a current order and is drawn every frame". It now reads:

   > it has a current order and is drawn every frame. When the game's rules end a unit's order, it gets a new one within one simulation tick, before its next movement step

Everything else in ADR 0004 stands, including the pass rule, the regression check and "any change bumps the version". This ADR adds two reasons to its Invalid runs and one item to its Log entry (below). The "ladder scenario" of ADR 0008, 0011 and 0012 is `MovementCoreProfile`'s own synthetic scene, which keeps its layout.

### Layout

Every value lives in `PerformanceLadder.asset` (`LadderScenario`). #232 may set the values with a trial range. Anything else, or anything outside a range, needs a superseding ADR.

| Field | Start | Trial range | Meaning |
|---|---|---|---|
| `OrderCycleSeconds` | 20 | fixed (owner) | The order cycle |
| `LaneGap` | 1.2 | 1.1 to `MeetDistance` | Cells between the goal lines: z = +gap/2 for the first side, z = −gap/2 for the second |
| `FirstSpawn.x`, `SecondSpawn.x` | −40, +25 | centres 50 to 80 apart, at −8/13 and +5/13 of that distance | Where the blocks start along the lanes |
| `FirstOrderEnd.x`, `SecondOrderEnd.x` | −120, +120 | fixed | Far ends, out of reach within a cycle |
| `OrderSpread` | 7 | fixed | Seeded ± offset of a goal along the lane |
| `PassShare` | 0.9 | fixed | Crossing rule |
| `MeetRows` | 1 | fixed | Crossing rule |
| `MeetDistance` | 2 | fixed | Crossing rule, in cells |
| `MeetSampleTicks` | 15 | fixed | Crossing rule |

- **Orders.** A side's order goes to its current end's x ± `OrderSpread`, at its goal line's z. The spread moves the goal along the lane, never across it. The branch's ±8 across it would swing a goal line by several times the largest gap. Along the lane it still gives a new goal cell, so a new field, nearly every cycle at every rung.
- **Spawn.** Each block sits wholly on its own side, with its facing row of unit centres on its goal line. Its centre z is ±(`LaneGap` / 2 + (rows − 1) × pitch / 2), worked out from N with `BlockPlacement`'s own numbers. Unit widths and `slotSpacing` belong to movement, not the scenario.
- **Why the goal lines sit at the seam.** Units converge on their goal. With goals in the middle of each lane, the facing edges would part every cycle, and the lanes would pass by never interacting. At the seam, convergence pulls each army toward the other.
- **Why 1.1 to 2.** Discs of opposite sides touch at 1.06 cells (0.354 + 0.707, ADR 0008), so a larger gap lets the facing rows pass without a head-on stop. A gap above `MeetDistance` would put them out of reach.
- **Why −40 and +25.** Each is half its side's free travel from x = 0, so both centres reach x = 0 ten seconds into every cycle, in both directions. At 2,000 units the second side's last unit needs about 85 of the 130 cells, which leaves 45 cells, about 6.9 s at 6.5 cells/s. At 50 or 80 apart it leaves 30 cells, about 4.6 s. This is arithmetic from spawn depth, before the short turn at each reversal.
- **Speeds** live in `MovementSettings.asset`. A speed change can break this arithmetic. The crossing rule then catches it, and a new version resets the layout.

### The crossing rule

Cycle k runs from tick 600k + 1 to tick 600(k + 1). The 10 s warm-up and 60 s window hold two complete cycles, k = 1 and k = 2 (simulation seconds 20 to 40 and 40 to 60). The partial cycles at each end aren't judged. The two cycles travel opposite ways, so each army passes once with the other on its right and once on its left, which will matter when the right-turn rule lands.

In each of those two cycles, for each side:
- **Pass.** At least `PassShare` of its units, rounded up, have passed. A unit has passed when, between the previous cycle's last tick and this cycle's last tick, it has changed sides of the other army's mean x.
- **Meet.** At least `MeetRows` × ceil(√(N/2)) of its units, one spawn row's worth, have met the other army. A unit has met it when a unit of the other side is within `MeetDistance`, centre to centre, on a sampled tick of that cycle (every `MeetSampleTicks` ticks).

Why:
- *The mean.* Armies that stop at contact never swap means, so this catches what the review saw, over the whole army. It needs one read of each unit per cycle.
- *90%* is a judgment. It forgives stragglers in a scrape along the seam. At 10 units it means all 5.
- *2 cells* is where the other army enters a unit's per-tick loop: `OverlapMovementJob` searches about 2.0 cells round a small unit and 2.4 round a large one.
- *Every 15 ticks.* Closing at 6.5 cells/s, the armies move 3.25 cells between samples. A pair that passes close enough to touch stays within 2 cells for 3.39 cells, so it is always seen. Sampling can miss a wider pass, but it can't count a meeting that didn't happen.

**How much the lanes may reduce contact: down to the armies' facing edges, and no further.**
- The gap's upper limit and the meet floor hold every judged cycle at every rung to that. The floor is a row because contact runs along a line, so its share falls as N rises: 3 of 5 units at 10 (60%), 32 of 1,000 at 2,000 (3.2%).
- This is a judgment, and a deliberately low bar. With no avoidance, units in overlapping lanes would stop head-on (Context), which is the meet-and-stall the owner didn't choose. So today's ladder measures two armies scraping along one line, not two armies mixing.
- The floor exists to fail lanes that have drifted apart, not to claim that the armies mix. So the real numbers are always reported. For each rung and side, the entry gives the worst judged cycle's pass share and meet share, plus mean travel as a share of free travel (reported, not gated).
- ORCA changes this (Consequences).

### Where the rule is measured

- **In every ladder Player run,** so the numbers describe the run they sit beside, as the renewal counts do. An EditMode test can't stand in: at 2,000 units the Editor logs renewals of 1/0/7/1000 (the review's figure) and the Players 0/0/1/1000 (checked in the release and development run files).
- **In the EditMode test too,** which reads the same counters at 10 and 2,000 units and replaces the lines named in Context.
- **A run that misses the rule is invalid,** with that reason, as a run whose units aren't active is. It didn't run the scenario, so it is neither a pass nor a fail, and the frame-time conditions don't change. A later PR that makes a rung miss the rule has no evidence for that rung until movement is fixed or a superseding ADR says otherwise.
- **A run whose window closes before both cycles finish can't show the rule.** The test is a `UnitSimulation.TickCount` of at least 1,800 when the window closes. It is recorded then, because the 2 s drain keeps ticking before `Finish`. At 30 Hz tick 1,800 comes 50 s into the window, so a run has 10 s to lose across warm-up and window. `TickClock` drops any backlog past two ticks, so a stall can spend that with healthy frame times: 48,000 frames of 1 ms and one of 12 s average 1.25 ms with a 1 ms 1% low, yet close near tick 1,742, and the drain would carry them past 1,800 (arithmetic). The test is the tick, not a frame rate: a steady 14 fps makes 28 ticks a second and gets there, and a steady 16.7 fps makes 25 and doesn't (arithmetic, from `TickClock`). Such a run:
  - **Is invalid, with that reason,** if its own average and 1% low frame times are both 16.67 ms or less.
  - **Stays valid but unchecked,** and the entry says so, if either is over. Its frame times stay in the medians. ADR 0004's medians can still pass a step with such a run among its three. If they do, the run becomes invalid with the same reason and is replaced, within the six attempts. So every run in a passing step has shown the rule, and the pass rule's conditions and medians don't change.
  - **Isn't checked, and the cost table says so,** if it is one of the two development cost runs (Movement cost). They sit outside the medians and can't pass, and the capture's 40 ms frames are over 16.67 ms only because of its cap, so frame times say nothing about them. At 40 ms the clock still makes 30 ticks a second, six every five frames, so the capture normally reaches tick 1,800.

These continue rules 1 to 17 and add to ADR 0004's rules:

18. **Crossing counters run only in the ladder.** The ladder asks for them when it queues its orders. Normal play never schedules them.
19. **The meet check is sampled and uses the grid.** Every `MeetSampleTicks` ticks, one `[BurstCompile]` `IJobFor` searches the spatial grid within `MeetDistance` for each unit that hasn't met the other army this cycle. It writes only its own unit's byte in a persistent array.
20. **The pass check runs once per cycle.** On a cycle's last tick, one single-threaded job reads each unit once and writes that cycle's counts to a persistent array sized at setup. The main thread never loops over units, and `PerformanceLadder` writes the counts to its metadata in `Finish`, after the window.

### If a trial can't meet the rule

1. Trial in a Player at 2,000 units first: `LaneGap` 1.2, 1.6, then 2.0, each at centres 65, 50, then 80 apart. That is at most nine pairs, smallest gap first because it keeps the most contact. A pair that meets the rule at 2,000 then runs at all nine rungs. If it misses at any rung, the trial moves to the next pair.
2. One set of values serves every rung, so the curve stays one layout. There are no per-rung values.
3. If no pair meets the rule at every rung, #232 stops and commits no entry. It doesn't lower `PassShare`, `MeetRows` or `MeetDistance`, widen a range, lengthen the cycle, or drop or excuse a rung. It reports each pair's worst shares per rung, and the Producer takes that to the owner. Any change goes through a superseding ADR.
4. The PR lists every pair tried and its shares. Trial runs aren't ladder entries.

### Order renewal

Owner decision 3, as #232 built it. `RenewOrders` in `ApplyCommandsJob` runs before movement on every tick, including batched ticks, and gives a unit whose order ended its side's current cycle destination. A live order is never renewed early, and every movement rule stays enabled.

**Rule 2, contact with an arrived member, can't fire in the ladder.** It needs an idle unit with the same order version (`PreferredVelocityJob.cs` lines 49 to 52). A unit goes idle only when that job ends its order, and `RenewOrders` makes it moving again before the job next reads it. The count was 0 in all 27 runs of the branch's ladder. The rule's neighbour search still runs for every moving unit, so its cost stays in the numbers, but its outcome and idle units are never measured. Entries keep reporting all four counts.

The group stall stays enabled, but lanes may leave it nothing to do. So the assertion that it fired at 2,000 units (`LadderFrameBufferTests.cs` line 86) goes. `RenewalWaitsUntilOrderEndsAndUsesCurrentCycleDestination` still covers renewal after each rule.

### Movement cost

**The pass rule and regression check don't change.** Nothing below sets `result`, `regressed` or the exit code, and no threshold applies. Two development Player runs at 2,000 units, at the entry's commit, come before the ladder, because entries are never edited. They use only tools already in the project, extended. No new tool or package.

| | Worker job time | The wait |
|---|---|---|
| Build | `PerformanceLadderDevelopment` | The same |
| Run | The existing `-ladder-profile` raw capture: 25 fps, full 60 s window, which ADR 0004's rule 11 already needs | Uncapped, no raw capture. `ProfilerRecorder` with `SumAllSamplesInFrame`, as `MovementCoreProfile` reads this marker |
| Read by | `LadderProfileAnalysis`, extended to read every thread of each frame, as `MovementCoreProfileBuilder.AnalyzeCaptureFile` does, and to sum `GetSampleTimeMs` ([RawFrameDataView][raw-frame]) over the tick chain's job markers | `PerformanceLadder`, per frame into a preallocated buffer, written in `Finish` |
| Figures | Mean ms per tick, 99th percentile by nearest rank over one-tick frames, mean per job, tick count, capture bytes, peak memory | Median, 99th percentile and maximum over frames that completed a tick (ADR 0012's set C), and the count of those frames |

- **Why job time uses the 25 fps capture.** Ticks run at a fixed 30 Hz, so a tick's jobs do the same work at any frame rate, and the capture keeps all 1,800 ticks. #232's 60 s capture at 2,000 units is 119,380,987 bytes, 59% of the 201,326,592-byte (192 MiB) refusal limit, with 1,500 frames against the loader's 2,000, and analysis peaked at 2.87 GB committed. Lanes may change the size. If it goes over the limit, don't raise the limit or skip frames. Use `-ladder-profile-seconds 40`, then 20: whole cycles, so passes keep their share of the capture. The entry says so.
- **What it sums.** The summary names the markers. Count ticks from `BuildSpatialGridJob`, because `ApplyCommandsJob` runs an extra time on any frame with ticks (`UnitSimulation.cs` lines 241 and 251). `FillDrawDataJob` (per frame) and the crossing jobs are listed but stay out of the total.
- **Why the wait doesn't use it.** At 25 fps workers have about 40 ms before the sync point, so the wait shows nothing short of a 40 ms overrun. In #232's capture, `Northwatch.Simulation`, which contains the wait, averages 0.017 ms per sample at two samples a frame. An uncapped raw capture isn't possible. The shortest allowed, 10 s at the 1,143 fps of the 2,000 rung, is over 11,000 frames, and at the capture's 80 kB per frame about 900 MB (arithmetic). A recorder writes no raw file, so neither limit applies. Uncapped, the wait is about the chain's wall time (ADR 0012). It is the most that can reach the main thread, since at 60 fps workers get a whole frame (inference).
- **Why not the release Player.** In non-development builds, marker `Auto` calls "are conditionally compiled to just return null" ([adding profiling information][markers]).

**Where they appear.** Raw files stay under `Game/Logs/`. `tools/unit-ladder.js` reads both results, refuses them if missing or from another commit, and writes them into the entry as a "Movement cost at 2,000 units" table and a JSON field, labelled with the Player and frame rate. The PR quotes them beside the 2,000 rung.

**What these numbers can't do.**
- *On a PC that isn't reference class* (the ladder PC has 12 cores and 20 threads, `reference-class: no`), they show trends only, against entries with the same hardware summary and scenario version. A faster core lowers job time, more threads shorten the chain and shrink the wait, and nobody has measured a factor to a mid-range 6-core CPU. So they can't show headroom on the reference PC, and they aren't set against ADR 0008's 3 ms and 0.5 ms budgets, which keep ADR 0012's runs.
- *Development Player.* The Profiler is on, so these figures never add to or subtract from the release entry's frame times.
- *One run each.* There is no spread yet, so a small difference isn't a finding. A threshold needs its own ADR, after several entries.
- *No ORCA.* ADR 0008 estimates 2 to 6 ms of worker time per tick for it, so today's figure isn't the final cost.

### The series

- The new series is **scenario version 3**, so `main` goes from 1 to 3. Version 2 already names the x = ±30 layout in PR #242's history, its review record and local logs, and reusing it would give one number two layouts. The harness only matches versions by equality (`newestBaseline`), so the gap does nothing.
- `docs/performance/unit-ladder/2026-11-06-4f80460.md` is replaced. PR #242 removes it and adds a version 3 entry measured on a clean commit. It never reached `main`, so ADR 0004's "never edited or deleted" doesn't cover it.
- Entries on `main` stay unedited. The first version 3 entry says "none (new series)" and carries no flags. PR #242 sets its height and 2,000-unit figures beside the last version 1 entry and says they aren't like for like (Issue #232).
- Trial runs stay at version 3 and produce no entry. Changing a value after the entry is measured means measuring again before merge. After merge it means version 4.

## Alternatives considered

The chain's per-unit work is the same under all four layouts. They differ in how much contact the ladder contains and what stops it. None has been run. The review's other two options, accepting the stop at contact and lengthening the cycle, aren't reopened (owner decision 1).

**A. Seam lanes (chosen).**
- *How it works:* as above.
- *Evidence:* arithmetic only: the pass fits the cycle, and contact lies along one line.
- *Cost at 2,000 units:* the same chain, plus one sampled grid search per unit not yet met, about one fifteenth of a neighbour pass per tick (estimate), and one read per unit per cycle. No allocation and no GPU cost.
- *Complexity:* a few fields, two small jobs and some harness lines.
- *Risk and reversal:* the seam may jam and miss the rule, which the trial path covers. A later version can change the layout, and entries keep their medians.

**B. A fixed offset with goals in the middle of each lane.**
- *How it works:* each side gets a constant z.
- *Evidence:* clearing the 2,000-unit blocks needs centres about 31 cells apart, which leaves about 29 cells between the blocks at 10 units, and convergence widens that (arithmetic).
- *Cost:* the same chain, with no contact.
- *Complexity:* the lowest: two values.
- *Risk and reversal:* it passes by never interacting, so the ladder would show two separate parades. Rejected for that.

**C. Overlapping lanes.**
- *How it works:* blocks and goal lines share part of their width.
- *Evidence:* the review's samples stop at contact even at 10 units with open ground around them. Units in the overlap would stop, and the pass share would fall by about the overlap's share (inference).
- *Cost:* unmeasured, likely more move-check fallbacks.
- *Complexity:* as A.
- *Risk and reversal:* it is meet-and-stall by another name until avoidance exists. It is the natural next layout after ORCA.

**D. Several interleaved lanes per side.**
- *How it works:* each side splits into k sub-blocks on alternating lanes, giving 2k − 1 seams. The meet share grows about with the seams (arithmetic).
- *Evidence:* none.
- *Cost:* 2k goal points per cycle, so 2k field builds, against #141's p95 of 1.27 ms open on one worker.
- *Complexity:* the highest: a new order type, because `QueueSideMove` orders a whole side, for a shape the game doesn't need.
- *Risk and reversal:* a ladder-only shape, unlike a real fight. It can come later as a new version, if the owner wants more contact before ORCA.

**Where the check and the figures come from.**
- *Crossing in EditMode only:* the Editor differs from the Player (above).
- *A per-tick crossing check in a separate development Player run per rung:* exact, with nothing added to measured runs, but about 11 more minutes per ladder (9 × 72 s), and it describes a different run from the entry's.
- *The wait from `MovementCoreProfile`:* it already records the marker, but it disables the ladder, and its `ValidateJob` runs inside the chain and lengthens the wait.
- *Figures in the PR only:* a regression couldn't be traced through the entries.

## Consequences

- **Benefits.** The scenario does what its words say. Passing is measured over whole armies, not one unit. Entries state how much the armies really touch, and record the worker time that frame time hides.
- **Tradeoffs.**
  - Contact is low at high rungs, and the ladder doesn't test S3.4 (two groups pass through each other), which stays with ADR 0008 Issue 5.
  - A small diagnostic job runs in measured Players. Its cost is in the series from the first entry, and the capture lists it. If it exceeds 1% of the chain's job time per tick (judgment), #232 reports that in the PR.
  - Each ladder gains about 2.5 minutes for the two development runs, plus analysis.
- **Risks.**
  - The seam may jam at 2,000 units, and the trial may stop #232 again.
  - The capture may outgrow its limit (above).
  - The Editor and Player renewal difference stays unexplained, and explaining it stays with #232.
  - #232 may pass 400 changed lines, and its PR should say why.
- **Reversibility.** Easy. A new ADR and scenario version change the layout, and old entries stay.
- **Triggers for a superseding ADR.**
  - **ORCA lands (ADR 0008 Issue 5).** Its PR reports these figures, and a superseding ADR decides whether the lanes narrow, overlap or go, and raises the meet floor, under a new version.
  - The trial stops under "If a trial can't meet the rule".
  - Someone wants a threshold on worker time.
- **Dependencies and owner decisions.** No package, Unity or handbook change. This ADR needs no new owner decision, but the Producer should tell the owner the floor's size: at least 32 of 1,000 units per side at 2,000. Option D is the route if that is too little before ORCA.
- **Not decided here.** Frames of 2 ms or more run at about 60 per second at both rungs (3,594 and 3,601 in run 1 of the branch's ladder), which isn't the 30 Hz tick. The cause isn't established.

### Files for #232

This is #232's follow-up work. Its Notes already say it follows this ADR. No other Issue is needed now.

| File | Change |
|---|---|
| `Game/Assets/Code/Presentation/Diagnostics/LadderScenario.cs`, `Game/Assets/Data/PerformanceLadder.asset` | Version 3, the fields above with range checks, goal lines, the spread along x |
| `Game/Assets/Code/Presentation/Diagnostics/PerformanceLadder.cs` | Spawn z from N, the uncapped wait mode, crossing counts and the tick at window close in the metadata |
| `Game/Assets/Code/Simulation/` | The crossing jobs (rules 18 to 20) |
| `Game/Assets/Code/EditorTools/LadderProfileAnalysis.cs` | Job time and a wait line in the summary |
| `Game/Assets/Code/Tests/EditMode/Presentation/LadderFrameBufferTests.cs` | Lines 63, 64 and 88 become the crossing rule. The assertion on line 86 goes |
| `tools/unit-ladder.js` and its tests | The invalid reasons and replacing an unchecked run in a passing step, crossing and cost in the entry, the fixed sentences at line 171 |
| `docs/performance/unit-ladder/README.md` | The scenario text, version 3, the rule 2 statement, the new commands |
| `docs/performance/unit-ladder/2026-11-06-4f80460.md` | Removed, with the version 3 entry added |

[raw-frame]: https://docs.unity.com/en-us/engine/6000.3/script-reference/unityeditor/profiling/rawframedataview
[markers]: https://docs.unity.com/en-us/engine/6000.3/manual/analysis/profiler/customizing/adding-information-code/intro
