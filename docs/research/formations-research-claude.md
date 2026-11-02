# Formations and Group Movement for a Red Alert 2–Style RTS: Survey, Algorithms and a Recommended Design

**Bottom line:** Make the default move a formation move. Every right-click should already give a role-sorted, pace-matched block. Use right-click-drag to set the front line's position, facing and width, and use a modifier-drag to draw a custom line. Build it as a hybrid: one flow field per size class handles routing, a per-group "formation anchor" moves along that field, and slots are assigned per role band using a sort-based assignment that does not cross paths. The shipped games that made formations optional (Supreme Commander, Total War's unlocked groups, C&C3, AoE IV) all collected the same complaint: players don't use them or don't trust them.

## TL;DR
- **Make formations the default and keep control cheap.** In Supreme Commander, formation move is a separate gesture and is rarely used in competitive play because of the speed loss and setup cost. Total War players complain that multi-unit moves only keep their shape when the group is "hard-locked." AoE II, where Line is the default and the slowest unit sets the pace, is the long-running positive example. Right-click-drag for facing and width, as in C&C3, Total War, WARNO and Beyond All Reason, is the most widely shared and best-liked control.
- **Assign slots by role band, then by geometry.** Put heavy and high-HP units in front, lighter units behind or on the flanks, and artillery and support at the rear (AoE II's Line order, BAR's Alt-move, Total War's block layout). Inside each band, use a deterministic sort-based assignment, or an optimal assignment for small bands, so units do not cross. Greedy nearest-slot assignment is the known cause of crossing and circling.
- **Store battle data in the group from day one** so combat can be added without a rewrite: a slot role, facing, front-line reference, anchor, cohesion state and stance. Re-form triggers and "loose versus locked" cohesion are cheap to store now and expensive to retrofit.

## Executive summary: key findings

1. **No shipped RTS in this survey both scales to thousands of units and uses rigid formations.** At large scale the pattern is a group-level route (flow field or shared path) plus per-unit steering to slots. Supreme Commander 2 moved to flow-field tiles specifically to support "obstacle avoidance, flocking, dynamic formations, crowd behavior" when "moving hundreds to thousands of individual agents across massive maps" (Elijah Emerson, ch. 23 "Crowd Pathfinding and Steering Using Flow Field Tiles", *Game AI Pro*, 2013). [Sourced]
2. **Pace matching is the feature players value most.** AoE II's formations were introduced so that "all units go at the speed of the slowest unit" (HeavenGames).\[1\] C&C3 players use formation preview mainly because "it makes them move at the same speed as the slowest unit."\[2\] [Sourced]
3. **Optional formations go unused.** The Forged Alliance Forever community wiki says formation move is rarely used because of the speed loss and setup difficulty.\[3\] A C&C3 player called formation preview "kinda shit… all its good for is having all units travel at the same speed."\[4\] Total War players argue "keeping the formation should be the default."\[5\] [Sourced]
4. **Right-click-drag is the de facto standard control:** C&C3 (both buttons, rotate and stretch), Total War (drag sets width and facing), WARNO (drag length sets the number of columns), CoH2 (drag sets facing), Supreme Commander (hold, then drag the front-line normal), BAR (drag a line).\[6\]\[7\]\[8\]\[9\]\[10\]\[11\] [Sourced]
5. **Drawing a free line is liked but has a known limit.** BAR's right-drag "draws a line on which all selected units will spread evenly", and its own docs call it "superior".\[10\] A community guide warns that drawing a line hands out end positions rather than a route.\[12\] [Sourced]
6. **Automatic role placement is well established.** AoE II Line puts "melee units at the front… ranged units behind… siege weapons/monks/heroes at the back."\[1\] BAR's formation move puts "higher HP units in front and weaker in back."\[10\] Total War's GroupFormations XML assigns unit classes to blocks by priority, with a mandatory "any" catch-all block.\[13\] [Sourced]
7. **Greedy assignment causes the most-hated failure: units crossing through each other.** Clodéric Mars and Jérémy Chanut (ch. 20 "Hierarchical Architecture for Group Navigation Behaviors", *Game AI Pro 2*, 2015, §20.7.2 "Slots Assignment") say greedy closest-slot assignment "doesn't always work: the entities might have to cross each other's paths and the last entities might have to circle around the group." They recommend sorting slots and units along the same axis and matching them in order. [Sourced]
8. **Optimal (min-total-distance) assignment removes crossings.** With Euclidean costs, a crossing pair can always be swapped to shorten the total. Lee, Kim and Ahn ("Defect-free atomic array formation using the Hungarian matching algorithm", *Physical Review A* 95, 053424, 2017) found Hungarian matching "shows no path crossing" where a greedy shortest-move gave crossings, "because the matching with path crossing gives a bigger travel distance than the corresponding collision-free matching that swaps the targets." Hungarian is O(n³), so use it per band or for small groups only.\[14\]\[15\] [Sourced + inference]
9. **The column-through-chokepoint behaviour exists in shipped games.** In AoE II, Line "can change from the regular horizontal line formation to a long vertical one" over long distances (AoE fandom wiki).\[16\] Mars and Chanut advise that group path cost should treat "falling back to a narrower spatial configuration" as comparable to a longer path. Sharma et al. ("Formation-Aware Planning and Navigation with Corridor Shortest Path Maps", *Computer Graphics Forum* 43(1), 2024) likewise weigh keeping a preferred formation against "switch[ing] to alternative formations requiring less clearance in order to utilize narrower corridors that can lead to a shorter overall path." [Sourced]
10. **Slot targets should be extrapolated ahead of the anchor, not placed exactly on it.** Mars and Chanut recommend extrapolating the slot with the group velocity over more than one frame to avoid jolts. The extrapolation time also tunes cohesion: small values make tight formations, large values loose ones.\[17\] [Sourced]
11. **Local avoidance must know who is in the group.** RVO/ORCA-style avoidance "tr[ies] to enforce a safe distance… that might forbid close formations." Group members should treat peers differently, for example by reacting only to imminent collisions (Mars and Chanut).\[17\] [Sourced]
12. **Stances are common across the genre and map directly onto formation behaviour.** C&C3 has Aggressive / Guard (default) / Hold Ground / Hold Fire. AoE II has Aggressive / Defensive / Stand Ground / No Attack. RA2 has Guard, Attack-Move and Guard-Destination. Total War has Fire at Will / Guard / Skirmish / Melee. C&C3's manual explicitly suggests "formations in combination with the Hold Ground stance to form a defensive line."\[18\]\[19\]\[20\]\[21\]\[22\] [Sourced]
13. **"Blobbing" is the failure that formations exist to prevent, and designers still struggle with it.** Relic's CoH3 2.4.0 patch notes acknowledge "the current systemic issues causing a reliance on massed-infantry (blobbing)… Players are currently incentivized to mass units together", and the follow-up 2.5.0 update tightens squad formations by 0.5–1 m. An earlier CoH3 patch fixed vehicles "converging to the same point" during formation moves.\[23\] [Sourced]
14. **Determinism needs ordering discipline.** Emerson notes that a quality improvement in SupCom 2 made "flow tiles order dependent."\[24\] For lockstep, every assignment and tie-break must use a stable key such as the unit ID, never thread completion order. [Sourced + common practice]

---

## Part A: Formations for movement

### A1. Survey of shipped games

**Command & Conquer: Red Alert 2 / Yuri's Revenge (2000/2001).** The hotkey references and the RA2 manual list Guard (G), Scatter (X), Attack-Move (Ctrl+Shift+click), Guard-Destination (Ctrl+Alt+click), Escort (Ctrl+Alt on a unit), Force-Move (Alt) and Waypoints (Z).\[21\]\[25\]\[26\]\[27\] None of the sources found lists a formation command. Selected units travel at their own speeds and bunch at the destination. Competitive players lean on Attack-Move instead, for example to make IFVs "move and shoot" against Rocketeers (XWIS forum).\[28\] *Takeaway: in the reference game, group cohesion is entirely the player's job, so a spinoff can add it without breaking genre expectations.* [Sourced; the absence of a formation command is inferred from the hotkey lists]

**Command & Conquer 3 / Kane's Wrath (2007–08).** "Formation preview": hold both mouse buttons at the destination. Moving the mouse horizontally rotates the formation; moving it vertically trades depth against width. Releasing sends the units, which "line up [in] the previewed formation" and travel at the slowest unit's speed.\[9\]\[29\] There is also a "formation move" toggle via planning mode (Ctrl+Z), according to the manual text reproduced on Steam.\[9\] Stances: Aggressive (Alt+A), Guard (default, Alt+S), Hold Ground (Alt+D), Hold Fire (Alt+F).\[30\] *Praise:* it lines up heavy armour for maximum DPS. *Criticism:* the formation is forgotten after the next plain right-click ("you have to keep setting the formation each time you move them"), and the two-button chord is awkward.\[4\] [Sourced]

**Company of Heroes 1 & 2.** There are no formation presets. Right-click-drag issues a facing order ("drag the mouse in the direction you want the unit to face").\[7\] Squads have internal model formations. Multi-unit moves converge, and a CoH2 player asked how to keep tanks in a line because "the tanks try to converge on the point I ordered them to move to."\[31\] *CoH3* added a formation-movement fix in update 1.2.0: "Vehicles now adhere to their formation as expected when performing formation movements instead of converging to the same point." It also added a preference for vehicles to keep their original facing.\[23\] In 2.5.0 it tightened infantry squad spacing by 0.5–1 m to make blobs more vulnerable to area-of-effect weapons.\[32\] [Sourced]

**Total War (Shogun 2 → Warhammer III).** These games have the richest controls. In Shogun 2, right-drag sets frontage and facing, Ctrl+drag walks instead of running, and Alt+drag forms from the centre rather than left to right.\[8\] Group formations can be locked (Ctrl+G or the padlock icon). A locked group keeps its relative layout, moves at a shared speed, and on an attack order "will attempt to select sensible target units relative to their formation positions" instead of converging on one target (Prima Games).\[33\] Alt+drag moves a selection while keeping its shape, and Ctrl rotates it.\[34\]\[35\] Preset group formations are data-driven XML. Blocks contain unit-class preferences with priorities,\[13\] and each block has `EntityArrangement` set to Line or Column and an `InterEntitySpacing`: 2.0 for infantry blocks and 6.0 for artillery blocks in "Double Line Standard". A catch-all `any` block is required "so that any units not fully accounted for… can be placed" (official Total War wiki).\[13\] *Criticism:* unlocked multi-selections move "at their individual speeds so the formation is broken up",\[5\] and "Alt-movement is… awkward and overly time-consuming."\[5\] [Sourced]

**Supreme Commander / Forged Alliance (2007).** A plain right-click is a non-formation move: each unit goes at its own speed and forms up at the end.\[3\] Holding right-click shows a formation preview. Dragging sets the facing ("drag the normal vector of your desired formation front line"), and left-clicking while holding cycles formation shapes.\[6\]\[36\] Ctrl+right-click does a default formation move, and Ctrl+Alt does an attack-move in formation.\[37\]\[38\] In formation, units move "with the speed of the slowest unit" (FAF wiki), and "taking formation takes priority over reaching the destination; units may move backward to achieve it."\[3\]\[38\] A community tip claims formation move spreads mobile shields through the group and puts tanky units in front, but I found no developer confirmation, and other players wish shields would "stay in the middle".\[39\]\[40\] Treat that claim as unverified. *Criticism:* it is rarely used competitively because of the speed loss. Fire states: Return Fire (default), Ground Fire, Hold Fire.\[3\] [Sourced; the shield and tank claim is community-only]

**Supreme Commander 2 (2010).** Flow-field tiles plus HPA-style routing let hundreds to thousands of agents move "instantly despite path complexity" and support "dynamic formations" (Emerson).\[41\]\[42\] [Sourced]

**Age of Empires II DE.** It has four formations: Line (default), Box, Staggered and Flank.\[43\] Grouping makes "all units go at the speed of the slowest unit."\[1\] In Line, "melee units at the front… ranged units behind… siege weapons/monks/heroes at the back."\[1\] Over long distances Line can turn into "a long vertical one".\[16\] Staggered spaces units out against area-of-effect and pass-through projectiles, Box encloses weak units, and Flank splits the group in two. The game's own tutorial notes that Box "is not used as much in online games, as it spreads out the group and makes the units on the outside easier targets."\[43\] In competitive play Line and Flank are used to dodge Mangonel shots and Staggered against Scorpions.\[44\] Stances: Aggressive, Defensive, Stand Ground, No Attack.\[18\] [Sourced]

**Age of Empires IV.** The fandom wiki lists Line, Wedge, Staggered and Column.\[16\] Forum users list Line, Wedge, Spread and Box (default),\[45\] so the sources conflict and the formation set appears to have changed between versions. Units "rearrange themselves immediately into the new formation once the option is selected, even if they are standing still."\[16\] *Criticism:* players report "unit formations in AoE IV are just for show", and Wedge is called "noob bait that will kill your cav."\[46\] There is also a bug thread saying formations are "extremely unreliable… when toggling the formations in between movement commands in rapid succession."\[47\] The only stance is Stand Ground / Hold Position.\[18\] [Sourced]

**StarCraft II (contrast).** It has no formations. Units flock, steer and clump into a ball at the destination.\[48\] Players note the "magic box": ordering a move to a point outside the selection's bounding box sends units straight and they ball up, while a point inside keeps relative offsets. Some players wanted Brood War-style line-keeping, and others defended clumping as a skill-based tool.\[49\] *Lesson: clumping is powerful for DPS concentration, so formations must not be strictly worse than clumping in a fight.* [Sourced, community]

**Men of War: Assault Squad 2.** It has no formation presets. Selecting a squad leader selects the whole squad, and the others "wait for the Squad leader to move before they move", which competitive players find slow.\[50\]\[51\] Box selection is capped at ten soldiers.\[52\] Players asked for a drag-a-line spread command, and modders found an unused `formation.set` file.\[53\] *Lesson: the lack of spreading pushes players to micromanage groups of 2–3 so that one shell doesn't kill everyone.*\[50\] [Sourced, community]

**Wargame: Red Dragon / WARNO (Eugen).** Right-click-drag shows white ghost previews of the selected units. As the drag gets longer the preview goes from one column to two, three and so on, and dragging along a road lines tanks up in a single column.\[11\] Units keep the order and facing set by the drag.\[54\] WARNO adds unit grouping, which players call "superior" to Red Dragon's stacks.\[55\] Matchsticks for my Eyes' WARNO review (1 September 2024) observed that the tactical AI "opens nearly every battle by charging a massed tank column down the highway." [Sourced]

**Homeworld (1999) → Homeworld 3 (2024).** The original had seven formations (Delta, Broad, X, Claw, Wall, Sphere, Custom) on F5–F11, with Tab cycling forward and Shift+Tab backward, plus a "Military Parade" around the mothership.\[56\] Formations changed combat (Sphere surrounds a target; Wall concentrates capital-ship fire).\[57\] Homeworld 3 has eight, adding Trail.\[58\] Community advice: "Never put your capital ships in formation. They'll move far too slowly."\[59\] [Sourced]

**Halo Wars (2009) / Halo Wars 2 (2017).** These are controller-first with no formation presets. Selection is global, local or by type.\[60\] Reviewers accepted that "a big clump of units is usually all you need", while criticising the lack of control groups and fine-grained splitting.\[61\]\[62\] [Sourced]

**Beyond All Reason (Spring engine).** It separates three commands. **Alt-move** ("Move and assume formation") puts higher-HP units in front and weaker behind, and units move "at their own, highest speed". **Ctrl-move** keeps the formation and moves "as slow as slowest unit". **Right-drag** draws a line, curves included, on which units "spread evenly".\[10\] A + right-drag gives a line attack.\[63\] *Praise:* the line drag is the community's favourite, but it hands out end positions and does not define a route.\[10\]\[12\] [Sourced]

**Others worth noting.** C&C Generals players remember its formation mode as easy (GameReplays C&C3 thread).\[4\] Total War's AI uses the same block formations it gives to players, with Attack, Defend and Deployment purposes\[13\] and minimum or maximum unit-category percentages (Total War wiki).\[13\] [Sourced]

### Comparison table

| Game | Formations available | How chosen | Default behaviour | Pace matching | Praised | Criticised |
|---|---|---|---|---|---|---|
| RA2 / YR | None found in hotkey lists or manual | — | Individual speeds; bunch at destination | No | Simple; attack-move micro | Stringing out; no cohesion |
| C&C3 / KW | Preview block (rotation, depth/width) | Hold LMB+RMB, move mouse | Plain move = no formation | Yes (slowest) in formation | Neat armour lines | Lost after next click; chord awkward |
| CoH1/2 | None (facing only) | RMB-drag = facing | Converge on point | No | Facing control | Tanks converge/blob |
| CoH3 | Group formation move (patched) | Drag | Formation-ish | Partial | 1.2.0 fix | Blobbing still "systemic" (2.4.0 notes) |
| Total War (Sh2–WH3) | Free drag + locked groups + XML presets | RMB-drag width/facing; Ctrl+G lock; Alt-drag | Unlocked = individual speeds | Locked groups only | Precise lines, AI uses same data | Lock needed; Alt awkward |
| SupCom/FA | Several presets, cycled | Hold RMB, LMB cycles; Ctrl+RMB | Plain move = no formation | Yes (slowest) | Auto role spread | Speed loss → rarely used |
| SupCom 2 | Dynamic formations on flow fields | — | — | — | Responsive, scales | Some bridge pathing failures shown in videos |
| AoE II DE | Line (default), Box, Staggered, Flank | Buttons/hotkeys | Line, role-ordered | Yes (slowest) | Dodge siege; cohesion | Box rarely used |
| AoE IV | Line/Wedge/Staggered/Column (sources conflict: Box default) | Buttons | Box-like clump | Partial | Staggered vs AoE | "Just for show"; unreliable toggling |
| StarCraft II | None | — | Flock/clump | No | Fluid, responsive | Clumping, no line-keeping |
| Men of War AS2 | None (squad-leader follow) | — | Converge/blob | Squad waits for leader | Squad cohesion | Slow; 10-unit cap; no spread |
| Wargame/WARNO | Drag line, multi-column by drag length | RMB-drag | Converge without drag | Road speed separate | Road columns, ghosts | — |
| Homeworld 1–3 | 7–8 presets + custom | F-keys / Tab cycle | Parade | Yes | Tactical meaning | Capital ships too slow |
| Halo Wars 1/2 | None | — | Blob | No | Simple on controller | Low control granularity |
| BAR | Alt-assume, Ctrl-keep, line drag | Modifiers + drag | Plain move | Ctrl only | Line drag "superior" | Line = endpoints, not route |

### A2. Controls: what is fast, what is clumsy

Sourced observations:
- **Right-drag for facing and width** is the most common and best-received control (C&C3, Total War, WARNO, CoH facing, SupCom, BAR). Total War adds a centre-anchored variant (Alt+drag "formed from centre") alongside left-to-right.\[8\]
- **Drag length as an input** is intuitive. WARNO adds columns as the drag gets longer, and C&C3 trades depth for width.\[9\]\[11\]
- **Modal chords are clumsy.** C&C3's two-button hold and SupCom's hold-then-left-click-to-cycle are frequently misunderstood in forum threads, and SupCom players lamented when Ctrl was remapped.\[6\]
- **Formations that are forgotten after one order** (C&C3) and **formations that require a lock** (Total War) generate repeat complaints.\[4\]\[5\]
- **Rapidly switching formation in the middle of a move** produces bugs (AoE IV forum thread).\[47\]
- **Saving custom formations:** Homeworld had a Custom formation slot. Total War's locked groups effectively save a layout.\[64\] BAR has no save feature, but the line drag replaces it.

My assessment (suggestion): the fastest, least error-prone scheme is **(1) plain right-click = auto formation at the destination, facing the direction of travel; (2) right-drag = straight front line (drag vector sets width and facing; depth follows from unit count); (3) Alt+right-drag (or a toggle) = freehand line or curve; (4) number-row or F-key hotkeys for formation type, persisted on the group; (5) Ctrl+right-click = "keep current shape" translation (Total War's Alt-drag behaviour)**. Live ghost previews of slots during any drag are essential: Wargame, C&C3 and SupCom all show them.

### A3. Automatic placement by unit class

**Sourced patterns.**
- AoE II Line: melee in front, ranged behind, siege, monks and heroes at the back. Box: weak units in the centre.\[1\]
- BAR Alt-move: ordered by HP, highest in front.\[10\]
- Total War: blocks per class with priorities. In the reference formation, artillery blocks sit beside the infantry line with wider spacing (6.0 against 2.0), cavalry sits on the flanks behind, the general is central-rear, and there is an "any" block.\[13\]
- WARNO beginner guidance: non-combat and anti-air units behind combat units, so that damaged long-range units can retreat quickly.\[11\]
- SupCom (community, unverified): shields spread through the group, tanky units in front.\[40\]

**Recommended rules for this game (suggestion):**

| Role band (front → back) | Classes | Lateral placement | Spacing (centre-to-centre, × footprint diameter) |
|---|---|---|---|
| 0 Front | Heavy armour (Rhino, Apocalypse, Prism-class tanks if tanky) | Centre-weighted across the full width | 1.3–1.5× |
| 1 Front-flank | Light vehicles (IFV, Flak Track as escorts) | Ends of the front line / flanks | 1.3× |
| 2 Middle | Infantry (GIs, Conscripts) | Behind the armour, full width | 1.2× (infantry footprint) |
| 3 Inner | Anti-air and support (Flak, IFV-AA, medics, engineers, repair) | Distributed evenly across the width, not clumped | 1.3× |
| 4 Rear | Artillery (V3, Grand Cannon-class mobile, Kirov if ground-attached) | Rear, centred, wide spacing | 2.0× |

- **Depth versus width:** keep the current block shape at about 2:1 width to depth, with one block per footprint class as the default. AoE II and Total War both default to wide-and-shallow lines. In a mixed group, stack the bands front to back and make each band's width equal to the widest band's width, so the rows line up and smaller units fill extra rows.
- **Spacing:** scale by footprint, as Total War does with 2.0 for infantry and 6.0 for artillery. I suggest an extra 0.2–0.5 footprint of gap so that ORCA can resolve without jitter. "Loose" or "staggered" variants double the gaps, as AoE II's Staggered does.
- **Pace:** the group moves at min(max speed) × 0.95. Followers may go up to 1.2–1.3× group speed while catching up. Dave Pottinger's "Implementing Coordinated Movement" (Ensemble Studios, Game Developer/Gamasutra, 1999) describes groups that require "units farther ahead [to] wait for other units to catch up and possibly allows slower units to get a temporary speed boost in order to catch up", and the virtual-leader literature suggests "a small multiplication". Units ahead of their slot slow down to 0.8×. Pottinger warns that letting slow units speed up "can often create unbalanced game play", so cap catch-up at the unit's own max speed. Only the group anchor obeys the slowest-unit rule.
- **Long trips:** consider a "travel pace" option where units travel at their own speed until they are about N cells from the destination and then converge. This is BAR's Alt-move versus Ctrl-move distinction,\[10\] and it answers SupCom's "speed loss" complaint.

### A4. Formation on the way

**The three architectures.**
1. **Rigid virtual leader** (Pottinger's "blind" following; Millington): slots are fixed offsets from an anchor that follows a path. It looks great on open ground, snags on obstacles, and must slow its turn rate by group width. Mars and Chanut note the group's max rotation rate "needs to take into account the maximum speed of its members and the width of the group."\[17\]
2. **Slot-at-arrival only:** units path individually, which in our case means following the flow field, and slots exist only at the goal. It is robust and cheap, but units string out and arrive staggered, as in SupCom and C&C3 without formation, or RA2.
3. **Hybrid (recommended):** the anchor follows the group's flow field (sampled at the anchor). Each unit steers toward an *extrapolated* slot target (anchor + rotated offset + v_group·τ) blended with its own flow-field direction. The blend weight depends on how far the unit is from its slot and on the local clearance. ORCA has the final say.

**Chokepoints and bridges (suggestion built on sourced principles).** Measure clearance ahead of the anchor using the flow field's cost or a distance-to-obstacle field. When the available width is less than the formation's width, enter **column mode**: the slot layout becomes k files, where k = floor(clearance / lateral spacing), with role bands kept front to back. Re-form when the clearance stays above the formation width for about 1 second, or about 30 ticks at 30 Hz. AoE II's switch to a "long vertical" shape over distance is the shipped precedent.\[16\] Mars and Chanut say the cost of falling back to the narrow configuration should be comparable to the cost of a longer route, and Sharma et al. (*Computer Graphics Forum*, 2024) plan explicitly for switching to formations "requiring less clearance in order to utilize narrower corridors that can lead to a shorter overall path."

**Turning: wheel versus re-form.** Wheeling pivots on the inner flank, and outer slots must travel π·W/2 for a 90° turn. Pottinger lists wheeling as a "more complex" formation model.\[65\] Recommendation: for turns of 45° or less, rotate the anchor's facing at a rate capped so the outer slot speed stays within the catch-up speed (ω ≤ (v_catch − v_group)/(W/2)). For larger turns, or at the destination, **re-form** (each unit takes the shortest path to its new slot) instead of wheeling. Total War offers both behaviours (Alt+Ctrl "swivel").\[35\]

**When to give up on the shape.** Drop to "loose" mode, where slots stop pulling and units just follow the flow field at group pace, when any of these holds: more than 25% of units have been more than 3 spacings from their slots for over 2 seconds; the anchor has been in column mode for more than 10 seconds; or a dense enemy or ally crowd is detected. Re-lock at the destination or once clearance returns. These thresholds are my suggestion; no source gives numbers.

### A5. Algorithms and performance at scale

**Slot generation.** Generate slots per role band as rows across the facing direction. Centre the row (Pottinger-style line: x = −w·(n/2) + w/2 + i·w).\[66\] For a drawn curve, resample the polyline by arc length at the band's spacing. If the curve is too short, add rows behind it, offset along the local normal. For staggered layouts, offset alternate rows by half a spacing. Cost: O(n), trivial.

**Assignment options.**
- *Greedy nearest-slot:* O(n²). This is the known cause of crossings and "circling" (Mars and Chanut).\[17\]
- *Sort-based:* project the units and the slots onto the formation's lateral axis, sort both, and match in order. For multiple rows, sort by (forward, lateral). O(n log n). Mars and Chanut recommend it "when no specialized slots are defined".\[17\] We get around that limit by running it **per role band**.
- *Hungarian / optimal assignment:* O(n³). It minimises total distance and therefore produces no crossing paths in the Euclidean case (Lee et al. 2017).\[67\] A 300-unit band is about 2.7×10⁷ basic operations. That is fine once per order on a worker job, but not every tick.
- *Recommended:* per band, use sort-based assignment with a cheap 2-opt pass (swap any pair whose segments intersect). Fall back to Hungarian or an auction algorithm for bands of 64 or fewer, where it costs microseconds. **Preserve relative positions**, which you already do: for "same shape" moves, the identity mapping onto translated and rotated slots is already crossing-free.

**Re-assignment cost and policy.** Assign once per order. Re-assign only at chokepoint entry or exit (using the same banded sort, so the ordering is stable), on group membership change (insert or remove locally and shift neighbours), or on a formation change. Never re-assign every tick: that causes "slot churn", where units chase a moving target.

**Integration with flow fields and ORCA.** One flow field per (goal, size class) is already shared. The anchor samples the field of the largest size class in the group, so the formation never routes where its tanks can't go. Per-unit desired velocity = normalise(w_slot·(slot_target − pos) + w_flow·flow_dir) × pace. ORCA then treats group peers with a shorter time horizon, so it "only consider[s] imminent collisions" (Mars and Chanut) and tight formations are not pushed apart.\[17\]

**Data-oriented, job-friendly design.**
- SoA arrays: `pos, vel, groupId, slotIndex, role, footprint`. Per group: `anchorPos, facing, pace, mode, slotOffsets[]` in a contiguous pool.
- Pipeline per 30 Hz tick: (1) group update job, parallel over groups: anchor advance, clearance probe, mode transitions; (2) slot target job, parallel over units: read only the group snapshot; (3) desired velocity; (4) ORCA, which reads the previous tick's positions; (5) integrate. Each stage reads the previous stage's immutable output. Mars and Chanut use the same "previous update state as read-only input" pattern to allow multithreading.\[17\]
- Budget estimate (suggestion): 2,000 units × about 100 ns per stage is about 0.2 ms per stage. Assignment happens only on order events and is amortised across ticks if needed.

**Determinism for replays and lockstep.** Use fixed-point or strictly controlled floating point. Sort with a total order (key, then unit ID). Process groups in group-ID order when writing shared state, or give each job disjoint output ranges. Never let a worker's completion order affect ties or tile-cache contents (Emerson's order-dependence warning).\[24\] Hungarian implementations must break ties deterministically.

### A6. Failure cases players hate, and how to avoid them

| Failure | Cause | Mitigation |
|---|---|---|
| Units crossing to reach slots | Greedy assignment | Banded sort + 2-opt / Hungarian per band |
| Stragglers | Slowest-unit rule only at the anchor; units snagged | Catch-up speed of 1.2–1.3× capped at the unit's max; anchor "leash" that slows when the centroid lags (Millington-style moderation) |
| Snagging on terrain | Slots placed inside obstacles | Project blocked slots to the nearest free cell; per-unit flow fallback when the slot is unreachable |
| Accordion effect | Followers stop and start in reaction to the units ahead | Extrapolated slot targets (τ of about 0.5–1 s); smooth acceleration; the group speed ramps |
| Walking past each other at arrival | Slot facing ignored; late re-assignment | Assign at order time; arrival tolerance of about 0.25 spacing; face the formation direction |
| Formation breaks in crowds | ORCA treats peers like strangers | Short peer horizon; loose mode in dense crowds; re-form on exit |
| Formation lost after next order | C&C3 design | Formation type persists on the group |
| Formation toggles unreliable | AoE IV bug reports | Formation change is an order in the command queue, processed deterministically |

---

## Part B: Formations in battle (designed for now, built later)

### B7. Meeting the enemy

**Sourced behaviour.**
- Total War locked groups pick "sensible target units relative to their formation positions" instead of all converging on one target. Unlocked groups "converge and blob up on the single target."\[33\]
- SupCom formation attack-move (Ctrl+Alt) has the formation "stop to engage units". The FAF wiki calls attack-move weak against artillery and splash damage.\[3\]\[36\]
- AoE II players use Line or Flank to dodge Mangonel shots and Staggered against Scorpions, so formations double as area-of-effect counterplay.\[44\]
- CoH3 deliberately made blobs more vulnerable to area-of-effect weapons.
- RA2's Attack-Move makes units "travel from place to place and actively attack & destroy enemies they encounter". Some units, such as Prism tanks, need it because they don't fire on the move.\[21\]\[27\]

**Recommended battle model (suggestion).**
- **Hold shape versus break:** each formation has a *cohesion* setting. *Locked*: units engage only from their slot area, within a leash of about 1–2 spacings. *Loose*: units may leave their slots to reach weapon range, then return. Attack-move defaults to Loose. Hold Position forces Locked.
- **Facing toward threats:** the group keeps a `threatBearing`. When contact is sustained, the formation may rotate toward it using the same capped-rate wheel as in A4.
- **Front and rear roles:** slot roles carry `engagementBand` = {absorb, line, support, standoff}. Standoff units (artillery) hold at about 0.8× their max range behind the front reference line and never path past it.
- **Range bands:** each band's preferred distance to the front line is derived from weapon range.
- **Flanking and wrapping:** a Flank formation splits into two anchors with a shared target point, like AoE II's Flank. "Wrap" moves the outer slots forward into a concave arc of radius about 1.5× target radius.
- **Focus versus spread fire:** use per-slot target selection, where each unit prefers targets in its lateral sector (Total War-style), plus a group "focus" toggle.
- **Retreat in order:** a reverse-move at group pace. Heavy units keep facing the threat, which matters for vehicles with directional armour, as in CoH. Rear bands leave first.
- **Re-forming after a fight:** trigger when no enemy has been in range for about 3 s (90 ticks). Re-assign using the banded sort from current positions to slots around the current centroid, not the original ones.

### B8. Stances

| Game | Stances (names) | Notes |
|---|---|---|
| RA2/YR | Guard (G), Attack-Move (Ctrl+Shift), Guard-Destination (Ctrl+Alt), Scatter (X) | Guard returns units to position after engaging |
| C&C3/KW | Aggressive, Guard (default), Hold Ground, Hold Fire | Manual: formations + Hold Ground = defensive line |
| AoE II DE | Aggressive, Defensive, Stand Ground, No Attack | Defensive pursues a few tiles then returns |
| AoE IV | Stand Ground / Hold Position only | Any move resets it to default |
| SupCom/FA | Return Fire (default), Ground Fire, Hold Fire | Separate attack-move / formation move |
| Total War WH3 | Fire at Will, Guard Mode, Skirmish, Melee mode | Guard stops pursuit; Skirmish kites melee |
| Homeworld | Aggressive / Neutral / Evasive tactics | Combined with formations |

Recommended stance set (suggestion): **Aggressive** (pursue; group cohesion Loose), **Guard** (default: engage within leash, return to slot), **Hold Position** (Locked, no movement), **Hold Fire**. Stances are stored per unit but set per group, and formation cohesion is derived from the stance unless overridden. (The Homeworld tactic names come from general knowledge of the series, not from the sources gathered here.)

### B9. What the movement layer must decide now

Data a group should keep from day one (suggestion, informed by Pottinger's group definition of units, max speed, centroid and commander, and by Mars and Chanut's slot properties of position, orientation and role):\[17\]\[65\]

- `GroupId`, a member list sorted by unit ID, and the `formationType` (persists across orders)
- `anchorPos`, `facing`, `pace`, `mode` ∈ {Forming, Moving, Column, Loose, Arrived, Engaged, Reforming}
- Per slot: `offset`, `facing`, `roleBand`, `engagementBand`, `assignedUnit`
- `frontLine` reference (a segment, or a polyline for drawn formations), `width`, `depth`
- `cohesion` (Locked/Loose), `stance`, and `catchUpMultiplier`
- `threatBearing` (unused until Part B) and `lastContactTick`
- Re-form triggers as named, tunable events: `onChokepointExit`, `onCombatEnd`, `onMembershipChange`, `onOrder`
- Command-queue integration: a formation change, a facing change and waypoints with per-waypoint formation (SupCom supports formation waypoints and patrols)\[6\]

---

## Recommended design for our game

**Default (no formation chosen): "Auto Line".** Role bands as in A3. Width-to-depth about 2:1 per band. Group pace = slowest × 0.95, catch-up up to 1.25×. Facing = direction from the group centroid to the target. Assignment = banded sort with 2-opt. Column mode at chokepoints, with re-form after 1 s of clearance. Relative positions are kept for short moves (target within about 2 formation widths) to avoid shuffling.

**Formation set:**
1. **Auto Line** (default, above).
2. **Column**: 1–3 files and role-banded, for roads and bridges. It is also the automatic chokepoint mode.
3. **Armoured Wedge / Spearhead**: heavy armour at the front and front flanks in a shallow V (about 30° arms), infantry and AA inside, artillery at the rear apex.
4. **Box / Escort**: heavy units on the perimeter, soft units in the centre. Warn the player that it is weak against artillery, as AoE II's tutorial does.
5. **Staggered / Loose**: any formation with spacing ×2 and alternate rows offset, against area-of-effect weapons (Prism, V3, Desolators).
6. **Custom line**: a freehand polyline; bands fill from the front of the line backward.
7. **Saved custom** (later): store the slot offsets and roles as a template.

**Controls:** right-click = Auto (or the persisted formation). Right-drag = straight front line (width and facing, live ghosts). Alt+right-drag = freehand line. Ctrl+right-click = translate while keeping the current shape. Formation hotkeys F1–F6 persist on the group. A Loose/Locked toggle. Shift queues waypoints, each with its own formation. Pressing a formation key in the middle of a move re-forms around the current anchor, with re-assignment through the order queue.

**Alternatives and trade-offs.**
- *Optional formations (SupCom/C&C3 style):* less CPU and less "magic", but evidence suggests low adoption.
- *Full Hungarian for all units:* best crossing avoidance, but O(n³) spikes at 300 or more units.
- *Rigid virtual leader:* prettiest on open maps, but poor on RA2-style urban maps with bridges.
- *Pure flocking (SC2):* cheapest and most fluid, but no roles and blobs that area-of-effect weapons punish.

## Caveats
- Several controls and behaviours come from community posts, fan wikis and manuals reproduced on third-party sites, not from developer documentation. The SupCom "shields spread / tanks in front" claim is unverified.
- The AoE IV formation set differs between sources (Column vs Box), probably because of patches.
- All numeric thresholds in the recommendations (catch-up 1.25×, re-form after 1 s / 3 s, spacing multipliers, loose-mode triggers) are my starting values, not sourced; tune them in playtests.
- The Hungarian no-crossing property holds for Euclidean straight-line costs. With flow-field path costs, crossings can still occur near obstacles.\[68\]

## Open questions for the designer
1. Should Auto Line be the default for *every* right-click, or only for groups above N units, or only for mixed classes?
2. On long trips, should units travel at their own speed and converge late (BAR Alt-move), or at group pace the whole way?
3. How much should formations matter in combat (e.g. a flank/rear armour bonus, or area-of-effect splash) so they are not strictly worse than clumping?
4. Should drawn custom formations be saveable, shareable, and usable by the AI (as in Total War)?
5. What should happen when groups merge or units are added mid-move: append to the rear band, or re-assign?
6. How should aircraft (Kirovs, Harriers) and naval units interact with ground formations: excluded, or escorted?
7. What is the maximum group size before automatic splitting into sub-formations (e.g. 200 units per formation)?
8. Should Hold Position formations block allied traffic, as AoE IV's Stand Ground makes units impassable?\[18\]

## Sources

1. [Formations - Age of Kings Heaven - HeavenGames](https://aok.heavengames.com/university/game-info/general-info/formations/)
2. [units move at the same speed? - Command & Conquer 3: Tiberium Wars](https://gamefaqs.gamespot.com/boards/932602-command-and-conquer-3-tiberium-wars/40822992)
3. [Unit Micro](https://wiki.faforever.com/Play/Learning/Unit-Micro)
4. [formation in cnc3? - GameReplays.org](https://www.gamereplays.org/community/index.php?showtopic=304701)
5. [Total War: WARHAMMER III](https://steamcommunity.com/app/1142710/discussions/0/3827536762642191478)
6. [Formation Move (ctrl + right click) - GameReplays.org](https://www.gamereplays.org/community/index.php?showtopic=164243)
7. [Company of Heroes 2 - manual](https://www.feralinteractive.com/en/manuals/companyofheroes2/latest/steam/)
8. [Total War: SHOGUN 2](https://steamcommunity.com/app/34330/discussions/0/3039355280230136766)
9. [CONTROLLING YOUR FORCES 101 :: Command&Conquer3](https://steamcommunity.com/groups/CommandnConquer3TW/discussions/0/598199244893720192/)
10. [Commands 2.0 ★ Beyond All Reason RTS](https://www.beyondallreason.info/commands-20)
11. [Steam-Community :: Guide :: WARNO BASIC GUIDE FOR COMPLETE BEGINNERS](https://steamcommunity.com/sharedfiles/filedetails/?l=german&id=2727549821)
12. [Essential hotkeys and unit selection tricks in Beyond All Reason](https://www.crdhq.com/articles/bar-useful-hotkeys-and-unit-selection-tricks)
13. [Group Formations documentation - Total War Wiki](https://wiki.totalwar.com/w/Group_Formations_documentation.html)
14. [Building a RTS Formation System. A deeper dive into implementing…](https://medium.com/@vladlucam/building-a-rts-formation-system-c66b97b27ca0)
15. [CAT-ORA: Collision-Aware Time-Optimal Formation Reshaping for Efficient Robot Coordination in 3D Environments](https://arxiv.org/pdf/2412.00603)
16. [Unit formation](https://ageofempires.fandom.com/wiki/Unit_formation)
17. <https://www.gameaipro.com/GameAIPro2/GameAIPro2_Chapter20_Hierarchical_Architecture_for_Group_Navigation_Behaviors.pdf>
18. [Unit stance](https://ageofempires.fandom.com/wiki/Unit_stance)
19. [1 Epilepsy Warning](https://cdn.cloudflare.steamstatic.com/steam/apps/24800/manuals/CCRA3UPpcdMANukeng1.pdf?t=1615390663)
20. [Nooby Questions - Command & Conquer 3: Tiberium Wars](https://gamefaqs.gamespot.com/boards/932602-command-and-conquer-3-tiberium-wars/51062628)
21. [Red Alert 2](http://nyerguds.arsaneus-design.com/manuals/Red%20Alert%202/Red%20Alert%202%20-%20English.pdf)
22. [Battle tactics panel - Total War: WARHAMMER Wiki - Fandom](https://totalwarwarhammer.fandom.com/wiki/Battle_tactics_panel)
23. [Company of Heroes 3 (CoH3) Update 1.000.005 Patch Notes](https://updatecrazy.com/company-of-heroes-3-coh3-update-1-000-005-patch-notes/)
24. [307 Crowd Pathfinding and Steering Using Flow Field Tiles Elijah Emerson 23](https://www.gameaipro.com/GameAIPro/GameAIPro_Chapter23_Crowd_Pathfinding_and_Steering_Using_Flow_Field_Tiles.pdf)
25. [Red Alert 2 Hotkeys - CnCmaps](http://cncmaps.net/red-alert-2-hotkeys/)
26. [Steam Community :: Guide :: Red Alert™ 2 basic Hotkeys](https://steamcommunity.com/sharedfiles/filedetails/?id=3175448213)
27. [Yuri's Revenge Hotkeys Cheatsheet — Logomonic Learning](http://www.logomoniclearning.com/hotkeys-1/2021/5/6/hotkeys)
28. [Hotkeys - Page 2 - Red Alert 2 & Yuri's Revenge - XWIS](https://xwis.net/forums/index.php/topic/172665-hotkeys/page-2)
29. [Command & Conquer: The Ultimate Collection - Official Game Manual](https://manuals.plus/m/83045ef11c377e01e072273076244b950bc1fa0b06ea738021263899464865b6)
30. [Command & Conquer 3: Tiberium Wars/Controls — StrategyWiki](https://strategywiki.org/wiki/Command_&_Conquer_3:_Tiberium_Wars/Controls)
31. [Company of Heroes 2](https://steamcommunity.com/app/231430/discussions/0/627456486970471821)
32. [2.5.0 Update Patch Notes - Company of Heroes 3](https://patchbot.io/games/company-of-heroes-3/articles/968-250-update-patch-notes)
33. [Total War: Warhammer Formation Control Guide and Tips - Prima Games](https://primagames.com/featured/total-war-warhammer-formation-control-guide-tips)
34. [Group formation missing? :: Total War: WARHAMMER General Discussions](https://steamcommunity.com/app/364360/discussions/0/357286663695928262/)
35. [Total War: WARHAMMER](https://steamcommunity.com/app/364360/discussions/0/353915953248732730)
36. [marching Formations - Supreme Commander: Forged Alliance](https://gamefaqs.gamespot.com/boards/941687-supreme-commander-forged-alliance/41589412)
37. [Attack Move / Formation Orders - GameReplays.org](https://www.gamereplays.org/community/index.php?showtopic=154096&mode=linear)
38. [Supreme Commander Complete Guide (3 Sections)](https://www.ludo.guide/guide/supreme-commander)
39. [Forged Alliance Forever • View topic - Formation and movement of ships](https://forums.faforever.com/viewtopic.php?f=67&t=10380)
40. [Supreme Commander: Forged Alliance](https://steamcommunity.com/app/9420/discussions/0/1290690669219630316)
41. [Crowd Pathfinding and Steering Using Flow Field Tiles](https://www.semanticscholar.org/paper/Crowd-Pathfinding-and-Steering-Using-Flow-Field-Emerson/a5334e28ea30b1865b87c3881a310b4f702beac3)
42. [HPA\*](http://davexi.co.uk/?page_id=40)
43. [Battle Formations](https://ageofempires.fandom.com/wiki/Battle_Formations)
44. [How do you attack or defend while holding formation? :: Age of Empires II: Definitive Edition General Discussions](https://steamcommunity.com/app/813780/discussions/0/4295943285926930402/)
45. [\[Suggestion\] Pick 4 Formations - IV - Discussion - Age of Empires Forum](https://forums.ageofempires.com/t/suggestion-pick-4-formations/286134)
46. [Wedge formation? - IV - Discussion - Age of Empires Forum](https://forums.ageofempires.com/t/wedge-formation/174834)
47. [Unit formations don't work properly - IV - Report a Bug - Age of Empires Forum](https://forums.ageofempires.com/t/unit-formations-dont-work-properly/185390)
48. [The Mechanics of SC2 \[Part 1\]](https://tl.net/forum/starcraft-2/132171-the-mechanics-of-sc2-part-1)
49. [\[D\] Pathfinding in SC2 - Page 2](https://tl.net/forum/starcraft-2/68954-pathfinding-in-sc2?page=2)
50. [Moving in formation :: Men of War: Assault Squad 2 General Discussions](https://steamcommunity.com/app/244450/discussions/0/540734792637635042/)
51. [moving whole squan instead one soldier :: Men of War: Assault Squad 2 General Discussions](https://steamcommunity.com/app/244450/discussions/0/616198623979698808/)
52. [Can't Select Multiple Units? :: Men of War: Assault Squad 2 General Discussions](https://steamcommunity.com/app/244450/discussions/0/1488866180604742407/)
53. [How to Make Units Spread Out? :: Men of War: Assault Squad 2 General Discussions](https://steamcommunity.com/app/244450/discussions/0/1761356057411375550/)
54. [Is there a way to make units move in formation? :: Wargame: Red Dragon General Discussions](https://steamcommunity.com/app/251060/discussions/0/3276824488725019592/)
55. [what is this different then wargame red dragon :: WARNO General Discussions](https://steamcommunity.com/app/1611600/discussions/0/5696507684094943309/)
56. [Steam Community :: Guide :: Homeworld: Historical and Technical Briefing](https://steamcommunity.com/sharedfiles/filedetails?id=398955008)
57. [Alien Legacy - Homeworld walkthrough - guide walkthrough](https://strategygamers.com/walkthrough/homeworld-remastered-collection-4)
58. [Best formations in Homeworld 3, listed](https://dotesports.com/homeworld/news/best-formations-in-homeworld-3-listed)
59. [Homeworld - Before I Play](https://beforeiplay.com/index.php?title=Homeworld)
60. [Halo Wars: Controls and commands Part I - gamepressure.com](https://guides.gamepressure.com/halowars/guide.asp?ID=6563)
61. [Halo Wars Review](https://giantbomb.com/reviews/halo-wars-review)
62. [Halo Wars Review - Giant Bomb](https://www.giantbomb.com/reviews/halo-wars-review/1900-117/)
63. [Attack Line ⇀ Command ★ Beyond All Reason RTS](https://www.beyondallreason.info/commands/attack-line)
64. [Total War: WARHAMMER](https://steamcommunity.com/app/364360/discussions/0/357287304421881185)
65. [Implementing Coordinated Movement](https://www.gamedeveloper.com/programming/implementing-coordinated-movement)
66. [GitHub - EezehDev/AI-Formations: Research project on coordinated movement of AI made with Unity. · GitHub](https://github.com/EezehDev/AI-Formations)
67. [Defect-free atomic array formation using Hungarian matching algorithm](https://arxiv.org/pdf/1704.07074)
68. [Formation Control for Connected and Automated Vehicles on Multi-lane Roads: Relative Motion Planning and Conflict Resolution](https://arxiv.org/pdf/2103.10287)
