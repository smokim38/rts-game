# ADR 0014: Outcome of the #160 wall check (crowd stalemate)

## Status

Accepted

## Date

2026-10-31

## Context

[ADR 0013](0013-open-ground-field-directions.md) Triggers, "Behind boxes": if #160's Player check (its acceptance criterion 7) fails, #160 stops and reports. If ADR 0013's behind-box EditMode test (its test 6) ends its order by group stall, #160 reports it in the PR. Either way, a superseding ADR weighs C, B′ and E for in-shadow cells. Criterion 7 reads: in the Burst development Player, at a normal focused 1920 × 1080 borderless window, 100 small and 10 large cubes "ordered past the long wall go round it with zero footprint overlaps and no footprint in a blocked cell".

**What run 2 found** (local commit 1e3ac0b, architect ruling on #160, 2026-10-31). Both items fired.
- When the field takes over, at ticks 3 to 4, 68 units have their turn refused: they end the tick at level 1 of the move check ([ADR 0012](0012-move-check-passes.md)), their new position with their start heading.
- From tick 5, 32 to 91 units a tick are held at level 2, their start pose, with nonzero preferred velocity.
- 66 units never leave the spawn block, and 18 bunch at the wall face. 26 of the 110 arrive: 16 small and 10 large.
- There are zero footprint overlaps and zero footprints in a blocked cell. Large cubes block no corner. The group stall fired only after the last large cube arrived, so it cut no moving queue short.
- Directions follow [ADR 0008](0008-movement-and-pathfinding.md)'s rule, and ADR 0013's in-sight branch plays no part.
- The behind-box test logged 86 group stalls: 86 of its 100 units were settled by the group stall.

**Inferred mechanism.** It is inferred from the counts above and the accepted rules, not traced unit by unit.
1. The block spawns at slot pitch (`slotSpacing` 1.25 × width): small cubes 0.625 cells apart and large ones 1.25, which is how an arrived group stands (ADR 0008, Slots). Two neighbours that have both turned 45° need 0.707 and 1.414 cells, which is disc spacing.
2. The block starts in the wall's shadow. When the field takes over, ADR 0008's eight-way directions turn the whole packed block 45° at once.
3. The move check rejects a candidate that conflicts with any pose a neighbour can hold, its turned pose included, and a move into space a neighbour leaves in the same tick (ADR 0012, Following conflicts). So packed units that all turn hold each other: first the turn is refused (level 1), then the move (level 2). A held unit restarts from rest and tries again the next tick.
4. More passes don't free them. Two neighbours both held at level 2 test each other's same three poses again in every pass.
5. The units that get free form a diagonal stream. At the wall face the field turns it 45° again, so it packs and locks again (the face merge).
6. The behind-box test has the same block, the same pitch and a box in its way, so the same cause is behind its 86 group stalls.

**Class.** The ruling sorts a failed check into three classes:
- **(a) A defect:** code that doesn't follow the accepted ADRs. Not this one. The directions, the move check and the guarantee behave as ADR 0008, ADR 0012 and ADR 0013 set them.
- **(b) The route rule:** the field's directions in shadow, which ADR 0013's trigger would change. Not this one either. Every shortest route round this wall turns the block at least 45°, so any route rule turns the packed block too.
- **(c) Crowd flow:** how packed units share space while they turn. This is the class. ADR 0012 foresaw it: its proof shows that no overlap is added, not that units make progress. ADR 0008 gives that work to its crowd layers: slots and pace (Issue 4), and ORCA and the right-turn rule (Issue 5).

**The owner's choice** (#160 Notes, 2026-10-31, "lets go with Option A"): keep #160 going with a reduced wall gate, move the full gate to ADR 0008 Issue 5's Player check, and record this in a short ADR before #160 merges.

**Constraints.** ADR 0004's hot-path rules, and ADR 0008, ADR 0011, ADR 0012 and ADR 0013 as accepted. No package. Out of scope (#165): avoidance, turn rules, slots, pace, any interim stalemate rule, and code.

## Decision

**The failed wall check is a crowd stalemate, class (c). No route rule, move-check rule or spawn rule changes. #160 merges with a reduced criterion 7 gate, and the full gate moves to ADR 0008 Issue 5's Player check.**

### #160 criterion 7

The setup stays as criterion 7 states it. The gate is the one #160's Notes record (2026-10-31):
- **Gated:** zero footprint overlaps, no footprint in a blocked cell, and at least one small and one large cube ending past the wall.
- **Reported:** arrival counts, units past the wall, stall positions, and the units ending at levels 1 and 2 per tick.

ADR 0013's test 6 keeps its gates and reports, so its group stalls stay reported, not gated, in #160. No #160 code adds a stalemate rule, changes the spawn pitch or changes directions to pass either check.

### ADR 0008 Issue 5 (ORCA and making way)

Issue 5 runs with the full chain, so it carries the full gate. It must add:
1. **The wall check as a Player check,** with the same setup as #160's criterion 7 and the cubes spawned at slot pitch, as #160 spawns them.
   - *Gated:* zero footprint overlaps, no footprint in a blocked cell, all 110 cubes past the wall (counted as #160's check counts them), and no group stall.
   - *Reported:* what #160 reports, plus units standing still for more than 5 s with somewhere to go.
2. **No group stall in the behind-box test,** gated as ADR 0008's Checks expect of S3 tests. This adds to ADR 0013's follow-up, which puts the test in Issue 5's scenes and reports units stalled near shadow edges.
3. **Both tests in its tuning.** They count among the tests that set the `moveCheckPasses` default (ADR 0012), and they run with ADR 0008's levers before an avoidance trigger counts.

If either test still fails after those levers, Issue 5 stops and reports to the architect. A superseding ADR then weighs the move check, as ADR 0012's trigger does when stalled units end their ticks at level 2, avoidance (ADR 0008's trigger), and B and C below.

### What this ADR settles or supersedes

From ADR 0013, only these parts:
1. **Triggers, "Behind boxes".** Both items fired in run 2, and this is the superseding ADR they call for. It weighs C, B′ and E for in-shadow cells and adopts none (Alternatives), because the class is (c), not (b). #160 resumes under the gate above and still reports the behind-box group stalls in its PR.
2. **Follow-up Issues, #160.** Criterion 7's gate is the one above. Test 6 is unchanged.
3. **Follow-up Issues, ADR 0008 Issue 5.** It gains the three items above.

This ADR doesn't settle ADR 0013's other triggers: a sight pass p95 over 1 ms at 256 × 256, and the 19 crowd cases failing. If #160's run 3 or a later run trips one of them, or any other trigger, #160 stops and reports as before.

Everything else stays in force:
- **ADR 0013:** the in-sight bit, the encoding, Sampling, "Behind obstacles (S3.1)" with its in-shadow route rule, the guarantee, rules 16 and 17, and its other follow-ups.
- **ADR 0008:** the field's directions, groups, slots, the group stall, the right-turn rule, and its Checks, which still expect no group stall in the S2 and S3 tests.
- **ADR 0011 and ADR 0012:** all of them.

The earlier ADRs' text isn't edited (AGENTS.md §9).

### Rules for hot paths and packages

No rule is added, and rules 1 to 17 stand. No package is added.

## Alternatives considered

| Option | #160 | Breaks the stalemate | Risk | Work |
|---|---|---|---|---|
| **A. Reduced gate now, full gate at Issue 5 (chosen)** | Resumes now | Left to Issue 5's crowd layers, unmeasured | The stalemate stays in builds until Issue 5 | This ADR |
| **B. Interim stalemate rule in the move check** | Waits for an ADR and its code | Possibly, unmeasured | Touches the check that holds the guarantee | An ADR and code, later thrown away |
| **C. Spawn the block at disc spacing** | Resumes after a spawn change | Unproven, and the face merge may still lock | Hides a case players hit | Low |
| **ADR 0013's route rules C, B′ and E** | Waits for an ADR and its code | No: every shortest route turns the block | ADR 0013's costs | Medium to high |

- **B. An interim stalemate rule in the move check, by a new ADR.**
  - *How it works:* a rule in the passes frees units that hold each other, until Issue 5 replaces it.
  - *Not chosen:* it delays #160 by an ADR, its code and their reviews. It changes the check whose order-free proof gives the guarantee (ADR 0012). It would be tuned without ORCA, and ADR 0012 notes that crowd results measured without ORCA aren't a general guide. Issue 5 then replaces it, so the work is thrown away.
- **C. Spawn the block at disc spacing.**
  - *How it works:* the spawn tool places blocks 0.707 and 1.414 cells apart, so two turned neighbours fit.
  - *Not chosen:* it is unproven. The stream still packs and turns at the wall face, so the face merge may still lock. It also changes the setup, not the movement: an arrived group stands at slot pitch (ADR 0008, Slots), so a player's group ordered round a wall would still lock.
- **ADR 0013's route rules for in-shadow cells (C, B′ and E).** Every shortest route round this wall turns the block at least 45°, so each of them still turns the packed block at once. B′ and E also carry ADR 0013's higher costs. They stay candidates if a later check shows a route-rule cause.
- **More move-check passes in #160.** Point 4 of the mechanism shows that more passes don't free a held pair, and ADR 0012 gives that lever to Issue 5.

## Consequences

- **Benefits.**
  - #160 merges without throwaway code, and the guarantee stays gated: no footprint overlap and no footprint in a blocked cell.
  - The gate still shows that both size classes route round the wall.
  - The stalemate stays visible in every #160 report, so Issue 5 has a baseline: 26 of 110 cubes past the wall in run 2.
- **Tradeoffs.**
  - Until Issue 5 merges, a packed group ordered round a long wall mostly stays where it is. In run 2, 84 of the 110 cubes didn't arrive. The owner and QA will see this in builds.
  - Until then, S3.1's "go round it" is shown for some units only.
- **Risks.**
  - The mechanism is inferred. If Issue 5 traces another cause, it reports it, and class (a) or (b) comes back to the architect.
  - That ORCA, the right-turn rule, slots and pace break the tie is expected, not measured. If they don't, the Issue 5 stop above applies.
  - A reduced gate could hide a drop in arrivals. The reported counts let the reviewer compare each run with run 2.
- **Cost at 2,000 units.** None. This ADR changes no code, memory or GPU work.
- **Reversibility.** Easy. A later ADR can restore the full gate, and no code depends on it.
- **Dependencies and owner decisions.** No package, no Unity change and no handbook change. The owner chose option A on 2026-10-31, and no further decision is needed.
- **Follow-up Issues.**
  - **#160:** criterion 7's gate as above, which its Notes already record. Nothing else in its scope changes.
  - **ADR 0008 Issue 5:** the three additions above, written into its acceptance criteria when it is drafted.
