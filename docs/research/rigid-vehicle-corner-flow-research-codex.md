# Research: dense rigid-vehicle corner and merge flow

**I found no verified method that meets all your constraints and guarantees progress from arbitrary dense configurations.** In particular, I found no evidence for continuous rigid-square collision checking, non-sliding motion, field-only navigation, snapshot decisions, and a **0.5 ms total budget for 2,000 vehicles** together.

There is, however, a consistent and useful answer across the fields: **organize traffic before it becomes geometrically trapped; preserve a feasible order through each conflict; and reserve somewhere to finish the maneuver.** The strongest guarantees depend on those conditions.

For your measured failures, my assessment is that the first gains should come from **correcting impossible corner geometry and removing artificial precedence blocks**, followed by persistent corner admission and staged turning. Speed smoothing comes afterward.

Below, **“Evidence” describes published results; “Assessment” and the proposed falsification tests are my inference.** None of the cited work establishes your byte-identical execution requirement.

1. **Designed lanes and zone admission — warehouse and port AGVs**

   **Evidence:** Li et al. give collision- and deadlock-avoidance proofs for AGVs on designed, one-way guidepaths. Vehicles reserve zones; crossing admission also checks for cycles of occupied/requested resources. Routes may be chosen online. The guarantees require suitable layouts, a safe initial state, finite missions, and nonblocking destination depots. Evidence is **theory plus container-terminal simulation**. Their cycle check is quadratic in vehicle count. Crucially, they serialize admission decisions because individually safe simultaneous grants can jointly introduce deadlock; physical traversal remains concurrent. [Li et al., 2016](https://pure.tue.nl/ws/portalfiles/portal/64555308/62685.pdf)

   **Vehicle action and computation:** Follow a shared lane, request the next resource before entering, and release resources after clearing them. This needs zone occupancy, intended exit, reservations, crossing-conflict relationships, and sometimes network-wide dependency information.

   **Assessment:** A small corner interlock is a plausible fit. The complete published algorithm is not demonstrated within your budget. Canonically ordered requests can make admission deterministic and independent of worker execution order. Restricting checks to individual corners loses the network guarantee unless spillback between corners is addressed.

   **Smallest falsifier:** Block a corner’s exit and check whether another vehicle can enter and stop across the conflict. Then test a four-corner ring whose simultaneous admissions consume its last free spaces.

   Deployed warehouses support the architectural approach, but do not disclose enough to copy a controller: the Kiva authors describe designed grid routing and large operational fleets, without publishing a reproducible corner-arbitration algorithm or your required timing evidence. [Wurman, D’Andrea and Mountz, 2008](https://ojs.aaai.org/aimagazine/index.php/aimagazine/article/view/2082/0)

2. **Route interlocking and movement phases — rail and trams**

   **Evidence:** Railway interlocking prevents incompatible movement authorities, holds an authorized route, and releases it under clearance conditions. Block length, braking distance, junction speed and clearance points matter. These are **established operational mechanisms**, but interlocking alone does not guarantee network-wide progress or fairness. [ORR, *Guidance on Minor Railways*, §§249–270, 313–325](https://www.hra.uk.com/storage/HGR-A0000%20Guidance%20on%20Minor%20Railways.pdf)

   Tram priority illustrates a separate scheduling layer: Dublin’s Luas detects approaching trams and changes road-signal phases, while reducing tram priority at selected junctions and times to accommodate competing traffic. [Transport Infrastructure Ireland](https://www.tii.ie/public-transport/luas/signals-junctions/)

   **Vehicle action and computation:** Wait outside the conflict, receive authority for a movement, and retain it until the entire body clears. Runtime information is occupancy, clearance, compatible movement masks, queue heads and phase state.

   **Assessment:** This is among the cheapest transferable mechanisms: integer counters and bitsets can be deterministic. Use short conflict regions and allow compatible following movements; a mutex covering the entire corner may guarantee orderly passage while imposing poor capacity. Fair scheduling must be added separately.

   **Smallest falsifier:** Two same-direction followers should pass without requiring the whole corner to become empty between them. An opposing request must remain excluded until the relevant swept body clears.

3. **Continuous geometric paths with persistent precedence — the strongest directly relevant progress theory**

   **Evidence:** Grégoire et al. coordinate robots along fixed geometric paths using priorities between conflicting movements. Their controller has continuous-space safety and liveness results under conditions including an acyclic priority graph and a suitable initial braking-safe state. Collision geometry is represented in the joint path-progress space; the formal model has additional geometric assumptions. This is **theory plus simulation**, not unrestricted field-following. [Grégoire, Bonnabel and de La Fortelle](https://arxiv.org/pdf/1306.0785)

   Mannucci et al. provide a broader supervisor for heterogeneous, potentially nonholonomic robots. Their guarantees require trajectory envelopes, admissible goals and paths, conservative stopping models, and procedures preventing blocking, deadlocks and livelocks. Infeasible requests may be rejected. [Mannucci, Pallottino and Pecora, 2021](https://iliad-project.eu/wp-content/uploads/2021/mannucci-et-al-TRO-2021.pdf)

   **Vehicle action and computation:** Progress along its assigned geometry while respecting predecessors at actual conflict intervals. It needs path progress, conflicting vehicles’ progress, stopping information and persistent precedence. Constructing/revising the geometry and precedence is additional work.

   **Assessment:** Potentially useful if you introduce **shared local corner maneuvers**, with precomputed swept-square conflicts. Vehicles can retain their shared field elsewhere. This adds maneuver commitment and shared arbitration state. The published guarantees do not transfer automatically to changing field directions or arbitrary packed initial poses. Deterministic execution is feasible in principle; the budget remains unverified.

   **Smallest falsifier:** Give the nominally highest-priority vehicle a lower-priority vehicle physically blocking its departure. If the scheduler cannot recognize an infeasible precedence choice, a total order has not solved the problem.

4. **Space-time reservations with speed schedules — automated intersections**

   **Evidence:** Dresner and Stone’s intersection manager checks requested vehicle movements against reserved space-time occupancy. Their simulations expose two unusually relevant failures: constant-speed reservations produced progressively slower crossings and deadlock; followers could reserve space that their own queue’s front vehicle needed. Acceleration schedules and restrictions on which queued vehicles could request reservations addressed those failures. **Simulation evidence**, not a universal liveness proof. [Dresner and Stone, 2008, §§3.4.2–3.4.4](https://www.cs.utexas.edu/~aim/papers/JAIR08-dresner.pdf)

   **Vehicle action and computation:** Request an entry time, speed and movement; execute the accepted schedule. The manager needs vehicle dimensions, movement geometry, predicted occupancy and existing reservations. Full trajectory simulation during admission can be expensive.

   **Assessment:** Precomputed maneuver envelopes and integer-tick slots are plausible adaptations. Copying the original reservation search is not a credible budget assumption. Deterministic request ordering is straightforward; reservation ownership should persist through clearance.

   **Smallest falsifier:** Two vehicles in one approach plus a crossing vehicle. Let the follower request first. If its reservation can indefinitely delay its predecessor, the admission policy is wrong.

5. **FIFO, zipper service and bounded batches — merge fairness**

   **Evidence:** Late-merge studies do not show a universal throughput advantage. A Virginia study found no statistically significant throughput improvement in its two-to-one field tests; simulated benefits depended on the lane configuration and heavy-vehicle share. **Field and simulation evidence**, with conditional results. [Beacher, Fontaine and Garber, 2004](https://vtrc.virginia.gov/media/vtrc/vtrc-pdf/vtrc-pdf/05-r6.pdf)

   **Vehicle action and computation:** Preserve physical queue order within each approach. Serve the head of alternating nonempty approaches, or a bounded batch. This requires queue-head identity, a “next approach” state, occupancy and service-completion detection; constant work per fixed-size merge after queue maintenance.

   **Assessment:** Cheap and deterministic. For an isolated merge, FIFO plus alternation prevents starvation **provided each selected vehicle can clear in finite time**. This is a conditional argument, not a solution to geometric deadlock. Batches can reduce switching losses, but need a batch limit or waiting-time bound.

   **Smallest falsifier:** Maintain continuous arrivals on one approach and insert one vehicle on the other. It must receive service within the chosen bound. Repeat with unequal vehicle sizes and service times.

6. **Admission metering and speed smoothing — airports and road traffic**

   **Evidence:** NASA’s ATD-2 field evaluation at Charlotte used gate holds to shift excess waiting off taxiways, with stable release-time commitments and adjustments to avoid starving the runway queue. This is **operational evidence for admission control**, not increased geometric crossing capacity. [NASA, *Strategic Surface Metering at Charlotte Douglas International Airport*, 2020](https://ntrs.nasa.gov/api/citations/20205004260/downloads/DASC2020_Strategic_Surface_Metering_at_CLT_FINAL.pdf?attachment=true)

   Separately, Stern et al. demonstrated that controlling one vehicle’s speed damped stop-and-go waves in a real ring of more than 20 cars. Their experiment deliberately did not involve intersections. [Stern et al., *Dissipation of stop-and-go waves*](https://arxiv.org/html/1705.01693v1)

   **Vehicle action and computation:** Metering holds vehicles upstream until downstream storage is available. Speed control adjusts longitudinal motion using leader gap, relative speed and a target speed. Both can use small state and constant work per vehicle once the leader is known.

   **Assessment:** Good budget candidates, with deterministic arithmetic and frozen release decisions. Metering prevents spillback; smoothing reduces repeated stops. Neither creates room for incompatible turns or resolves a mutual-wait cycle.

   **Smallest falsifiers:** Meter your 110 vehicles to queues of 2, 4 and 8 near each corner; reject settings that leave the corner unnecessarily idle. Separately, use 20 vehicles in one lane and a scripted brief slowdown; check whether speed control reduces stop counts without reducing discharge.

7. **Car-like trajectory search and valet-parking optimization**

   **Evidence:** CL-CBS explicitly plans nonholonomic, shaped vehicles using body-conflict search and spatiotemporal Hybrid A*. It has **simulation and physical-robot evidence**, but published examples take seconds: warehouse cases include approximately 1.87 seconds for 20 vehicles and 11.22 seconds for 40. Its faster sequential variant can fail when an earlier vehicle finishes in a location needed by a later one. [Wen et al., *CL-MAPF*](https://april.zju.edu.cn/wp-content/papercite-data/pdf/wen2022clm.pdf)

   Valet-parking research also uses infrastructure-coordinated MPC and supplied reference paths, with small simulated fleets. I could verify the author-repository abstract and indexed sections of one such paper, but could not retrieve its complete PDF for a full proof review. [Kneissl et al.](https://mediatum.ub.tum.de/doc/1625272/document.pdf)

   **Vehicle action and computation:** Execute an individual trajectory; the planner searches or optimizes future poses, controls and conflicts. Per-tick tracking can be cheap, but generating and repairing those plans is substantial additional computation.

   **Assessment:** Poor fit as your whole-fleet controller. Useful as an offline oracle for tiny jams or for generating maneuver templates. Determinism requires fixed search ordering, arithmetic and iteration limits; stopping when a wall-clock deadline expires can change outputs between runs.

   **Smallest falsifier:** Benchmark a four-to-eight-vehicle corner under your actual budget before considering integration.

   Commercial automated valet parking is real, but Bosch’s public description does not disclose enough scheduling, fairness, density or timing information to verify your requirements. [Bosch/APCOA deployment description](https://www.bosch-presse.de/pressportal/de/en/bosch-and-apcoa-to-provide-automated-valet-parking-technology-in-parking-garages-across-germany-250496.html)

8. **Reserved escape space and explicit recovery — industrial mobile robots**

   **Evidence:** Draganjac et al. reserve “private zones” containing nonholonomic motion primitives. A lower-priority blocker can move to a removal pose inside its reserved space. Progress depends on those zones and sector-capacity conditions. Evidence includes **proofs, ten simulated vehicles, five small robots and two forklifts**. The authors acknowledge conservative space use; dense/corridor behavior and throughput remain limitations. Published coordination work is \(O(KN)\) per vehicle for remaining path length \(K\). [Draganjac et al., 2016](https://doi.org/10.1109/TASE.2016.2603781)

   **Vehicle action and computation:** Negotiate precedence, stop or execute a legal clearing maneuver, then resume. It needs nearby occupied/reserved regions, candidate removal poses and enough state to retain the recovery decision.

   **Assessment:** The valuable idea is **guaranteed escape space**, rather than priority alone. The full algorithm poorly matches your density and architecture. A small, precomputed recovery library could be deterministic and inexpensive, but cannot guarantee recovery when no legal maneuver exists.

   **Smallest falsifier:** Overlay the required escape envelopes on your packed block. If they cannot be allocated, the guarantee does not apply.

   Port-terminal recovery research similarly detects resource dependencies and changes operation order or resource assignments. That addresses operational deadlocks; it does not prove that arbitrary interlocked vehicle bodies can separate. [Lehmann, Grunow and Günther, 2006](https://portal.fis.tum.de/en/publications/deadlock-handling-for-real-time-control-of-agvs-at-automated-cont/)

9. **Reactive nonholonomic collision avoidance with cooperative intent**

   **Evidence:** MCCA combines quadratic programs, communicated motion intentions, temporary priorities and heading-oscillation suppression. Its reported tests use circular bounds, up to 40 simulated robots and 0.25-second control cycles. Collision-avoidance constraints include slack penalties. Despite the title, I could not verify a general deadlock-freedom theorem or relevant runtime benchmark. [Zheng and Li, *Deadlock-Free Collision Avoidance for Nonholonomic Robots*](https://arxiv.org/html/2305.04511)

   More generally, control-barrier-function research explicitly identifies safe deadlock equilibria and develops restricted recovery results, including small physical experiments. Collision avoidance alone does not establish progress. [Grover, Liu and Sycara](https://arxiv.org/abs/1911.09146)

   **Vehicle action and computation:** Solve local optimization problems using poses, velocities, obstacles and communicated intentions. Persistent cooperative intent requires more information than neighbors’ poses alone.

   **Assessment:** Useful ideas, but a poor direct fit for hard square non-overlap and your budget. Synchronous deterministic implementations are possible in principle, but change the execution model and need validation. Solver determinism is additional engineering.

   **Smallest falsifier:** Use an exactly symmetric encounter without noise, followed by your packed corner. Apply your hard swept-square safety check. Reject the method if optimization slack creates unsafe moves or the safety filter restores the original deadlock.

For **who goes first**, the important distinction is between a physical dependency and an optional ordering rule.

Suppose A physically needs B to move, while B has a clear exit but waits because A has higher route priority. The combined dependency is still:

\[
A\text{ waits for B's body},\qquad B\text{ waits for A's priority}.
\]

A total priority order only eliminates cycles among the priority rules. It does not eliminate cycles involving occupied space. This is my explanation of your measured two-vehicle lock.

My proposed merge policy would be:

1. **Honor already committed clearing movements.**
2. **Consider physical queue heads for new admission.**
3. **Require a feasible crossing and receiving space before admission.**
4. **Choose between eligible heads using alternation or persistent request age**, with stable ID only as a final tie-breaker.
5. **Keep that decision until clearance**, rather than renegotiating every tick.

For an isolated corner, a progress argument becomes possible if every admission reserves an executable maneuver with bounded completion time and the scheduler serves waiting approaches fairly. With multiple corners, you also need to prevent circular downstream dependencies. Aging cannot compensate for an unavailable exit or an impossible turn.

A wait-for graph is useful diagnostically: record the actual reason and blocker for each rejected movement. Cycle detection is cheap relative to geometric planning, but a cycle is not necessarily a proof of deadlock when alternative maneuvers exist. Recovery still needs somewhere physically reachable to go.

For **throughput**, I would prioritize these interventions in your case:

| Intervention | Why it comes here | Cheap discriminating test |
|---|---|---|
| Correct swept-footprint geometry | Your mixed-size “lanes” are physically incompatible at some positions. Arbitration cannot fix that. | One small and one large vehicle, with no other traffic, traversing the proposed parallel movements. |
| Remove policy-only vetoes | You already observed clear departures being suppressed. | Isolate the two vehicles at a jam head and classify each rejection as geometry or policy. |
| Persistent admission with exit space | Prevents repeated negotiation and stopping inside the conflict. | Compare one complete granted maneuver against tick-by-tick yielding. |
| Compatible pipelining | A whole-corner mutex can waste most of the available capacity. | Compare exclusive corner occupancy with safe same-direction following. |
| Speed/headway control | Helps once the corner can discharge continuously. | Measure departure headways and stop counts with identical geometry and admission. |
| Bounded batches | Helps only when changing approaches causes meaningful lost time. | Compare batches of 1, 2 and 4 while preserving the same fairness bound. |

More lanes are not automatically better. One port-AGV study found a four-road layout outperformed six-road alternatives because extra roads increased travel and crossing competition. That is layout-specific simulation evidence, but it cautions against counting nominal lanes instead of usable, compatible movements. [Li et al., 2011](https://skoge.folk.ntnu.no/prost/proceedings/ifac11-proceedings/data/html/papers/1232.pdf)

Likewise, queue-pressure policies require finite-storage care. Research explicitly shows ordinary back-pressure control failing when downstream queues have finite capacities. [Grégoire et al., *Capacity-aware back-pressure traffic signal control*](https://arxiv.org/abs/1309.6484)

Your **5.7 vehicles/second** corresponds to a mean departure headway of about **5.26 ticks**. One additional idle tick per departure would reduce that to roughly **4.79/second**. A measured rate of **1/second** means a 30-tick headway: roughly **25 extra ticks per departure** relative to that baseline. That suggests substantial blocking or maneuver losses, not merely one unavoidable tick of discretization.

I cannot verify that 5.7/second is physically achievable without angular-speed limits, acceleration rules, vehicle mix and complete corner geometry. With two corners sustaining that rate, 2,000 vehicles would take approximately 175 seconds; at one per second per corner, approximately 1,000 seconds.

For **turning a packed block**, there is a particularly useful geometric result that follows directly from your shapes. This is my derivation, not a literature claim.

Take identical, initially axis-aligned squares of width \(w\), on a square lattice with center spacing \(d=1.25w\).

- **One square turning while its neighbors remain axis-aligned** has sufficient clearance when
  \[
  d\geq \frac{1+\sqrt2}{2}w\approx1.207w.
  \]
- **Adjacent squares rotating synchronously through \(45^\circ\)** require
  \[
  d\geq\sqrt2w\approx1.414w.
  \]

Thus, at your packing density, **individual turns can fit while simultaneous turns physically cannot**.

The smallest test is a 2×2 block: rotate one diagonal pair, then the other. A checkerboard schedule extends this to an ideal equal-size lattice for 90° or 180° changes. It does not automatically extend to mixed sizes, irregular spacing, obstacles or arbitrary initial headings.

For general arrangements, the corresponding cheap candidate is:

- Compute conservative local rotation envelopes.
- Select a deterministic set of mutually compatible turns.
- Preserve each turn’s ownership until completion.
- If no turn fits, move a boundary vehicle into real free space before attempting interior turns.

This requires nearby envelope checks and persistent turn state. It can fit a spatial-index implementation; its runtime and progress remain unverified. The smallest falsifier is a mixed-size 2×2 block where the selected turns overlap despite each individually clearing stationary neighbors.

Your “front first” observation is therefore credible, but “front” should mean **a vehicle with an executable clearing maneuver**. Distance-to-goal alone does not establish that. If no turn or translation is possible, scheduling cannot unpack the block. Recovery may require additional space or reversing; whether reverse motion is permitted in your model was not specified.

There are also several tempting approaches that the evidence does **not** justify:

- **Graph progress guarantees applied directly to square motion.** PIBT has a genuine finite-progress theorem under graph-cycle conditions, but those conditions do not certify swept rotations, mixed sizes or continuous execution. Its priority-inheritance idea is useful; its proof is not transferable unchanged. [PIBT, theorem 2](https://www.ijcai.org/proceedings/2019/0076.pdf)
- **Planning point-agent routes and expecting a later controller to repair geometry.** CL-CBS comparisons document failures when initial paths omit kinematic feasibility. [CL-MAPF](https://april.zju.edu.cn/wp-content/papercite-data/pdf/wen2022clm.pdf)
- **Replacing squares with enclosing circles at this density.** Two equal squares can stand at \(1.25w\) center spacing, while their enclosing circles require \(\sqrt2w\). That conservative model rejects configurations you need to support.
- **Repeated priority switching, or unrestricted reservation retries.** Persistent intent and request throttling appear precisely because repeated local reconsideration can produce oscillation or wasted computation.
- **Assuming deadlock freedom implies starvation freedom or high throughput.** A safe controller can indefinitely defer one approach; a fair controller can serialize far more space than necessary.

Finally, **start-of-tick reads are compatible with coordinated motion, but checking each move only against stationary start poses is insufficient**. Two approaching vehicles can each move less than the initial gap while their combined motion closes more than that gap. Rotation also needs intermediate swept-footprint checking.

The implementation I would test first would retain your field and add shared corner movements, persistent admission/turn ownership, physical queue order, and downstream-space checks. All proposals would come from the same snapshot; grants would be computed in a canonical logical order; moves would commit together. If even shared local maneuver commitment or persistent arbitration state is disallowed, I found no general progress solution for the remaining model.

Determinism would require integer timestamps, stable tie-breaks, controlled numeric behavior, deterministic conflict reductions, and no wall-clock-dependent solver decisions. Logical serialization of a small admission stage can still be independent of thread execution order.

Your budget is **250 nanoseconds per vehicle on average for the entire pass**. Precomputed geometry, bounded neighbor queries and small resource-state updates are plausible candidates. General trajectory search, repeated optimization and whole-fleet dependency checks are not supported by the published timings. I would begin with the **two-vehicle policy-veto test and the 2×2 staged-turn test**, then benchmark the smallest persistent corner scheduler against your existing 110-vehicle case before expanding it.
