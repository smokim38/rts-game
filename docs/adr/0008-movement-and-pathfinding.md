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
| Fight | Every order gets "a marker at the target and the units reacting on the next visible frame" |

The sandbox test units are a small cube, half a cell wide, at 4 cells/s, and a large cube, one cell wide, at 2.5 cells/s. S2.2 and S2.5 are about these cubes, so here *overlap* means two drawn squares, seen from above, sharing area. S2.2's area is checked as the smallest rectangle aligned with the group's forward direction that holds every square. The squares' convex hull is never larger, so this is the stricter reading.

**Map size.** The core loop sets map size by travel time: a medium-speed vehicle crosses from base to base in 60 to 90 seconds. Speeds aren't set yet. At 3 cells/s, between the two test cubes, that is 180 to 270 cells. So this ADR plans for a 1-against-1 map of about 256 × 256 cells (65,536) and budgets up to 512 × 512 (262,144) for four sides. These are assumptions until the units and map sections set real numbers.

**Constraints**
- ADR 0004's hot-path rules, its pass rule (average and 1% low frame time at most 16.67 ms at the 2,000 step) and its ladder.
- Packages: only Burst, Collections and Mathematics (ADR 0004, whose license question AGENTS.md §5 now covers; the install Issue is #84) and the engine's job system.
- ADR 0005 and ADR 0007: unit state and every per-unit job live in `Northwatch.Simulation`. A ScriptableObject class lives in the assembly that reads it, which copies its values into Simulation at match start.
- The engineers are AI agents, each on one Issue at a time. Each stage should be one job with fixed inputs and outputs that an EditMode test can run without a scene.

**Evidence gap.** Nobody has measured any of this in the project. The sources below give the algorithms and their properties, not costs on our hardware. Every cost figure here is an estimate from operation counts. The spike in Consequences measures them.

## Decision

**Flow fields for pathfinding, ORCA for avoidance, group pace control with arrival slots, and a 30 Hz simulation tick.**

1. A move order gets one flow field per goal cell and size class, built over the whole map by a Burst job, published on a fixed tick and shared by every unit of the order. Each unit reads its direction from the field at its own position.
2. Each tick, every unit turns that direction into a preferred velocity. ORCA (optimal reciprocal collision avoidance) then picks the nearest velocity that avoids its neighbours, a pass removes any overlap left, and a last check rejects any move that would make two drawn units overlap.
3. A group moves at its slowest member's speed. Units behind catch up and units ahead slow down, and each unit ends in its own slot of a compact block, one block per unit width, facing the direction of travel.
4. The simulation ticks 30 times per second, and drawing interpolates between ticks (ADR 0004).

### Why

- **Pillar 3.** A flow field gives a correct direction from every reachable cell. A unit shoved aside in a crowd is still on its route and needs no new path. Chris Taylor said of Supreme Commander 2's flow fields: "if a unit loses its way, it can re-find its path without re-pathing" ([Taylor interview][taylor]). ORCA lets units anticipate each other rather than collide first, and it "provides sufficient conditions for collision-free motion" ([ORCA][orca]). Pace control and slots make groups arrive together and in shape instead of piling onto one point.
- **Pillar 4.** The path cost is per order, not per unit: "even if I have 500 units selected — I create one path. One flow field is created" ([Taylor interview][taylor]). Following a field costs a unit four cell reads per tick. ORCA reduces each unit's choice to "a low-dimensional linear program" ([ORCA][orca]) over a capped number of neighbours, so its per-unit cost has an upper bound however dense the crowd.
- **The team.** Every stage is one Burst job built from a published method: Dijkstra's shortest paths for fields, ORCA from its paper, and Reynolds' arrival and flow-following behaviours ([Reynolds][reynolds]). Each can be tested on its own.

### Pathfinding: flow fields

**Static maps.** These are allocated at match start, and a job rebuilds them only when walkability changes.
- `walkable`: one byte per cell. Every static obstacle occupies whole cells. Buildings already do (core loop, Build), and the sandbox's boxes are placed on the cell grid.
- **Size classes** serve pathfinding only; formation spacing comes from unit widths (Slots). A unit's *disc* is the circle round its drawn square (Contact shapes). Class k holds the unit types whose disc is at most k + 1 cells across, and its passable positions are those where a free square of k + 1 by k + 1 cells fits, as clearance-based planning does for agents of different sizes ([HAA*][haa]).
  - The small cube's disc is 0.71 cells across, so it is class 0, where a cell is passable when it is free.
  - The large cube's disc is 1.41 cells across, so it is class 1 and needs gaps two cells wide. A one-cell-wide square fits a one-cell gap only dead straight, with no room to spare.
  - The sandbox needs classes 0 and 1.
- `region`: a connected-region label per cell and class (two bytes), from a flood fill using the same moves as the fields.

**Building a field.** A field's key is (goal cell, class). The build job runs Dijkstra's algorithm from the goal over the class's passable cells, with eight neighbours, integer costs of 10 straight and 14 diagonal, and no corner cutting: a diagonal step needs both side cells passable. Its priority queue lives in preallocated memory. The fields Issue picks the queue type (a bucket queue suits the small integer costs), and the spike times it. The job works in a 32-bit scratch and writes, per cell:
- a direction byte: the angle to the neighbour with the lowest cost
- the remaining distance, as a `ushort` in tenths of a cell, saturating at 65,534, with 65,535 meaning unreachable

In open ground, a path of eight-way steps is at most cos 22.5° + (√2 − 1) × sin 22.5° ≈ 1.082 times the straight line, well inside S3.1's 1.25.

**Following a field.** A unit blends the directions of its four nearest cells by bilinear weights, skipping unreachable cells, so it turns smoothly instead of at cell edges. This is Reynolds' flow field following: the unit "steers to align its motion with the local tangent of a flow field" ([Reynolds][reynolds]).

**Direct seek.** A unit steers straight at its target. With a clear straight line (a walk over passable cells with the same corner rule, up to `directSeekRange`, 12 cells by default), that is a full route, used:
- for an order where every unit has such a line, which then requests no field
- for the final approach to a slot, and for idle units walking home

Without a clear line, direct seek is only a stopgap while a unit's field is on its way (below). Obstacles can hold the unit up but never let it through: ORCA's static constraints, the static clamp and the move check (Local movement) keep it out of blocked cells.

**Field pool and requests**
- The pool holds `fieldCapacity` fields (64 by default) at 3 bytes per cell, in two arrays allocated at match start. Each field counts its users, the units whose current order uses it. A field with no users stays cached until it's replaced, least recently used first, so recurring goals such as rally points, Depots and Supply fields usually find their field already built.
- A miss queues a request. The queue is first in, first out, by tick and then command order, with one row per group and class, so it can't overflow. Up to `buildSlots` builds (2 by default) run at once, each in its own scratch arrays.
- When a build slot is free, the head request takes a pool slot: a free one, else the least recently used field with no users, else (saturation) a field with users whose lease has expired, fewest users first, then oldest lease, then lowest slot index. A field picked for replacement takes no new users.
- **Leases.** A field is leased for at least `minLease` (3 s by default) from when it is published. When a field with users is replaced, every group using it switches to direct seek on the tick the new field is published, and requests its field again at the back of the queue. So under saturation, groups take turns: each waits at most about `minLease` × (waiting groups ÷ `fieldCapacity` + 1), moving by direct seek meanwhile. Saturation needs more than 64 different (goal cell, class) pairs in use by moving units at once. The debug overlay counts waits and replacements.
- **Fixed publish tick.** A build that starts with tick t is published by the first job of tick t + `buildLatencyTicks` (1 by default), `PublishFieldsJob`, which is scheduled with the build's handle as a dependency: "The job system won’t run the dependent job until the job it depends upon is finished" ([job dependencies][job-deps]). So when a field becomes available depends only on tick numbers, never on worker timing or load. A build that overruns delays that tick's chain at the sync point. The spike sets `buildLatencyTicks` so that under 1% of builds overrun on the reference PC on the largest planned map, and the overlay counts overruns.
- Each build runs on one worker, outside the tick's chain, while frames go on. It writes its own scratch, not the pool, because the safety system "tracks what reads or writes to any NativeContainer instance" ([native containers][native-container]), and movement jobs read the pool every tick. The driver calls `JobHandle.ScheduleBatchedJobs` after scheduling, because "The job system intentionally delays job execution until you call `ScheduleBatchedJobs` manually" ([ScheduleBatchedJobs][batched]).
- **Latency (S2.1).** Units move from the tick their order is applied, by direct seek, whether or not a field is ready, and switch to the field once it's published, normally one tick later.

**Unreachable goals (S3.2).** When the goal cell isn't passable, or lies in a different region from a unit, that unit's goal becomes the nearest cell of the unit's own region. A ring search outward from the goal finds it, visiting at most the map's cells, once per order. Units in the same region share the corrected goal, and their slots are laid out around it. The units go there and stop. Nothing retries, because the region test is exact.

**Walkability changes,** such as a building placed or destroyed (S9), arrive through the command queue and apply at the start of a tick.
- The static-map rebuild job depends on the builds in flight, so that one tick can wait for up to one build. Walkability changes are rare.
- The results of those builds are dropped and re-queued, because their inputs changed.
- Every cached field with users is queued for a rebuild, most users first, and fields without users are dropped.
- Until the new field lands, the static clamp and the move check (below) keep units out of the new obstacle.

**NavMesh has no role.** There is no baking, no `NavMeshAgent` and no query. `com.unity.ai.navigation` stays unused, which answers the question ADR 0004 left to this ADR.

### Local movement: ORCA

**Contact shapes (S2.2, S2.5, S3.1).** A unit has two shapes:
- its **footprint**: the square it is drawn as, seen from above, at its heading. Overlap in the GDD's sense is between footprints, and QA measures it on them, at each tick's poses (Risks).
- its **disc**: centred on it, with a radius of half the footprint's diagonal, 0.354 cells for the small cube and 0.707 for the large one. The footprint lies inside the disc at every heading, so two units whose discs don't overlap can't overlap, however they are turned.

Each pair of units has one contact rule, which ORCA and the overlap pass both use:
- **Formation pairs** are two units of the same group that both face the group's forward direction exactly and are each seeking or holding their slot (Groups). Their footprints are aligned, so the exact test is simple: they are apart when they are apart along one of the group's two axes. ORCA gives the pair a combined radius of their two half-widths, and the overlap pass separates them along the axis where they overlap least. This lets slots sit closer than discs allow. When a new order ends a formation, its units' discs overlap a little, and the first ticks spread them to disc spacing.
- **Every other pair** uses the two discs.
- **Static obstacles** always meet the disc: in the clamp, in passability and slot checks, and in the move check.

Each tick, every unit runs five stages in order:
1. **Preferred velocity.** The direction comes from the field or from direct seek, and the speed from group pace (below). Near its slot, the unit uses Reynolds' arrival, which makes it "slow down as it approaches the target" ([Reynolds][reynolds]).
2. **Avoidance (ORCA)**, written in Burst from the paper ([ORCA][orca]). No third-party code is copied.
   - Up to `maxNeighbours` (10 by default) of the nearest units within `neighbourDistance` (4 cells) each add one half-plane, with a time horizon of 1 s. Ties go to the lower unit index. Grid buckets are scanned nearest first, and once 10 units are found, the search range shrinks to the tenth one's distance.
   - Up to four blocked cells within 1.5 cells each add a static constraint from their nearest point, with a 0.5 s horizon. The unit takes full responsibility for these.
   - The authors' RVO2 library exposes the same parameters (`maxNeighbors`, `timeHorizon`, `timeHorizonObst`, [RVO2 parameters][rvo2-params]). When the linear program has no solution, the paper's fallback picks the velocity that violates the constraints least.
   - All the defaults are starting values, which the spike and the S3 tests tune.
3. **Integration.** The unit's velocity moves toward the chosen one within its acceleration limit, and the unit moves and turns toward its velocity. While it seeks its slot directly (Groups) it turns to the group's forward direction instead, and an idle unit keeps its heading. A static clamp then pushes its disc out of any blocked cell in the 3 × 3 cells around it (S3.1).
4. **Overlap pass.** Each unit moves itself out of any contact still left under its pair rules, by its share of each overlap (below). The move is capped at half its disc's radius per tick, and the clamp runs again afterwards. The query radius adds twice the largest step, because the grid was built before integration. A unit with no overlap doesn't move.
5. **Move check.** A unit's new pose (position and heading) is accepted only if its footprint overlaps no other unit's footprint more than it did at the start of the tick, and its disc reaches no further into a blocked cell.
   - Footprints are tested exactly, with the separating axis test that [OBBTree][obbtree] uses for oriented boxes ("tests for overlaps between oriented bounding boxes based on a separating axis theorem"). Two squares need four axes.
   - It tests every unit whose footprint could touch, not a capped few. Footprints don't overlap, so that is at most about 20 in the densest packing.
   - A unit that fails keeps its new position with its old heading. If that still fails, it goes back to its start-of-tick pose with zero velocity.
   - Units next to a fallback are checked again, in rounds, until none falls back. Each round decides from the poses at its start, so the result doesn't depend on order. Each unit falls back at most twice, so N units have at most 2N fallbacks, and the rounds end.
   - These later rounds loop inside one single job, which "processes all loop elements in a single worker thread" ([Schedule][schedule]), so no extra core shortens them. A fallback can make the unit behind it fall back in the next round, so a chain can run down a dense queue or a block's column, one round per link (Cost).
   - So a tick that starts with no overlapping footprints ends with none. The spawn tool places units without overlaps.

**What is guaranteed.** ORCA's own guarantee is conditional. Of `neighborDist` and `maxNeighbors`, RVO2's documentation says "If the number is too low, the simulation will not be safe" ([RVO2 parameters][rvo2-params]), and ours are capped for cost: two small cubes closing head-on at 8 cells/s first see each other about 0.4 s before contact, under half the time horizon. Acceleration limits also mean a unit can't always take the velocity ORCA chose, and in a dense crowd the linear program may have no solution. One capped, parallel overlap pass can leave an overlap, or push a unit into a third. So ORCA and the overlap pass keep contacts rare and shallow, and only the move check guarantees S2.2's and S2.5's "no overlaps" and S3.1's "never through obstacles". It turns what would be an overlap into a stall: a unit that falls back doesn't move that tick. The spike and the S3 tests count fallbacks and the stalls they cause, including in dense-contact and wall-contact scenes (Consequences).

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

**Pace (S2.3, S6.4).** For each moving unit i, `r_i` is the field's remaining distance at its position (the straight-line distance while it has no field), plus its slot's offset from the goal along the forward direction. So `r_i` is how far the unit is from its own slot along the route. Each tick, a job sums `r_i` for each group in fixed-point integers, so the mean doesn't depend on thread order. Then:

```text
target speed_i = clamp(v_group + k_pace × (r_i − mean r), minPace × v_group, v_max_i)
```

Here `v_group` is the group's slowest top speed, `k_pace` is 0.5 per second by default, and `minPace` is 0.3. Units behind speed up and units ahead slow down. Units bound for front slots travel ahead by their slot's offset, so the group travels at about its arrival depth, and every unit reaches its slot at about the same time. A one-unit group moves at its own top speed.

**Slots.** When an order is applied, a job lays out slots around the goal (after any correction for reachability):
- Forward is the direction from the group's centroid to the goal. Slots form square lattices aligned with forward.
- Units form one **block per footprint width**, whatever their size class. A block of N units has `ceil(sqrt(2N))` columns, so it is about twice as wide as it is deep. Its pitch, sideways and front to back, is `slotSpacing` × that width.
- Blocks stack front to back, and the stack is centred on the goal. The block whose members are furthest forward on average when the order is given goes in front, the wider one on a tie. The facing rows of two blocks are `slotSpacing` × the sum of their half-widths apart.
- Units in their slots face forward and are formation pairs, tested as aligned squares (Contact shapes). So with `slotSpacing` above 1, no two slots' squares overlap, whatever the widths. `slotSpacing` is 1.25 by default and must stay between 1.05 and 1.3.
  - **S2.2:** 100 small cubes fill 15 × 7 slots at 0.625 cells. The rectangle holding their squares is 9.25 × 4.25 cells, 1.57 times their combined footprint of 25 cells² (1.69 at 1.3).
  - **S2.3:** 50 small and 10 large cubes form a 10 × 5 block at 0.625 cells (6.1 cells wide, 3 deep) and a 5 × 2 block at 1.25 cells (6 wide, 2.25 deep), with facing rows 0.94 cells apart. Each unit's pace counts its own slot's offset, so both blocks come in together.
- A slot is valid when the unit's disc there clears every blocked cell, and the slot is in the goal's region for the unit's class, has a clear line from the goal (however long), has its disc clear of every stationary unit from outside the order, and keeps the spacing above from every other slot.
- **The search is bounded.** Each invalid slot is replaced by a valid, unused point of its block's lattice from the `slotSearchRings` rings around the block (8 by default), nearest the goal first, ties in a fixed lattice order. A block of c columns and r rows looks at no more than (c + 16) × (r + 16) points at the default, so the layout always ends.
- **Too little room.** A block can still run short, as in a small room reached through a bent corridor. Its last units in the assignment order below, the furthest back, then get no slot. These *overflow* units follow the field with the rest, with pace counting a slot one pitch behind the block's rear row. Each one stops and becomes idle, with that point as its home, where its disc first comes within 0.1 cells of an idle unit of its group. So they fill in back along the route from the units already there, the nearest places outside, and each is reachable because the unit stops on its own way in. The overlay counts them.
- **Assignment keeps relative positions.** Within each block, units sorted by forward position fill the rows front to back, and within each row they are sorted by sideways position. Slots are ordered the same way and paired in order, so paths rarely cross. Sorting uses `NativeSortExtension` from Collections ([NativeSortExtension][nativesort]).

**Arrival and stillness (S2.2, S2.4)**
- A unit seeks its slot directly and turns to face forward once it has a clear line to the slot, however long, and either is within `arrivalRange` (6 cells) of it, or its distance to the goal along the field (in a straight line while it has no field) is at most `arrivalRange` plus the slot's distance from the goal. Every valid slot has a clear line from the goal, so every unit finds one on its way in, at the latest at the goal, including on the wings of a block wider than `arrivalRange`. The move check holds a turn back until the unit has room, so it finishes turning before it joins the formation.
- Within 0.05 cells and nearly still, it stops and becomes idle, with the slot as its home.
- **Progress** is a new best on a unit's remaining route. While it follows the field, that is its distance to the goal along the field (in a straight line while it has no field) plus its slot's distance from the goal (none for an overflow unit). While it seeks its slot directly, it is its straight-line distance to the slot. By the triangle inequality that is no more than the first, give or take the field's rounding, so switching doesn't hide progress. A unit progresses when its route falls at least 0.1 cells below its best so far. Being shoved back and forth doesn't count, and each unit can progress only finitely often.
- **Congestion isn't arrival.** A unit within a quarter pitch of its slot that hasn't progressed for 1 s settles where it is. A unit further away never settles on its own: waiting behind others, as at a gap, is congestion, so it keeps its order, field and pace, and the right-turn rule keeps working on it (S3.3).
- **Group stall.** When no moving unit of a group has progressed for `groupStallTime` (6 s by default, after the right-turn rule reaches 60° at 5 s), its moving units settle where they are, and the overlay counts it. The timer doesn't run while any unit of the group waits for a field. This ends orders whose last slots can't be reached, for example a slot under an enemy or a unit on hold position. While a queue files through a gap, its front units progress, so the queue never trips it. Because progress is finite, every order ends, so S2.4 can always be measured.
- **Checks.** The S2 and S3 tests expect no overflow units and no group stalls, and every unit ends idle within a quarter pitch of its slot.
- An idle unit's preferred velocity is zero, and ORCA keeps a velocity that is already safe. Settled units of a group are apart under their pair rule, so they don't push each other, and an arrived group stays still unless something pushes it.
- An own-side unit can still pass through an arrived block: idle units give way (rule 3 above) and walk back to their slots.
- **S2.2's margin.** Even with a unit settled a quarter pitch outside the block on every side, the 100 small cubes would fill 1.75 times their footprint at the default spacing, and 1.88 at 1.3.

**Other orders,** which the S4 and S5 Issues refine within this ADR:
- **Stop (S4.1).** Deceleration must be at least twice the top speed per second, so a unit at full speed halts within 0.5 s.
- **Waypoints (S4.3).** Each waypoint is a leg of the same group. The next leg's field is requested when the group has a quarter of the current leg left. Slots are laid out only at the last waypoint.
- **Moving targets (S5 attack-target).** Direct seek within range and line of sight. Otherwise a field to the target's cell, rebuilt once the target has moved 4 cells, at most once per second per group.

### Tick rate: 30 Hz

The simulation runs at a fixed 30 ticks per second (dt = 1/30 s). It uses the accumulator method of [Fix Your Timestep][fiedler], and drawing interpolates between the last two ticks, as ADR 0004 sets. At most two ticks run in one frame. Beyond that, the game slows down rather than falling into the spiral of death, where "being behind causes your update to simulate more steps to catch up" ([Fix Your Timestep][fiedler]). When a frame runs two ticks, the driver chains both and completes them once. The ladder log records 30 Hz.

Why 30:
- **Step size.** The fastest test unit moves 4/30 ≈ 0.13 cells per tick, about half the small cube's half-width of 0.25. So units can't skip past each other, or through a one-cell obstacle, within a tick, which the move check relies on because it tests only where a tick ends. At 20 Hz the step would be 0.2 cells, close to the half-width. Rule: a unit type's top speed is at most 30 × half its width, which is 7.5 cells/s for the small cube. A faster type needs a new ADR (sub-steps or a higher rate).
- **Feedback needs no tick (Fight).** On the frame after the click, whether or not a tick has run, the presentation draws the order marker and an acknowledgement on every ordered unit, for example a pulse of its selection marker, whose look the UI Issue sets. It is one time stamp per order, read by the drawing job, so it adds nothing per unit, and it never moves or turns a footprint.
- **Response (S2.1).** An order is applied at the next tick, and every unit has a velocity from that tick. The worst case from click to the first drawn frame of movement is one frame to read input, one tick and one frame: about 67 ms at 30 Hz and 60 fps (83 ms at 20 Hz), within S2.1's 0.1 s.
- **Cost.** 30 Hz costs half the CPU of 60 Hz. At 60 fps, ticks fall on every other frame, so ADR 0004's 1% low sees the per-tick cost (budget below).
- **Practice.** Blizzard's StarCraft II API says its real-time mode "Uses the “faster” game speed (22.4 gameloops per second)" ([s2client-proto][s2proto]). An RTS simulation that ticks slower than the frame rate is established practice.

### How it maps onto ADR 0004

The tick's chain, in order. "Parallel" means `IJobFor.ScheduleParallel`, and "single" means `IJob` or `IJobFor.Schedule`. The job names are examples.

| Job | Kind | Reads | Writes |
|---|---|---|---|
| `PublishFieldsJob` | single, depends on the builds due this tick | their build scratch | field pool, groups' fields, requests |
| `RebuildStaticMapsJob`, only after a walkability change | single | `walkable` | passability per class, regions |
| `ApplyOrdersJob` | single | command queue, regions, last tick's grid | unit order state, group table, slots, field requests |
| `BuildSpatialGridJob` | parallel bucket pass, then a single counting sort | positions | grid |
| `GroupPaceJob` | single | fields, units | group sums, latest progress tick |
| `PreferredVelocityJob` | parallel | fields, groups, slots, static maps | preferred velocity |
| `AvoidanceJob` | parallel | grid, poses, velocities, static maps | new velocity |
| `IntegrateJob` | parallel | new velocity, unit types | pose, previous pose, velocity, state |
| `ResolveOverlapJob` | parallel | grid, poses | corrected pose |
| `CheckMovesJob` | parallel first round, then one single job looping over the fallback rounds | grid, start and corrected poses, static maps | accepted pose |
| `BuildFlowFieldJob`, up to 2 | single each, outside the chain, due `buildLatencyTicks` later | static maps | its own scratch |

Unit AI and targeting (a later ADR) slot in between the grid and preferred velocity.

**The spatial grid** is a counting sort, rebuilt every tick:
- Buckets are 2 × 2 cells, with a start and a count per bucket.
- Unit indices are sorted by bucket, and poses and velocities are copied in the same order, so neighbour scans read memory in sequence.
- The sort runs single-threaded in index order, so the order within a bucket is fixed.
- ORCA, the overlap pass, the move check, slot validation and home return query it. ADR 0004's targeting and selection share it. It has no hash maps and nothing that grows.

**Allocated once, at match start, with `Allocator.Persistent`.** Sizes are for 2,000 units on a 256² map, with 512² in brackets:

| Data | Size |
|---|---|
| Movement state per unit: pose and previous pose (position and heading), velocity, preferred and new velocity, home, slot, group, field, state and timers | about 90 B per unit, 180 KB |
| Group table and request queue | about 0.15 MB |
| Spatial grid | about 0.2 MB (0.6 MB) |
| Static maps for classes 0 and 1: walkable, passability, regions | 0.5 MB (2 MB) |
| Field pool: 64 fields × cells × 3 B | 12 MB (48 MB) |
| Build scratch for 2 slots, sized from the cell count; the fields Issue tests the bound | about 3 MB (10 MB) |
| Slot, ring-search and sort scratch | about 0.2 MB |

Nothing is allocated per tick or per order.

**Determinism.** Every parallel per-unit job reads the state from the start of its stage and writes only its own unit's row. This matters because for `IJobFor`, "The indices have no guaranteed order" ([IJobFor][ijobfor]). Group sums use integers, neighbour ties go by index, and each round of the move check decides from the poses at its start. Fields are published on their due tick, and the request queue and replacements follow fixed orders, so when a field becomes available depends only on the inputs. So a tick's result doesn't depend on the number of workers or on their load. An overrunning build delays a frame but doesn't change the result. The same seed, inputs and build on the same machine replay the same way, which EditMode tests and ladder runs rely on. Burst keeps its default float mode. Its Deterministic mode targets consistency across platforms and can cost floating-point performance ([BurstCompile][burst-compile]), and a single-player game doesn't need it.

### Cost at 2,000 units

These are estimates for one tick with 2,000 moving units, from operation counts, not measurements:

| Stage | Cost per tick |
|---|---|
| Grid, group pace, preferred velocity with its line tests to slots, integration | about 0.3–0.7 ms of worker time |
| ORCA: about 150 grid candidates and 10 constraints per unit in a dense crowd, about 1–3 µs per unit | about 2–6 ms of worker time |
| Overlap pass | about 0.3–1 ms of worker time |
| Move check, first round: up to about 20 footprint tests per unit | about 0.2–0.5 ms of worker time |
| Move check, fallback rounds: serial, about 5–10 µs per fallback to re-test the units round it | about 0.1–0.5 ms of wall time for tens of fallbacks. A chain down a dense 200-unit queue, two fallbacks per unit, would take about 2–4 ms. The bound, 2N fallbacks, is about 20–40 ms at 2,000 units, which the cap on serial fallbacks (Levers) removes |
| **Total** | **about 3–8 ms of worker time for the parallel stages, so about 1–2 ms wall time**, assuming about five cores' worth of parallel speed-up on a 6-core CPU, **plus the fallback rounds**, which no extra core shortens |
| Main thread: drain commands, schedule, complete | at most 0.3 ms, flat in the unit count, plus the wait for a build that overruns its tick |
| One field build, outside the chain, on one worker | about 1–6 ms at 256², 4–25 ms at 512² |
| An order for 2,000 units (sort and slots), only on that tick | about 0.2 ms, up to about 1 ms when the ring search runs to its limit |

Per unit, that is about 1.5–4 µs of worker time per tick and about 90 bytes. Movement adds no GPU cost.

**Against the pass rule.** At 60 fps, movement adds about 1–2 ms plus the fallback rounds to every other frame and nothing to the rest. That leaves most of the 16.67 ms for drawing and for the systems still to come.

**Budget.** At 2,000 units on the reference PC, the movement chain takes at most 3 ms of wall time and 0.5 ms of main-thread time on tick frames. Both are read from the Profiler's timeline in a development build. The fallback rounds count within the 3 ms and may use about 0.5 ms of it in 99% of ticks. If the spike measures more, the cap on serial fallbacks applies (Levers). A PR over budget either fixes it or states the cost and why, as ADR 0004's regression rule asks. The budget is a judgment, and the spike checks it.

### Rules for hot paths

These add to ADR 0004's rules.

1. **Fields, not paths.** A unit holds a field index, never a path. A field belongs to a (goal cell, class) pair, never to a unit.
2. **Own row only.** Parallel per-unit movement jobs read the state from the start of their stage and write only their own unit's row. No float atomics. Single-threaded jobs (the grid's sort, group sums, publishing, the move check's later rounds) work in index order. Per-group sums use integers.
3. **Neighbours through the grid,** capped at `maxNeighbours` for ORCA, with ties broken by unit index. The move check is never capped.
4. **Builds stay in their scratch.** At most `buildSlots` builds run at once. Only `PublishFieldsJob` writes the pool, on each build's due tick, through a dependency on its handle. Simulation state never depends on [`JobHandle.IsCompleted`][is-completed].
5. **Walkability changes only at the start of a tick,** through the command queue.
6. **One settings asset.** Every movement tunable lives in one movement settings ScriptableObject in `Data/`. Its class lives in `Northwatch.Presentation`, which copies it into a plain struct in Simulation at match start (ADR 0005 rule 5).
   - At match start, every unit type is checked: top speed at most 30 × half its width, and deceleration at least twice its top speed per second. `slotSpacing` must be between 1.05 and 1.3.
   - A failed check logs an error and the match doesn't start.
7. **No NavMesh,** physics queries or colliders for movement.
8. **Where the code goes.** Code lives in `Code/Simulation/Movement/` (the tick stages, ORCA, groups and slots), `Code/Simulation/FlowFields/` (static maps, the pool and builds) and `Code/Simulation/Spatial/` (the grid), in the matching `Northwatch.Simulation.*` namespaces (ADR 0005, ADR 0007). Tests mirror these folders.
9. **Footprints decide overlap.** Overlap is tested on footprints, never on discs alone. The move check is the last stage that changes a pose in a tick, and drawing never moves or turns a footprint.

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

- **Separation steering alone** ([Reynolds][reynolds]), plus the overlap pass. It's the cheapest and simplest, at a few operations per neighbour. But it reacts to contact instead of anticipating it, so two groups crossing head-on (S3.4) push into a jam. Blended steering forces can also "cancel each other out at inopportune times" ([Reynolds][reynolds]). Not chosen; the overlap pass and the move check stay as the safety net.
- **ORCA (chosen).** It anticipates collisions over a time horizon. The authors report simulations "involving thousands of agents", with collision-free actions computed "in only a few milliseconds" on their hardware ([ORCA][orca]). It costs more per unit (a small linear program) and needs tuning in dense crowds. It assumes every agent shares the work of avoiding, which our share rules make explicit. Background for the implementer: [Sunshine-Hill][sunshine-hill].
- **Anticipatory force models,** for example the time-to-collision power law of [Karamouzas et al.][powerlaw]. They also anticipate, and cost less than a linear program. But they integrate forces, so at a 30 Hz tick the strong forces close to a collision would need clamping and tuning that we have no evidence for. Not chosen. This is the first alternative if ORCA fails the S3 tests (Consequences).

### Collision shape

- **Discs of half the width.** The cheapest and densest, but two such discs can be apart while their squares overlap at the corners, so S2.2 and S2.5 could fail. Not chosen.
- **Discs round the whole square, for every pair.** Safe at any heading, but slots must then be 1.41 widths apart. 100 small cubes would fill 1.97 times their footprint on a square lattice, or 1.8 on a staggered one with no room left to arrive, against S2.2's limit of 2. Not chosen.
- **Oriented squares inside avoidance.** Exact, but ORCA's published method is for discs, so it would need another velocity-obstacle method and the most code. Not chosen.
- **Chosen:** discs round the square, aligned squares within a formation, and the move check on footprints.

### Groups

- **Each unit at its own speed, with no slots.** The simplest, but a mixed group strings out: over 50 cells, small cubes arrive 7.5 s before large ones, so S2.3 fails. Everyone also piles onto the goal point, so S2.2 fails.
- **A rigid formation behind a virtual leader.** Keeps its shape on the way, but breaks in gaps and around obstacles, and needs rules for re-forming. Pillar 3 doesn't ask for "a perfectly predictable path for every single unit".
- **Pace control with arrival slots (chosen).** Keeps groups together and arrival tidy, for one sum per group per tick and one sort per order.

### Tick rate

- **20 Hz.** The cheapest, but the small cube's 0.2-cell step is close to its 0.25 half-width, and fast unit types would be limited to 20 × their half-width.
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
  - Units outside a formation keep further apart than their squares need (small cubes' centres at least 0.71 cells apart, not 0.5), so a 3-cell gap takes four small cubes abreast, not six. A one-cell-wide unit needs two-cell gaps.
  - Under saturation, groups take turns with fields and use direct seek in between, which can lengthen routes past S3.1's 1.25. S3.1's test doesn't saturate the pool.
- **Risks.**
  - Every cost figure is an estimate until the spike.
  - ORCA may still stall at the 3-cell gap (S3.3) despite the right-turn rule. The response is to tune within the parameters. If S3.3 or S3.4 still fails after two review rounds, the Producer brings it back to the architect.
  - The move check turns leftover overlaps into one-tick stalls. If they stall units at the gap (S3.3) or in crossings (S3.4), the levers below come first.
  - A group stall could end an order whose jam would have cleared later. Its 6 s outlasts the right-turn rule, the overlay counts stalls, and the S2 and S3 tests expect none.
  - Drawn frames blend two tick poses. Between two overlap-free ticks, a frame can show a corner contact no deeper than half of how far the pair moved relative to each other in that tick. QA measures overlap on tick poses. At rest, as in S2.2 and at S2.5's arrival, frames and ticks are the same.
  - The acknowledgement reads the Fight section's "units reacting" as visible feedback on the next frame, with movement following within S2.1. If the designer meant visible movement on that frame, a fixed tick with interpolation can't give it, and that would be a GDD question for the owner.
  - Maps for four sides larger than 512² would need the tiled method.
- **Reversibility.**
  - Tiles instead of whole-map fields change only the storage behind one sampling function.
  - Another avoidance method replaces only `AvoidanceJob`, whose input (preferred velocity) and output (new velocity) stay the same.
  - The tick rate is one constant, but tuning values in seconds and the unit-type check depend on it. A change needs a superseding ADR and starts a new ladder series.
- **Triggers for a superseding ADR.**
  - Field builds take more than 20 ms at the 95th percentile on the largest planned map, the pool needs more than 64 MB, or the spike's order stream leaves more than 5% of moving unit-ticks on direct seek: adopt flow-field tiles.
  - The movement chain stays over budget after the levers below, or ORCA can't pass S3.3 or S3.4: revisit avoidance.
  - A unit type is faster than 30 × half its width.
- **Levers within this ADR.** These are tuning and need no new ADR:
  - fewer neighbours, down to 6, or more, up to 16
  - no ORCA for units with no neighbour within `neighbourDistance`
  - no movement stages for idle units with no mover nearby
  - a second overlap pass per tick
  - a cap on the move check's serial work (`maxSerialFallbacks`, the fallbacks after its first round). When it is reached, the units still to be checked, and every unit that could touch one of them, directly or through others, go back to their start-of-tick poses. Those units then overlap no more than at the start of the tick and can't touch any unit outside the set, so the guarantee holds, at the price of a one-tick stall for the set. The serial time is then at most the cap times 5–10 µs, plus one pass to find the set.
  - a different bucket size for the grid
  - `fieldCapacity`, `minLease` and `buildLatencyTicks`, within the memory trigger
- **ADR 0004.** This ADR is ADR 0004's follow-up 7 and supersedes nothing. It sets the tick rate (30 Hz) and the grid's layout, and adds the rules above to ADR 0004's hot-path rules. ADR 0004's one sync point still holds: a field build completes through the chain of its due tick, at that sync point.
- **Dependencies and owner decisions.** No package, no Unity change and no handbook change. No owner decision is needed. `com.unity.ai.navigation` stays in the manifest unused. Removing it would need its own ADR (AGENTS.md §4).
- **Follow-up Issues,** in build order, after #84 (packages) and ADR 0004's sandbox and ladder harness (its follow-ups 5 and 6):
  1. **Movement spike** (`type:spike`, gameplay-programmer, two days). It can be folded into ADR 0004's follow-up 4 spike if that hasn't started. On the reference PC, it measures:
     - field build time (median and 95th percentile) at 128², 256², 384² and 512², on open ground and with 20% of cells blocked, plus the pool and scratch memory at each size
     - the movement chain's wall time, per job and in total, and its main-thread time, at 1,000 and 2,000 units, against the 3 ms and 0.5 ms budget. It runs in the ladder scenario (two blocks crossing head-on) and with 200 units through a 3-cell gap.
     - frame times on tick and non-tick frames, with the ladder method, at 1,000 and 2,000 units
     - the time from an order to every unit moving (S2.1)
     - field saturation at 256²: 200 single-unit orders to 200 different goal cells in one tick, then 10 orders per second to new goal cells for 60 s. It records the wait from request to publish (median, 95th percentile and maximum), replacements per second, the share of moving unit-ticks on direct seek, and the time from each order to its units moving.
     - the share of builds that miss their due tick at each map size, which sets `buildLatencyTicks`
     - footprint overlaps per tick (expected 0), move-check fallbacks per tick and the stalls they cause, and units stalled for more than 5 s with somewhere to go. It runs in both scenes above, a dense-contact scene (200 units ordered onto one point from four sides) and a wall-contact scene (a crowd pushed along a wall and into a concave corner).
     - in the same scenes, at 1,000 and 2,000 units: the move check's serial rounds and fallbacks per tick, its longest chain, and the rounds' serial wall time (median, 99th percentile and maximum) against their 0.5 ms share. Five-core scaling doesn't apply to them.
     - 0 B GC Alloc after warm-up

     Its finding says which trigger or lever above applies. If the time box runs out first, the finding reports what was measured, and the rest is measured in the ladder entries of Issues 3 to 5.
  2. **Static maps and flow fields** (the pathing half of S3.1 and S3.2): `walkable` from cell-aligned boxes, passability for classes 0 and 1, regions, the build job, the pool, the request queue, leases, the fixed publish tick, direct seek and goal correction.
     - EditMode tests: distances on open ground, a detour round a box within 1.25 times the shortest route, a goal inside a ring of boxes, 65 goals with 64 fields (every group moves at once and gets its field within the lease bound), and a build published on its due tick under any worker load.
     - The debug overlay draws field directions and regions.
  3. **The 30 Hz movement tick** (S2.1, S3.1, Fight feedback): movement state, footprints and discs, the grid, preferred velocity, integration with the static clamp, the overlap pass, the move check, interpolation and the next-frame acknowledgement. Orders move units, without slots yet. With two gameplay-programmers, Issues 2 and 3 can run in parallel once they agree the sampling function's signature.
     - EditMode tests: squares turned 45° whose discs just touch don't overlap, and no tick adds a footprint overlap in a crowd squeezed together or against a wall and into a concave corner.
  4. **Groups and arrival** (S2.2, S2.3, S2.4): the group table, blocks per width, the bounded slot search and overflow units, slot assignment, formation pairs, pace, arrival, progress, group stalls and facing. The overlay draws slots.
     - EditMode tests: a room too small for the order behind a bent corridor (the layout ends within its bound, every unit ends idle, and the overflow units stand back along the corridor), a unit put on hold position on a slot after the order (one group stall, then all idle), and a 2,000-unit block whose wing units reach their slots.
  5. **ORCA and making way** (S3.3, S3.4, S3.5, S2.5): the avoidance job, the share rules, idle units walking home, and the right-turn rule. Its tests include the dense-contact and wall-contact scenes and check the S3 expectations under Arrival and stillness, and its ladder entry shows S2.5.
  6. **Suggested:** add rules 1 to 9 to the gameplay-programmer and code-reviewer role files, together with ADR 0004's follow-up 11. These are protected paths, so that Issue must name them.
- **Later Issues.** Issues 3 to 5 each include their ladder entry (ADR 0004). Later Issues use these rules without new movement work: S4 (stop, hold, waypoints), S5 (moving targets), the S6.4 test, S9 (walkability changes) and the units section (wider size classes).

[navquery]: https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Experimental.AI.NavMeshQuery.html
[ijobfor]: https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Unity.Jobs.IJobFor.html
[schedule]: https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Unity.Jobs.IJobForExtensions.Schedule.html
[is-completed]: https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Unity.Jobs.JobHandle.IsCompleted.html
[batched]: https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Unity.Jobs.JobHandle.ScheduleBatchedJobs.html
[native-container]: https://docs.unity3d.com/6000.3/Documentation/Manual/job-system-native-container.html
[job-deps]: https://docs.unity3d.com/6000.3/Documentation/Manual/job-system-job-dependencies.html
[obbtree]: https://doi.org/10.1145/237170.237244
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
