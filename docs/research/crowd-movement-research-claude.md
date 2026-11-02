# Crowd Movement for a Large-Scale Grid RTS: Fixing Packed-Group Jams Without Giving Up the No-Overlap Guarantee

Your jams are mostly caused by how the move check is ordered and stepped, not by the route geometry: at 30 Hz a small unit moves 0.133 cells per tick, which is more than the 0.125-cell gap in a block packed at 0.625 spacing, so any follower whose move check runs before its leader's is refused every tick. Rows turning by more than about 37° then lock against each other sideways. Fix the acceptance stage first (front-to-back ordering, multi-pass acceptance, partial steps, travel spacing). After that, add wall clearance in the field, arrival slots and priority rules. Treat any-angle routing as a quality improvement, not the cure.

## TL;DR

- **Root cause (inference from your numbers, checked arithmetically):** the move check accepts moves one at a time in unit-index order. A follower whose index comes before its leader's sees the leader still in place. Its 0.133-cell step overruns the 0.125-cell gap, so the move is refused. At corners, side neighbours whose lateral clearance (0.625 × cos θ) drops below the 0.5-cell width lock against each other. That threshold is 36.9°, which matches your measured 37°.
- **Most likely fix for both measured cases:** keep the no-overlap gate, but change the acceptance stage:
  - run it in deterministic front-to-back order (sorted by integration-field distance);
  - run 2–3 acceptance passes per tick;
  - accept the longest safe fraction of a refused move, then try a sideways slide;
  - open travel spacing to about 0.72–0.75 cells before the first turn;
  - add a wall-clearance cost band;
  - send unreachable or crowded goals to a precomputed slot set.

  Then add priority rules (hold position, idle friendly units, enemies) and a deadlock escalator that replaces the 5-second "settle" rule.
- **Any-angle routes help only partly:** turns of about 23° clear the 37° lock but turns of about 42° do not. Any-angle paths also put the whole turn at the corner vertex, which makes corner merging worse. Adopt line-of-sight smoothing for route quality and the 1.25× rule. Expect the real liveness gains from local resolution. Use ORCA as an optional velocity planner, not as the safety layer.

---

## 1. Summary: top recommendations, in order

1. **Reorder the move check from front to back, and run it in multiple passes.** Sort each tick's movers by the integration-field value of their current cell (distance to goal), using unit index as the tie-break. Accept moves in that order. Run 2–3 passes, so a unit refused in pass 1 can retry once the unit ahead has moved. This stays deterministic and sequential. Of everything here, it is the cheapest change that removes the "follower blocked by a leader that hasn't moved yet" failure. [Inference, high confidence; the arithmetic is below.]
2. **Accept partial moves and a slide before refusing outright.** If the full step is rejected, try a shorter step (for example 3/4, 1/2 or 1/4 of it, in fixed point). Then try the component of the step tangent to the blocking neighbour or wall. Your guarantee ("overlap never grows") holds for each candidate, because every candidate goes through the same check. [Inference; Recoil's changelog documents a similar slide-along-edges behaviour against static objects.]
3. **Use a travel spacing for packed groups, and a separate arrival spacing.** Before or while starting to move, let a packed block open from 0.625 to at least 0.71 cells (0.5 / cos 45°). That makes a 45° turn passable sideways. Close up again only at arrival slots. [Inference from the geometry.]
4. **Add a wall-clearance cost band to the field build.** Add cost to cells within 1–2 cells of a wall or corner. Supreme Commander 2 did something similar, running a blur pass that added a cost gradient near walls to improve flow down hallways and around jagged edges. This spreads the stream around corners instead of pinning it to the corner vertex. [Shipped and documented for SupCom 2; the effect on your jam is inference.]
5. **Build arrival slots on a reachable-cell flood, and add a crowded-goal stop.** For the ring case, compute the corrected goal and flood outward over reachable cells only. Hand out slots in flood order, then assign units to slots. Units whose goal region is already full stop early instead of pushing. [Inference; the "stop when the destination area is dense" rule appears in a StarCraft-2-inspired third-party implementation.]
6. **Add priority and share rules, then a deadlock escalator.**
   - Hold position never moves.
   - Idle friendly units are pushed aside and walk home.
   - Moving units ignore same-direction movers when steering.
   - Enemies don't yield.
   - Replace the 5-second settle rule with a staged escalator: re-route with a congestion cost, then make a sideways step, then swap slots or priorities, and finally (if you want) allow a bounded overlap only with friendly idle units.

   [StarCraft II's rules are documented second-hand; the escalator is inference.]
7. **Add any-angle smoothing (line-of-sight shortcuts on your existing field) for route quality, not for liveness.** [Research plus inference.]
8. **Make ORCA (or a cheaper sampled velocity planner) optional, and only after steps 1–6 are measured.** It reduces how often the gate refuses a move. It does not guarantee progress, and it is the hardest piece to make deterministic. [Published research plus inference.]
9. **Prototype a sub-cell reservation backbone with PIBT-style priority inheritance** as the long-term liveness guarantee for chokepoints and crossings (Novel idea A). [Research plus speculation.]

---

## 2. Keep, change, add

### Keep

- **One shared flow field per order, goal and size class.** This is the same design family as Supreme Commander 2. Emerson's Game AI Pro chapter on it says agents move instantly regardless of path complexity. Your measured 6 ms p95 build on a background job fits your 0.1 s start requirement (3 ticks). Units should start moving straight away using direct seek or the previous field while the new field builds; Emerson's design does the same thing with portal positions. [Shipped and documented for SupCom 2.]
- **Line-of-sight direct seek near the goal.** SupCom 2 used an LOS pass so that agents within line of sight ignore the flow field and steer straight toward the exact goal position. [Shipped and documented.]
- **The hard no-overlap gate.** Of the open-source engines that went the other way, 0 A.D. shows the cost. In 0 A.D. Alpha 25, "unit pushing" made movement smoother but allowed overlap. Lead developer wraitii wrote that overlap was not a desired outcome but was much harder to prevent, and players complained that chokepoints lost their strategic relevance. Your gate is the right foundation for an RA2-style game where chokepoints matter. [Shipped and documented, from 0 A.D. forum posts by the developers.]
- **Correcting an unreachable goal once.** SupCom 2 used island IDs to check reachability quickly. Keep the single correction, but feed it into the slot flood (see section 7).
- **Integer, index-ordered sequential stages.** Keep them. Determinism is the main constraint on every method below.

### Change

- **Acceptance order:** use front-to-back order (integration value, then index), not index only.
- **Acceptance passes:** run 2–3 passes per tick, not one.
- **Refusal semantics:** "refuse the whole move" becomes "longest safe fraction, then slide, then refuse".
- **Packed spawn spacing:** open groups to at least 0.71 cells for travel and close them at arrival.
- **The 5-second group stall rule:** it conflicts with your own requirement that no unit stands still more than 5 s while it has somewhere to go. Use it only as the last stage of an escalator, and log every time it fires as a bug signal.
- **Field cost:** add a wall-proximity cost band. Keep it small (for example +1 to +3 on a base cost of 10) so the 1.25× route limit holds; verify this with the route-length test.

### Add

- Group pace, as planned. Age of Empires IV documents the behaviour: units travelling in formation move at the speed of the slowest unit, with a temporary speed boost to get into position. [Shipped; documented by a fan wiki, medium confidence.]
- Arrival slots on a reachable flood, assigned greedily by rank (section 7).
- Priority and share rules, plus a deadlock detector and escalator (section 6).
- A spatial hash (uniform bins of 1 cell) rebuilt each tick for neighbour queries. Everything except the gate needs it.
- Later: a velocity planner (ORCA or sampled), and the sub-cell reservation experiment.

---

## 3. Method comparison table

Costs are order-of-magnitude estimates for 2,000 units on one core, with about 8–16 neighbours per unit, from a uniform-grid neighbour query. They are inference, scaled from your measured 0.6 ms p99 chain (about 0.3 µs per unit per tick). They are not benchmarks; check them in the sandbox.

| Method | Cost / unit / tick | Memory | Determinism | Fits Burst jobs | Helps which requirements | Main risks |
|---|---|---|---|---|---|---|
| Shared Dijkstra flow field (current) | ~0 per unit (lookup); build ~6 ms per order | 1 byte direction + 2–4 bytes integration per cell per field (~64K cells at 256²) | Easy (integer costs) | Yes (build in a background job) | Fast start, routes never cross obstacles | 8-way artifacts; all paths meet at corner vertices |
| LOS / any-angle smoothing on the field | +0.05–0.2 µs (line check to a parent or target) | Optional parent pointer per cell | Easy with integer Bresenham | Yes | 1.25× route length, feel, shallower turns | Puts the whole turn at the corner vertex |
| Theta* per group | One search per order (similar to A*) | Open list per search | Easy with integer or fixed point | Yes (single job) | Same as above | Per-group, not shared; corners still meet |
| Wall-clearance cost band | 0 per unit; one-time distance transform | 1 byte per cell | Easy | Yes | Corner merging, fewer wall-face jams | Longer routes; can close 1-cell gaps if it adds too much cost |
| Front-to-back multi-pass gate with partial steps and slide | +0.2–0.6 µs (sort plus up to 3 passes) | Sort keys, 8 bytes per unit | Easy (sequential) | The sort and the proposals parallelise; acceptance stays sequential | Packed starts, merges, the 0.1 s start, no overlaps | Sequential stage caps scaling at around 10k units |
| Boids / steering (separation, alignment, cohesion) | 0.2–0.5 µs | None | Fine in fixed point | Fully parallel (read old state, write own velocity) | Spacing, look | Jitter, oscillation, no guarantees |
| Social force (Helbing) | 0.2–0.5 µs | None | Fine in fixed point; needs small time steps | Fully parallel | Emergent lanes in crossings | Oscillation, "pushing" overlaps, tuning |
| ORCA / RVO2 | 1–3 µs (half-planes plus a 2D linear program) | ~100 bytes per unit scratch | Hard with floats; possible with fixed point | Fully parallel | Crossings, head-on, fewer gate refusals | Freezing in dense crowds, symmetric deadlocks, no liveness guarantee |
| Sampled velocity / context steering | 0.5–2 µs (8–16 samples) | Small | Easy (integer scoring) | Fully parallel | Same as ORCA, simpler | Lower quality in dense crowds |
| Position-based crowds (Weiss et al.) | 0.5–2 µs per iteration × 2–4 | Small | Jacobi version deterministic in fixed point | Jacobi parallel; Gauss-Seidel sequential | Dense packing, smooth contact | Does not guarantee zero overlap; needs your gate on top |
| Continuum crowds | Per group: density splat plus an eikonal solve over the grid every few ticks (ms-scale) | Several float fields per group | Hard (float eikonal) | Jacobi or fast-iterative eikonal solvers parallelise | Congestion-aware routing, crossings | Cost grows with the number of groups; a minimum-distance pass is still needed |
| Congestion-cost re-integration (light version) | 0 per unit; one integer re-build every 0.5–1 s per active order | One density byte per cell | Easy | Yes | Stream splitting, rerouting around jams | Oscillation between routes; build budget |
| Sub-cell reservation with PIBT | 0.2–0.5 µs | One occupancy int per sub-cell (~1 MB at 512² × 4 sub-cells) | Easy (integer, ordered) | Mostly sequential | Liveness at gaps and crossings, no overlaps | Grid-like look; the reachability proof needs graph assumptions |

---

## 4. Question 1: How shipped games handle group movement

### Supreme Commander 2 (Gas Powered Games): flow-field tiles [Shipped and documented, primary source]

Source: Elijah Emerson's Game AI Pro chapter 23, "Crowd Pathfinding and Steering Using Flow Field Tiles".

- **Why they moved off per-unit paths.** In the original Supreme Commander, each unit followed a fixed, one-way A* path. When paths crossed, the code stopped the units and waited for the conflict to resolve, and players spent their time watching and re-clicking.
- **World layout.** The map is split into 10 × 10 m sectors of 1 m grid squares, joined by portal windows. A path request runs "merging" A* over the portal graph so that many sources share portals. Flow tiles are then built per sector and cached by portal ID.
- **Field formats.**
  - Cost field: 8 bits, 255 means wall.
  - Integration field: 24 bits (16-bit cost plus 8 bits of flags).
  - Flow field: 8 bits (a 4-bit direction index plus flags).
  - Roughly half to two-thirds of the pathable space was clear sectors, which share one static clear cost field.
- **Line of sight.** An LOS pass flags cells that can see the goal and traces Bresenham lines from LOS corners. The cost integration then runs only over cells that cannot see the goal.
- **Walls and large units.** The editor ran a blur pass to add a cost gradient near walls. For large units, a wall-cushioning process moved walls outward. Mixed selections used the most restrictive movement type's path for compatible units.
- **Steering and contact.** The chapter recommends storing a path direction vector and blending in new flow directions as agents cross grid squares. Agents could push each other and slide along walls using physics.
- **Lesson for you:** SupCom 2 handled crowding with physics-style soft pushing, not a hard gate. The chapter does not describe chokepoint deadlock handling. Its documented ideas you can use are the wall-cost gradient, wall cushioning by size class and direction blending.

### StarCraft II (Blizzard): navmesh, A* and steering rules [Documented second-hand]

The primary source is James Anhalt's GDC 2011 AI Summit talk "AI Navigation: It's Not a Solved Problem – Yet". It is behind the GDC Vault paywall; I relied on detailed third-party notes (the How to RTS literature review, tchayen.com).

- **Pathing.** A constrained-Delaunay navmesh is rebuilt when buildings change, A* runs on it, and paths are smoothed with the funnel algorithm. Units are not taken into account when building the navmesh.
- **Steering.** Boids-style behaviours: following, flocking, grouping, separation, avoidance and arrival. The notes report that the pathing engine uses no dynamic memory.
- **Avoidance rules.** These are close to your planned share rules:
  - ignore units moving in your general direction, and idle units (which can be pushed out of the way);
  - look ahead and aim at the gap closest to the centre;
  - anticipate oncoming units and turn the same way they do;
  - many rules about who pushes whom.
- **Locked units.** The A* Pathfinding Project (Aron Granberg, a commercial Unity asset, so ideas only) has job types documented as inspired by StarCraft 2's avoidance of locked units. What "locked" means (probably attacking or hold-position units) is my inference.
- **Lesson for you:** SC2's feel comes from rules about who yields, not from a better collision solver. Ignoring same-direction movers is the key rule for packed groups. Your gate effectively treats a same-direction neighbour as an obstacle, and that is exactly the failure.

### Planetary Annihilation (Uber Entertainment) [Weak documentation]

- PC Gamer reported that the second half of an hour-long developer video covered unit pathfinding and how flow fields create a dynamic movement system. Just Push Start (26 March 2013) reported that host Jon Mavor was joined by Elijah Emerson to talk about navigation and flow fields. That is the same Emerson who wrote the SupCom 2 chapter.
- Forum posts and a later blog (Habrador) describe sectors, A* between sectors and flow fields inside sectors. One poster says flow fields are integrated incrementally, with units falling back to the next A* border when they reach a cell that has no direction yet.
- The PA talk named in your brief could not be verified as a primary source. Treat everything here as low-confidence secondary material.
- **Player sentiment:** in the Steam Planetary Annihilation: TITANS discussion "Pathfinding?", players report units getting stuck instead of finding the nearest ramp, and replies advise using smaller groups and avoiding large move orders on maps with a lot of CSG terrain.

### Age of Empires (DE remasters, AoE II, AoE IV)

**AoE: Definitive Edition (AoE I remaster).** Source: Richard Geldreich's blog; he was an engineer on the remaster. [Shipped and documented, primary.]

- They ported Age II's entire obstruction manager into DE. Units use circular obstructions and buildings use square ones.
- Chokepoint behaviours: a "wait" behaviour that rechecks about every second for up to 45 seconds, and a stuck-unit watchdog that notices when a unit has made no forward progress and switches behaviours.
- Straight-line paths are preferred for short ranges, and waypoints can be skipped.
- Villagers switch to a smaller collision radius on contact with friendly villagers. An attempt to let villagers overlap broke the game.
- Geldreich admits the system still has problems with chokepoints.
- **Lesson for you:** shipped games rely on wait-and-watchdog layers. A 45-second wait is far outside your 5-second requirement.

**Age of Empires II (and DE).** Source: reverse-engineering notes from the openage project. [Medium confidence.]

- There are three pathfinders: a long-range mip-mapped one that ignores moving units, a short-range tile pathfinder, and a polygonal convex-hull pathfinder used in combat or when units cross paths.
- Formations split at obstacles such as trees.

**Age of Empires IV.** [Shipped behaviour; fan-wiki documentation, medium confidence.]

- Formations sort unit types (cavalry first, then melee, ranged, siege). All units move at the slowest member's speed, and units get a temporary speed boost to reach their formation positions.
- Units travel in formation only when they are within a few tiles of each other.
- Player complaints focus on units jamming at wall gates, getting stuck on each other and failing to clear the way through a gate.

### Company of Heroes (Relic) [Shipped; patch-note level]

- Company of Heroes 3 patch notes describe several vehicle fixes:
  - vehicles can end their move order at a set distance before a blocker;
  - a front-facing movement preference;
  - vehicles start at a base movement speed instead of slowly ramping up;
  - fixes that reduce how often vehicles pivot in tight spaces.
- The notes say pathfinding changes have deep cascading effects on the rest of the game.
- Player complaints across CoH 1–3 centre on vehicles pivoting or turning round instead of reversing, and on friendly vehicles blocking each other.
- **Lesson for you:** "stop short of a blocker" is a cheap anti-dance rule. Never accept a path that makes a unit turn round to make room.

### Total War (Creative Assembly) [Not documented in sources I could verify]

I found no primary technical source on Total War regiment movement. My general knowledge is that units move as formation blocks with per-soldier local collision, but I could not verify it. Treat any claim about it as speculation.

### They Are Billions (Numantian Games) [Mostly undocumented]

The only documented fact is marketing: a custom engine handling hordes of up to 20,000 units in real time. The method is not public. Flow fields toward the colony are a plausible guess, but that is speculation.

### Beyond All Reason / Recoil engine (GPL: ideas only) [Shipped; changelog level]

- Recoil's changelog archive records a rewrite of collision handling between units and static objects, replacing impulse pushing with SC2-style strafing.
- Collision forces on ground units also let them slide along the edges of terrain and structures so pushed units don't get trapped against blockers.
- QTPFS (a quadtree pathfinder) automatically attempts to repath incomplete paths, and BAR's announcement says it adjusts pathfinding resolution on demand.
- A newer engine note mentions moving push-resistant units to a trap-check system.
- There is a repath rate-limit setting (pfRepathMaxRateInFrames). The changelog explains the trade-off: shorter intervals cause more unnecessary repaths for slow units but make it less likely players think a unit is stuck.
- **Lesson for you:** sliding along blockers and rate-limited repathing are mature, shipped patterns.

### 0 A.D. / Pyrogenesis (GPL: ideas only) [Shipped; developer forum posts]

- Alpha 25 (2021) added "unit pushing" (SVN rP25182, followed by fixes including an out-of-sync fix and a fix for pairs of units being allowed to overlap).
- The pathfinder.xml parameters include a pushing Radius, StaticExtension, MovingExtension and MinimalForce, so there are separate push ranges for standing and moving units.
- In January 2022 wraitii merged a change that slows units based on the pushing pressure around them (diff D4439). Players reported that high pressure settings restored the strategic meaning of chokepoints. That comes from a user forum post, so medium confidence.
- **Lesson for you:** soft pushing gives fluid crowds but costs overlap and chokepoint meaning. The "pressure slows you down" idea, though, maps cleanly onto your gate as a speed governor.

### Original Command & Conquer / Red Alert, OpenRA (GPL: ideas only) [Partly documented]

- EA released the Tiberian Dawn and Red Alert source under GPL v3: the Remastered DLL source in 2020 and the original source in 2025. You can read it for ideas, but don't copy it.
- Movement is cell-based. Players widely describe its pathfinding as edge-hugging. A community mod advertises A* pathfinding to remove the edge-hugging.
- OpenRA developers note that in the original games blocking units are nudged out of the way. OpenRA added a change so a player's and allied units move out of the way when blocking production facilities.
- An OpenRA issue (#20972) discusses "grid locking", where two units each wait for the other to move and stay stuck forever. It proposes that units reserve a future cell so followers can move into a cell a leader is vacating.
- **Red Alert 2 itself:** I found no primary technical documentation. Anything about its sub-cell infantry placement or vehicle crowding is from memory or guesswork, and I have not repeated it here as fact.

---

## 5. Question 2: Methods, assessed for your constraints

**Shared flow field plus local steering (your current family).** Every shipped field-based RTS above combines a shared field with a local layer: physics pushing in SupCom 2, pushing in 0 A.D., steering rules in SC2 over navmesh paths. The documented weakness is the 8-way, corner-convergent geometry of the field. Yang, Chen and Dong's "Integration field-based breadth-first search for flow field pathfinding" (Scientific Reports 15:40580, 18 November 2025) notes that Emerson's LOS fix still leaves path deviations outside the line-of-sight region. [Published research.]

**ORCA / RVO2.**

- What it does: van den Berg, Guy, Lin and Manocha's "Reciprocal n-body Collision Avoidance" gives sufficient conditions for collision-free motion by having each agent take half the responsibility for each pairwise avoidance. Each neighbour becomes a half-plane, and the velocity is chosen with a low-dimensional linear program. When the program is infeasible in dense crowds, a 3D LP fallback minimises violation.
- Cost: per unit, linear in the number of neighbours; the work is fully parallel (read old state, write own velocity).
- Determinism: float LP and float vector maths are a cross-machine risk. Assume you need fixed-point maths, or a proof that Burst float results match on every target, before lockstep. Unity's Burst 1.8 manual documents FloatMode.Deterministic, intended to make floating-point results consistent across supported platforms; the same manual says it does not guarantee bitwise-identical NaN representations and applies only to Burst-compiled code.
- Failure modes [inference plus common knowledge in the literature]:
  - ORCA is collision-free only if everyone reciprocates. Hold-position units and enemies break that, so model them as non-reciprocating, with the mover taking full responsibility.
  - Dense crowds "freeze" (the feasible velocity set collapses).
  - Symmetric head-on deadlocks happen without a tie-break such as your right-hand rule.
  - ORCA says nothing about liveness at a 3-cell gap.
- Fit: a good velocity planner in front of your gate. It is not a replacement for the gate.

**Continuum crowds (Treuille, Cooper, Popović, SIGGRAPH 2006).**

- What it does: per step, it splats agents into a density field and computes a speed field that depends on density and direction. From that it builds an anisotropic unit-cost field and solves a potential (eikonal) per group. A final step enforces the minimum distance between people.
- Strength: congestion-aware routing that naturally splits streams and handles crossing groups.
- Cost: a full eikonal solve per group per update. At 256² to 512² with tens of active orders, that is milliseconds per group. It is only viable if you update every 0.5–1 s and use integer Dijkstra rather than float fast marching.
- In 2010, according to Dick Leung's blog (28 March 2010), GPG's Chris Taylor said their work was based on the University of Washington's continuum crowd research. In a 2012 Wildfire Games forum thread, 0 A.D. developer Ykkrosh disputed that, saying SupCom 2 used a basic static vector field plus local steering. That fits Emerson's chapter, which describes a static-cost field. Treat "SupCom 2 used continuum crowds" as unverified.
- Recommendation: use a light version, a density-penalty cost re-integration, only for long jams.

**Density- or congestion-aware flow fields (light version).** Add a cost term from the occupancy of the last N ticks and rebuild the order's field every 0.5–1 s while its jam metric is high. This is integer, deterministic and uses your existing job. Risk: route flip-flopping. Damp it with hysteresis, for example only switching when the new cost is more than 10% lower. [Inference.]

**Boids / steering behaviours.** Cheap, fully parallel and deterministic in fixed point. Failure modes are jitter, oscillation and units flipping between sides of an obstacle. One RTS developer blog (jdxdev) fixed side-flipping by locking avoidance to the same side of the agent's forward vector. No guarantees. [Practitioner report.]

**Context steering** (Andrew Fray, GDC 2013 "The Next Vector", with Graham Pentheny's Fieldrunners 2 example of fast coordinated movement of hundreds of agents). Score 8–16 candidate directions with interest and danger maps, using integer scoring. It is deterministic, parallel and cheap. It is a good alternative to ORCA if fixed-point ORCA proves painful. [Published talk.]

**Position-based crowds (Weiss, Litteneker, Jiang, Terzopoulos, MIG 2017).**

- What it does: positions are projected onto constraints, including frictional contact constraints borrowed from granular materials and long-range avoidance constraints. The authors report interactive rates for hundreds of thousands of agents.
- For you: Jacobi iterations are parallel and deterministic in fixed point. Gauss-Seidel is sequential.
- Failure mode: constraints are met only approximately after a few iterations. The method lowers overlap but doesn't guarantee zero, so keep your gate as the final authority.

**Social force (Helbing and Molnár, 1995).** Agents feel a driving force toward their desired velocity plus repulsive forces from others and from walls. This is a well-known published model; I did not retrieve the primary paper in this research. It is known for emergent lane formation in counter-flows and for oscillation at bottlenecks.

- Cost: cheap and parallel.
- Failure modes: stiffness needs small time steps, units overlap under pressure, and tuning is hard. At 30 Hz with hard gating, you would see oscillation against the gate.

**Lane formation.** Your right-hand rule for head-on meetings is the deterministic shortcut to what social force produces naturally. Apply it to group-versus-group crossings too: bias each unit's preferred velocity sideways when the neighbour field shows counter-flow. [Inference.]

**Corner smoothing and any-angle methods.**

- Theta* (Nash, Daniel, Koenig, Felner, AAAI 2007 and JAIR 2010): Basic Theta* finds shorter paths than both A* with post-smoothing and Field D*, with runtime comparable to A* on grids.
- For a shared field, the equivalent is to store a line-of-sight parent per cell (Theta*-style parent propagation inside your Dijkstra), or to do string-pulling during lookup.
- Determinism: easy with integer line checks.
- Failure mode for crowds: every taut path touches the same corner vertex, so the turn and the convergence concentrate there. [Research plus inference.]

**Multi-agent path finding for liveness: PIBT** (Okumura, Machida, Défago, Tamura, IJCAI 2019 and AIJ 2022). Each step, each agent has a unique priority. A blocking lower-priority agent temporarily inherits the higher priority, and backtracking prevents stuck agents. The authors prove that all agents eventually reach their goals on graphs where every pair of adjacent nodes lies on a simple cycle (for example, biconnected graphs). It is the strongest published liveness tool that fits a grid and a per-tick budget. Caveat: a 1-cell dead-end corridor breaks the biconnected assumption. [Published research.]

---

## 6. Question 3: Your jam, and techniques that keep the guarantee and add liveness

### 6.1 What is actually happening (inference, checked against your numbers)

**Step versus gap.**

- Small unit: 4 cells/s ÷ 30 Hz = 0.133 cells per tick.
- Gap in your packed block: 0.625 − 0.5 = 0.125 cells.
- So a follower directly behind its leader cannot move unless the leader has already moved this tick.
- With index-ordered acceptance, about half of all follower–leader pairs are evaluated in the wrong order. Those followers are refused every tick. Packed blocks then advance in stop-go waves at roughly half speed even on straight runs.
- Large units (0.083 cells per tick) suffer less.

**The 37° side lock.**

- Two side neighbours at spacing s both heading in a new direction θ off the old heading become a staggered pair. Measured across the new heading, their lateral clearance is s·cos θ.
- At s = 0.625 and width 0.5, s·cos θ < 0.5 when θ > arccos(0.8) = 36.9°. That matches your 37°.
- Above that angle, any speed difference (one refused, one not) makes the rear unit run into the front one. Neither can slide past, so the gate holds both.
- At 45° you need s ≥ 0.707. At 90° no spacing works for sliding past, and the pair must become a queue: one unit goes first, the other waits.

**Corner convergence.** A shortest-distance field sends every stream to the same corner vertex. Rounding a wall end means about 180° of total turning, concentrated in 1–2 cells. A merge at the wall face is a 2-into-1 lane drop. Without an ordering rule (who goes first), the gate turns it into a mutual block.

**Ring case.** All 100 units seek one corrected cell, so the arrival point is a sink with no capacity. The 86 stalls split into the spawn-block step-versus-gap problem and corner convergence. The stall rule then "settles" them, which hides the cause.

### 6.2 Techniques that keep "overlap never grows" and add progress

1. **Front-to-back sequential acceptance.**
   - Key: 32-bit integration distance of the current cell, minus a sub-cell progress term from the fixed-point position projected on the flow direction, then unit index.
   - Sort with a deterministic radix sort (Burst, one job), then run the gate in that order.
   - The leader always moves first, so followers see the freed space in the same tick.
   - This removes the step-versus-gap refusals.
2. **Gauss-Seidel passes.** Run 2–3 acceptance passes, with refused units retrying in later passes. Usually only a small fraction retry, so the extra cost is small. [Inference.]
3. **Partial steps and slide.** Try the full step, then ¾, ½ and ¼ of it, then the tangential slide. Every candidate goes through the same "overlap not greater than at tick start, no blocked cell" test, so the guarantee holds. Recoil's slide-along-edges and SC2's strafing are the shipped relatives.
4. **Travel spacing.** On order start, packed units first take a dilation step: each moves away from the group centroid until spacing reaches at least 0.72 cells, and the leading edge starts moving at once. This meets the 0.1 s start requirement and removes the 37° lock for turns up to about 46°. Arrival slots can close back to 0.625–0.7.
5. **Turn queuing (an inner-first rule).** When the field direction at a unit's next cell differs from its current heading by more than the lock angle for its spacing, the unit yields to any neighbour on the inside of the turn that is further along. This is "zipper" merging made deterministic: priority goes to the larger progress key, and the right-hand side wins ties. [Inference.]
6. **Wall-face merge rule.** At a merge (two flow directions joining into one), alternate who goes first by parity of tick or unit index, like a zipper merge in traffic. This avoids both sides refusing each other indefinitely.
7. **Wall-clearance cost** (SupCom 2's blur pass): it moves the convergence point away from the vertex and spreads the corner over 2–3 cells of arc.
8. **Narrow gaps (3 cells).** Give the gap a capacity of about 4 small units abreast at 0.625 spacing (3 ÷ 0.625 ≈ 4.8, minus wall clearance). Units more than about 6 cells from the gap mouth get speed scaled by downstream density. This is 0 A.D.'s "pressure slows you down" idea used as a governor, not as overlap. The queue forms before the gap instead of at its mouth.
9. **Two groups crossing.**
   - Apply the right-hand bias to preferred velocities when a counter-flow neighbour is ahead.
   - Ignore same-direction movers in avoidance (SC2's rule) so each group moves as a block.
   - Let ORCA or context steering choose the velocity before the gate.
   - If both groups stall, the escalator gives the group with lower order priority a 1-cell sideways step.
10. **Deadlock detection and resolution.**
    - Track per-unit progress (decrease in integration distance) over a sliding 1 s window.
    - A unit is stuck if it has "somewhere to go" (distance > 0 and not at its slot) but progress is 0 for 1 s.
    - Build the wait-for graph from refusals (who blocked whom) and detect cycles with a deterministic walk.
    - Escalate:
      1. at 1 s: slide or a sideways step;
      2. at 2 s: priority inheritance (the blocker yields one cell, PIBT-style);
      3. at 3 s: swap slots or targets between same-class friendly units;
      4. at 4 s: re-route with a congestion cost.
    - Log any unit still stuck at 5 s.

### 6.3 Which would most likely fix your two cases

- **110 cubes around a wall.** Techniques 1, 2 and 4 address the spawn and turn locks directly, and the arithmetic says they are the dominant cause. Techniques 5–7 address the merge at the wall face and the corner. My expectation (inference) is that ordering plus spacing alone moves the count past the wall from 26 to most of the group, and corner rules finish the job.
- **100 cubes at the ring.** Techniques 1, 2 and 4 again for the spawn block, technique 7 for the two corners, and a reachable slot flood (section 7) for the sink. Without slots, the arrival point can never hold 100 units.

### 6.4 Answer to the architect's question: any-angle routes

They free part of the block, not all of it.

- A 23° turn keeps lateral clearance at 0.575 (> 0.5), so it passes. A 42° turn gives 0.464 (< 0.5), so it still locks at 0.625 spacing.
- Any-angle paths make the first leg straighter. But the taut path wraps the corner vertex, so the remaining turn happens at one point, and the corner merge gets worse.
- Spacing of at least 0.72 plus front-to-back acceptance makes the gain from any-angle largely irrelevant for liveness.
- Local resolution (ordering, slide, spacing, priority) is the real fix. Any-angle is worth adding for route quality and the 1.25× rule. [Inference, arithmetic-backed.]

---

## 7. Question 4: Groups and arrival

**Slots.** From the corrected goal, flood over reachable cells for the size class, with a wall-clearance check. Generate candidate slot positions on a lattice: rows facing the travel direction, at 0.625–0.7 spacing for small units and 1.1–1.2 for large ones. Accept slots in flood order until you have N.

- **Footprint check.** A square lattice at 0.7 spacing gives 0.49 cells² per unit, against your "twice the combined footprint" limit of 2 × 0.25 = 0.5 cells² per small unit, assuming a square half-cell footprint. 0.7 just fits. Hex packing at 0.625 gives more margin.
- **Assignment.**
  - Optimal assignment (Hungarian algorithm, O(n³)) is fine for 100 units in a background job, but not for 2,000 at once.
  - Greedy assignment in front-to-back order (the leading unit takes the nearest free slot on the far side) is O(n log n) and good enough.
  - Fix crossing assignments afterwards with a pairwise swap pass: swap if the sum of squared distances drops. A few deterministic passes are enough.
  - Swapping targets, not bodies, is also your deadlock breaker. [Inference; target-swapping is studied in multi-agent research.]
- **Ending the order and avoiding "dancing".**
  - Once a unit reaches its slot, it becomes "arrived-idle". Its priority drops (SC2-style: it can be pushed) and it can only be displaced by a sideways step plus a walk-home rule.
  - Stop when inside the slot radius (0.1 cells), or when the slot is occupied and the unit is within 1.5 cells. In that case, take the nearest free slot instead of pushing.
  - A third-party SC2-inspired implementation (Granberg's SC2Avoidance example) stops agents when unit density around their destination is high and lowers their avoidance priority once arrived. That is the same idea.
  - No velocity planning for arrived units unless they are being pushed. That meets "no unit still moving 2 s after arriving".
- **Shape through chokepoints and regrouping.** Don't try to keep the block shape inside a 3-cell gap. Let the stream run in rank order, and give each unit its slot offset relative to the moving group anchor, re-applied once the anchor has passed the gap. AoE II's documented behaviour is that formations split at obstacles. AoE IV re-forms with a speed boost, which players complain about as unrealistic but which delivers cohesion.
- **Mixed sizes and speeds.**
  - Use the slowest member's pace, as planned.
  - Large units take the rear or centre slots of the arrival block, so small units don't have to path around them.
  - Large units use their own field (SupCom 2's wall cushioning by size class, which you already do).
  - The 3-second arrival-spread requirement then reduces to slot distance differences: under 7.5 cells at 2.5 cells/s, so keep the block depth under about 6 cells for 60 units.
- **Attack-move depth within 2×.** Use the same pace plus slot offsets relative to the anchor. When a unit's depth from the anchor exceeds 2× the initial depth, slow the leaders by 25%.

---

## 8. Question 5: Feel, as players experience it

- **Praised:**
  - SupCom 2 flow fields (described on the PA forums as beautifully orchestrated unit movement).
  - SC2's responsiveness and its clean group movement.
  - 0 A.D. Alpha 25 pushing, for smoothness (players liked units marching as a dense crowd).
- **Hated:**
  - Units stuck at wall gates (AoE IV).
  - Vehicles pivoting or turning round when ordered backwards, and friendly vehicles blocking each other (CoH 2).
  - Overlapping "worms" of units and chokepoints that no longer matter (0 A.D. A25).
  - Edge-hugging (original Red Alert, which modders fixed with A*).
  - Large groups breaking down on complex terrain (Planetary Annihilation).
  - Long waits at chokepoints (AoE DE: the 45 s wait and the admitted chokepoint problems).
- **Technical causes** (inference from the sources):
  - "Stuck" comes from wait-and-retry without priority (AoE, CoH) or from mutual blocking with no ordering (OpenRA's "grid locking").
  - "Clumping" and overlap come from soft pushing (0 A.D.).
  - "Dancing" comes from arrival rules that keep pushing toward one shared point.
  - "Pivoting" comes from re-planning direction without a preference for facing.
- **Implication for you.** Your hard gate already avoids overlap and the loss of chokepoint meaning. Your risk is the "stuck" end of the spectrum. Ordering, slide, priority and an escalator are the shipped-game answers, and they make that risk manageable.

---

## 9. Question 6: Three new ideas (at most)

### Idea A: "Discrete liveness, continuous look" (sub-cell PIBT backbone)

- **How it works.** Overlay a sub-cell grid (0.5 cells, so 1,024² at 512²). Each tick:
  1. Every moving small unit proposes a next sub-cell from the flow field.
  2. Conflicts are resolved with PIBT: priority = waiting time + progress key; a blocker inherits priority and moves aside or forward; backtracking prevents stalls.
  3. Each unit gets an exclusive reservation of current and next sub-cells.
  4. The continuous position moves toward the reserved sub-cell centre with your normal speed and smoothing.
  5. The overlap gate stays as a safety check.

  Large units reserve 2×2 sub-cells.
- **Why it might work.** PIBT has a proof of reachability on biconnected graphs, and its per-step cost is local. Exclusive reservation gives no-overlap by construction. OpenRA developers have already proposed cell reservation to stop grid locking.
- **Failure modes.**
  - A grid-like look, which needs smoothing.
  - 1-wide dead ends break the proof.
  - Multi-cell PIBT for large units is less studied.
  - The look in open-field crossings may be worse than ORCA's.
- **Estimated cost.** 0.2–0.5 µs per unit (mostly sequential), so 0.4–1 ms at 2,000 units. Memory is about 4 MB for int occupancy at 512², or 1 MB with ushort and a 256² map.
- **Evidence level:** published research (PIBT) plus speculation (the hybrid with continuous motion in an RTS).
- **Novelty:** probably known in warehouse robotics; unusual in RTS.
- **Cheap experiment:** implement it for small units only, then run the 3-cell-gap test and the crossing test. Pass: none standing still more than 5 s while it has somewhere to go, zero overlaps, under 1 ms p99.

### Idea B: Rank-ordered "conveyor" acceptance with target swapping

- **How it works.**
  - The acceptance stage processes units in progress-key order (section 6.2).
  - For same-class friendly units that block each other (A wants B's cell and B wants to go past A), swap their arrival targets, plus their identities within the order, instead of moving bodies.
  - Swaps are chosen deterministically (lowest index pair first) and limited to one per unit per second.
- **Why it might work.** In a homogeneous group the player cannot tell which unit ends where. Swapping removes most crossing and arrival deadlocks at zero motion cost.
- **Failure modes.**
  - Swapping across units with different health or veterancy can surprise players. Restrict it to idle-identical units, or accept that it doesn't matter.
  - Swap oscillation; avoid it with a cooldown.
- **Cost:** about 0.05 µs per unit (pair checks only on refusals).
- **Evidence:** inference. Target swapping is studied in multi-agent path finding research. The rank ordering is common in cellular-automaton pedestrian models. It is probably already known in parts.
- **Experiment:** 100 small units into a 10 × 10 slot block from a shuffled spawn. Pass: all settled within 2 s of the last arrival, no oscillation, zero overlaps.

### Idea C: "Corner wheel" lanes using a wall-distance field

- **How it works.** Precompute a wall-distance transform per size class (integer chamfer, once per map, updated when buildings are placed). Near corners (where the field direction changes by more than 30° within 2 cells):
  - each unit keeps its lateral offset from the stream's inner edge, measured in the wall-distance field;
  - it follows that iso-contour around the corner, like a marching band wheeling;
  - speed scales with its contour's arc length relative to the inner lane, so the block rotates nearly rigidly instead of shearing.
- **Why it might work.** Shear is what causes the 37° lock. A rigid wheel keeps relative positions.
- **Failure modes.**
  - Outer lanes need more speed than units have. Fix by slowing inner lanes instead, at a throughput cost.
  - Concave corners and very irregular walls produce messy contours.
  - Deciding which units are in "this stream".
- **Cost:** about 0.1–0.2 µs per unit near corners, plus about 0.25–1 MB of distance field.
- **Evidence:** inference. It is related to SupCom 2's wall cost blur and to standard edge-following.
- **Experiment:** the 110-cube wall case with and without the wheel, on top of Idea B. Pass: at least 105 of 110 past the wall end within (path length ÷ 4 cells/s) + 5 s, with zero overlaps.

---

## 10. Ranked sandbox experiments (with pass/fail)

1. **Front-to-back ordering plus 3 passes plus partial step and slide.**
   - Pass: 110-cube wall case has at least 100 past the wall; ring case has at least 90 settled outside the ring.
   - Pass: chain p99 at most 1.0 ms at 2,000 units; zero overlaps; determinism hash equal over 10,000 ticks on two machines.
2. **Travel spacing at 0.72.**
   - Pass: no refusals caused by the side lock (log the reason codes) during the block's first turn of 45° or less.
   - Pass: first move within 3 ticks for every selected unit.
3. **Wall-clearance cost band (+1 to +3 over 2 cells).**
   - Pass: corner jam count down by at least 50%.
   - Pass: all routes at most 1.25× shortest on 100 random queries; no 1-cell gap closed for small units.
4. **Reachable slot flood and greedy assignment with a swap pass.**
   - Pass: ring case 100 of 100 settled.
   - Pass: 100 small units within twice their footprint; no unit moving 2 s after arrival; zero overlaps.
5. **Priority and share rules plus the escalator.**
   - Pass: idle friendly units displaced stay within 2 cells of home and return within 5 s.
   - Pass: hold-position units' displacement = 0; stop halts within 15 ticks.
6. **3-cell gap with 200 units, and two groups of 100 crossing.**
   - Pass: none standing still more than 5 s while it has somewhere to go (tracked per unit); zero overlaps.
7. **Line-of-sight smoothing (Theta*-style parents in the field build).**
   - Pass: route length within 1.05× of true shortest on open maps; build p95 still at most 8 ms.
   - Also report how jam counts change (expect a small gain).
8. **Mixed group, 50 small and 10 large, sent 50 cells.**
   - Pass: arrival spread at most 3 s; attack-move depth at most 2× initial.
9. **Idea A (sub-cell PIBT) prototype.** Pass as in experiment 6 at under 1 ms p99. Kill it if the look is rejected in a blind A/B.
10. **ORCA or context steering in fixed point as a velocity planner.**
    - Pass: gate refusals down by at least 50% in crossings; cost under 2 µs per unit; determinism hash equal.
    - Kill it if refusals don't drop or determinism can't be achieved.
11. **Scale test: 2,000 units crossing the map.** Pass: 60 fps on the RTX 3050 reference with zero overlaps and zero allocations per tick (profiler). Repeat at 5,000 and 10,000 for headroom.

---

## 11. Confidence and caveats

- **Confidence labels used:**
  - "Shipped and documented": a primary developer source (Emerson's Game AI Pro chapter, Geldreich's blog, the 0 A.D. developer posts, the Recoil changelog, BAR announcements, Relic patch notes).
  - "Documented second-hand": notes or summaries of talks (SC2 GDC 2011) or fan wikis (AoE IV, openage notes on AoE II).
  - "Published research": peer-reviewed papers (ORCA, Continuum Crowds, Theta*, PBD crowds, PIBT, social force).
  - "Inference": my analysis, including all cost estimates.
  - "Speculation": guesses about undocumented games (Total War, They Are Billions, RA2) and novel ideas.
- **Gaps:**
  - No primary technical sources were found for Total War, They Are Billions, Red Alert 2 or Company of Heroes internals.
  - The Planetary Annihilation flow-field talk could not be verified.
  - SC2 details come from third-party notes of a paywalled talk.
- **Licensing:** Recoil, 0 A.D., OpenRA and the C&C source are GPL, and Aron Granberg's A* Pathfinding Project is commercial. Use their ideas only.
- **Determinism:** before letting any float-based planner (ORCA, PBD, continuum) affect lockstep state, check that Burst's FloatMode.Deterministic holds on your target platforms. The Burst 1.8 manual says the mode aims for consistent results across supported platforms, but does not guarantee bitwise-identical NaNs and covers only Burst-compiled code. Fixed-point is the safe default.
- **Cost numbers are estimates.** Measure them. The sequential acceptance stage is the likely scaling ceiling around 10,000 units. Mitigate by partitioning into independent spatial islands (connected clusters of interacting units), which can be processed in parallel in deterministic island order.

---

## Sources

### Shipped games: primary and developer sources
- Elijah Emerson, "Crowd Pathfinding and Steering Using Flow Field Tiles", Game AI Pro ch. 23 (Supreme Commander 2) — https://www.gameaipro.com/GameAIPro/GameAIPro_Chapter23_Crowd_Pathfinding_and_Steering_Using_Flow_Field_Tiles.pdf
- James Anhalt, "AI Navigation: It's Not a Solved Problem – Yet", GDC 2011 (StarCraft II; paywalled) — https://gdcvault.com/play/1014514/AI-Navigation-It-s-Not
- How to RTS, pathing literature review (SC2 talk notes) — https://howtorts.github.io/2014/01/06/pathing-literature-review.html
- tchayen, open problems of A* / any-angle pathfinding (SC2 notes) — https://tchayen.com/open-problems-of-a-star-for-any-angle-pathfinding
- Richard Geldreich, "On Age DE's pathing/movement" — http://richg42.blogspot.com/2018/02/on-age-des-pathingmovement.html
- openage, AoE II pathfinding reverse-engineering notes — https://github.com/SFTtech/openage/blob/master/doc/reverse_engineering/game_mechanics/pathfinding.md
- Age of Empires wiki, Unit formation (AoE IV) — https://ageofempires.fandom.com/wiki/Unit_formation
- Relic, Company of Heroes 3 patch notes archive — https://help.relic.com/hc/en-us/articles/39307744455571-Company-of-Heroes-3-Patch-Notes-Archive
- They Are Billions, Steam store page — https://store.steampowered.com/app/644930/They_Are_Billions/
- Recoil engine changelog archive (GPL; ideas only) — https://recoilengine.org/changelogs/archive/
- Beyond All Reason microblog 48 — https://www.beyondallreason.info/microblogs/48
- Beyond All Reason microblog 149 — https://www.beyondallreason.info/microblogs/149
- RecoilEngine discussion #171 — https://github.com/beyond-all-reason/RecoilEngine/discussions/171
- Wildfire Games forum, "For a better unit movement" (0 A.D.) — https://wildfiregames.com/forum/topic/56436-for-a-better-unit-movement/
- Wildfire Games forum, A25 feedback from testing — https://wildfiregames.com/forum/topic/39973-a25-feedbacks-from-testing/page/4/
- Wildfire Games forum, unit production / pushing pressure discussion — https://wildfiregames.com/forum/topic/86939-why-units-are-produced-so-fast/page/4/
- Command & Conquer Remastered Collection (Wikipedia) — https://en.wikipedia.org/wiki/Command_%26_Conquer_Remastered_Collection
- EA, C&C Remastered Steam Workshop / source release news — https://www.ea.com/games/command-and-conquer/command-and-conquer-remastered/news/steam-workshop-support
- Steam Workshop, C&C pathfinding mod — https://steamcommunity.com/sharedfiles/filedetails/?id=2137186133
- OpenRA pull request #16701 — https://github.com/OpenRA/OpenRA/pull/16701
- OpenRA changelog (fork mirror) — https://github.com/xbayrockx/OpenRA/blob/bleed/CHANGELOG
- OpenRA issue #20972 (grid locking) — https://github.com/OpenRA/OpenRA/issues/20972

### Planetary Annihilation (secondary)
- PA forums, "Pathfinding" — https://forums.planetaryannihilation.com/threads/pathfinding.68897/
- PA forums, "Flow field pathfinding" — https://forums.planetaryannihilation.com/threads/flow-field-pathfinding.34578/
- Habrador, "Why you need to learn flow field" — https://blog.habrador.com/2017/07/why-you-need-to-learn-flow-field.html
- Steam, Planetary Annihilation: TITANS discussion — https://steamcommunity.com/app/386070/discussions/0/4369124243587359945/

### Third-party implementations (commercial; ideas only)
- A* Pathfinding Project, SC2Avoidance — https://arongranberg.com/astar/documentation/dev_4_1_10_84de22ad/old/class_s_c2_avoidance.php
- A* Pathfinding Project, JobHorizonAvoidancePhase1 (4.3.8) — https://arongranberg.com/astar/documentation/dev_4_3_8_84e2f938/old/struct_pathfinding_1_1_r_v_o_1_1_sampled_1_1_job_horizon_avoidance_phase1.php
- A* Pathfinding Project, JobHorizonAvoidancePhase1 (4.3.49) — https://arongranberg.com/astar/documentation/dev_4_3_49_e8ec22b8/jobhorizonavoidancephase1.html
- jdxdev, "Boids for RTS" — https://www.jdxdev.com/blog/2021/03/19/boids-for-rts/

### Published research
- van den Berg, Guy, Lin, Manocha, "Reciprocal n-Body Collision Avoidance" — https://www.semanticscholar.org/paper/Reciprocal-n-Body-Collision-Avoidance-Berg-Guy/69458cdda06755f4fb564ac8a8b9733d3fb21947
- ORCA project page (UNC GAMMA) — https://gamma.cs.unc.edu/ORCA/
- van den Berg et al., ICRA workshop paper — https://emotion.inrialpes.fr/fraichard/safety2010/10-vandenberg-etal-icraw.pdf
- Treuille, Cooper, Popović, "Continuum Crowds" (SIGGRAPH 2006) — https://grail.cs.washington.edu/projects/crowd-flows/continuum-crowds.pdf
- Andrew Fray, "The Next Vector" (context steering), GDC 2013 — https://archive.org/details/GDC2013Fray
- Weiss, Litteneker, Jiang, Terzopoulos, position-based crowd simulation (MIG 2017) — https://diglib.eg.org/items/1a538cbb-d42a-4a19-82e5-e85b27b228ff
- Position-based crowds preprint — https://ar5iv.labs.arxiv.org/html/1802.02673
- Nash, Daniel, Koenig, Felner, Theta* — https://dl.acm.org/doi/10.5555/1946417.1946429
- Okumura et al., PIBT (arXiv HTML) — https://arxiv.org/html/1901.11282v5
- Okumura et al., PIBT (arXiv abstract) — https://arxiv.org/abs/1901.11282
- Okumura et al., PIBT (IJCAI 2019) — https://mlanthology.org/ijcai/2019/okumura2019ijcai-priority/

### Player sentiment
- Age of Empires forums, "Movement and formations" — https://forums.ageofempires.com/t/movement-and-formations/193866
- Steam, Company of Heroes 2 discussion (1) — https://steamcommunity.com/app/231430/discussions/0/412448158151359678/
- Steam, Company of Heroes 2 discussion (2) — https://steamcommunity.com/app/231430/discussions/0/648814845560450579/
- Steam, Company of Heroes 2 discussion (3) — https://steamcommunity.com/app/231430/discussions/0/46476691026586591/?l=english
- Steam, Age of Empires IV discussion — https://steamcommunity.com/app/1466860/discussions/0/3426697823607751704
