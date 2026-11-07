# ADR 0022: The held-tangent tests assert what the rules guarantee

## Status

Accepted

## Date

2026-11-07

## Context

Issue #261 is PR 3 of [ADR 0019](0019-vehicles-move-by-disc-clear-steps.md), with [ADR 0020](0020-wall-tangent-ships-with-the-motor.md)'s wall tangent and [ADR 0021](0021-wall-hold-ends-when-the-route-step-clears.md)'s rules 32 and 33. The gameplay-programmer's report at dd8430e is untrusted and wasn't re-run for this ADR. It says 54 of 56 targeted EditMode tests pass, including all three `SmallVehicleRoundsWallEndConcaveCornerAndOneCellGap` cases and both tests ADR 0020 names. Two `MovementCoreTests` cases fail, and nothing was tuned. "Checked" means read from the branch's code at dd8430e. Nothing was run.

| Test | Expected | Reported |
|---|---|---|
| `ShallowRouteRefusalDrivesAlongTheRowOnTheNextTickWithoutReleasing`, ADR 0021's stall-pose case | Heading exactly 0 from tick 3 | −7.4505806 × 10⁻⁹ rad at tick 3. Position (0.2663419, 1.370953), clearance 0.0174, held 3 ticks, refused only on tick 1 |
| `ClearRouteReleasesEvenWhenTheGateNeighbourOrPlaneStopsTheHeldStep(2)`, the plane case | Release | No release |

**The stall-pose case matches ADR 0021's trace except in the heading's last bit.** Tick 2 drives at −4°, to x 0.1330 with clearance 0.0174. Tick 3 drives along +x, to x 0.2663.
- The hold is exactly along the row (checked). The held point is (0, 1) and the start (0, 1.3803), so the normal's x is exactly 0, the stored tangent is exactly 0 and the drive direction is exactly (1, 0).
- The turn stage doesn't assign the drive direction's angle. It adds `atan2(sin(a − h), cos(a − h))` to the heading h (checked). From −4° that comes back one float step short. The reported value is exactly −2⁻²⁷, the spacing of floats between 1/16 and 1/8, where 4° (0.0698 rad) lies (arithmetic).
- No ADR promises more. ADR 0019's turn stage turns "by at most `TurnSpeed × TickDuration`", and rule 24 allows "one tick's turn at most". Only rule 27's arrival turn "ends on the exact axis", and that is PR 4. ADR 0021's "exactly along the row" came from a hand trace in real numbers. It isn't a property of the turn the ADRs specify. The code isn't wrong, and the design has no gap.

**The plane case expects what rule 32 doesn't give.** The test was added in this branch for rule 32. It reads the release byte that ADR 0021 introduced, no such test exists on `main`, and ADR 0021's table doesn't list it (checked). It puts a held small vehicle at the +x plane limit on an empty map, with tangent +x and route +x. The plane cut makes the step zero, so rule 32 tests the route's step. That step crosses the plane limit, and `StaticClamp.NoWorse` refuses any step past the radius-limited plane before it reads the map (checked, on `main` too). So no release is written, as rule 32 says: release needs "`StaticClamp.NoWorse(endpoint + step, endpoint, r, Walkable)` accepts the step". Rule 32's "before any neighbour or plane-edge limit" sets only the length of `s_r`, not what `NoWorse` tests.

**Constraints.** Rules 21 to 33 hold. No bar moves (#249). ADR 0021's trigger stands.

## Decision

**Both failures are tests that claim more than the rules guarantee. No code, rule, constant or bar changes. The stall-pose case asserts the hold exactly and the heading within rounding of it. The plane case keeps its claim with a route the plane doesn't refuse, and its old route moves to a new test that expects the hold to stay.**

### What this supersedes

Only one sentence in ADR 0021's "Files and tests for PR 3", in the stall-pose row: "From tick 3 its heading lies exactly along the row". The table below replaces it. ADR 0021 isn't edited (AGENTS.md §9), and the rest of that row stays.

It also corrects one line of ADR 0021's reasoning that no rule depends on. Under "Holds stay at walls", "meets a blocked cell" reads "meets a blocked cell or crosses the plane limit", because rule 32's test is `NoWorse`, which treats the plane limit as a wall. A released vehicle whose route crosses the limit would be cut to zero anyway (ADR 0019, "Plane edges"), so keeping the hold there changes nothing.

### Tests for PR 3

Gate 6 reads this table with ADR 0020's and ADR 0021's as the list of test changes for PR 3.

| Test in `MovementCoreTests` | Change |
|---|---|
| `ShallowRouteRefusalDrivesAlongTheRowOnTheNextTickWithoutReleasing` | From tick 1 the stored tangent is exactly 0. From tick 2 the drive direction is exactly (1, 0). From tick 3 the heading is within 10⁻⁶ rad of 0, and z stays within 10⁻⁵ cells of its value after tick 2. Every other assertion stays exact on all 12 ticks: refused on tick 1 only, a pose change from tick 2, held every tick, and both safety checks |
| `ClearRouteReleasesEvenWhenTheGateNeighbourOrPlaneStopsTheHeldStep` | Case 2's route is (0, 4), along the plane edge. Its tangent stays +x, so the plane still stops the held step, and it must release. Cases 0 and 1 don't change |
| New: `RouteStepPastThePlaneLimitKeepsTheHold` | Case 2's old setup: the +x plane limit, tangent +x, route (4, 0), empty map. The position doesn't change, there is no refusal and no release, and the hold, its point and its tangent stay |

A heading that the turn stage computes is asserted within 10⁻⁶ rad, as every other turned heading in `MovementCoreTests` already is (checked). Exact equality stays for values the motor stores or keeps unchanged, such as a tangent, a held point or a zero step's pose, and for values that are exact in float arithmetic, such as the axis of angle 0.

### Why this lowers no bar

All arithmetic unless marked.
- *The row's claim stays exact.* The test says the vehicle drives along the row and is never refused again. Refusals, the hold, the pose change and both safety checks stay exact. The drive direction, which is what the hold decides, is asserted exactly.
- *The tolerances sit far inside the gates.* 10⁻⁶ rad is 5.7 × 10⁻⁵ degrees. Gate 3's slide bar and gate 13's drawn bar are both 1°, about 17,000 times wider. The z tolerance, 10⁻⁵ cells, is 1,000 times inside gate 3's lateral bar of 0.01 cells.
- *A heading error isn't a slide.* The step moves along the heading itself, so heading and displacement agree to rounding whatever the heading is (checked, `DiscClearStepJob`).
- *It doesn't build up.* Each tick aims again at the same stored tangent, so the error is one rounding of the remaining turn, not a sum. At 2⁻²⁷ rad a 0.133-cell step moves z by 10⁻⁹ cells, under half the float spacing at z = 1.37, so z doesn't change. If a heading ever did bring the disc into the row, `NoWorse` would refuse the step and set a new hold, so safety never rests on it.
- *The plane scenario is still tested.* The clear-route case keeps the claim in the test's name, and the old route keeps its own test with the result rule 32 gives.

### Rules for hot paths

None change. Rules 21 to 33 stand.

## Alternatives considered

| Option | How it works | Evidence | Cost at 2,000 units | Complexity | Risk and reversal |
|---|---|---|---|---|---|
| **A. Correct the test text (chosen)** | As above | The reported figures match ADR 0021's trace, and the heading's error is exactly one float step (checked, arithmetic) | None: tests only | About 25 test lines | None to the game. Easy to reverse |
| B. Land the turn exactly | When the remaining turn is within one tick's turn, set the heading to the drive direction's angle, as rule 27 does for arrival | ADR 0019's text allows it. Unmeasured | One select per vehicle per tick. Every moving vehicle's heading bits change, and so does the fixed scenario hash. Headings would also wrap to ±π, which today they never do (checked) | Small, but in the motor | A motor change made to fit a hand trace. It isn't in ADR 0021's file table for `TurnVehiclesJob`, and it isn't the turn #252 ran. PR 4's arrival turn may revisit it |
| C. Assert exactly from tick 4 | The next turn happens to land on 0 (arithmetic) | — | None | None | Rests on rounding luck, and still changes ADR 0021's text |
| D. Leave the plane out of the release test | A second `NoWorse` without the plane check | — | A second static test | Low | Breaks rule 32's text, and gains nothing: a released vehicle whose route crosses the limit is cut to zero |
| E. `[Ignore]` either case, or accept the failure | — | — | — | — | Lowers a bar (#249), and gate 6 fails |

## Consequences

- **Benefits.** PR 3 keeps every assertion ADR 0021 meant, and gains a test that pins rule 32's behaviour at the plane limit.
- **Tradeoffs.** About 25 more changed test lines in PR 3.
- **Risks.** The stall-pose case failed at tick 3, and NUnit stops at the first failed assertion, so its ticks 4 to 12 haven't run. If any assertion in the three tests above fails after this change, ADR 0021's trigger applies: the programmer stops, reports figures, and changes nothing else.
- **Issue #261.** Its acceptance criteria don't change.
- **Dependencies.** No package, Unity or handbook change. This ADR's PR is documentation only.
