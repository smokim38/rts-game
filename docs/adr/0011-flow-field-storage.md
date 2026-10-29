# ADR 0011: Flow-field storage after the movement spike

## Status

Proposed

## Date

2026-10-29

## Context

[ADR 0008](0008-movement-and-pathfinding.md) keeps a pool of `fieldCapacity` (64) whole-map flow fields. It names three triggers for adopting flow-field tiles: builds over 20 ms at the 95th percentile on the largest planned map, a pool over 64 MB, or the spike's order stream leaving more than 5% of moving unit-ticks on direct seek. Its follow-up 1, the movement spike (#141), has reported. The spike's code was throwaway, and its PC (i7-12700KF with 12 cores and 20 threads, RTX 3050 6 GB, 64 GB RAM, Windows 11) isn't reference-class for CPU, because ADR 0004 names a mid-range 6-core CPU. Its numbers follow.

**Field builds** (class 0, Burst Dijkstra, two build slots, 30 Hz, published one tick later; medians of three runs):

| Map | Build median / p95, open (ms) | Build median / p95, 20% blocked (ms) | Pool, 64 fields (MiB) | Scratch, two slots (MiB) |
|---|---|---|---|---|
| 128² | 0.215 / 0.267 | 0.444 / 0.530 | 3 | 0.47 |
| 256² | 0.896 / 1.271 | 1.762 / 2.618 | 12 | 1.88 |
| 384² | 3.385 / 4.692 | 4.053 / 5.029 | 27 | 4.22 |
| 512² | 5.035 / 9.416 | 9.143 / 14.412 | 48 | 7.50 |

The worst single-run p95 was 15.8 ms (512², blocked). Builds missed their due tick only in one 512² blocked run, 10 of 3,598 (0.28%), so `buildLatencyTicks` = 1 holds.

**Saturation at 256²** (64 fields, two slots, 3 s leases). The stream was 200 single-unit orders to 200 goal cells in one tick, then 600 orders at 10 per second to new goal cells on the opposite half of an open map.
- 68.4% of moving unit-ticks were on direct seek, against the 5% trigger. The other two triggers didn't fire.
- The wait from request to publish was 4,967 / 6,733 / 7,261 ms (median / p95 / max). The run replaced 21.6 fields per second, published 1,362 builds, and ended with 137 requests pending.
- Every unit started moving on the tick its order was applied.

**What the stream shows.** Pool capacity limited it, not build speed:
- Two slots can publish 60 fields per second. The run replaced 21.6 per second, which is the 64 fields divided by the 3 s lease (21.3). So each publish waited for a lease to expire.
- With 200 goals in use and 64 fields, groups take turns, and each spends about 1 − 64/200 = 68% of its time without a field, which matches the 68.4% measured.
- So no lease or queue setting fixes it, and neither does a cheaper build. Only enough fields for the goals in use does.

**Movement chain** (throwaway: small cubes only, prebuilt fields, no pace or slots, instrumentation included). At 2,000 units the chain's median was about 1.24 to 1.55 ms and its p99 3.0 to 6.1 ms. The move check's serial rounds had a p99 of 1.9 to 5.0 ms, over their 0.5 ms share at 1,000 and 2,000 units in every scene. No footprint overlapped on any tick in the 33 crowd runs. Two items went unmeasured because the Editor's profile loader showed only the last 2,000 frames of each capture: main-thread time over the whole window (0.5 ms budget), and 0 B GC Alloc over the whole window.

**Constraints**
- ADR 0004's hot-path rules, and ADR 0008 except the parts this ADR supersedes. No new package. ADR 0005 and ADR 0007 set folders and names.
- The engineers are AI agents, each on one Issue at a time. Movement Issue 3 should be able to start before the field Issue (2), so the function movement uses to read a field must be fixed now.
- Maps: about 256² for one against one, and up to 512² budgeted for four sides (ADR 0008, from `docs/design/core-loop.md`). The sandbox is 256².

**Evidence gap.** Nobody has measured a larger pool, tiles or paged storage. The figures for them below are arithmetic from the spike's numbers.

## Decision

**Keep whole-map flow fields and raise `fieldCapacity` from 64 to 256. That fits maps of up to 87,381 cells (for example 256 × 256, or at most 295 × 295) in 64 MiB. Fix the sampling function now, so the storage behind it can change later without touching movement. Don't adopt flow-field tiles.**

### Why

- **It removes the measured cause.** 256 fields cover the stream's 200 goals in use, with room for recurring goals such as rally points and Depots. So no field that still has users is replaced, and waits come only from build throughput (Target, below).
- **It stays within ADR 0008's other triggers at 256².** The pool is 48 MiB (trigger 64 MB). Builds take 1.3 ms open and 2.6 ms blocked at the 95th percentile (trigger 20 ms), with no missed due ticks.
- **Routes stay exact.** A whole-map Dijkstra field gives the shortest eight-way route from every cell (ADR 0008).
- **It is the least work.** It needs one new default and a start-up check. Tiles would cut build time, which isn't the limit, at the highest cost to the team (Alternatives).

### Field pool

These rules replace ADR 0008's `fieldCapacity` default and its "more than 64" saturation threshold. The rest of its "Field pool and requests" stays.
- `fieldCapacity` is 256 by default. Saturation now needs more than 256 different (goal cell, class) pairs in use by moving units at once. ADR 0008's turn-taking then applies unchanged.
- **Match-start checks.** The pool (`fieldCapacity` × cells × 3 B) must be at most 64 MiB, `fieldCapacity` at least 200 (the stream's goals in use), and `buildSlots` between 2 and 4. If any check fails, the match logs an error and doesn't start, as with ADR 0008 rule 6. Tests that build a pool directly may use any capacity.
- **Why 64 MiB.** ADR 0008's trigger says 64 MB, but its memory table writes MiB as MB: 64 fields at 256² is its "12 MB", which is exactly 12 MiB. So this ADR reads the trigger as 64 MiB.
- **Empty groups drop their request.** If a request reaches the head of the queue after every unit of its group has left for a new order, it is dropped without a build.

| Map | Cells | Pool at 256 fields | Allowed |
|---|---|---|---|
| 256² | 65,536 | 48 MiB | Yes |
| 295² | 87,025 | 63.7 MiB | Yes, the largest square |
| 384² | 147,456 | 108 MiB | No |
| 512² | 262,144 | 192 MiB | No |

A map over 87,381 cells needs a superseding storage ADR first (see Triggers).

### Target for the field Issue

**On the stream below, at the defaults, at most 5% of moving unit-ticks are on stopgap direct seek.** A unit-tick counts when the unit has a move order that needs a field and its group has no published field for its class. On an open map that is the same as `Sample` not returning `Ready` (Sampling).

The stream is an EditMode test on a 256² open map:
- **Units.** 200 class-0 units on distinct cells, placed from a fixed seed.
- **Held still.** No movement stage runs, so every unit with an order counts as moving for the whole window.
- **Deterministic.** Fields publish on fixed ticks (ADR 0008), so the result doesn't depend on the hardware.
- **Orders.** At tick 0, each unit gets a single-unit order. Then on ticks 3, 6, … 1,800 (10 per second, 600 orders), one order goes to the next unit in index order, wrapping round.
- **Goals.** Each goal is a new cell on the other half of the map from its unit (split at x = 0) and more than `directSeekRange` (12 cells) from it, so every order needs a field. All goals are different and come from the same seed.
- **Window.** Ticks 0 to 1,829.
- **Without the group table.** If Issue 4's group table isn't there yet, the test may feed the queue one request per order.

**Expected result** (arithmetic, not measured):

| Source of stopgap ticks | Unit-ticks |
|---|---|
| The 200 requests queued at tick 0. Two slots publish them over 100 ticks | 10,100 |
| The 33 orders given meanwhile, which wait behind them | about 1,900 |
| The other 567 orders, one tick each | 567 |
| **Total**, out of 200 × 1,830 = 366,000 | **about 12,600, or 3.4%** |

With `buildSlots` 3 the share is about 2.2%.

### Sampling

Movement reads a field only through this function. Issue 3 creates the three types, each in its own file in `Code/Simulation/FlowFields/` (`Northwatch.Simulation.FlowFields`, as ADR 0005 and ADR 0007 set), together with their tests.

```csharp
internal enum FlowSampleStatus : byte { Ready, Unreachable, Unavailable }

internal struct FlowSample
{
    internal float2 Direction; // world x and z: unit length, or zero at the goal
    internal float Distance;   // cells along the field to the goal
}

internal struct FlowFieldReader
{
    internal const int NoField = -1;
    internal FlowSampleStatus Sample(int field, float2 position, out FlowSample sample);
}
```

What `Sample` does. No storage change may alter these eight points:
1. **Inputs.** `field` is the field index the unit's group holds for the unit's class, or `NoField`. `position` is the unit's world x and z, which callers never offset.
2. **No field.** `NoField` gives `Unavailable`. A later storage may also return `Unavailable` where a field holds no data. Callers treat `Unavailable` and `Unreachable` alike, as a unit still waiting for its field, and use stopgap direct seek (ADR 0008). They never request a field per unit.
3. **Where values sit.** A class-k field's value (x, y) belongs to the world point origin + (x, y) + (k + 1) / 2. That point is the centre of the (k + 1) × (k + 1) square of cells whose lowest-x, lowest-z cell is (x, y), which is how clearance-based planning anchors a square to a cell ([HAA*][haa]). For class 0 the point is the cell centre. For class 1 it is the corner shared by four cells. Issue 2 anchors passability per class the same way.
4. **Weights.** Let p = position − origin − (k + 1) / 2, i = floor(p) and t = p − i. The values at i, i + (1, 0), i + (0, 1) and i + (1, 1) get the weights (1 − tx)(1 − ty), tx(1 − ty), (1 − tx)ty and tx·ty. A value outside the map, or with distance 65,535, is unreachable and gets weight 0. The other weights are scaled to sum to 1. If no value is reachable, the result is `Unreachable`, with zero direction and distance.
5. **Distance.** The weighted mean of the reachable values' distances, in cells (tenths ÷ 10).
6. **Direction.** The normalised weighted sum of the direction vectors of the reachable values whose distance is above 0.
   - If that sum is shorter than 0.001 because the directions cancel, use the direction of the heaviest such value. On a tie, use the first in the order of point 4.
   - If no such value exists (only goal values), the direction is zero.
7. **Encoding.** A direction byte d (0 to 7) is the vector (cos 45d°, sin 45d°) in world (x, z), turning from +x toward +z. Distances are ADR 0008's: a `ushort` in tenths of a cell, saturating at 65,534, with 65,535 meaning unreachable.
8. **Safe in jobs.** `Sample` writes nothing and allocates nothing. It compiles with Burst, is safe in parallel jobs, and reads at most four values from each array.

**The whole-map storage.** Issue 2 fills it, and Issue 3's tests fill it by hand.
- `directions` (`byte`) and `distances` (`ushort`) each hold `fieldCapacity` × width × height values. Field f's value (x, y) is at f × width × height + y × width + x.
- `classes` (`byte`) holds one value per field.
- The reader holds these arrays as `NativeArray<T>.ReadOnly`, which "Represents a NativeArray<T> interface constrained to read-only operations" ([ReadOnly][readonly]). It also holds the width, the height and the world position of cell (0, 0)'s lowest corner. A cell is one world unit, as in the sandbox.
- The reader's constructor and its fields belong to the storage and may change with it. Only a superseding ADR changes the three type names, `Sample`'s signature or points 1 to 8.

### Measuring main-thread time and GC over the whole window

This method replaces "read from the Profiler's timeline" for main-thread time in ADR 0008's Budget. It also adds a whole-window GC check to the capture that ADR 0004 rule 11 asks for. The budget values stay the same.

**The run**
- Use a Windows x64 development Player, otherwise set up like the ladder (ADR 0004), in the ladder scenario at 1,000 and 2,000 units.
- Each run has 10 s of warm-up and a 60 s window. Do three runs and report medians.
- It must be a development build: in release builds, `ProfilerMarker.Begin` and `End` "are conditionally compiled away" ([ProfilerMarker][marker]).

**What the harness records**
- **Main-thread time.** The driver wraps all its main-thread movement work in one `ProfilerMarker`: draining commands, scheduling the tick's chain, and completing it at the sync point, including any wait there. A `ProfilerRecorder` ([ProfilerRecorder][recorder]) records the marker with `SumAllSamplesInFrame`, which is used "to sum all samples within a frame and collect those as one sample per frame" ([SumAllSamplesInFrame][sum-samples]).
- **GC.** A second recorder counts the `GC.Alloc` marker (category `Internal`) on all threads, also summed per frame, the way Unity's example counts allocations ([SumAllSamplesInFrame][sum-samples]).
- **Every frame of the window.** The harness reads both recorders. It stores each tick frame's main-thread time in a buffer allocated before the window, and adds the `GC.Alloc` count to a running total. Reading the recorders allocates nothing, and unlike the Editor's loader there is no frame limit.

**Report and pass**
- Report the median, 99th percentile and maximum of main-thread time on tick frames, the maximum on other frames, the `GC.Alloc` total, and the number of frames with any allocation.
- **Pass:** the median of the three runs' 99th percentiles on tick frames (nearest rank, as in ADR 0004) is at most 0.5 ms, and no run allocates in simulation or drawing code.
- The `GC.Alloc` total is expected to be 0. If it isn't, the PR names the allocating code from a Profiler capture of those frames. An allocation in simulation or drawing code fails (ADR 0004 rule 11).

### Rules for hot paths

These continue ADR 0008's rules 1 to 9, and add to ADR 0004's rules.

10. **Fields only through the reader.** Movement jobs read fields only through `FlowFieldReader.Sample`. Only the field pool's own code (builds, publishing and the reader) indexes the field arrays.
11. **No field means stopgap direct seek.** `Unavailable` and `Unreachable` both mean stopgap direct seek. Field requests stay per group and class.
12. **The pool fits or the match doesn't start.** These are the match-start checks under Field pool.

### What this ADR supersedes

It supersedes only the parts of ADR 0008 listed here:
1. In "Field pool and requests": the `fieldCapacity` default and the saturation threshold that follows from it. This ADR also adds the match-start checks and the dropping of requests for empty groups.
2. In "Following a field": this ADR makes the rule exact (Sampling) and keeps its intent.
3. In "Allocated once": the field-pool row. It becomes 48 MiB at 256², and maps over 87,381 cells aren't allowed.
4. In "Budget": how main-thread time is read. The method is now the one under Measuring main-thread time and GC.
5. In Consequences:
   - the risk "Maps for four sides larger than 512² would need the tiled method"
   - the first trigger, which adopts tiles
   - the lever on `fieldCapacity`, `minLease` and `buildLatencyTicks`

   This ADR's Triggers and Levers replace them.
6. The follow-up Issues 2 to 5, as amended below.

Everything else in ADR 0008 stays in force. That includes:
- flow fields as the pathfinding method, the build and the per-cell encoding
- direct seek, the request queue, leases, the replacement order and the fixed publish tick
- local movement, groups and the tick rate
- the job chain, the budget values and rules 1 to 9
- the triggers on avoidance and on unit speed

ADR 0008's decisions aren't edited (AGENTS.md §9). This section is the record of what is superseded.

### Packages

None are added.

## Alternatives considered

Memory is given for a pool of 256 fields, which the stream needs. Build times are #141's medians, open / 20% blocked. Figures marked "est." are arithmetic. For tiles and pages, the estimates assume tiles of 16 × 16 cells and a corridor three tiles wide.

| Option | Memory, 256² / 512² | Build per field | Route around obstacles | #141 stream at 256² | Work for the team |
|---|---|---|---|---|---|
| **A. Keep 64 fields** (ADR 0008) | 12 / 48 MiB, for 64 fields only | 0.9 / 1.8 ms at 256²; 5.0 / 9.1 ms at 512² | The shortest eight-way route | 68.4%, measured | None |
| **B. 256 whole-map fields (chosen)** | 48 / 192 MiB | Same as A | Same as A | about 3.4% (est.) | A default and a start check |
| **C. A pool filling 64 MiB** | 341 fields at 256²; 85 at 512² | Same as A | Same as A | about 3.4% (est.); at 512², about 1 − 85/200 = 57% | Same as B |
| **D. Flow-field tiles** | about 9–15 / 18–30 MiB (est.), plus sector graphs | 3–5× (256²) to 6–11× (512²) fewer cells (est.), plus a coarse search | Limited to a coarse corridor | Like B if tiles suffice; pushed units wait for extra tiles | High |
| **E. Whole-map builds, paged storage** | Same as D, plus page tables (0.1 / 0.5 MiB) | Same as A, plus marking pages | Same as A inside the stored pages | Like B; units outside the pages wait for an extension | Medium |
| **F. 2 B per cell** | 32 / 128 MiB | Same as A | Same as A | Like B at 256² | Low |
| **G. Lease and queue tuning** | 12 / 48 MiB | Same as A | Same as A | still about 1 − 64/200 = 68% | Low |

- **C. Size the pool from a memory budget.**
  - *How it works:* the same code as B, with the capacity set to fill 64 MiB.
  - *Not chosen:* at 256² it spends 16 MiB more for no measured need, and at 512² it would quietly leave about 57% of unit-ticks on direct seek. `fieldCapacity` stays a lever up to the limit instead.
- **D. Flow-field tiles** ([Emerson][emerson]).
  - *How it works:* the map is split into sectors. A coarse search over the sectors picks a route, fields are built only for the sectors along it, and they are joined at sector edges.
  - *Gains:* it cuts both memory and build time. HPA*, a related method, is "up to 10 times faster" than A* ([HPA*][hpa]).
  - *Route quality:* bounded by the corridor. Hierarchical methods "usually return suboptimal paths" ([JPS][jps]), and HPA* gets "within 1% of optimal" only after smoothing ([HPA*][hpa]), which a field can't do. S3.1 allows 1.25.
  - *Crowds:* units pushed off the corridor need extra tiles built on demand, so crowds spend more time on direct seek.
  - *Work for the team:* sector graphs per class, a coarse search, joining at edges, tiles on demand, and graph updates when walkability changes. That is several Issues.
  - *Not chosen:* its main gain, build time, isn't the limit. 512² builds take 9.4 ms open and 14.4 ms blocked at the 95th percentile, under the 20 ms trigger. Tiles become the candidate if build time crosses its trigger.
- **E. Whole-map builds with paged storage.**
  - *How it works:* builds stay as they are now. On publish, the field keeps only fixed-size pages (for example 16 × 16 cells) along the routes from its group's start cells, plus a margin. The pages sit in a shared page pool, with a page table per field. A unit that reaches a missing page waits for an extension build.
  - *Gains:* memory is close to D, routes stay exact, and build time stays as measured.
  - *Unknowns:* how many pages an order needs, and how often crowds leave their pages. Nobody has measured either, and we found no published account of this method.
  - *Not chosen now.* It is the first candidate when a map goes over the limit (Triggers).
- **F. Store distances only (2 B per cell),** and work out directions from the eight neighbours when sampling, which takes 16 reads instead of 4. A third more fields per MiB isn't needed at 256² and isn't enough at 512². Not chosen.
- **G. Shorter leases, more build slots or queue priorities only.** The stream shows the share is about 1 − capacity ÷ goals in use, whatever the lease. Shorter leases only add rebuilds. Not chosen. `buildSlots` stays a lever for bursts.
- **H. A\* paths while fields are scarce.** It breaks ADR 0008 rule 1 ("Fields, not paths") with a second way to follow a route, and pushed units would need repair paths. Not chosen.

## Consequences

- **Benefits.**
  - The measured saturation goes away at the one-against-one map size, with no new machinery.
  - Routes stay exact.
  - Movement Issue 3 can start now against a fixed sampling function, and a later storage change stays behind it.
- **Tradeoffs.**
  - The pool takes 48 MiB at 256² instead of 12 MiB, allocated once at match start.
  - Maps are limited to 87,381 cells (about 295²) until a storage ADR raises the limit. That is below ADR 0008's 512² budget for four sides.
  - A walkability change queues a rebuild of every field with users (ADR 0008). With up to 256 such fields and two slots, the last rebuild could take about 4.3 s. Its group uses the old field meanwhile, and the static clamp and the move check keep units out of the new obstacle. The S9 Issue measures this.
- **Cost at 2,000 units.** These are estimates, which Issues 2 and 3 measure:
  - Sampling reads four values from each array per unit per tick, as ADR 0008 planned.
  - Builds and publishing cost the same per field as before.
  - The scan for a field to replace covers 256 fields, at most twice per tick.
  - There is no GPU cost and no allocation.
- **Risks.**
  - The spike's PC isn't reference-class, and its chain was throwaway. Builds at 256² have about seven times headroom to the 20 ms trigger, so the decision doesn't depend on exact build times.
  - With real movement, units that arrive stop counting as moving, so the share can be a little higher than in the held-still test. Issue 5 checks the stream again with the full chain.
  - Real play may use more than 256 goals at once. ADR 0008's turn-taking then applies, and the overlay counts it.
- **Reversibility.** Easy. The capacity is one setting. A different storage changes the pool code and the reader's internals, not movement (Sampling).
- **Triggers for a superseding storage ADR.** These replace ADR 0008's tiles trigger.
  - **A larger map is planned,** with more than 87,381 cells. Before it is built, a spike (two days) measures paged storage (E) at that size: pages per order on the stream and in the four crowd scenes, extension builds per minute, and memory. An ADR then picks E, D or another storage.
  - **The 5% target fails.** On an allowed map, the stream or a full AI-against-AI match leaves more than 5% of moving unit-ticks on stopgap direct seek after the levers.
  - **Builds get slow.** On reference-class hardware, builds take more than 20 ms at the 95th percentile on the largest allowed map, or more than 1% miss their due tick at `buildLatencyTicks` 2. Tiles (D) are then the candidate, because only they cut build time.
- **Levers.** These replace ADR 0008's last lever. They are tuning and need no new ADR:
  - `fieldCapacity`, from 200 up to the 64 MiB limit
  - `buildSlots`, from 2 to 4. More slots shorten bursts but take workers from the chain, so a change shows its ladder entry.
  - `minLease`
  - `buildLatencyTicks`
- **ADR 0004.** No part of it is superseded. The capture that rule 11 asks for still applies, and the whole-window check adds to it.
- **Dependencies and owner decisions.** No package, no Unity change and no handbook change, and no owner decision is needed. The map limit matters to the maps section of the GDD, which hasn't been written yet.
- **Follow-up Issues.** ADR 0008's Issues 2 to 5 change as follows. Everything else in them stays. "The four scenes" means ADR 0008's spike scenes: the ladder scenario, 200 units through a 3-cell gap, dense contact and wall contact.
  2. **Static maps and flow fields.**
     - *Scope:* `fieldCapacity` 256, the match-start checks, dropping requests for empty groups, and passability and builds anchored as in Sampling point 3. It fills the storage layout above. It doesn't create or edit the three Sampling types before Issue 3 merges.
     - *Tests:* ADR 0008's list, where "65 goals with 64 fields" becomes `fieldCapacity` + 1 goals at a small test capacity. Also the stream test above, at most 5%, and a match-start check that fails for a 296² map and passes for 295².
     - *Measurements:* the same as #141 but on its own build job:
       - build median and p95 at 256², open and 20% blocked
       - missed due ticks during the stream
       - for the stream: the wait (median, p95 and maximum), replacements per second and the stopgap share
       - pool and scratch memory in MiB
       - `PublishFieldsJob`'s time in the chain
       - its ladder entry (ADR 0004)
  3. **The 30 Hz movement tick.**
     - *Scope:*
       - It creates the three Sampling types and their tests. Until Issue 2 merges, every group's field is `NoField`, so units move by direct seek.
       - It builds the move check's cap from the start. That is `maxSerialFallbacks` with its rollback set, an ADR 0008 lever. ADR 0008's Budget applies the cap once the spike measures more than 0.5 ms, and the spike measured 1.9 to 5.0 ms. The first round runs in parallel, and later rounds check only units that could touch a unit that fell back in the round before.
       - Measurement sets the cap's default so that the serial rounds' p99 is at most 0.5 ms at 2,000 units in the four scenes. Start from 32.
     - *Tests:*
       - Sampling points 2 to 7: a value read at its own point, a blend halfway between values, an unreachable value skipped, all four unreachable, a position outside the map, directions that cancel, a class-1 value read at its cell corner, and `NoField`.
       - Two parallel Burst jobs sharing one reader's arrays, with safety checks on and no error.
       - With a cap of 0, a queue that forms a chain adds no footprint overlap, and its rollback set stalls for one tick.
     - *Measurements:*
       - the whole-window method above
       - the chain's wall time, per job and in total
       - the serial rounds: median, p99 and maximum, fallbacks and the longest chain, at 1,000 and 2,000 units in the four scenes
       - its ladder entry
  4. **Groups and arrival.** *Measurements added:*
     - #141's stall and arrival measurements, repeated with pace and slots in the four scenes: units standing still for more than 5 s with somewhere to go, group stalls, overflow units, and the S2.2 to S2.4 checks
     - the whole-window method
  5. **ORCA and making way.** *Measurements added:*
     - the four scenes at 1,000 and 2,000 units with the full chain, tuning ADR 0008's levers before any avoidance trigger counts. The spike's throwaway chain triggers nothing.
     - the stream at 256² with the full chain, with a stopgap share of at most 5%
     - the whole-window method
  - **Suggested:** add rules 10 to 12 to the gameplay-programmer and code-reviewer role files, together with ADR 0008's follow-up 6. These are protected paths, so that Issue must name them.

[emerson]: http://www.gameaipro.com/GameAIPro/GameAIPro_Chapter23_Crowd_Pathfinding_and_Steering_Using_Flow_Field_Tiles.pdf
[hpa]: https://papersdb.cs.ualberta.ca/~papersdb/view_publication.php?pub_id=585
[jps]: https://ojs.aaai.org/index.php/AAAI/article/view/7994
[haa]: https://doi.org/10.1109/CIG.2008.5035648
[readonly]: https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Unity.Collections.NativeArray_1.ReadOnly.html
[recorder]: https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Unity.Profiling.ProfilerRecorder.html
[sum-samples]: https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Unity.Profiling.ProfilerRecorderOptions.SumAllSamplesInFrame.html
[marker]: https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Unity.Profiling.ProfilerMarker.html
