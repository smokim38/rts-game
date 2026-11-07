# ADR 0019: Vehicles move by disc-clear steps and keep their shape on own spots

## Status

Accepted

## Date

2026-11-06

## Context

Issue #249 asks the studio to resolve big-army vehicle movement without sliding. Issue #250 tested a motor that gets there at 2,000 units, in throwaway code, and Issue #252 re-ran it with the one number this ADR changes. This ADR plans the game version. Each figure says what it is: measured, arithmetic, inference or judgment. "Checked" means read by hand from #250's or #252's retained files, since no code was run for this ADR.

**Read this first.**

1. **#250's 2,000-unit pass is less clean than its counts look.** The rows that meet the other army stop head-on in cycle 1 and stand still for about 16 s. My plan in #249 assumed the seam stays 1.2 cells wide. It doesn't.
2. **#252 measured the fix in throwaway code, and every kill item passed.** With this ADR's 12° gate, that jam is gone at all nine rungs. Nothing yet measures the approach in game code, and head-on still stalls.
3. **The layout is the side-by-side passing that ADR 0017 option B rejected,** apart from the distance between the blocks. See "Is this ADR 0017's option B?".

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
- The studio resolves movement without asking about steps (#249).
- Vehicles stand 1.42 widths apart: "yes, go with 1.42" (2026-11-07, recorded on #251). See "Spacing".

**Constraints.** ADR 0004's hot-path rules and pass rule. ADR 0008's 3 ms and 0.5 ms budgets, measured as ADR 0012 says. No new package. #232's PR merges first, now PR #253 (PR #242 was closed unmerged): the crossing counters and scenario version 3 aren't on `main` yet (checked).

## Decision

**Every vehicle turns freely, then drives straight along its heading, by a step that can't close two discs. No job ever pushes a vehicle. Vehicles enter the world and stand 1.42 widths apart, so discs are always clear and turning is always safe. A group order gives each vehicle its own spot, so lines stay parallel. A vehicle drives only once its heading is within one tick's turn of its route, which removes the seam jam. L and M are ladder layout, in scenario version 4. The owner chose the 1.42 spacing, and #252 measured the 12° gate in throwaway code, where every kill item passed.**

### What this supersedes

Only these parts. Earlier ADRs' text isn't edited (AGENTS.md §9). Issue #251 names ADR 0008, 0012, 0017 and 0018. ADR 0013, 0014 and 0015 are added because the same change touches them.

| ADR | Superseded, for vehicles (every unit on `main` today) | Stays in force |
|---|---|---|
| 0008 | Formation pairs in Contact shapes. Integration's velocity ramp and static clamp, the overlap pass and the move check (stages 3 to 5). "Who gives way" and "Idle units make way" as pushes. `slotSpacing` 1.25 and its 1.05 to 1.3 range, and `ceil(sqrt(2N))` columns. The job table and cost rows for those stages. Rule 3's move-check clause, rule 6's spacing range, and rule 9 except that overlap is still measured on footprints. The "Discs round the whole square" alternative is reversed. "Response (S2.1)"'s "every unit has a velocity from that tick", for a vehicle that must turn first (see Consequences) | Fields, pool, tick rate, grid, group stall, progress, goal correction, budgets, and stage 2. ORCA still outputs `NewVelocity`, which is now a heading target. S3.5 stays required |
| 0012 | The passes, levels, `moveCheckPasses`, rules 13 to 15, its lever, its tests | Budgets, runs, frame sets, statistic, the four scenes, and the guarantee, now proved by discs. *Move check* now means the end of the turn job to the end of the step job. Level metrics become held vehicles: an order and a zero step |
| 0013 | In "What movement does with a sample", the target of a vehicle with a valid spot. In rule 16, the same target | The bit, encoding, sampling, and rule 17 |
| 0014, 0015 | Option C's reason in ADR 0014: an arrived group no longer stands inside discs | Issue 5's gates, with "slot pitch" now 1.42 |
| 0017 | "Orders" (the goal line's z), "Why the goal lines sit at the seam", and the series number | Lanes, `LaneGap`, x values, spawn formula, the definitions of passed and met, rules 18 to 20, renewal, movement cost |
| 0018 | "Nothing is adopted" and "Each side keeps one shared goal". The layout table gains the rows below. Version 3 stays #232's | The label, its removal bar word for word, reporting, the tick 1,800 check |

### Motor

**Turn stage** (parallel, replaces `IntegrateMovementJob`). A vehicle in `Moving` mode with a nonzero `NewVelocity` turns toward it by at most `TurnSpeed × TickDuration`, 12° at today's asset value (checked). It reads no neighbour and no map. Other modes don't turn.

**Step stage** (`DiscClearStepJob`, parallel, replaces `OverlapMovementJob` and `MoveCheckPassJob`). All inputs are immutable: start poses, new headings, the grid, unit rows. Each vehicle writes only its own row.

1. **Gate.** It drives only if `Moving`, with a route, and with its new heading within one tick's turn of that route. #250 used 45°, and #252 measured 12°. See "The 10-unit rung".
2. **Speed.** `s = min(type speed, |NewVelocity|) × dt`, capped by the distance to its target. There is no acceleration ramp, as measured, and a later ramp stays safe because it only lowers `s`.
3. **Neighbours.** For every live vehicle j within `r_i + LargeRadius + 2 × maxStep`, through the grid and never capped: `n` is the unit vector from i's start to j's start, and `c = h_i · n`. When `c > 0`, then `g = d − r_i − r_j − 0.0001`, `w = 1` if `h_j · n ≥ 0` else `0.5`, and `s = min(s, max(0, w × g / c))`.
4. **Plane edge.** `s` is cut along the heading so the disc stays inside the plane limit.
5. **Short steps.** A step under 0.001 cells becomes zero.
6. **Static.** The step is taken only if `StaticClamp.NoWorse` accepts it. Otherwise the vehicle holds its position this tick.

**Commit** is the only writer of a vehicle's pose. `Velocity` is displacement over `dt`.

**Determinism and parallel rules.** Every decision reads state from before the stage and writes its own row. `min` doesn't depend on order, and "The indices have no guaranteed order" ([IJobFor][ijobfor]) therefore can't change a result. There is no single-threaded part, no second pass and no read before the sync point. #250's and #252's byte-identical streams measure this at one seed, 103.

### Safety argument

The step is in the style of buffered Voronoi cells, where each robot keeps to its side of a shared boundary, and whose authors prove collision avoidance for simple straight-line motion ([Zhou et al.][bvc]).

**A pair with clear discs at the start stays clear** (arithmetic). Separation along `n` changes by `−s_i c_i − s_j c_j`, with `c_j = −h_j · n`.
- Only i closes: its limit is `g / c_i` and j adds or changes nothing, so the loss is at most g.
- Both close: each sees the other closing, so `w = 0.5` on both sides and the loss is at most g.
- Neither closes: separation doesn't fall.

The two units can't disagree on the case, because `n` negates exactly in floating point. So separation stays at least `r_i + r_j + 0.0001`, and true distance is never less than separation. A footprint lies inside its disc at every heading (ADR 0008), so clear discs can't overlap however they turn.

**Rounding.** At |x| < 128 a position's unit in the last place is 7.6 × 10⁻⁶ cells. Rounding across both units is under 3 × 10⁻⁵, inside the 10⁻⁴ margin (arithmetic). At the 295-cell map limit, one unit in the last place is 1.5 × 10⁻⁵ and the margin still holds.

**Between ticks.** Both units move in straight lines, and separation along `n` is linear in time, so it stays above the sum at every interpolated frame (arithmetic). This closes ADR 0008's risk of a drawn corner contact. Neither #250 nor #252 measured it.

**Overlapping discs at the start are not safe.** This is the condition the whole motor rests on (arithmetic):
- *A turn.* Two small squares 0.625 apart that both turn 45° are 0.442 apart along their own axes, under their 0.5 width, so they overlap.
- *A step.* A large vehicle at the origin faces +x. A small one at (0.83, 0.31) heads 110°. They don't overlap: their reaches along x add to 0.82. The small one isn't closing (`h · n` = +0.008), so nothing limits its step of 0.133. It ends at (0.785, 0.435), overlapping on all four axes, even though the distance grew. With equal sizes and one mover this can't happen, but mixed pairs can.

So no vehicle may ever start inside a neighbour's disc. See "Spacing".

**Other cases I checked.**
- *Idle and hold.* These vehicles neither turn nor step. A mover's `w = 0.5` against an idle vehicle facing it is only conservative.
- *The gate.* It limits when a vehicle drives, never how far, so it plays no part in the proof. At 45° it causes the seam jam, and at 12° it doesn't (measured, #252).
- *Plane edges.* The cut is along the heading, so it adds no slide, and a vehicle at the limit can still turn and drive inward.
- *Short steps.* #250's B4 read 1.0489° of slide, above the 1° bar, which fits rounding on steps near 0.0003 cells (inference). Its lateral total stayed at 0.0002 cells. At 0.001 cells rounding gives at most about 0.31° at |x| < 128 (arithmetic). Without the short-step rule the bar could fail with no vehicle sliding. With the rule, #252's B4 reads 0.098° and 2.2 × 10⁻⁶ cells (measured), though #252 changed the gate too, so the two effects aren't separated.
- *Measured.* All 22 of #250's motor runs and all 18 of #252's had no overlap and no static error, starting clear at 1.42.

### Walls and obstacles

#250's and #252's map was empty, so nothing here is measured with this motor. Static obstacles always meet the disc, as in ADR 0008.

**No static error can occur** (arithmetic). A step is accepted only if no blocked cell's penetration grows, the test is at the endpoint, and a step of at most 0.133 cells can't cross a cell. Turning doesn't change a disc. So no clamp is needed, and none may move a vehicle: a clamp is a slide.

**The cost of having no clamp.** A vehicle touching a wall with any part of its route pointing into it holds forever, because its route doesn't change while it stands still. Field blending near a wall's end does this (inference from #220's wall-end wiggle). So:

- **Tangent at contact.** When a step was refused by a blocked cell last tick, the vehicle takes the tangent of its nearest blocked point, on the route's side, as its heading target. It keeps that target until the route's ray no longer meets that point at the vehicle's own radius. This is #220's S1. It holds one point, one heading and one flag per unit. It is still a turn and then a drive, so it adds no slide.
- **Evidence.** On #220's fixtures, under the old check, S1 cut pivots from 1,427 to 204 and raised active corner flow, but delivered 56 of 110 on the small wall against 110 for the plain tangent (measured). Those jams fit that check's turn locks (inference), and under this motor it is unmeasured.
- **Spots.** A vehicle's spot is valid when its disc clears every blocked cell and it lies in the goal's region. Otherwise its spot is the shared corrected goal, which is ADR 0008's overflow. An in-sight vehicle steers at its spot, so ADR 0013's clear-line proof no longer covers that line. The tangent rule handles a graze.

### Spacing

Pitch is 1.42 widths: 0.71 and 1.42 cells, against discs of 0.7071 and 1.4142. `slotSpacing` is 1.42 and must lie between 1.42 and 1.43. Below that a block starts inside discs. Above that S2.2 fails (below).

| State | On `main` today (checked) | Under this ADR |
|---|---|---|
| Block spawn | Pitch 1.25, inside discs | Pitch 1.42, discs clear |
| Block beside other units | `BlockPlacement.Overlaps` tests aligned squares | Tests the sum of radii. Refused, as today |
| Single add | `AddUnit` checks only the map, so it can stack on a unit | Refused when its disc overlaps a live vehicle |
| Arrival | Rules 1 to 4 stop a vehicle where it is. Slots don't exist yet | The same. The motor never enters a disc, so a stop is disc-clear |
| Travel in a formation | None. A group shares one goal and has no slots | Own spots at pitch 1.42, so discs stay clear |
| Idle and hold | The overlap pass can push idle units | Never moved or turned |
| Parked slots (ADR 0008 Issue 4, unbuilt) | Specified at 1.25, as formation pairs | Pitch 1.42. No formation pairs for vehicles |
| `slotSpacing` 1.25 | Used only by block spawn (`ApplyCommandsJob`) | Replaced |
| New order from any of these | | A free turn, because discs are clear |
| Scenes and tests placed by hand | `MovementMeasurement` scene 0 at pitch 1, `MoveOrderTests` lattice at 0.625 and touching at 0.5 | Scene 0 and the lattice at 1.42 pitch. The touching cases go. Scenes 1 to 3 are at 0.72 and stay |
| Later systems (S9 buildings, S10 unit exits) | Unbuilt | Must place vehicles disc-clear. Their ADRs say how |

In development builds the measurement counts any pair inside discs, and a nonzero count fails the gates. There is no release guard: it would cost a second neighbour scan for a state no path reaches (judgment).

**What the pitch does to shape** (arithmetic).
- Blocks grow 13.6% in width and depth from 1.25 and 9.2% from 1.3. Area grows 29% and 19%. A block of 1,000 small vehicles goes from 395 to 507 cells², and 1,000 large from 1,580 to 2,027.
- S2.2 stays reachable, but only just, and only on square blocks. 100 small vehicles fill 1.90 times their footprint on a 10 × 10 block, and 1.95 with each settled within 0.05 cells. ADR 0008's 15 × 7 block is 1.99, and 2.05 settled. So vehicles use `ceil(sqrt(N))` columns, as spawn already does. 1.43 is the most that stays under 2 when settled.
- In travel, columns stretch by one tick per link: 0.71 to 0.84 and 1.42 to 1.50 (measured). A 2,000-unit block ends cycle 0 18% and 5% longer. By the end of cycle 2, First is 20% longer and Second is still under 6% (#252, seed 103). Vehicles recover their pitch on reaching their spots (inference). Neither spike tests that, because orders renew long before arrival, about 75 cells short at 10 units (checked).

**Owner decision, 2026-11-07.** Asked whether vehicles may stand 1.42 widths apart, the owner answered "yes, go with 1.42" (Producer session, recorded on #251). The owner's earlier answer, on #202, was that parked vehicle groups may stand 4% further apart (1.3), and this is 13.6%. The yes accepts the costs above: bigger blocks, and S2.2 reachable only on square blocks. No design within budget was found that keeps 1.3: a free 45° turn needs 1.41 (arithmetic), and a checked turn is options B and C below.

### Spots: game rules and ladder layout

| Item | Verdict | Why |
|---|---|---|
| Pitch 1.42 (#245's P) | **Game rule** | Every spawned or parked group needs it |
| Own spot (#245's O): order point plus the vehicle's offset from the group's centre at order time, clipped to the plane | **Game rule** | Shared goals lock. Two vehicles on converging lines lose all gap, and then every step has `c > 0` and is zero, for good (arithmetic). The owner's #245 answer asks for it |
| Drive gate and short-step rule | **Game rule** | Motor |
| L: keep spawn z when the goal z lies inside the block | **Ladder layout, not code** | Order each side to its own block's centre z. With own spots the spot's z is then `z_c + (z_i − z_c) = z_i`, the same as L (arithmetic). As a game rule, L would stop a click inside a block's width from centring it, which no design asks for |
| M: rows fill from the seam outward | **Ladder layout** | Without it Second's partial row sits on the seam. `BlockPlacement` takes a row-order argument, and only the ladder uses it |

**Scenario version 4.** Version 3 is #232's and its entry merges first. Any change after that is a new version (ADR 0018). Version 4 changes these rows and keeps the rest of ADR 0018's table: gap 1.2, centres −40 and +25, far ends ±120, spread 7, meet 2 cells, sample 15 ticks.

| Field | Version 3 | Version 4 |
|---|---|---|
| Spawn pitch | 1.25 | 1.42, from `slotSpacing` |
| Order z | Goal lines at ±gap/2 | Each side's block-centre z |
| Row fill | First from the seam, Second toward it | Both from the seam outward |

**Arrival with own spots.** Rule 1 uses the spot. Rule 2, stopping beside an arrived group mate, applies only to vehicles whose spot fell back to the shared goal. At pitch 1.42 neighbours are always within its 0.1 cells, so it would stop vehicles short of their spots (arithmetic). Rules 3 and 4 stay.

**Own-spot limits.** A group keeps the shape it was ordered in. Compacting a scattered selection, or turning a block's lattice to face its order, needs converging lines, and both stay with ADR 0008 Issue 4. That Issue must show it doesn't lock.

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

### Gates

The implementation's last game-code PR and its ladder entry must show all of these. Everything is on the ladder PC, whose entries say `reference-class: no`.

| # | What | Bar | Status |
|---|---|---|---|
| 1 | Ladder entry, version 4, all nine rungs, three valid runs each | ADR 0018's removal bar for every rung, side and run: travel at least 0.60 in cycles 0, 1, 2; at least 90% passed and one spawn row met in cycles 1, 2 | Gate. The label stays, and a later ADR removes it |
| 2 | Same entry, 2,000 rung | ADR 0004's pass rule. Regression check against the newest version 4 entry | Gate |
| 3 | Physical run in game code, development Player, 2,000 units, version 4 layout, 1,800 ticks, measured outside the timed jobs | Slide at most 1°, lateral at most 0.01 cells, zero footprint overlap, zero static error, zero pairs inside discs | Gate. Other rungs are reported |
| 4 | Job orders 1/1, 1/64, all/1, same run | Full physical state byte-identical | Gate |
| 5 | ADR 0012 certification: its four scenes, uncapped at 1,000 and 2,000, capped at 2,000, three runs | Move check p99 at most 0.5 ms, chain p99 at most 3 ms, main-thread work and total at most 0.5 ms on sets U, S and C, ADR 0011's GC check | Gate |
| 6 | Full EditMode suite | Passes. Changes to tests are only those listed under Files | Gate |
| 7 | The 19 crowd cases in `MoveOrderTests` | Their assertions pass at the new spawn. Their S2.2 ratio is reported | Gate |
| 8 | `MovementCoreProfile` scenes 4 to 6 | S2.1's first-tick count, ADR 0014's wall gate (no overlap, none in a blocked cell, at least one small and one large past the wall), ADR 0015's ring gate. Delivered counts reported beside 26 of 110 and 15 of 100 | Gate on the safety lines |
| 9 | #220's fixtures: mixed, small and large × wall, ring, west, north, S2.3, at 1.42 | Slide, overlap, static, response and no reverse pass on all 15. First drive, delivered, group stall and jam lines reported beside #220's R+F column | Gate on the safety lines. Any fixture worse than R+F is listed in the PR and the final report |
| 10 | 0 B GC Alloc capture (ADR 0004 rule 11), and before and after Profiler numbers (AGENTS.md §4) | As stated | Gate |
| 11 | Lowest single-vehicle travel share, seam rows' minimum seam z gap, block length in travel | Reported | Report |

**Why flow lines in fixtures aren't gates.** #249's bar doesn't include them, and ADR 0014 and ADR 0015 already put them on Issue 5. A bar isn't lowered by this, and the final report must quote them. The expected result is mixed: converging streams lock, so some fixtures will stall (see Consequences). A fixture line that fails on overlap, static error or slide is a stop.

### Rules for hot paths

These continue rules 1 to 20 and add to ADR 0004's rules.

21. **One pose writer.** `CommitMovementJob` is the only job that writes a vehicle's pose in a tick. No job pushes, clamps or corrects a vehicle's position.
22. **Discs stay clear.** Every path that creates a vehicle refuses a pose inside a live vehicle's disc. Development builds count such pairs, and the count must stay zero.
23. **The step reads start poses and new headings only.** It uses the formula above, with no footprint test, level, second pass or single-threaded part. The neighbour search stays uncapped.
24. **Turn before step, unchecked, one tick's turn at most.** The turn reads no neighbour.
25. **Drive only near the route.** Within one tick's turn. A step under 0.001 cells is zero.
26. **Own spots are worked out once per order,** in the single-threaded command stage. Movement jobs read `Spot` and never recompute a group's centre.

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

## Consequences

- **Benefits.**
  - Vehicles visibly don't slide, and the proof needs no order between them.
  - The chain shrinks by one job and one kind of test. Nothing at rest jitters, because nothing pushes.
  - Drawn frames can't show an overlap.
  - Groups keep their shape.
- **Tradeoffs.**
  - Groups stand 13.6% further apart and use 29% more area than today. The owner accepted this (see "Spacing").
  - **Rear ranks start late.** A column starts one tick per link, the following conflict of ADR 0012 ([Stern et al.][mapf]), so the back of a 32-row block reaches full speed about 1.0 s after the front (measured 4.05 cells at 4 cells/s). S2.1's test counts any nonzero velocity, and a rear vehicle does creep 0.003 cells a tick, so it passes. The short-step rule leaves that creep alone: First's seam row, front to back, had no zero step in cycle 0 (measured, #252). A player may not read that as moving within 0.1 s. Option E is the lever. This goes in the final report.
  - **Turning vehicles drive late.** The gate holds a vehicle still while it turns. One ordered to reverse stands for 13 ticks, 0.43 s (measured, #252), where #250's gate held it for 11 (arithmetic). Any vehicle more than 24° off its route has zero velocity on the order's tick (arithmetic). It is visibly turning from that tick, but S2.1 says every selected unit starts moving within 0.1 s. The first-tick tests don't see this, because `MoveOrderTests` and `MovementCoreProfile` scene 4 order vehicles toward a point they already face (checked). This ADR doesn't change S2.1. This goes in the final report.
  - **Converging lines lock.** Two vehicles that touch while both closing stop for good until their order ends. So funnels such as S3.3's gap will likely stall (inference, unmeasured), and only the group stall ends them. Deadlock is a known problem of this family of methods and has its own recovery research ([Abdullhak and Vardy][bvc-deadlock]).
  - **No push.** An idle vehicle in a mover's way now stays there, so the mover stops. S3.5 moves wholly to Issue 5.
  - S2.2's margin is thin: 1.95 when settled, against 2.
- **Risks.**
  - Walls, offsets taken at order time (#250 and #252 fixed them at spawn), and the tangent rule are unmeasured. Each has a gate above. The 12° gate is measured, but only in #252's throwaway code.
  - Second's met count has no margin, and what it counts is side-by-side passing, not crossing, as above.
  - ADR 0012's gap, dense and wall scenes have never run with this motor. Their check cost is estimated similar to the ladder's, because density is similar (judgment).
  - The ladder PC isn't reference class, and nothing here measures a 6-core CPU (ADR 0012, ADR 0017).
- **Cost at 2,000 units.** As in option A, measured in the harness. Memory: `Spot`, `SpotOffset` and tangent state add about 30 B per unit, about 60 KB. The level arrays go. No GPU cost, and no allocation after warm-up (gate 10).
- **Reversibility.** Moderate. The motor is one job swap, and `AvoidanceJob` still only writes `NewVelocity`. The pitch is one value, but changing it is a new scenario version.
- **Triggers.** Each one comes back to the architect.
  - Gate 3 or 4 fails, or a fixture shows an overlap, static error or slide. That is a defect in the proof or the code.
  - Gate 5 fails on the move check with this motor.
  - Delivered counts in gate 8 fall below the baselines. A contact-tangent rule for vehicles, not yet designed, is the first candidate.
  - ADR 0017's ORCA trigger, and ADR 0018's "a ladder entry meets the bar", which calls for the later ADR that removes the label.
- **Dependencies.** No package, Unity or handbook change. The final report should also carry S2.1's rear-rank and turning delays and that S3.4 stays open.

### Follow-up work

**Spike: done** (#252, `type:spike`, gameplay-programmer, no game code). In #250's harness it changed only the gate to 12° and added the short-step rule. It re-ran Stage C (nine rungs), seeds 7 and 211 at 2,000, the cost and job-order repeats, and the head-on B4 case. All 19 runs took about 10 minutes.
- *Measured:* every figure in #250's table, the lowest single-vehicle travel share, the minimum seam z gap per cycle, and the head-on B4 case again.
- *Kill rule, fixed before the run:* any rung, side or judged cycle under the bar, any vehicle under 0.60 of free travel in cycles 0 to 2, any physical error, non-identical job orders, or check p99 over 0.5 ms or chain p99 over 3 ms at 2,000.
- *Result:* no kill item was crossed. The figures are under "What #252 measured", and the finding is on #252. The spike's code is not merged, and the game version is the work below.

### Files for the implementation Issues

One Issue per PR, in this order, one programmer, after #232's PR merges (now PR #253; PR #242 was closed unmerged). Assets overlap, so they don't run in parallel (`docs/studio/workflow.md`). PR 2 changes no tick job, so it needs no ladder entry (ADR 0004).

| PR | Files | Change | Changed lines (estimate) |
|---|---|---|---|
| **1. Spawn, entry, own spots, version 4** | `Simulation/Movement/MovementParameters.cs`, `Presentation/MovementSettings.cs`, `Data/MovementSettings.asset` | `slotSpacing` 1.42, range 1.42 to 1.43 | ~380, with a ladder entry on the old motor, so the motor's effect shows on one layout |
| | `Simulation/BlockPlacement.cs`, `Simulation/ApplyCommandsJob.cs` | Disc tests at spawn and add, row-order argument, spot from offset | |
| | `Simulation/UnitCommand.cs`, `Simulation/SelectionJob.cs`, `Simulation/Movement/MovementUnit.cs` | Group centre and offset per order, `Spot`, `SpotOffset` | |
| | `Simulation/FlowFields/RequestFieldsJob.cs`, `Simulation/Movement/PreferredVelocityJob.cs`, `Simulation/Movement/OrderProgressJob.cs` | Spot validity at goal correction, target is the spot, rule 2 for fallback vehicles only | |
| | `Presentation/Diagnostics/LadderScenario.cs`, `Data/PerformanceLadder.asset`, `Presentation/Diagnostics/PerformanceLadder.cs` | Version 4: order z, row fill, minimum travel figure | |
| | `tools/unit-ladder.js` and its tests, `docs/performance/unit-ladder/README.md`, a new entry | The fixed line, the figure, the version | |
| | Tests: `BlockPlacementTests`, `UnitSimulationTests`, `MoveOrderTests`, `MovementCoreTests` (spacing check), `LadderFrameBufferTests`, `SandboxSceneBuilderTests` | Spawn at 1.42, lattice at 0.71, touching cases gone, refused adds | |
| **2. Measurement** | `Simulation/Movement/MovementMeasurement.cs` | Slide and lateral as #220 measures them, pairs inside discs, state hash, version 4 layout scene | ~300 |
| | `Presentation/Diagnostics/MovementCoreProfile.cs`, `EditorTools/MovementCoreProfileBuilder.cs` | Job-order options, fixture size set and start heading, fixture lines | |
| | Tests beside them | Baseline run reported on the old motor | |
| **3. The motor** | New `Simulation/Movement/DiscClearStepJob.cs`, `TurnVehiclesJob.cs` | As above | ~650, of which about 300 are deletions. Over 400 because splitting it would leave two motors on `main` (AGENTS.md §4). The PR says so |
| | `MovementPipeline.cs`, `CommitMovementJob.cs`, `MovementParameters.cs`, `MovementSettings.cs`, `MovementSettings.asset` | New chain, `StepBound` is the top step, `moveCheckPasses` and `acceleration` go | |
| | Delete `IntegrateMovementJob.cs`, `OverlapMovementJob.cs`, `MoveCheckPassJob.cs`, `MoveCheckData.cs` and their `.meta` files | | |
| | `MovementCoreTests`, `MovementMeasurement.cs` scene 0 | Level tests replaced: pair cases, inside-disc counterexamples, weights, plane edge, short steps, job orders, static hold | |
| | A ladder entry | First candidate for gates 1 and 2 | |
| **4. Walls** | `PreferredVelocityJob.cs` or `TurnVehiclesJob.cs`, `DiscClearStepJob.cs`, `MovementUnit.cs` | Tangent at contact, held to release | ~250 |
| | `MovementCoreTests`, `MoveOrderTests` | Wall end, concave corner, ring, one-cell gap for small vehicles | |
| | A ladder entry, all gates | The entry a later ADR may name | |

Paths are under `Game/Assets/Code/` except data assets, tools and docs. After the bar is met, the architect writes the later ADR that names the entry and removes the label. It restates the fixed line above.

[ijobfor]: https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Unity.Jobs.IJobFor.html
[mapf]: https://arxiv.org/abs/1906.08291
[bvc]: https://msl.stanford.edu/bibliography/zhou_fast_2017.html
[bvc-deadlock]: https://researchr.org/publication/AbdullhakV21
