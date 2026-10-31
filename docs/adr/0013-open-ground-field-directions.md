# ADR 0013: Flow-field directions in open ground (line of sight)

## Status

Accepted

## Date

2026-10-31

## Context

[ADR 0008](0008-movement-and-pathfinding.md) builds each flow field with Dijkstra's algorithm over eight neighbours (costs 10 and 14, no corner cutting). Per cell it stores "a direction byte: the angle to the neighbour with the lowest cost". [ADR 0011](0011-flow-field-storage.md) fixes how movement samples a field (Sampling, points 1 to 8) and how the byte is encoded (point 7).

**What #160 found.** Run 1 of #160 (local commit 29096cd) built fields this way. The 19 asserted crowd cases of `HundredUnitsComeToRestWithoutAnyTickAddingFootprintOverlap` in `MoveOrderTests` (#152) failed. Each case orders 100 small cubes, in a 10 × 10 block at the origin, about 15 cells along +x on an open 256 × 256 map. The architect ruling in #160's Notes (2026-10-31) found the cause:
- In open ground, take a cell off the goal's row and column. The straight and the diagonal neighbour toward the goal both lie on a shortest eight-way route. The diagonal one is 14 closer to the goal and the straight one only 10, so "lowest cost" always picks the diagonal. Every such cell points 45° toward the goal's row or column.
- The two halves of the block steer at +45° and −45°, so they meet at 90° along the goal row, inside the block. The move check ([ADR 0012](0012-move-check-passes.md)) keeps them from overlapping, so they lock, and the group stall ends the order as designed. 85 to 99 of the 100 units end with under one cell of progress.
- On main, the same cases use direct seek. Every unit heads within about 13° of +x (atan(2.81 / 12.19)), neighbours' headings differ by much less, and the cases pass.
- The ruling ruled out stopgap switching, sampling near the goal and `directSeekRange` as causes. Its disallowed list includes raising `directSeekRange`, changing the cases, per-unit direct seek inside an order that has a field, and changing the direction rule, Sampling or the encoding outside an accepted ADR. No fix fits the accepted ADRs.

**The owner's choice** (#163, 2026-10-31): option A. Fix the route rule now. A unit with a clear line to the goal walks straight at it, and follows the field only when an obstacle is in the way.

The artefact is a known one. Openage found that "the flow field vectors only support 8 directions", so "paths become diamond-shaped". It added a line-of-sight pass, so units in marked cells "can then be pathed directly towards the target in a straight line" ([openage][openage]).

**Constraints**
- ADR 0004's hot-path rules. ADR 0008, ADR 0011 and ADR 0012 except where superseded here. No new package.
- **Build budget** (ADR 0011 Triggers): at most 20 ms at the 95th percentile on the largest allowed map (87,381 cells, about 295²), and at most 1% of builds missing their due tick. #141 measured a p95 of 1.27 ms open and 2.62 ms with 20% of cells blocked, at 256².
- **Pool:** 3 B per cell and 256 fields, which is 48 MiB at 256². The 64 MiB limit sets the map limit.
- The guarantee that no tick adds a footprint overlap and no disc reaches further into a blocked cell (ADR 0008, ADR 0012).
- The 19 cases pass unchanged.
- Out of scope (#163): avoidance, turn rules, slots, pace, and code.

**Evidence gap.** Run 1's results and main's behaviour are measured. Nothing in the options below is. Every cost is an estimate from operation counts, scaled from #141's build at about 14 ns per cell (0.896 ms for 65,536 cells).

## Decision

**Each build also sets an in-sight bit per cell: whether the straight line from the cell to the goal is clear. A unit whose sampled values are all in sight direct-seeks its own goal. Everywhere else it follows the field as before. The bit sits in a spare bit of the direction byte, so pool memory and the map limit don't change.**

### The in-sight bit

For a field with goal value g and class k, value v's bit is set exactly when:
- v is reachable, and
- the closed segment from v's point to g's point meets no cell that is impassable for class k.

Here cells are the unit squares of the class's lattice, centred on value points (ADR 0011 Sampling point 3). The segment meets a cell when it touches it, including at a corner. That is ADR 0008's direct-seek walk with its corner rule, without the range limit. The goal value's bit is set.

**How the build computes it.** `BuildFlowFieldJob` runs a sight pass in its own scratch, before or after the search, using integer arithmetic only.
- **The sweep.** It sweeps outward from the goal, row by row in each of eight octants. It keeps the slopes still lit as a list of intervals with rational ends. This is shadowcasting, where "Quadrants are traversed row by row" and exactness comes from "rational numbers instead of floating point" ([symmetric shadowcasting][ford]). We write it from the description and copy no code.
- **Shadows.** The blocking rule is the walk's, not that page's. In octant coordinates (depth d ≥ 1 from the goal, column 0 ≤ c ≤ d), an impassable cell at (d′, c′) removes the closed slope range from (2c′ − 1)/(2d′ + 1) to (2c′ + 1)/(2d′ − 1) from every later row. It does so whenever its square overlaps a lit range, even if its own centre is already in shadow.
- **Interior cells.** A passable cell at (d, c), with 0 < c < d, is in sight when c/d lies inside a lit range. A slope exactly on a shadow's end touches a corner, so it isn't in sight.
- **Axis and diagonal.** These are single rays:
  - (d, 0) is in sight when it is passable and (d − 1, 0) is in sight.
  - (d, d) is in sight when it is passable, (d − 1, d − 1) is in sight, and both side cells (d, d − 1) and (d − 1, d) are passable (the corner rule).
- **Map edges.** A segment between two cells inside the map meets only cells inside it, so cells outside the map are never visited and never cast shadows.
- **Arithmetic.** Slopes are compared by cross-multiplying integers.
- **Scratch.** A row's lit list holds at most one more interval than the row has cells. Two such buffers per build slot are allocated at match start, about 8 KiB at 256².
- **Cost.** Each cell that overlaps a lit range is visited once per octant it belongs to. Est. 0.15–0.35 ms at 256² open, and less where obstacles hide most of the map.
- **Directions unchanged.** The direction bits keep ADR 0008's rule: the angle to the neighbour with the lowest cost.

### Encoding

This replaces ADR 0011 Sampling point 7.
- **Direction byte:** the direction d (0 to 7) in bits 0 to 2, encoded as ADR 0011 point 7 sets. The in-sight bit is bit 3 (value 8). Bits 4 to 7 are zero.
- **Distances** don't change.
- **Memory.** A field still takes 3 B per cell. The pool stays 48 MiB at 256 fields on 256², and ADR 0011's 87,381-cell limit and match-start checks stand.
- **Goals.** The storage adds a `goals` array: one `int` per field holding its goal cell, 1 KiB at 256 fields. `PublishFieldsJob` writes it with the field, as it does `classes`, and the reader holds it as `NativeArray<int>.ReadOnly`.

### Sampling

This replaces ADR 0011 Sampling point 6 and adds one field to `FlowSample`. The type names, `Sample`'s signature and points 1 to 5 and 8 stay.

```csharp
internal struct FlowSample
{
    internal float2 Direction; // world x and z: unit length, or zero at the goal
    internal float Distance;   // cells along the field to the goal
    internal bool InSight;     // Ready, and every contributing value is in sight
}
```

**Point 6, Direction.** The candidates are the contributing values whose distance is above 0, as before. A candidate's vector depends on its bit:
- In sight: the unit vector from `position` to the goal value's point.
- Otherwise: its direction byte's vector (point 7).

Then s, W, the cancel test and the heaviest-candidate fallback apply to these vectors unchanged.

**`InSight`** is true when `Sample` returns `Ready` and every contributing value's bit is set.
- Then every candidate has the same vector, so the direction points straight at the goal value's point. It is zero exactly on that point, where only the goal value contributes.
- With `Unreachable` or `Unavailable`, `InSight` is false.
- `Distance` keeps point 5, the field's distance, in every case.
- Point 8 holds: `Sample` reads at most four values from each per-cell array, plus the field's class and goal.

### What movement does with a sample

| Sample | Direction | Remaining route |
|---|---|---|
| `Ready`, `InSight` | Direct seek of the unit's own goal point: the unit vector to it | Straight-line distance to that point |
| `Ready`, not `InSight` | The field's `Direction` | The field's `Distance` |
| `Unreachable` or `Unavailable` | Stopgap direct seek (ADR 0011 point 2 and rule 11) | Straight-line distance |

- **A full route.** An in-sight unit's direct seek is ADR 0008's direct seek as a full route. It has no `directSeekRange` limit, because the build has already done the line test.
- **Remaining route.** Such a unit measures its remaining route the way ADR 0008 measures it for a unit without a field: as a straight line. That covers progress now, and pace's r_i and the arrival condition when Issue 4 builds them. No pace, slot or arrival rule changes.
- **Its own goal point.** The unit steers at its order's point, not at the goal value's point. That is where the order sends it, and it is what direct seek on main steers at. The two points are at most 0.71 cells apart.
- **Unchanged.** An order still requests no field when every unit has a clear line within `directSeekRange` (ADR 0008). S2.1 is unchanged: units move from the order's tick by stopgap direct seek.

**Open ground.** With no obstacle, every reachable value is in sight. So from the tick its field is published, every unit steers exactly as direct seek does on main and counts progress the same way. The only difference from main is that progress restarts once, when the field is published, because run 1 restarts it when a unit's field changes. So the 19 cases are expected to pass unchanged. Headings stay within direct seek's 13° of +x, instead of meeting at 90°.

**Behind obstacles (S3.1).**
- **In shadow.** A unit follows the eight-way field until its samples are in sight, then goes straight.
- **Route length.** From any point, the straight line to the goal is no longer than the rest of the eight-way route. So following the field into sight and then going straight is never longer than following the field all the way. The field's route is at most about 1.082 times the straight line in open stretches (ADR 0008), inside 1.25.
- **Shadow edges.** A sample that mixes the two kinds of value blends the pull toward the goal with the field's directions, so the turn spreads over about one cell.

**Guarantee.** This changes only the preferred velocity's direction and the remaining-route distance that callers use. The static clamp, the overlap pass and the move check still run after it (ADR 0008, ADR 0012). So no tick adds a footprint overlap, and no disc reaches further into a blocked cell. A unit whose sampled values see the goal while its own disc grazes a box corner slides along the box, as any direct-seeking unit does.

### Rules for hot paths

These continue rules 1 to 15 (ADR 0008, ADR 0011 and ADR 0012) and add to ADR 0004's rules.

16. **The build does the long line test.** No movement job walks a line longer than `directSeekRange`. Beyond that, the in-sight bit is the line test. `Ready` with `InSight` means direct seek of the unit's own goal, with its straight-line distance as the remaining route. Direct seek's line tests within `directSeekRange` (orders without a field, slots and home) stay.
17. **The sight pass is integer-only and bounded.** It runs in the build job and its scratch, compares slopes as integers, allocates nothing, and visits each cell at most once per octant. Only the pool's code decodes direction bytes (rule 10), masking with 7 for the direction and 8 for the bit.

### What this ADR supersedes

From ADR 0008, only these parts:
1. In "Building a field": the direction-byte bullet. The byte also carries the in-sight bit, and the build computes it (The in-sight bit). The search, its costs, the corner rule, the queue, the scratch, the distances and the 1.082 bound stay.
2. In "Following a field": a unit whose sample is in sight direct-seeks its goal instead of following the blended directions.
3. In "Direct seek": a third full-route use, with no range limit: a unit whose sample is in sight.

From ADR 0011, only these parts:
1. Sampling point 6, and `FlowSample`'s new field.
2. Sampling point 7, the encoding.
3. In "The whole-map storage": the added `goals` array.

Everything else stays in force, including:
- **From ADR 0008:** the search, the pool, requests, leases, the publish tick, `directSeekRange` and its uses, local movement, groups, the tick rate and rules 1 to 9
- **From ADR 0011:** the pool size, the map limit and match-start checks, Sampling points 1 to 5 and 8, the stream test (`InSight` doesn't change the status, so "not `Ready`" still means stopgap) and rules 10 to 12
- **ADR 0012:** all of it

The earlier ADRs' text isn't edited (AGENTS.md §9).

### Packages

None are added.

## Alternatives considered

Costs are estimates, not measured. Builds are at 256², and "+" means added to #141's build.

| Option | Build, open / 20% blocked | Memory per cell | Open ground | Behind boxes (S3.1) | Crowd before avoidance | Hot path | Work |
|---|---|---|---|---|---|---|---|
| **A. In-sight bit (chosen)** | +0.15–0.35 ms / less | None (spare bit) | Straight at the goal, as on main | Eight-way until in sight, then straight. Never longer than the field's route | As main in open ground. Merging at shadow edges, unmeasured | One more read and one normalise per sample | Medium: a sweep and its oracle test |
| **B. Gradient of today's distances** | None | None, or 1 B less if the byte goes | 21.8° off the goal row, so the halves meet at 43.6° | Eight-way | Jam softened, likely still present | 16 reads per sample | Low |
| **B′. Gradient of an Eikonal field** | p95 about 2.5–5 ms open (2–4 × the search) | As B | Close to radial | Any-angle, shorter | Close to direct seek, unmeasured | As B | High |
| **C. Tie-break toward the goal bearing** | +0.05–0.2 ms | None | Parallel within 22.5° of an axis or diagonal. Seams at 22.5° + k·45° | Eight-way, hugs box faces | The 19 cases likely pass. Halves meet at 45° on a seam | None | Low |
| **D. Line test per unit per tick** | None | None | As A, exact per unit | As A | As A | About 0.3–2.5 ms of worker time per tick at 2,000 units | Low |

- **B. Directions from the distance gradient.**
  - *How it works:* `Sample` takes the direction as the negative gradient of the stored distances, from 16 reads instead of 4, as ADR 0011 option F reads them.
  - *Why it fails:* eight-way distances are 10 × max(|dx|, |dy|) + 4 × min(|dx|, |dy|), and their gradient points atan(4 / 10) = 21.8° off the goal row. That still gives only eight directions, so the halves would meet at 43.6° instead of 90°.
  - *Not chosen:* it halves the convergence but doesn't remove it.
- **B′. Gradient of an Eikonal field.**
  - *How it works:* the build solves the Eikonal equation for distances that approximate straight-line distance, with the fast marching method ([Sethian][fmm]). Continuum crowds (ADR 0008) builds a field of this kind.
  - *Gains:* directions are close to radial in open ground and any-angle behind boxes.
  - *Costs:* each cell needs a quadratic solve, and the distances' meaning changes. Its build cost and accuracy near the goal are unmeasured.
  - *Not chosen:* it does more than the owner's rule asks, at the highest cost and risk. It is the candidate if seams behind obstacles fail (Triggers).
- **C. Tie-break toward the goal bearing.**
  - *How it works:* the rule changes from the lowest-cost neighbour to the neighbour on a shortest route whose step is closest to the bearing to the goal. On grids, "there can be more than one shortest path of the same length", and the search "is going to pick one, and it may not be the one you prefer". One published fix is to "Nudge the paths when there's a tie" ([Red Blob Games, Ugly paths][ugly]).
  - *Limits:* directions stay eight. A block whose bearing to the goal is near 22.5° + k·45° splits along a seam, and the two sides meet at 45°. Even in the 19 cases, the wing units turn 45° inward about 7 cells from the goal. Behind obstacles, choosing toward the goal defers diagonal steps until they are forced, so units bunch on box faces.
  - *Not chosen:* it fixes the tested bearing, not orders in general.
- **D. A line test per unit per tick.**
  - *How it works:* each unit walks the line to its exact goal every tick (or every N ticks).
  - *Gains:* exact per unit, with no build change.
  - *Costs:* the work grows with units × distance: about 65–320 cells per unit, so 0.3–2.5 ms of worker time per tick at 2,000 units, against a 3 ms chain budget. Testing every 8 ticks cuts that by 8, but a unit's mode then lags by up to 0.27 s. With a range cap it becomes the ruling's rejected `directSeekRange` route, and the diagonal field returns beyond the cap.
  - *Not chosen:* the bit costs once per field, not per unit per tick, as ADR 0008's Pillar 4 reasoning asks. D is the fallback if the sight pass is too slow (Triggers).
- **E. Any-angle directions behind obstacles.**
  - *How it works:* in-shadow cells point at the obstacle corner their shortest any-angle route turns at. Theta* lets paths leave grid edges because "paths formed by grid edges can be longer than true shortest paths" ([Theta*][theta]).
  - *Gains:* the best routes behind boxes.
  - *Costs:* corner anchors per cell, and line tests during the search.
  - *Not chosen:* the owner's rule keeps the field behind obstacles. E is a candidate under Triggers.
- **Other ways to compute the bit.**
  - *A walk from every cell:* about 65,536 cells × 125–250 steps at 1.5–3 ns, so 12–49 ms at 256², near or over the 20 ms build trigger. It stays as the test oracle.
  - *A wavefront stopped by lines from the goal through obstacle corners,* as openage and the Bevy flow-field-tiles plugin ([integration field][bevy]) do. It costs about the same as the sweep, but its rasterised lines can differ from the walk by a cell along shadow edges. The sweep is exact for the same cost. The Bevy plugin runs the pass only in the goal's sector. A radius or sector limit would bring the diagonal funnel back at its edge.

## Consequences

- **Benefits.**
  - In open ground, ordered groups move as direct seek does on main, with the field still giving routes behind obstacles.
  - The bit costs nothing per cell in memory, and its work is per field, not per unit.
  - Routes in sight are straight, which is as short as a route can be.
  - The bit's meaning is ADR 0008's own walk, so a test can check it exactly.
- **Tradeoffs.**
  - In-sight units all steer at one point, so they converge as direct seek does (accepted in ADR 0008 for orders without a field) until Issue 4's slots.
  - Behind a box, in-shadow units follow eight-way directions, which can merge with in-sight units near the box's corner.
  - A unit's remaining route drops when its samples come into sight (straight line against the eight-way distance). That counts as progress, which is true. A unit pushed back into shadow sees it rise, which never counts as progress.
- **Cost at 2,000 units.** These are estimates, which #160 measures:
  - The build gains 0.15–0.35 ms at 256² open, on one worker outside the chain.
  - `Sample` gains one `int` read and one normalise per unit per tick, about 0.01 ms of worker time in all.
  - Memory grows by 1 KiB of goals, plus about 8 KiB of scratch per build slot. No allocation, and no GPU cost.
- **Risks.**
  - The sight pass's exactness at corners and octant edges. The oracle test covers it.
  - Walkability changes (S9). Until a rebuilt field is published, an in-sight unit can steer at a new building. The clamp and the move check hold it there, for up to about 4.3 s at worst (ADR 0011). The S9 Issue measures this.
  - The 19 cases differ from main only by the progress reset at publish, so they are expected, not proven, to pass.
- **Reversibility.** Easy. Ignoring the bit restores ADR 0008's behaviour, and the byte layout keeps the direction where it was.
- **Triggers.** Each one comes back to the architect.
  - **The sight pass is slow.** Its p95 is over 1 ms at 256² (open, 20% blocked, or 2% single cells) on the ladder PC, or a build breaks ADR 0011's build trigger. #160 stops and reports, and a superseding ADR weighs D.
  - **The 19 cases fail** with this ADR implemented as written. #160 stops and reports, and the ruling's disallowed list still applies.
  - **Behind boxes.**
    - If #160's Player check (its acceptance criterion 7) fails, #160 stops and reports.
    - If the behind-box test below ends its order by group stall, #160 reports it in the PR, and the Producer takes it to the owner.

    Either way, a superseding ADR then weighs C, B′ and E for in-shadow cells.
- **Dependencies and owner decisions.** No package, no Unity change and no handbook change. No owner decision is needed beyond the new owner instruction that #160 already waits for.
- **Follow-up Issues.**
  - **#160 (ADR 0008 Issue 2, part 2).**
    - *Scope added:*
      - the sight pass in `BuildFlowFieldJob` with its scratch
      - the encoding and the `goals` array
      - `FlowSample.InSight` and point 6 in `FlowFieldReader`
      - the in-sight branch in `PreferredVelocityJob` and `OrderProgressJob`, which must agree on the remaining route

      Its acceptance criterion 1 then reads "in ADR 0013's encoding". Nothing else in its scope changes, and the ruling's disallowed list still applies.
    - *Tests added:*
      1. **Oracle.** Every value's bit equals an exact integer walk to the goal, with the corner rule. The maps are 64 × 64: open, one box, 20% blocked with three seeds, and 2% single blocked cells. Also the sandbox layout at 256², with two goals. Each map runs for classes 0 and 1, with goals at the centre, an edge, a corner and next to a box. Unreachable values have the bit clear.
      2. **Directions unchanged.** On the same maps, bits 0 to 2 equal the lowest-cost-neighbour rule, and bits 4 to 7 are zero.
      3. **Sampling.**
         - All four values in sight.
         - One value in shadow: a blend of the bearing and that value's byte.
         - None in sight: as before.
         - Exactly on the goal value's point: `Ready`, `InSight`, zero direction and distance 0.
         - `Unavailable` and `Unreachable`: `InSight` false.
      4. **Open ground.** On the open 256² map, from the tick a 100-unit block's field is published, every sample is `InSight`. Every unit's preferred velocity equals main's direct seek, bit for bit, on every tick.
      5. **The 19 cases** pass unchanged. The PR lists each case's logged line beside main's.
      6. **Behind a box.** The same block is ordered to (15, 0) with a box over x 7 to 9 and z −4 to 4.
         - *Gated:* no tick adds a footprint overlap, and no disc reaches further into a blocked cell.
         - *Reported:* each unit's progress, the arrival rules used, group stalls and the last-change tick.
      7. **Detour.** The detour test of acceptance criterion 6 stays within 1.25.
    - *Measurements added:*
      - the sight pass alone and the whole build, median and p95, at 256² (open, 20% blocked and 2% single cells) and at 295² open
      - missed due ticks during the stream
      - `PreferredVelocityJob`'s p99 at 2,000 units before and after, with the ADR 0012 matrix still passing
      - scratch memory
  - **#161 (part 3).**
    - The overlay tints in-sight cells, and gets directions through the pool's code, masked (rule 10).
    - A corrected goal is both the field's goal and the units' goal, so units in sight of it steer straight at it.
    - Its tests add the oracle for a corrected goal at the ring of boxes, and its ladder entry includes the sight pass.
  - **ADR 0008 Issue 4 (groups and arrival).** In-sight units use their straight-line distance for r_i, progress and the arrival condition. The slot rules are unchanged. Its arrival tests run with fields published.
  - **ADR 0008 Issue 5 (ORCA).** It adds the behind-box crowd test to its scenes, and reports units stalled near shadow edges before any avoidance trigger counts.
  - **S5 and S9.** Moving targets and walkability changes need no extra work: every build sets the bit. S9 measures the stale-field wait above.
  - **Suggested:** add rules 16 and 17 to the gameplay-programmer and code-reviewer role files, together with ADR 0008's follow-up 6 and the rules of ADR 0011 and ADR 0012. These are protected paths, so that Issue must name them.

[openage]: https://blog.openage.dev/openage-development-news-march-2024.html
[ford]: https://www.albertford.com/shadowcasting/
[fmm]: https://pmc.ncbi.nlm.nih.gov/articles/PMC39986
[ugly]: https://www.redblobgames.com/pathfinding/a-star/implementation.html
[theta]: https://jair.org/index.php/jair/article/view/10676
[bevy]: https://docs.rs/crate/bevy_flowfield_tiles_plugin/latest/source/src/flowfields/fields/integration_field.rs
