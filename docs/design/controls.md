# Controls: mouse and keyboard

> Part of the Game Design Document (GDD), and the single reference for controls. It builds on [vision.md](vision.md) and [core-loop.md](core-loop.md). Every part names the design pillar(s) it serves and its per-unit cost (Pillar 4). Technical choices belong in ADRs (`docs/adr/`).
>
> When any other GDD page names a mouse button or a key, it means that action's binding in the **active controls option** below. The keys named in core-loop.md (arrow keys, Shift, Ctrl+1 to 9) are the defaults; in Custom, the player's own binding applies.

## How to read this section

- **Pillars** as in [vision.md](vision.md): **P1** Fast and readable, **P2** Smarter AI, **P3** Movement that just works, **P4** Armies at scale.
- **Status:** **Now** is built with the grey-box sandbox (core-loop S1, S2, S4 and the checks in this page). **Later** is designed now and built once the named system exists. **Open** is not designed yet and waits for combat (core-loop S5).
- **Pixels** are measured on a 1080-pixel-high screen and scale in proportion to screen height at other resolutions.
- **Own unit:** a mobile unit of the player's side. **On screen:** the unit's centre is inside the visible map view.
- **Group** means a control group (1 to 9), not a squad of the AI.

## Controls options

| Option | What it is | Default |
|---|---|---|
| **Classic** | The left button does everything for units: select, box select and give orders. The right button deselects with a click and pans the camera with a drag | **Yes** |
| **Modern** | The left button selects, the right button gives orders, the middle button pans | No |
| **Custom** | The player's own bindings, starting as a copy of Classic or Modern (see [Custom](#custom)) | No |

The player picks one option in the settings panel. Exactly one is active at a time.

**Pillars:** P1 (players play with the scheme their hands already know). **Per-unit cost:** none.

## Mouse rules (all options)

| Rule | Value | Why |
|---|---|---|
| **Click or drag** | A press is a **click** if the pointer moves **6 pixels or less** from where the button went down before it is released. Once it moves **more than 6 pixels** while held, it is a **drag** until release. How long the button is held doesn't matter | Small enough that a box or a pan starts at once, large enough that a shaky hand still clicks. One rule for every button |
| **When it acts** | A click acts on **release**. A drag acts as soon as it passes 6 pixels | Classic must tell a click from a drag on both buttons before acting. Core-loop S2.1's 0.1 s is measured from the release |
| **Double-click** | A second click of the same button within **0.35 s** of the first, within 6 pixels of it | Close to common desktop settings |
| **Double-tap** | The same key pressed twice within **0.35 s**, with no other key pressed in between | Same window as the double-click, so both feel alike |
| **Edge pan zone** | The pointer within **4 pixels** of a screen edge, with the game window in focus | A thin zone avoids panning by accident while clicking near the edge of the view |
| **Edge pan during a drag** | Off while any drag (box select or drag pan) is in progress | A box drawn near the edge doesn't slide the map away |

**Where a click lands:** over an own unit is **on a unit**. Anywhere else is **on ground**, including enemy units and buildings until combat exists (see [Open until combat](#open-until-combat-s5)).

**Which action a click triggers.** One button may carry several click actions. A click with no modifier held resolves to the first match:
1. On a unit, and **Select** uses this button: select that unit.
2. On ground, own units are selected, and **Move** uses this button: move.
3. **Deselect** uses this button: clear the selection.
4. Otherwise nothing happens.

A click with a modifier held (Shift, Ctrl, Alt) triggers only actions bound with that modifier. For example, in Modern a Shift+left-click on ground does nothing and keeps the selection.

**Pillars:** P1 (every input has one clear meaning). **Per-unit cost:** none. A click tests what is under the pointer once.

## Default bindings

| Action | Classic | Modern | Status |
|---|---|---|---|
| Select one own unit | Left-click the unit | Left-click the unit | Now |
| Box select | Left-drag | Left-drag | Now |
| Select all own units of that type on screen | Double-left-click a unit | Double-left-click a unit | Now |
| Add or remove one unit | Shift+left-click the unit | Shift+left-click the unit | Now |
| Add the units in a box | Shift+left-drag | Shift+left-drag | Now |
| Add all of that type on screen | Shift+double-left-click a unit | Shift+double-left-click a unit | Now |
| Move | Left-click ground | Right-click ground | Now |
| Deselect all | Right-click (anywhere), or Esc | Left-click empty ground, or Esc | Now |
| Drag pan | Hold right button and drag | Hold middle button and drag | Now |
| Key pan | Arrow keys | Arrow keys | Now |
| Edge pan | Pointer at a screen edge | Pointer at a screen edge | Now |
| Zoom in and out | Scroll wheel (away from you zooms in) | Scroll wheel (away from you zooms in) | Now |
| Stop | S | S | Now (core-loop S4.1) |
| Hold position | H | H | Now (core-loop S4.2) |
| Save group | Ctrl+1 to 9 | Ctrl+1 to 9 | Now |
| Select group (double-tap centres the camera) | 1 to 9 | 1 to 9 | Now |
| Add the selection to a group | Shift+1 to 9 | Shift+1 to 9 | Now |
| Centre on the next group marker (see [Cycle key](#cycle-key)) | Space | Space | Later: after movement Issue 2 (units arriving) |
| Same type on screen (double-tap: whole map) | T | T | Now |
| Combat units on screen (double-tap: whole map) | Q | Q | Now |
| Cycle unit types in the selection | Tab | Tab | Later |
| Filters: aircraft, ground vehicles, infantry, support | No default key | No default key | Later |
| Smart group on or off | Set per group, no default key | Set per group, no default key | Later |
| Queue an order | Shift + the order | Shift + the order | Later (core-loop S4.3) |
| Menu and pause | **F10** | **F10** | Now. Fixed in every option |

Notes on the bindings:
- **Classic with nothing selected:** a left-click on ground does nothing. No marker appears.
- **Classic right button:** a click (6 pixels or less) clears the selection and leaves the camera still. A drag pans and leaves the selection unchanged.
- **Modern right button:** a right-drag gives no order and does nothing.
- **Stop and Hold position** act on the selection. With nothing selected, they do nothing.
- **Esc** deselects all in both Classic and Modern (owner decision, Issue #132).
- **F10** opens the game menu and pauses the match until the menu closes. It can't be rebound, and no other action can use it.

### Control groups

- **Save (Ctrl+N):** group N becomes exactly the current selection, replacing its old members. With nothing selected, Ctrl+N does nothing and group N keeps its members.
- **Select (N):** the selection becomes group N. If group N is empty, nothing changes.
- **Double-tap N:** selects group N and centres the camera on the middle of its units.
- **Add (Shift+N):** adds the selected units to group N. The selection doesn't change.
- A unit can be in several groups. Destroyed units leave every group (core-loop S1.4).

**Pillars:** P1 (a big army takes few keys). **Per-unit cost:** none per frame. A destroyed unit leaves its groups once.

## Selection extras

| Feature | Rule | Status |
|---|---|---|
| **Same type (T)** | The selection becomes every own unit on screen whose type is one of the types in the current selection. Double-tap T does the same over the whole map. With nothing selected, nothing happens | Now |
| **Add to group (Shift+1 to 9)** | See [Control groups](#control-groups) | Now |
| **Combat units (Q)** | The selection becomes every own combat unit on screen. Double-tap Q does the same over the whole map. Collectors, Field Trucks and units marked as support in the units section are never included | Now |
| **No size limit** | A selection or a group can hold any number of units, up to the side's whole army (the unit cap in core-loop) | Now |
| **Cycle types (Tab)** | In a mixed selection, Tab makes the next unit type the active one, which picks whose own commands the command panel shows. The whole selection still takes move, stop and hold orders | Later: waits for the command panel and type-specific commands (units section) |
| **Filters** | Aircraft, ground vehicles, infantry, support. Each one keeps only the units of that category in the current selection. No default keys; players bind them in Custom | Later: waits for unit categories in the units section. Aircraft also wait for core-loop open question 4 |
| **Smart groups** | Off by default, set per group. When a group's smart setting is on, every newly produced own unit whose type is already in that group joins it | Later: waits for unit production (core-loop S10) and an on-screen group display to set it from |

**Pillars:** P1 (large armies stay quick to command); P4 (selection works at the full 2,000-unit scale). **Per-unit cost:** see [Per-unit costs](#per-unit-costs).

## Queued orders (later)

Holding Shift with an order adds it to the end of each selected unit's queue instead of replacing the queue. Up to 16 waypoints, shown while the units are selected (core-loop S4.3 and S4.4). Until S4.3 is built, Shift + an order on ground gives the plain order.

**Pillars:** P1, P3. **Per-unit cost:** showing the queued route. Units given the same queue share one drawn route.

## Order markers

A move order on ground shows a **marker** at the target point (core-loop S2.1). There are two kinds: the **plain marker** and the **numbered marker** of a control group. Both use the player's marker colour and shape. Off-screen numbered markers get **edge arrows**, and the **cycle key** jumps the camera to them.

| Part | Status |
|---|---|
| [Marker settings](#marker-settings) (colour and shape) | Now: built with the settings panel |
| [Plain marker](#plain-marker) | Now |
| [Numbered markers](#numbered-markers) | Later: after movement Issue 2 (units arriving) |
| [Cycle key](#cycle-key) | Later: after movement Issue 2 (units arriving) |
| [Edge arrows](#edge-arrows) | Later: after movement Issue 2 (units arriving) |

Until numbered markers are built, every move order shows a plain marker, including orders given to a recalled group.

### Marker settings

| Setting | Choices | Default | Effect |
|---|---|---|---|
| Marker colour | **Green**, Magenta, Cyan, White | Green | Colour of every marker, its group number and the edge arrows. Each choice stands out against the grey ground and the blue, red and yellow team colours |
| Marker shape | **Pulsing ring**, pulsing square, none | Pulsing ring | Ring and square are both **1 cell across**, never smaller than **24 pixels** across at the farthest zoom, and pulse the same way. The square's edges run along the map's cell lines. **None** draws no marker of either kind (see [With shape "none"](#with-shape-none)) |

- Both settings sit on the **Controls** page of the settings panel, apply at once (also to markers already shown) and are saved between sessions like the other settings.
- Reset to Classic and Reset to Modern don't change them.
- Core-loop S2.1's "shows a marker there" means this marker. With shape **none**, only the movement part of S2.1 applies.

**Pillars:** P1 (the player picks a marker they can always see). **Per-unit cost:** none.

### Counted units

- Every marker, plain or numbered, counts the units that took its order. A unit leaves the count when it **arrives**, is **destroyed**, or is given **any other order** (a move, Stop or Hold position).
- "Arrives" means the same for both kinds of marker (see [open question 21](#open-questions-for-the-owner)).

### Plain marker

- A move order that isn't a [numbered order](#numbered-markers) shows a plain marker at its target point.
- **One at a time.** The next move order of any kind removes it. If that order is plain, its own plain marker replaces it.
- **Other end.** It also goes away when its [count](#counted-units) reaches zero: none of the units of its order is still going there, because each one arrived, was destroyed, or was given Stop or Hold position.
- No marker appears when nothing moves (for example Classic, left-click on ground with nothing selected).

**Pillars:** P1 (every order gets instant feedback). **Per-unit cost:** none per frame. A unit of the order that arrives, is destroyed or is stopped updates the marker once.

### Numbered markers

- **Numbered order.** A move order is numbered when the selection is exactly the units of control group N: recalled with its number key, or selected any other way so it holds exactly that group's units and no others. It shows a numbered marker: the marker with the number **N** in its middle.
- **Readable.** The number is in the marker colour, at least **16 pixels** tall at every zoom, doesn't pulse, and is drawn over units and ground.
- **Counted units.** The marker counts the units that took the order, as in [Counted units](#counted-units).
- **End.** The marker stays until its count reaches zero: every surviving unit has arrived, or the group got a new order. It also ends at once when group N is saved again with Ctrl+N.
- **One per group, nine at most.** A new numbered order for group N replaces group N's marker. Up to 9 numbered markers can show at once, plus the one plain marker.
- **Two groups, one unit.** A unit counts only for the marker of its latest order. If a unit of groups 1 and 2 is sent with group 2, it leaves marker 1's count.
- **Adding to a group** with Shift+N doesn't add the new units to an active marker N.
- A plain move order doesn't remove numbered markers. Its units leave the count of any numbered marker they were in.

**Pillars:** P1 (several groups' destinations stay readable at once); P3 (the player sees when a group has arrived). **Per-unit cost:** none per frame. One check per move order against the nine groups; one count update when a unit arrives, is destroyed or gets a new order.

### Cycle key

- **Space**, in Classic and Modern. In Custom it can be remapped like any key command. No other action uses Space in Classic or Modern.
- Each press centres the camera on the next active numbered marker, in number order: the lowest first, then upward, then back to the lowest. Markers that have ended are skipped. With one active marker, each press centres on it again.
- With no active numbered marker, Space does nothing.
- Space moves only the camera. The selection and the zoom stay as they are. Near a map edge, the camera centres as close as the map allows (core-loop S1.1).
- Holding Space counts as one press.
- **With double-tap.** Double-tapping a group key still centres on that group's units, not its marker. It also sets the cycle position to that group, so the next Space goes to the next higher active marker.
- **Existing Custom bindings.** A player who already has Custom gets Space for this action, or no binding if Space is already used (the game updates rule in [Custom](#custom)).

**Pillars:** P1 (one key finds every group on the move). **Per-unit cost:** none.

### Edge arrows

- For each numbered marker whose centre is outside the visible map view, a small arrow shows at the screen edge, pointing toward the marker, with the group number beside it.
- **Placement.** The arrow sits where a line from the screen centre to the marker meets the screen edge, pulled in so the arrow and its number are fully on screen, within **48 pixels** of the edge. The arrow is **24 pixels** long and its number at least **16 pixels** tall, both in the marker colour, with no pulse.
- **No overlap.** When two arrows would overlap, the one with the higher number moves along the edge until it is clear.
- An arrow goes away on the frame its marker's centre enters the view, or when its marker ends.
- The plain marker never gets an arrow.
- Arrows can't be clicked. A click on an arrow acts as a click on the map under it, and the edge pan zone still works there.

**Pillars:** P1 (the player knows where every group is headed without panning). **Per-unit cost:** none. At most 9 arrows, whatever the army size.

### With shape "none"

No marker of either kind is drawn. Numbered markers still exist and count their units, so **edge arrows still show** (in the chosen colour) and **Space still works**. The arrow disappears when the marker's point comes into view, even though no marker is drawn there.

## Camera settings (all options)

| Setting | Range | Default | Effect |
|---|---|---|---|
| Edge panning | On or off | On | Off: the pointer at the edge doesn't pan. Key pan and drag pan still work |
| Pan speed | 25% to 200%, steps of 5% | 100% | Key pan and edge pan speed. At 100% the view moves **one screen width per second**, at any zoom |
| Zoom speed | 25% to 200%, steps of 5% | 100% | At 100%, one wheel notch moves **10%** of the way between the nearest and farthest zoom. Zoom centres on the middle of the screen |
| Invert drag pan | On or off | Off | Off: the ground follows the pointer (drag right and the map moves right). On: the view moves the way you drag (drag right and the map moves left). Drag pan is always one to one with the pointer; pan speed doesn't change it |

**Pillars:** P1. **Per-unit cost:** none.

## Settings panel

- Opened from the F10 menu, in a match or from the main menu. The match stays paused while it is open.
- One **Controls** page shows: the option picker (Classic, Modern, Custom), the full binding list, the camera settings, and the [marker settings](#marker-settings).
- For Classic and Modern the binding list is read-only. For Custom it is editable.
- A change applies and is saved as soon as it is made. There is no Apply button.

### Saved between sessions

- The chosen option, the Custom bindings, the camera settings and the marker settings are kept between sessions.
- The Custom bindings are kept while Classic or Modern is active, so switching back to Custom restores them.
- If the saved settings are missing or can't be read, the game starts with Classic, default camera settings and the default marker (green pulsing ring), and shows a one-line message saying so.

**Pillars:** P1. **Per-unit cost:** none.

## Custom

- **Starting point.** The first time the player picks Custom, the panel asks which option to copy: Classic or Modern. Custom then starts as an exact copy of it.
- **What can be remapped:** every action in [Default bindings](#default-bindings) except **F10**:
  - **Mouse buttons:** which button (left, right, middle, side buttons 4 and 5) does select and box select, move, deselect and drag pan. Double-click and Shift+click follow the select button.
  - **Modifier keys:** which of Shift, Ctrl or Alt does each modifier job: add or remove from the selection, queue an order, save a group, add to a group.
  - **Camera controls:** the four key pan directions, drag pan, and zoom in and out (the scroll wheel or any keys).
  - **Commands:** each key command individually, including each of the nine group keys, each filter and the marker cycle key. Any key, with or without Shift, Ctrl or Alt. Double-tap follows its key.
- **Capture.** The player picks an action and presses the new input. Every key except F10 can be captured, including Esc. A Cancel button leaves the binding unchanged. Pressing F10 shows "F10 is reserved for the menu" and nothing changes.
- **Conflicts.** Two actions conflict when they use the same input: the same key or button, with the same modifiers and the same gesture (press, click, double-click, drag). The exceptions are the click actions of [Mouse rules](#mouse-rules-all-options): Select may share a button with Move or with Deselect. Move and Deselect may not share one. The save-group and add-to-group modifiers must differ.
  - On a conflict, a warning names the other action and offers **Swap** (the other action takes this action's old input) or **Cancel** (nothing changes).
  - Every action that has a binding in Classic or Modern must keep one. If a swap would leave such an action with no binding, only Cancel is offered.
- **Reset to Classic** and **Reset to Modern** set every Custom binding to that preset after one confirmation. They don't change the camera settings.
- **Game updates.** A command added in a later version starts in Custom with its Classic binding, or unbound if that input is already in use; the panel says so once.

**Pillars:** P1. **Per-unit cost:** none.

## Open until combat (S5)

These aren't designed yet. They are settled together with combat (core-loop S5), so their bindings fit the attack rules:
- **Attack a target** and how it is given in each option.
- **Attack-move.**
- **Damaged-units filter.**
- **Clicking an enemy unit or building.** Until then, a click on one counts as a click on ground. Enemy units are never selected.

## Per-unit costs

| Feature | Cost for every unit | How we keep it down |
|---|---|---|
| Selection markers | Each selected unit shows a marker every frame | Must hold core-loop S1.5 (2,000 selected, 33 ms) |
| Double-click, T and Q | One check of each own unit **per key press**, nothing per frame | Check C4.5 below |
| Groups | Nothing per frame. A destroyed unit leaves its groups once | — |
| Smart groups (later) | One check against nine groups **per produced unit** | Nothing per frame |
| Queued routes (later) | A drawn route for selected units with a queue | Units with the same queue share one route |
| Order markers | None per frame. At most 10 markers (9 numbered, 1 plain) and 9 edge arrows are drawn, whatever the army size | Check C9.4 below |
| Numbered order check (later) | Each move order compares the selection with the nine groups once, nothing per frame | Only a group with as many units as the selection needs a closer look |
| Marker counts | One update when a unit of an order arrives, is destroyed or gets a new order, nothing per frame | Check C9.4 below |
| Options, camera, settings, cycle key | None | — |

## Checks

Each check uses the grey-box sandbox (core-loop) with its spawn tool and on-screen frame counter. "Cubes" are the sandbox test units. Unless a check says otherwise, the marker settings are the defaults (green pulsing ring).

### C1. Classic and Modern (Now)

1. **Classic, left button.** With 5 own small cubes selected: a left press and release on ground that moves the pointer 6 pixels gives a move order with a marker. A left press that moves 7 pixels or more draws a box, selects the own units inside it, and gives no move order. With nothing selected, a left-click on ground shows no marker and nothing moves.
2. **Classic, right button and Esc.** With units selected: a right press and release that moves the pointer 6 pixels or less clears the selection, and the camera doesn't move. A right press that moves more than 6 pixels pans the camera, and after release the selection is unchanged. With units selected, Esc clears the selection, and the camera doesn't move.
3. **Modern.** A right-click on ground moves the selected units with a marker. A left-click on empty ground clears the selection, and so does Esc. A middle-drag pans. A right-drag of more than 6 pixels gives no order.
4. **Double-click (both options).** With 10 own small cubes and 3 own large cubes on screen, 5 own small cubes off screen and enemy small cubes on screen: a double-left-click on an own small cube selects exactly the 10 on-screen own small cubes. Two clicks 0.4 s apart act as two single clicks.
5. **F10.** In Classic, Modern and Custom, F10 opens the menu and the match is paused: no unit moves while it is open.

### C2. Settings panel (Now)

1. **Defaults.** On a first start with no saved settings: Classic is active, edge panning is on, pan speed and zoom speed are 100%, and invert drag pan is off.
2. **Edge pan off.** With edge panning off, the pointer resting at the left edge for 5 s doesn't move the camera, and the arrow keys still pan.
3. **Speeds.** Holding an arrow key for 1 s moves the view one screen width at 100% and two screen widths at 200%, within 5%. At 100% zoom speed, ten wheel notches go from the farthest zoom to the nearest.
4. **Invert drag pan.** Off: a drag pan of 100 pixels to the right moves the ground under the pointer 100 pixels to the right. On: the same drag moves it 100 pixels to the left.
5. **Saving.** Pick Modern, set pan speed 150% and edge panning off, quit and restart: all three are kept. With the saved settings made unreadable, the game starts with Classic and default camera settings, shows the message, and has no errors.

### C3. Custom remapping (Now)

1. **Copy.** Picking Custom and copying Modern gives a binding list identical to Modern, entry by entry. The same holds for Classic.
2. **Rebind and keep.** Binding Stop to X: X stops the selected units and S does nothing. After a restart, X is still Stop.
3. **Conflict and swap.** Binding Stop to H shows a warning that names Hold position. Swap gives Stop = H and Hold position = S. Cancel leaves both unchanged.
4. **Mouse and modifiers.** From a copy of Classic: setting drag pan to the middle button makes a middle-drag pan, and a right-click still clears the selection. Setting the add-or-remove modifier to Alt makes Alt+left-click toggle a unit, and Shift+left-click on a unit then no longer toggles it.
5. **Fixed and reset.** F10 can't be captured for any action, and the menu entry can't be edited. Reset to Classic and Reset to Modern each make every binding equal that preset, and the camera settings don't change.

### C4. Selection extras (Now)

1. **T.** With one own small cube selected, 10 own small cubes on screen (including it), 5 off screen, 3 own large cubes on screen and enemy small cubes on screen: T selects exactly the 10. A double-tap of T selects exactly the 15 own small cubes.
2. **Shift+number.** With group 3 holding 4 units and 2 other units selected: Shift+3 leaves the selection as it was; pressing 3 then selects all 6. A unit added to group 3 that is also in group 4 stays in group 4.
3. **Q.** With 10 own small cubes and 2 Collectors on screen (before core-loop S8 exists, 2 cubes marked as support), and 5 own small cubes off screen: Q selects exactly the 10 cubes. A double-tap of Q selects exactly the 15 cubes and never a Collector.
4. **No limit.** Box-selecting 2,000 own units selects all 2,000. Ctrl+1, then clearing the selection with the active option's deselect input, then 1, selects all 2,000 again.
5. **Speed.** With 2,000 own small cubes on the map: a double-tap of T (with one cube selected) and a double-tap of Q each select all 2,000 on the next frame, and that frame takes no longer than **33 ms**. Q with as many of them on screen as fit at the farthest zoom meets the same limit.

### C5. Marker settings (Now, with the settings panel)

1. **Defaults.** On a first start with no saved settings, a move order shows a green pulsing ring 1 cell across. The colour list offers exactly Green, Magenta, Cyan and White, and the shape list exactly pulsing ring, pulsing square and none.
2. **Square and colour.** With Magenta and pulsing square chosen, the next move order shows a magenta pulsing square 1 cell across, its edges along the cell lines. At the farthest zoom, the ring and the square are each at least 24 pixels across.
3. **Change at once.** Changing the colour or the shape while a marker is shown changes that marker at once, with no new order.
4. **None.** With shape none, a move order shows no marker, and every selected unit still starts moving within 0.1 s (core-loop S2.1).
5. **Saving.** Pick Cyan and pulsing square, quit and restart: both are kept. Reset to Classic leaves them unchanged. With the saved settings made unreadable, the game starts with a green pulsing ring, shows the message and has no errors.

### C6. Plain marker (Now)

1. **One at a time.** With 5 own small cubes selected, two move orders to points 10 cells apart: after the second order, only the marker at the second point shows.
2. **Goes on arrival.** 5 cubes sent 20 cells away: the marker shows while any of them is still on the way, and goes away on the frame the last one arrives.
3. **No units left to go.** 5 cubes sent 20 cells: Stop with 3 of them selected leaves the marker; Hold position with the other 2 selected removes it. In a second run, destroying all 5 on the way removes the marker on the frame the last one is destroyed.

### C7. Numbered markers (Later: after movement Issue 2)

1. **Number shown.** Group 3 holds 10 own small cubes. Pressing 3 and giving a move order shows a marker with "3" in its middle, at least 16 pixels tall at both the nearest and the farthest zoom. Box-selecting exactly those 10 and ordering also shows "3". Removing one of them with Shift+click and then ordering shows a plain marker.
2. **Arrival and losses.** Group 3 (10 cubes) sent 30 cells: the marker shows while any unit is still on the way. With 2 of them destroyed on the way, the marker goes away on the frame the 8th surviving unit arrives. In a second run, destroying all 10 removes it.
3. **New orders and re-saving.** While marker 3 shows: a new move with group 3 leaves exactly one marker 3, at the new point. Stop with group 3 selected removes marker 3. A plain move of 4 of the 10 leaves marker 3 showing until the other 6 arrive. Ctrl+3 with any selection removes marker 3 at once.
4. **Shared unit.** A unit is in groups 1 and 2. Group 1 is sent east, then group 2 is sent west: the shared unit goes west, and marker 1 goes away when the other units of group 1 arrive.
5. **Nine plus one.** Groups 1 to 9, each sent with its number key to its own point, show 9 numbered markers. A plain order then adds a 10th marker. The next plain order replaces only the plain marker, and all 9 numbered markers stay. With shape none, the same steps draw no marker.

### C8. Cycle key (Later: after movement Issue 2)

1. **Order and wrap.** With markers 2, 5 and 7 active and the camera elsewhere, four presses of Space centre the camera on 2, 5, 7 and 2 in turn, each within 1 cell of the screen centre (or as close as the map edge allows). The zoom and the selection don't change.
2. **Nothing active.** With no numbered marker active, Space leaves the camera where it is, with no errors. Holding Space for 2 s with markers 2 and 5 active moves the camera once.
3. **Ended markers skipped.** After Space centres on 5, marker 5 ends; the next Space centres on 7.
4. **With double-tap.** With markers 2, 5 and 7 active, a double-tap of 5 centres on group 5's units, and the next Space centres on marker 7.
5. **Binding.** No other action uses Space in Classic or Modern. In Custom, binding the cycle action to X makes X cycle and Space do nothing. Binding Stop to Space then shows a warning that names the cycle action.

### C9. Edge arrows (Later: after movement Issue 2)

1. **One arrow.** Marker 4 lies off screen to the right: one arrow shows at the right edge, pointing toward it, with "4" beside it, fully on screen and within 48 pixels of the edge. Panning until the marker's centre is in view removes the arrow on that frame.
2. **Plain marker.** A plain marker off screen gets no arrow.
3. **No overlap.** Markers 1 and 2 lie off screen in nearly the same direction: two arrows show, and they don't overlap. A click on an arrow acts on the map under it.
4. **Scale.** 2,000 own small cubes in groups 1 to 8 (222 each) and group 9 (224). Each group is sent with its number key to its own point, all chosen so they are off screen. While the units move, 9 numbered markers are active, and before Space is pressed 9 arrows show with numbers 1 to 9 and none overlap. For 30 s, including 9 presses of Space, no frame takes longer than **33 ms** (core-loop S1.5), and the Profiler shows no memory allocated per frame by markers, arrows or marker counts.
5. **Shape none.** With shape none and marker 6 off screen, its arrow shows in the chosen colour, and Space still centres on marker 6.

## Open questions for the owner

Choices this page makes where the owner's instructions left the detail open. Each one is easy to change.

1. **Click or drag:** 6 pixels (at 1080p) separates a click from a drag, on every button.
2. **Clicks act on release,** so Classic can tell a click from a drag. Core-loop S2.1's 0.1 s counts from the release.
3. **Double-click and double-tap window:** 0.35 s.
4. **Classic, left-click on ground with nothing selected:** does nothing.
5. **Shift+click on ground** (Classic left, Modern right): a queued move once core-loop S4.3 exists, a plain move until then. Shift+left-click on ground in Modern does nothing.
6. **Esc in Classic:** *settled by the owner (Issue #132):* Esc deselects all in Classic too, as in Modern.
7. **Edge pan zone:** 4 pixels, and off during any drag.
8. **Pan speed:** 100% moves one screen width per second; slider 25% to 200%.
9. **Zoom speed:** 100% is 10% of the zoom range per wheel notch; slider 25% to 200%; zoom centres on the middle of the screen, not the pointer.
10. **Drag pan:** "grab the ground" by default, one to one, not affected by pan speed. Invert makes the view move the way you drag.
11. **Ctrl+number with nothing selected:** does nothing, so a group can't be wiped by accident.
12. **Tab:** changes the active type inside the selection; it doesn't select other units.
13. **Smart groups:** new units join a smart group when their type is already in it.
14. **Enemy units until combat:** count as ground for clicks and are never selected.
15. **Settings:** changes apply and save at once (no Apply button); the Reset buttons don't touch camera settings.
16. **Marker colours:** Green (default), Magenta, Cyan, White. Recommendation: keep these, and check them again when real team colours and terrain exist; Cyan is the nearest to blue, and Green would clash if a third or fourth side gets a green team colour.
17. **Square marker:** same size and pulse as the ring (1 cell, at least 24 pixels), edges along the cell lines. Recommendation: keep, so switching shape never changes how big a marker looks.
18. **Marker settings and resets:** they apply at once, also to markers already shown, and Reset to Classic or Modern doesn't change them. Recommendation: keep, as with the camera settings.
19. **Plain marker, extra end:** *settled by the owner (Issue #155):* it also goes away when none of the units of its order is still going there: each one arrived, was destroyed or was given Stop or Hold position, the same as numbered markers.
20. **Selection matching two groups:** if the selection is exactly the units of more than one group, the order uses the group recalled by its key (if unchanged), otherwise the lowest number. Recommendation: keep.
21. **What "arrived" means** (plain and numbered markers alike): a unit has arrived when it has stopped at the end of its move order, as movement Issue 2 decides its final spot; a unit sent where it can't reach counts as arrived when it stops at the nearest reachable point (core-loop S3.2). The marker goes on the frame the last counted unit arrives. Recommendation: keep.
22. **Who counts:** a unit leaves a numbered marker's count when it arrives, is destroyed or gets any other order. So a unit in two groups counts only for its latest order, and units added with Shift+N don't join an active marker. Recommendation: keep.
23. **Ctrl+N re-save:** ends group N's marker at once, because the number no longer means those units. Recommendation: keep.
24. **Number placement:** in the middle of the marker, in the marker colour, at least 16 pixels tall, not pulsing, drawn over units. Recommendation: keep; it reads at every zoom without adding a second shape.
25. **Cycle order:** lowest number first, then upward, wrapping, skipping ended markers; camera only, zoom and selection unchanged; holding Space counts as one press. Recommendation: keep.
26. **Cycle and double-tap:** a double-tap of N still centres on group N's units (not its marker) and sets the cycle position to N. Recommendation: keep, so Space carries on from the group the player just looked at.
27. **Shape "none":** edge arrows still show and Space still works. Recommendation: keep, so a player who hides markers on the ground can still find their groups. The alternative is to hide arrows too.
28. **Edge arrows:** 24 pixels long, number at least 16 pixels tall, on the line from the screen centre to the marker, within 48 pixels of the edge, in the marker colour, never overlapping, not clickable, and none for the plain marker. Recommendation: keep; clicking an arrow to jump there could come later.
