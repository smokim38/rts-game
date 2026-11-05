# ADR 0016: The performance ladder draws the game's current unit mesh

## Status

Accepted

## Date

2026-11-05

## Context

[ADR 0004's Test scene](0004-unit-simulation-architecture.md#performance-ladder) says "units are Unity's built-in cube". PR #228 (Issue #224) makes the game draw a 36-triangle tank instead of the 12-triangle cube. `PerformanceLadder.unity` instantiates `MatchDriver.prefab`, which holds the mesh, with no override, so the ladder now draws the tank too. The verdict record on PR #228 at commit 0500bd5c1e23c7cd759bb01291770f8d932993a3 escalated this: ADR 0004 and the ladder README say cube, yet the PR keeps scenario version 1 and compares with a cube entry.

The owner has settled the direction (Producer session, 2026-11-05, Issue #229): the ladder measures what the game really draws. Two questions remain: what a mesh change does to the comparison series, and whether the tank entry can stay in scenario version 1.

Two facts decide them:
- **A new version turns the regression check off for its first entry.** `newestBaseline` in `tools/unit-ladder.js` keeps only entries with the same `scenarioVersion` and hardware. With no match, `before` is undefined, so `regressed` and `previouslyPassing` are false at every step (lines 70 and 73), and the command can't fail on them (line 243).
- **The mesh isn't in the scenario.** ADR 0004 says every scenario parameter lives in one ScriptableObject, and `LadderScenario` has no mesh field. ADR 0004's "When to re-run" item 2 lists "unit drawing" among the changes whose "before" is the newest `main` entry "on the same hardware and scenario version". Only the README ties cubes to a version.

## Decision

**Ladder units use the unit mesh the game currently draws. A change of unit mesh stays in the comparison series, with a recorded before and after. The tank entry stays in scenario version 1.**

### What this supersedes

Only the words "units are Unity's built-in cube" in ADR 0004's Test scene. The line now reads:

> units use the unit mesh the game currently draws, with one prebuilt URP material per faction and GPU instancing on; no art, textures, animation or audio

The rest of that line and of ADR 0004 stands, including the rule that any scenario parameter change bumps the version. "No art" doesn't exclude the unit mesh, because this line now requires it. It still excludes textures, animation and audio. ADR 0004's other mentions of cubes (its context and its draw-count example) are history, not rules. ADR 0004 itself isn't edited (AGENTS.md §9).

### Rules for a unit mesh change

1. A unit mesh isn't a scenario parameter. Don't bump `LadderScenario.Version` for it, and don't add a mesh field to `LadderScenario`.
2. The ladder takes its mesh from `MatchDriver.prefab`, as the game does. No separate ladder mesh, and no override in `PerformanceLadder.unity` or `PerformanceLadderBuilder`.
3. The PR carries its ladder entry like any unit-drawing PR (ADR 0004). Its context is `re-run: Issue <n> <mesh name>`, so the entry folder shows where the mesh changed, and the PR states the old and new triangle counts.
4. The pass and regression rules apply unchanged (ADR 0004, [ADR 0009](0009-slow-frame-regression-cutoff.md)), including fix-or-explain and escalating a designed cost to the owner.
5. Don't combine a unit mesh change with a scenario change in one PR. The version bump would remove the mesh's "before".

### The tank entry

`docs/performance/unit-ladder/2026-11-05-7392d45.md` (tank, on the #224 branch) stays in scenario version 1 as the before and after bridge from [`2026-11-05-1106593.md`](../performance/unit-ladder/2026-11-05-1106593.md) (cube). Reasons:
- Nothing in the scenario changed. `Game/Assets/Data/PerformanceLadder.asset` says `Version: 1` on both sides.
- The entries match on seed, tick rate, build fields and the full hardware summary, and were measured the same day. The harness chose the comparison itself.
- Moving the tank to a new version would mean a new ladder and would remove the only before and after. Entries are never edited (ADR 0004).

Entries up to and including `1106593` drew the cube and don't say so, so this ADR records that boundary. If PR #228 is measured again before it merges, the later entry takes this place on the same terms.

### What the cube-to-tank comparison shows

Average frame time, using the summaries' unrounded medians:

| Units | Cube ms | Tank ms | Change |
|---|---|---|---|
| 10 | 0.763 | 0.733 | -0.030 ms (-3.9%) |
| 500 | 0.770 | 0.757 | -0.013 ms (-1.6%) |
| 1,000 | 0.794 | 0.781 | -0.014 ms (-1.7%) |
| 2,000 | 0.840 | 0.844 | +0.004 ms (+0.4%) |

**It can show:**
- No step fails or regresses, and the ladder height stays 2,000.
- No frame-time cost that stands out from noise. The six newest cube entries (`6e8659f` to `1106593`, different commits, none naming a unit-drawing change; the level stepped up at `6e8659f`, so older ones are left out) range over 0.835 to 0.845 ms at 2,000 units, and the tank is inside that. They range over 0.028 ms at 10 units, where the mesh can hardly matter, yet the tank is 0.030 ms faster there. So one pair of ladders resolves about 0.03 ms.
- The main thread didn't grow: 0.334 to 0.321 ms at 2,000 units. The mesh doesn't change the instance count or the draw chunks.

**It can't show:**
- A small per-unit cost. Growth from 10 to 2,000 units is 0.077 ms for the cube entry and 0.111 ms for the tank, against 0.077 to 0.100 ms across those six entries. That is a hint of at most about 0.03 ms, which one pair can't confirm. The regression flag can't see it either: a step at 0.84 ms is flagged only above 1.34 ms.
- Shading cost at play zoom. The ladder camera (`CameraSize` 200) gives 2.7 pixels per world unit at 1080p, so a small unit is about 2 pixels across and a large one about 4 (arithmetic). The added work is nearly all vertices.
- Moving units. Both entries say units hold position.
- The vision target. The ladder PC is `reference-class: no`: its GPU matches, its CPU is stronger.

### How much the missing GPU figure leaves unknown

`gpuMs` is `null` and `missingGpu` equals the frame count at every step of all ten entries on `main` and of the tank entry. The Issue #97 speed test found the same with Graphics Jobs on and off. Unity's [FrameTimingManager page][ftm] lists Windows DirectX 12 as supported, so the cause isn't established. Issue #224 records the owner's answer that frame time is enough for that PR. This ADR proposes no fix.

- **Not affected:** pass or fail, ladder height and regression flags. They use only `cpuFrameTime`, which "includes all waiting time and overheads" ([FrameTiming][frame-timing]), and ADR 0004 says missing GPU samples don't invalidate a run. A GPU limit makes the render thread wait to present ([Profiler markers][markers], `Gfx.WaitForPresentOnGfxThread`), so it shows in frame time.
- **Bounded:** the CPU side can only get a few frames ahead, so average GPU time per frame can't stay above the frame time for long. At 2,000 tanks that puts it at about 0.84 ms or less on the RTX 3050, the reference GPU, which is about 5% of the 16.67 ms budget. This is inference, not a measurement, and it covers the average only.
- **Unknown:**
  - The real GPU time, anywhere from near zero up to that bound. So the comparison can't say whether the tank costs the GPU more than the cube, even though it triples the triangles (72,000 against 24,000 per pass at 2,000 units).
  - Whether a step is limited by the CPU or the GPU. The main thread accounts for 0.321 of the 0.844 ms. The other 0.523 ms may be render-thread work, presenting or waiting for the GPU, and an entry can't split them.
  - How much GPU margin is left. Extra GPU work that still finishes within the frame interval doesn't make the CPU wait (inference), so frame time can stay flat while the margin shrinks. A later mesh could use up that margin with no flag. A failing step can't hide, but the warning can come late.

So today's gap is small in absolute terms, and it grows with the frame time, because the bound is only as tight as the frame is short.

## Alternatives considered

None of these changes code that runs per frame, so CPU, GPU, memory and allocations at thousands of units are the same under all three. They differ in what the ladder catches.

**A. Stay in the series (chosen).**
- *How it works:* as above. The next entry compares with the newest `main` entry in the version, whichever mesh it drew. After PR #228 merges, that is the tank entry.
- *Evidence:* the cube-to-tank pair ran this way with no harness change. The regression check stays on for the PR that adds the cost.
- *Complexity:* the lowest: one README edit and no code.
- *Risk and reversal:* one curve spans several meshes, so a step in the history can be a mesh rather than code. The entry's context and this ADR mark it. It is easy to reverse: a later ADR can start a new series at any point, and entries keep their medians.

**B. A new scenario version for each mesh change.**
- *How it works:* each mesh change bumps `LadderScenario.Version`, so each series has one mesh.
- *Evidence:* it is the reading the README invites and the review raised, and every series would be like for like. But each series' first entry says "none (new series)" and carries no flags (Context). So the PR that adds a mesh's cost is the one PR the regression check can't see.
- *Complexity:* a version bump in every mesh PR, plus a before and after compared by hand, which the harness doesn't do.
- *Risk and reversal:* the highest risk. Heavier meshes are where the ladder matters most (Pillar 4), and the check would be off exactly there. A 5,000-triangle mesh would be 10 million triangles per pass at 2,000 units, with no flag unless a reader compares by hand. Bumps can't be undone, because entries are never edited.

**C. A cube control ladder beside the real one.**
- *How it works:* every drawing PR runs two ladders, one with the cube and one with the game's mesh. The gap between them is the mesh's cost, free of code changes.
- *Evidence:* it would give the cleanest mesh cost, but it still has no GPU figure. It measures a cube the game no longer draws, against the owner's direction.
- *Complexity:* about 35 more minutes of the one ladder PC per PR (ADR 0004), and a mesh switch the game doesn't have. That is a harness change, which starts a new series.
- *Risk and reversal:* low risk and easy to reverse, but the cost repeats on every drawing PR for a gap we can't yet show matters.

## Consequences

- **Benefits.** No scenario or harness change. The cost of any future mesh meets the existing checks on the PR that adds it.
- **Tradeoffs.** The series spans meshes, so a long-range trend has to be read with the boundaries in mind. Entries before the tank don't name their mesh.
- **Risks.** The limits above: resolution of about 0.03 ms for one pair, flags that start at +0.5 ms at today's speed, the GPU gap, and a PC that isn't reference-class.
- **Trigger for a superseding ADR.** The game draws units with textures or animation, or more than one mesh per size class. The line above then stops describing what the game draws, and which meshes the ladder uses becomes a scenario question. ADR 0004's rendering ADR (follow-up 9) is the natural place.
- **Dependencies and owner decisions.** No package, Unity or handbook change. No owner decision is needed for this ADR.
- **PR #228.** This ADR asks it for no version bump, harness change or README edit.
- **Not decided here.** `docs/design/core-loop.md` (lines 11 and 328) describes sandbox units as cubes. That comes from Issue #224's change, not from this decision, and the GDD belongs to the game-designer, so it is raised with the Producer (AGENTS.md §9).

### Files

| File | Exact text | Result |
|---|---|---|
| `docs/performance/unit-ladder/README.md`, line 3 | "Scenario version 1 uses the sandbox's native-array simulation and instanced cubes." | **Must change:** "instanced cubes" becomes "the instanced unit mesh the game currently draws (ADR 0016)" |
| Same line | "small cubes for the first faction and large cubes for the second" | **Must change:** "small units for the first faction and large units for the second" |
| Same file, line 16 | "Changes to the scenario or harness start a new scenario version/comparison series." | **Must change:** add "A unit mesh change doesn't; it stays in the series (ADR 0016)." |
| `tools/unit-ladder.js`, line 231 | `scenarioVersion: firstMeta.meta.scenarioVersion` | No change. The value comes from the Player, and nothing here hard-codes a version. |
| `Game/Assets/Code/Presentation/Diagnostics/LadderScenario.cs`, line 8; `Game/Assets/Data/PerformanceLadder.asset`, line 15 | `public int Version = 1;` and `Version: 1` | No change. |
| `docs/performance/issue-97-speed-test-finding.md`, line 1 | "2,000-cube speed-test finding" | No change. It is a dated finding about the cube. |

### Follow-up Issue

1. **Update the ladder README as listed above, once PR #228 has merged.** Until then `main` still draws the cube and the README is right. A separate docs Issue leaves Issue #224's frozen criteria alone.

[frame-timing]: https://docs.unity.com/en-us/engine/6000.3/script-reference/unityengine/frametiming
[ftm]: https://docs.unity.com/en-us/engine/6000.3/manual/analysis/graphics-performance-profiling/profile-rendering/frame-timing-manager/manager
[markers]: https://docs.unity.com/en-us/engine/6000.3/manual/analysis/profiler/markers
