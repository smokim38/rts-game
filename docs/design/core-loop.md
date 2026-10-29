# Core Skirmish Loop

> Part of the Game Design Document (GDD). It builds on [vision.md](vision.md): every mechanic below names the design pillar(s) it serves and, where it adds work for every unit, states that per-unit cost (Pillar 4). Balance numbers (costs, health, damage, speeds) belong in later GDD sections. Technical choices belong in ADRs (`docs/adr/`).

## Overview

A match is one human player against 1 to 7 AI opponents on a skirmish map, as a free-for-all or in teams. Each side starts with a small base. You gather one resource, **Supply**, with harvesting vehicles. You spend it on buildings, then on an army, and you fight until one side has no base left. A typical 1 against 1 match should last 15 to 25 minutes; matches with more sides run longer (see [Target match length](#target-match-length)).

The first release has **two factions**. They play by the same rules and economy, and differ in their units. The AI opponent plays by exactly the same rules as the player. It sees only what its own units see, gets no free resources, and gives the same orders a player can give.

Before any art exists, we build all of this in a **grey-box sandbox**: flat ground, units as plain cubes and buildings as plain boxes. Movement comes first, because it has to work well before anything else is built on top of it. The [Grey-box sandbox](#grey-box-sandbox) part lists the systems in build order, with acceptance criteria for each.

## How to read this section

- **Pillars** are numbered as in [vision.md](vision.md): **P1** Fast and readable, **P2** Smarter AI, **P3** Movement that just works, **P4** Armies at scale.
- **Per-unit cost** says what extra work a mechanic adds for every unit on the map. "None" means the cost is per side or per building, not per unit.
- **Names are placeholders.** The building, unit and resource names here describe what each thing does (Collector, Depot, Generator). The factions section will give each faction its own original names.
- A **cell** is one square of the building grid. All map distances in this document are in cells.

## Match setup

### Players and the unit cap

| Setting | First release | Why |
|---|---|---|
| Sides | **2 to 8 sides:** 1 human player against 1 to 7 AI opponents | Owner (Issue #145): up to 8 sides, as in the classic games of this style. Vision: skirmish against AI only. 1 against 1 is the main case and is built first ([Grey-box sandbox](#grey-box-sandbox)) |
| Teams | Free-for-all, or teams set in setup (see [Teams](#teams)) | Owner (Issue #145) |
| Faction choice | Each side picks one of the two factions, or Random | Two factions (owner, 2026-10-22) |
| Unit cap | **2,000 mobile units in the whole match**, shared equally between the sides (table below). Buildings don't count | Owner: "Max 2000 units", kept for matches of every size for now (Issue #145). This matches the vision's performance target of 2,000 active units, so a full match is always inside what we test. A higher cap for 6 to 8 sides is open question 9 |
| AI difficulty | Easy, Normal, Hard, set for each AI side (see [The AI opponent](#the-ai-opponent)) | P2 |

**Unit cap per side.** Each side's cap is 2,000 divided by the number of sides at match start, rounded down, so the match total never goes over 2,000:

| Sides | 2 | 3 | 4 | 5 | 6 | 7 | 8 |
|---|---|---|---|---|---|---|---|
| Cap per side | 1,000 | 666 | 500 | 400 | 333 | 285 | 250 |

- The cap is per side in team matches too. In a 1v3, the side that is alone has 500 and so does each side of the three (default; open question 10).
- Shares are fixed at match start. When a side is out, its share is not passed on to anyone.

**Pillars:** P1, P4. **Per-unit cost:** none. The cap is what keeps every other per-unit cost bounded. With 8 sides the match total stays at 2,000, so per-unit costs stay inside what we test. What grows is per-side work (up to 7 AI commanders, one fog of war per side) and map size (see [The map](#the-map)). Once 8-side maps exist, the performance check should also run on the largest of them.

### Teams

The setup screen has one slot per side: the human first, then each AI. Each slot sets the faction, the team, the start position (or Random) and, for an AI, its difficulty.

- **Team** is None or Team 1 to 4. Sides with the same team number are allies. A side set to None plays alone. Four numbers are enough: 8 sides can form at most four teams of two or more.
- **Layout presets** fill in every slot's team in one click. Any slot can still be changed afterwards.

| Sides | Free-for-all | Team layouts offered |
|---|---|---|
| 2 | 1v1 | none |
| 3 | 1v1v1 | 1v2 |
| 4 | 1v1v1v1 | 2v2, 1v3 |
| 5 | 1v1v1v1v1 | 2v3, 1v4 |
| 6 | 1v1v1v1v1v1 | 3v3, 2v2v2, 1v5 |
| 7 | 1v1v1v1v1v1v1 | 3v4, 1v6 |
| 8 | 1v1v1v1v1v1v1v1 | 4v4, 2v2v2v2, 1v7 |

- **1vN** puts the human alone against every AI on one team. In other layouts the human can have AI allies, for example the human and three AIs against four AIs in a 4v4.
- **Uneven teams** (such as 1v3 or 3v5) are allowed.
- **The match can't start** unless it has at least two sides and at least two opposing parties: if every side is on the same team, setup refuses to start and says why.
- **Allies** (defaults, open question 11):
  - never attack each other. Auto-targeting and attack-move ignore allied units and buildings, and an attack order on an ally is refused.
  - share vision: each side sees everything its allies see.
  - share nothing else. Supply, units, build radius, Depots and production stay with each side. Only a side's own idle units step aside for its moving units (S3 item 5).
  - start on neighbouring start positions, unless positions are chosen by hand.

**Pillars:** P1 (named layouts, set up in one click); P2 (AI allies and opponents play by the same rules as the player). **Per-unit cost:** the enemy check in target search becomes one lookup in a small side-against-side table (8 by 8) per candidate target, about the same cost as telling own from enemy. Shared vision is combined per team on the cell grid, not per unit.

### The map

- **Size is set by travel time, not by cells.** On a map for two sides, a medium-speed vehicle crosses from one base to the other in about **60 to 90 seconds**. Maps for 4, 6 and 8 sides keep **the same distance between neighbouring bases** (the nearest other start positions). This keeps first contact early with any number of sides (P1) and gives large armies room to move (P3, P4).
- **Start positions** are fair at every side count. Each one has:
  - the same open building space, one Supply field next to it, and two more fields within a short drive (the natural places to expand)
  - the same number of neighbouring bases, at the neighbour distance, with every neighbour distance on the map within 5% of the others.

  Maps are mirrored or rotated copies of one quarter or half wherever the square grid allows. Positions are random unless chosen in setup.

| Sides | Start layout | Neighbours per base |
|---|---|---|
| 2 | Facing each other across the map, one half a rotated copy of the other | 1 |
| 4 | One near each corner, each quarter a rotated copy | 2 |
| 6 | A ring of six round the middle of the map | 2 |
| 8 | A ring of eight round the middle of the map | 2 |

- **Fewer sides than positions:** a map can be played with fewer sides than its start positions. Unused positions stay empty.
- **Size limit.** A map may have at most **87,381 cells (about 295 x 295)** until a storage decision is made first ([ADR 0011](../adr/0011-flow-field-storage.md)). A larger map can't be built before then.
- **Rough sizes.** The neighbour distance is a balance number, fixed by tuning the 2-side map. For planning only, at 128 cells (a medium-speed vehicle at about 2 cells per second, with detours) and room for a base between each start position and the map edge:

| Sides | Rough map size | Cells | Within the size limit? |
|---|---|---|---|
| 2 | about 150 x 150 | about 22,500 | Yes |
| 4 | about 180 x 180 | about 32,400 | Yes |
| 6 | about 305 x 270 | about 82,400 | Only just. Over the limit if the distance ends up much above 128 cells |
| 8 | about 385 x 385 | about 148,000 | No. An 8-side map fits only if the neighbour distance is at most about 90 cells (open question 12) |

- **The first build is the 2-side map.** Maps for 4, 6 and 8 sides come later (see [Grey-box sandbox](#grey-box-sandbox)).
- **Terrain in the first release:** open ground, impassable blocks (cliffs, rocks, buildings) and narrow passes between them. Height, water and bridges are later questions (open question 4).
- **In the sandbox:** one flat square of ground, start positions marked on it, and boxes as obstacles.

**Pillars:** P1, P3, P4. **Per-unit cost:** none. Blocked terrain costs movement time per unit, and that is covered under movement. Bigger maps and more sides add work per cell for each side (vision and explored ground), which grows with map size and side count, not with units.

### What each side starts with

| Item | Count | Why |
|---|---|---|
| Headquarters (HQ), already in place | 1 | Starting with a working base skips a slow opening (P1). The HQ produces all buildings |
| Depot (Supply drop-off) | 1 | The economy runs from the first second (P1) |
| Collector (Supply harvester) | 2 | Enough income to make the first choices matter |
| Basic infantry | 6 | For scouting and early defence (P2: the AI scouts too) |
| Starting Supply | Set in the economy section | Balance number |

## The loop

The match is one repeating cycle: **gather → build → train → fight → expand**, and back to gathering. Each step below gives the rule, the pillars it serves and its per-unit cost.

### 1. Gather

- **One gathered resource: Supply.** It lies in fields on the map. Collectors drive to a field, fill up, drive to the nearest Depot and unload, and the side's Supply goes up by exactly the amount unloaded. They repeat this on their own until ordered otherwise.
- **Fields run out and don't regrow.** A Collector whose field runs dry moves on to the nearest field it knows about. With no field left, it goes back to its Depot and waits, and the player is told. Finite fields push sides to expand and stop matches stalling (P1).
- **Each new Depot comes with one free Collector,** so building a Depot at a new field starts income there right away.
- **Power is not a resource you collect.** Generators produce power, and most buildings use it. Power isn't stockpiled; it is the difference between what is produced and what is used (see [Build](#2-build)).

**Pillars:** P1 (one resource, one counter, one simple loop); P2 (the economy is something both sides can attack and defend, and the AI must plan it). **Per-unit cost:** only Collectors run the gathering loop, and a side needs tens of them, not hundreds. The cost is their repeated trips between field and Depot.

### 2. Build

- **The HQ produces one building at a time.** When it is ready, the player places it. Placement is on free, open cells within the **build radius** of the side's own buildings. Building first and placing afterwards means no builder units are needed (P4) and the base grows fast (P1).
- **Cost is paid as production goes.** If Supply runs out, production pauses and carries on when Supply comes in. Cancelling refunds what was spent.
- **Prerequisites form a short tech tree.** Each building needs one or more others before it can be made. If a prerequisite is destroyed, whatever depends on it is locked again until it is rebuilt.
- **Power:** when a side uses more power than it produces, all production slows down and powered defences stop firing. Fixing the shortfall restores both at once. This makes Generators a target worth raiding (P2).
- **Repair and sell:** a damaged building can be repaired for Supply. A building can be sold back for part of its cost.
- **Expanding:** the Vehicle Yard can build a **Field Truck**. It drives anywhere and unfolds into an **Outpost**: a small HQ that adds build radius and can produce buildings. This is how a side reaches distant fields, and how it can recover after losing its HQ.

Building categories that both factions share:

| Category | Job |
|---|---|
| HQ / Outpost | Produces buildings, gives build radius |
| Generator | Produces power |
| Depot | Supply drop-off; comes with one Collector |
| Infantry Yard | Trains infantry |
| Vehicle Yard | Builds vehicles, Collectors and Field Trucks |
| Tech Center | Unlocks advanced units |
| Defences | Fixed guns that protect the base |

**Pillars:** P1 (fast base growth, readable rules); P2 (power and prerequisites give the AI real targets and plans). **Per-unit cost:** none. Buildings are few and don't move.

### 3. Train

- **Production queues are per category,** one for infantry and one for vehicles, not one per building. Each extra Yard of the same kind makes that queue faster. A big army then takes few clicks (P1, P4).
- **Rally point:** new units drive to the rally point the player set, and gather there as a group (P3).
- **At the unit cap,** production pauses with a message and carries on as soon as the side drops below its cap.

**Pillars:** P1, P3, P4. **Per-unit cost:** none beyond the unit existing. A unit leaving a Yard is an ordinary move order.

### 4. Fight

**Orders** the player and the AI can give: move, attack a target, attack-move (move and fight anything met on the way), stop, hold position, and queued waypoints. Every order gets instant feedback: a marker at the target and the units reacting on the next visible frame (P1).

**Combat rules:**
- Each unit has health, an armour class, and one weapon with damage, range, rate of fire and a weapon type. A small table sets how much each weapon type hurts each armour class. This gives clear counters (anti-armour beats vehicles, and so on) with one lookup per shot (P1, P4).
- Most weapons hit at once. Shots that fly to their target (artillery, missiles) are kept for units where the delay matters to play, because each shot in flight is extra work.

**Unit behaviour** (P2: units act sensibly, so the player micromanages to gain an edge, not to fix bad behaviour):
- **Auto-targeting:** a unit with no target fires at the best enemy in range. It prefers targets it hurts most, and enemies attacking it. It keeps its target until that target dies or leaves range, so it doesn't flicker between targets.
- **Chase limit:** an idle unit that starts a fight on its own chases only a short distance, then goes back to where it was.
- **Retreat:** a group that is clearly losing pulls back towards its rally point. The group decides this together, not each unit on its own. A direct player order (hold position, attack this) always overrides retreat.
- **Formation:** a group fighting on attack-move stays together instead of stringing out behind its fastest units.

**Vision and fog of war:** each unit and building sees a set radius. A side sees only what its units see. Ground it has explored shows the buildings last seen there, and unexplored ground is hidden. The AI uses exactly the same fog (P2: no information it shouldn't have).

**Per-unit costs (P4):**

| Mechanic | Cost for every unit | How we keep it down |
|---|---|---|
| Movement | Every moving unit, all the time. The largest cost in the game | Settled by ADR. Groups move as groups (P3) |
| Target search | Every armed unit looks for enemies in range | Search on a short interval, not continuously, and keep targets (no flicker) |
| Vision | Every unit and building updates what its side can see | Update on a short interval, on the cell grid |
| Damage | One table lookup per shot | Fixed, small table |
| Shots in flight | One extra object per shot, only for weapons that use them | Only on the few units that need them |
| Retreat check | Per group, not per unit | Groups decide together |

### 5. Expand and repeat

Fields near the start run out, so sides must claim and hold new fields with Depots and Outposts. Every new field is another place to raid and defend. That gives the AI commander real choices about where to fight, and keeps the match moving towards an end (P1, P2).

## Win and loss

- **A side is out** when it has **no buildings and no Field Truck** left. Its other units are removed from the map at that moment. Units alone can't keep a side alive, so nobody has to hunt down the last stray unit (P1).
- **Victory:** the player wins when every side that isn't on the player's team is out. On teams, a team is out when all of its members are out. AI allies still in the match share the win.
- **Defeat:** the player loses as soon as they are out, even if AI allies are still playing. The match ends then; there is no spectating in the first release.
- **Draw:** if the last sides are knocked out at the same moment, the match is a draw.
- **Surrender:** the player can quit at any time, and it counts as a loss.
- **No time limit.** The end screen shows match time, units built and lost, and Supply gathered.

**Pillars:** P1. **Per-unit cost:** none. The check counts buildings and Field Trucks per side.

## Target match length

| Phase | Target time (1 against 1, Normal AI) | Target time (6 to 8 sides, Normal AI) | What happens |
|---|---|---|---|
| Opening | 0 to 4 minutes | 0 to 4 minutes | Economy and first buildings; scouts meet |
| First fights | about 4 to 6 minutes | about 4 to 6 minutes | The first real attack or raid |
| Middle | 6 to 15 minutes | 6 to 20 minutes | Expanding, tech, armies in the hundreds |
| End | 15 to 25 minutes | 20 to 40 minutes | Large battles, bases fall until one side or team is left |

These are targets to tune against, not limits. Hard AI matches may be shorter. Matches with 3 to 5 sides fall between the two columns.

- **Why the opening stays the same:** neighbouring bases are the same distance apart on every map, so first contact comes at the same time.
- **Why the end runs longer:** a free-for-all ends only when one side is left, so bases fall one after another. Each side's army is smaller (250 to 333 units), but there are more of them. Team matches such as 4v4 should sit near the shorter end, because each team fields up to 1,000 units, as a side does in a 1 against 1.
- The 6 to 8 side targets are defaults to tune once the AI plays full matches of that size (open question 13).

**Pillars:** P1 (quick matches); P4 (the middle and end are where big battles are normal play).

## The AI opponent

The AI plays by the player's rules. It works on three levels.

| Level | What it does | Per-unit cost |
|---|---|---|
| **Commander** (one per AI side) | Plans the economy and build order, scouts, decides when and where to attack, and changes plan in response to what it has seen the player do | None: one per side |
| **Squads** (groups of units) | Attack, defend, raid, retreat and regroup as a group | Per group, not per unit |
| **Units** | The same unit behaviour the player's units have (auto-targeting, chase limit, formation) | Shared with player units; nothing extra |

**Fairness rules** (P2):
- Same starting assets, same costs, same unit cap. No hidden resource or production bonuses at any difficulty.
- Same fog of war. The AI knows only what its units have seen, plus what its allies see, exactly as for the player.
- Same orders. The AI gives the orders a player can give, and no others.
- A limit on how many orders per minute the AI gives, set by difficulty, so it can't win only by inhuman speed.

**Difficulty** changes the quality of decisions, not the rules: how well it plans its economy, how much it scouts, how fast it reacts, and how well it picks fights. Selectable personalities (aggressive, defensive) remain an open question in vision.md and #18.

### With up to 7 AI sides

- **One Commander per AI side,** so up to 7 in an 8-side match. Each has its own difficulty and its own order limit. Seven Commanders do seven times the planning work of one, so their planning must be spread over time rather than all landing in the same frame (for the AI's technical design to settle).
- **Choosing whom to fight.** In a free-for-all, each Commander picks its targets from what it has seen: first whoever threatens its own base, then the opponent it judges it can hurt most (near, weak, or busy fighting someone else). It treats the human like any other opponent. No rule makes the AIs gang up on the player or spare them.
- **Allies.** An AI treats allied sides as not enemies and shares their vision, exactly as the player does. In the first build each Commander plays its own game: no shared plans, no joint attacks and no help sent to an ally (open question 14).

**Pillars:** P2 (a fair opponent that picks its fights, in any match size); P4. **Per-unit cost:** none. Commanders are per side. Choosing whom to fight is a Commander decision, not a unit one.

## The two factions

The first release has two factions.

**What they share:**
- Supply as the one resource, Collectors and Depots, and power.
- The building categories in [Build](#2-build), the HQ and Field Truck, and the win and loss rules.
- The order set and the unit behaviour rules.
- Broad unit roles: Collector, basic infantry, anti-armour infantry, light fast vehicle, main battle vehicle, artillery, and base defences.

**Where they may differ:**
- Stats and costs within each role (for example, one faction is cheaper and more numerous, the other tougher and more expensive).
- A few units unique to each faction.
- One signature mechanic per faction, at most, if it doesn't add a per-unit cost that breaks P4.

Faction themes, names, unit rosters and balance numbers are left to the factions, units and economy sections (open questions 3 and 7).

## Grey-box sandbox

Everything here is built and tested with **plain ground and simple shapes**: a flat square map, units as cubes, buildings as boxes, Supply fields as flat tiles, and plain team colours so sides can be told apart. No art.

**Sandbox test units.** These values exist only for testing and are not balance numbers:
- **Small cube:** half a cell wide, speed 4 cells per second, health 100, one weapon: 10 damage per shot, 1 shot per second, range 5 cells.
- **Large cube:** 1 cell wide, speed 2.5 cells per second, health 300, the same weapon.

Every system below must keep working with the sandbox spawn tool (place any number of cubes for either side) and an on-screen counter for units and frame rate. "Performance target" means the vision's target: 2,000 active units at 60 fps at 1080p on the reference PC, measured as settled by vision.md open question 3.

Suggested build order. Each system is one or more later Issues.

**8-side matches** (4 to 7 AI opponents, the larger team layouts, and maps for 6 and 8 sides) come after the 1 against 1 systems, S1 to S12, work, as later Issues.

### S1. Camera and selection

1. The camera looks down at a fixed angle. It pans with the arrow keys and the screen edges, zooms between a set nearest and farthest distance, and can't leave the map.
2. Clicking an own unit selects it, and the deselect input clears the selection; which mouse buttons these use follows the active controls option ([controls.md](controls.md)). Dragging a box selects exactly the own units whose centre is inside the box. Enemy units are never added to a box selection.
3. Shift+click adds or removes one unit; Shift+drag adds the units in the box to the current selection.
4. Ctrl+1 to Ctrl+9 saves the selection as a group; pressing the number selects it again, and pressing it twice quickly centres the camera on it. Destroyed units leave every group.
5. Box-selecting 2,000 units shows the selection on the next frame, and that frame takes no longer than 33 ms.

### S2. Moving a group

1. A move order on ground with units selected (the move click of the active controls option, see [controls.md](controls.md)) shows a marker there, and every selected unit starts moving within 0.1 s.
2. 100 small cubes sent to an open point stop with no two overlapping, packed into an area no larger than twice their combined footprint.
3. A mixed group (50 small, 10 large cubes) sent 50 cells keeps together: the first and last units arrive within 3 s of each other.
4. Two seconds after arriving, no unit is still moving (no jitter or sliding).
5. 2,000 small cubes ordered across the map at once all arrive with no overlaps, and the game holds the performance target throughout.

### S3. Paths around obstacles and through crowds

1. A unit sent past box obstacles never passes through them, and its route is no more than 1.25 times the shortest possible route.
2. A unit sent into an area it can't reach (inside a closed ring of boxes) moves to the nearest reachable point and stops, with no endless retrying.
3. 200 small cubes sent through a 3-cell-wide gap all get through, and none stands still for more than 5 s while it still has somewhere to go.
4. Two groups of 100 sent through each other in opposite directions on open ground both reach their targets, and no unit stands still for more than 5 s on the way.
5. Idle friendly units in a moving unit's way step aside to let it pass, without a new order, and stay within 2 cells of where they were.

### S4. Order set

1. Stop: units halt within 0.5 s and drop their current order.
2. Hold position: units don't move for any reason, including being attacked, until given a new order. Once S5 exists, they still fire at enemies in range.
3. Shift with the move click of the active controls option ([controls.md](controls.md)) queues up to 16 waypoints. Units visit them in order, and the queued route is shown while they are selected.
4. A new order without Shift replaces the whole queue at once.

### S5. Combat basics

1. A unit with an enemy in range fires on its own. Each hit lowers the target's health by exactly the damage in the table, and a unit at 0 health is removed from the map, its selection and its groups within 1 s.
2. Attack-target: the unit moves into range of the chosen enemy, attacks only it until it is destroyed, then goes back to auto-targeting.
3. Attack-move: units head for the point, fight every enemy they meet, and carry on to the point once no enemy is in range.
4. With a test damage table of 100%, 50% and 25% for three armour classes, a 10-damage shot deals exactly 10, 5 and 2.5 damage.
5. A fight of 1,000 small cubes against 1,000 runs to the end with the game holding the performance target.

### S6. Unit behaviour

1. In a 20 against 20 fight, a unit changes target only when its target dies or leaves range, or when it switches to an enemy that is attacking it.
2. An idle unit that auto-engages chases at most 8 cells from where it stood, then returns there.
3. With retreat allowed, a group of 10 small cubes meeting 40 pulls back towards its rally point before it loses half its units. Under hold position or a direct attack-target order, it doesn't retreat.
4. A group of 30 on attack-move meets enemies with its units spread over no more than twice the depth it had when it set off.

### S7. Vision and fog of war

1. Each side sees only the cells inside its units' and buildings' vision radius. Enemy units outside them aren't shown and can't be targeted.
2. Unexplored ground is hidden. Explored ground out of sight shows the enemy buildings last seen there, as they were then.
3. An enemy unit entering vision appears within 0.25 s; one leaving vision disappears within 0.25 s.
4. A debug view shows that what each AI side knows about enemy units matches its own fog exactly, with nothing extra.
5. 2,000 units with vision hold the performance target.

### S8. Supply and Collectors

1. A Collector sent to a field fills up, drives to the nearest own Depot, and unloads; the side's Supply goes up by exactly the amount it carried. It repeats this with no further orders.
2. When its field runs dry, the Collector moves to the nearest field its side has seen. If there is none, it returns to its Depot, waits, and the player gets a message.
3. If its Depot is destroyed, the Collector takes its load to the next nearest own Depot.
4. With 10 Collectors on one field and one Depot, no Collector waits more than 10 s in a row to load or unload.

### S9. Base building and power

1. The HQ produces one building at a time. A finished building can be placed only on free open cells inside the build radius. Invalid spots are shown as invalid and refused.
2. Cost is taken as production goes. At 0 Supply production pauses, and it carries on when Supply comes in. Cancelling refunds exactly what was spent.
3. A building is unavailable until its prerequisites exist, and becomes unavailable again if they are all destroyed.
4. When power used is more than power produced, production runs at a set slower rate and powered defences stop firing. Both return to normal as soon as power is enough again.
5. A Field Truck unfolds into an Outpost only on a valid spot, and the Outpost adds build radius and can produce buildings.

### S10. Unit production and the cap

1. Each category (infantry, vehicles) has one queue of up to 15 units. Cost is taken as production goes, as for buildings.
2. Each extra Yard of a category makes that queue produce faster, at a rate set in the economy section.
3. New units drive to the rally point and gather there as a group, following S2's rules.
4. At its unit cap, a side's production pauses with a message, and it carries on as soon as the side is below the cap.

### S11. Match flow, win and loss

1. The setup screen sets the map, each side's faction, the number of AI opponents (1 to 3), teams and difficulty. Each side starts with exactly the items in [What each side starts with](#what-each-side-starts-with).
2. A side with no buildings and no Field Truck is out at once, and its remaining units are removed.
3. The player sees the victory screen when every opponent is out, and the defeat screen as soon as they are out. Sides knocked out at the same moment give a draw.
4. The end screen shows match time, units built, units lost and Supply gathered for each side.

### S12. AI commander, first version

1. Using only player orders and the same starting items, the AI plays a full match against another AI to a result, with no errors.
2. Logs show the AI receives no Supply other than from Collectors, and no knowledge of enemy units outside its fog.
3. On Normal, the AI builds its economy, scouts, and makes its first attack by minute 6.
4. When its base is attacked, the AI sends defenders within 15 s.
5. The AI beats an opponent that does nothing within 20 minutes in 10 out of 10 matches.

## Open questions for the owner

1. **The unit cap.** *Decided for now (owner, Issue #145):* 2,000 mobile units for the whole match, shared equally between the sides, with buildings not counted. A ranked "ladder" would need online play, which belongs to the online multiplayer question in vision.md (open question 5). A higher cap for 6 to 8 sides is question 9.
2. **Number of AI opponents.** *Decided (owner, Issue #145):* 1 to 7 AI opponents (2 to 8 sides), free-for-all or teams. 1 against 1 is built and tuned first; 8-side matches come after S1 to S12.
3. **Faction themes, names and rosters.** For the factions section. *Recommendation:* the same economy for both, with differences in units and at most one signature mechanic each.
4. **Air units, water and height.** *Recommendation:* no naval units and no water in the first release. Decide on air units in the factions section, since they add a second kind of movement. Keep them out of the sandbox.
5. **Power.** Keep power as a second economy rule, or drop it to make things simpler? *Recommendation:* keep it. It adds raid targets and planning for the AI at no per-unit cost.
6. **Superweapons** (one very large attack on a long timer). *Recommendation:* leave them out of the first release and revisit once the loop plays well.
7. **Balance numbers** (costs, build times, health, damage, speeds, starting Supply). For the units and economy sections, tuned in the sandbox.
8. **Start already in place, or start with a Field Truck?** *Recommendation:* start with the HQ already in place. It's faster and the first decision would almost always be "unfold here" anyway.
9. **A higher unit cap for 6 to 8 sides.** With 8 sides each side has only 250 units. Should matches with 6 to 8 sides get a cap above 2,000? That needs a new, higher performance target in vision.md first, because the current one is 2,000 active units. *Recommendation:* decide after movement and combat (S2 to S6) are built and measured against the current target, and raise the cap only if those measurements show room to spare on the reference PC.
10. **Cap shares with uneven teams.** Default: every side gets the same share whatever its team, fixed at match start, and a side that is out passes its share to nobody. The alternative is to split the cap equally per team, then per side within it. *Recommendation:* keep equal shares per side. It is simple to read, and an uneven team is a handicap the player chooses on purpose.
11. **What allies share, and where they start.** Default: shared vision only; allies never attack each other; no shared Supply, units, build radius or Depots; only a side's own units step aside for it; allies start on neighbouring positions. *Recommendation:* keep this for the first build, and revisit shared Depots and allied stepping aside after team matches have been playtested.
12. **Map sizes for 6 and 8 sides.** At the planning distance of 128 cells, a 6-side map only just fits the 87,381-cell limit and an 8-side map (about 385 x 385) is well over it. It would fit only with neighbouring bases at most about 90 cells apart. *Recommendation:* keep the same neighbour distance for every side count, as the owner asked, and make the storage decision (ADR 0011) before the first 8-side map is built, rather than shrinking 8-side maps.
13. **Match length with 6 to 8 sides.** Default targets: the same opening and first fights as 1 against 1, a middle to 20 minutes and an end from 20 to 40 minutes. *Recommendation:* tune these once the AI plays full 8-side matches against itself.
14. **AI teamwork.** Default: each AI Commander plays its own game, with no joint plans. *Recommendation:* none in the first build. Afterwards, add two behaviours in this order: send help to an ally whose base is under attack, and join an ally's attack on the same target. Both are per side, with no per-unit cost.
