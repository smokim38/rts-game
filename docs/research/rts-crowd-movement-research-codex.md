# Crowd movement for a large-scale RTS

Technical architecture research • 1 November 2026

## 1. Summary: recommendations in order

1. **Keep the shared flow fields and the hard geometry check. Add a way to coordinate movement through contested space.** Your measurements justify improving the existing system before replacing its global planner. A collision veto can preserve safety while refusing every useful move. Local avoidance and a progress policy are separate responsibilities. **[I]**
2. **Investigate footprint rotation before committing to ORCA.** The reported approximately 37° jam threshold matches half-cell squares, spaced 0.625 cells apart, rotating in place together. It does not follow from equal translation of fixed footprints. Any-angle headings below that threshold can release some units; headings near 42° still fail this particular geometry. **[I, geometric derivation in §5]**
3. **Build reachable arrival capacity, stable slots, and command states before elaborate formations.** A corrected goal cell is a route anchor, not room for 100 units. Expand it into reachable exterior slots, preserve assignments, and explicitly finish movement. Replace the five-second “settle” rule with recovery or a truthful blocked result. **[I]**
4. **Start with inexpensive steering, anticipatory clearance, and fair merge/gap admission. Benchmark ORCA against that baseline.** ORCA is a useful candidate-velocity generator, but its collision guarantee is conditional and it does not guarantee arrival. Do not remove the final validator or expect a right-turn bias to resolve every deadlock. **[R/I; ORCA evidence in §4]**
5. **Allow formations to deform while travelling.** Preserve lane order and group identity through narrow passages, then assemble gradually into destination slots. Group pace should follow feasible group progress, not merely assign everyone the slowest unit’s maximum speed. **[I]**
6. **Make the acceptance contract physically consistent.** Universal immediate movement, bounded waiting, immovable holds, bounded detours and arbitrary narrow passages cannot all be guaranteed simultaneously. State the feasible test conditions, preserve safety when they fail, and expose why a command is blocked. **[I]**
7. **Protect determinism and profile on a named CPU.** The supplied 0.6 ms movement-chain time is encouraging; the RTX 3050 specification does not establish CPU navigation performance. Include crowded tails, simultaneous orders, field memory, fixed publication ticks and cross-machine replay hashes. **[B/I]**

### Evidence and scope

**[D]** means shipped behavior documented by its developer, official release notes, or identified implementation. It is strong evidence about that source/version, not proof of an unchanged 2026 implementation. **[R]** means published research; its demonstrated results and theorem assumptions do not automatically transfer to this game. **[I]** means engineering inference, recommendation or explicit mathematical derivation. **[S]** means untested proposal or cost estimate. **[B]** identifies measurements supplied in the brief, not independently reproduced. Developer prototypes and player anecdotes are separately identified and never promoted to shipped internals. Labels apply to their paragraph or table row.

This is a research and experiment plan, not a benchmark of your code. No game implementation was run. No GPL code is supplied or proposed for incorporation.

## 2. What to keep, change and add

### Keep

**[I] Keep the 30 Hz data-oriented simulation, preallocated arrays, shared navigation and interpolated rendering.** At 2,000 units, the supplied 0.6 ms p99 chain corresponds to 0.30 microseconds per unit per tick as an accounting average, including shared work. It is not the marginal cost of another unit. Preserve this baseline as the control experiment. The 6 ms p95 field build can remain asynchronous, subject to command-latency and deterministic-publication rules.

**[I] Keep size-specific passability and one-time unreachable-goal correction, but qualify the latter by connectivity.** Different source components may need different corrected anchors. A geometrically nearby free cell is insufficient if it is inside the closed ring or otherwise unreachable by the relevant size class. All route, smoothing, steering and validation stages must agree on clearance.

**[I] Keep exact non-increasing-overlap validation as the final authority.** With initially disjoint units, a correctly implemented invariant preserves disjointness. If initial overlaps exist, “never increase” does not guarantee their removal. Preserve the distinction between strict geometric safety, eventual progress and successful arrival.

### Change

**[I] Change the destination from a point sink to a set of reserved, reachable poses.** Generate enough legal area, validate slot-to-slot clearance, and connect each assigned slot to the approach corridor. In the ring case, use the reachable outside region; do not repeatedly redirect all 100 units into the same corrected cell.

**[I] Change rejection from an unexplained stop into recorded dependency information.** Record the blocking unit or obstacle, proposed direction, rejected translation/rotation, progress age and destination state. A wait-for graph and blocked-motion statistics distinguish geometric lock, merge contention, infeasible slots and stale routing.

**[I] Change group pace from a rigid speed cap to a progress controller.** Permit immediate useful movement, limit leaders getting too far ahead, and let trailing units use spare speed when recovering. A slowest-speed cap alone cannot compensate for longer paths or a queue.

**[I] Change five-second settling into escalation.** A unit that still has a reachable assignment has not arrived merely because it stopped. Escalate from local steering to admission control, coordinated retreat or replanning; only report arrival after satisfying an explicit terminal condition.

### Add

**[I] Add a deterministic spatial broadphase, corridor/portal metadata, stable terminal assignments and a small recovery scheduler.** Use snapshot-to-proposal jobs for ordinary movement. Resolve contested portals and small dependency components in stable order, then validate and commit a mutually compatible set of trajectories. Expensive recovery should be rare and work-budgeted; fixed ID order alone is not fair, so age waiting requests.

**[I] Suggested tick pipeline:** snapshot poses and command states; build spatial bins; read route/slot preferences; arbitrate portal admission; compute steering or ORCA proposals; reconcile joint collisions; commit; update progress and arrival state. Parallelize read-only proposal generation while keeping deterministic reductions and conflict commits. Preallocate bins, pair lists, solver scratch, reservation tables and recovery queues at scenario capacity. Overflow must take a safe, diagnosed fallback rather than allocate or drop collision candidates. A stable count/prefix/scatter broadphase avoids relying on nondeterministic hash insertion order.

**[I] Add capacity and memory limits before full-map fields multiply.** With just a 32-bit integration value and one direction byte, one field occupies 320 KiB at 256² or 1.25 MiB at 512². Two size classes double those figures; 100 distinct two-class orders at 512² would use roughly 250 MiB before scratch, costs and metadata. Share equivalent static fields by goal/class/map revision, bound caches, and consider segmented fields when profiling justifies them.

## 3. Shipped games: what is actually documented

### StarCraft II

**[D]** Blizzard’s 5.0.15 notes increase allied push priority for Thors and Siege Tanks to help large units traverse friendly crowds. This is direct evidence that yielding policy depends on unit type; it is not evidence of equal reciprocal responsibility or strict separation. [Official patch notes](https://news.blizzard.com/en-us/article/24225313/starcraft-ii-5-0-15-patch-notes).

**[D]** Bob Fitch’s original Blizzard AIIDE 2011 slides describe dynamic triangles/zones, variable unit sizes, A* and circular unit representations. The described AI groups use a real member as leader and swing wide around obstacles to avoid feeding piecemeal into defenses. The talk concerns computer-player AI, so that group policy must not be generalized to every human-issued selection order. It does not establish ORCA, exact player arrival assignment or a zero-overlap invariant. [Conference source](https://movingai.com/aiide11/speakers.html), [original slides archive, slides 34–35](https://movingai.com/aiide11/BlizzardAI.zip).

### Supreme Commander 2

**[D]** Elijah Emerson describes portal-connected sectors, cached integration/flow tiles, movement-specific clearance, direct seeking near visible goals, blended flow directions and wall-adjacent cost gradients. Compatible mixed groups use the most restrictive route. Crucially, local motion also pushes other agents and slides along walls using physics. Your physics-free implementation therefore cannot inherit the complete result by copying only its field architecture. The chapter attributes the predecessor’s frustrating stop/wait behavior to collision interactions that required repeated player intervention. [Developer chapter, 2013](https://www.gameaipro.com/GameAIPro/GameAIPro_Chapter23_Crowd_Pathfinding_and_Steering_Using_Flow_Field_Tiles.pdf).

**[D]** Official patch history also records units getting stuck against walls while starting movement through steering without a field. This is evidence of integration failure cases, not a guarantee that later movement is universally live. [Patch archive](https://www.supremecommander2.com/na/blog.html).

### Planetary Annihilation

**[D]** Uber’s shipped build 73737 notes explicitly discuss flow-field integration, smaller sectors and a more granular higher-level path graph. The developer connects this granularity to correcting wrong-side routes around rocks, and describes rebuilding the graph and small/large pathable islands after dynamic-wall changes. This verifies hierarchical fields and size-related connectivity after release. It does not disclose exact local collision handling, slot assignment or bottleneck fairness; SupCom2’s details must not simply be transferred to PA. [Official release archive, 9 October 2014, “Detail Pathing Notes”](https://store.steampowered.com/news/posts/?appids=233250&enddate=1423780422).

### Age of Empires II: Definitive Edition

**[D]** Update 153015 explicitly prioritizes movement over assembling a formation in its Ctrl+right-click escape command. Its fixes cover front units regrouping before advancing, regrouping during retreat, siege passing narrow gaps and formations crossing obstructions. The developers acknowledge responsiveness problems and interactions beyond the pathfinder. This documents the cost of formation policy; it does not disclose the complete global or local algorithm. [Official update, August 2025](https://www.ageofempires.com/news/age-of-empires-ii-definitive-edition-update-153015/).

### Age of Empires IV

**[D]** Frank Cheng’s GDC 2022 presentation combines hierarchical A*, overlapping segmented flow fields and steering. A virtual leader follows the flow; members seek their formation spots when visible and otherwise follow the field. Mixed traversal classes can extend the most restrictive class’s field to support less restricted members. This is a close architectural precedent for shared navigation plus separate formation and local movement. It does not establish ORCA use, zero overlap or bounded waiting. [Official speaker slides, especially formation and mixed-size sections](https://media.gdcvault.com/GDC%2B2022/Speaker%2BSlides/Pathing%2BIn%2BAge_Cheng_Frank%2B2022-03-29%2B00.16.38.pdf).

### Company of Heroes

**[D]** Chris Jurney’s original-game chapter describes stable squad roles, followers targeting offsets from a predicted leader position, speed correction, terrain-dependent shapes and small searches that project offsets to reachable locations. Near arrival, a distinct destination mode reserves individual places and checks their reachability. This is strong support for deformable travel and stable terminal slots, rather than maintaining an exact block through every corner. It is not a 2,000-unit performance measurement. [Developer chapter, 2008](https://forum.arongranberg.com/uploads/short-url/rWod3K2KhNWcOEdjewnsLXQKU6A.pdf).

**[D—different version]** Relic’s CoH3 explanation separates grid A* routes from vehicle turn plans. Road preference could produce surprising routes; turning/reversing produced circles and exposed rear armor. The lesson is to test predictable motion and facing as well as route validity, without attributing CoH3 internals to CoH1. [Relic’s pathfinding explanation](https://help.relic.com/hc/en-us/articles/39572638372755--PC-Coral-Viper-1-6-0-Pathfinding-Improvements).

### Total War

**[Developer-documented pre-release implementation]** The Warhammer GDC 2016 siege presentation explicitly queues groups at entry points and admits one “unit” at a time. Here a unit is a regiment/formation, not one soldier. This is concrete higher-level chokepoint scheduling, but the pre-release talk does not prove identical behavior across shipped Total War titles. [Arsenault’s slides, storm-entry tactic](https://media.gdcvault.com/gdc2016/Presentations/Arsenault_Andre_Have_Fun_Storming.pdf).

**[D]** Medieval II patch notes identify clumping on 180° turns and repeated reforming at bridges. These are documented formation failure modes; the notes do not identify the low-level avoidance solver. [Official patch notes](https://wiki.totalwar.com/w/Patch_Notes_%28M2TW%29.html).

### They Are Billions

**[D—developer description, 2018 build]** Numantian’s v0.8.2 update says units calculate individual paths and recompute them as surroundings change; optimization restricts invalidation to relevant changes. Infected can plan through destructible walls and attack them when obstructed. Therefore large swarms are not evidence of shared flow fields, and their progress problem differs from movement around permanently blocked terrain. Published details do not establish formal formations, ORCA or your strict footprint invariant. [Developer update](https://www.numantiangames.com/News/2018-06-07-they-are-billions-development-update-v-0-8-2/).

### Beyond All Reason / Recoil

**[D]** BAR’s 2023 engine update describes an overhauled quadtree pathfinder with adaptive resolution and reduced zigzag movement. The 2024 hotfix documents corrections to sliding speed and conspicuous corner “hops,” illustrating the tradeoff between unjamming and visible motion quality. [2023 update](https://www.beyondallreason.info/microblogs/48?4be0118c_page=4), [2024 hotfix](https://www.beyondallreason.info/microblogs/71).

**[D—source inspected, not release-pinned]** Recoil’s ground mover contains avoidance, idle-friendly pushing, push resistance, stuck/repath logic and arrival termination. These layers do not establish strict nonpenetration. Player commands separately expose formation movement at the slowest selected speed and distributed destinations. Read the GPL engine for ideas only. [Ground mover](https://github.com/beyond-all-reason/RecoilEngine/blob/master/rts/Sim/MoveTypes/GroundMoveType.cpp), [official controls](https://www.beyondallreason.info/commands-2-0-grid).

### 0 A.D.

**[D—historical design and inspected source]** The developer design separates long-range clearance-aware navigation from short-range visibility-based handling, and warns about inconsistent passability between layers. Motion-system source uses spatial subdivision, fixed-point data, pushing and lateral nudges; that is not a hard no-overlap theorem. The historical design includes unfinished work, so it is not a complete current-release specification. GPL implementation ideas only. [Design document](https://svn.wildfiregames.com/nightly-build/trunk/docs/pathfinder.pdf), [motion-system source](https://github.com/0ad/0ad/blob/master/source/simulation2/components/CCmpUnitMotion_System.cpp).

### Original Command & Conquer and Red Alert 2

**[D—released original source]** Tiberian Dawn’s pathfinder describes direct cell traversal, following obstacle edges on both sides, choosing a local detour and optimizing the move list. Occupancy and retry policy live in the movement layer. Local detour selection is not a globally shortest-path proof. These GPL sources are conceptual references only. [EA FINDPATH.CPP](https://github.com/electronicarts/CnC_Tiberian_Dawn/blob/main/FINDPATH.CPP), [FOOT.CPP](https://github.com/electronicarts/CnC_Tiberian_Dawn/blob/main/FOOT.CPP).

**[D/I—documented controls, unknown internals]** RA2’s original manual describes move orders, waypoints and coordinated routes. It does not establish its avoidance, formation assignment or corner solver. No authoritative implementation description sufficient to answer those details was verified here. Red Alert 1 and OpenRA are not substitutes for RA2 evidence. [Original manual, mirrored scan](https://static.wikia.nocookie.net/cnc_gamepedia_en/images/b/b5/Red_Alert_2_-_English.pdf/revision/latest?format=original).

## 4. Methods, costs and implementation fit

**[I] Cost model.** Let `N` be units, `k` relevant nearby agents/obstacle features, `M` field cells, `G` distinct goal/class fields, `T` ticks between rebuilds, and `I` solver iterations. Spatial indexing is additional work unless stated. The table gives algorithmic work, not transferable microsecond measurements. All complexity, Burst-fit and requirement mappings are engineering analysis; cited research establishes the methods.

| Method / evidence | Work per unit per tick | Additional memory | Burst jobs and determinism | Helps these requirements | Main risks |
|---|---|---|---|---|---|
| Shared static field **[B/I]** | `O(1)` sample; amortized build `O(G M log M / (N T))` for heap Dijkstra | `O(G M)` plus build scratch | Samples parallel; stable heap ties and publication | Cheap shared routes, size clearance | Point sinks, no traffic capacity or progress policy |
| ORCA/RVO2 **[R/I]** | `O(k)` agent constraints; obstacle handling and reference LP can be `O(k²)`; dense fallback worse | `O(k)` scratch per worker, or `O(Nk)` retained | Parallel snapshot solves; stable neighbors/constraints and numeric rules | Predictive avoidance, crossing crowds | Disc model, infeasibility, neighbor omissions, deadlock |
| Continuum crowds **[R/I]** | Advection `O(1)` plus field work about `O(G M log M / N)` each refresh | `O(G M + N)` before contact data | Cell stages parallel; potential solves/reductions need ordering | Large streams, density response, emergent lanes | Goal count, coarse-grid intersections, no individual liveness |
| Congestion overlay **[I]** | `O(1)` deposit/sample; amortized changed-field work | Shared `O(M)` overlay | Integer bins fit well; fixed refresh and hysteresis | Avoid overloaded alternative corridors | Route oscillation, self-congestion, long detours |
| Boids / steering **[R/I]** | `O(k)` | `O(N)` plus grid; optional neighbors | Excellent snapshot jobs; ordered sums | Responsiveness, separation, cohesion, arrival preference | Balanced forces stall; no hard safety |
| Position-based crowds **[R/I]** | `O(Ik)` plus broadphase/rebuilds | `O(N)` corrections, optional `O(Nk)` neighbors | Jacobi parallel; fixed reductions/iterations | Dense collective deformation | Residual overlap, jamming, iteration cost |
| Social forces **[R/I]** | `O(k)`, potentially costly nonlinear terms | `O(N)` plus grid | Parallel; deterministic math and timestep handling | Natural-looking pedestrian interactions | Soft penetration, stiffness; realistic jams are undesirable |
| Lane bias + portal admission **[I]** | `O(1)` lane/state work, or `O(k)` local bias; queue work amortized | `O(N + portals)` or lane grid | Stable queue order; independent portals parallel | Head-on traffic, merges, bounded service under assumptions | Insufficient width, downstream blockage, starvation without aging |
| Theta* / LOS smoothing **[R/I]** | `O(1)` waypoint following; search and ray checks amortized over route users | Search `O(M)`, shared waypoint lists | Independent searches parallel; deterministic geometry | Smoother corners, shorter static routes | Does not allocate space; no universal 1.25 stretch theorem |
| Shared field + local solver + recovery **[I]** | Sum of selected stages; exceptional search budget separate | Fields, local scratch, reservations | Natural staged pipeline; deterministic arbitration | Broadest fit across the brief | Integration complexity; bounded search is not complete |

### What the methods actually guarantee

**[R] ORCA** constructs pairwise velocity constraints using shared responsibility, then chooses a preferred feasible velocity. The original paper explicitly discusses densely packed conditions with no feasible solution; collision freedom is not guaranteed in that fallback. RVO2 documentation warns that insufficient neighbor distance/count can undermine safety. These are material qualifications, not rare details to hide behind a headline benchmark. [ORCA paper](https://emotion.inrialpes.fr/fraichard/safety2010/10-vandenberg-etal-icraw.pdf), [RVO2 parameters](https://gamma-web.iacs.umd.edu/RVO2/documentation/2.0/params.html).

**[I—source complexity analysis]** The table’s implementation bounds reflect nested obstacle/constraint scans and the dense fallback in the reference implementation; they are not the randomized LP’s expected-linear analysis. RVO2 is an ORCA implementation, not merely another name for the earlier RVO construction. [Official implementation](https://github.com/snape/RVO2/blob/main/src/Agent.cc), [RVO2 project](https://gamma-web.iacs.umd.edu/RVO2/).

**[I] For this game**, make pair responsibility complementary. A hold-position unit takes zero motion responsibility; the mover must account for that before solving, not after its partner has assumed a half-share. Treat enemies as non-yielding and retain actual-geometry validation. A persistent right-hand preference helps symmetric meetings but does not reserve an exit. Limit heuristic neighbors for performance only if the final safety broadphase still checks every relevant collision candidate.

**[R] Continuum crowds** combine density and directional flow with a shared potential, demonstrating lanes and crossings. Their finite-resolution simulation can still create intersections, and the paper’s separation cleanup does not strictly preserve minimum distances. **[I]** A slowly refreshed coarse congestion overlay is therefore a more conservative first experiment than replacing every order’s static field with a dynamic continuum solve. [Treuille et al., 2006](https://grail.cs.washington.edu/projects/crowd-flows/continuum-crowds.pdf).

**[R] Steering and social forces** provide local behavioral components, not complete multi-agent planning. **[I]** Use bounded separation, wall tangents and arrival preferences as candidate motion; excessive cohesion opposes opening a gap, and opposing repulsions can settle into equilibrium. Social-force realism is not itself a desirable RTS outcome. [Reynolds, 1999](https://www.red3d.com/cwr/steer/gdc99/), [Helbing and Molnár, 1995](https://arxiv.org/abs/cond-mat/9805244).

**[R] Position-based multi-agent dynamics** predict positions and iteratively correct short-range and anticipatory constraints. The published system uses finite parallel iterations and shows bottleneck arching/jamming. **[I]** It can generate useful coordinated proposals, but a fixed iteration budget is not a proof of zero residual penetration. Validate the complete proposed state; clipping individual corrections afterward can invalidate the joint solution. [Weiss et al.](https://arxiv.org/html/1802.02673v2).

**[R] Theta*** finds continuous-heading grid routes but does not guarantee the true shortest route. **[I]** First test footprint-clear line-of-sight smoothing of existing shared routes. Use swept footprint clearance, not a center ray through diagonal cell corners. A straight line to a corner is valid only after accounting for the unit’s clearance and orientation. [Daniel et al.](https://arxiv.org/abs/1401.3843).

### Runtime and determinism

**[S] Initial engineering budget, not a forecast:** retain the measured 0.6 ms chain, provision roughly 0.4 ms for spatial indexing/steering/telemetry and 0.5 ms for amortized coordination, making a proposed 1.5 ms p99 movement budget at 2,000 units. ORCA must compete inside that budget or justify increasing it with whole-frame measurements. Publish both wall time and CPU work; adding independently measured p99 values does not predict the p99 of their sum.

**[D]** Burst 1.8.25 added supported `FloatMode.Deterministic`; the old claim that it remains unimplemented is outdated. Verify the installed package and its documented architecture/math restrictions. **[I]** Numeric support does not fix unordered neighbor enumeration, race-dependent writes, heap ties, unseeded randomness or wall-clock-dependent field availability. Use deterministic completion/publication barriers, stable priority aging and state hashes. [Burst changelog](https://docs.unity3d.com/Packages/com.unity.burst@1.8/changelog/CHANGELOG.html), [compilation documentation](https://docs.unity3d.com/Packages/com.unity.burst@1.8/manual/compilation-burstcompile.html).

**[D/I—licensing]** Current official RVO2 is Apache-2.0; its older project-page wording should not be confused with the current distribution’s license. A port would still require preallocated Burst-compatible storage and preserved notices. Check any third-party C# port separately. Recoil, 0 A.D. and EA’s GPL releases remain ideas-only references under your constraint. No additional runtime package is required by this recommendation. [RVO2 license](https://github.com/snape/RVO2/blob/main/LICENSE).

## 5. Diagnosing and releasing the measured jams

### The 37° threshold answers the architect’s question

**[I—derivation]** Assume identical squares of side `s = 0.5`, centers separated horizontally by `d = 0.625`, with both squares oriented at angle `θ`. For `0° ≤ θ ≤ 45°`, their separating-axis condition is `d cos θ ≥ s`. They first touch at:

`θ = acos(0.5 / 0.625) = 36.8699°`.

Thus 23° is safe for this pair; 42° and 45° are not. To permit every shared orientation without changing this horizontal center arrangement requires `d ≥ s√2 ≈ 0.7071`, before numerical margin. This matches the brief’s diagnosis if collision footprints rotate while centers retain their row arrangement. It is not the same as rotating an entire rigid formation, where the center arrangement rotates too.

**[I] If footprints are fixed**, translating all units by the same vector preserves pair distances and cannot create overlap. Then investigate different sampled velocities, differential rotation, wall contact or the acceptance order. Capture desired and accepted poses before attributing the jam to eight-way directions alone.

**[I] Any-angle routes can therefore release part of the block, but cannot be the complete fix.** A smaller initial heading may avoid this rotation barrier; a later corner can still demand an unsafe turn, and converging streams still compete for the same space. Conversely, “only local steering can help” is too strong: footprint design, shared-path smoothing, release scheduling and terminal capacity can all help independently.

**[I] ORCA’s disc model needs an explicit decision.** A disc enclosing a half-cell square has radius approximately 0.3536 and diameter 0.7071: these proxy discs already overlap at the supplied spawn spacing. An inscribed radius of 0.25 misses the corners. Options are an orientation-independent gameplay footprint with separately animated facing, exact polygon-aware proposals, or a deliberately conservative proxy whose packing cost is accepted. None is a transparent drop-in replacement.

### Preserve safety while allowing coordinated motion

**[I] Audit the validator’s transaction semantics.** If both units independently test against old positions, each may accept a move that is safe alone but unsafe together. At initial center separation 0.625, two half-cell squares moving 0.1 toward each other each pass an old-state test, yet finish only 0.425 apart. A serial validator can avoid that error but may falsely block followers until leaders are processed. These are audit cases, not allegations about your measured implementation.

**[I] Validate mutually dependent moves together**, including interactions with outsiders and any unit whose proposal is rejected. Check the overlap invariant per pair, not as a summed overlap that could hide worsening one contact. Use conservative swept tests for static obstacles and moving footprints; endpoint checks alone do not establish collision freedom throughout translation/rotation or render interpolation. For small conflict components, accept a compatible joint proposal atomically. Otherwise retain the previous safe state and request a coordinated alternative. Once a guard changes an ORCA proposal, its original reciprocal-velocity argument no longer establishes safety; the joint validator does.

### Practical policies by failure mode

| Situation | Recommended action **[I]** | Why it should help; limits |
|---|---|---|
| Packed block turning | Delay collision-footprint rotation where allowed; open clearance from the exposed boundary inward; release small cohorts into verified free space | Removes the identified orientation barrier. Interior units cannot expand into occupied neighbors; needs an actual vacancy and may conflict with universal immediate start |
| Streams merging at a wall | Assign persistent approach lanes and zipper admission; reserve a downstream berth before entering the merge | Stops both streams repeatedly contesting the same pose. Fairness requires finite service and a clear exit |
| Sharp outside corner | Use clearance-aware tangents/waypoints, reduced approach speed and a reserved turning pocket | Avoids wall-directed velocity and prevents arrivals packing the corner before it clears |
| Three-cell gap | Choose lanes from actual footprint clearance; assign entry sequence before the throat; keep exit lanes clear | Converts a local negotiation into scheduled service. Do not assume six small units fit with positive safety margins |
| Two opposing groups | Persistent right-side lanes where width permits; otherwise alternate bounded batches with aging | Avoids oscillating choices. A single narrow passage may require backing out, and whole-army batches can violate five-second waiting |
| Persistent deadlock | Detect repeated blocked dependencies; reserve a nearby vacancy and coordinate a local retreat/reorder | Can escape a local minimum without overlap. Small bounded search is a recovery heuristic, not a universal theorem |

**[I] Most likely wall-case fix:** combine rotation-aware release, footprint-clear smoothing and controlled merging. ORCA alone is especially questionable if its disc proxies start overlapped. **Most likely ring-case fix:** first allocate enough reachable exterior arrival slots, then apply the same release/corner policies. The reported 26/110 and 14/100 outcomes should remain regression baselines; success means every unit completes or receives a justified infeasible result, not simply more units escaping.

### What “guaranteed progress” can honestly mean

**[R]** Priority Inheritance with Backtracking proves finite-time arrival under specific graph assumptions, including suitable cycles around adjacent vertices. It is not a theorem for arbitrary corridors, rotating large footprints, immovable units and bounded home radii. Published work also combines reciprocal avoidance with local multi-agent planning to repair deadlocks, improving success on its benchmarks without establishing your complete contract. [PIBT](https://arxiv.org/abs/1901.11282), [local MAPF recovery](https://arxiv.org/abs/2107.00246).

**[I] A defensible local guarantee** is possible for a finite queue with a verified clear downstream berth, bounded traversal time, no permanent intruder, fair admission and reserved safe trajectories. Each admission then completes and eventually serves every queued unit. The waiting bound depends on queue length and service time; fairness alone does not imply five seconds.

**[I] Global liveness needs stronger map/admission rules or a complete planner on a faithful finite model.** A budgeted fallback that fails should report “recovery pending” or “search budget exhausted.” Failure to find a plan is not proof that no feasible route exists; reserve “infeasible” for a validated impossibility result. Never silently allow overlap, move hold units or mark stalled units arrived to pass the test.

## 6. Groups, slots and stopping without dancing

**[I] Separate travel membership from final assignment.** Maintain a group anchor, route corridor, lane/rank order, pace and terminal slot IDs. Travel slots are soft preferences that can compress into columns; destination slots are persistent reservations. A 180° move need not send every member through the group to exchange front/back rank unless tactical roles require it.

**[I] Start with deterministic spatial sorting plus local swaps.** Sort members and slots along travel/lateral axes, assign compatible sizes, then improve a bounded neighborhood. This is typically `O(n log n)` sorting plus bounded refinement. Naive nearest-free-slot greedy can cost `O(n²)` and creates crossings or steals slots needed by large units. An optimized Hungarian implementation takes `O(n³)` work with a dense `O(n²)` cost matrix; use it as a small-group quality oracle or infrequent comparison, not every tick. Distance-optimal assignment alone does not make the resulting trajectories collision-free. [Assignment-method analysis, Bertsekas](https://web.mit.edu/people/dimitrib/Bertsekas_Assignment_1981.pdf).

**[I] Assignment costs should include route distance, compatibility, crossing/order penalties and reassignment cost.** Reserve large slots first when using a heuristic, then fill remaining small slots. This reduces one failure mode but is not globally optimal. Lock assignments once near arrival; reopen them only for a sustained obstruction, terrain change or command revision. Fill deeper slots before entrance slots where settling early would block access.

**[I] Use two geometric tests for the compactness requirement:** total footprint area and the envelope of the final formation. For 100 half-cell squares, summed area is 25 cells² and the allowed envelope is 50 cells². Define the envelope as a travel-oriented bounding rectangle including full footprints; using occupied area alone would pass even widely scattered units. A regular 10×10 layout at 0.7071 pitch has a 50 cells² envelope when measured using its all-orientation enclosing discs; actual axis-aligned half-cell squares occupy an approximately 47.1 cells² envelope. Conservative proxies consume packing headroom, but they are not the physical shape. Other packings may differ.

**[I] Through chokepoints**, reduce width in advance, preserve order, and regroup in a downstream area with sufficient capacity. Do not reform while blocking the exit. An attack-move depth controller can slow the front or delay entry, but geometry may make a fixed depth bound infeasible: fitting area `A` into corridor width `W` requires at least `A/W` longitudinal extent, even before spacing.

**[I] Mixed-speed timing:** 50 cells takes a small unit 12.5 seconds and a large unit 20 seconds at the given maxima. Merely sending both at their own speed produces 7.5 seconds of separation before traffic. Start with a 2.5 cells/s common cruise pace, then adjust each unit’s desired speed using remaining route/slot distance and a shared arrival window. Reserve sufficient terminal throughput; a narrow access queue can dominate the three-second arrival-spread target.

**[I] Shared-route policy can also conflict with individual shortest paths.** A corridor wide enough for the large class may substantially detour around a gap usable by small units. Specify whether the 1.25× test uses each unit’s independent optimum or the group’s common-route constraint; do not quietly change the baseline to make cohesion pass.

**[I] Explicit states prevent dancing:** `Moving → Approaching → Settled`, with separate `Yielding`, `ReturningHome`, `Stopped`, `Holding` and `Blocked`. Settling requires a valid reserved pose, small positional error and low velocity for a short fixed interval. On settling, zero velocity and disable recurring slot optimization; use a larger threshold for reopening than for entering. Final facing must also be safe—rotating a square after arrival can recreate the original problem.

**[I] Stop and hold override group policy.** Stop cancels route following and settling corrections within 15 ticks; hold cancels all movement authority, including pushing and recovery. Idle-friendly yielding needs a feasible reserved refuge and return path within two cells of home. If none exists, reroute the mover or report blockage. Returning home needs hysteresis so continuous traffic does not cause repeated out-and-back oscillation.

## 7. Player feel and defensible causal links

**[D/I] Responsiveness is more than frame rate.** AoE II DE’s escape command and regrouping fixes directly connect formation policy to command response. The inference for this game is to start feasible motion immediately and assemble while travelling; do not make every click wait for a perfect block. The source establishes the shipped policy change, not a universal player preference. [AoE II DE update](https://www.ageofempires.com/news/age-of-empires-ii-definitive-edition-update-153015/).

**[Anecdotal evidence; not an implementation claim]** An AoE II player discussion contains both praise for ranged-unit control without regrouping and complaints about mixed groups sticking. These are direct experiences, not a representative survey or proof of a particular bug cause. They support testing the two feelings separately: “obeys my order immediately” and “maintains useful cohesion.” [Player discussion](https://www.reddit.com/r/aoe2/comments/1x7w1wo/pathing_this_patch/).

**[Anecdotal evidence; mechanism unverified]** Total War players report frustration when regiments use ladders despite an apparently available gate, sometimes requiring repeated manual waypoints. Their proposed explanations disagree and are not implementation evidence. The actionable inference is to expose and test unexpected entrance choices, especially when avoiding congestion changes a tactically meaningful route. [First-person discussion](https://www.reddit.com/r/totalwar/comments/1kpyya4), [official-community feedback](https://community.creative-assembly.com/total-war/total-war-warhammer/forums/77-siege-beta/threads/10769-units-still-prefer-using-ladders-and-towers-over-an-open-gate).

**[D/I] Predictability can outweigh nominal travel efficiency.** CoH3’s developer account ties unexpected routes and vehicle circles to route costs and turn plans. BAR’s corner-hop fix shows why an effective unjamming trick can still look wrong. For your sandbox, measure reverse motion after a forward order, oscillating heading, unexplained detours and correction jumps alongside arrival time. [Relic](https://help.relic.com/hc/en-us/articles/39572638372755--PC-Coral-Viper-1-6-0-Pathfinding-Improvements), [BAR hotfix](https://www.beyondallreason.info/microblogs/71).

**[I] Clumping is a design choice with tradeoffs.** A compact army feels responsive and concentrates fire, while rigid collision and perfect formations can make it harder to control. The available sources do not justify ranking all listed games by player satisfaction, nor attributing every complaint to their global pathfinder. Prefer replay-backed cases, command-to-motion latency, completion rates and blind comparisons of two movement variants. Avoid substituting widespread reputation for evidence.

**Additional firsthand feedback, all anecdotal.** These reports establish what particular players experienced, not prevalence, current-build status or the correctness of their algorithmic explanations. The implications are test priorities inferred for your game.

| Game | Reported praise or frustration | Implication and evidence limit |
|---|---|---|
| StarCraft II | Players praise movement that rarely gets stuck while another dislikes tight packing. [June 2022 discussion](https://www.reddit.com/r/starcraft/comments/vld7v9/a_gdc_talk_done_by_james_anhalt_lead_software/) | Test responsiveness and packing separately; a claimed link to competitive “deathball” play is player interpretation |
| PA: TITANS | Units reportedly fail to find a nearby ramp; another player suggests smaller groups and waypoints. [February 2024 report](https://steamcommunity.com/app/386070/discussions/0/4369124243587359945/) | Test narrow approach discovery and command recovery; TITANS report, no verified replay/root cause |
| AoE IV | An army reportedly twitches at building/resource corners until another command; staff acknowledges pathing work. [February 2022 forum report](https://forums.ageofempires.com/t/units-get-stuck-while-pathing/197076) | Corner oscillation deserves a regression fixture; historical symptom, not a solver diagnosis |
| They Are Billions | Gate bunching persists after repeated move commands; backing units out reportedly helps. [January 2018 discussion](https://steamcommunity.com/app/644930/discussions/0/1620599015914378180/) | Test automatic recovery that permits temporary retreat; early-access anecdote |
| BAR | Constructors reportedly become trapped among defenses or blocked by assistants. [June 2024 discussion](https://www.reddit.com/r/beyondallreason/comments/1doa9ir) | Test idle-friendly obstruction and refuge availability; no verified internal cause |
| 0 A.D. | A new player finds automatic grouping makes peeling off individual units difficult. [August 2023 discussion](https://www.reddit.com/r/0ad/comments/160i333) | Formation control can frustrate independent of path validity; do not diagnose a navigation defect from this alone |
| RA2 / Yuri’s Revenge | A reviewer praises simple click control but dislikes dangerous detours when a narrow passage is occupied. [June 2017 first-person review](https://gbatemp.net/threads/red-alert-2-retro-review.474152/) | Test route predictability under traffic; the review covers both base game and expansion |
| Original C&C | A commenter recalling the original describes circuitous harvester routes and bridge jams. [Retrospective account](https://www.reddit.com/r/commandandconquer/comments/gz1gj0/comment/ftedpl0/) | Useful scenario lead, weaker retrospective evidence within a remaster discussion |

**[I] The defensible causal links remain narrower than these observations:** developer sources explicitly connect regrouping to response, route/turn policies to surprising vehicle behavior, and corner recovery to visible hops. For the other anecdotes, the cause remains unverified. This report does not claim a representative satisfaction survey for any title.

## 8. Three unusual combinations worth testing

**[S] These are proposed adaptations, not established novelties.** Any-angle routing plus ORCA plus local cooperative repair already appears in published work. The possible advantage here is narrower integration with your exact safety contract and sparse exceptional work. The numeric estimates below are provisional CPU budget bands at 2,000 units, not measurements; reject them if target-hardware profiling disagrees. [Existing Theta*–ORCA–Push-and-Rotate combination](https://arxiv.org/abs/2008.01227).

### A. Move the vacancy backward through the jam

**[S] Mechanism:** when a stable blocking component appears, find a nearby free berth and build a short chain of coordinated moves that brings that free space toward the blocked leader. Reserve the chain, validate its swept geometry and execute it as a scheduled transaction. This addresses the reason independent safe proposals fail: nobody volunteers to make room.

**[S] Cost and risks:** ordinary units retain a few dependency/age fields; target 0.02–0.10 microseconds per unit per tick for monitoring, or 0.04–0.20 ms total. Budget recovery separately at 0.2–1.0 ms per job, with a deterministic expansion cap and incremental execution. Chains can be long, no vacancy may exist, and changing commands can invalidate reservations. This resembles push/rotate and priority inheritance; the adaptation is continuous-footprint validation and event-triggered use.

**[S] Cheap kill test:** give the wall case one explicit exterior vacancy. Compare independent steering with bounded 8-, 16- and 32-unit recovery components. Kill the approach if completion depends on overlap, component size repeatedly explodes, or 95% of jams need global search.

### B. Budget turning clearance before admitting the turn

**[S] Mechanism:** attach a required turning envelope to each upcoming corner. Cohorts receive admission only when their rotation and downstream lane are both clear. While packed, retain the current collision orientation where gameplay permits and begin safe translation; expand or reorder before rotating. This combines shape-aware clearance with queue admission rather than asking the final veto to discover the same failure every tick.

**[S] Cost and risks:** table lookup plus local clearance tests, approximately `O(k)` near a turn. Provision 0.05–0.25 microseconds per active unit, at most 0.1–0.5 ms if all 2,000 are active. It can make facing lag travel, over-reserve space or fail for vehicles with genuine turn constraints. Predictive clearance and scheduling are known ideas; this particular integration is unvalidated.

**[S] Cheap kill test:** sweep spacing, approach angle and wall clearance with rendering decoupled from collision orientation as a diagnostic variant. Require 110/110 completion without increasing overlap; abandon if gains disappear when exact swept turns are checked.

### C. Reserve destination capacity backward to the approach

**[S] Mechanism:** select reachable terminal slots first, reserve the approach cells needed to fill them, and feed aggregate occupancy/capacity back into the shared route preference. Units choose destination sectors with available capacity rather than all following a point sink. Commit assignments with hysteresis so the field does not chase every vacancy.

**[S] Cost and risks:** target `O(1)` membership/capacity sampling, 0.02–0.10 microseconds per unit, or 0.04–0.20 ms total; slot generation and assignment are separate order-time work. Risks include stranded capacity, costly mixed-size packing and reservation churn after new commands. This is related to multi-goal potentials and destination reservation, not a claim of a new general algorithm.

**[S] Cheap kill test:** run the closed-ring case with an exterior slot pool and no ORCA. Require 100/100 legal settlements; reject the added field coupling if stable slots alone achieve equal completion with less cost and no worse travel.

## 9. Ranked sandbox experiments and acceptance contract

**[I] Common harness:** retain exact deterministic scenario seeds, initial poses, unit IDs, commands and map revisions. Record desired/accepted motion, blocker, slot, progress age, queue age, field revision and per-stage time. Report all-unit completion, worst meaningful stationary interval, path length, final envelope, maximum penetration and CPU p50/p95/p99. Use release Burst builds on a named CPU with fixed package versions; report frame-time and simulation-time distributions separately.

| Rank | Experiment | Pass / fail measure **[I]** |
|---|---|---|
| 1 | Geometry and commit audit | Reproduce the 36.87° transition for rotating squares, distinguish fixed footprints, and reject adversarial simultaneous/swept collisions. Zero newly positive penetration from a disjoint start |
| 2 | Factorial replay of both jams | Compare current, smoothing only, rotation-release only, stable slots only, and combined coordination. Pass 110/110 beyond the wall and 100/100 legally settled outside the ring; no timeout counted as arrival |
| 3 | Terminal assignment and stop states | 100 small units within a footprint-inclusive envelope ≤50 cells²; no overlap; each settled unit stops within 60 ticks. Stop command within 15 ticks; hold position bitwise unchanged |
| 4 | Fair merge, gap and crossing | 200 through the three-cell gap; opposing groups of 100 cross. In declared feasible fixtures, every unit completes and no meaningful stationary interval exceeds 150 ticks. Log queue service separately from solver deadlock |
| 5 | Steering versus ORCA | Same fields, slots, priorities and validator. ORCA passes only if it improves hard-case completion/throughput or visible oscillation without violating the movement budget; test proxy-overlap initial states explicitly |
| 6 | Mixed-speed group and formation | 50 small + 10 large travel 50 cells; first-to-last arrival spread ≤90 ticks. Attack-move footprint-inclusive depth ≤2× declared initial depth in geometrically feasible fixtures; no mandatory pre-assembly delay |
| 7 | Yielding, enemies and recovery | Idle friend never exceeds two cells from home and returns after traffic clears; hold never moves; enemies receive no yielding obligation. Recover feasible deadlocks, explicitly report infeasible ones |
| 8 | Route quality and congestion | Independent obstacle/footprint sweep validation; static route length ≤1.25× the chosen shortest-path oracle for every test, not just average. Report executed traffic detours separately; reject route-flapping regressions |
| 9 | Throughput, scale and lockstep | 2,000 cross-map units at 60 fps on the named reference PC, zero overlap/allocation, provisional movement p99 ≤1.5 ms. Repeat 5,000/10,000 to find scaling limits; compare per-tick state hashes across intended CPUs and worker counts |

**[I] Define “starts within 0.1 seconds” as physical useful displacement within three simulation ticks**, measured from the agreed command-acceptance tick, and report input/network delay separately. Do not pass it by setting a moving flag, oscillating in place or rotating only. Test background-field bursts and valid provisional movement. If a unit has no safe initial displacement because it is enclosed by holds or fully packed obstacles, classify that fixture as physically infeasible rather than weakening safety.

**[I] Define waiting without rewarding jitter.** Track sustained displacement toward a valid route/assigned recovery step, not total absolute motion. A unit can have a topological route yet no immediately available capacity. A fair queue can exceed five seconds; to enforce the deadline, provision sufficient lanes/refuges, cap admitted demand or redesign the fixture. At ideal zero-margin spacing, six half-cell lanes across three cells moving at four cells/s would carry at most 48 units/s; real clearance, entry and turning reduce that capacity. This optimistic bound is a sanity check, not a throughput prediction.

**[I] Define the 1.25× baseline before choosing a planner.** Optimal paths on the existing clearance grid are different from optimal continuous paths of a rotating square. A grid A* oracle certifies only its grid model; even a high-resolution reference remains an approximation to continuous motion. A certified continuous bound needs an exact optimum for the chosen shape model, or a valid lower bound `L` on that optimum with candidate length ≤`1.25 L`. Theta* and smoothing need empirical stretch checks unless such certification is supplied. Congestion and required backing-up can break a bound on total executed distance even when the static route passes.

**[I] Define arrival as a state transition, not first contact with a goal radius.** Measure three-second group spread using final slot settlement and measure the two-second stop bound from entry into the agreed approach/arrival region. If the contract instead starts timing only once a unit is already motionless, that test proves nothing.

**[I] Treat impossible fixtures as specification tests.** A hold unit sealing the sole gap, an enemy refusing passage, a missing refuge within the home radius, an undersized terminal region, or a corridor whose capacity requires depth above the limit should produce a clear blocked outcome. Finite-time arrival under stated assumptions and performance on a declared suite are achievable targets; unconditional liveness for all such configurations is not.

## 10. Source guide and reuse boundaries

All technical external claims above link to primary material. The following groups identify the most useful sources for architecture review; source dates describe the evidence, not an assertion about the latest game build.

- **Navigation architecture:** [Fitch, Blizzard AIIDE 2011 slides](https://movingai.com/aiide11/BlizzardAI.zip); [Emerson, SupCom2 flow-field tiles](https://www.gameaipro.com/GameAIPro/GameAIPro_Chapter23_Crowd_Pathfinding_and_Steering_Using_Flow_Field_Tiles.pdf); [PA shipped navigation notes](https://store.steampowered.com/news/posts/?appids=233250&enddate=1423780422); [Cheng, AoE IV GDC 2022](https://media.gdcvault.com/GDC%2B2022/Speaker%2BSlides/Pathing%2BIn%2BAge_Cheng_Frank%2B2022-03-29%2B00.16.38.pdf); [Numantian’s TAB update](https://www.numantiangames.com/News/2018-06-07-they-are-billions-development-update-v-0-8-2/).
- **Formations and feel:** [Jurney, original CoH formations](https://forum.arongranberg.com/uploads/short-url/rWod3K2KhNWcOEdjewnsLXQKU6A.pdf); [AoE II DE update 153015](https://www.ageofempires.com/news/age-of-empires-ii-definitive-edition-update-153015/); [Relic, CoH3 turn plans](https://help.relic.com/hc/en-us/articles/39572638372755--PC-Coral-Viper-1-6-0-Pathfinding-Improvements); [Warhammer siege scheduling](https://media.gdcvault.com/gdc2016/Presentations/Arsenault_Andre_Have_Fun_Storming.pdf).
- **Local motion research:** [ORCA](https://gamma-web.iacs.umd.edu/ORCA/); [RVO2 parameter caveats](https://gamma-web.iacs.umd.edu/RVO2/documentation/2.0/params.html); [continuum crowds](https://grail.cs.washington.edu/projects/crowd-flows/continuum-crowds.pdf); [position-based crowds](https://arxiv.org/html/1802.02673v2); [Reynolds steering](https://www.red3d.com/cwr/steer/gdc99/); [social forces](https://arxiv.org/abs/cond-mat/9805244).
- **Routing and progress research:** [Theta*](https://arxiv.org/abs/1401.3843); [PIBT](https://arxiv.org/abs/1901.11282); [local MAPF recovery](https://arxiv.org/abs/2107.00246); [earlier Theta*–ORCA hybrid](https://arxiv.org/abs/2008.01227). These establish prior art and conditional results, not the studio’s performance or guarantees.
- **Implementation ideas only, GPL:** [Recoil ground movement](https://github.com/beyond-all-reason/RecoilEngine/blob/master/rts/Sim/MoveTypes/GroundMoveType.cpp); [0 A.D. motion](https://github.com/0ad/0ad/blob/master/source/simulation2/components/CCmpUnitMotion_System.cpp); [EA Tiberian Dawn pathfinding](https://github.com/electronicarts/CnC_Tiberian_Dawn/blob/main/FINDPATH.CPP). Do not copy or port their code into the permitted-code implementation.
- **Permissive reference and toolchain:** [official RVO2 Apache-2.0 license](https://github.com/snape/RVO2/blob/main/LICENSE); [Burst changelog](https://docs.unity3d.com/Packages/com.unity.burst@1.8/changelog/CHANGELOG.html); [Burst numeric determinism documentation](https://docs.unity3d.com/Packages/com.unity.burst@1.8/manual/compilation-burstcompile.html). Pin the actual versions evaluated.
- **Evidence gaps explicitly retained:** exact player-group arrival rules in SC2, detailed local collision/formation handling in PA, and RA2’s internal movement solver remain unverified here. [RA2’s manual](https://static.wikia.nocookie.net/cnc_gamepedia_en/images/b/b5/Red_Alert_2_-_English.pdf/revision/latest?format=original) supports its controls, not a named internal algorithm.
