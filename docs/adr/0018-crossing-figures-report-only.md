# ADR 0018: The ladder reports crossing figures instead of requiring them, and says its armies are mostly stuck

## Status

Accepted

## Date

2026-11-06

## Context

[ADR 0017](0017-ladder-offset-lanes-and-movement-cost.md) made a crossing rule part of a valid ladder run: in each judged cycle, 90% of each side passes the other army and one spawn row's worth meets it. If no layout met the rule, #232 had to stop with no entry and wait for a superseding ADR. That has happened. This ADR changes the rule and what follows from it, and leaves the rest of ADR 0017 alone.

Each figure below says what it is: measured, arithmetic, inference or judgment.

**The trial.** All nine lane pairs missed the rule at 2,000 units (measured: one release Player per pair, #232's trial report). Each side has 1,000 units, and the rule needs 900 passed and 32 met. Figures are each side's worst judged cycle. Travel share is mean endpoint displacement over free travel. Every run had `activeUnitsValid` true and closed its window at tick 2,097.

| Gap | Centres | Passed, first / second | Met, first / second | Travel share, first / second |
|---|---|---|---|---|
| 1.2 | 65 | 61 / 1 | 96 / 28 | 0.099 / 0.079 |
| 1.2 | 50 | 28 / 1 | 67 / 37 | 0.088 / 0.094 |
| 1.2 | 80 | 22 / 1 | 54 / 22 | 0.051 / 0.082 |
| 1.6 | 65 | 67 / 2 | 84 / 31 | 0.125 / 0.060 |
| 1.6 | 50 | 7 / 1 | 71 / 29 | 0.089 / 0.084 |
| 1.6 | 80 | 39 / 1 | 53 / 22 | 0.087 / 0.078 |
| 2.0 | 65 | 31 / 1 | 41 / 24 | 0.055 / 0.087 |
| 2.0 | 50 | 50 / 1 | 68 / 33 | 0.070 / 0.096 |
| 2.0 | 80 | 29 / 1 | 42 / 16 | 0.081 / 0.056 |

**The #245 finding.** A throwaway runner reproduced the gap 1.2, centres 65 row exactly, in all three repeats, then spawned each side alone (measured, one seed). Its round n is cycle n here.
- 97.86% of the shortfall from free travel remains with each army alone: mean travel share 0.131 with both armies, 0.150 alone, over six side-rounds.
- Of 5,708,947 refused candidate moves, 5,686,057 had a friendly first blocker (99.6%) and 22,890 an enemy one (0.4%). First-blocker counts can repeat a unit and hide later blockers.
- This supports mostly self-crowding in this scenario. It doesn't divide the blame among contact, turning, fan-in and the move check.
- No case reached the finding's 0.60 travel bar, so no fix is established.
- The runner accelerates ticks in a synchronous loop. The finding claims nothing about rendering, frame time or ladder height.

**Owner decision** (Producer session, 2026-11-06). The owner replied "1. Yes" to "Should I go ahead with publishing the speed test, labelled "armies mostly stuck, movement unsolved"?" This ADR records that and doesn't reopen it.

## Decision

**A run that misses `PassShare` or `MeetRows` is no longer invalid, and neither threshold exists any more. Pass share, meet share and mean travel share stay measured and reported per rung and side. One fixed layout, gap 1.2 and centres 65, serves every rung at scenario version 3. The tick 1,800 check stays. Every entry and its PR say "armies mostly stuck, movement unsolved" beside the travel figures until a measured result and a later ADR remove those words. Nothing from #245 is adopted, and movement stays open.**

### What this supersedes

Only these parts of ADR 0017, whose text isn't edited (AGENTS.md §9):

1. **Decision summary.** The sentence requiring 90% to pass and a spawn row to meet.
2. **What this supersedes, item 1.** The words "so the armies pass each other once per cycle", which the trial contradicts. The scenario line now reads:

   > the scenario: N/2 units per faction spawn in blocks on offset lanes, one each side of the plane's centre line. Each side is ordered along its own lane to alternating far ends on a fixed cycle. Entries report how many units pass the other army and how far they travel. Once combat exists, units fight but can't die, so the count stays at N. The random seed is fixed.

   ADR 0017 also adds "two reasons" to ADR 0004's Invalid runs. The missed rule goes. What remains is a run that can't show its figures: the window-close case and malformed counters, both below.
3. **Layout.** The trial-range column and the sentence letting #232 use it, the `PassShare` and `MeetRows` rows, "Why 1.1 to 2", and "The crossing rule then catches it" under Speeds. A speed change now shows only in the figures.
4. **The crossing rule.** The two numeric requirements, the *90%* reason, and the meet floor and what rests on it in "How much the lanes may reduce contact". The cycle numbering and the definitions of passed and met stay.
5. **Where the rule is measured.** The EditMode item and "A run that misses the rule is invalid". The window-close item stays, with a new reason (below).
6. **"If a trial can't meet the rule".** All of it.
7. **Consequences.** The seam-jam risk, the "trial stops" trigger, "raises the meet floor" in the ORCA trigger, and the note about telling the owner the floor's size.
8. **Files for #232.** Amended below.

ADR 0017 otherwise stays in force: the lanes, seam goal lines, spawn, orders, rules 18 to 20, order renewal, movement cost and the series rules. ADR 0004's pass rule and regression check and ADR 0009's cut-off don't change.

### Crossing figures

- **Measured as before.** Cycles, passed, met, sampling and travel share keep ADR 0017's definitions and rules 18 to 20, in every ladder Player.
- **Reported as before.** For each rung and side, the entry gives the worst judged cycle's pass share, meet share and mean travel share.
- **A low figure has no consequence of its own.** It doesn't invalidate a run, fail a step, stop #232 or fail QA. A later PR whose entry lowers a share isn't blocked, but its description quotes both entries' figures, because nothing else would flag it.
- **Still invalid: missing or malformed counters.** That run can't report its figures. It is a harness fault, not a movement result.
- **The crossing fields.** `PassShare` and `MeetRows` are removed from `LadderScenario`, `PerformanceLadder.asset`, the Player metadata and every check. A threshold nothing enforces would be dead data that reads like a rule (AGENTS.md §4). `MeetDistance` and `MeetSampleTicks` stay at 2 and 15, because they define the meet share. Only validators, tests and metadata read the two removed fields, so no unit behaves differently (checked in #232's code).
- **The EditMode test.** `SeededOrdersKeepEveryUnitActiveAcrossTheWholeWindow` drops its two threshold assertions. It keeps order continuity, renewals, batched determinism, the `units` count and zero allocations, and now asserts that `passed` and `met` lie between 0 and `units`. `CrossingCountsWholeArmiesAndClearsMeetFlagsAtEachBoundary` stays, so the counters are still tested.
- **The "If a trial can't meet the rule" path.** It is gone, because nothing can be missed. No more trials run. PR #242 still lists the nine pairs above as evidence, and they aren't entries.
- **One added figure: cycle 0's travel share, per rung and side, in its own column.** ADR 0017 leaves cycle 0 unjudged, and its reported figures then look better than the run. In #245's control the second side's cycle 0 travel share is 0.0045, against 0.079 for its worst judged cycle, and simulation seconds 10 to 20 of that cycle fall in the measured window (measured, #245's runner). `LadderCrossing` already counts cycle 0, and `Finish` doesn't read it (checked), so this adds no measuring work.
- **What travel share is.** Endpoint displacement in any direction. It misses a unit that moved and came back, and it can't tell sideways shoving from progress along the lane.

### Window close: the tick 1,800 check stays

The check, its three cases and its code stay as ADR 0017 wrote them. Only the reason changes, from "can't show the rule" to "can't show the figures":

1. The figures cover two complete cycles. A run that closes its window before tick 1,800 doesn't have them for that window, and the drain would finish them outside it.
2. It is the only check that a run simulated what it timed. `TickClock` drops backlog, so a stall can lose ticks with healthy frame times (ADR 0017's example closes near tick 1,742 with a 1.25 ms average, arithmetic). Dropping the check would loosen what a passing step has shown, and this ADR must not do that (judgment).
3. It stops nothing today: all nine trial runs closed at tick 2,097 (measured).

It isn't a movement test, because a fully jammed army still reaches tick 1,800. The conditions and medians of ADR 0004's pass rule, and its regression check, stay as written.

### Layout and version

One set of values serves all nine rungs. There are no trial ranges and no per-rung values. Any other value needs a superseding ADR.

| Field | Value |
|---|---|
| `LaneGap` | 1.2 |
| `FirstSpawn.x`, `SecondSpawn.x` | −40, +25 (centres 65 apart) |
| `OrderCycleSeconds` | 20 |
| `FirstOrderEnd.x`, `SecondOrderEnd.x` | −120, +120 |
| `OrderSpread` | 7 |
| `MeetDistance`, `MeetSampleTicks` | 2, 15 |

These are ADR 0017's start values. The trial figures give no reason to leave them:
- No pair is close to the old rule: at most 67 and 2 of 1,000 passed, and no travel share reached 0.13 (measured).
- Gap 1.2, centres 50 has the best lower travel share of its two sides, 0.088 against 0.079. It and gap 2.0, centres 50 are the only pairs whose meets clear the old floor on both sides (67 / 37 and 68 / 33 against 32), where 1.2 / 65 misses by 4 on the second side. But they pass fewer first-side units (28 and 50 against 61). Leads this small, from one run of one seed, don't justify moving the layout (judgment).
- Gap 1.2, centres 65 has the most first-side meets (96) and the only repeated evidence and displacement data, from #245's control (measured). ADR 0017's arithmetic for it still holds: both centres reach x = 0 ten seconds into each cycle.

**The series is scenario version 3.** It doesn't bump: no version 3 entry exists, ADR 0017 kept trial runs out of entries, and #232's asset already holds these values (checked). After the first version 3 entry merges, any layout change means version 4 (ADR 0017). Entries on `main` stay unedited, and versions 1 and 3 stay not like for like.

### The label

The words are exactly `armies mostly stuck, movement unsolved`. They appear in every entry from this series, and in the description of every PR that adds or quotes one.

| Where | Form |
|---|---|
| Entry, height line | `Ladder height: <n> (armies mostly stuck, movement unsolved)` |
| Entry, line above the crossing table, which holds the travel figures | `Crossing shares, armies mostly stuck, movement unsolved (worst judged cycle across valid attempts; first / second side):` |
| Entry, movement cost caption | `Movement cost at 2,000 units, armies mostly stuck, movement unsolved (development Player; separate from release frame times):` |
| Entry, JSON summary | `"movementStatus": "armies mostly stuck, movement unsolved"` |
| PR description, starting with PR #242 | The same words next to every quoted height and travel figure |
| `docs/performance/unit-ladder/README.md` | The same words and the removal condition |

`tools/unit-ladder.js` writes them from one constant, unconditionally. It doesn't read the figures, so no result can drop them. `tools/unit-ladder.test.js` asserts each entry form. The reviewer checks the PR description.

**The words go only when both of these hold:**

1. **A measured result.** One full ladder entry in which every rung, each side and all three valid runs meet this bar, and each of those runs showed both cycles:

   | Figure | Cycles | Bar |
   |---|---|---|
   | Mean travel share | 0, 1, 2 | At least 0.60 |
   | Passed | 1, 2 | At least 90% of the side, rounded up |
   | Met | 1, 2 | At least ceil(√(N/2)) units |

2. **A later ADR.** An accepted ADR names that entry, shows it meets the bar, and removes the words. That entry keeps them, because entries are never edited.

Frame-time results aren't part of this bar. The words are about movement only.

Why this bar (judgment):
- 90% and one row are ADR 0017's numbers, kept only here. They are what the scenario set out to show.
- 0.60 is the bar #245 fixed before it ran. The largest single displacement in #245's control is 51.6 of 50 cells, about 3% over free travel (measured). With that ceiling, a mean of 0.60 means fewer than half the units can sit at a tenth of free travel or less, so "mostly stuck" stops being true (arithmetic).
- Cycle 0 is in because #245 measured a case that clears later cycles and not that one: own spots and safe step, second side alone, 0.006 / 0.896 / 0.975.

**None of these removes the words:** rewording, shortening or moving them away from the figures, a footnote, a new scenario version, a layout change, passing frame times, or a result from a spike, trial or throwaway harness. A later ADR that changes the bar must give measured evidence for it, and the words stay until an entry meets the new bar.

### What the height and frame times can and can't say

No version 3 frame time exists yet, so nothing here predicts the height.

**How little the height then includes.** At 2,000 units on this layout:

| | First side | Second side |
|---|---|---|
| Passed, worst judged cycle | 61 of 1,000 (6.1%) | 1 of 1,000 (0.1%) |
| Met, worst judged cycle | 96 of 1,000 (9.6%) | 28 of 1,000 (2.8%) |
| Mean travel share, worst judged cycle | 0.099 | 0.079 |
| Mean travel share, cycle 0 | 0.339 | 0.0045 |
| Median net displacement, cycles 1 / 2, cells | 0.066 / 0.292 of 80 | 4.622 / 0.000 of 50 |

The first three rows are #232's Player and #245's control, which match exactly (measured). The last two come from #245's runner only. They are taken to describe the same motion because its counters match (inference). Contact between the armies is almost absent: removing the enemy recovers only 0.019 of free travel (arithmetic from the measured 0.8686 and 0.8499 shortfalls), and 0.4% of first blockers are enemy. So the height describes two armies jammed mostly on themselves. It doesn't describe armies crossing.

**They can say:**
- *A frame-time result for N active units.* A stuck unit is still active under ADR 0004: it holds an order, runs every per-unit system and is drawn. `activeUnitsValid` was true in all nine trial runs (measured). A `pass` means this commit, on this PC, kept those frames within the rule.
- *The drawing part.* Every unit is drawn each frame whether it moves or not, so drawing carries over to moving armies (inference).
- *That stuck units aren't skipped.* In #245's control about 1.6 candidate moves were refused per unit per tick (arithmetic: 5,708,947 over 1,800 ticks × 2,000 units; refusals can repeat a unit). Whether a refused unit costs more or less than a free one isn't measured.
- *Trends within the series.* The regression check runs unchanged against the newest version 3 entry with the same hardware summary.

**They can't say:**
- *"N units move smoothly."* So no labelled entry may be cited as meeting the vision target, even on a reference-class PC. This limits the claim only: ADR 0004's text, its pass rule and each step's `result` don't change.
- *The cost of free movement or of armies mixing.* A jam and a moving column have different neighbours and refusals, and nobody has measured which way a fix moves frame time. #245 can't answer this, because its runner has no rendering and claims no frame time. The movement cost table has the same limit, on top of ADR 0017's "What these numbers can't do".
- *That a later frame-time change is only per-unit cost.* A PR that frees the armies changes the workload. ADR 0004's regression rule still applies to it as written.
- *The state of each rung.* The words describe the series, and they come from the 2,000 rung. No Player has reported a lower rung on this layout, and in the Editor the 10-unit case met the old rule (measured). Each rung's own shares say how much it moved.

### Nothing is adopted, and movement stays open

Own spots, largest safe step and wider spawn stay out of the game and out of the scenario. Every case missed its bar, #245 makes no recommendation, and its code is throwaway. Each side keeps one shared goal, the move check and spawn pitch stay as they are, and the cause of the jam isn't decided here.

**Triggers for a superseding ADR:**
- A measured test of a movement change meets its bar, and someone proposes it for the game. Follow-up test #247 is one possible source. This ADR doesn't wait for it and assumes no outcome.
- ORCA lands (ADR 0008 Issue 5). As ADR 0017 says, its PR reports these figures and a superseding ADR decides the lanes. That ADR also decides whether any floor returns.
- A ladder entry meets the bar above.
- Someone wants a threshold back on any share, or any other layout value.

## Alternatives considered

| Option | How it works | Evidence | Cost at 2,000 units | Complexity | Risk and reversal |
|---|---|---|---|---|---|
| **A. Report only, labelled (chosen)** | As above | The nine pairs, #245, the owner's answer | The counters as built: one sampled grid search per unit not yet met, one read per unit per cycle. 0 managed bytes after warm-up (measured, Editor only). No GPU cost | Less code than ADR 0017 | The height gets quoted without the words, so they sit on its line. Easy to reverse |
| **B. Keep the rule, hold #232** | No entry until movement is fixed | Ruled out by the owner's answer. No tested fix meets the bar | None added | None now | An open-ended block, and the ladder keeps timing units that hold position |
| **C. Lower the thresholds to what runs reach** | `PassShare` falls to about 0.001 | The second side's 1 of 1,000 | As A | As ADR 0017 | A rule that gates nothing and reads like a result. Rejected |
| **D. Publish without the words** | Figures only | The owner's question names the words | As A | Lowest | "Ladder height: 2000" would read as moving armies. Rejected |
| **E. Put a #245 case in first** | Own spots, safe step or wider spawn go into the game or scenario | All missed the bar. With both armies, wider spawn's travel is 0.072 to 0.526, and own spots with safe step 0.003 to 0.868. Safe step's check p99 is 0.80 to 1.17 ms alone, over its 0.5 ms line (instrumented runner) | Unmeasured in a Player | A movement decision with no ADR behind it | Rejected: nothing is established |
| **F. Remove the counters too** | Nothing measured | None | Saves the two small jobs | Lowest | Nothing would show whether armies move. Rejected |

## Consequences

- **Benefits.** #232 can finish. The ladder gets a series with units under orders, and it says plainly what they do.
- **Tradeoffs.**
  - No automatic alarm remains if lanes drift apart or the jam worsens. The floor was that alarm, and now only the figures and the PR's quote show it.
  - The ladder still doesn't test S3.4 (two groups pass through each other), which stays with ADR 0008 Issue 5.
- **Risks.**
  - The words can get lost when a height is quoted elsewhere, including in reports to the owner. The Producer keeps them with any quoted height.
  - The first entry after a fix may look like a regression against a jammed baseline. ADR 0004's rule handles it as written, and this ADR doesn't soften it.
  - The bar is judgment and untested at low rungs, since no Player has reported them on this layout.
- **Reversibility.** Easy. A new ADR and scenario version can bring back a rule, and old entries stay.
- **Dependencies and follow-ups.** No package, Unity or handbook change. This ADR's PR is documentation only, with no change under `Game/` or `tools/`. #232 does the work below, and no new Issue is needed.
- **Not decided here.** The cause of the jam, the Editor and Player renewal difference (ADR 0017), and the 60-per-second 2 ms frames (ADR 0017).

### Files for #232

Against ADR 0017's list:

| File | ADR 0017 said | Now |
|---|---|---|
| `Game/Assets/Code/Presentation/Diagnostics/LadderScenario.cs`, `Game/Assets/Data/PerformanceLadder.asset` | Version 3, fields with range checks, goal lines, spread along x | Version 3, goal lines and spread stay. `PassShare` and `MeetRows` go. `Validate` requires the layout values above exactly, not ranges. The asset is regenerated by the builder |
| `Game/Assets/Code/Presentation/Diagnostics/PerformanceLadder.cs` | Spawn z, wait mode, crossing counts and window-close tick in metadata | The same, minus `passShare` and `meetRows`. `Finish` also writes cycle 0's two counts in their own field, so the four judged rows and their checks don't change |
| `Game/Assets/Code/Simulation/` | The crossing jobs | No change |
| `Game/Assets/Code/EditorTools/LadderProfileAnalysis.cs` | Job time and a wait line | The same, and `Analyze` drops `passed >= 900` and `met >= 32`, which would otherwise throw on these runs. `crossingValid` now means well-formed counters |
| `Game/Assets/Code/EditorTools/PerformanceLadderBuilder.cs` | Not listed | `-ladder-lane-gap`, `-ladder-spawn-distance` and `-ladder-trial` go |
| `Game/Assets/Code/Tests/EditMode/Presentation/LadderFrameBufferTests.cs` | Lines become the crossing rule, the stall assertion goes | The EditMode changes above. `LayoutRejectsValuesOutsideTheAdrTrialRanges` becomes a check that rejects any other value, including the old trial values |
| `tools/unit-ladder.js` | Invalid reasons, replacing an unchecked run, crossing and cost in the entry, fixed sentences | `checkCrossing` drops the 90% and row checks and its `passShare` and `meetRows` checks, which would otherwise invalidate every run. Malformed counters and the tick check stay. `movementCost` no longer refuses a miss. The label constant, the cycle 0 column and its allow-list entry are added. The fixed sentence loses "at least 90%", "one spawn row" and "scrape along a seam", and says the shares are reported, not required |
| `tools/unit-ladder.test.js` | Its tests | A run with `passed: 1` and `met: 0` stays valid and shows its shares. Malformed counters stay invalid. Each label form and the cycle 0 column are asserted |
| `docs/performance/unit-ladder/README.md` | Scenario text, version 3, rule 2, new commands | The same, plus the label and removal condition. "A missed rule makes the run invalid", the 90% and row requirements and the trial paragraph go |
| `docs/performance/unit-ladder/2026-11-06-4f80460.md` | Removed, with the version 3 entry added | The same. PR #242 also carries the label and lists the nine pairs |
