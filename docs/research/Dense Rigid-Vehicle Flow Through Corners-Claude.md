# Dense Rigid-Vehicle Flow Through Corners

Nov 4, 2026 · @Moe

## Bottom line

No published method guarantees progress for exactly your setting: rigid squares, no sliding, a shared field, and decisions from start-of-tick poses only. Every guarantee I found adds one thing you lack — discrete slots on a graph, a per-vehicle path, or a single agreed order of passage. Each of those can be built locally and deterministically at a corner, so the honest answer is "nothing fits off the shelf, but three pieces adapt cheaply."

What your measurements most likely show **\[Inference\]**:

- **The jam-head refusals are priority inversion.** A vehicle yields to one that cannot move. Railways, roundabouts and warehouse planners all fix this the same way: right of way goes to whoever can actually use it. That needs a second pass inside the tick, not another rule inside the one pass.
- **Most of the 5x throughput gap is the stop-and-pivot at the corner, plus all-or-nothing moves.** In the 2023 Amazon-sponsored robot competition, the winning team found the rotate-on-the-spot action model far more prone to congestion. Switching to four-way moves turned their planner from worse than simple PIBT into 185–192% better on three large maps **\[Sourced, below\]**.
- **Directional guidance beat clever coordination.** Hand-tuned lane weights roughly tripled warehouse throughput in the same competition **\[Sourced\]**.
- **Unpacking is hard in general but easy on your lattice.** Moving rigid blocks along their own axis is PSPACE-complete in general **\[Sourced\]**. At 1.25x spacing, a two-colour checkerboard can rotate half the block at once **\[Inference, arithmetic in Q4\]**.

What fits your limits, in the order I would try it:

1. Two admission rules from road traffic: do not enter a corner zone unless your exit is clear, and vehicles already in the zone have priority. Deployed for decades, cost near zero.
2. Replace all-or-nothing moves with the largest safe step, and let vehicles turn while moving where your rules allow it.
3. A deterministic multi-pass tick: intent, priority key, greedy resolve in key order with priority inheritance, then commit. The greedy result can be computed in parallel and stays identical to the sequential one **\[Sourced\]**.
4. Lanes through each corner as a second guidance field, and a wait-for-cycle detector as a backstop.

What does not fit as-is: PIBT as published, because its guarantee assumes one agent per lattice slot and your mixed sizes break that; and the ORCA or barrier-function families (circular footprints and known deadlocks).

## How to read this

Every claim carries one of three labels, so you can see what rests on a source and what rests on my reasoning.

- **\[Sourced\]** — stated in a primary source I opened (paper, standard, agency page). Linked in place and in Sources.
- **\[Inference\]** — my own reasoning or arithmetic applied to your setup. Treat as a hypothesis to test.
- **\[Unverified\]** — something I believe from memory but could not confirm from a page I opened.

Your constraints, as I use them below:

| Constraint | Value |
| --- | --- |
| Tick | 30 Hz (dt = 33.3 ms) |
| Footprints | Squares, side 0.5 and 1.0 cell; no overlap ever |
| Top speed | 4.0 cells/s (0.133 cell/tick) and 2.5 cells/s (0.083 cell/tick) |
| Motion | Translate along heading only; rotate on the spot |
| Guidance | Shared distance-to-goal field; no per-vehicle paths |
| Decision input | Neighbours' start-of-tick poses |
| Determinism | Byte-identical across runs and thread orders; no run-time randomness |
| Budget | Under 0.5 ms for the whole move check at 2,000 vehicles |

One geometric fact recurs throughout **\[Inference, arithmetic\]**: a square of side s sweeps a disc of radius s × 0.707 when it rotates. That is 0.354 for the small square and 0.707 for the large one.

## Q1. Methods that guarantee or support progress

Safety (no overlap) is easy to guarantee from start-of-tick poses alone; progress is not. Every progress guarantee below needs either a graph of slots, a path per vehicle, or an agreed order at a shared zone.

| Method | What it guarantees | Evidence | Deterministic, order-independent | Fits 0.5 ms at 2,000 |
| --- | --- | --- | --- | --- |
| 1. Space splitting (buffered Voronoi cells) | No collisions; no progress | Proof, simulation, 5-quadrotor trials | Yes, by construction | Likely **\[Inference\]** |
| 2. Zone admission rules (box junction, circulating priority, route locking) | No gridlock inside the zone | Deployed: road law, rail interlocking | Yes, with a fixed tie-break key | Yes |
| 3. Priority inheritance with backtracking (PIBT) | Every agent reaches its goal, on biconnected graphs | Proof, simulation, robots, competition win | Deterministic; sequential by design | Cost likely yes; its slot model does not fit |
| 4. Space-time reservation (tiles or time windows) | Collision- and deadlock-free for reserved routes | Container-terminal data; intersection simulation | Yes, if granted in key order | Yes, at corners only |
| 5. Critical sections along known paths | Safe and deadlock-free under delays | Proofs, simulation, lab robots | Yes | No at fleet scale |
| 6. Wait-for-cycle detection and recovery | Detects deadlock; recovery is a policy | Rail simulators, terminal simulations | Yes | Yes |

### 1. Space splitting (buffered Voronoi cells)

- **Each vehicle:** computes a separating line to each neighbour from start-of-tick positions, shrinks its side by its own size, and moves only inside the result.
- **Per tick:** neighbour positions only; O(k) in neighbours, the same order as ORCA **\[Sourced: [Zhou et al. 2017](https://msl.stanford.edu/bibliography/zhou_fast_2017.html)\]**.
- **Deterministic:** yes, natively. Both sides of a pair compute the same line, and nothing depends on who is processed first.
- **Evidence:** a proof of collision avoidance for single-integrator robots, simulation against ORCA, and over 70 trials with five quadrotors **\[Sourced\]**. Deadlock is left to right-hand-rule heuristics in follow-up work **\[Sourced: [Chen et al.](https://arxiv.org/html/2202.06071v4)\]**. A weighted variant gives higher-priority agents larger cells, so they rarely yield **\[Sourced: [Pierson et al.](https://sites.bu.edu/pierson/files/2021/05/pierson2020icra.pdf)\]**.
- **Fit \[Inference, arithmetic\]:** with circumscribed circles it is infeasible for you. A 1-cell square needs radius 0.707, but the bisector at 1.25 spacing is only 0.625 away. A square-aware split works: clamp the translation along the heading so the swept box stays on your side, a 1-D calculation per neighbour. Nose-to-tail, an even split lets large vehicles keep full speed at 1.25x. Small vehicles at 4 cells/s settle to about 1.53x their width.
- **Smallest test:** replace your whole move check with this half-gap clamp and no ordering rules at all, then run the 110-vehicle wall. Zero overlaps is required. If corner stalls fall, your ordering rules were causing them.

### 2. Zone admission rules

- **Each vehicle:** near a corner, treats a precomputed corner polygon as one shared zone. It may enter only if its exit beyond the zone has room for its footprint, and vehicles already inside always have priority.
- **Sources:** UK law says you must not enter a box junction until your exit road or lane is clear **\[Sourced: [Highway Code rule 174](https://content.govdelivery.com/accounts/UKDVSA/bulletins/15a027d)\]**. Early roundabouts locked up under heavy traffic; the cure was making entering traffic give way to circulating traffic **\[Sourced: [roads.org.uk](https://www.roads.org.uk/articles/roundabouts/taming-streets), [archive file](https://archive.roads.org.uk/index.php/DSIR_12/181a)\]**. Britain made that the general rule in November 1966 **\[Sourced: [Hansard](https://api.parliament.uk/historic-hansard/written_answers/1966/nov/02/roundabouts-give-way-rule)\]**, and a Road Research Laboratory study found about 10% more capacity under overload **\[Sourced: [Blackmore](https://swov.nl/nl/publicatie/priority-roundabouts)\]**.
- **Rail equivalent:** a route is locked once a train passes its signal, and sectional release frees each section as the train clears it, so the next route can be set sooner **\[Sourced: [EP3594085](https://data.epo.org/publication-server/rest/v1.0/publication-dates/20200115/patents/EP3594085NWA1/document.html), [IEC 821-05-20](https://electropedia.org/iev/iev.nsf/17127c61f2426ed8c1257cb5003c9bec/e489af9d41d12ecfc1257ff10051f390)\]**. A worked example in a train-platforming paper has the second train occupying its route up to 60 s earlier under sectional release **\[Sourced: [Lagrangian platforming paper](https://arxiv.org/pdf/2405.01438)\]**.
- **Per tick:** a zone occupancy mask and an exit-buffer check; O(1) for vehicles near corners.
- **Deterministic:** yes, if simultaneous entrants are ranked by a fixed key from start-of-tick state.
- **Fit:** excellent and nearly free. It stops the zone itself from locking, but you still need a fair rule among entrants (Q2).
- **Smallest test:** treat one end of the wall only and compare stalls and flow with the untreated end in the same run.

### 3. Priority inheritance with backtracking (PIBT)

- **Each vehicle:** ranks candidate next cells by field distance and takes the best free one. If a lower-priority vehicle is in the way, that vehicle inherits the higher priority and must move first; if it cannot, the search backtracks to the next candidate **\[Sourced: [Okumura et al.](https://arxiv.org/abs/1901.11282v3)\]**.
- **Guarantee:** every agent reaches its destination in finite time, regardless of count, on graphs where each adjacent pair lies on a cycle of length 3 or more **\[Sourced\]**. Communication is local, within two hops **\[Sourced: [arXiv v1](https://arxiv.org/abs/1901.11282v1)\]**.
- **Per tick:** a slot lattice and a priority per agent; recursive, processed in priority order.
- **Deterministic:** yes, given a total order and fixed tie-breaks. It is sequential; see Q2 for a parallel form with identical output.
- **Evidence:** proof, simulation, a robot demonstration, and the core of the winning 2023 League of Robot Runners entry **\[Sourced: [Jiang et al.](https://arxiv.org/html/2404.16162v1)\]**. With rotate-on-the-spot actions, the winners had to plan the next location first and then take only the first action toward it **\[Sourced\]**.
- **Fit \[Inference\]:** the proof covers single-slot agents only, so your mixed 0.5 and 1-cell sizes are outside it. Per timestep PIBT costs O(|A|·(Δ + F + log|A|)), and groups of agents that cannot interact may be planned in parallel \[Sourced: PIBT full text\]. The competition's 250 ms for 10,000 agents covers multi-step windows and per-goal distance tables you do not need \[Sourced: Jiang et al.\], so it overstates your cost. Cost is probably not the blocker; the slot model is. Use it only inside conflict clusters at corners, and only when a vehicle reaches a slot boundary.
- **Smallest test:** offline, with no time limit, run PIBT on a 0.5-cell lattice copy of the wall using only small vehicles. If even this cannot beat about 1 vehicle/s per corner, the limit is geometry and rotation, not coordination.

### 4. Space-time reservation

- **Each vehicle:** within some range of a corner, requests the space-time cells its footprint will sweep through the corner at its planned speed. A manager grants the request only if none are taken; a refused vehicle stops before the zone.
- **Sources:** the intersection version divides the junction into a grid of tiles, simulates each request, and serves first come, first served. Against a traffic light it cut delay and greatly increased throughput, in simulation **\[Sourced: [Dresner and Stone](https://www.cs.utexas.edu/~aim/papers/IJCAI07-kurt.pdf)\]**. At the Altenwerder container terminal in Hamburg, a time-window router gave each lane segment a polygon, blocked all intersecting segments together, and avoided collisions, deadlocks and livelocks at route-computation time **\[Sourced: [Gawrilow et al.](https://core.ac.uk/download/484089629.pdf)\]**. Average computation was a few hundredths of a second per request, with a maximum under half a second **\[Sourced\]**. The same algorithm was later adapted to airport taxiing **\[Sourced: [Stirling paper](https://www.stir.ac.uk/research/hub/file/894758)\]**.
- **Per tick:** a small per-corner bit table over a short horizon; rasterising one swept footprint per request.
- **Deterministic:** yes, if requests are granted in key order rather than in thread arrival order.
- **Evidence:** terminal data showed an explicit advantage over the static router CTA used **\[Sourced\]**. Whether the new router went into production I could not verify **\[Unverified\]**.
- **Fit \[Inference\]:** good at corners only. A 3x3-cell corner at 0.25-cell tiles is 144 tiles; 30 time slots make 4,320 bits. A granted vehicle blocked downstream breaks the plan, so pair it with the exit-clear rule.
- **Smallest test:** one corner, a 1-second horizon, constant speed through the zone. If single-lane flow stays under 2 vehicles/s, reservation is not your bottleneck fix.

### 5. Critical sections along known paths

- **Each vehicle:** follows a known path. Wherever its swept envelope overlaps another's, a precedence rule decides who crosses first, and the other may advance only to a critical point before the overlap **\[Sourced: [coordination\_oru port announcement](https://discourse.openrobotics.org/t/multi-robot-coordination-package-python-port-of-orebro-universitys-coordination-oru/56705)\]**.
- **Guarantees:** the fleet stays in a safe state with no deadlocks while goals arrive online **\[Sourced: [Mannucci et al. 2021](https://katalog.lib.cas.cz/KNAV/EdsRecord/edb,153953178)\]**. A related control law is proven safe and deadlock-free even when robots are delayed **\[Sourced: [Čáp, Gregoire, Frazzoli](https://ar5iv.labs.arxiv.org/html/1603.08582)\]**.
- **Per tick:** pairwise critical sections, recomputed when paths change; precedence revisions are cheap.
- **Deterministic:** yes, if precedence revision uses a fixed key.
- **Evidence:** proofs and simulation; the related-work table in one paper lists tests of up to 100 robots in simulation **\[Sourced: [Mannucci et al. 2019](https://www.centropiaggio.unipi.it/sites/default/files/provably_safe_multi_robot_coordination_with_unreliable_communication.pdf)\]**.
- **Fit \[Inference\]:** you have no paths. You could roll the field forward two seconds per vehicle, but pairwise envelope overlaps at thousands of vehicles will not fit the budget. Its useful core is "one shared zone, one agreed order, wait at a fixed point", which is Methods 2 and 4.
- **Smallest test:** none worth running separately; test Methods 2 and 4.

### 6. Wait-for-cycle detection and recovery

- **Each vehicle:** records the single vehicle that blocks its intended move. Cycles in that graph are deadlocks; each is broken by a fixed key.
- **Sources:** railway simulators avoid deadlock with variants of the banker's algorithm. Detection followed by rollback is too inefficient, and prevention over a whole simulation is impractical **\[Sourced: [JRTPM summary](https://unpaywall.org/10.1016%2Fj.jrtpm.2017.05.001)\]**. A myopic look-ahead test is insufficient at medium or high train densities **\[Sourced: [Simon et al.](https://www.cslog.uni-bremen.de/bsimon/files/Papers/2014-SJL-TRR.pdf)\]**. Container-terminal work detects deadlock by a resource matrix or by tracing requests, then resequences or reassigns jobs **\[Sourced: [Lehmann et al.](https://econpapers.repec.org/RePEc:spr:sprchp:978-3-540-49550-5_11)\]**.
- **Per tick:** one pointer per vehicle. With one blocker each, the graph is a functional graph and cycles are found in O(n) **\[Inference\]**.
- **Deterministic:** yes.
- **Fit:** excellent, as a backstop and as a measuring instrument.
- **Smallest test:** log the wait-for graph at every stall in the 110-vehicle run. Classify each as a 2-cycle, a chain ending at a vehicle with a clear line held back by rules, or a chain ending at geometry. The mix tells you which fix matters.

## Q2. Who goes first at a merge

The rules that avoid both deadlock and starvation share three parts: occupants beat entrants, entrants are ranked by a key that grows while they wait, and right of way passes only to a vehicle that can use it. Fixed priority by route distance has none of the three, and fails the way you measured.

| Field | Rule | Stops deadlock | Stops starvation | Evidence |
| --- | --- | --- | --- | --- |
| Roundabouts | Entering traffic gives way to traffic already circulating | Yes, inside the ring | No: entry capacity depends on circulating flow and gap acceptance ([TRRL capacity paper](https://trid.trb.org/View/40033)) | Deployed since 1966 |
| Box junctions | Enter only when your exit is clear | Yes: nobody waits inside | No | Deployed law |
| Rail interlocking | A set route stays locked until the train clears each section | Yes, for set routes | Set by timetable or dispatcher | Deployed |
| Railway simulators | Banker's-style or reservation look-ahead before entering the next section | Yes, if the look-ahead is complete; myopic tests fail at high density | Depends on train priorities | Commercial tools ([JRTPM](https://unpaywall.org/10.1016%2Fj.jrtpm.2017.05.001), [Simon et al.](https://www.cslog.uni-bremen.de/bsimon/files/Papers/2014-SJL-TRR.pdf)) |
| Autonomous intersections | First come, first served on space-time tiles | Collision-free by construction | Yes, by arrival order **\[Inference\]** | Simulation ([Dresner and Stone](https://www.cs.utexas.edu/~aim/papers/IJCAI07-kurt.pdf)) |
| Lane closures | Zipper or late merge: use both lanes, take turns at the merge point | Not applicable | Yes, by alternation | Field and simulation, mixed results (below) |
| Warehouse planning (PIBT) | Priority rises each step until the goal, then resets; inheritance and backtracking | Yes, on graphs with cycles | Yes: every agent eventually holds top priority | Proof, simulation |
| Fixed-order prioritized planning | One static order | No: some instances fail for every static order | No | Proof, simulation |

Two details matter for you **\[Sourced: [PIBT full text](https://ar5iv.labs.arxiv.org/html/1901.11282)\]**. PIBT's authors state that prioritisation alone can still cause deadlocks, which is why they add inheritance and backtracking. They also cite an analysis of instances that fail under any static priority order, as the case for dynamic priorities. Fixed-order planning is guaranteed only when no vehicle's start or goal ever blocks another's route **\[Sourced: [Čáp et al.](https://pure.tudelft.nl/ws/files/67074672/07138650.pdf)\]**; a packed corner violates that.

The zipper evidence is weaker than its reputation. A Virginia study found a statistically significant throughput gain in simulation only for three-to-one lane closures. For two-to-one and three-to-two closures, the gain appeared only with at least 20% heavy vehicles **\[Sourced: [Beacher et al.](https://rosap.ntl.bts.gov/view/dot/19648/dot_19648_DS1.pdf)\]**. A Maryland field test found its dynamic late merge beat the conventional merge on total throughput **\[Sourced: [Kang et al.](https://workzonesafety.org/publication/dynamic-late-merge-control-at-highway-work-zones-evaluation-observations-and-suggestions)\]**. Alternation is reliably fair; it is not reliably faster.

### A merge rule for your engine \[Inference\]

Compute one lexicographic key per vehicle from start-of-tick state:

1. Inside a corner zone beats outside it (the roundabout rule).
2. Ticks spent waiting at the zone entry, more first (PIBT's aging; prevents starvation and produces zipper-style alternation on its own).
3. Field distance to goal, smaller first.
4. Vehicle ID.

Then resolve in key order. Each vehicle takes its largest safe move against the moves already granted this tick and the start-of-tick footprints of everyone not yet resolved. If its best move is blocked only by a lower-key vehicle, resolve that vehicle first under the inherited key. A lower-key vehicle that cannot move gets no say, which removes your jam-head refusals.

This breaks your rule that each vehicle decides only from neighbours' start-of-tick poses, and I think that rule has to go for the resolve step. Two contenders can agree on a winner from start-of-tick state alone. Whether the winner can actually move depends on the winner's own neighbours, which is information beyond one hop. PIBT gets it by recursion inside the timestep.

It stays byte-identical across thread orders in any of three ways:

- Run the resolve step on one thread. It is linear in vehicles times neighbours.
- Split vehicles into groups that cannot interact this tick and resolve groups on separate threads. PIBT's authors note that such groups cannot affect each other **\[Sourced: PIBT full text\]**.
- Use the deterministic parallel greedy algorithm, which always returns the same set as the sequential greedy pass in the same order **\[Sourced: [Blelloch, Fineman, Shun](https://arxiv.org/pdf/1202.3205)\]**.

If you must keep strict start-of-tick decisions, the nearest substitute is k synchronous rounds per tick, each reading only the previous round's tentative grants. That is deterministic and order-independent, but news that a leader moved travels only one vehicle per round down a queue.

Why this should avoid starvation, though it is not a proof for continuous geometry: occupants leave in finite time because they entered only with a clear exit. Among entrants, the waiting count makes the longest waiter top eventually, and the top entrant enters when its path clears. That mirrors the structure of PIBT's reachability proof.

## Q3. What raises corner and merge throughput most

The biggest levers in the evidence are not stopping to turn, directional lanes, and keeping vehicles moving slowly instead of stopping. Admission control comes next; reservation helps mainly where two streams cross. Better ordering rules alone recover little.

| Lever | Likely effect at your corners | Evidence strength | Cost to try |
| --- | --- | --- | --- |
| Turn without a full stop | Large, if pivot time dominates headway **\[Inference\]** | Competition simulation, robot execution | Low |
| Largest safe step, not all-or-nothing | Removes the one-tick stops **\[Inference\]** | Robot execution, ring-road field tests | Low |
| Lanes through each corner | Roughly multiplies by lane count **\[Inference\]**; 2–3x on competition maps | Competition simulation, peer-reviewed simulation | Medium |
| Admission control and density caps | About 10–50%, depending on how overloaded | Road field data, competition simulation | Low |
| Space-time reservation | Helps crossing or merging streams more than one stream | Simulation, container-terminal data | Medium |

### 1. Turning without a full stop

Your 1 vehicle/s may be close to the ceiling for a single lane where every vehicle stops and pivots **\[Inference, arithmetic\]**. Headway at a pivot corner is roughly the pivot time plus (length + gap) / speed. For a 1-cell vehicle at 2.5 cells/s with a 0.25 gap, the second term is 0.5 s. A 90-degree pivot of 0.5 s then gives 1 vehicle/s. Plug in your turn rate: if it is near this, coordination cannot fix the corner and only geometry can.

The evidence agrees that turning costs throughput:

- The 2023 competition winners found the rotate-on-the-spot model far more congestion-prone, because changing direction takes extra steps. With four-way moves, their planner beat simple PIBT by 185–192% on three maps where it had lost under rotations **\[Sourced: [Jiang et al.](https://arxiv.org/html/2404.16162v1)\]**.
- Warehouse robots that move 3 m continuously are faster than robots that stop every metre, because of finite acceleration **\[Sourced: [Hönig et al.](https://act.usc.edu/publications/Hoenig_RAL2019.pdf)\]**.
- At the Hamburg terminal, AGVs had to begin turning well before an intersection and needed a long straight segment between curves. The router therefore modelled every permitted turn as its own arc with turning rules **\[Sourced: [Gawrilow et al.](https://core.ac.uk/download/484089629.pdf)\]**.

For you **\[Inference\]**: if a vehicle may rotate and translate in the same tick, follow a heading that leads the field, so the turn is spread over the approach. If it may not, pre-rotate in the last cells before the corner, where the next vehicle is not yet waiting.

### 2. Largest safe step instead of all-or-nothing

Crawling in one-tick stops is the signature of a binary move test **\[Inference\]**. A follower whose gap is a little shorter than a full step stops for a tick, then the gap opens and it moves, and the pattern repeats. Clamping to the largest safe step makes followers trail smoothly. A small target time gap makes them slow before they would have to stop.

Supporting evidence: stop-and-go waves emerge above a critical density even without bottlenecks, and controlling the speed of one vehicle damped them on a ring of 20 or more cars **\[Sourced: [Stern et al.](https://arxiv.org/pdf/1705.01693)\]**. The Hamburg AGVs reserved a tube ahead of them at least as long as their braking distance, so spacing scaled with speed **\[Sourced: Gawrilow et al.\]**.

### 3. Lanes through each corner

A shared distance field sends everyone along the same inside line. Planners that send agents along free-flow shortest paths create congestion; congestion-avoiding guidance gave large throughput gains with over 10,000 agents **\[Sourced: [Chen et al.](https://ojs.aaai.org/index.php/AAAI/article/view/30054)\]**. Optimised guidance graphs improved the throughput of three different planners on eight benchmark maps **\[Sourced: [Zhang et al.](https://www.ijcai.org/proceedings/2024/0035)\]**. Kiva gave its grid edges weights at design time **\[Sourced: [Wurman et al.](https://www.dcsc.tudelft.nl/~sc4081/2018/assign/pap/robert_kiva_paper.pdf)\]**.

The competition numbers are the strongest I found **\[Sourced: Jiang et al., Table 3\]**:

| Instance | PIBT, no guidance | PIBT, manual guidance | Windowed planner, no guidance | Windowed planner, manual guidance |
| --- | --- | --- | --- | --- |
| Warehouse, 8,000 agents | 10.37 | 30.07 | 5.89 | 31.34 |
| Sortation, 10,000 agents | 10.20 | 29.99 | 10.19 | 39.34 |
| City, 3,000 agents | 5.95 | 9.57 | 6.13 | 14.10 |

Units are goals reached per step. For you **\[Inference\]**: run two or three lanes through each corner, spaced at least 0.75 cells for mixed sizes as you measured. Make lane choice an upstream decision so the lane merge happens on a straight, not in the corner. Offset copies of the field, or a cost term by lane, would do it.

### 4. Admission control and density caps

More vehicles can mean less flow. On one competition map, throughput peaked at 2,500 of 4,000 agents; parking the rest restored it, and without that the team lost 15.7%, 51.8% and 47.0% on three instances **\[Sourced: Jiang et al.\]**. On freeways, discharge from a queue can be about 10% lower than the flow before the queue formed **\[Sourced: [Cassidy and Bertini](https://its.berkeley.edu/node/4141)\]**; reported drops range from 3% to 18% **\[Sourced: [Yuan et al.](https://research.tudelft.nl/files/75552687/2491_08.pdf)\]**. The roundabout priority rule added about 10% capacity under overload **\[Sourced: [Blackmore](https://swov.nl/nl/publicatie/priority-roundabouts)\]**.

For you **\[Inference\]**: hold vehicles before a corner zone instead of letting them pack into it. The box-junction rule does this for free.

### 5. Space-time reservation

Reservations at intersections beat traffic lights on throughput by a wide margin, in simulation **\[Sourced: [Dresner and Stone](https://www.cs.utexas.edu/~aim/papers/IJCAI07-kurt.pdf)\]**. Time-window routing beat claim-as-you-go routing on Hamburg terminal data **\[Sourced: Gawrilow et al.\]**. For one stream around one corner, I expect little gain over levers 1–4; it pays where two streams cross or merge inside the zone **\[Inference\]**.

## Q4. Turning packed vehicles and unpacking a tight block

I found almost nothing published on rotating a packed block of vehicles in place. The nearest work, on dense storage and sliding puzzles, says unpacking is hard in general and that working systems keep empty "escort" space and move in a planned order. Your own geometry gives a cheap, deterministic two-phase schedule.

### What is known

- **Hard in general.** Rush Hour, where each vehicle moves only along its own axis and never rotates, is PSPACE-complete in generalised form **\[Sourced: [Flake and Baum via Hearn and Demaine](https://arxiv.org/pdf/cs/0205005)\]**. It stays PSPACE-complete with only 1x2 cars, and with 1x1 cars plus fixed blocks **\[Sourced: [1x1 Rush Hour paper](https://arxiv.org/pdf/2003.09914)\]**. Coordinating rectangles in a rectangular room is PSPACE-hard **\[Sourced: [Čáp et al.](https://ar5iv.labs.arxiv.org/html/1603.08582)\]**. No cheap rule will unpack every configuration.
- **Dense storage works by escorts.** Puzzle-based storage packs items on a grid, possibly with a single empty cell; an item moves only into a neighbouring empty cell **\[Sourced: [summary of Gue and Kim](https://arxiv.org/pdf/2505.22497)\]**. Retrieval time depends heavily on where the empty cells sit **\[Sourced: [Kota, Taylor, Gue](https://www.emerald.com/insight/content/doi/10.1108/JMTM-08-2013-0109/full/html)\]**.
- **Automated garages rearrange ahead of time.** One design reaches near-100% vehicle density by treating parking and retrieval as multi-robot path planning, and finds that rearranging cars off-peak speeds up ordered retrieval later **\[Sourced: [Guo and Yu](https://arxiv.org/pdf/2302.01305)\]**.
- **Warehouse robots rotate as a task step.** Kiva drive units may rotate a pod to present the right face at a station **\[Sourced: [Wurman et al.](https://www.dcsc.tudelft.nl/~sc4081/2018/assign/pap/robert_kiva_paper.pdf)\]**. The 2023 competition winners called dead ends and narrow corridors an open problem and parked excess agents away from them **\[Sourced: [Jiang et al.](https://arxiv.org/html/2404.16162v1)\]**.

### A two-phase checkerboard for your lattice \[Inference, arithmetic\]

A square of side s sweeps a disc of radius 0.707s when it turns 90 degrees. On a grid at 1.25s centre spacing:

| Pair | Needed centre distance | Available | Fits |
| --- | --- | --- | --- |
| One rotating, side neighbour still | 0.707s + 0.5s = 1.207s | 1.25s | Yes, 0.043s spare |
| Both side neighbours rotating | 0.707s + 0.707s = 1.414s | 1.25s | No |
| Both diagonal neighbours rotating | 1.414s | 1.768s | Yes |

So colour the block like a chessboard by row plus column parity. Black squares rotate while white squares hold, then white rotates while black holds. The whole block turns in two pivot times, whatever its size, which matches your finding that all-at-once locks and front-first frees it.

For mixed sizes the same check applies with each vehicle's own extent: 0.354 for a small square turning, 0.5 for a large one holding still. A small square turning beside a still large one needs 0.854 cells.

Off a perfect lattice, use a local-maximum rule. A vehicle may rotate this tick only if no neighbour whose disc overlaps its own is also rotating with a higher key. That is one parallel pass, deterministic and order-independent. With row-plus-column parity as the key it reproduces the checkerboard; with a fixed hash of vehicle ID it gives a different but still deterministic independent set.

Front-first still matters after the turn. Once rotated, only the front row has room to drive, so free space travels backward through the block like an escort. Turning everyone in two phases removes the pivot from that backward wave, leaving only the drive-off time per row.

## Q5. What has been tried and found not to work

The recurring failures are local reactive control in dense symmetric scenes, static priorities, claiming space as you go without look-ahead, and adding vehicles past the flow peak.

| Tried | Where | What happened | Lesson for you |
| --- | --- | --- | --- |
| Reactive collision avoidance (barrier functions, ORCA-style) | Multi-robot control | Deadlock is a force equilibrium on the edge of the safe set; small nudges help, but robots can fall back in ([Grover et al.](https://arxiv.org/pdf/1911.09146)) | Local safety rules alone will keep producing jam-head standoffs |
| ORCA versus planning | Indoor-map simulation | Revised prioritized planning solved many instances where ORCA failed ([Čáp et al.](https://pure.tudelft.nl/ws/files/67074672/07138650.pdf)) | Reactive methods are weakest exactly in narrow, dense places |
| Car-like ORCA | Car-like robots | Still requires a circular robot shape ([Alonso-Mora et al.](https://typeset.io/pdf/reciprocal-collision-avoidance-for-multiple-car-like-robots-2dj5pqk02x.pdf)) | A circle around your 1-cell square needs 1.414 spacing; you pack at 1.25 **\[Inference\]** |
| Right-hand-rule tie-breaks | Voronoi-cell planners | Called heuristics by later work, which added a theoretical argument for an adaptive version ([Chen et al.](https://arxiv.org/html/2202.06071v4)) | A fixed side preference is deterministic but not a guarantee |
| Static priorities | Warehouse planning | Prioritisation alone can deadlock; some instances fail for every static order ([PIBT full text](https://ar5iv.labs.arxiv.org/html/1901.11282)) | Matches your one-lock-then-queue result |
| Claim-as-you-go routing | Hamburg container terminal | Claiming rules caused deadlocks and hurt performance; deadlock fixes worked only for few vehicles ([Gawrilow et al.](https://core.ac.uk/download/484089629.pdf)) | Claim a zone only with its exit, or reserve ahead |
| Myopic look-ahead | Railway simulators | Insufficient at medium or high train density ([Simon et al.](https://www.cslog.uni-bremen.de/bsimon/files/Papers/2014-SJL-TRR.pdf)); rollback after detection too slow ([JRTPM](https://unpaywall.org/10.1016%2Fj.jrtpm.2017.05.001)) | One-step checks at corners will not be enough |
| More optimisation without guidance | 2023 competition | The stronger windowed planner got lower throughput than simple PIBT on four instances; myopia and congestion fed each other ([Jiang et al.](https://arxiv.org/html/2404.16162v1)) | Fix the flow pattern before tuning the solver |
| More vehicles | 2023 competition | Throughput peaked at 2,500 of 4,000 agents on one map (same source) | Cap density near corners |
| Roundabouts without a priority rule | UK roads, 1950s–60s | Prone to locking up as traffic grew ([archive file](https://archive.roads.org.uk/index.php/DSIR_12/181a)) | Occupants-first is what stops zone gridlock |
| Metering signals on a roundabout approach | London, 1960 | Results were not good and the equipment was removed (same source) | Throttling inflow alone did not fix a locking junction |
| Zipper merge everywhere | Virginia work zones | Throughput gains only in some layouts ([Beacher et al.](https://rosap.ntl.bts.gov/view/dot/19648/dot_19648_DS1.pdf)) | Use alternation for fairness, not as a capacity fix |

One more failure is my own inference rather than a published result. Buffered Voronoi cells built from circumscribed circles cannot work at your spacing, because each 1-cell vehicle's buffer exceeds half the gap to its neighbour.

## Test plan

Run these in order; the first two decide whether coordination or geometry is your real limit, and each later test is only worth running if the earlier ones point to it. All use your existing 110-vehicle wall unless stated.

| # | Test | Setup | Falsified if | What a failure means |
| --- | --- | --- | --- | --- |
| 1 | Classify every stall | Log each vehicle's single blocker at every stop; find cycles; tag each stall as 2-cycle, rule-held chain, or geometry-held chain | Fewer than half of stalls are rule-held or 2-cycles | Ordering fixes (Q2) will not move your numbers; go to geometry |
| 2 | Pivot ceiling | One corner, single file, no other traffic; shrink spacing until flow stops rising | Peak flow is well above 1 vehicle/s | Your corner is not pivot-limited; the loss is interaction |
| 3 | Largest safe step | Replace the all-or-nothing move with the largest non-overlapping step along the heading | One-tick stops do not fall by most of their count | Stops come from something other than step quantisation |
| 4 | Half-gap clamp alone | Remove ordering rules; each vehicle may use half the gap to each neighbour (Q1, Method 1) | Any overlap, or stalls rise | Your rules were not the cause of refusals, or the split needs weights |
| 5 | Zone admission | One wall end only: enter the corner zone only with a clear exit; occupants first | Treated end is not better than the untreated end in the same run | Zone gridlock is not your failure mode |
| 6 | Key-ordered resolve with inheritance | Key as in Q2; single thread first | Any 2-cycle survives where one member had a clear line | Inheritance is wired wrong, or the conflict test is too coarse |
| 7 | Two lanes | Two offset fields through one corner at 0.75-cell spacing | Flow is below 1.6x the single-lane ceiling from test 2 | The lane merge is happening too close to the corner |
| 8 | Checkerboard turn | 10x10 block of 1-cell squares at 1.25 spacing, facing away from the route | Turning takes more than about two pivot times | The rotation sweep check is wrong, or vehicles drift off-lattice |
| 9 | Determinism | Rerun tests 5–8 with vehicle arrays shuffled and thread counts changed; hash state every tick | Any hash differs | A decision depends on processing order |
| 10 | Budget | 2,000 vehicles, worst case packed around four corners | Move check exceeds 0.5 ms | Split the resolve step by non-interacting groups (Q2) |

My guess **\[Inference\]**, which test 2 settles cheaply: your corners are close to the single-lane pivot ceiling. If so, lanes and turning on the move will matter more than any priority rule.

## Sources

I read the full text of Wurman et al., Jiang et al., Gawrilow et al. and the PIBT journal version; the rest from abstracts and excerpts.

**Warehouse robots, AGVs and dense storage**

- [Wurman, D'Andrea, Mountz — Coordinating hundreds of cooperative, autonomous vehicles in warehouses (Kiva)](https://www.dcsc.tudelft.nl/~sc4081/2018/assign/pap/robert_kiva_paper.pdf)
- [Okumura et al. — Priority Inheritance with Backtracking, journal version](https://ar5iv.labs.arxiv.org/html/1901.11282) · [arXiv abstract](https://arxiv.org/abs/1901.11282v3)
- [Jiang, Zhang, Veerapaneni, Li — Scaling lifelong MAPF to more realistic settings (League of Robot Runners 2023)](https://arxiv.org/html/2404.16162v1)
- [Chen, Harabor, Li, Stuckey — Traffic flow optimisation for lifelong MAPF](https://ojs.aaai.org/index.php/AAAI/article/view/30054) · [extended abstract](https://ojs.aaai.org/index.php/SOCS/article/view/31573)
- [Zhang et al. — Guidance graph optimization for lifelong MAPF](https://www.ijcai.org/proceedings/2024/0035)
- [Hönig et al. — Persistent and robust execution of MAPF schedules in warehouses](https://act.usc.edu/publications/Hoenig_RAL2019.pdf)
- [Gawrilow, Köhler, Möhring, Stenzel — Dynamic routing of AGVs in real time (Hamburg CTA)](https://core.ac.uk/download/484089629.pdf) · [conflict-free real-time AGV routing](https://ideas.repec.org/h/spr/oprchp/978-3-540-27679-1_3.html)
- [Lehmann, Grunow, Günther — Deadlock handling for AGVs at automated container terminals](https://econpapers.repec.org/RePEc:spr:sprchp:978-3-540-49550-5_11)
- [Gue and Kim, puzzle-based storage, as summarised in a 2025 paper](https://arxiv.org/pdf/2505.22497) · [Kota, Taylor, Gue — Retrieval time in puzzle-based storage](https://www.emerald.com/insight/content/doi/10.1108/JMTM-08-2013-0109/full/html)
- [Guo and Yu — Automated garage design](https://arxiv.org/pdf/2302.01305)

**Continuous-space multi-robot coordination**

- [Zhou, Wang, Bandyopadhyay, Schwager — Buffered Voronoi cells](https://msl.stanford.edu/bibliography/zhou_fast_2017.html)
- [Pierson et al. — Weighted buffered Voronoi cells](https://sites.bu.edu/pierson/files/2021/05/pierson2020icra.pdf)
- [Chen et al. — MPC with deadlock resolution](https://arxiv.org/html/2202.06071v4)
- [Grover, Liu, Sycara — Deadlock analysis and resolution for multi-robot systems](https://arxiv.org/pdf/1911.09146)
- [Alonso-Mora et al. — Reciprocal collision avoidance for car-like robots](https://typeset.io/pdf/reciprocal-collision-avoidance-for-multiple-car-like-robots-2dj5pqk02x.pdf)
- [Čáp, Novák, Kleiner, Selecký — Prioritized planning algorithms](https://pure.tudelft.nl/ws/files/67074672/07138650.pdf)
- [Čáp, Gregoire, Frazzoli — Safe and deadlock-free execution under delays](https://ar5iv.labs.arxiv.org/html/1603.08582)
- [Mannucci, Pallottino, Pecora — Safe and live coordination with online goal posting (record)](https://katalog.lib.cas.cz/KNAV/EdsRecord/edb,153953178) · [with unreliable communication](https://www.centropiaggio.unipi.it/sites/default/files/provably_safe_multi_robot_coordination_with_unreliable_communication.pdf)
- [coordination\_oru Python port announcement](https://discourse.openrobotics.org/t/multi-robot-coordination-package-python-port-of-orebro-universitys-coordination-oru/56705)

**Rail signalling and railway simulation**

- [Simon, Jaumard, Le — Deadlock avoidance and detection in railway simulation](https://www.cslog.uni-bremen.de/bsimon/files/Papers/2014-SJL-TRR.pdf)
- [Deadlock resolution in synchronous railway simulation (JRTPM 2017)](https://unpaywall.org/10.1016%2Fj.jrtpm.2017.05.001)
- [EP3594085 — Route release in computer interlocking](https://data.epo.org/publication-server/rest/v1.0/publication-dates/20200115/patents/EP3594085NWA1/document.html) · [IEC 821-05-20 route section](https://electropedia.org/iev/iev.nsf/17127c61f2426ed8c1257cb5003c9bec/e489af9d41d12ecfc1257ff10051f390)
- [Train platforming with route locking and sectional release](https://arxiv.org/pdf/2405.01438)

**Road traffic engineering and intersections**

- [Highway Code rule 174, box junctions (DVSA)](https://content.govdelivery.com/accounts/UKDVSA/bulletins/15a027d)
- [Roundabouts: taming the streets](https://www.roads.org.uk/articles/roundabouts/taming-streets) · [DSIR 12/181a archive file](https://archive.roads.org.uk/index.php/DSIR_12/181a) · [Hansard, 2 November 1966](https://api.parliament.uk/historic-hansard/written_answers/1966/nov/02/roundabouts-give-way-rule) · [Blackmore, Priority at roundabouts](https://swov.nl/nl/publicatie/priority-roundabouts) · [Roundabout capacity (TRRL)](https://trid.trb.org/View/40033)
- [Dresner and Stone — Sharing the road (reservation intersections)](https://www.cs.utexas.edu/~aim/papers/IJCAI07-kurt.pdf)
- [Beacher, Fontaine, Garber — Late merge evaluation (Virginia)](https://rosap.ntl.bts.gov/view/dot/19648/dot_19648_DS1.pdf) · [Kang, Chang, Paracha — Dynamic late merge (Maryland)](https://workzonesafety.org/publication/dynamic-late-merge-control-at-highway-work-zones-evaluation-observations-and-suggestions)
- [Cassidy and Bertini — Traffic features at freeway bottlenecks](https://its.berkeley.edu/node/4141) · [Yuan, Knoop, Hoogendoorn — Capacity drop](https://research.tudelft.nl/files/75552687/2491_08.pdf)
- [Stern et al. — Dissipation of stop-and-go waves](https://arxiv.org/pdf/1705.01693)

**Airport ground movement**

- [Fuzzy airport ground movement paper, noting the AGV time-window algorithm's adaptation to taxiing](https://www.stir.ac.uk/research/hub/file/894758)

**Complexity and deterministic parallelism**

- [Hearn and Demaine — PSPACE-completeness of sliding-block puzzles](https://arxiv.org/pdf/cs/0205005) · [1x1 Rush Hour with fixed blocks](https://arxiv.org/pdf/2003.09914)
- [Blelloch, Fineman, Shun — Greedy sequential MIS and matching are parallel on average](https://arxiv.org/pdf/1202.3205)

### Could not verify

- Whether the Hamburg time-window router went into production; the paper compares it on terminal data and keeps the numbers confidential.
- Industrial deployments of the Örebro coordination framework; I saw only an announcement post.
- Any primary source on coordinating many cars in automated valet parking or vehicle yards, beyond the automated-garage paper.
- Airport detail beyond the algorithm's adaptation to taxiing; I found no deployment description.
- Your engine's turn rate, which the pivot-ceiling arithmetic in Q3 depends on.
- Timing of any proposal here in your engine; all cost figures for your setup are estimates.
