# Crowd movement for a large-scale RTS

**Technical research report · 31 October 2026**

**Scope:** Unity 6.3 LTS, URP, Burst/Collections/Mathematics, 30 Hz simulation, custom grid navigation, 2,000 units initially. No NavMesh, physics engine, or movement colliders. The recommendations preserve those constraints.

## Evidence and confidence

- **[D] Shipped and documented:** developer descriptions, release notes, or published implementation. Applies only to the named game/version; software documentation is identified separately.
- **[R] Published research:** what a paper establishes under its stated assumptions, not a shipping or performance guarantee.
- **[I] Inference:** calculations from your brief or architectural recommendations. Geometric calculations have high confidence under the stated shape assumptions; implementation recommendations require testing.
- **[S] Speculation:** an unvalidated combination or timing estimate.
- **[B] Brief:** your measurements and requirements, accepted as reported; this research did not run or inspect your simulation.
- **[A] Anecdote:** firsthand player/contributor testimony; limited evidence beyond the described experience.

Labels apply to the paragraph, list item, or table row they introduce. Links identify supporting sources; recommendations drawn from them remain explicitly labelled inference.

## 1. Summary: recommendations in priority order

1. **[I] Audit the move validator before implementing ORCA.** A small unit advances 0.1333 cells per tick, exceeding the packed formation's 0.125-cell edge clearance. Checking a proposed pose against neighbours' old poses can reject a perfectly safe common translation. Validate the final set of accepted motions, including units that remain stationary.
2. **[I] Keep shared flow fields and exact collision rejection. Add a separate progress controller.** Your reported 0.6 ms movement chain is a valuable baseline. Local steering should propose motion; safety should certify it; coordinated yielding, reservations, and recovery should resolve persistent blocking.
3. **[I] Treat packed rotation as a clearance problem.** For the stated square geometry, contact begins at 36.87°. A 23° turn may help; 42° still overlaps. Any-angle routes can improve the case, but cannot generally resolve packed rotation or merging.
4. **[I] Build reachable destination regions with exclusive slots early.** Correcting an unreachable click to one outside cell does not create enough parking capacity. This is particularly relevant to the closed-ring test.
5. **[I] Use explicit traffic rules at corners and gaps.** Preserve lane order, alternate merging streams fairly, reserve downstream space, and allow temporary retreat. Add local cooperative planning when ordinary steering cannot resolve a conflict.
6. **[I] Make formations elastic during travel and stable at arrival.** Use a common route for mixed groups, pace feedback, and persistent slot ownership. Restore the compact block after a chokepoint; do not require units to assemble before obeying.
7. **[I] Benchmark ORCA as an optional improvement to ordinary avoidance.** Its collision theorem is conditional, its dense-case fallback can violate constraints, and it has no general progress guarantee. A closely related navigation/ORCA/cooperative-recovery pipeline already exists in research. ([S25], [S35])

**[I] Recommended architecture:** shared static routing → group/portal policy → preferred velocities → local avoidance → joint motion validation → commit. Rejected movement feeds a persistent blocker graph and a bounded recovery planner. A timeout records failure or triggers recovery; it does not turn a stuck unit into a successful arrival.

## 2. What to keep, change, and add

### Keep

**[I] Keep the data-oriented simulation, fixed tick, preallocated arrays, and rendering interpolation.** Keep the measured flow-field implementation while investigating execution. A global routing rewrite is unlikely to address a local space conflict by itself.

**[I] Keep size-specific traversability and direct seeking.** Verify that both use the same footprint model as the final validator. Centre-only visibility, diagonal corner cutting, and interpolation across blocked corners can otherwise invalidate a seemingly correct route.

**[I] Keep the no-growth overlap invariant as a backstop.** Starting from zero overlap, a correctly implemented pairwise invariant preserves zero overlap at the validated states. It does not remove pre-existing overlap, establish safety between ticks, or establish eventual arrival.

### Change

| Existing or planned choice | Recommended change and reason |
|---|---|
| **[I] Reject a candidate move independently** | Audit synchronous motion and rejection dependencies. A convoy needs neighbours to move together; a rejected leader must invalidate dependent follower proposals. |
| **[I] Settle after five seconds without progress** | Distinguish arrived, queued, recovering, and blocked. Trigger recovery before the deadline; record unresolved cases as failures. |
| **[I] One corrected goal cell** | Keep its reachable component, but generate enough valid slots around an arrival region. Reject or reshape impossible layouts explicitly. |
| **[I] Slowest-member speed cap** | Add feedback from route progress and predicted arrival times. A constant cap cannot compensate for queues or differing route lengths. |
| **[I] Rigid block while travelling** | Use an elastic corridor formation; preserve order through constrictions and restore shape only where space permits. |
| **[I] ORCA with generic share rules** | Separate friendly reciprocity, immovable holds, idle yielding, and hostile occupancy. Persist passing-side decisions and retain exact safety checks. |

### Add

**[I] Add deterministic spatial indexing and diagnostics.** Maintain sorted cell buckets, stable neighbour ordering, blocker IDs, rejection reasons, progress age, slot ID, and portal ownership. Count routing failures separately from unit contacts, rotation conflicts, invalid slots, and capacity queues.

**[I] Add a common corridor for mixed groups.** Choose the route using the most restrictive member, then allow smaller units to use compatible lateral space. Independent size-class fields can send one group around opposite sides of an obstacle. AoE IV documents a related extended-flow approach. ([S02])

**[I] Add bounded, preallocated recovery storage.** The normal path should remain cheap. Escalate persistent conflict components to a local space-time planner; reserve yielding and exit space before executing its first move.

### Make the acceptance criteria mathematically testable

**[I] Some requirements cannot hold for every map and occupancy.** A unit surrounded by held units cannot move immediately. A completely occupied one-lane corridor may have no passing schedule. The required guarantee must name the permitted starting configurations and available free space; this is a feasibility condition, not an excuse to accept jams.

| Requirement | Necessary definition or qualification |
|---|---|
| **[I] Every unit starts within 0.1 s** | Measure physical displacement within three ticks of command application in a feasible fixture. Separately measure input/network latency. Turning animation or jitter is not progress. |
| **[I] Compact arrival within 2× footprint** | Specify a formation-aligned enclosing rectangle, or another explicit region. World-axis bounds change when the same block rotates. |
| **[I] Mixed arrivals within 3 s** | Specify initial formation, common route, available destination slots, and queue conditions. |
| **[I] No movement 2 s after arrival** | Start timing at first legal geometric entry into the assigned capture tolerance, independently of the arrived flag. Test displacement and state transitions. |
| **[I] Routes ≤1.25× shortest** | Distinguish static planned route length from executed distance, including yielding/retreat. Define the same footprint and obstacle semantics for the reference shortest path. |
| **[I] No individual waits over 5 s** | Bound queue length and corridor occupancy. Static reachability alone does not imply immediate space-time availability. |
| **[I] Idle yields within 2 cells of home** | A legal parking place and connecting motion must exist within that limit. Otherwise traffic must wait, reroute, or report blocking. |
| **[I] Attack-move depth ≤2×** | Define depth along the route and the combat/constriction policy. Narrow passages and front-line combat may make the bound infeasible. |
| **[I] Stop/hold** | Stop must cancel queued/recovery motion within 15 ticks. Hold must prohibit translation and rotation if “never moves” includes facing. No yielding system may override it. |

**[I] Preserve the strict thresholds in the feasible benchmark fixtures.** Also include deliberately impossible fixtures, where correct results are zero collisions and a truthful blocked status, not invented successful completion.

**[I] If 0.1 s means player-click-to-visible-motion, input buffering, network delay and interpolation must also fit inside it.** Passing a three-tick simulation-only test would not establish that stronger end-to-end requirement.

### Performance and determinism

**[B/I] Your 0.6 ms p99 chain at 2,000 units normalizes to 0.30 μs/unit/tick.** That is aggregate elapsed time divided by unit count, not the cost of an independent unit update. Record both critical-path elapsed time and total worker CPU time. The RTX 3050 identifies rendering hardware; a CPU model, compiler/package versions, and workload are still needed for movement comparisons.

**[I] Use 2.0 ms p99 total movement time at 2,000 units as a provisional experiment budget**, subject to the architect's complete frame budget. It permits 1.4 ms of additional work over the reported baseline. This is a proposed gate, not a prediction. Measure frame-time spikes, background field work, recovery spikes, and catch-up ticks separately.

**[I] Field count matters alongside unit count.** With 4-byte integration, 1-byte direction, and 1-byte flags, one example layout costs 0.375 MiB at 256² or 1.5 MiB at 512², excluding shared costs and search scratch. Fifty resident 512² fields would consume 75 MiB. Reuse equivalent requests, retire obsolete orders, and introduce tiles/portals only when order churn or larger maps justify them.

**[D, software documentation] Burst added FloatMode.Deterministic support in 1.8.25.** Current documentation describes consistent results on supported 64-bit platforms, with restrictions involving non-Burst inputs, function pointers, intrinsics, and NaNs. Older documentation saying the mode is unsupported is stale. ([S36], [S37])

**[I] Pin the exact package/compiler and validate replay hashes.** Stable tie-breaking, neighbour order, fixed-point or controlled floating-point geometry, and defined overflow/rounding remain necessary. Never decide simulation work from wall-clock time. Background field results must become visible on predetermined simulation ticks or deterministic completion barriers; publishing whenever a job happens to finish can desynchronize lockstep. Bound recovery by node expansions or candidates, not milliseconds.

## 3. Method comparison

**[I] Cost model:** N = units, k = relevant local neighbours, G = grid cells, F = active fields, I = projection iterations, H = reservation horizon, M = agents in a recovery component. Costs below are operation-count models inferred from the methods, not measured Burst timings. Spatial lookup is additional; dense cells or unbounded neighbourhoods can make local work quadratic.

| Method and evidence | Per-unit, per-tick work | Additional memory | Determinism and Burst/job fit | Helps which requirements? | Principal risks |
|---|---|---|---|---|---|
| **[B/I] Shared Dijkstra field** | O(1) sample; event build roughly O(G log G), shared | O(FG), plus search scratch | Excellent sampling; dependency-heavy integration; stable heap ties | Cheap routing and broad responsiveness | No crowd capacity, distinct arrivals, or progress guarantee |
| **[R/I] ORCA / RVO2** [S25], [S26], [S27] | O(k) constraints plus LP; expected-linear randomized theory, possible superlinear fixed-order solve/fallback | O(k) scratch per active solve; O(Nk) if retained | Independent solves fit jobs; numeric and tie discipline required | Anticipatory crossing and ordinary avoidance | Dense infeasibility, symmetry, cap omissions, nonreciprocal units, disc/square mismatch |
| **[R/I] Continuum crowds** [S28] | O(1) sample/splat; dynamic potentials roughly O(FG log G) per rebuild | Several O(FG) arrays | Sampling parallel; ordered accumulation and field propagation harder | Large common-goal streams, congestion and lanes | Expensive many-goal updates; individual clearance not exact |
| **[I] Congestion-aware shared fields** [S15, S28] | O(1) local contribution/sample, plus scheduled global/coarse updates | O(G) density plus fields | Integer accumulation and deterministic staged updates fit | Route distribution, overloaded corners | Herding between routes, stale costs, detours beyond 1.25× |
| **[R/I] Boids/prioritized steering** [S29] | O(k) | O(N), shared bins | Excellent; fixed ordering for sums | Separation preferences, cohesion, arrival slowdown | Force cancellation, oscillation, local minima; no hard guarantees |
| **[R/I] Social forces** [S30] | O(k), possibly multiple integration substeps | O(N), shared bins | Parallel-friendly; nonlinear arithmetic needs discipline | Natural spacing and emergent opposing streams | Tuning, numerical stiffness, contact penetration, weak command semantics |
| **[R/I] Position-based crowds** [S31] | O(Ik), often repeated pair processing | O(N+Nk) with pairs/double buffers | Jacobi parallel; ordered Gauss–Seidel more dependent | Dense rearrangement and coherent candidate poses | Finite iterations need not eliminate overlap; projection can violate speed/hold |
| **[I] Explicit lanes/portal reservations** [S33] | O(1) assigned-lane follow; arbitration by contenders | O(N+PH), P local portal resources | Serial arbitration, parallel following; integer ticks | Gap throughput, fair merging, opposing traffic | Capacity waits, exit blocking, lane transitions; no universal deadline |
| **[R/I] Theta*/LOS smoothing** [S32] | O(1) segment follow; event search plus LOS checks | Search O(G), cached waypoints | Independent searches; deterministic predicates | Shorter/smoother static routes and corners | No multi-agent scheduling; Theta* is not exact shortest path |
| **[R/I] Local cooperative recovery** [S33], [S34], [S35] | Mostly counters normally; conflict-dependent search when triggered | Roughly O(local G × H), plus agent constraints | Components parallel; deterministic bounded search | Escape from persistent jams, temporary retreat | Incomplete bounded searches; graph proofs do not transfer automatically |
| **[I] Recommended combined system** | O(k) ordinary work plus rare recovery and shared builds | Bins, fields, slots, blocker graph, bounded planner | Fits custom jobs; ordered commits | Covers all requirement categories in feasible fixtures | Integration correctness and p99 cost must be demonstrated |

**[I] Practical operation counts:** at N=2,000 and k=12–24, one neighbour pass visits roughly 24,000–48,000 directed pairs; six projection passes revisit roughly 144,000–288,000. These are not equivalent operations or time estimates. Safety checks must examine every potentially colliding pair even if expensive steering uses a capped list.

## 4. Question 1 — What shipped games actually document

### StarCraft II

**[D] Blizzard explicitly uses allied push priorities.** Patch 5.0.14 links the Ultralisk priority change to getting stuck behind friendly units; 5.0.15 increases Thor and Siege Tank priorities for crowded formations. Blizzard's control guide also acknowledges separation caused by speed and terrain, and friendly blocking during focused pursuit. These sources support intentional yielding policies, not automatic mixed-speed cohesion. ([S04], [S05], [S06])

**[D] Anhalt's GDC 2011 slides separate planning, steering, and collision.** They show constrained Delaunay navigation, allocation-free A* and funnel smoothing; Boids-inspired steering; gap selection and coordinated turns to suppress hallway oscillation; and bounded fixed-point collision/pushing of circular units. The relevant video slides were inspected directly, approximately 03:54–14:54. They do not establish ORCA, exact non-overlap, or liveness. This architecture is informative, but your project should retain its custom grid rather than adopt its mesh/physics implementation. ([S03])

### Supreme Commander 2

**[D] Emerson describes tiled cost/integration/flow fields, portal routing and caching**, LOS integration, wall-adjacent cost gradients, obstacle expansion for large units, restrictive movement types for mixed groups, and blended directions. Crucially, the system also uses unit pushing and wall sliding through its physics integration. Its motivating problem was repeated blocking along rigid individual paths in the earlier Supreme Commander. This is a strong precedent for shared routing plus flexible execution; its physics layer does not satisfy your engine constraints or establish exact non-overlap. ([S01])

### Planetary Annihilation

**[D, developer recollection] PA programmer Forrest Smith describes the pathfinder as based on voxels and flow fields.** This supports the high-level architecture. His later comments qualify recollections about update scheduling; they do not document final corner arbitration, formation assignment, or collision guarantees. Pre-release demonstrations and large army counts are not substitutes for those missing details. ([S09])

### Age of Empires II: Definitive Edition

**[D] Official patches expose the importance of formation policy.** Update 153015 adds escape movement that prioritizes moving over assembling, alongside fixes for regrouping, freezes and obstruction behaviour. Update 185872 improves slot distribution, initial movement toward the destination, and formation-centre commands that previously caused shuffling. It explicitly discusses combat overlap/stacking thresholds. Thus its movement feel is not evidence for your stricter zero-overlap contract. The sources do not reveal a complete current pathfinder implementation. ([S07], [S08])

### Age of Empires IV

**[D] Cheng's GDC 2022 presentation documents flow fields plus steering for a 1,600-unit design context.** It uses Fast Marching Method gradients, 256 stored directions, LOS integration, a portal graph, and overlapping flow segments. Mixed groups use flow extended from their most restrictive movement class. A virtual formation leader generates slots; members follow the flow when a slot is not visible. Moving-unit, immobile-unit and small-obstacle avoidance are separate concerns. The slides do not identify ORCA or prove collision-free liveness. Historical timings lack enough context to predict your Burst cost. ([S02])

### Company of Heroes

**[D] Jurney describes stable hierarchical squads in the original game.** Followers seek offsets from predicted leader positions; speed feedback controls longitudinal spacing. Small searches project formation targets around obstacles, terrain changes the formation shape, and hysteresis avoids excessive retasking. Arrival uses reserved individual destinations. These are directly relevant ideas, but small squads are not evidence of 2,000-unit performance. ([S10])

**[D] CoH3 provides a separate, later implementation lesson:** Relic distinguishes grid A* from vehicle turn plans. Its developer explains that road preference and wide, fast turns could produce tactically bad detours or “donuts.” Algorithmic travel-time optimization can conflict with the player's intended motion. Do not retroactively assign CoH3 internals to CoH1. ([S11])

### Total War

**[D] Rome II's official manual documents locked groups preserving formation.** A Warhammer II developer interview describes parallel battle processing and emphasizes map connectivity: visually connected streets can be disconnected in the navigation representation. These are documented features and failure causes. ([S12], [S13])

**[I, evidence boundary] This research did not establish the series' exact local crowd solver or slot-matching algorithm from primary evidence.** Regimental soldiers also differ from independently selected RTS units; headline entity counts are not comparable per-unit benchmarks.

### They Are Billions

**[D] Numantian's v0.8.2 update says each unit computes its own path, refreshed every few seconds.** It describes reducing broad invalidation by reacting only to relevant changes. Infected can treat destructible walls differently from permanent barriers. This does not exclude shared internal data, but it does not document the often-assumed shared-flow-field implementation. Local avoidance, overlap tolerance and arrival assignment remain unspecified. ([S14])

### Beyond All Reason / Recoil

**[D] BAR's 2021 pathfinding update parallelizes whole requests**, deferring cache and heatmap mutations to avoid desynchronization; the heatmap influences opposing streams. In 2023, an overhauled QTPFS became the default, with adaptive resolution and fixes addressing zigzags, overly long paths, trapped units and building penetration. These are version-specific release descriptions. ([S15], [S16])

**[D] Recoil's maintainer changelog describes assigning nearby appropriate formation targets to reduce crossing.** An issue records a unit becoming stuck while pushing another against a building; it is evidence that such a failure existed, not proof that it remains unresolved. The GPL engine is useful for ideas only. ([S17], [S18], [S38])

### 0 A.D.

**[D, published implementation] The versioned 0.28 motion source uses fixed-point positions, spatial subdivisions, weighted pushing, pressure-based slowdown and perpendicular responses to crossing.** Its comments acknowledge imperfect overlap handling and remaining bog-down cases; formations receive special treatment. This is practical heuristic engineering, not strict nonpenetration or a liveness proof. The GPL source must not be copied or transliterated into your implementation. ([S19])

**[D, historical design] Its older pathfinder document describes long-range grid A*/JPS and short-range vector search**, but explicitly mixes implemented and planned features. It cannot establish the current routing implementation. ([S20])

### Original Command & Conquer, Red Alert, and Red Alert 2

**[D, published original source] Tiberian Dawn's pathfinder describes LOS advancement and clockwise/counterclockwise obstacle-edge following.** Its drive code classifies blockers, requests friendly scattering, and reserves movement destinations. Red Alert's movement source includes formation offsets and nearby correction of blocked goals. These are historical precedents for explicit execution rules; they are not modern crowd guarantees. EA's releases are GPL, so use ideas only. ([S21], [S22], [S23], [S38])

**[D, narrow scope] Louis Castle's Tiberian Sun interview describes ignoring moving allies during routing, nudging stationary friendly blockers, and bridge shuffling.** He links Red Alert 2: Yuri's Revenge to those innovations, but this does not document RA2's exact implementation. RA1, OpenRA and Tiberian Sun must not be presented as verified RA2 internals. ([S24])

## 5. Question 2 — What the methods establish

### ORCA and RVO2

**[R] ORCA selects a velocity from pairwise permitted half-planes.** Its reciprocal, holonomic model supplies sufficient conditions for collision avoidance when agents can choose compatible feasible velocities. In dense conditions the intersection can be empty; the paper's fallback minimizes violation and explicitly loses the collision guarantee. This is not a guarantee of reaching goals. ([S25])

**[D, reference implementation documentation] RVO2 warns that too few neighbours or too small a search distance makes simulation unsafe.** A longer horizon reduces manoeuvring freedom and increases relevant neighbourhood size. Current RVO2 C++ and C# repositories use Apache-2.0; the C# version is not automatically an allocation-free Burst implementation. ([S26], [S27])

**[I] Use ORCA for candidate velocity generation, with these adaptations:**

- Give moving friendlies complementary pair shares; both must agree on responsibility.
- Represent holds as immovable occupancy. The moving unit must not expect half the correction from them.
- Let idle units yield only after reserving a reachable refuge inside the home limit.
- Predict moving enemies without assuming cooperation. No avoidance method can guarantee a feasible escape from arbitrary hostile motion.
- Persist a right-side passing preference across ticks, but allow a deliberate override when that side is blocked.
- Do not add cohesion or clamp acceleration after solving without revalidating: the result may leave the permitted velocity region.

**[I] Square footprints require special care.** A circumscribed disc for a 0.5-cell square has radius 0.3536; two such discs need 0.7071 spacing. Your 0.625-spaced squares therefore start “overlapping” in conservative disc ORCA. Radius 0.25 fits the spacing but misses square corners. Choose the collision model explicitly: conservative discs with room to expand, shape-aware constraints, or approximate steering followed by exact square validation.

### Continuum, congestion, forces, and position projection

**[R] Continuum Crowds combines density, velocity and travel costs in dynamic potentials**, producing coherent streams and lanes. Its full paper adds pairwise separation correction and explicitly says that this does not strictly preserve minimum distances after other pairs move. It is not an exact-contact solution. ([S28])

**[I] Prefer a coarse congestion overlay to rebuilding every order field each tick.** Update on fixed ticks, smooth costs over time, retain route choices with hysteresis, and constrain added detours. Density should charge occupied footprint area, so a large square contributes four small squares' area. Avoid treating the group's own moving occupancy as an impenetrable wall.

**[R] Reynolds describes composable separation, alignment, cohesion, avoidance and arrival behaviours.** Helbing–Molnár models desired velocity and interpersonal/boundary forces. Both provide useful behaviour models, not your combined safety/progress contract. ([S29], [S30])

**[I] Use explicit priorities instead of summing all desires indiscriminately.** A strong centre-seeking force can defeat a lane decision; separation can cancel forward movement at a corner. Explicit lanes are more predictable than relying on emergent pedestrian behaviour.

**[R] Position-based crowds iteratively project candidate positions.** Weiss and colleagues explicitly acknowledge that iterations may not converge to the constraint manifold. Their large-crowd results use CUDA on a GT 750M, a 1/48 s timestep and multiple solver iterations; those results are not CPU/Burst timing evidence. ([S31])

**[I] Projection is worth testing as a dense-component proposal generator.** Keep hold units fixed, bound displacement by speed, check obstacles, and certify the entire result. Neither a fixed iteration count nor a final velocity clamp makes it exact.

### Any-angle paths and cooperative planning

**[R] Theta* finds short any-angle routes but does not guarantee the true shortest path.** Its search and LOS semantics concern static geometry; they do not allocate space among agents. ([S32])

**[I] First test LOS shortcutting of the existing field route.** Cache a visible corner/waypoint per group, with per-unit footprint validation and fallback to the field. Use inflated obstacles or equivalent swept-footprint tests; a clear centre ray is insufficient. Do not blindly interpolate directions across obstacle boundaries or average opposite routes around a wall.

**[R] Cooperative A* plans in space-time; windowed variants limit lookahead.** Prioritized/windowed planning is not generally complete. PIBT's finite-time theorem requires a graph where adjacent vertices lie on suitable cycles; a one-cell dead end, mixed-size occupancy or permanent holds cannot inherit that theorem. ([S33], [S34])

**[R] Theta* + ORCA + local Push and Rotate is already published.** Its cooperative component reduces deadlocks in narrow passages experimentally. It supports selective coordination, without proving your square-footprint implementation or five-second deadline. ([S35])

## 6. Question 3 — Diagnose and resolve the measured jams

### 6.1 Isolate artificial blocking from real geometric blocking

**[I, arithmetic] At 30 Hz, a small unit moves 4/30 = 0.13333 cells.** Initial surface clearance is 0.625 − 0.5 = 0.125. If each candidate is tested against the forward neighbour's tick-start pose, the full step penetrates that old footprint by 0.00833 cells. Yet if both move identically, their separation never changes.

**[I] This is a diagnostic hypothesis, not an observed bug.** Run a straight convoy with frozen orientation and identical velocities. Shuffle IDs. A front-to-back serial validator may succeed while the reverse order fails; independent old-pose checks may release only the front row. Arbitrary unit storage order must not determine whether a physically feasible convoy moves.

**[I] Joint validation needs rejection closure.** Checking all proposed endpoints is insufficient if one proposal later fails and that unit stays behind. Recompute dependent conflicts, or validate and commit a connected component atomically. Include outside units and obstacles; a fallback that creates new dependencies must expand/revalidate the component. A safe fallback can stop it; a progress controller must then propose another manoeuvre.

**[I] Validate relative motion through the tick.** Endpoint separation does not prove separation during translation, rotation, or visual interpolation. For translation use relative swept tests; for rotation use conservative bounds or subdivision with a proved geometric error bound, not arbitrary finite samples. The renderer must follow the certified trajectory. A swept volume treated as permanently occupied can over-reject a safe following convoy: time correspondence matters.

### 6.2 Answer to the any-angle question

**[I, geometry] Assume equal square footprints of side a=0.5, identical yaw θ, and centres separated horizontally by d=0.625.** Separating-axis testing gives centre offsets d cos θ and d sin θ in the squares' axes. They overlap when both magnitudes are below a. Between 0° and 45°, contact begins at:

**θ = arccos(a/d) = arccos(0.8) = 36.87°.**

| Shared yaw with centres unchanged | Result for that pair |
|---|---|
| **[I] 23°** | Separated |
| **[I] 36.87°** | Touching |
| **[I] 42°** | Overlapping |
| **[I] 45°** | Overlapping; spacing must reach at least 0.7071 |

**[I] Therefore, any-angle routes could free part of the formation.** They can avoid some unnecessary rotations and reduce the number of conflicts. They cannot make the 42° arrangement legal. Smoothing a rotation still traverses forbidden angles unless centres also rearrange.

**[I] Distinguish three operations:** changing a common translation velocity preserves relative positions; rotating bodies at fixed centres changes their footprints; rotating both bodies and formation offsets preserves internal geometry but requires external swept clearance and faster travel by outer units. The jam is not intrinsically caused by all units changing travel direction together.

### 6.3 Packed block: release space before demanding the turn

**[I] Try these in increasing complexity:**

1. Keep current facing while a safe translation advances the block, if vehicle design permits this motion.
2. Expand or shear the local block before rotating; release boundary rows into free space and permit interior units to follow simultaneously.
3. Split travel into ordered strips that negotiate the corner, then restore the formation.
4. If ordinary candidates fail, jointly plan a small retreat/rearrangement for the blocked component.

**[I] Do not require every intermediate move to reduce distance to the goal.** Sideways movement and short retreat may be essential. Retain the no-overlap invariant; relax monotonic goal progress, not collision safety.

### 6.4 Walls, merging streams, gaps, and opposing groups

**[I] At walls, preserve tangential motion.** Project preferred movement along the wall, target a clearance-offset waypoint beyond the corner, and avoid dragging every unit through the same corner point. Use a small wall-clearance cost without falsely closing narrow valid gaps.

**[I] At merges, assign a deterministic admission order.** Alternate source streams, age waiting requests, and preserve within-stream order. Grant admission only if the exit has space for the complete footprint. Reserve both the throat and downstream clearance; otherwise a locally successful entry can block everyone behind it.

**[I] In the 3-cell gap, compute usable lane count from actual footprints and clearance.** Six 0.5-cell columns fit only at exact zero margin; rotation, wall clearance, or lane separation reduces that count. Handle large units as wider reservations. For opposing flows, dedicate directional lanes where feasible; otherwise alternate bounded batches and drain the gap before reversing direction.

**[I] For two groups crossing in open ground**, persistent right-side choices and longitudinal following should reduce oscillation. If a locked formation obstructs the crossing, dissolve travel slots temporarily. A right-turn rule alone cannot solve a four-way cyclic blockage or create missing passing space.

**[I] Fairness requires assumptions.** If a portal serves an available lane every h seconds, a contender with q units ahead waits on the order of qh, plus clearance delays. Age-based fair admission prevents starvation of persistent feasible requests under finite demand, bounded service, and a draining exit; it cannot impose a five-second bound on arbitrary backlog.

### 6.5 Deadlock detection and conditional progress

**[I] Record who blocks whom when a candidate is rejected.** Detect persistent strongly connected components, and also chains ending at an immovable blocker. Use route-coordinate progress over a window; tiny oscillations should not reset the timer. Queue waiting, combat and unreachable goals need separate reasons.

**[I] Proposed recovery ladder:** after roughly 0.3–0.5 s without meaningful progress, latch a passing side or alternate candidate; after roughly 1 s, reserve an explicit yielding manoeuvre; by roughly 2 s, invoke local cooperative search or replan the corridor. These are tuning starting points, not proven deadlines.

**[I] Conditional liveness can be established for a restricted scheduler:** finite demand, compatible footprints, reachable exits, sufficient yielding space, fair eventual admission of every persistent feasible request, bounded service, and no unresolved reservation cycles or permanent blockers. A finite conflict-free plan completes if its environment remains valid and granted manoeuvres execute. Finding such a plan with bounded local search is not guaranteed. PIBT and related graph results retain their own assumptions. ([S33], [S34], [S35])

### 6.6 Most likely fixes for the two failures

| Case | Highest-value interventions | Why |
|---|---|---|
| **[I] Wall: 26/110 pass** | Validator audit; coordinated packed release; corner clearance; fair merge admission | Addresses the initial rotation/translation blockage and the subsequent converging streams separately |
| **[I] Ring: 14/100 arrive** | Same release fixes; reachable outside arrival region; distinct slots; keep corner exits clear | A corrected single goal remains a congested sink; early arrivals must not occupy later units' access |

**[I] Neither diagnosis proves the existing implementation's cause.** Instrument the rejection categories and change one mechanism at a time. Treat “110/110 and 100/100 reach valid final poses” as success; five-second settling is not an alternative completion condition.

## 7. Question 4 — Groups, slots, and arrival

### Assignment

**[R] Dense minimum-sum assignment can be solved by a cubic-time Hungarian method.** A dense cost matrix requires quadratic storage. This optimizes the chosen assignment cost, not collision-free execution or the last unit's arrival time. ([S40])

**[I] Start with deterministic order-preserving assignment.** Project units and slots onto the travel/lateral axes, sort into rows or bands, then match within bands. This is roughly O(n log n) before local improvements. Naive nearest-free-slot greedy assignment is O(n²) and can strand the last unit with a poor slot. Add bounded pair swaps; use exact matching for small groups as an offline quality reference.

**[I] Penalize route distance, crossing, incompatible size, and changing an existing assignment.** For synchronized arrival, consider maximum arrival time or arrival spread as well as total travel. A sum-optimal assignment can still give one unit a very long trip. Reserve large-unit slots first, then pack small units into valid remaining regions.

### Travel, constrictions, and mixed pace

**[I] Maintain travel order separately from final slot identity.** Use a virtual group progress coordinate and lateral preferences. Before a constriction, narrow into lanes without demanding a full reassembly; after the last constriction, restore rows in a reserved expansion area. Avoid swaps that force units to cross through each other.

**[I] A common mixed-group route can violate the small units' individual 1.25× limit** when only they fit through a shortcut. Define whether the reference is the common legal group route or each unit's own shortest route; if it is the latter, some maps require temporary splitting or conflicting requirements remain.

**[I, arithmetic] Fifty cells takes 12.5 s at 4 cells/s and 20 s at 2.5 cells/s.** A shared 2.5-cell/s pace removes that nominal 7.5-second difference only for equal routes without queues. At that pace, 7.5 cells of extra travel consumes the entire three-second tolerance.

**[I] Slow the group anchor when required slow units fall behind; let faster units catch up within their actual speed limits.** Do not stop everyone for every tiny slot error. Coordinate destination access rather than letting early units seal the front row. On attack-move, brake leading marchers as longitudinal span approaches twice its initial depth; let the rear catch up. Define separate combat/constriction behaviour when that bound is infeasible.

### Compactness and stopping the dance

**[I, arithmetic] One hundred small squares occupy 25 cells².** A 10×10 layout at 0.625 pitch spans 6.125×6.125 = 37.516 cells² in formation coordinates, below the 50-cell² limit. Thus compact arrival is geometrically possible in open space. Turning body yaw inside that unchanged lattice reintroduces the earlier problem; rotate the layout with the bodies or validate a different packing.

**[I] Use explicit arrival states:** approach → align to reserved pose → arrived. Freeze final facing from the approach direction. Brake within a bounded capture region, validate the final snap or short move, then latch arrival. Maintain separate enter/exit tolerances. Do not continuously chase a moving centroid or reassign slots because costs differ by tiny amounts. An idle yield temporarily suspends arrival and returns to the same reserved home when the corridor clears; hold cancels yielding.

**[I] Give deep slots access before sealing their approach.** Arrival reservations must include access, not just final occupancy. If the requested compact layout is blocked, search nearby orientations/placements or return a truthful constrained result.

## 8. Question 5 — Movement feel and its technical causes

**[D] Developer-confirmed complaints are more defensible than a claim of universal player consensus.** AoE II DE's patches connect retreat/regrouping and shuffling problems to formation behaviour; Relic connects dangerous detours and donut turns to road preference and turn planning; Blizzard connects bulky friendly blocking to push priority. ([S07], [S08], [S11], [S04], [S05])

**[I] These support four design priorities:** obey immediately where motion is feasible; avoid gratuitous reversals; keep arrival positions stable; and make friendly yielding predictable. Congestion is tactically meaningful, but unexplained cancellation or endless oscillation feels like a failed command.

**[D/I] Positive evidence is narrower.** BAR's release describes smoother, less obviously eight-directional movement; CoH's formation chapter deliberately minimizes unnecessary role swaps. Those are documented design goals and improvements, not a representative survey of player praise. The sources inspected do not establish comparative satisfaction rankings for Total War, TAB, PA, 0 A.D., or RA2. ([S16], [S10])

**[A] Three firsthand examples help specify feel tests.** One SC2 player praises accepting repeated, slightly adjusted commands without interrupting motion. A TAB player reports snipers dying when retreat through a gate jammed; replies describe different experiences. A 2023 0 A.D. discussion objects to unreadable melee blobs, while a gameplay contributor reports stronger-pushing experiments causing walking in place and repeated displacement at resources. These are anecdotes, not consensus, current bug verification, or proof that strict non-overlap is impossible. ([S41], [S42], [S43])

**[I] Do not equate visual smoothness with correctness.** Some cited games permit pushing or overlap that your contract excludes. In playtests, compare input-to-motion delay, unwanted backward travel, number of destination retasks, clearance of friendly blockers, and player correction clicks alongside completion time.

## 9. Question 6 — Two unusual combinations to test

**[S] These are candidate combinations, not claimed breakthroughs.** No evidence establishes that either beats state of the art. Timings below are rough incremental elapsed-time estimates at 2,000 units, excluding the existing chain; absent a specified CPU, they are order-of-magnitude hypotheses.

### A. Reserve turning space for contact-connected groups

**[S] How:** use rejected rotation contacts to identify a local group that cannot turn independently. First test a common translation; otherwise reserve nearby expansion space and generate a few coordinated shear/expand/turn candidates. Certify the joint trajectories before committing. Release the group back to independent steering immediately afterward.

**[I] Why it may help:** most neighbouring units in the packed spawn want similar motion. A small coordinated motion can preserve spacing where independent rejection cannot. This combines contact components, formation deformation and swept validation—known ingredients; the rotation-triggered specialization may be useful here.

**[S] Cost hypothesis:** 0.05–0.20 μs/unit/tick for detection, plus 0.2–1.0 ms for bounded active solves: **0.15–0.70 μs/unit/tick total when normalized over 2,000 units**, or 0.3–1.4 ms. Start with at most two 128-unit components and six candidates each. These costs consume the same movement budget as avoidance. Many large active components can invalidate this estimate.

**[S] Failure modes:** insufficient expansion space, components becoming too large, slow outer units, or expansion blocking another stream. It has no universal liveness proof.

**[I] Cheap experiment:** two squares, a 10×11 block, then the original wall. Compare ordinary steering with coordinated candidates. Kill the idea if it cannot clear all 110 within the safety/speed limits or repeatedly needs global rearrangement; reject any “win” from overlap or teleporting.

### B. Couple destination capacity to bottleneck admission

**[S] How:** associate each portal admission with downstream free space or a reachable arrival slot. A unit acquires a short lease before entering, releases it after clearing, and cannot park across the next route. Slot filling order propagates available capacity back toward congested merges.

**[I] Why it may help:** the ring failure combines travel congestion with terminal packing. Coordinating the two may prevent early arrivals from blocking the rest. This resembles traffic backpressure and reservation systems; novelty is unproven and probably lies only in the RTS-specific combination.

**[S] Cost hypothesis:** 0.03–0.15 μs/unit/tick, about 0.06–0.30 ms at 2,000 units, assuming O(1) lease updates and few active portals. Slot generation/reassignment is separate event work.

**[S] Failure modes:** stale slots, cyclic resource dependencies, underused lanes, and excess queueing. Use ordered resource acquisition or a cycle-breaking retreat rule; do not hold one portal while indefinitely waiting for another.

**[I] Cheap experiment:** the closed ring and two serial gaps. Compare portal-only scheduling against capacity-coupled admission. Require 100/100 legal arrivals and fewer long stalls without materially reducing throughput; kill it if leases introduce persistent new cycles.

## 10. Ranked sandbox experiments and pass/fail gates

**[I] Common protocol:** fixed saved maps/orders, original spacing, at least 100 reproducible seeds where applicable, shuffled IDs, both sizes, and a recorded CPU/package/build configuration. Log meaningful progress, all rejection reasons, completion, overlap and terrain penetration, allocations, p50/p95/p99 time, and replay hashes. An unsolved test remains failed.

| Rank | Experiment | Pass/fail measure |
|---|---|---|
| **1 [I]** | Validator isolation: frozen yaw, identical translation; then block the leader | All feasible units move within three ticks; blocked-leader dependencies remain collision-free; ID layout does not change success |
| **2 [I]** | Rotation sweep: 0–45°, body-only versus whole-formation rotation | Contacts agree with the 36.87° threshold under stated geometry; zero intermediate overlap; feasible common translations accepted |
| **3 [I]** | Exact original wall/ring fixtures; release and arrival changes separately | 110/110 and 100/100 finish in valid poses; no automatic settling counted as arrival; meaningful wait ≤150 ticks where feasible |
| **4 [I]** | Any-angle A/B with other movement unchanged | Zero obstacle intersections and ≤1.25× defined shortest route; report completion improvement separately. Partial improvement does not close the jam defect |
| **5 [I]** | ORCA versus cheaper predictive steering, both with same safety/recovery | Choose only if completion/throughput or fewer corrections improve within total cost budget; sweep neighbour count/horizon and record infeasible solves |
| **6 [I]** | 200 through 3-cell gap; two opposing groups of 100; repeated right-angle merges | All complete, zero overlaps, no meaningful-progress stall >150 ticks in prevalidated feasible fixtures; no starvation of either stream |
| **7 [I]** | Arrival and mixed groups | 100 small units occupy ≤50 cells²; 50 small +10 large over 50 cells have ≤90-tick arrival spread; all settle ≤60 ticks after valid arrival |
| **8 [I]** | Idle/stop/hold/attack-move interaction | Idle displacement from home ≤2 cells and return after clearance; stop ≤15 ticks; hold invariant; feasible attack-move fixtures retain ≤2× initial depth |
| **9 [I]** | 2,000 units with many independent orders, plus 5,000/10,000 scaling | Zero per-tick allocations/collisions; provisional movement p99 ≤2 ms at 2,000; full game meets 60 fps on named reference PC; publish measured scaling |
| **10 [I]** | Deterministic replay and adversarial infeasibility | Identical hashes for identical inputs across target machines/worker counts; no state dependence on job finish time; impossible cases remain safe and report blocking |

**[I] Add a completion deadline to each fixture before testing.** Derive it from a collision-free reference schedule or a generously bounded offline solution. A five-second stall metric alone can be defeated by creeping forever. For route quality, use an exact reference appropriate to the footprint model; simple convex polygon worlds permit visibility-based references, while orientation-dependent square motion may require a richer configuration-space reference.

**[I] Include cold-start and repeated-command variants.** Test 1, 10, then 50 simultaneous distinct orders on both map sizes, including field building, slot assignment and deterministic publication in the three-tick response measurement. Repeat similar clicks at fixed intervals; they must not restart formation assembly or suppress otherwise feasible movement. Field-build p95 alone does not establish startup compliance.

**[I] Implementation order:** validator/geometry audit → reachable slots and arrival latching → coordinated release and portal rules → cheap steering/ORCA comparison → rare cooperative recovery → larger-map caching and congestion refinement. Keep each accepted improvement only when it resolves an observed failure within the measured budget.

## 11. Sources and reuse boundaries

**[D/I] Code policy:** RVO2's current repositories use Apache-2.0; OpenSteer advertises MIT. They are possible reference implementations, not additional runtime dependencies or ready-made Burst ports. Recoil, 0 A.D. and EA's C&C releases are GPL: this report recommends only their architectural ideas, never copying or translating their code. A paper's public availability is not a permissive code licence. Retain the licence and notices of any actual permissive code reused. ([S27], [S38], [S39])

### Shipped games and developer material

- **S01:** Elijah Emerson, *Crowd Pathfinding and Steering Using Flow Field Tiles*, Game AI Pro, chapter 23. Developer account of Supreme Commander 2. [PDF][S01]
- **S02:** Frank Cheng, *Pathing in Age of Empires IV: Flow Fields and Steering Behaviors*, GDC 2022. [Slides][S02]
- **S03:** James Anhalt et al., *AI Navigation: It's Not a Solved Problem—Yet*, GDC 2011. SC2 slides inspected in video, approximately 03:54–14:54. [Session/video][S03]
- **S04–S06:** Blizzard, SC2 [5.0.14 notes][S04], [5.0.15 notes][S05], and [Unit Control guide][S06]. Push policies and documented tactical limitations.
- **S07–S08:** World's Edge, AoE II DE updates [153015, August 2025][S07] and [185872, September 2026][S08]. Formation, responsiveness and overlap changes.
- **S09:** Forrest Smith, 2024 first-person PA pathfinding recollection. [Discussion][S09]; [contemporary author background](https://www.forrestthewoods.com/blog/tech_of_planetary_annihilation_chrono_cam/).
- **S10:** Chris Jurney, *Company of Heroes Squad Formations Explained*, AI Game Programming Wisdom 4, 2008. [Chapter mirror][S10]
- **S11:** Relic, *Coral Viper 1.6.0—Pathfinding Improvements*, originally April 2024. [Developer explanation][S11]
- **S12–S13:** Total War: [Rome II group manual][S12]; Creative Assembly developer [Warhammer II interview, 2018][S13].
- **S14:** Numantian Games, *They Are Billions—Development Update v0.8.2*, June 2018. [Developer post][S14]
- **S15–S16:** BAR [multithreaded pathing release, 2021][S15] and [QTPFS release, 2023][S16].
- **S17–S18:** Recoil [maintainer changelog][S17] and [recorded wall/pushing failure #3114][S18]. The latter is an issue report.
- **S19:** 0 A.D. 0.28, motion-system source, versioned Debian mirror. GPL; inspected for ideas. [Source][S19]
- **S20:** Wildfire Games, historical pathfinder design; includes implementation/planning caveat. [PDF][S20]
- **S21–S23:** EA original sources: Tiberian Dawn [FINDPATH.CPP][S21], [DRIVE.CPP][S22], and Red Alert [FOOT.CPP][S23]. GPL; inspected for ideas.
- **S24:** Louis Castle, *Tiberian Sun—War Stories*, 2019. [Developer interview/transcript][S24]

### Research, implementations, and platform documentation

- **S25:** Van den Berg et al., *Reciprocal n-body Collision Avoidance*, 2011; especially dense infeasibility and obstacle handling. [Paper][S25]
- **S26:** RVO2, neighbour caps, search distance and horizons. [Parameter documentation][S26]
- **S27:** RVO2 [C++ licence][S27] and [C# repository](https://github.com/snape/RVO2-CS), Apache-2.0.
- **S28:** Treuille, Cooper and Popović, *Continuum Crowds*, 2006; separation limitation in §4.5. [Paper][S28]
- **S29:** Craig Reynolds, *Steering Behaviors for Autonomous Characters*, GDC 1999. [Author's paper][S29]
- **S30:** Helbing and Molnár, *Social Force Model for Pedestrian Dynamics*, 1995, author-deposited version. [Paper][S30]
- **S31:** Weiss et al., *Position-based Real-time Simulation of Large Crowds*, Computers & Graphics, 2019. [Paper][S31]
- **S32:** Daniel et al., “Theta*: Any-Angle Path Planning on Grids”, JAIR 2010, archived 2014. [Paper][S32]
- **S33:** David Silver, *Cooperative Pathfinding*, AIIDE 2005. [Paper][S33]
- **S34:** Okumura et al., *Priority Inheritance with Backtracking for Iterative Multi-agent Path Finding*, IJCAI 2019. [Paper and theorem scope][S34]
- **S35:** Dergachev et al., “A Combination of Theta*, ORCA and Push and Rotate for Multi-agent Navigation”, 2020. [Paper][S35]
- **S36–S37:** Unity, Burst [changelog][S36] and [floating-point compilation documentation][S37], inspected at version 1.8.31.
- **S38:** [Recoil licence][S38]; [EA Red Alert licence](https://github.com/electronicarts/CnC_Red_Alert/blob/main/LICENSE.md). 0 A.D. licensing appears in S19's header.
- **S39:** [OpenSteer project and MIT licence statement][S39].
- **S40:** Dimitri Bertsekas, *A New Algorithm for the Assignment Problem*, 1981; assignment formulation and Hungarian complexity. [Paper][S40]

### Firsthand experience, explicitly anecdotal

- **S41:** Player account of SC2 responsiveness to repeated commands, July 2024. [Comment][S41]
- **S42:** TAB player discussion of retreat/gate congestion, with differing replies. [Thread][S42]
- **S43:** 0 A.D. player/contributor discussion of overlap and pushing tradeoffs, May 2023. [Thread][S43]

[S01]: https://www.gameaipro.com/GameAIPro/GameAIPro_Chapter23_Crowd_Pathfinding_and_Steering_Using_Flow_Field_Tiles.pdf
[S02]: https://media.gdcvault.com/GDC%2B2022/Speaker%2BSlides/Pathing%2BIn%2BAge_Cheng_Frank%2B2022-03-29%2B00.16.38.pdf
[S03]: https://www.gdcvault.com/play/1014514/AI-Navigation-It-s-Not
[S04]: https://news.blizzard.com/en-us/article/24162754/starcraft-ii-5-0-14-patch-notes
[S05]: https://news.blizzard.com/en-us/article/24225313/starcraft-ii-5-0-15-patch-notes
[S06]: https://news.blizzard.com/en-us/article/4552957/game-guide-unit-control
[S07]: https://www.ageofempires.com/news/age-of-empires-ii-definitive-edition-update-153015/
[S08]: https://www.ageofempires.com/news/age-of-empires-ii-definitive-edition-update-185872/
[S09]: https://news.ycombinator.com/item?id=41097228
[S10]: https://forum.arongranberg.com/uploads/short-url/rWod3K2KhNWcOEdjewnsLXQKU6A.pdf
[S11]: https://help.relic.com/hc/en-us/articles/39572638372755--PC-Coral-Viper-1-6-0-Pathfinding-Improvements
[S12]: https://r2enc.totalwar.com/en/manual/single-player/0082_enc_page_battle_play_phase_conflict_groups/
[S13]: https://www.gamedeveloper.com/design/designing-i-total-war-warhammer-ii-i-to-handle-tons-of-units-and-massive-battles
[S14]: https://www.indiedb.com/games/they-are-billions/news/they-are-billions-development-update-v082
[S15]: https://www.beyondallreason.info/news/groundbreaking-engine-improvements-are-live-multithreaded-pathing-and-new-renderer-deployed
[S16]: https://www.beyondallreason.info/microblogs/48
[S17]: https://github.com/beyond-all-reason/RecoilEngine/discussions/171
[S18]: https://github.com/beyond-all-reason/RecoilEngine/issues/3114
[S19]: https://sources.debian.org/src/0ad/0.28.0-3~bpo13%2B1/source/simulation2/components/CCmpUnitMotion_System.cpp
[S20]: https://svn.wildfiregames.com/nightly-build/trunk/docs/pathfinder.pdf
[S21]: https://github.com/electronicarts/CnC_Tiberian_Dawn/blob/main/FINDPATH.CPP
[S22]: https://github.com/electronicarts/CnC_Tiberian_Dawn/blob/main/DRIVE.CPP
[S23]: https://github.com/electronicarts/CnC_Red_Alert/blob/main/CODE/FOOT.CPP
[S24]: https://arstechnica.com/video/watch/war-stories-c-and-c-tiberian-sun/
[S25]: https://gamma.cs.unc.edu/ORCA/publications/ORCA.pdf
[S26]: https://gamma-web.iacs.umd.edu/RVO2/documentation/2.0/params.html
[S27]: https://github.com/snape/RVO2/blob/main/LICENSE
[S28]: https://grail.cs.washington.edu/projects/crowd-flows/continuum-crowds.pdf
[S29]: https://www.red3d.com/cwr/steer/gdc99/
[S30]: https://arxiv.org/abs/cond-mat/9805244
[S31]: https://web.cs.ucla.edu/~dt/papers/cag19/cag19.pdf
[S32]: https://arxiv.org/abs/1401.3843
[S33]: https://ocs.aaai.org/Papers/AIIDE/2005/AIIDE05-020.pdf
[S34]: https://ijcai.org/proceedings/2019/76
[S35]: https://arxiv.org/abs/2008.01227
[S36]: https://docs.unity3d.com/Packages/com.unity.burst@1.8/changelog/CHANGELOG.html
[S37]: https://docs.unity3d.com/Packages/com.unity.burst@1.8/manual/compilation-burstcompile.html
[S38]: https://github.com/beyond-all-reason/RecoilEngine/blob/master/LICENSE
[S39]: https://opensteer.sourceforge.net/
[S40]: https://web.mit.edu/dimitrib/www/Bertsekas_Assignment_1981.pdf
[S41]: https://news.ycombinator.com/item?id=41106905
[S42]: https://www.reddit.com/r/TheyAreBillions/comments/1tbp6n7/pathing_and_unit_movement_interactions_really/
[S43]: https://wildfiregames.com/forum/topic/107332-disable-unit-overlapping/

