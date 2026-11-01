# ADR 0015: Outcome of the #161 ring check (crowd stalemate)

## Status

Accepted

## Date

2026-10-31

## Context

[ADR 0008](0008-movement-and-pathfinding.md), Unreachable goals (S3.2): a unit whose goal cell is impassable or in another region gets the nearest cell of its own region, found by one ring search per order. "The units go there and stop", and nothing retries. Its Checks expect no group stall in the S2 and S3 tests. #161 (ADR 0008 Issue 2, part 3) builds this. Its criterion 5 is a Player check in the Burst development Player, at a focused 1920 × 1080 borderless window: "100 small cubes sent into the ring stop at the nearest reachable points with zero footprint overlaps and no further requests". A 100-cube EditMode ring test checks the same order.

**What #161's runs found** (code at f1d9b66, its git-ignored run logs). The block of 100 small cubes spawns at slot pitch around (60, 30), west of the ring, and is ordered to (88, 30), inside it. The ring's outer faces are at x 75 and 95 and z 20 and 40.
- Goal correction ran once per order and class: one goal search and one field request, and none after the cubes stopped.
- Every cube had the same corrected goal (checked on every tick in EditMode), 7 cells east of the goal cell on the ring's east face: (95.5, 30.5) in EditMode, (95.5, 29.5) in the Player. They differ by a cell because the order point lies on a cell edge. The other faces are 10 to 14 cells away, so each is the nearest cell of the units' region.
- The EditMode test counted 1 cube arriving exactly, 14 stopping beside an arrived cube (ADR 0008's overflow rule, used until slots exist) and 85 settled by the group stall. The Player check, run twice, gave 1, 13 and 86 both times.
- Every cube ended stopped. Neither test had a footprint overlap on any tick. The Player check found no disc, and so no footprint, in a blocked cell on any tick.
- In the Player, 63 of the group-stalled cubes stand in the spawn block, 15 at the ring's south-west corner and 8 at its north-west corner. The 14 that arrived stand in a line along the east face.
- At tick 4 after the order, 61 cubes end the tick at level 1 of the move check ([ADR 0012](0012-move-check-passes.md)). From tick 5, 29 to 87 a tick are held at level 2, their start pose, with an order and nonzero preferred velocity.
- The group stall fired at tick 383, one tick after the last arrival, when 80 of the 86 had been still for over 5 s. So it cut no progressing cube short.

**Inferred mechanism.** It is [ADR 0014](0014-wall-check-outcome.md)'s, at a ring. It is inferred from the counts above and the accepted rules, not traced unit by unit.
1. The block is in the ring's shadow, so it follows the eight-way field ([ADR 0013](0013-open-ground-field-directions.md)). The field turns its north part 45° left, toward the north-west corner, and its south part 45° right. The block's final headings show both turns.
2. Small cubes at slot pitch are 0.625 cells apart. Side neighbours that both turn by θ touch when 0.625 × cos θ < 0.5, so above 36.9° (arithmetic). At 45° they need 0.707. So the move check refuses the turn, then holds the move, as ADR 0014's points 3 and 4 describe.
3. The cubes that get free stream to the west corners. There the field turns them again along the faces, and they pack and lock: the corner merge, like ADR 0014's face merge.
4. The 14 that get round reach the corrected goal and stop. The goal and the arrival rules stall nobody.

**Class**, in ADR 0014's terms:
- **(a) A defect.** Not this one. The search finds the nearest cell of the units' region once per order and class, every cube shares it, nothing retries, and the move check holds the guarantee.
- **(b) The route rule.** Not the class, though its share is unmeasured. Every route round the ring turns the block toward a west corner. Straight lines to the corners, as ADR 0013's any-angle options B′ and E would give, turn it about 23° to 42° (arithmetic), and slot-pitch squares that all turn less than 36.9° don't touch. Such a rule might free part of the spawn block. The streams would still turn at the corners, where 23 cubes jammed.
- **(c) Crowd flow.** This is the class: packed cubes that turn hold each other, in the block and again at the corners. ADR 0012 foresaw it: its proof shows that no overlap is added, not that units make progress. ADR 0008 gives that work to slots and pace (Issue 4), and ORCA and the right-turn rule (Issue 5).

**The owner's choice** (#161 Notes, scope change, 2026-10-31, answer "A"): handle it like the #160 wall. #161 gates no overlaps, nothing in a blocked cell, every cube stopped and no retries, and reports the stalled cubes. The strict gate moves to the steering step, ADR 0008 Issue 5. Not B, crowd steering in #161, and not C, the ring test with fewer cubes.

**Constraints.** ADR 0004's hot-path rules, and ADR 0008 and ADR 0011 to ADR 0014 as accepted. No package. Out of scope (#180): avoidance, slots, arrival shape, any stalemate rule, and code.

## Decision

**The failed ring check is a crowd stalemate, class (c). Goal correction, the route rule and the move check don't change. #161 merges with a reduced ring gate, and the full gate moves to ADR 0008 Issue 5, beside ADR 0014's wall check.**

### #161 criterion 5 and the EditMode ring test

The setup stays: 100 small cubes spawned at slot pitch west of the ring and sent inside it. Both checks get the gate that #161's Notes record (2026-10-31):
- **Gated:** zero footprint overlaps on every tick, no footprint in a blocked cell, every cube stopped, one goal search and at most one field request per order and class, and no retries.
- **Reported:** arrivals at the corrected goal (exact, and beside an arrived cube), group stalls, and the stalled cubes' positions.

The one-cube ring orders of criterion 4 keep their gates, including arrival at the corrected goal. No #161 code adds a stalemate rule or changes the spawn pitch, the directions or goal correction to pass either check.

### ADR 0008 Issue 5 (ORCA and making way)

Issue 5 runs with the full chain, so it carries the full gate. Beside ADR 0014's three additions, it must add:
1. **The ring check as a Player check,** with #161's criterion 5 setup.
   - *Gated:* #161's gate, plus all 100 cubes reaching the nearest reachable points, as ADR 0008's Checks expect of S3 tests: no group stall, no overflow unit, and every cube idle within a quarter pitch of its slot round the corrected goal.
   - *Reported:* what #161 reports, plus units standing still for more than 5 s with somewhere to go.
2. **The 100-cube EditMode ring test,** with the same gate.
3. **Both tests in its tuning.** They count among the tests that set the `moveCheckPasses` default (ADR 0012), and they run with ADR 0008's levers before an avoidance trigger counts.

If either still fails after those levers, Issue 5 stops and reports to the architect, as ADR 0014 sets for the wall check.

### What this ADR settles

From ADR 0008, for #161's two 100-cube ring tests only:
1. **Arrival and stillness, Checks:** the expectation of no group stall. #161 reports group stalls there; Issue 5 gates them.
2. **Unreachable goals (S3.2):** "The units go there and stop." #161 gates the stop, the single search and request, and no retries. Issue 5 gates that all 100 go there.
3. **Follow-up Issues, Issue 5:** it gains the three items above.

Everything else stays in force:
- **ADR 0008:** goal correction, the field's directions, groups, slots, the group stall, the right-turn rule, its Checks for every other S2 and S3 test, and rules 1 to 9.
- **ADR 0011, ADR 0012 and ADR 0013:** all of them.
- **ADR 0014:** all of it, including #160's wall gate and Issue 5's wall additions.

The earlier ADRs' text isn't edited (AGENTS.md §9). This ADR covers no other trigger. If a later #161 run trips one, #161 stops and reports as before.

### Rules for hot paths and packages

No rule is added, and rules 1 to 17 stand. No package is added.

## Alternatives considered

| Option | #161 | Breaks the stalemate | Risk | Work |
|---|---|---|---|---|
| **A. Reduced gate now, full gate at Issue 5 (chosen)** | Resumes now | Left to Issue 5's crowd layers, unmeasured | The jam stays in builds until Issue 5 | This ADR |
| **B. Crowd steering in #161** | Waits for slots, pace, ORCA and their reviews | Expected, unmeasured | Pulls steps 4 and 5 into step 2 | High |
| **C. Ring test with fewer cubes** | Resumes after a test change | No: it avoids the jam | Hides a case players hit | Low |

- **B. Bring crowd steering into #161.**
  - *How it works:* #161 builds ADR 0008's crowd layers, slots and pace (Issue 4) and ORCA and the right-turn rule (Issue 5), so the ring check can pass in full.
  - *Not chosen:* #161's Notes put groups, slots and arrival shape in step 4 and ORCA in step 5. ADR 0012's Issue A comes before Issues 4 and 5, and ADR 0014 already moved the wall's full gate to Issue 5, so both checks are tuned once, with the full chain. The PR would go far past the ~400-line guide (AGENTS.md §3). Whether the layers break the tie is unmeasured either way.
- **C. Run the ring test with fewer cubes.**
  - *How it works:* a smaller block packs less, so more of it may get round the ring.
  - *Not chosen:* it changes the setup, not the movement, and is unproven: point 2 holds for any two packed neighbours. A group ordered into a ring is the case S3.2 and players need, and the jam would stay, unreported.
- **ADR 0014's other options.** An interim stalemate rule in the move check and a spawn at disc spacing are not chosen, for ADR 0014's reasons. ADR 0013's route rules C, B′ and E stay candidates, as ADR 0014 left them (Risks).

## Consequences

- **Benefits.**
  - #161 merges without throwaway code. The guarantee, the stop and the single search and request stay gated.
  - The jam stays visible in every #161 report, so Issue 5 has a baseline: 14 of 100 cubes at the corrected goal in the Player.
- **Tradeoffs.**
  - Until Issue 5 merges, a packed group sent into a closed ring mostly stops where it jams: 86 of 100 in the Player. The owner and QA will see this in builds.
- **Risks.**
  - The mechanism is inferred. If Issue 5 traces another cause, it reports it, and class (a) or (b) comes back to the architect.
  - The route rule's share is unmeasured (Class, b). If Issue 5's layers don't free the spawn block, its stop applies, and a superseding ADR weighs C, B′ and E with this case.
  - A reduced gate could hide a drop in arrivals. The reported counts let the reviewer compare each run with these.
- **Cost at 2,000 units.** None. This ADR changes no code, memory or GPU work.
- **Reversibility.** Easy. A later ADR can restore the full gate in #161's tests, and no code depends on this one.
- **Dependencies and owner decisions.** No package, no Unity change and no handbook change. The owner chose option A on 2026-10-31, and no further decision is needed.
- **Follow-up Issues.**
  - **#161:** its 100-cube EditMode ring test and criterion 5 gate and report as above, which its Notes already record. Nothing else in its scope changes.
  - **ADR 0008 Issue 5:** the three additions above, written into its acceptance criteria when it is drafted, beside ADR 0014's.
