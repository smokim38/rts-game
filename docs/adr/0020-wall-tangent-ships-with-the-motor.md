# ADR 0020: The wall tangent ships with the disc-clear motor

## Status

Accepted

## Date

2026-11-07

## Context

[ADR 0019](0019-vehicles-move-by-disc-clear-steps.md) puts the disc-clear motor in PR 3 and the tangent at contact in PR 5 ("Files for the implementation Issues"). Every PR must leave the full EditMode suite passing (AGENTS.md §8, ADR 0019 gate 6), and #249 never lowers a bar.

**PR 3 without the tangent fails two tests on `main`.** The gameplay-programmer's full EditMode run of Issue #261 at cc1adb2 (reported, not re-run for this ADR) had 6 failures. Four were its own expectation fixes. Two remain, with every assertion as on `main` (checked):

| Test | Result | Where it stopped |
|---|---|---|
| `FlowFieldPoolTests.MovingUnitRoundsSandboxWallWithoutStaticPenetration(Small)` | Group stall (rule 4), 40.92 cells from the goal, against under 0.1 | (19.59, 39.94). The wall is x 20 to 21, z −40 to 40 (checked: map half-width 128), so the disc is 0.053 cells off the face and 0.06 short of its end |
| `MoveOrderTests.ClosedRingOrderStopsWithoutOverlapsOrFurtherRequests(1, Small)` | 9.93 cells from the corrected goal (95.5, 30.5), against under 0.05 | About the ring's outer corner at (95, 40) (inference from the distance) |

Both runs kept every safety assertion, and both Large cases passed.

**This is the case ADR 0019 predicted** under "The cost of having no clamp": a vehicle whose route points into a wall holds forever, because its route doesn't change while it stands still. The old motor rounded these corners with its static clamp, which slides, and ADR 0019 removes it. Both tests assert ordinary behaviour: one vehicle sent round a wall gets there. Gate 8's safety line, a small vehicle past the wall in ADR 0014's scene, is at risk for the same reason, and #261 requires it now (inference, unmeasured).

## Decision

**The tangent at contact ships in PR 3, with the motor. PR 5 goes. PR 4, arrival facing, becomes the implementation's last game-code PR, so its ladder entry is the one that shows all of ADR 0019's gates. ADR 0019's design, gates and bars don't change.**

### Files, rows 3 to 5

This supersedes only rows 3 and 5 of ADR 0019's "Files for the implementation Issues". The rest of ADR 0019 stays in force and isn't edited (AGENTS.md §9). Gate 6's "changes to tests are only those listed under Files" reads this table for PR 3.

| PR | Change | Changed lines (estimate) |
|---|---|---|
| **3. The motor and the wall tangent** | Everything in ADR 0019's row 3 | ~1,060: ADR 0019's ~800 plus row 5's ~260. Over 400 because the motor without the tangent fails tests on `main`, and the motor can't be split (ADR 0019). The PR says so |
| | Row 5's change: tangent at contact, held to release, in `TurnVehiclesJob.cs`, `DiscClearStepJob.cs` and `MovementUnit.cs`, by rules 30 and 31 below. `CommitMovementJob.cs` and `MovementPipeline.cs`, already in row 3, carry the new state | |
| | Row 5's tests, in `MovementCoreTests` and `MoveOrderTests`: wall end, concave corner, ring, one-cell gap for small vehicles. A tangent 80° off the route drives on its 6th tick, and the hold ends once the route's ray clears the held point | |
| | Test changes that follow from row 3, now listed: `TankMeshBuilderTests.FixedMovementScenarioPreservesSimulationBytes` re-pinned once, on the PR's final head, with the old and new hash in the PR. `MovementMeasurementTests` carry the inner-margin result, the drawn rule, gate 13(a) as assertions and row 14's output path | |
| | Unchanged, and they must pass: `FlowFieldPoolTests.MovingUnitRoundsSandboxWallWithoutStaticPenetration` and `MoveOrderTests.ClosedRingOrderStopsWithoutOverlapsOrFurtherRequests`, with every assertion as on `main` | |
| **4. Arrival facing** | As ADR 0019's row 4. Its ladder entry shows all gates, and is the entry a later ADR may name | ~200 |
| **5.** | None | |

### Rules for hot paths

These continue rules 21 to 29.

30. **Only the step reads the map for the tangent.** When `StaticClamp.NoWorse` refuses a step, `DiscClearStepJob` finds the blocked point nearest the vehicle's start position, among the cells within its radius plus `StepBound`, in a fixed scan order, and writes it to its own row. Commit stores it as the held point, with its tangent on the route's side (ADR 0019, "Tangent at contact"), and sets the flag. A refusal always sets the hold and replaces any earlier point. The turn stage still reads no neighbour and no map.
31. **One drive direction a tick.** The turn stage decides it once per vehicle: the held tangent while the route's ray still meets the held point at the vehicle's own radius, otherwise `NewVelocity`. It writes that direction to its own row, and the step's gate reads it without recomputing it. Speed and both short-step tests still use `|NewVelocity|`, and `NewVelocity` is never overwritten. Commit clears the flag on the tick the turn stage released the hold, unless that tick's step was refused again.

## Alternatives considered

| Option | How it works | Evidence | Cost at 2,000 units | Complexity | Risk and reversal |
|---|---|---|---|---|---|
| **A. Tangent in PR 3 (chosen)** | As above | ADR 0019's design. Under this motor it is unmeasured, and #220's S1 under the old check delivered 56 of 110 | As ADR 0019 planned: its ~30 B per unit already counts the tangent state. The drive-direction row adds 8 B per unit, 16 KB. A search over at most 3 × 3 cells on a refused tick only. A held vehicle does one ray test a tick, and every other vehicle one flag test. No allocation. The open ladder map has no refusals, so ladder cost shouldn't move (inference). Gates 5 and 10 measure it | The same code as planned, in one bigger PR, so the review is heavier | If the tangent doesn't make both tests pass, PR 3 stops on an ADR 0019 trigger instead of merging. That costs nothing, because PR 3 couldn't merge without it. It changes only the order of work, so it is easy to reverse |
| B. Ignore or skip the two tests until PR 5 | `[Ignore]` with an Issue number | — | None | Low | Lowers two bars on `main` between merges, which #249 forbids. `main` ships a vehicle that can't round a wall end |
| C. Keep `StaticClamp.Apply` for walls until PR 5 | The old clamp moves the vehicle along the wall | ADR 0019: "a clamp is a slide" | As today | Low | Breaks the owner's no-slide rule (#197) and rule 21, and leaves two motors on `main` (AGENTS.md §4) |
| D. Re-assert both tests to accept a group stall at the wall | Change the expected outcome | — | None | Low | Lowers a bar |
| E. Merge PR 3 and PR 5 back to back | `main` fails between them, or a PR is stacked on an unmerged one | — | None | Medium | Breaks AGENTS.md §8 on `main`. Review and QA records cover one head each, and PR 3's head fails gate 6 |
| F. Tangent first, motor second | — | — | — | — | Can't work: the tangent is a stage of jobs that don't exist before PR 3 |

## Consequences

- **Benefits.** No PR leaves `main` with failing tests or a vehicle that can't round a wall. No bar moves.
- **Tradeoffs.** PR 3 grows to about 1,060 changed lines, and its review covers walls as well as the motor. The tangent ships unmeasured, as ADR 0019 already said, and gates 3, 8 and 9 measure it in PR 3.
- **Risks.** #220's S1 evidence is mixed and comes from the old check. If, with the tangent as specified, either test above still fails, or row 5's tests can't pass, that is an ADR 0019 trigger. The programmer stops and reports figures, and changes no tolerance or rule.
- **Issue #261.** Its acceptance criteria don't change. Criterion 1 names ADR 0019's "Motor" section and rule 25, which already make the drive direction `NewVelocity` or the held tangent. Criterion 3 requires gate 6 and gate 8's safety lines. Only the note "wall tangents PR 5" goes out of date.
- **Follow-up.** No PR 5 Issue. The PR 4 Issue carries the all-gates ladder entry.
- **Dependencies.** No package, Unity or handbook change. This ADR's PR is documentation only.
