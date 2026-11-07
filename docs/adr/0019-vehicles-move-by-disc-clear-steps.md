# ADR 0019: Vehicles move by disc-clear steps and keep their shape on own spots

## Status

Accepted

## Date

2026-11-07

## Context

Issue #249 asks the studio to resolve big-army vehicle movement without sliding. Issue #250 tested a motor that gets there at 2,000 units, in throwaway code, and Issue #252 re-ran it with the one number this ADR changes. This ADR plans the game version, under Issue #255, which replaces #251 after its PR #254 closed unapproved. Each figure says what it is: measured, arithmetic, inference or judgment. "Checked" means read by hand from #250's or #252's retained files, or from the code on `main`, since no code was run for this ADR.

**Read this first.**

1. **#250's 2,000-unit pass is less clean than its counts look.** The rows that meet the other army stop head-on in cycle 1 and stand still for about 16 s. My plan in #249 assumed the seam stays 1.2 cells wide. It doesn't.
2. **#252 measured the fix in throwaway code, and every kill item passed.** With this ADR's 12° gate, that jam is gone at all nine rungs. Nothing yet measures the approach in game code, and head-on still stalls.
3. **The layout is the side-by-side passing that ADR 0017 option B rejected,** apart from the distance between the blocks. See "Is this ADR 0017's option B?".
4. **S2.1 stays unresolved.** A vehicle that has to turn drives up to 0.43 s late, and rear ranks already facing their route only creep at first, which no player can see. This ADR claims no compliance. The conflict with the design text goes to the owner in the final report of #249, and S2.1 is not part of the Resolved bar. See "Response (S2.1): unresolved".
5. **One motor rule is newer than both spikes.** A pair that rounding has worn inside half the proof's margin may only open. The proof needs it, ordinary contact never reaches it, and no spike ran it. See "Safety argument" and gate 3.

**What #250 measured** (release Player, ladder PC, accelerated 1,800 ticks, no obstacles):

| Case | Result |
|---|---|
| Motor with L and M, 2,000 units, seeds 103, 7, 211 | Worst travel 0.952. Each judged cycle: First 989 passed, 34 met; Second 1,000 passed, 32 met. Slide 0.0132°, lateral 2.5 × 10⁻⁶ cells, no overlap, no static error |
| Cost at 2,000 | Cost runs: check p99 0.28 to 0.34 ms, chain p99 0.95 to 1.08 ms. Highest of any 2,000-unit run: check 0.468 ms, chain 1.29 ms. Comparative, not ADR 0012 certification |
| Job orders 1/1, 1/64, 19/1, seed 103 | Full state byte-identical |
| Nine rungs, seed 103 | All pass except 10 units: Second met 1 in cycle 2, needs 3 |
| Not measured | Game code, walls and obstacles, parked slots, ADR 0012's scenes, rendered frames, foot soldiers |

**What #250's retained positions show** (checked at 10, 50 and 2,000 units; other rungs inferred from their travel figures):

| Total units | Floor | Met, First / Second, cycle 0 | Cycle 1 | Cycle 2 |
|---|---:|---:|---:|---:|
| 10 | 3 | 4 / 3 | 4 / 3 | 3 / **1** |
| 50 | 5 | 9 / 5 | 7 / 5 | 7 / 5 |
| 100 | 8 | 16 / 8 | 10 / 8 | 10 / 8 |
| 200 | 10 | 20 / 10 | 12 / 10 | 12 / 10 |
| 300 | 13 | 26 / 13 | 15 / 13 | 15 / 13 |
| 400 | 15 | 30 / 15 | 17 / 15 | 17 / 15 |
| 500 | 16 | 32 / 16 | 18 / 16 | 18 / 16 |
| 1,000 | 23 | 46 / 23 | 25 / 23 | 25 / 23 |
| 2,000 | 32 | 64 / 32 | 34 / 32 | 34 / 32 |

- **Contact is one row.** At 2,000 units, in each judged cycle, 968 of 1,000 Second vehicles and 966 of 1,000 First vehicles aren't within 2 cells of an enemy on any sampled tick (measured counts). Without the jam, First's count is 936, because its second row then comes within 2 cells too (#252).
- **That row jams in cycle 1.** At tick 1,200, First's seam row stands at z ≈ 0.455 having moved 14.7 to 18.9 of 80 cells. Second's stands at z ≈ −0.512 having moved 9.2 to 11.8 of 50. Units 0 and 1031 are 1.0607 cells apart, which is exact disc contact (checked). Those 64 vehicles stood still for about 15 to 16 s (arithmetic). S3.4's limit is 5 s.
- **The mean hides it.** They are 3.2% of each side, so travel stays at 0.95. The 989 passed are the same jam: 11 First seam vehicles stop short of Second's mean x (inference).
- **Why it jams.** Discs of a small and a large vehicle touch at 1.0607 cells, so the 1.2 gap has 0.139 to spare. #250 lets a vehicle drive once its heading is within 45° of its route. A 180° turn at 12° per tick then drives at 36°, 24° and 12° off route, which is 0.160 cells sideways for a small vehicle and 0.100 for a large one, 0.26 together (arithmetic). The checked drift is about 0.145 and 0.09. In cycle 1 both rows turn toward the seam. In cycle 0 and cycle 2 they don't.
- **Cycle 0 is the no-jam case.** First doesn't turn and Second turns away, so the seam stays at 1.2 or wider. Every rung then meets the floor, and every vehicle passes (measured).

**What #252 measured** (same PC and harness as #250, release Player, accelerated 1,800 ticks, no obstacles). It made two changes: the drive gate goes from 45° to 12°, and a step under 0.001 cells becomes zero. Its thresholds were fixed before the run, and nothing was tuned after it.

| Case | Result |
|---|---|
| Kill rule, case 5, 17 observations | Every item passed. That is 11 distinct runs (nine rungs at seed 103, seeds 7 and 211 at 2,000) and 6 repeats for cost and job order |
| Travel | Worst mean 0.953. Lowest single vehicle 0.929, against #250's 0.184. Both are Second in cycle 0 at 2,000 units |
| Seam | Minimum gap 1.1557 cells, against disc contact at 1.0607. Longest seam-row stand-still 0.43 s (13 ticks), which is the reversal's turn, against about 16 s in #250 |
| 2,000 units, seeds 103, 7, 211 | Each judged cycle: 1,000 of 1,000 passed on both sides, met 64 First and 32 Second. Slide at most 0.0077°, lateral at most 2.0 × 10⁻⁶ cells, no overlap, no static error |
| Other rungs, seed 103 | All pass. 10 units: Second met 3 in cycles 1 and 2, against a floor of 3. Second met exactly the floor at every rung in every cycle |
| Cost at 2,000, 9 observations | Check p99 at most 0.451 ms (0.298 to 0.373 ms outside the job-order runs), chain p99 at most 1.32 ms. Comparative job timestamps in the harness, not ADR 0012 certification |
| Job orders 1/1, 1/64, 19/1, seed 103 | Full state byte-identical |
| B4, head-on, seed 103 | Still stalls: Second travel 0.241, 0.068, 0.037, longest stand-still 19 s. See "Head-on (S3.4)" |
| Not measured | Game code, walls and obstacles, offsets taken at order time (both spikes fix them at spawn), parked slots, ADR 0012's scenes, rendered frames, foot soldiers |

- **Why the jam went.** Seam drift in cycle 1 is 0.044 cells, which matches the 0.045 worked out in "The 10-unit rung". Cycle 2's low of 1.1749 is what cycle 1 leaves behind: the rows end cycle 1 at z 0.5858 and −0.5891 (checked at 10 units).
- **Limits.** Rungs below 2,000 ran one seed each, and the repeats retrace the same paths. The seam gap is the z distance between the two seam rows. It is side-by-side passing, so none of this shows crossing.

**Owner decisions that stand** (not reopened):
- Vehicles don't slide. "Packed vehicles should stay still while a crowded group spreads out" (#197).
- Soldiers on foot may sidestep (#197).
- Big groups keep their shape while travelling, rather than funnelling toward one point (#245).
- The studio resolves movement without asking about steps: "For this movement issue, I just want the studio to work on it continuously and autonomously until it gets movement issue resolved or if its working on testing the ideas provided or implementing some of the ideas to improve or help with the movement issue to just do it without asking me for every little detail or step." (Producer session, 2026-11-06, for the loop tracked in #249. Source: Issue #255's Owner instruction section.)
- Vehicles stand 1.42 widths apart: "yes, go with 1.42 STOP ASKING ME TO APPROVE I SAID!", answering "may vehicle groups stand further apart, at 1.42 vehicle widths?" (Producer session, 2026-11-07. Source: Issue #255's Owner instruction section.) It supersedes the 1.3 the owner accepted for parked groups on #202, and it covers spacing only. See "Spacing".

**Constraints.** ADR 0004's hot-path rules and pass rule. ADR 0008's 3 ms and 0.5 ms budgets, measured as ADR 0012 says. No new package. #232's PR #253 has merged (PR #242 was closed unmerged), so the crossing counters and scenario version 3 are on `main`, with the entry `docs/performance/unit-ladder/2026-11-07-8e07c94.md`. It changed neither the draw interpolation nor the order of commands and movement in a tick (checked).

## Decision

**Every vehicle turns freely, then drives straight along its heading, by a step that can't close two discs. No job ever pushes a vehicle. Blocks stand 1.42 widths apart, and every entry path requires at least the sum of radii plus the proof's 0.0001-cell margin, so discs are always clear and turning is always safe. A group order gives each vehicle its own spot, so lines stay parallel. A vehicle drives only once its heading is within one tick's turn of its route, which removes the seam jam. An arrived group turns to one common facing, so it packs within S2.2 whichever way it was sent. A drawn vehicle is either turning where it stands or driving along its nose, so no frame shows a slide. L and M are ladder layout, in scenario version 4. S2.1 stays unresolved: this ADR claims no compliance and reports the facts, and the conflict goes to the owner in the final report of #249. The owner chose the 1.42 spacing, and #252 measured the 12° gate in throwaway code, where every kill item passed.**

### What this supersedes

Only these parts, and the rest of each ADR stays in force. Earlier ADRs' text isn't edited (AGENTS.md §9).

| ADR | Superseded, for vehicles (every unit on `main` today) | Stays in force |
|---|---|---|
| 0008 | Formation pairs in Contact shapes. Integration's velocity ramp and static clamp, the overlap pass and the move check (stages 3 to 5). "Who gives way" and "Idle units make way" as pushes. `slotSpacing` 1.25 and its 1.05 to 1.3 range, and `ceil(sqrt(2N))` columns. The job table and cost rows for those stages. Rule 3's move-check clause, rule 6's spacing range, and rule 9 except that overlap is still measured on footprints. The "Discs round the whole square" alternative is reversed. Vehicles facing forward in their slots, "an idle unit keeps its heading", and S2.2's rectangle aligned with forward (see "Arrival facing"). As descriptions of this motor, for a vehicle that must turn first: "Units move from the tick their order is applied" (Latency) and "every unit has a velocity from that tick" (Response). S2.1's response checks as pass or fail: they stay printed (see "Response (S2.1): unresolved") | Fields, pool, tick rate, grid, group stall, progress, goal correction, budgets, and stage 2. ORCA still outputs `NewVelocity`, which is now a heading target. S2.1's requirement isn't superseded, and this ADR doesn't claim to meet it. S3.5 stays required |
| 0012 | The passes, levels, `moveCheckPasses`, rules 13 to 15, its lever, its tests | Budgets, runs, frame sets, statistic, the four scenes, and the guarantee, now proved by discs. *Move check* now means the end of the turn job to the end of the step job. Level metrics become held vehicles: an order and a zero step |
| 0013 | In "What movement does with a sample", the target of a vehicle with a valid spot. In rule 16, the same target. "units move from the order's tick", as a description of this motor, for a vehicle that must turn first | The bit, encoding, sampling, and rule 17 |
| 0014, 0015 | Option C's reason in ADR 0014: an arrived group no longer stands inside discs | Issue 5's gates, with "slot pitch" now 1.42 |
| 0017 | "Orders" (the goal line's z), "Why the goal lines sit at the seam", and the series number | Lanes, `LaneGap`, x values, spawn formula, the definitions of passed and met, rules 18 to 20, renewal, movement cost |
| 0018 | In "Nothing is adopted, and movement stays open": the adoption sentences, "Each side keeps one shared goal", and "the move check and spawn pitch stay as they are". "Seam goal lines" among what stays of ADR 0017. The layout table gains the rows below. Version 3 stays #232's | The label, its removal bar word for word, reporting, the tick 1,800 check. Movement stays open until the later ADR that removes the label |

### Motor

**Turn stage** (parallel, replaces `IntegrateMovementJob`). A vehicle in `Moving` mode turns toward its **drive direction** by at most `TurnSpeed × TickDuration`, 12° at today's asset value (checked). The drive direction is `NewVelocity`, or the held wall tangent (see "Walls and obstacles").
- It doesn't turn when `|NewVelocity| × dt` is under the short-step distance. So close to its target, the direction is only rounding (see "Arrival facing").
- An `Idle` vehicle with a `Facing` turns toward it at the same rate (see "Arrival facing"). A `Hold` vehicle never turns.
- No turn reads a neighbour or the map.

**Step stage** (`DiscClearStepJob`, parallel, replaces `OverlapMovementJob` and `MoveCheckPassJob`). All inputs are immutable: start poses, new headings, the grid, unit rows. Each vehicle writes only its own row.

1. **Gate.** It drives only if `Moving`, with a route, and with its new heading within one tick's turn of its drive direction. #250 used 45°, and #252 measured 12°. See "The 10-unit rung".
2. **Speed.** `s = min(type speed, |NewVelocity|) × dt`, capped by the distance to its target. There is no acceleration ramp, as measured, and a later ramp stays safe because it only lowers `s`.
3. **Neighbours.** For every live vehicle j within `r_i + LargeRadius + 2 × maxStep + 0.0001`, through the grid and never capped, where `maxStep` is the fastest type's step. `n` is the unit vector from i's start to j's start (`normalizesafe`), `c = h_i · n`, and `g = d − r_i − r_j − 0.0001`.
   - *Closing.* When `c > 0`: `w = 1` if `h_j · n ≥ 0` else `0.5`, and `s = min(s, max(0, w × g / c))`.
   - *Inner margin.* When `d − r_i − r_j < 0.00005` and `c > −0.05`: `s = 0`. A pair that rounding has worn that close may only open, so a tangent or barely opening step waits. Neither spike ran this rule.
4. **Plane edge.** `s` is cut along the heading so the disc stays inside the plane limit.
5. **Short steps.** A step under 0.001 cells becomes zero.
6. **Static.** The job works out the endpoint once and asks `StaticClamp.NoWorse` about it. If accepted, Commit stores those same bits. Otherwise the vehicle holds its position this tick.

**Commit** is the only writer of a vehicle's pose. `Velocity` is displacement over `dt`.

**Determinism and parallel rules.** Every decision reads state from before the stage and writes its own row. `min` doesn't depend on order, and "The indices have no guaranteed order" ([IJobFor][ijobfor]) therefore can't change a result. There is no single-threaded part, no second pass and no read before the sync point. #250's and #252's byte-identical streams measure this at one seed, 103. The inner-margin rule reads the same inputs, and gate 4 covers it.

### Safety argument

The step is in the style of buffered Voronoi cells, where each robot keeps to its side of a shared boundary, and whose authors prove collision avoidance for simple straight-line motion ([Zhou et al.][bvc]).

**Terms.** `m` is the margin in `g`, 0.0001 cells. A pair's clearance is `k = d − r_i − r_j`, in stored float32 positions. `e` is the most rounding can move a stored endpoint along any direction, 0.71 units in the last place. At |x| < 128 a unit in the last place is 7.6 × 10⁻⁶ cells, so `e` is 5.4 × 10⁻⁶. At the 295-cell map limit (ADR 0011, half-width 147.5) it is 1.5 × 10⁻⁵, so `e` is 1.1 × 10⁻⁵. A vehicle with a zero step keeps the same bits and adds no `e`. All other float error in a tick, in `d`, `n`, `c`, the limit and the heading's length, is under 10⁻⁶ (arithmetic).

**Every pair keeps `k` at or above the clearance floor, 2.5 × 10⁻⁵** (arithmetic, by induction over ticks).

*The start.* Every entry path gives `k ≥ m` (see "Spacing").

*A tick with `k ≥ m`, so `g ≥ 0`.* Separation along `n` changes by `−s_i c_i − s_j c_j`, with `c_j = −h_j · n`.
- Only i closes: its limit is `g / c_i` and j adds or changes nothing, so the loss is at most g.
- Both close: each sees the other closing, so `w = 0.5` on both sides and the loss is at most g.
- Neither closes: separation doesn't fall.

The two units can't disagree on the case, because `n` negates exactly in floating point. So before rounding, separation stays at least `r_i + r_j + m`, and true distance is never less than separation. Rounding across both units takes off at most `2e` plus 10⁻⁶. So `k` ends at least 8.8 × 10⁻⁵ at |x| < 128, and 7.7 × 10⁻⁵ at the map limit.

*A tick with `m/2 ≤ k < m`, so `g < 0`.* A closing unit has a zero step. A unit that isn't closing may step, and rounding can then take off `2e` plus 10⁻⁶. So `k` ends at least 3.8 × 10⁻⁵ at |x| < 128, and 2.7 × 10⁻⁵ at the map limit.

*A tick with `k < m/2`.* A unit with `c > −0.05` has a zero step and keeps its bits. A unit that steps has `c ≤ −0.05`, and the short-step rule makes its step at least 0.001 cells. So it opens the pair by at least 5 × 10⁻⁵, against at most 1.1 × 10⁻⁵ of rounding. `k` can't fall on such a tick, however many follow one another, so rounding can't build up past the floor.

*A pair beyond the search radius.* Then `g` is above `2 × maxStep`, so even two closing vehicles at top step keep `k ≥ m` before rounding. That is why the radius includes `m`.

A footprint lies inside its disc at every heading (ADR 0008), so clear discs can't overlap however they turn.

**Why each part is needed** (worked by hand in float32).
- *Bare clearance at entry fails.* Two small vehicles at (100, 0) and (100.5, 0.5) have `d² = 0.5`, so their discs touch and `k` is about 0. A 0.001001-cell step along (1, −1)/√2 has `c = 0` exactly, so the closing rule doesn't limit it. One unit in the last place at x = 100 is 2⁻¹⁷, so x rounds from 100.00070781 to 100.00070953, and the squared separation is about 0.4999993, not 0.5. The pair is now 5 × 10⁻⁷ inside discs. Requiring `k ≥ m` at entry refuses this start.
- *The margin at entry alone fails over time.* At that step size a tangent move gains `s² / 2d`, about 7 × 10⁻⁷, while rounding can lose 5.4 × 10⁻⁶. Two vehicles side by side on the same heading gain nothing at all. A route that keeps a pair like that could repeat the loss every tick until the margin is gone. The inner-margin rule refuses those steps below `m/2`, and even the touching start above can't get worse.
- *The rule starts at half the margin, not all of it.* A capped closing step lands at `k = m`, give or take `2e`, so about half of all contacts sit just under `m`. A hold at the whole margin would freeze a compressed column ordered sideways, because `c` is near 0 on every link. At `m/2`, a landed pair is still at least 7.7 × 10⁻⁵ clear, so ordinary contact never reaches the rule, and closing and tangent steps stay as #250 and #252 ran them.

**Between ticks.** Both units move in straight lines, and separation along `n` is linear in time, so every interpolated frame keeps it between its start and end values, whatever heading is drawn (arithmetic). Rounding the two drawn positions takes off at most another `2e`, which still leaves drawn discs clear: by 2.7 × 10⁻⁵ at |x| < 128, and 5 × 10⁻⁶ at the map limit. This closes ADR 0008's risk of a drawn corner contact between two vehicles. Neither #250 nor #252 measured it, so gate 13 does. Walls differ: see "Walls and obstacles".

**Overlapping discs at the start are not safe.** This is the condition the whole motor rests on (arithmetic):
- *A turn.* Two small squares 0.625 apart that both turn 45° are 0.442 apart along their own axes, under their 0.5 width, so they overlap.
- *A step.* A large vehicle at the origin faces +x. A small one at (0.83, 0.31) heads 110°. They don't overlap: their reaches along x add to 0.82. The small one isn't closing (`h · n` = +0.008), so nothing limits its step of 0.133. It ends at (0.785, 0.435), overlapping on all four axes, even though the distance grew. With equal sizes and one mover this can't happen, but mixed pairs can.

So no vehicle may ever start closer to a neighbour than the sum of radii plus `m`. See "Spacing".

**Other cases I checked.**
- *Idle and hold.* These vehicles never step, and a hold vehicle never turns. An idle vehicle's arrival turn stays inside its disc, so it can't touch anything. A mover's `w = 0.5` against an idle vehicle facing it is only conservative.
- *The gate.* It limits when a vehicle drives, never how far, so it plays no part in the proof. At 45° it causes the seam jam, and at 12° it doesn't (measured, #252).
- *Plane edges.* The cut is along the heading, so it adds no slide, and a vehicle at the limit can still turn and drive inward.
- *Short steps.* #250's B4 read 1.0489° of slide, above the 1° bar, which fits rounding on steps near 0.0003 cells (inference). Its lateral total stayed at 0.0002 cells. At 0.001 cells rounding gives at most about 0.31° at |x| < 128 (arithmetic). Without the short-step rule the bar could fail with no vehicle sliding. With the rule, #252's B4 reads 0.098° and 2.2 × 10⁻⁶ cells (measured), though #252 changed the gate too, so the two effects aren't separated.
- *The inner-margin rule.* It can only turn a step into zero, so it can't weaken anything above. Reaching it from a landed pair takes at least four ticks in a row with almost no gain at |x| < 128 (arithmetic), and in open travel side-by-side neighbours stand 0.0029 clear. So it should almost never fire (inference). A vehicle it holds waits like any closing vehicle, until its route turns or the group stall ends the order. Gate 3 reports how often it fires.
- *Measured.* All 22 of #250's motor runs and all 18 of #252's had no overlap and no static error, starting clear at 1.42. They didn't have the inner-margin rule and didn't count pairs under the floor, so they don't test the induction. Gate 3 does.

### Drawn frames

#250's and #252's slide figures are tick poses, and neither spike drew a frame. The owner's rule has to hold in the frames a player sees.

**What `main` does (checked).** `FillDrawDataJob` interpolates position and heading by the same `Interpolation` t, on every tick. This motor turns and then steps along the new heading. So on a tick that both turns and drives, a frame faces `h0 + t × (h1 − h0)` while the vehicle travels along `h1`, and the slide is `(1 − t) × turn` (arithmetic). A reversal has two such ticks, 156° to 168° and 168° to 180°. The halfway frame shows 6°, and a frame just after the tick starts shows nearly 12°. At a steady 60 fps t alternates 0 and 0.5 (`TickClock`), so the 6° frame appears on each of them. Route bends do the same at smaller angles.

**The rule.** A tick that drove draws its new heading on every frame with t above 0. A tick that didn't drive interpolates heading as today. Drawn position stays the plain interpolation.

```text
moved  = any(Position != Previous.Position)
weight = moved ? ceil(t) : t
angle  = Previous.Heading + wrapped(Heading - Previous.Heading) * weight
```

So every frame shows one of the two things the motor does: turning where it stands, or driving along its nose. The rule needs no new data, and it is still drawing between the last two ticks, as ADR 0004 sets. It adds one branch per vehicle per frame and no allocation. Its cost goes in PR 3's before and after numbers.

**With the 12° turn.** The gate and the turn stage cap a drive tick's turn at one tick's turn, so the largest snap is 12°, where a still tick already shows 6° a frame at 60 fps. A reversal draws 13 still ticks of smooth turning, then two 12° snaps as it pulls away. A straight route has no snap, and a bend snaps by that tick's small turn. Two frames either side of a tick boundary can cut the corner of the path. That is the turn itself, which no drawing can avoid, so gate 13 judges each frame against its own tick.

**With the short-step rule.** It makes `moved` exact. A step is either zero, when Commit leaves the position bit-identical, or at least 0.001 cells, so rounding can't flip the branch. On a 0.001-cell step, rounding gives at most about 0.31° at |x| < 128, and 0.62° at the map limit, which is the worst drawn slide (arithmetic) and is under the 1° bar. A creeping rear vehicle counts as driving, so its last two turn ticks snap rather than ease. A step cut to zero by a neighbour or a wall eases (judgment: neither is noticeable).

**Alternatives.**
- *Keep the plain interpolation:* fails as above.
- *Turn first within the tick, then drive:* the vehicle holds its position while its heading eases, then covers the whole step in the rest of the tick. It doesn't slide, and it is smoother above 60 fps. At 60 fps it gives the same 12° snap, and the position stalls for a frame and then doubles speed, up to 0.067 cells off (arithmetic). It is safe, because each unit's partial step stays within the limit that proves the pair clear. It is the lever if the snap looks wrong on fast screens, and it changes one function.
- *Never drive on a tick that turns:* plain interpolation would then be right, with no drawing change. But every route bend would stop the vehicle for a tick, and it isn't the motor #252 measured.

**Tests that assert the slide today (checked).** `MovementCoreTests.DrawInterpolatesWrappedHeadingAndPositionWithoutChangingSimulation` and `TankMeshBuilderTests.HullAndBarrelFollowSimulatedSizeAndInterpolatedHeading` set a moved position and expect an interpolated heading. PR 3 changes both.

### Walls and obstacles

#250's and #252's map was empty, so nothing here is measured with this motor. Static obstacles always meet the disc, as in ADR 0008.

**What `StaticClamp.NoWorse` proves: endpoint safety, and only that** (checked). It compares the penetration of each blocked cell around the endpoint with the same cell's penetration at the start. The job tests the same bits Commit stores, so rounding can't change the answer. Every entry path starts clear (`StaticClamp.IsClear`), so a vehicle's disc is never in a blocked cell at a tick pose. Turning doesn't change a disc. So no static error can occur, no clamp is needed, and none may move a vehicle: a clamp is a slide.

**What it leaves open: the swept disc.** `NoWorse` doesn't look at the path between the two poses. A blocked cell's rounded boundary is convex, so a short step can enter and leave it near a corner with both ends clear. Being shorter than a cell only proves that a step can't jump a whole cell. It doesn't prove that the swept disc stays clear. What follows:
- *In simulation, nothing touches.* The simulation holds no pose between ticks, and every later tick reads only tick poses. So the graze changes nothing, and #249's zero static error is about tick poses, as ADR 0014's gate is.
- *In a drawn frame, a disc can graze a corner.* If both ends of a step of length `s` are at least `r` from a blocked point, every point between them is at least `√(r² − s²/4)` from it, whatever the blocked shape. So a disc dips in by at most `r − √(r² − s²/4)` (arithmetic): 0.0063 cells for a small vehicle at 0.133, and 0.0012 for a large one at 0.083. That is 0.14 and 0.03 pixels at the nearest zoom. A footprint lies inside its disc, so the drawn square dips no further. This is accepted (judgment: under a pixel, only near a corner, and for at most one tick).
- *No vehicle tunnels.* Passing a gap narrower than its disc takes a step longer than the overlap of the two rounded corners, so it works only if the gap is within twice that depth of the disc's width. Blocked cells lie on the grid, so corner gaps are `√(a² + b²)` wide, and none falls between 0.694 and 0.707 for small vehicles or between 1.412 and 1.414 for large ones (arithmetic).
- *It is gated.* Gate 9 samples drawn frames in the wall fixtures and bars penetration above these depths. A deeper frame means the code isn't this motor. A later speed change moves the bars by the same formula.

**The cost of having no clamp.** A vehicle touching a wall with any part of its route pointing into it holds forever, because its route doesn't change while it stands still. Field blending near a wall's end does this (inference from #220's wall-end wiggle). So:

- **Tangent at contact.** When a step was refused by a blocked cell last tick, the vehicle holds the tangent of its nearest blocked point, on the route's side (its left on an exact tie). This is #220's S1. It holds one point, one heading and one flag per unit. It is still a turn and then a drive, so it adds no slide.
  - *Drive direction.* While held, the tangent replaces `NewVelocity` for both the turn stage and the drive gate. Speed still comes from `|NewVelocity|`. If the tangent changed only the turn target, the gate would measure the vehicle against a route up to 90° off its heading. It would turn to the tangent and then stand still until the group stall, which is the lock this rule exists to remove (arithmetic).
  - *Original route.* `NewVelocity` is not overwritten. Stage 2 still writes it every tick, and it is used only for release: the hold ends when that route's ray no longer meets the held point at the vehicle's own radius.
  - *A second refusal.* A step along the tangent can't deepen that point's contact (arithmetic). If another blocked cell refuses it, the vehicle holds the new nearest point instead. Within rounding of contact a very short tangent step can still be refused. That is a hold, not an error, and the group stall ends it.
- **Evidence.** On #220's fixtures, under the old check, S1 cut pivots from 1,427 to 204 and raised active corner flow, but delivered 56 of 110 on the small wall against 110 for the plain tangent (measured). Those jams fit that check's turn locks (inference), and under this motor it is unmeasured.
- **Spots.** A vehicle's spot is its order's resolved goal plus its offset. It is valid when its disc clears every blocked cell and it lies in the goal's region. Otherwise its spot is the shared corrected goal, which is ADR 0008's overflow. An in-sight vehicle steers at its spot, so ADR 0013's clear-line proof no longer covers that line. The tangent rule handles a graze.

### Spacing

Pitch is 1.42 widths: 0.71 and 1.42 cells. `slotSpacing` is 1.42 and must lie between 1.42 and 1.43. Below 1.4145 a block starts inside the margin. Above 1.43 S2.2 fails (below).

**Every entry path requires `d ≥ r_i + r_j + 0.0001`, not bare disc clearance.** This is the start of the induction under "Safety argument", so `Footprint.DiscMargin` is one constant, shared with the step. The clearance is tested on the stored float32 positions. Pitch 1.42 clears it for every pair (arithmetic):

| Pair | Contact | Needed at entry | What gives it | Spare |
|---|---:|---:|---|---:|
| Small and small | 0.7071 | 0.7072 | Pitch 0.71 | 0.0028 |
| Large and large | 1.4142 | 1.4143 | Pitch 1.42 | 0.0057 |
| Small and large | 1.0607 | 1.0608 | A block has one size, so this pair never meets at a pitch. The entry test decides it, and the ladder's seam starts at 1.2 | 0.139 |

A block's partial row sits half a pitch off in x, but a full pitch away in z, so its pairs are no closer. Rounding a lattice position moves a pair by at most `2e`, about 10⁻⁵ against those spares.

| State | On `main` today (checked) | Under this ADR |
|---|---|---|
| Block spawn | Pitch 1.25, inside discs | Pitch 1.42, clear of the margin |
| Block beside other units | `BlockPlacement.Overlaps` tests aligned squares | Tests the sum of radii plus the margin, in `PlaceBlock`'s one pass over live units. Refused, as today |
| Single add | `AddUnit` checks only the map, so it can stack on a unit | Refused when closer than the sum of radii plus the margin. One pass over live units on the add path only, not inside `AddUnit`, so a block doesn't scan once per unit |
| Two commands in one tick | | Both tests read `State`, never the grid, so they see each other |
| Arrival | Rules 1 to 4 stop a vehicle where it is. Slots don't exist yet | The same, then an arrival turn. The motor keeps every pair above the floor, so a stop is disc-clear and the turn is free |
| Travel in a formation | None. A group shares one goal and has no slots | Own spots at pitch 1.42, so discs stay clear |
| Idle and hold | The overlap pass can push idle units | Never moved. Hold never turns, and idle only finishes its arrival turn |
| Parked slots (ADR 0008 Issue 4, unbuilt) | Specified at 1.25, as formation pairs | Pitch 1.42. No formation pairs for vehicles |
| `slotSpacing` 1.25 | Used only by block spawn (`ApplyCommandsJob`) | Replaced |
| New order from any of these | | A free turn, because discs are clear |
| Scenes and tests placed by hand | `MovementMeasurement` scene 0 at pitch 1, `MoveOrderTests` lattice at 0.625 and touching at 0.5, `FlowFieldPoolTests` lattice at 0.625, `SelectionTests` adds 0.3 apart | Scene 0 and the lattices at 1.42 pitch. The touching cases go, and `SelectionTests` spreads out. Scenes 1 to 3 are small vehicles at 0.72, 0.0128 clear of the margin, and stay. `FlowFieldMeasurement` runs no motor, so it isn't an entry path (checked) |
| Later systems (S9 buildings, S10 unit exits) | Unbuilt | Must place vehicles at this clearance. Their ADRs say how |

**The boundary cases go in the implementation tests.** PR 1: two small vehicles at (100, 0) and (100.5, 0.5) are refused by the single add and by the block test, and a start exactly at the sum of radii plus the margin is accepted while one 10⁻⁵ closer is refused, for all three size pairs. PR 3: that touching pair, set by hand, can't take the 0.001001-cell step along (1, −1)/√2, and its squared separation stays exactly 0.5. From a start at exactly the margin, steps of 0.001 and 0.133 cells at x near 100, with the heading set tangent again each tick, never take `k` under the floor in 1,800 ticks. A column landed at contact and ordered sideways drives off, with the inner-margin rule firing on none of them.

In development builds the measurement counts any pair under the clearance floor, and a nonzero count fails the gates. There is no release guard: it would cost a second neighbour scan for a state no path reaches (judgment).

**What the pitch does to shape** (arithmetic).
- Blocks grow 13.6% in width and depth from 1.25 and 9.2% from 1.3. Area grows 29% and 19%. A block of 1,000 small vehicles goes from 395 to 507 cells², and 1,000 large from 1,580 to 2,027.
- S2.2 stays reachable, but only just, only on square blocks, and only because vehicles square up on arrival (see "Arrival facing"). Squared up, 100 small vehicles fill 1.90 times their footprint on a 10 × 10 block, and 1.95 with each settled within 0.05 cells. ADR 0008's 15 × 7 block is 1.99, and 2.05 settled. So vehicles use `ceil(sqrt(N))` columns, as spawn already does. 1.43 is the most that stays under 2 when settled.
- In travel, columns stretch by one tick per link: 0.71 to 0.84 and 1.42 to 1.50 (measured). A 2,000-unit block ends cycle 0 18% and 5% longer. By the end of cycle 2, First is 20% longer and Second is still under 6% (#252, seed 103). Vehicles recover their pitch on reaching their spots (inference). Neither spike tests that, because orders renew long before arrival, about 75 cells short at 10 units (checked).

**Owner decision, 2026-11-07.** Source: Issue #255's Owner instruction section. Asked "may vehicle groups stand further apart, at 1.42 vehicle widths?", the owner answered "yes, go with 1.42 STOP ASKING ME TO APPROVE I SAID!" (Producer session). It supersedes the 1.3 the owner accepted for parked groups on #202, which was 4% further apart where this is 13.6%. It covers spacing only. The figures above follow from that pitch, and this ADR doesn't claim the owner weighed them. They go in the final report, with the arrival turn, which came after that answer. No design within budget was found that keeps 1.3: a free 45° turn needs 1.41 (arithmetic), and a checked turn is options B and C below.

### Spots: game rules and ladder layout

| Item | Verdict | Why |
|---|---|---|
| Pitch 1.42 (#245's P) | **Game rule** | Every spawned or parked group needs it |
| Own spot (#245's O): order point plus the vehicle's offset from the group's centre at order time, clipped to the plane | **Game rule** | Shared goals lock. Two vehicles on converging lines lose all gap, and then every step has `c > 0` and is zero, for good (arithmetic). The owner's #245 answer asks for it |
| Drive gate and short-step rule | **Game rule** | Motor |
| L: keep spawn z when the goal z lies inside the block | **Ladder layout, not code** | Order each side to its own block's centre z. With own spots the spot's z is then `z_c + (z_i − z_c) = z_i`, the same as L (arithmetic). As a game rule, L would stop a click inside a block's width from centring it, which no design asks for |
| M: rows fill from the seam outward | **Ladder layout** | Without it Second's partial row sits on the seam. `BlockPlacement` takes a row-order argument, and only the ladder uses it |

**Scenario version 4.** Version 3 is #232's, on `main` since PR #253. Any change after that is a new version (ADR 0018). Version 4 changes these rows and keeps the rest of ADR 0018's table: gap 1.2, centres −40 and +25, far ends ±120, spread 7, meet 2 cells, sample 15 ticks.

| Field | Version 3 | Version 4 |
|---|---|---|
| Spawn pitch | 1.25 | 1.42, from `slotSpacing` |
| Order z | Goal lines at ±gap/2 | Each side's block-centre z |
| Row fill | First from the seam, Second toward it | Both from the seam outward |

**Arrival with own spots.** Rule 1 uses the spot. Rule 2, stopping beside an arrived group mate, applies only to vehicles whose spot fell back to the shared goal. At pitch 1.42 neighbours are always within its 0.1 cells, so it would stop vehicles short of their spots (arithmetic). Rules 3 and 4 stay. Every rule then hands over to the arrival turn.

**Own-spot limits.** A group keeps the shape it was ordered in. Compacting a scattered selection, or turning a block's lattice to face its order, needs converging lines, and both stay with ADR 0008 Issue 4. That Issue must show it doesn't lock. Until then a scattered selection keeps its spread, so S2.2 can't hold for it. A block re-ordered while travelling keeps its stretch in the same way: a 10 × 10 block stretched to 0.84 along its travel axis fills 2.22 times its footprint (arithmetic). Gate 11 reports this.

**Renewal.** A renewed ladder order keeps the vehicle's `SpotOffset` from its side's last order. It has no group to take a centre from, and the job computes none.

### Arrival facing (S2.2, S2.4)

**The problem** (arithmetic). A footprint's reach along a lattice axis grows as the vehicle turns away from that axis: `0.5 × (|cos θ| + |sin θ|)` cells, up to 0.707 at 45°. This motor's vehicles stop facing the way they drove, and own spots keep the block's lattice. A 10 × 10 block at 0.71 therefore fills:

| Facing against the lattice | Exactly on spots | Settled within 0.05 cells |
|---|---:|---:|
| 0° | 1.90 | 1.95 |
| 10° | 1.94 | 2.00 |
| 45° | 2.01 | 2.07 |

It stays under 2 only within 10.2° of a lattice axis when settled, and within 29.4° exactly on spots. So the 1.95 under "Spacing" needs vehicles squared to the lattice, which this motor doesn't give by itself.

**How S2.2 is measured** (checked). `MoveOrderTests.HundredUnitsComeToRestWithoutAnyTickAddingFootprintOverlap` writes `S2.2_area_ratio`: the world-axis box holding each square at its real heading, over 25 cells². It only prints the ratio, and every one of its orders runs along +x, so no test could see this. ADR 0008 names a different rectangle, aligned with the group's forward direction. The two agree only while forward is a lattice axis, and own spots break that. At a 45° order ADR 0008's rectangle reads 3.64, or 3.80 with vehicles squared to the lattice, because a square lattice doesn't fit a diagonal box (arithmetic). So for vehicles this ADR supersedes that rectangle: S2.2 is checked on the rectangle aligned with the group's lattice, which is what the test computes. The squares' hull is never larger, so ADR 0008's reason for a strict reading still holds. The measure isn't the fix: even the hull of 45° squares is 2.005.

**Options** (ratios at a 45° order, arithmetic).

| Option | Exact / settled | Cost | Verdict |
|---|---:|---|---|
| **Arrival turn to one lattice axis** | 1.90 / 1.95 | One byte per vehicle, one test per idle vehicle per tick, and a visible turn of up to 45° | **Chosen** |
| Stop facing the way they drove | 2.01 / 2.07 | None | Fails S2.2 |
| Tighter pitch | 2.00 / over 2 at the 1.4142 minimum | Below that, a block starts inside discs | Can't work: on any square block, a lattice at disc pitch with squares at 45° is exactly twice their footprint |
| Turn the lattice to face the order | 1.90 / 1.95 | Converging lines | Can lock. Stays with ADR 0008 Issue 4 |
| Staggered lattice, rows 0.615 apart | 1.86 / 1.92 | Spawn shape changes, so seam rows, row floors and #252's figures stop applying | Unmeasured. The lever if the turn must go |
| Test only along an axis | 1.90 / 1.95, at 0° only | None | Hides a failure: S2.2 says "an open point" |

**The rule.**
- *One facing per order.* At order time, beside the spot, the order takes one `Facing`: the world axis nearest the direction from the group's centre to the order point. `|x| ≥ |z|` picks ±x, otherwise ±z. If the point is the centre, it uses the lowest-slot vehicle's heading instead. One value for the whole order means a 45° order can't split on rounding.
- *Why world axes.* They are the lattice axes, because `BlockPlacement` lays blocks along x and z (checked) and own spots only translate them. Anything that later rotates a lattice, such as ADR 0008 Issue 4, must restate this rule.
- *The turn.* When any arrival rule makes a vehicle `Idle`, the turn stage turns it toward `Facing` at 12° a tick, lands it on the exact axis, and clears `Facing`. A settled vehicle then costs one byte test a tick.
- *Exceptions.* Side-wide ladder orders leave `Facing` clear, because they renew rather than arrive. So #252's figures still describe the ladder. Stop and Hold, once built, clear it. A new order replaces it.

**Why it is free** (arithmetic). Discs are clear at 1.42 and a footprint stays inside its disc at every heading, so the turn can't touch a neighbour. It doesn't move a disc, so it can't meet a wall. It reads and writes only its own row, so determinism holds.

**S2.4** (arithmetic). The turn happens once and in one direction: the difference only falls, it ends on the exact axis, and nothing pushes an idle vehicle. So it can't jitter. On open ground a vehicle arrives along the order's direction, so it turns at most 45°, which takes 4 ticks, 0.13 s. After a bent route it turns at most 180°, 15 ticks, 0.5 s. Both are well inside S2.4's 2 s. These are still ticks, so the frames ease. A vehicle has arrived when its arrival rule fires, so markers don't wait for the turn.

**No twitch on the last tick** (arithmetic). This motor has no ramp, so the last step lands on the spot at speed, and rule 1 fires a tick later, once `Velocity` has dropped. On that extra tick the route is rounding and can point anywhere, so an unguarded turn stage would swing up to 12° (today's ramp hides this). The turn stage's short-step exception stops it.

**What the player sees.** A block sent off-axis stops and squares up, in 0.13 s at most. A block sent along an axis doesn't turn. This goes in the final report.

### Is this ADR 0017's option B?

**Yes, in substance.** Option B was side-by-side lanes with goals in the middle of each lane, rejected because "it passes by never interacting, so the ladder would show two separate parades". Version 4 has goals in the middle of each lane. The differences:
- The facing rows stay 1.2 cells apart at every rung, where B's fixed offset left 29 cells at 10 units.
- Own spots remove the convergence B also suffered from.

**Contact figures.** 32 of 1,000 Second and 64 of 1,000 First come within 2 cells in every judged cycle (measured, #252). That is Second's seam row, and First's seam row plus its second row, 1.91 cells from Second's seam row (arithmetic). #250's 34 First came from the jam. Without it, the facing rows' discs never touch: the seam stays at least 1.1557 wide against 1.0607 (measured). The armies don't mix. Second's met count sits exactly on the floor at every rung.

**Why accept it, and what it can't claim.** The evidence doesn't support calling this armies crossing, and this ADR doesn't. It is accepted because it is what #249's bar measures, and ADR 0017 itself called the floor "a deliberately low bar" that exists "to fail lanes that have drifted apart, not to claim that the armies mix". What a passing entry shows: blocks of up to 2,000 vehicles travel and reverse in shape, without sliding, within budget. What it doesn't show: mixing, head-on passing, or the cost of either.

So from version 4 every entry also carries this line, written from one constant beside the crossing table, and it stays after any later ADR removes ADR 0018's label: `Armies pass side by side on separate lanes; head-on is not tested (S3.4).` The entry also reports the lowest single-vehicle travel share per side and cycle. That figure would have shown the jam, at 0.184 and 0.185, where the mean showed 0.95 (checked).

Resolved's final report must say that a passing entry depends on the wider spawn, own spots, and the L and M layout.

### The 10-unit rung

**Cause, under #250's 45° gate** (positions checked; windows arithmetic; which sample saw what is inference).
1. In cycle 1 the seam rows jam, as above: 3 vehicles a side stop near x = 1, having moved 37.6 of 80 and 23.5 of 50 cells.
2. They end cycle 1 already past each other. First is at x 1.40 to 2.81 and goes +x in cycle 2, while Second is at x −1.77 to 1.06 and goes −x. So those rows don't meet again.
3. The only contact left in cycle 2 is Second's seam row grazing First's second row, about 1.91 to 1.99 cells apart in z. Within 2 cells that leaves |dx| under 0.2 to 0.6.
4. At 10 units that row is partial, 2 vehicles 0.93 apart, so a pass lasts 6 to 10 ticks against a 15-tick sample, and only one Second vehicle is seen. At 50 units and up the row is full, a pass lasts at least 23 ticks, and every vehicle is seen.

**The rule, one for all nine rungs.** A vehicle drives only with its heading within one tick's turn of its route, 12°. The lateral drift of a reversal falls to 0.028 and 0.017 cells, 0.045 together, so the seam stays at least 1.155 wide against 1.0607 (arithmetic), and #252 measured 1.1557. A reversal costs 2 more ticks, 0.3% of a cycle (arithmetic), which makes 13 still ticks in all (measured).

**It meets the bar (measured, #252, no tuning).** At 10 units, Second met 3 in cycles 1 and 2 against a floor of 3, First met 5 and 4, and all 5 vehicles on each side passed. At every rung, Second's met count and the share passing are the same in cycles 1 and 2 as in cycle 0. Each seam vehicle passes the whole opposing row, at z 1.2 ± 0.045: its window is at least 4.5 cells against 3.25 cells between samples at 10 units, and longer above that (arithmetic).

**The margin is zero.** Second's count sits exactly on the floor at every rung and cycle, so one missed vehicle fails the entry. Rungs below 2,000 ran one seed each, so gate 1's three runs can still find that miss.

The bar stays. `MeetSampleTicks`, `MeetDistance`, `PassShare`'s 90% and the row floor don't move.

### Head-on (S3.4)

**Passing the ladder doesn't prove S3.4.** Two groups sent through each other meet head-on, and this motor has no way past: a vehicle can't sidestep, and closing vehicles stop.

Case B4 is the motor with own spots, without L and M, so the blocks re-centre into each other. The table gives #252's run at the 12° gate, with #250's run at 45° in brackets (measured, seed 103):

| | Cycle 0 | Cycle 1 | Cycle 2 |
|---|---:|---:|---:|
| First travel | 0.489 (0.487) | 0.923 (0.521) | 0.712 (0.530) |
| Second travel | 0.241 (0.252) | 0.068 (0.069) | 0.037 (0.024) |
| Passed of 1,000, First / Second | 284 / 186 (284 / 186) | 237 / 186 (234 / 186) | 46 / 72 (228 / 199) |

**The 12° gate doesn't fix this.** Second hardly moves after cycle 0, and the longest seam-row stand-still is 19 s of a 20 s cycle, against S3.4's 5 s limit. Vehicles on both sides end cycle 2 with no net travel. First's higher mean doesn't show passing: its passed count falls to 46 by cycle 2. There was no overlap and no static error, slide stayed at 0.098° or less, and there were 5,000 group-stall renewals against #250's 4,000. S3.4's own test, two groups of 100, hasn't run. Comparing B4 with case 5 doesn't isolate L from M.

S3.4, S3.3 and S3.5 stay with ADR 0008 Issue 5, with ADR 0014's and ADR 0015's gates. Its avoidance job writes `NewVelocity`, which this motor turns toward and drives along. Foot soldiers get their own motor in a later ADR.

### Response (S2.1): unresolved

**S2.1 stays open.** It says "every selected unit starts moving within 0.1 s", counted from the click's release (`docs/design/core-loop.md`, `docs/design/controls.md`). The owner's rule is that vehicles don't slide, so a vehicle facing the wrong way has to turn before it drives. A reversal drives on its 14th tick, after standing for 0.43 s (measured, #252). This ADR claims no compliance with S2.1, and it takes no reading of "starts moving" that would make it pass. It doesn't decide whether a turn counts as starting to move.

- **Where it goes.** This conflict with the design text goes to the owner in the final report of #249.
- **What it isn't.** S2.1 is not part of #249's Resolved bar (Issue #255). So no gate below tests it, and every figure in this section is reported, not gated. No result here can pass or fail an entry or a PR.
- **What stays.** The GDD doesn't change, and ADR 0008's S2.1 row still states the requirement.

**Checks that look like S2.1.** `MoveOrderTests` asserts every unit has velocity on the first tick, and `MovementCoreProfile` scene 4 requires a drawn move within 0.1 s. They order vehicles toward a point they already face (checked), so rear ranks pass on a 0.0028-cell creep that no player can see. A pass there would read as compliance, so PR 2 and PR 3 take these terms out of pass or fail, and they stay printed. They aren't evidence for S2.1 in either direction.

The rest of this section is the record of what the motor does.

**The turn begins on the order's first tick** (checked on `main`).
1. A click's `UnitCommand` leaves `ExecuteTick` at 0, so `ApplyCommandsJob` applies it at once.
2. `UnitSimulation.ScheduleFrame` runs `ApplyCommands` and then `movement.Schedule` in the same tick. PR #253 only added ladder counters around them.
3. In that chain, `RequestFieldsJob` resolves the goal and `PreferredVelocityJob` writes `NewVelocity`. The job this ADR replaces with the turn stage reads the result straight after. No field is needed, because with none ready the direction is direct seek.
4. `MoveOrderTests.ClickQueuesSnapshotAndNextTickPlansEverySelectedUnit` already asserts that plan on the next tick.

So a vehicle that must turn turns its first 12° on the order's first tick. `MatchDriver`'s bound for the first drawn frame is about 83 ms after the release at 60 fps. A change first made on the second tick would draw at about 117 ms, and on the third at about 150 ms (arithmetic). At a steady 60 fps, the first frame that shows the turn shows 6° of it.

**Time to first drive, by turn angle.** Still ticks are `max(0, ceil((θ − 24°) / 12°))`. Only the 180° row is measured (#252). The rest is arithmetic, and no spike ran an oblique order.

| Turn to the route | Still ticks | First drive tick | Standing still |
|---:|---:|---:|---:|
| Up to 24° | 0 | 1 | 0 s |
| 45° | 2 | 3 | 0.07 s |
| 60° | 3 | 4 | 0.10 s |
| 90° | 6 | 7 | 0.20 s |
| 135° | 10 | 11 | 0.33 s |
| 180° | 13 | 14 | 0.43 s |

**Behind the front rank.** A vehicle can't pass the one ahead, so each rank starts a tick after the rank before it, the following conflict of ADR 0012. Its first near-full step comes on tick `first drive + rank − 1`. At 45° a vehicle waits for both neighbours ahead, so the delay counts ranks along both axes. Arithmetic, apart from about 1.0 s measured for 32 ranks facing their route (#252).

| Block | Facing order | Reversal | 45° order |
|---|---:|---:|---:|
| 100 (10 ranks) | Tick 10, 0.33 s | Tick 23, 0.77 s | Tick 21, 0.70 s |
| 2,000 (45 ranks) | Tick 45, 1.5 s | Tick 58, 1.9 s | Tick 91, 3.0 s |

**Compressed columns.** A column already compressed to contact doesn't even creep. With gaps under the short-step distance, rank k has a zero step until tick k, on a facing order, and no pose change of any kind before then (arithmetic). The last vehicle of a 10-rank column stands for 0.3 s, and of a 45-rank column for 1.47 s. Only a blocked stop leaves a column like that, because an arrival ends at pitch 0.71. On an order it must turn for, each vehicle begins its turn on the first tick and then waits its rank.

**What a player can see by the third tick.** Pixels are at the nearest zoom: orthographic size 20 and the isometric camera give about 22 pixels a cell at 1080p (checked, arithmetic).

| By the third tick | Movement | At the nearest zoom |
|---|---:|---:|
| A small vehicle turning 12° a tick: its corner | 0.074 cells a tick | 1.6 pixels a tick |
| A small vehicle driving at full speed | 0.133 cells a tick | 2.9 pixels a tick |
| A small rear-rank vehicle creeping | 0.0028 cells a tick, 0.0084 in all | 0.18 pixels in all |

- **Reversal and oblique orders.** Every vehicle has to turn, so every vehicle at every rank visibly turns from the first tick. One more than 48° off its route hasn't driven by then (arithmetic). An order only a few degrees off their heading behaves like a facing order (judgment).
- **Facing orders, first three ranks.** They have driven by the third tick.
- **Facing orders, rear ranks.** They have nothing to turn, and the vehicle ahead hasn't moved. Their position changes by 1.7% of a vehicle's width, which no player can see. That is 70 of 100 vehicles, and about 93% at 2,000. What they show in that time is the acknowledgement pulse on the next frame (ADR 0008), and they visibly drive on the tick in the table above.

**What could close it, none of it chosen.** These go in the final report beside the conflict. #249 says proposed target changes go there and don't count as Resolved.
- *An owner ruling that a turn counts as starting to move.* That would cover vehicles that must turn. It wouldn't cover rear ranks or compressed columns on a facing order.
- *Turn faster:* 56° a tick with a 12° gate would drive on the third tick of a reversal. That is 1,680°/s, so a vehicle flips in a tenth of a second, which reads as a snap rather than a turn (judgment), and rear ranks still wait.
- *A one-tick gate at a higher rate:* 45° a tick drives on the third tick, but 45° off route. That is 0.153 cells sideways across the seam, against 0.139 to spare, which is #250's jam again (arithmetic).
- *Drive backwards:* no slide and no delay, but gate 9 bars reverse ticks.
- *Option E's relay:* a vehicle moves in the same tick as the one ahead, so rear ranks start at once. It conflicts with this ADR's no-serial rule, and it is unmeasured.

### Gates

The implementation's last game-code PR and its ladder entry must show all of these. Everything is on the ladder PC, whose entries say `reference-class: no`.

| # | What | Bar | Status |
|---|---|---|---|
| 1 | Ladder entry, version 4, all nine rungs, three valid runs each | ADR 0018's removal bar for every rung, side and run: travel at least 0.60 in cycles 0, 1, 2; at least 90% passed and one spawn row met in cycles 1, 2 | Gate. The label stays, and a later ADR removes it |
| 2 | Same entry, 2,000 rung | ADR 0004's pass rule. Regression check against the newest version 4 entry | Gate |
| 3 | Physical run in game code, development Player, 2,000 units, version 4 layout, 1,800 ticks, measured outside the timed jobs | Slide at most 1°, lateral at most 0.01 cells, zero footprint overlap, zero static error, zero pairs under the clearance floor of 2.5 × 10⁻⁵. The lowest clearance and the inner-margin rule's firing count are reported | Gate. Other rungs are reported |
| 4 | Job orders 1/1, 1/64, all/1, same run | Full physical state byte-identical | Gate |
| 5 | ADR 0012 certification: its four scenes, uncapped at 1,000 and 2,000, capped at 2,000, three runs | Move check p99 at most 0.5 ms, chain p99 at most 3 ms, main-thread work and total at most 0.5 ms on sets U, S and C, ADR 0011's GC check | Gate |
| 6 | Full EditMode suite | Passes. Changes to tests are only those listed under Files | Gate |
| 7 | The 19 crowd cases in `MoveOrderTests` | Their assertions pass at the new spawn, apart from the first-tick count, which is printed only. Their S2.2 ratio is asserted at most 2, where today it is only printed | Gate |
| 8 | `MovementCoreProfile` scenes 4 to 6 | ADR 0014's wall gate (no overlap, none in a blocked cell, at least one small and one large past the wall), ADR 0015's ring gate. Delivered counts reported beside 26 of 110 and 15 of 100. First-tick count and drawn response reported | Gate on the safety lines |
| 9 | #220's fixtures: mixed, small and large × wall, ring, west, north, S2.3, at 1.42 | Slide, overlap, static and no reverse pass on all 15. On sampled drawn frames (t 0.25, 0.5, 0.75), disc penetration of a blocked cell at most 0.0064 cells small and 0.0013 large, with the deepest reported. Pose change by the third tick, first drive, delivered, group stall and jam lines reported beside #220's R+F column | Gate on the safety lines. Any fixture worse than R+F is listed in the PR and the final report |
| 10 | 0 B GC Alloc capture (ADR 0004 rule 11), and before and after Profiler numbers (AGENTS.md §4) | As stated | Gate |
| 11 | Lowest single-vehicle travel share, seam rows' minimum seam z gap, block length in travel, and the S2.2 ratio of a 100-vehicle block re-ordered in mid-travel | Reported | Report |
| 12 | S2.2 and S2.4 at any order direction. Game code, EditMode: 100 small vehicles from a spawned block, sent 15 cells at 0°, 30°, 45°, 60°, 90°, 135° and 180° | Ratio at most 2, measured as `MoveOrderTests` measures it and asserted. No overlap. All end by rule 1, on one exact axis heading. Each arrival turn takes at most 4 ticks and never changes direction, and no pose changes for the 2 s after it | Gate |
| 13 | Drawn frames. (a) EditMode: the real `FillDrawDataJob`, redrawn through `ScheduleFrame(0, t)` at t 0.25, 0.5, 0.75 and 1, on reversal and 45° orders of 100 vehicles, and on a hand-set 156° to 168° moving tick. (b) Gate 3's physical run, same t, through `DrawnPose` | On every sample of a tick that drove, drawn heading within 1° of that tick's whole displacement, from its previous position to its new one. Comparing samples with each other would measure rounding on a few ten-thousandths of a cell. Still ticks hold position and interpolate heading. t = 1 equals the tick pose. Zero footprint overlap and zero pairs inside bare discs on sampled frames | Gate. PR 2 reports it on the old motor |
| 14 | S2.1's facts. Game code, 2,000 small vehicles in one block from rest at 1.42, open map, sent 30 cells at 0°, 30°, 45°, 90°, 135° and 180°. EditMode for ticks, `MovementCoreProfile` scene 4 at 60 fps for drawn frames. Also a 45-rank column stopped against a held vehicle until its gaps are under the short-step distance, then ordered on | Per direction, lowest, median and highest over vehicles: first tick with any pose change, first tick with a nonzero step, first tick with a step of at least half top speed, and click release to the first drawn pose change. The count of vehicles whose only change by the third tick is a step under half top speed with no turn. For the column, first nonzero step by rank | Report only. No bar. It goes in the PR and in #249's final report, and no result here counts toward Resolved |

**Why S2.1 has no gate.** Issue #255 sets this: S2.1 stays unresolved and outside the Resolved bar. A gate could only pass on a reading, or fail on a bar #249 doesn't hold. Row 14 records the facts.

**Why flow lines in fixtures aren't gates.** #249's bar doesn't include them, and ADR 0014 and ADR 0015 already put them on Issue 5. A bar isn't lowered by this, and the final report must quote them. The expected result is mixed: converging streams lock, so some fixtures will stall (see Consequences). A fixture line that fails on overlap, static error or slide is a stop.

### Rules for hot paths

These continue rules 1 to 20 and add to ADR 0004's rules.

21. **One pose writer.** `CommitMovementJob` is the only job that writes a vehicle's pose in a tick. No job pushes, clamps or corrects a vehicle's position.
22. **Discs stay clear, with the margin.** Every path that creates or places a vehicle refuses a pose closer to a live vehicle than the sum of radii plus `Footprint.DiscMargin`, 0.0001 cells. Bare disc clearance isn't enough. Development builds count pairs under the 2.5 × 10⁻⁵ clearance floor, and the count must stay zero.
23. **The step reads start poses and new headings only.** It uses the formula above, including the inner-margin rule, with no footprint test, level, second pass or single-threaded part. The neighbour search stays uncapped, and its radius includes the margin. The endpoint `NoWorse` tests is the one Commit stores.
24. **Turn before step, unchecked, one tick's turn at most.** The turn reads no neighbour. A target under the short-step distance gives no turn.
25. **Drive only near the drive direction.** Within one tick's turn. The drive direction is `NewVelocity`, or the held tangent, and the held tangent never overwrites `NewVelocity`. A step under 0.001 cells is zero.
26. **Own spots and `Facing` are worked out once per order.** The group's centre, `SpotOffset` and `Facing` come from the single-threaded command stage. `Spot` and its validity come from goal correction in `RequestFieldsJob`, which is also single-threaded. The later movement jobs only read them, and nothing recomputes a group's centre. A renewal keeps its `SpotOffset`.
27. **An arrival turn is one turn.** It reads and writes only its own row, ends on the exact axis, and clears `Facing`, so a settled vehicle costs one byte test a tick and no trigonometry.
28. **Draw what the motor did.** A tick that drove draws its new heading on every frame with t above 0. Only a tick that stood still interpolates heading. Drawn position is always the plain interpolation. `FillDrawDataJob` and the measurement use one function, `DrawnPose`.
29. **Nothing delays the first turn.** The turn stage reads the `NewVelocity` of the order's own tick. No field, neighbour or group state may hold it back. This is a rule for the motor, not a claim about S2.1.

### Packages

None are added.

## Alternatives considered

Cost figures are at 2,000 units on the ladder PC unless marked.

| Option | How it works | Evidence | Cost | Complexity | Risk and reversal |
|---|---|---|---|---|---|
| **A. Disc-clear motor, pitch 1.42, own spots, 12° gate (chosen)** | As above | #250 at 45° and #252 at 12°: above. Every #252 kill item passed | Check p99 at most 0.451 ms, chain at most 1.32 ms (#252, comparative). No allocation or GPU cost. About 30 B per unit more | Less code than today: one job replaces three | Locks on converging lines, and head-on still stalls. Easy to reverse: the job has the same inputs |
| **B. Keep ADR 0012's check with no slide (#220's R+F, #247's N)** | Levels and footprint tests, with turn-in-place | #247: O+S+P travel at most 0.232 by cycle 2 at every seed, overlap on all, 4,446 to 10,077 pair-ticks | Check p99 0.585 to 0.698 ms unchanged, over budget, and 2.41 to 3.65 ms with safe step | Already built in a harness | The turn lock. #220 tagged 92% of long stalls as two-cycles. Fails the bar |
| **C. Disc-clear at 1.25 or 1.3, with a checked turn only inside discs** | Keeps the #202 pitch. Inner vehicles wait while outer ones peel off, as #197 describes | No run. Arithmetic: neighbours at 1.3 touch above 39.7°. Full rows never clear sideways because vehicles can't sidestep | A second scan plus footprint tests. Likely over 0.5 ms, as B is | Medium | The reversal lock returns (#249). Not chosen: the owner chose 1.42 |
| **D. Corridor side-shift (#249's fallback 1)** | Own spots centred on the goal. While a block is ahead, each block shifts sideways | None | One job per group every 15 ticks, estimated | Medium | Many groups, oscillation. It could help S3.4, so Issue 5 may weigh it |
| **E. Exact column relay (#249's fallback 2)** | Front-to-back single-threaded pass, keeps 0.71 with no start lag | Python only | Estimated 0.05 to 0.2 ms, serial | Medium | Conflicts with this ADR's no-serial rule. The lever if the rear-rank delay below must go |

**Fixing the 10-unit rung.**
- *Gate 12° (chosen):* removes the jam at every rung (measured, #252).
- *`LaneGap` 1.4:* clears a 0.26 drift, but it leaves lanes drifting in the game and narrows the meet window. Unmeasured.
- *Sampling every tick:* doesn't remove the jam, and changing a measure to pass isn't a fix.
- *Per-rung values:* ruled out.

**Ladder layout.**
- *Keep goal lines at the seam, with L as a game rule:* same geometry, more game code, and a click that doesn't centre.
- *A comb of interleaved rows:* each side's rows 2.4 cells apart with the other's between them, one order per side. Every vehicle would pass within 1.2 cells of enemies, so the armies really would mix (arithmetic), though the row pitch becomes 4.8 widths for small vehicles and 2.4 for large, and no game shape looks like it. Not measured, so it is not adopted. It is the natural next version if the owner wants more contact, and a candidate for a reported cost scene.

**Clearance at entry and near contact.**
- *Bare disc clearance at entry:* fails, as the worked pair under "Safety argument" shows.
- *The margin at entry and nothing else (the motor #250 and #252 ran):* no spike showed a failure, but the induction doesn't close, because a tangent step inside the margin can lose clearance tick after tick.
- *A hold at the whole margin:* closes it, but freezes a compressed column ordered sideways.
- *A hold at half the margin (chosen):* closes it for one comparison per neighbour, and ordinary contact never reaches it. A pair worn that far holds while within 2.9° of tangent, until its route turns or the group stall ends the order.
- *A second test of each stored endpoint against its neighbours:* also closes it, but it is a second scan and it needs the neighbours' final poses, so it brings back an order between units.

**Arrival facing and drawn frames.** The options are weighed in those sections. S2.1's are listed under "Response (S2.1): unresolved", and none is chosen.

## Consequences

- **Benefits.**
  - Vehicles don't slide on any tick or in any drawn frame, and the proof needs no order between them.
  - The chain shrinks by one job and one kind of test. Nothing at rest jitters, because nothing pushes.
  - Drawn frames can't show two vehicles overlapping.
  - Groups keep their shape.
- **Tradeoffs.**
  - Groups stand 13.6% further apart and use 29% more area than today. The owner chose the pitch, and the area follows from it (see "Spacing").
  - **S2.1 stays open.** This motor doesn't meet it as written, this ADR doesn't claim otherwise, and the conflict goes to the owner in the final report of #249. The next two items are its parts.
  - **Rear ranks start late.** A column starts one tick per link, the following conflict of ADR 0012 ([Stern et al.][mapf]), so the back of a 32-row block reaches full speed about 1.0 s after the front (measured 4.05 cells at 4 cells/s). A rear vehicle creeps 0.003 cells a tick, which no player can see, and the short-step rule leaves that creep alone: First's seam row, front to back, had no zero step in cycle 0 (measured, #252). A column already compressed to contact doesn't even creep. Oblique orders stretch the delay across both axes, to an estimated 3 s at 2,000 units (arithmetic, unmeasured). Option E is the lever.
  - **Turning vehicles drive late.** The gate holds a vehicle still while it turns. One ordered to reverse stands for 13 ticks, 0.43 s (measured, #252), where #250's gate held it for 11 (arithmetic). Any vehicle more than 24° off its route has zero velocity on the order's tick, though its turn begins on that tick (arithmetic).
  - **Arrived vehicles square up.** A block sent off-axis turns up to 45° on stopping, in 0.13 s at most on open ground. It is visible, no design page asks for it, and S2.2 needs it (see "Arrival facing"). This goes in the final report.
  - **Converging lines lock.** Two vehicles that touch while both closing stop for good until their order ends. So funnels such as S3.3's gap will likely stall (inference, unmeasured), and only the group stall ends them. Deadlock is a known problem of this family of methods and has its own recovery research ([Abdullhak and Vardy][bvc-deadlock]).
  - **No push.** An idle vehicle in a mover's way now stays there, so the mover stops. S3.5 moves wholly to Issue 5.
  - S2.2's margin is thin: 1.95 when settled, against 2, and only with the arrival turn. Without it, a 45° order gives 2.07.
- **Risks.**
  - Walls, offsets taken at order time (#250 and #252 fixed them at spawn), the tangent rule, the arrival turn, the inner-margin rule, drawn frames and oblique orders are unmeasured. Each has a gate or a reported line above. The 12° gate is measured, but only in #252's throwaway code.
  - A drawn disc can graze a corner by up to 0.0063 cells, between two clear tick poses. Gate 9 bars anything deeper.
  - Second's met count has no margin, and what it counts is side-by-side passing, not crossing, as above.
  - ADR 0012's gap, dense and wall scenes have never run with this motor. Their check cost is estimated similar to the ladder's, because density is similar (judgment).
  - The ladder PC isn't reference class, and nothing here measures a 6-core CPU (ADR 0012, ADR 0017).
- **Cost at 2,000 units.** As in option A, measured in the harness. Memory: `Spot`, `SpotOffset`, `Facing` and tangent state add about 30 B per unit, about 60 KB. The level arrays go. The drawn rule adds one branch per vehicle per frame, the arrival turn one byte test per idle vehicle per tick, and the inner-margin rule one comparison per neighbour, none of them measured. No GPU cost, and no allocation after warm-up (gate 10).
- **Reversibility.** Moderate. The motor is one job swap, and `AvoidanceJob` still only writes `NewVelocity`. The pitch is one value, but changing it is a new scenario version.
- **Triggers.** Each one comes back to the architect.
  - Gate 3, 4, 12 or 13 fails, or a fixture shows an overlap, static error, slide or a deeper graze than gate 9 allows. That is a defect in the proof or the code.
  - Gate 5 fails on the move check with this motor.
  - Delivered counts in gate 8 fall below the baselines. A contact-tangent rule for vehicles, not yet designed, is the first candidate.
  - ADR 0017's ORCA trigger, and ADR 0018's "a ladder entry meets the bar", which calls for the later ADR that removes the label.
- **Dependencies.** No package, Unity or handbook change. This ADR's PR is documentation only, with no change under `Game/` or `tools/`.
- **The final report of #249** carries: S2.1's open conflict with row 14's figures and the options that could close it, the arrival turn, the costs of the pitch, that a passing entry depends on the wider spawn, own spots and the L and M layout, and that S3.4 stays open.

### Follow-up work

**Spike: done** (#252, `type:spike`, gameplay-programmer, no game code). In #250's harness it changed only the gate to 12° and added the short-step rule. It re-ran Stage C (nine rungs), seeds 7 and 211 at 2,000, the cost and job-order repeats, and the head-on B4 case. All 19 runs took about 10 minutes.
- *Measured:* every figure in #250's table, the lowest single-vehicle travel share, the minimum seam z gap per cycle, and the head-on B4 case again.
- *Kill rule, fixed before the run:* any rung, side or judged cycle under the bar, any vehicle under 0.60 of free travel in cycles 0 to 2, any physical error, non-identical job orders, or check p99 over 0.5 ms or chain p99 over 3 ms at 2,000.
- *Result:* no kill item was crossed. The figures are under "What #252 measured", and the finding is on #252. The spike's code is not merged, and the game version is the work below.

### Files for the implementation Issues

One Issue per PR, in this order, one programmer. #232's PR #253 has merged (PR #242 was closed unmerged), so nothing waits on it. Assets overlap, so they don't run in parallel (`docs/studio/workflow.md`). PR 2 changes no tick job, so it needs no ladder entry (ADR 0004).

| PR | Files | Change | Changed lines (estimate) |
|---|---|---|---|
| **1. Spawn, entry, own spots, version 4** | `Simulation/Movement/MovementParameters.cs`, `Presentation/MovementSettings.cs`, `Data/MovementSettings.asset` | `slotSpacing` 1.42, range 1.42 to 1.43 | ~380, with a ladder entry on the old motor, so the motor's effect shows on one layout |
| | `Simulation/Movement/Footprint.cs`, `Simulation/BlockPlacement.cs`, `Simulation/ApplyCommandsJob.cs` | `DiscMargin`. Clearance tests (sum of radii plus margin) at spawn and add, row-order argument, spot from offset, renewal keeps its offset | |
| | `Simulation/UnitCommand.cs`, `Simulation/SelectionJob.cs`, `Simulation/Movement/MovementUnit.cs` | Group centre and offset per order, `Spot`, `SpotOffset` | |
| | `Simulation/FlowFields/RequestFieldsJob.cs`, `Simulation/Movement/PreferredVelocityJob.cs`, `Simulation/Movement/OrderProgressJob.cs` | Spot validity at goal correction, target is the spot, rule 2 for fallback vehicles only | |
| | `Presentation/Diagnostics/LadderScenario.cs`, `Data/PerformanceLadder.asset`, `Presentation/Diagnostics/PerformanceLadder.cs` | Version 4: order z, row fill, minimum travel figure | |
| | `tools/unit-ladder.js` and its tests, `docs/performance/unit-ladder/README.md`, a new entry | The fixed line, the figure, the version | |
| | Tests: `BlockPlacementTests`, `UnitSimulationTests`, `MoveOrderTests`, `MovementCoreTests` (spacing check), `LadderFrameBufferTests`, `SandboxSceneBuilderTests`, `SelectionTests`, `FlowFieldPoolTests` | Spawn at 1.42, lattices at 0.71, touching cases gone, refused adds, fixtures spread to the new clearance. The entry boundary cases under "Spacing" | |
| **2. Measurement** | `Simulation/Movement/MovementMeasurement.cs` | Slide and lateral as #220 measures them, pairs under the clearance floor and lowest clearance, state hash, version 4 layout scene, sampled drawn frames with disc penetration of blocked cells, and per-vehicle first pose change, first step and first half-speed step | ~400 |
| | New `Simulation/Movement/DrawnPose.cs`, `Simulation/FillDrawDataJob.cs` | Move today's draw into one function that the job and the measurement share. Its output doesn't change here | |
| | `Presentation/Diagnostics/MovementCoreProfile.cs`, `EditorTools/MovementCoreProfileBuilder.cs` | Job-order options, fixture size set and start heading, fixture lines, order direction in scene 4, and drawn heading beside drawn position in the response figures. `firstDrawTime`, `allDrawTime` and the first-tick moving count leave `orderPassed`, and stay in the `-order.txt` output | |
| | Tests beside them | Baseline run reported on the old motor | |
| **3. The motor** | New `Simulation/Movement/DiscClearStepJob.cs`, `TurnVehiclesJob.cs` | As above, with the inner-margin rule and the turn stage's short-step exception | ~800, of which about 300 are deletions. Over 400 because splitting it would leave two motors on `main` (AGENTS.md §4). The PR says so |
| | `Simulation/Movement/DrawnPose.cs` | The drawn rule. It lands here because this motor is what creates the slide, so no commit on `main` draws it | |
| | `MovementCoreTests.DrawInterpolatesWrappedHeadingAndPositionWithoutChangingSimulation`, `TankMeshBuilderTests.HullAndBarrelFollowSimulatedSizeAndInterpolatedHeading` | Still ticks keep the interpolated heading. Moved ticks expect the new heading | |
| | `MoveOrderTests` | Gate 13(a), and row 14's reversal, oblique, facing and compressed-column orders, printed. The first-tick assertions in `ClickQueuesSnapshotAndNextTickPlansEverySelectedUnit` and `HundredUnitsComeToRestWithoutAnyTickAddingFootprintOverlap` become printed only. `BlockedKnotSettlesBy181TicksAndRemainsStill` moves to 0.71 pitch | |
| | `MovementPipeline.cs`, `CommitMovementJob.cs`, `MovementParameters.cs`, `MovementSettings.cs`, `MovementSettings.asset` | New chain, `StepBound` is the top step, `moveCheckPasses` and `acceleration` go | |
| | `StaticClamp.cs` | `Apply` has no caller left, so it goes (AGENTS.md §4) | |
| | Delete `IntegrateMovementJob.cs`, `OverlapMovementJob.cs`, `MoveCheckPassJob.cs`, `MoveCheckData.cs` and their `.meta` files | | |
| | `EditorTools/LadderProfileAnalysis.cs`, `tools/unit-ladder.js`, `tools/unit-ladder.test.js`, `Presentation/Diagnostics/MovementCoreProfile.cs` | Their job-name lists and CSV columns name the new jobs, so the ladder's movement cost keeps reading the whole chain (checked) | |
| | `EditorTools/MovementCoreProfileBuilder.cs`, `SandboxSceneBuilderTests` | `TunePasses` and the pass-count test go with `moveCheckPasses` | |
| | `MovementCoreTests`, `MovementMeasurement.cs` scene 0 | Level tests replaced: pair cases, inside-disc counterexamples, the motor boundary cases under "Spacing", weights, plane edge, short steps, job orders, static hold | |
| | A ladder entry | First candidate for gates 1 and 2 | |
| **4. Arrival facing** | `Simulation/ApplyCommandsJob.cs`, `Simulation/SelectionJob.cs`, `Simulation/Movement/MovementUnit.cs` | `Facing` beside the spot, one per order, none on side-wide ladder orders | ~200 |
| | `TurnVehiclesJob.cs` | The arrival turn | |
| | `MoveOrderTests` | Gate 12, and the S2.2 assert in gate 7's 19 cases | |
| | A ladder entry | Shows the ladder unchanged, because side-wide orders have no `Facing` | |
| **5. Walls** | `TurnVehiclesJob.cs`, `DiscClearStepJob.cs`, `MovementUnit.cs` | Tangent at contact, held to release. The tangent is the drive direction for the turn and the gate, and `NewVelocity` stays the route for release | ~260 |
| | `MovementCoreTests`, `MoveOrderTests` | Wall end, concave corner, ring, one-cell gap for small vehicles. A tangent 80° off the route drives by its 6th tick, and releases once the route's ray clears the point | |
| | A ladder entry, all gates | The entry a later ADR may name | |

Paths are under `Game/Assets/Code/` except data assets, tools and docs. After the bar is met, the architect writes the later ADR that names the entry and removes the label. It restates the fixed line above.

[ijobfor]: https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Unity.Jobs.IJobFor.html
[mapf]: https://arxiv.org/abs/1906.08291
[bvc]: https://msl.stanford.edu/bibliography/zhou_fast_2017.html
[bvc-deadlock]: https://researchr.org/publication/AbdullhakV21
