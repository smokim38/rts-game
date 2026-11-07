# ADR 0021: A wall hold ends when the route's own step clears

## Status

Accepted

## Date

2026-11-07

## Context

Issue #261 is PR 3 of [ADR 0019](0019-vehicles-move-by-disc-clear-steps.md), and it carries [ADR 0020](0020-wall-tangent-ships-with-the-motor.md)'s wall tangent. The gameplay-programmer's report at 039ef0c is untrusted and wasn't re-run for this ADR. It says the two `main` tests that ADR 0020 names now pass, and so do the one-cell gap, 80° tangent, release, short-route and hold tests. Two of ADR 0019 row 5's new fixtures, cases 0 and 1 of `MoveOrderTests.SmallVehicleRoundsWallEndConcaveCornerAndOneCellGap`, end in a group stall. Every safety and no-slide assertion still holds in both:

| Fixture | Blocked (world) | Order | Stall pose | From goal | Held ticks |
|---|---|---|---|---:|---:|
| 0, wall end | x 0 to 1, z −4 to 4 | (−4, 0.5) to (4, 0.5) | (0.8226, 4.3802) | 5.015 | 184 |
| 1, concave corner | That, plus x −4 to 0, z 3 to 4 | (−2, 2) to (4, 2) | (−4.3801, 3.6755) | 8.546 | 185 |

The programmer tuned nothing and stopped, as ADR 0020 requires.

**Why they stall.** This comes from the branch's code, worked by hand. Nothing was run. "Checked" means read from the code or worked out from it. "Inference" means it would need a run to confirm.

At both poses a small vehicle (r = 0.3536, step s = 0.1333) stands clear of a flat face by c. Its route meets that face at a shallow angle α. The field directions come from `BuildFlowFieldJob`'s lowest-cost legal neighbour, the lit flags from `BuildSight`, and the blend from `FlowFieldReader.Sample` (all checked):

| Fixture | Face | c | Route | α |
|---|---|---:|---|---:|
| 0 | z = 4, the wall's top | 0.0267 | Cells (129, 131) and (129, 132) are lit, so they steer by the bearing to the goal, weight 0.32. Cell (128, 132) is unlit, because wall cell (128, 131) breaks its diagonal, so it points +x, weight 0.60 | 16.0° |
| 1 | x = −4, the top bar's end | 0.0265 | Cell (123, 131) points +z, weight 0.73. Cells (123, 132) and (124, 132) point +x, weight 0.18. None is lit | 13.6° |

Every tick then runs the same way (checked against the code, which follows rules 30 and 31 as written):
1. The route's step moves the disc s sin α toward the face, 0.0367 and 0.0314 cells. That is more than c, so `NoWorse` refuses it. The step finds the nearest blocked point, straight across the face at h = r + c, and Commit sets the hold.
2. On the next tick, the turn stage's release test asks whether the route's ray passes within r of that point. The ray passes at h cos α, 0.3655 and 0.3694, which is more than r. So the hold ends before it is used, the drive direction is the route again, and step 1 repeats.

The pose, heading, route and map never change, so this is a fixed point. The flag is set at the end of every tick, which fits 184 and 185 held ticks against the group stall's 180 (inference).

**The gap is in the design, not in the code or the fixtures.** ADR 0019 sets the hold when a step is refused, and ends it when the route's ray misses a point at radius r. A step is refused when c < s sin α. The point test ends the hold when c > r / cos α − r. Both are true at once for any route within about 34° of a face for small vehicles, or about 13° for large ones, over a band of clearances (arithmetic). Most routes round a wall end at angles like these. The fixtures are ordinary geometry: a wall end, and a pocket whose way out passes a wall end. The code matches the ADR text for the held point, the tangent's side and tie, the search radius and the release test.

**Constraints.** Rules 21 to 31 hold: one pose writer, no slide, no neighbour or map read in the turn stage, no order between units and no allocation. No bar moves (#249). ADR 0020's stop rule stands.

## Decision

**A hold ends when the static test that set it accepts the route's own step, taken from the pose where the vehicle starts its next tick. The step makes that test, because it already reads the map. The turn stage reads only the flag. A hold also ends with its order. Everything else in ADR 0019 and ADR 0020 stays.**

### What this supersedes

Only these parts. ADR 0019 and ADR 0020 aren't edited (AGENTS.md §9).
- ADR 0019, "Tangent at contact", *Original route*: the release clause, "the hold ends when that route's ray no longer meets the held point at the vehicle's own radius". `NewVelocity` still isn't overwritten, and release is still judged on it.
- ADR 0020, rule 31: "while the route's ray still meets the held point at the vehicle's own radius", and its last sentence. Rule 33 replaces both. The rest of rule 31 stays: there is one drive direction a tick, the turn stage writes it once and the gate reads it, and speed and both short-step tests use `|NewVelocity|`.
- ADR 0020, the row 3 test "the hold ends once the route's ray clears the held point", and option A's "A held vehicle does one ray test a tick".

Rule 30 stays as written: the step still finds the held point and Commit still stores it, and the tangent still comes from it.

### Rules for hot paths

These continue rules 21 to 31.

32. **The step decides release with the test that refused.** Take a vehicle whose hold applied this tick and whose step wasn't refused, including a step the gate stopped and a step cut to zero. `DiscClearStepJob` tests one more step: the route's step from the endpoint the job writes, which is the start pose if the vehicle didn't step. The step is `normalizesafe(NewVelocity) × s_r`. `s_r` is `min(type speed, |NewVelocity|) × dt`, capped by the distance from that endpoint to the vehicle's target, as the step's speed rule computes it, before any neighbour or plane-edge limit. The step writes release to its own row in either of two cases: `s_r` is under the short-step distance, or `StaticClamp.NoWorse(endpoint + step, endpoint, r, Walkable)` accepts the step. The test reads no neighbour and writes no pose.
33. **A hold belongs to its order and to moving.** On a refusal, Commit sets the flag as in rule 30 and stores the vehicle's `OrderVersion` with it. On any other tick it keeps the flag only if the hold applied and the step didn't write release. The turn stage applies the hold, so that the drive direction is the held tangent, only if all of these are true: the flag is set, the vehicle is `Moving` with a nonzero route, and the stored order version equals `OrderVersion`. Otherwise the drive direction is `NewVelocity`. The turn stage makes no ray test.

### Why this closes the gap

All arithmetic unless marked.
- *A hold can't end where its route was refused.* `PreferredVelocityJob` computes the route from the pose, the field and the order. So while a vehicle hasn't moved, its route doesn't change, the release test repeats the refusal, and the hold stays until the vehicle drives along its tangent. That is a change of pose. The only other outcome is a refusal along the tangent, the concave stop that ADR 0019 already accepts as "a hold, not an error", which the group stall ends.
- *Holds stay at walls.* A hold lasts only while the route's step from the vehicle's pose meets a blocked cell. So a vehicle never follows a tangent across open ground.
- *Fixture 0, worked by hand from the stall pose.* Tick 1 is refused, as it is now. On tick 2 the hold applies. The heading turns from −16° to −4°, the vehicle drives 0.133, and c falls to 0.017. The route's step from there is still refused. On tick 3 the vehicle drives along +x to x = 1.089. From there the route, now 23° below +x, clears the corner, so the hold ends. On tick 4 the vehicle turns where it stands. On tick 5 it drives at −24°, 0.027 clear of the corner, and goes on around it (inference: nothing was run).
- *Fixture 1, the same way.* The vehicle holds +z up the bar's end for four steps, and the hold ends at z = 4.21. Its route is then 70° away. It turns where it stands for four ticks and drives on the fifth, 0.026 clear of the corner. It rounds the corner with one more short hold (inference).
- *Safety doesn't change.* Release chooses only the drive direction. Every step still passes the gate, the neighbour limits, the plane cut and `NoWorse` at the endpoint the job stores. ADR 0019's proof never depended on the drive direction. A vehicle still turns and then drives, so nothing slides.
- *Commit is still the only pose writer, and determinism holds.* The test writes one byte. It reads only immutable inputs and its own endpoint, so job order can't change a result.
- *Rule 29 holds.* A held vehicle that gets a new order turns toward the new route on the order's first tick, because the old hold's order version no longer matches.

### Files and tests for PR 3

These add to ADR 0020's row 3. Gate 6 reads this table and ADR 0020's as the list of test changes for PR 3.

| File | Change |
|---|---|
| `TurnVehiclesJob.cs` | The ray test goes. The hold applies by rule 33 |
| `DiscClearStepJob.cs` | Rule 32's test, on every path that doesn't refuse, and the release byte |
| `CommitMovementJob.cs`, `MovementUnit.cs`, `MovementPipeline.cs` | The hold's order version, the release byte's array, and rule 33's flag update |
| `MovementCoreTests` | `HeldTangentReleasesOnTheTickTheOriginalRouteRayClearsThePoint` is replaced by a release test on a map. A small vehicle is held beside a blocked column with its route 16° into it. Its drive direction stays the tangent while the route's step from its endpoint is refused, and becomes the route on the tick after that step clears the column's end |
| | `HeldTangentEightyDegreesOffRouteDrivesOnSixthTick` keeps its claim and figures. It gains a blocked column 0.08 cells beyond the disc along the route, so the route stays refused and the 72° step on tick 6 is accepted. On an empty map, rule 32 would end the hold at once |
| | `SecondStaticRefusalReplacesTheHeldPointEvenOnTheReleaseTick` keeps its claim, that a refusal replaces the held point, in two cases: the hold applied, and the hold didn't apply because the order changed. In both cases the new point and the current order version are stored |
| | New: the stall pose as a unit case. A small vehicle stands 0.0267 above a blocked row with no hold, its heading and route 16° into the row. It is refused on tick 1 and changes pose on tick 2. From tick 3 its heading lies exactly along the row, and it stays held with no further refused tick |
| | New: a held vehicle given a new order, with a different `OrderVersion`, turns toward the new route on that order's first tick. A hold under `Idle` or `Hold` is cleared by Commit |
| Unchanged, and must pass | All three `SmallVehicleRoundsWallEndConcaveCornerAndOneCellGap` cases, and the two tests ADR 0020 names, with every assertion as written |

This adds about 60 to 100 changed lines to PR 3.

## Alternatives considered

| Option | How it works | Evidence | Cost at 2,000 units | Complexity | Risk and reversal |
|---|---|---|---|---|---|
| **A. Release by the step's static test (chosen)** | Rules 32 and 33 | Hand traces above. Nothing is measured, but the fixtures run in seconds in EditMode, so PR 3's own tests measure it and no spike is needed | One more `NoWorse` (3 × 3 cells) per held vehicle per tick, on top of the one every stepping vehicle makes, in place of the ray test. 4 B per unit for the order version and 1 B for the release byte, about 10 KB. The open ladder map has no refusals, so ladder cost shouldn't move (inference). Gates 5 and 10 measure it | About 60 to 100 lines in five files already in PR 3 | Easy to reverse: it changes only when a hold ends. If a fixture still fails, ADR 0020's stop rule applies |
| B. Ray test at the contact search's radius, r + `StepBound` | As now, but a hold can't end before the vehicle moves | Hand trace on fixture 0's face: three tangent steps, then the hold ends, the route's step is refused and the hold starts again, every 4 ticks | As today | One constant | Wall following runs at 75% speed with a 12° flick every 4 ticks, like #220's wiggle. Release still isn't tied to the refusal |
| C. At least one tangent step, then the ray test | A minimum hold | Hand trace: one step in two, with a 12° to 16° flick every other tick | As today | Low | Half speed along walls |
| D. End the hold when the route stops pointing into the stored contact normal | A sign test | Fixture 0: past the wall end the route still points −z, so the vehicle keeps driving along +x | As today | Low | Fails the wall end |
| E. The turn stage tests the route against the map | A swept test in the turn stage | — | A map read per held vehicle | Low | Breaks rule 30 |
| F. Clamp or slide along the wall | — | ADR 0019: "a clamp is a slide" | — | — | Breaks rule 21 and the owner's no-slide rule (#197) |
| G. Accept the stalls, or change the fixtures' assertions | — | — | — | — | Lowers a bar (#249) |

## Consequences

- **Benefits.**
  - While the route presses into a wall, a vehicle follows the wall at full speed with a steady heading, and turns toward the route only once the route's own step clears.
  - The stall both fixtures hit can't recur.
  - A held vehicle's first turn on a new order isn't delayed.
- **Tradeoffs.**
  - A held vehicle makes two static tests a tick.
  - Each unit stores one more int.
  - PR 3 grows to about 1,150 changed lines, for the reason ADR 0020 gives.
- **Risks.**
  - The traces are by hand, so the fixtures are the measurement.
  - Concave dead ends still stop until the group stall, as ADR 0019 accepts.
  - The test uses the route at the tick's start pose, not at its endpoint. A route that changes between cells at a wall can end a hold and start it again a tick later, which shows as one 12° flick (judgment: rare).
  - Gates 8 and 9 report delivered counts, which may still fall short of R+F. Those lines are reported, not gated.
- **Trigger.** If either fixture or any test listed above still fails with rules 32 and 33, the programmer stops. They report the stall pose, route angle, clearance and held ticks, and change no constant, tolerance or rule. Such a failure would mean the cause isn't the gap described here, so it comes back to the architect.
- **Issue #261.** Its acceptance criteria don't change, by ADR 0020's reading of them: the drive direction is still `NewVelocity` or the held tangent.
- **Dependencies.** No package, Unity or handbook change. This ADR's PR is documentation only.
