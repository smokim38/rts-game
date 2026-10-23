# ADR 0008: Movement and pathfinding

## Status

Accepted

## Date

2026-10-23

## Context

[ADR 0004](0004-unit-simulation-architecture.md) makes units rows in native arrays, updated by Burst jobs on a fixed tick, with a spatial grid rebuilt every tick. It leaves four questions to this ADR (its follow-up 7): how units find paths, how they avoid each other, how groups keep formation, and how often the simulation ticks. Unity's job-friendly NavMesh query "has been deprecated without replacement" ([NavMeshQuery][navquery]), so ADR 0004 already decided that we build large-group pathfinding ourselves.

**What movement must do.** Pillar 3 asks that "groups travel as a group, flow around each other and around obstacles, don't pile onto one spot, and arrive in a sensible shape". Pillar 4 asks for thousands of units, with every feature judged by its per-unit cost (`docs/design/vision.md`). The owner's priority is movement that just works, at up to 2,000 units at 60 fps on the reference PC. The sandbox criteria this ADR has to make reachable (`docs/design/core-loop.md`, in short):

| Criterion | Requirement |
|---|---|
| S2.1 | Every selected unit starts moving within 0.1 s |
| S2.2 | 100 small cubes stop with no overlaps, in no more than twice their combined footprint |
| S2.3 | 50 small and 10 large cubes sent 50 cells arrive within 3 s of each other |
| S2.4 | Two seconds after arriving, no unit is still moving |
| S2.5 | 2,000 small cubes cross the map with no overlaps, at the performance target |
| S3.1 | Never through obstacles; route at most 1.25 times the shortest |
| S3.2 | An unreachable target: go to the nearest reachable point and stop, with no endless retrying |
| S3.3 | 200 small cubes pass a 3-cell gap; none stands still for more than 5 s while it has somewhere to go |
| S3.4 | Two groups of 100 pass through each other; none stands still for more than 5 s |
| S3.5 | Idle friendly units step aside without an order and stay within 2 cells of where they were |
| S6.4 | A group of 30 on attack-move stays within twice the depth it set off with |
| S4.1, S4.2 | Stop halts within 0.5 s; hold position never moves |

The sandbox test units are a small cube, half a cell wide, at 4 cells/s, and a large cube, one cell wide, at 2.5 cells/s.

**Map size.** The core loop sets map size by travel time: a medium-speed vehicle crosses from base to base in 60 to 90 seconds. Speeds aren't set yet. At 3 cells/s, between the two test cubes, that is 180 to 270 cells. So this ADR plans for a 1-against-1 map of about 256 × 256 cells (65,536) and budgets up to 512 × 512 (262,144) for four sides. These are assumptions until the units and map sections set real numbers.

**Constraints**
- ADR 0004's hot-path rules, its pass rule (average and 1% low frame time at most 16.67 ms at the 2,000 step) and its ladder.
- Packages: only Burst, Collections and Mathematics (ADR 0004, whose license question AGENTS.md §5 now covers; the install Issue is #84) and the engine's job system.
- ADR 0005 and ADR 0007: unit state and every per-unit job live in `Northwatch.Simulation`. A ScriptableObject class lives in the assembly that reads it, which copies its values into Simulation at match start.
- The engineers are AI agents, each on one Issue at a time. Each stage should be one job with fixed inputs and outputs that an EditMode test can run without a scene.

**Evidence gap.** Nobody has measured any of this in the project. The sources below give the algorithms and their properties, not costs on our hardware. Every cost figure here is an estimate from operation counts. The spike in Consequences measures them.

## Decision

**Flow fields for pathfinding, ORCA for avoidance, group pace control with arrival slots, and a 30 Hz simulation tick.**

1. A move order gets one flow field per goal cell and size class, built over the whole map by a Burst job and shared by every unit of the order. Each unit reads its direction from the field at its own position.
2. Each tick, every unit turns that direction into a preferred velocity. ORCA (optimal reciprocal collision avoidance) then picks the nearest velocity that avoids its neighbours, and a last pass removes any overlap left.
3. A group moves at its slowest member's speed. Units behind catch up and units ahead slow down, and each unit ends in its own slot of a compact block that faces the direction of travel.
4. The simulation ticks 30 times per second, and drawing interpolates between ticks (ADR 0004).

### Why

- **Pillar 3.** A flow field gives a correct direction from every reachable cell. A unit shoved aside in a crowd is still on its route and needs no new path. Chris Taylor said of Supreme Commander 2's flow fields: "if a unit loses its way, it can re-find its path without re-pathing" ([Taylor interview][taylor]). ORCA lets units anticipate each other rather than collide first, and it "provides sufficient conditions for collision-free motion" ([ORCA][orca]). Pace control and slots make groups arrive together and in shape instead of piling onto one point.
- **Pillar 4.** The path cost is per order, not per unit: "even if I have 500 units selected — I create one path. One flow field is created" ([Taylor interview][taylor]). Following a field costs a unit four cell reads per tick. ORCA reduces each unit's choice to "a low-dimensional linear program" ([ORCA][orca]) over a capped number of neighbours, so its per-unit cost has an upper bound however dense the crowd.
- **The team.** Every stage is one Burst job built from a published method: Dijkstra's shortest paths for fields, ORCA from its paper, and Reynolds' arrival and flow-following behaviours ([Reynolds][reynolds]). Each can be tested on its own.

### Pathfinding: flow fields

**Static maps.** These are allocated at match start, and a job rebuilds them only when walkability changes.
- `walkable`: one byte per cell. Every static obstacle occupies whole cells. Buildings already do (core loop, Build), and the sandbox's boxes are placed on the cell grid.
- **Size classes.** Class 0 is every unit up to one cell wide, which covers both test cubes. A cell is passable for class 0 when it is free. A wider unit type gets a class whose passable cells are those where its footprint fits, as clearance-based planning does for agents of different sizes ([HAA*][haa]). The first such unit type adds its class; the sandbox needs only class 0.
- `region`: a connected-region label per cell and class (two bytes), from a flood fill using the same moves as the fields.

**Building a field.** A field's key is (goal cell, class). The build job runs Dijkstra's algorithm from the goal over the class's passable cells, with eight neighbours, integer costs of 10 straight and 14 diagonal, and no corner cutting: a diagonal step needs both side cells passable. Its priority queue lives in preallocated memory. The fields Issue picks the queue type (a bucket queue suits the small integer costs), and the spike times it. The job works in a 32-bit scratch and writes, per cell:
- a direction byte: the angle to the neighbour with the lowest cost
- the remaining distance, as a `ushort` in tenths of a cell, saturating at 65,534, with 65,535 meaning unreachable

In open ground, a path of eight-way steps is at most cos 22.5° + (√2 − 1) × sin 22.5° ≈ 1.082 times the straight line, well inside S3.1's 1.25.

**Following a field.** A unit blends the directions of its four nearest cells by bilinear weights, skipping unreachable cells, so it turns smoothly instead of at cell edges. This is Reynolds' flow field following: the unit "steers to align its motion with the local tangent of a flow field" ([Reynolds][reynolds]).

**Direct seek, without a field.** A unit with a clear straight line to its target steers straight at it. The line is a walk over passable cells with the same corner rule, up to `directSeekRange` (12 cells by default). Direct seek is used:
- for an order where every unit has such a line, which then requests no field
- for the final approach to a slot
- while a unit waits for its field
- for idle units walking home

**Field pool and builds**
- The pool holds `fieldCapacity` fields (64 by default) at 3 bytes per cell, in two arrays allocated at match start. Each field counts the units whose current order uses it. A field with no users stays cached until it's evicted, least recently used first, so recurring goals such as rally points, Depots and Supply fields usually find their field already built.
- A miss queues a build request. Up to `buildSlots` builds (2 by default) run at once, each in its own scratch arrays. A build starts only once it has reserved a pool slot that is free or has no users. If none is available, the request waits, its units use direct seek, and the debug overlay counts the wait.
- **Builds never stall a tick.** A build is an `IJob` scheduled at the same time as the tick's chain but not part of it, and it runs on one worker while the frames go on.
  - At each tick frame's one sync point (ADR 0004 rule 5), the driver checks each build's `JobHandle.IsCompleted`, which "Returns true if the task has completed" ([IsCompleted][is-completed]).
  - It combines the finished builds' handles with the tick chain's handle, and completes them all in one call, so finished builds add no wait.
  - The next tick's first job copies them into the pool. Unfinished builds stay in flight until a later tick frame.
- Builds write their own scratch, not the pool, because the safety system "tracks what reads or writes to any NativeContainer instance" ([native containers][native-container]), and movement jobs read the pool every tick. The driver calls `JobHandle.ScheduleBatchedJobs` after scheduling, because "The job system intentionally delays job execution until you call `ScheduleBatchedJobs` manually" ([ScheduleBatchedJobs][batched]).
- **Latency.** An order applied at a tick moves its units from that tick by direct seek, and they switch to the field once it's published, normally at the next tick.

**Unreachable goals (S3.2).** When the goal cell isn't passable, or lies in a different region from a unit, that unit's goal becomes the nearest cell of the unit's own region. A ring search outward from the goal finds it, visiting at most the map's cells, once per order. Units in the same region share the corrected goal, and their slots are laid out around it. The units go there and stop. Nothing retries, because the region test is exact.

**Walkability changes,** such as a building placed or destroyed (S9), arrive through the command queue and apply at the start of a tick.
- The static-map rebuild job depends on the builds in flight, so that one tick can wait for up to one build. Walkability changes are rare.
- The results of those builds are dropped and re-queued, because their inputs changed.
- Every cached field with users is queued for a rebuild, most users first, and fields without users are dropped.
- Until the new field lands, the static clamp (below) keeps units out of the new obstacle.

**NavMesh has no role.** There is no baking, no `NavMeshAgent` and no query. `com.unity.ai.navigation` stays unused, which answers the question ADR 0004 left to this ADR.

### Local movement: ORCA

Each tick, every unit runs four stages in order:
1. **Preferred velocity.** The direction comes from the field or from direct seek, and the speed from group pace (below). Near its slot, the unit uses Reynolds' arrival, which makes it "slow down as it approaches the target" ([Reynolds][reynolds]).
2. **Avoidance (ORCA)**, written in Burst from the paper ([ORCA][orca]). No third-party code is copied.
   - Up to `maxNeighbours` (10 by default) of the nearest units within `neighbourDistance` (4 cells) each add one half-plane, with a time horizon of 1 s. Ties go to the lower unit index. Grid buckets are scanned nearest first, and once 10 units are found, the search range shrinks to the tenth one's distance.
   - Up to four blocked cells within 1.5 cells each add a static constraint from their nearest point, with a 0.5 s horizon. The unit takes full responsibility for these.
   - The authors' RVO2 library exposes the same parameters (`maxNeighbors`, `timeHorizon`, `timeHorizonObst`, [RVO2 parameters][rvo2-params]). When the linear program has no solution, the paper's fallback picks the velocity that violates the constraints least.
   - All the defaults are starting values, which the spike and the S3 tests tune.
3. **Integration.** The unit's velocity moves toward the chosen one within its acceleration limit, and the unit moves. A static clamp then pushes its disc out of any blocked cell in the 3 × 3 cells around it (S3.1).
4. **Overlap pass.** Each unit moves itself out of any overlap with its neighbours that is still left, by its share of each overlap (below). The move is capped at half its radius per tick, and the clamp runs again afterwards. The query radius adds twice the largest step, because the grid was built before integration. A unit with no overlap doesn't move.

A unit's collision shape is a disc with a radius of half its width. ORCA's guarantee assumes velocity can change instantly. Acceleration limits and dense crowds can still leave small overlaps, and stage 4 removes them.

**Who gives way.** ORCA splits the responsibility for each pair of units. We set the shares by state. A unit is *moving* when it has a move order, and *idle* when it has none, even while it walks home. For a pair of units, the first rule below that matches, with either unit taking the role of A, decides:
1. **A is on hold position (S4.2):** A takes 0 and B takes 1. If B is also on hold position, neither moves.
2. **A is idle, B is moving, and either B is an enemy or A is already at least 1.75 cells from its home:** A takes 0 and B goes round it (1). Enemies don't make way.
3. **A is idle and B is a moving unit of its own side:** A steps aside (1), and B takes 0.
4. **Otherwise** (both moving, or both idle): 0.5 each, which is ORCA's reciprocal split.

Both units compute their shares from the same snapshot, so the shares add up to 1 for every pair except two units on hold position. The overlap pass uses the same shares.

**Idle units make way (S3.5).**
- When a unit stops, it records its home position.
- Moving units of its own side push it aside through rule 3.
- From 1.75 cells away from home, rule 2 applies instead: it stops giving way and movers go round it.
- No stage moves an idle unit more than 2 cells from its home. When a push would, the other unit of the pair resolves the overlap.
- Once no moving own-side unit is within 2 cells, it walks home by direct seek at half speed.
- If a stationary unit blocks its home for 1 s, its current position becomes its home.

**No stalemates (S3.3, S3.4).** When a moving unit has stayed below 20% of its target speed for 1 s, and a neighbour ahead of it is moving toward it, it turns its preferred direction 15° to its right for each further second, up to 60°. The turn resets once it moves freely again. Every unit turns the same way, so two units blocking each other head-on both turn right and pass. Units queueing behind others that move the same way don't turn, so a queue at a gap doesn't jostle sideways. This rule is a judgment call, tuned by the S3 tests.

### Groups and formations

A **group** is the set of units given one order together. A group table, with one row per group and a capacity equal to the unit capacity, holds each group's goal, forward direction, field per class, speed and pace sums. A unit given a new order leaves its old group.

**Pace (S2.3, S6.4).** For each moving unit i, `r_i` is the field's remaining distance at its position, plus its slot's offset from the goal along the forward direction. So `r_i` is how far the unit is from its own slot along the route. Each tick, a job sums `r_i` for each group in fixed-point integers, so the mean doesn't depend on thread order. Then:

```text
target speed_i = clamp(v_group + k_pace × (r_i − mean r), minPace × v_group, v_max_i)
```

Here `v_group` is the group's slowest top speed, `k_pace` is 0.5 per second by default, and `minPace` is 0.3. Units behind speed up and units ahead slow down. Units bound for front slots travel ahead by their slot's offset, so the group travels at about its arrival depth, and every unit reaches its slot at about the same time. A one-unit group moves at its own top speed.

**Slots.** When an order is applied, a job lays out slots around the goal (after any correction for reachability):
- Forward is the direction from the group's centroid to the goal.
- For N units of a size class, the class forms a block with `ceil(sqrt(2N))` columns, so the block is about twice as wide as it is deep. The pitch is `slotSpacing` × the class's width.
  - The default `slotSpacing` is 1.25. The 100 small cubes of S2.2 then fill 15 × 7 slots at 0.625 cells, about 1.65 times their combined footprint.
  - `slotSpacing` must stay at or below 1.4, which keeps the area per slot at or under about twice a unit's footprint.
- Blocks stack front to back. The class whose members are furthest forward when the order is given goes in front.
- A slot is valid when its cell is passable for the class, is in the goal's region, has a clear line from the goal, and holds no stationary unit from outside the order. Each invalid slot is replaced by the next unused cell centre in flood-fill order from the goal.
- **Assignment keeps relative positions.** Units sorted by forward position fill the rows front to back, and within each row they are sorted by sideways position. Slots are ordered the same way and paired in order, so paths rarely cross. Sorting uses `NativeSortExtension` from Collections ([NativeSortExtension][nativesort]).

**Arrival and stillness (S2.2, S2.4)**
- Within `arrivalRange` (6 cells) of its slot, with a clear line to it, a unit seeks the slot directly.
- Within 0.05 cells and nearly still, it stops, faces the group's forward direction and becomes idle, with the slot as its home.
- A unit within 1.5 pitches of its slot that gains less than 0.1 cells on it in 1 s settles where it is.
- An idle unit's preferred velocity is zero, and ORCA keeps a velocity that is already safe. So an arrived group stays still unless something pushes it.
- Square units at rest are aligned to their slot grid, so their drawn squares don't overlap.

**Other orders,** which the S4 and S5 Issues refine within this ADR:
- **Stop (S4.1).** Deceleration must be at least twice the top speed per second, so a unit at full speed halts within 0.5 s.
- **Waypoints (S4.3).** Each waypoint is a leg of the same group. The next leg's field is requested when the group has a quarter of the current leg left. Slots are laid out only at the last waypoint.
- **Moving targets (S5 attack-target).** Direct seek within range and line of sight. Otherwise a field to the target's cell, rebuilt once the target has moved 4 cells, at most once per second per group.

### Tick rate: 30 Hz

The simulation runs at a fixed 30 ticks per second (dt = 1/30 s). It uses the accumulator method of [Fix Your Timestep][fiedler], and drawing interpolates between the last two ticks, as ADR 0004 sets. At most two ticks run in one frame. Beyond that, the game slows down rather than falling into the spiral of death, where "being behind causes your update to simulate more steps to catch up" ([Fix Your Timestep][fiedler]). When a frame runs two ticks, the driver chains both and completes them once. The ladder log records 30 Hz.

Why 30:
- **Step size.** The fastest test unit moves 4/30 ≈ 0.13 cells per tick, about half the small cube's radius of 0.25. So units can't skip past each other, or through a one-cell obstacle, within a tick. At 20 Hz the step would be 0.2 cells, close to the radius. Rule: a unit type's top speed is at most 30 × its radius, which is 7.5 cells/s for the small cube. A faster type needs a new ADR (sub-steps or a higher rate).
- **Response (S2.1).** An order is applied at the next tick, and every unit has a velocity from that tick. The worst case from click to the first drawn frame of movement is one frame to read input, one tick and one frame: about 67 ms at 30 Hz and 60 fps (83 ms at 20 Hz).
- **Cost.** 30 Hz costs half the CPU of 60 Hz. At 60 fps, ticks fall on every other frame, so ADR 0004's 1% low sees the per-tick cost (budget below).
- **Practice.** Blizzard's StarCraft II API says its real-time mode "Uses the “faster” game speed (22.4 gameloops per second)" ([s2client-proto][s2proto]). An RTS simulation that ticks slower than the frame rate is established practice.

### How it maps onto ADR 0004

The tick's chain, in order. "Parallel" means `IJobFor.ScheduleParallel`, and "single" means `IJob` or `IJobFor.Schedule`. The job names are examples.

| Job | Kind | Reads | Writes |
|---|---|---|---|
| `PublishFieldsJob` | single | completed build scratch | field pool |
| `RebuildStaticMapsJob`, only after a walkability change | single | `walkable` | class passability, regions |
| `ApplyOrdersJob` | single | command queue, regions, last tick's grid | unit order state, group table, slots, field requests |
| `BuildSpatialGridJob` | parallel bucket pass, then a single counting sort | positions | grid |
| `GroupPaceJob` | single | fields, units | group sums |
| `PreferredVelocityJob` | parallel | fields, groups, slots, static maps | preferred velocity |
| `AvoidanceJob` | parallel | grid, positions, velocities, static maps | new velocity |
| `IntegrateJob` | parallel | new velocity, unit types | position, previous position, velocity, state |
| `ResolveOverlapJob` | parallel | grid, positions | corrected position |
| `BuildFlowFieldJob`, up to 2 | single each, outside the chain | static maps | its own scratch |

Unit AI and targeting (a later ADR) slot in between the grid and preferred velocity.

**The spatial grid** is a counting sort, rebuilt every tick:
- Buckets are 2 × 2 cells, with a start and a count per bucket.
- Unit indices are sorted by bucket, and positions and velocities are copied in the same order, so neighbour scans read memory in sequence.
- The sort runs single-threaded in index order, so the order within a bucket is fixed.
- ORCA, the overlap pass, slot validation and home return query it. ADR 0004's targeting and selection share it. It has no hash maps and nothing that grows.

**Allocated once, at match start, with `Allocator.Persistent`.** Sizes are for 2,000 units on a 256² map, with 512² in brackets:

| Data | Size |
|---|---|
| Movement state per unit: position, previous position, velocity, preferred and new velocity, home, slot, heading, group, field, state and timers | about 80 B per unit, 160 KB |
| Group table | about 0.1 MB |
| Spatial grid | about 0.2 MB (0.6 MB) |
| Static maps for class 0: walkable, passability, region | 0.25 MB (1 MB) |
| Field pool: 64 fields × cells × 3 B | 12 MB (48 MB) |
| Build scratch for 2 slots, sized from the cell count; the fields Issue tests the bound | about 3 MB (10 MB) |
| Slot and sort scratch | about 0.1 MB |

Nothing is allocated per tick or per order.

**Determinism.** Every per-unit job reads the state from the start of its stage and writes only its own unit's row. This matters because for `IJobFor`, "The indices have no guaranteed order" ([IJobFor][ijobfor]). Group sums use integers, and neighbour ties go by index. So a tick's result doesn't depend on the number of workers, and the same seed, inputs and build on the same machine replay the same way, which EditMode tests and ladder runs rely on. Burst keeps its default float mode. Its Deterministic mode targets consistency across platforms and can cost floating-point performance ([BurstCompile][burst-compile]), and a single-player game doesn't need it.

### Cost at 2,000 units

These are estimates for one tick with 2,000 moving units, from operation counts, not measurements:

| Stage | Cost per tick |
|---|---|
| Grid, group pace, preferred velocity, integration | about 0.3–0.5 ms of worker time |
| ORCA: about 150 grid candidates and 10 constraints per unit in a dense crowd, about 1–3 µs per unit | about 2–6 ms of worker time |
| Overlap pass | about 0.3–1 ms of worker time |
| **Total** | **about 3–8 ms of worker time, so about 1–2 ms wall time**, assuming about five cores' worth of parallel speed-up on a 6-core CPU |
| Main thread: drain commands, schedule, complete | at most 0.3 ms, flat in the unit count |
| One field build, outside the chain, on one worker | about 1–6 ms at 256², 4–25 ms at 512² |
| An order for 2,000 units (sort and slots), only on that tick | about 0.2 ms |

Per unit, that is about 1.5–4 µs of worker time per tick and about 80 bytes. Movement adds no GPU cost.

**Against the pass rule.** At 60 fps, movement adds about 1–2 ms to every other frame and nothing to the rest. That leaves most of the 16.67 ms for drawing and for the systems still to come.

**Budget.** At 2,000 units on the reference PC, the movement chain takes at most 3 ms of wall time and 0.5 ms of main-thread time on tick frames. Both are read from the Profiler's timeline in a development build. A PR over budget either fixes it or states the cost and why, as ADR 0004's regression rule asks. The budget is a judgment, and the spike checks it.

### Rules for hot paths

These add to ADR 0004's rules.

1. **Fields, not paths.** A unit holds a field index, never a path. A field belongs to a (goal cell, class) pair, never to a unit.
2. **Own row only.** Per-unit movement jobs read the state from the start of their stage and write only their own unit's row. No float atomics, and no writes to another unit's row. Per-group sums use integers.
3. **Neighbours through the grid,** capped at `maxNeighbours`, with ties broken by unit index.
4. **Builds stay in their scratch.** At most `buildSlots` builds run at once, and only the publish job writes the pool.
5. **Walkability changes only at the start of a tick,** through the command queue.
6. **One settings asset.** Every movement tunable lives in one movement settings ScriptableObject in `Data/`. Its class lives in `Northwatch.Presentation`, which copies it into a plain struct in Simulation at match start (ADR 0005 rule 5).
   - At match start, every unit type is checked: top speed at most 30 × its radius, deceleration at least twice its top speed per second, and `slotSpacing` at most 1.4.
   - A failed check logs an error and the match doesn't start.
7. **No NavMesh,** physics queries or colliders for movement.
8. **Where the code goes.** Code lives in `Code/Simulation/Movement/` (the tick stages, ORCA, groups and slots), `Code/Simulation/FlowFields/` (static maps, the pool and builds) and `Code/Simulation/Spatial/` (the grid), in the matching `Northwatch.Simulation.*` namespaces (ADR 0005, ADR 0007). Tests mirror these folders.

### Packages

None are added. Burst, Collections and Mathematics (ADR 0004) and the engine's job system are enough. RVO2, the authors' ORCA library, is "released under the Apache License, Version 2.0" ([RVO2][rvo2]). We don't use it or copy from it. It is C++, and porting its code would bring in a new dependency, which needs the owner's approval and its own Critical Issue (AGENTS.md §5). We write ORCA from the paper.

## Alternatives considered

### Pathfinding

| | Whole-map flow fields (chosen) | A* per unit | A* per group | Hierarchical (HPA*, flow-field tiles) |
|---|---|---|---|---|
| Work per order | One field per goal and class | One search per unit | One search | One coarse search, then local work |
| Work per unit per tick | Four cell reads | Follow a waypoint list | Follow an offset from the group's path | As in A* or flow fields |
| A unit pushed off its route | Still has a direction | Needs a new path | Needs a new path or must steer back | Needs a new path, or a field for its sector |
| Memory | A pool of fields, which grows with map area | A path per unit | A path per group | Small and bounded |
| Complexity for the team | Low | Low | Medium | High |

- **A\* per unit** ([A*][astar]). Simple, and optimal on a grid. Jump point search can "speed up A* by an order of magnitude and more" on uniform-cost grids ([JPS][jps]). But S2.5 sends 2,000 units at once, which means either 2,000 searches in one tick or units that start late (S2.1). A unit shoved out of its lane in a crowd must re-path. Taylor described A* at high unit counts: "everyone of those has to have a separate path find operation", and re-pathing whenever units hit a problem "just flattens the CPU" ([Taylor interview][taylor]). Not chosen.
- **A\* per group.** One search per order is cheap. But a block 15 units wide can't follow a path one unit wide through a gap, so units at the edges cut corners, and shoved units need their own repair paths, which brings back per-unit A*. Not chosen.
- **Hierarchical methods.** HPA* is "up to 10 times faster" than a highly optimised A*, with paths "within 1% of optimal" after smoothing ([HPA*][hpa]). Harabor and Grastien note that hierarchical methods are fast but "usually return suboptimal paths" ([JPS][jps]). The flow-field version splits the map into sectors and builds fields only for the sectors along a coarse route ([Emerson][emerson]). That cuts build cost and bounds memory on large maps. The price is a sector graph to maintain, fields to stitch at sector edges, and extra sectors on demand for displaced units. At 256², a whole-map field is affordable (estimates above), so we start with the simpler method. Units read fields only through one sampling function, so the tiled version can later replace the storage without touching the movement jobs (see the triggers in Consequences).
- **Continuum crowds** ([Treuille et al.][continuum]) fold avoidance into the field itself: "a dynamic potential field simultaneously integrates global navigation with moving obstacles such as other people". The field changes as the crowd moves, so with tens of groups that means a whole-map solve per group on every tick. Not chosen.
- **Unity NavMesh.** `NavMeshAgent` breaks ADR 0004 rule 1, and the job-friendly query is deprecated ([NavMeshQuery][navquery]). Not chosen.

### Local avoidance

- **Separation steering alone** ([Reynolds][reynolds]), plus the overlap pass. It's the cheapest and simplest, at a few operations per neighbour. But it reacts to contact instead of anticipating it, so two groups crossing head-on (S3.4) push into a jam. Blended steering forces can also "cancel each other out at inopportune times" ([Reynolds][reynolds]). Not chosen; the overlap pass stays as the safety net.
- **ORCA (chosen).** It anticipates collisions over a time horizon. The authors report simulations "involving thousands of agents", with collision-free actions computed "in only a few milliseconds" on their hardware ([ORCA][orca]). It costs more per unit (a small linear program) and needs tuning in dense crowds. It assumes every agent shares the work of avoiding, which our share rules make explicit. Background for the implementer: [Sunshine-Hill][sunshine-hill].
- **Anticipatory force models,** for example the time-to-collision power law of [Karamouzas et al.][powerlaw]. They also anticipate, and cost less than a linear program. But they integrate forces, so at a 30 Hz tick the strong forces close to a collision would need clamping and tuning that we have no evidence for. Not chosen. This is the first alternative if ORCA fails the S3 tests (Consequences).

### Groups

- **Each unit at its own speed, with no slots.** The simplest, but a mixed group strings out: over 50 cells, small cubes arrive 7.5 s before large ones, so S2.3 fails. Everyone also piles onto the goal point, so S2.2 fails.
- **A rigid formation behind a virtual leader.** Keeps its shape on the way, but breaks in gaps and around obstacles, and needs rules for re-forming. Pillar 3 doesn't ask for "a perfectly predictable path for every single unit".
- **Pace control with arrival slots (chosen).** Keeps groups together and arrival tidy, for one sum per group per tick and one sort per order.

### Tick rate

- **20 Hz.** The cheapest, but the small cube's 0.2-cell step is close to its 0.25 radius, and fast unit types would be limited to 20 × their radius.
- **60 Hz.** The smallest steps and lag, at twice the movement CPU, for no criterion that 30 Hz misses.

## Consequences

- **Benefits.**
  - One field serves a whole order, so moving 2,000 units costs about as much pathfinding as moving one.
  - Shoved units never lose their route.
  - Crossing groups anticipate each other, and idle units make way and come back.
  - Groups arrive together and in shape.
  - Each stage is one job with fixed inputs and outputs, testable on its own.
- **Tradeoffs.**
  - Field memory grows with map area, and each build costs a few milliseconds of one worker.
  - ORCA needs tuning in dense crowds.
  - A group moves at its slowest member's speed. S2.3 asks for this. If the designer later wants fast units to go ahead on some orders, that is a new order type, not a change to this ADR.
  - Static obstacles must occupy whole cells.
- **Risks.**
  - Every cost figure is an estimate until the spike.
  - ORCA may still stall at the 3-cell gap (S3.3) despite the right-turn rule. The response is to tune within the parameters. If S3.3 or S3.4 still fails after two review rounds, the Producer brings it back to the architect.
  - S2.2 and S2.5 say "overlapping" for cubes. The collision shape here is a disc of half the width. Units at rest are aligned, so their squares don't overlap, but moving squares can touch at the corners. QA should measure overlap on the discs.
  - The core loop's Fight section says units react "on the next visible frame". With ADR 0004's interpolation, units start moving within about four frames at 60 fps (67 ms or less). The order marker can still appear on the next frame, and S2.1's 0.1 s is met.
  - Maps for four sides larger than 512² would need the tiled method.
- **Reversibility.**
  - Tiles instead of whole-map fields change only the storage behind one sampling function.
  - Another avoidance method replaces only `AvoidanceJob`, whose input (preferred velocity) and output (new velocity) stay the same.
  - The tick rate is one constant, but tuning values in seconds and the unit-type check depend on it. A change needs a superseding ADR and starts a new ladder series.
- **Triggers for a superseding ADR.**
  - Field builds take more than 20 ms at the 95th percentile on the largest planned map, or the pool needs more than 64 MB: adopt flow-field tiles.
  - The movement chain stays over budget after the levers below, or ORCA can't pass S3.3 or S3.4: revisit avoidance.
  - A unit type is faster than 30 × its radius.
- **Levers within this ADR.** These are tuning and need no new ADR:
  - fewer neighbours, down to 6
  - no ORCA for units with no neighbour within `neighbourDistance`
  - no movement stages for idle units with no mover nearby
  - a different bucket size for the grid
- **ADR 0004.** This ADR is ADR 0004's follow-up 7 and supersedes nothing. It sets the tick rate (30 Hz) and the grid's layout, and adds the rules above to ADR 0004's hot-path rules. ADR 0004's one sync point still holds: a field build is completed only at the sync point, and only once it has finished.
- **Dependencies and owner decisions.** No package, no Unity change and no handbook change. No owner decision is needed. `com.unity.ai.navigation` stays in the manifest unused. Removing it would need its own ADR (AGENTS.md §4).
- **Follow-up Issues,** in build order, after #84 (packages) and ADR 0004's sandbox and ladder harness (its follow-ups 5 and 6):
  1. **Movement spike** (`type:spike`, gameplay-programmer, two days). It can be folded into ADR 0004's follow-up 4 spike if that hasn't started. On the reference PC, it measures:
     - field build time (median and 95th percentile) at 128², 256², 384² and 512², on open ground and with 20% of cells blocked, plus the pool and scratch memory at each size
     - the movement chain's wall time, per job and in total, and its main-thread time, at 1,000 and 2,000 units, against the 3 ms and 0.5 ms budget. It runs in the ladder scenario (two blocks crossing head-on) and with 200 units through a 3-cell gap.
     - frame times on tick and non-tick frames, with the ladder method, at 1,000 and 2,000 units
     - the time from an order to every unit moving (S2.1)
     - overlapping pairs per tick, and units stalled for more than 5 s with somewhere to go, in both scenes
     - 0 B GC Alloc after warm-up

     Its finding says which trigger or lever above applies. If the time box runs out first, the finding reports what was measured, and the rest is measured in the ladder entries of Issues 3 to 5.
  2. **Static maps and flow fields** (the pathing half of S3.1 and S3.2): `walkable` from cell-aligned boxes, class 0 passability, regions, the build job, the pool, the request queue and publishing, direct seek and goal correction.
     - EditMode tests: distances on open ground, a detour round a box within 1.25 times the shortest route, and a goal inside a ring of boxes.
     - The debug overlay draws field directions and regions.
  3. **The 30 Hz movement tick** (S2.1, S3.1): movement state, the grid, preferred velocity, integration with the static clamp, the overlap pass and interpolation. Orders move units, without slots yet. With two gameplay-programmers, Issues 2 and 3 can run in parallel once they agree the sampling function's signature.
  4. **Groups and arrival** (S2.2, S2.3, S2.4): the group table, slots and their assignment, pace, arrival, settling and facing. The overlay draws slots.
  5. **ORCA and making way** (S3.3, S3.4, S3.5, S2.5): the avoidance job, the share rules, idle units walking home, and the right-turn rule. Its ladder entry shows S2.5.
  6. **Suggested:** add rules 1 to 8 to the gameplay-programmer and code-reviewer role files, together with ADR 0004's follow-up 11. These are protected paths, so that Issue must name them.
- **Later Issues.** Issues 3 to 5 each include their ladder entry (ADR 0004). Later Issues use these rules without new movement work: S4 (stop, hold, waypoints), S5 (moving targets), the S6.4 test, S9 (walkability changes) and the units section (wider size classes).

[navquery]: https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Experimental.AI.NavMeshQuery.html
[ijobfor]: https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Unity.Jobs.IJobFor.html
[is-completed]: https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Unity.Jobs.JobHandle.IsCompleted.html
[batched]: https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Unity.Jobs.JobHandle.ScheduleBatchedJobs.html
[native-container]: https://docs.unity3d.com/6000.3/Documentation/Manual/job-system-native-container.html
[nativesort]: https://docs.unity3d.com/Packages/com.unity.collections@2.6/api/Unity.Collections.NativeSortExtension.html
[burst-compile]: https://docs.unity3d.com/Packages/com.unity.burst@1.8/manual/compilation-burstcompile.html
[taylor]: https://www.engadget.com/2009-06-09-interview-chris-taylor-on-supreme-commander-2-part-two.html
[emerson]: http://www.gameaipro.com/GameAIPro/GameAIPro_Chapter23_Crowd_Pathfinding_and_Steering_Using_Flow_Field_Tiles.pdf
[astar]: https://ieeexplore.ieee.org/document/4082128
[jps]: https://ojs.aaai.org/index.php/AAAI/article/view/7994
[hpa]: https://papersdb.cs.ualberta.ca/~papersdb/view_publication.php?pub_id=585
[haa]: https://doi.org/10.1109/CIG.2008.5035648
[continuum]: https://grail.cs.washington.edu/projects/continuum-crowds
[orca]: https://gamma.cs.unc.edu/ORCA/
[rvo2]: https://gamma.cs.unc.edu/RVO2/
[rvo2-params]: https://gamma.cs.unc.edu/RVO2/documentation/2.0/params.html
[sunshine-hill]: http://www.gameaipro.com/GameAIPro3/GameAIPro3_Chapter19_RVO_and_ORCA_How_They_Really_Work.pdf
[powerlaw]: https://link.aps.org/doi/10.1103/PhysRevLett.113.238701
[reynolds]: https://www.red3d.com/cwr/steer/gdc99/
[fiedler]: https://gafferongames.com/post/fix_your_timestep/
[s2proto]: https://github.com/Blizzard/s2client-proto/blob/master/docs/protocol.md
