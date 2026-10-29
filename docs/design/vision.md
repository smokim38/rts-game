# Game Vision & Design Pillars

> This page is the foundation of the Game Design Document (GDD). Every later GDD section must trace back to at least one pillar below. A feature that serves no pillar doesn't belong in the game. Balance numbers belong in later GDD sections; technical decisions belong in ADRs (`docs/adr/`).

## Vision statement

A fast, readable real-time strategy game in a classic isometric style, where huge armies move cleanly and the enemy plays like a thinking opponent. The world, factions and units are all original; *Command & Conquer: Red Alert 2* is a style inspiration only.

## Target player and play modes

- **Player:** someone who enjoys classic base-building strategy games and wants quick, readable matches against an opponent that feels smart, on an ordinary gaming PC.
- **First release:** basic skirmish only, one human player against AI opponents. No other modes.

## Design pillars

### 1. Fast and readable

Matches move quickly, and the player can read the battlefield at a glance in a bright isometric look.

- **This means:** every unit, building and attack is recognizable by shape and color from the default camera; every order gets instant feedback; the pace rewards quick decisions.
- **This doesn't mean:** cluttered screens, realism over clarity, or effects that hide what is happening.

### 2. Smarter AI, from commander to soldier

The enemy plays like a real opponent, and every unit behaves sensibly on its own.

- **This means:**
  - *Enemy commander:* plans a strategy (economy, scouting, when and where to attack) and changes it in response to what the player does.
  - *Units:* pick sensible targets, retreat from fights they are losing, and hold formation. The player micromanages to gain an edge, not to fix bad unit behavior.
- **This doesn't mean:** difficulty that comes mainly from hidden bonuses or information the AI shouldn't have, or units so independent that they ignore the player's orders.

### 3. Movement that just works

Units get where they are sent, together, without traffic jams or strange detours.

- **This means:** groups travel as a group, flow around each other and around obstacles, don't pile onto one spot, and arrive in a sensible shape. A large move order looks orderly.
- **This doesn't mean:** realistic vehicle physics, or a perfectly predictable path for every single unit.

### 4. Armies at scale

Thousands of units can fight at once on a modest PC.

- **This means:** large battles are normal play, not a stress test. Every feature is judged by what it costs per unit. Per-unit costs such as individual visual effects, complex targeting rules or per-unit special abilities must state that cost in their GDD section.
- **This doesn't mean:** units become faceless dots. Each unit stays readable (Pillar 1) and behaves sensibly (Pillar 2); scale comes from efficient design, not from removing what makes the game fun.

## Performance target

- At least **2,000 active units** at **60 fps** at **1080p**, on an **RTX 3050 6 GB** with a **mid-range 6-core CPU**.
- This is a release requirement, not a stretch goal. How we reach it is a technical decision for ADRs.

## Non-goals (first release)

- Campaign or story missions.
- Multiplayer of any kind.
- Anything taken from Red Alert, Westwood or EA games: no faction names, unit names or assets. Original IP only.
- Realistic military simulation.
- Photorealistic graphics.

## Open questions

1. **Selectable AI personalities** (for example, an aggressive or a defensive commander): do we want them? See #18. Not promised.
2. **An AI that learns from every game:** is it wanted and feasible? See #18. Not promised.
3. **Measuring the performance target:** which test scenario, what counts as an "active" unit, and whether 60 fps is an average or a minimum. To be settled before performance testing starts.
4. **Factions:** how many, and their themes. To be answered in the factions section of the GDD.
5. **Online multiplayer** (people playing each other over the internet, including any ranked ladder): a separate decision for later (owner, Issue #145). The first release stays skirmish against AI, with up to 8 sides. *Recommendation:* keep it out of the first release. If online play is likely one day, say so early: it usually needs every PC to run the match identically, which is much cheaper to plan for than to add afterwards.
6. **"Quick matches" and no time limit.** The target player above wants "quick, readable matches", and Pillar 1 says "matches move quickly". The owner has since set no target match length and no time limit: a match runs until one side or one group of allies is left (Issue #145, see [core-loop.md](core-loop.md#match-pace)). *Recommendation:* read "quick" as a fast pace, not a short match, and reword those two lines in a later Issue so they can't be taken as a length target.
