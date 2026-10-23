# ADR 0004: Unit simulation architecture and performance ladder

## Status

Accepted

## Date

2026-10-22

## Context

The vision makes **at least 2,000 active units at 60 fps at 1080p, on an RTX 3050 6 GB with a mid-range 6-core CPU** a release requirement (`docs/design/vision.md`, Performance target). AGENTS.md §4 says an ADR decides the unit simulation architecture and that nobody introduces a second approach. ADR 0001 left that decision to a later ADR. The vision also leaves open how the target is measured (open question 3). This ADR decides both, and it answers open question 3 (see "Performance ladder").

**What each unit costs.** Every simulation tick, each unit:
- moves: follows its path and steers around its neighbours (Pillar 3)
- thinks: keeps or picks a target, retreats from a losing fight, holds formation (Pillar 2)
- is drawn

Steering and target search both ask "who is near me?". If each unit checks every other unit, 2,000 units make 2,000 × 1,999 ≈ 4 million checks per tick. So neighbour queries must go through a spatial structure, and per-unit work must spread across all six cores.

**Constraints**
- Unity 6000.3.23f1, URP 17.3.0 and the Mono backend (ADR 0001). The PC renderer (`Game/Assets/Settings/PC_Renderer.asset`, `m_RenderingMode: 2`) uses the Forward+ path, and the GPU Resident Drawer is off in `PC_RPAsset.asset`.
- Unity 6.3 LTS support ends in December 2027 (ADR 0001), so the upgrade to the next LTS happens during development. Unity names that release as 6.7 ([ECS status][ecs-status]).
- The team is AI agents, each on one Issue at a time. Small, stable APIs that fail at compile time, rather than slowing down silently, suit that team best.
- Dependencies follow AGENTS.md §5. A Unity package needs an accepted ADR that names it and its license and says why the game needs it. The owner is asked first, and a new dependency is Critical tier.
- Owner context (Producer session, 2026-10-22):
  - Climb a ladder from 10 to 2,000 units and keep past recordings, so a regression can be traced to when performance was last good.
  - The first playable is a grey-box sandbox (flat ground, cubes, no art) for movement first, then the other core mechanics and AI.
  - Two factions, skirmish against AI only.
  - The owner agreed in advance to install the Unity packages this ADR needs. A separate Critical Issue adds them after this ADR merges.

**Evidence gap.** Nobody has measured any option in this project, and we found no official Unity benchmark for this kind of game. The ladder below is the measurement. A time-boxed spike (see Consequences) checks the chosen path at 2,000 units before features are built on it.

## Decision

**Option C, a data-oriented hybrid.** Units are rows in plain native arrays, not GameObjects. Burst-compiled jobs update those arrays, and the renderer draws units from them with GPU instancing. This is the game's one unit simulation approach (AGENTS.md §4).

### Why

- **Pillar 4, armies at scale.** Per-unit work runs as native code. Burst "translates from IL/.NET bytecode to highly optimized native code using LLVM" ([Burst][burst-pkg]). The job system is part of the core engine and lets code "use all available CPU cores" ([job system][jobs]). Main-thread work grows with draw calls, not units. One `Graphics.RenderMeshInstanced` call draws at most 1023 instances, or 511 with Unity's default per-instance matrices ([RenderMeshInstanced][rmi]). So 2,000 cubes in two faction materials take four instanced draws per pass.
- **Mono stays.** Burst code runs as native code under either backend (ADR 0001). A Windows build puts it in one native library, and Windows-to-Windows builds need no external toolchain ([Burst builds][burst-build]), unlike IL2CPP.
- **Pillar 3, movement.** Every unit queries its neighbours every tick, and a spatial grid rebuilt in a job keeps that cheap. Unity's job-friendly NavMesh query API "has been deprecated without replacement" ([NavMeshQuery][navquery]), so in any option we build large-group pathfinding ourselves (later ADR).
- **Pillar 2, smarter AI.** A unit's decision is the same small calculation for every unit, which is what parallel jobs do best. The enemy commander runs in plain C# and gives orders through the same command queue as the player, so it can't change unit state directly. A queue alone doesn't limit what it reads, so it never reads the unit arrays. It reads only its own faction's summaries, which jobs build from what that faction can see, because Pillar 2 rules out "information the AI shouldn't have" (`docs/design/vision.md`). The GDD sets the visibility rules, and the AI ADR (follow-up 8) defines the summaries and the filter.
- **AGENTS.md §4.** With no GameObject per unit, `Find*`, `GetComponent` and `SendMessage` can't appear in unit code. Burst doesn't support "any methods related to managed objects", catching exceptions, or storing to static fields except through `SharedStatic` ([HPC#][hpc]). So the Burst compiler rejects per-unit code that uses managed objects, which is where managed allocations come from.
- **The team.** The API surface is small: `NativeArray`, `IJobFor` or `IJobParallelFor`, `[BurstCompile]`, `Unity.Mathematics` and `Graphics.RenderMeshInstanced`. There's no baking or SubScene workflow. Jobs are plain structs, so EditMode tests can run them without a scene.
- **The next upgrade.** Unity is moving ECS into the engine core in 6.4: "This change does involve structural changes to our API", rolled out over the releases "leading up to the next LTS in Unity 6.7" ([ECS status][ecs-status]). Building on the core job system keeps that upgrade small and leaves Entities open for a later ADR once its API settles.

### How the parts fit

| Part | Where it runs | Notes |
|---|---|---|
| Unit state | One `NativeArray` per field (a struct of arrays), allocated once per match at full capacity | Other code refers to a unit only by a handle (slot plus generation). The handle survives reordering, and a stale handle is detectable. |
| Simulation tick | One driver MonoBehaviour per match schedules a chain of jobs on a fixed tick, independent of the frame rate | Units behave the same on fast and slow PCs, and ladder runs stay comparable. Drawing interpolates between the last two ticks, so motion stays smooth. The movement ADR sets the tick rate, and the ladder logs it. |
| Spatial grid | Burst job, rebuilt every tick | Shared by steering, targeting and selection |
| Movement | Burst jobs: path following, separation and avoidance, integration | Pathfinding method (for example flow fields), avoidance and formations: later ADR |
| Unit AI and targeting | Burst jobs: a state machine per unit, and target search through the grid, spread across ticks | Rules and time-slicing: later ADR |
| Commander AI | Main thread, plain C#, a few times per second | Reads only its faction's visibility-filtered summaries, which the jobs write, and issues orders through the command queue |
| Player orders and selection | Input on the main thread (Input System, already in the manifest). Box and click selection run as a Burst job over unit positions. | No colliders per unit |
| Drawing | A Burst job fills per-instance matrices, and the driver calls `Graphics.RenderMeshInstanced` per mesh and material | Prebuilt URP materials with GPU instancing enabled ([GPU instancing][gpu-instancing]). Animation, LOD and the BatchRendererGroup: later rendering ADR |
| Everything else | Camera, UI, lights, ground and match setup stay ordinary GameObjects | Anything whose count grows with the unit count (projectiles, health bars, selection markers) follows the unit rules |

### Packages

| Package | Version | Publisher | License | In `manifest.json`? | Why the game needs it |
|---|---|---|---|---|---|
| `com.unity.burst` | 1.8.30 | Unity Technologies | Source code: Unity Companion License; otherwise: Unity Package Distribution License ([license][burst-license]) | No. In `packages-lock.json` at 1.8.30, depth 2, through URP | Compiles the per-unit jobs to native code |
| `com.unity.collections` | 2.6.8 | Unity Technologies | Unity Companion License ([license][collections-license]) | No. In the lock at 2.6.8, depth 2 | Job-safe containers beyond `NativeArray` (lists, queues, hash maps) for the command queue, the grid and AI |
| `com.unity.mathematics` | 1.3.3 | Unity Technologies | Unity Companion License ([license][mathematics-license]) | No. In the lock at 1.3.3, depth 2 | Vector types and math functions that Burst compiles to SIMD code |
| `com.unity.render-pipelines.universal` | 17.3.0 | Unity Technologies | Unity Companion License ([license][urp-license]) | Yes | Renders the game, including the instanced unit materials |
| `com.unity.inputsystem` | 1.20.0 | Unity Technologies | Unity Companion License ([license][inputsystem-license]) | Yes | Player input for orders and selection |
| `com.unity.test-framework` | 1.6.0 | Unity Technologies | Unity Companion License ([license][test-framework-license]) | Yes | EditMode tests that run the jobs without a scene |
| `com.unity.ext.nunit` | 2.0.5 | Unity Technologies | Unity Package Distribution License ([license][nunit-license]) | No. In the lock at 2.0.5, depth 1, through Test Framework | Test Framework tests "must be in a test assembly, which is any assembly that references NUnit" ([test assembly][test-assembly]), and this package holds `nunit.framework.dll` |

- **Support for 6000.3.23f1.**
  - Burst 1.8.30 "Changed minimum Editor version to 6.0" ([Burst changelog][burst-changelog]).
  - Collections 2.6.8 declares Unity 2022.3.20f1 as its minimum in its own `package.json` (in the project's Package Manager cache). Collections 2.7.1 is the release that "Updated the minimum editor version to 6000.0" ([Collections 2.7 changelog][collections-changelog]).
  - Mathematics 1.3.3 is the version Unity lists as released for 6000.3 ([Mathematics][mathematics-pkg]).
  - All three already resolve and compile in this project under 6000.3.23f1, because URP 17.3.0 depends on them.
- **Why these exact versions** of Burst, Collections and Mathematics. They are what `packages-lock.json` resolves today. Listing them in the manifest should therefore change no resolved version, and nothing URP compiles against. Unity's 6000.3 manual now lists newer releases for the 6000.3 line: Burst 1.8.31 and Collections 2.7.1 ([Burst][burst-pkg], [Collections][collections-pkg]). Moving to them is an upgrade, which needs its own ADR (AGENTS.md §4).
- **Burst's license and §5.** The §5 exception covers Unity packages under "the Unity Companion License or the Unity Terms of Service". Burst's non-source parts are under the Unity Package Distribution License ([UPDL][updl]), which the exception doesn't name. ADR 0002 also treats that license as outside the list. So this ADR approves Collections and Mathematics, but **Burst's approval takes effect only once AGENTS.md §5 covers the Unity Package Distribution License**. That is owner decision 1 (see Consequences). Burst also bundles third-party components under their own licenses, listed in its `Third Party Notices.md` (for example LLVM, under "Apache 2.0 with LLVM Exceptions").
- **NUnit's license and §5.** `com.unity.ext.nunit` is under the same Unity Package Distribution License and is in the lock only because Test Framework depends on it. Test assemblies reference it directly, so it gets the same treatment as Burst: **its approval for direct use takes effect only with owner decision 1**. It stays out of the manifest, at the version Test Framework pins.
- **Used from the engine, with no package:** the C# Job System, `NativeArray<T>` (in `UnityEngine.CoreModule`, [NativeArray][nativearray]), `Graphics.RenderMeshInstanced` and the `FrameTimingManager`.
- **Already in the manifest and used:** URP, Input System and Test Framework need no ADR for direct use (AGENTS.md §5). Their rows put the publisher and license of every package the choice uses on record.
- **`com.unity.ai.navigation` 2.0.14 is not used.** Units get no `NavMeshAgent`, and the job-friendly NavMesh query is deprecated ([NavMeshQuery][navquery]). This ADR doesn't remove the package, because removal needs its own ADR (AGENTS.md §4). The pathfinding ADR may decide whether NavMesh baking has any role.
- **Not approved here:** `com.unity.entities`, `com.unity.entities.graphics` and `com.unity.physics`. The packages that Collections pulls in (`com.unity.test-framework.performance` 3.5.0 and `com.unity.nuget.mono-cecil` 1.11.6) stay indirect and are not approved for direct use.

### Rules for hot paths

These rules are for the gameplay-programmer and the reviewers, and they add to AGENTS.md §4.

1. **A unit is a row, not an object.** No GameObject, MonoBehaviour, Collider, Rigidbody, NavMeshAgent, Animator or Renderer per unit, or per anything else whose count grows with the unit count.
2. **Per-unit work runs only in `[BurstCompile]` jobs** (`IJobFor`, `IJobParallelFor` or `IJob`) over native containers. Main-thread code handles only summaries, orders and draw calls, and never loops over all units.
3. **Job structs hold only** blittable fields, native containers and `Unity.Mathematics` types: no classes, managed arrays, strings, `try`/`catch`, or writes to static fields. Mark inputs `[ReadOnly]`.
4. **Allocate once.** Unit arrays, the grid and every scratch buffer use `Allocator.Persistent`, sized at match start for the match's full capacity and disposed at match end. Per-frame and per-unit code allocates nothing, on the main thread or in a job (AGENTS.md §4): no `Allocator.Temp` or `Allocator.TempJob` containers, and no container that grows. Containers are cleared and reused, and jobs add with the `NoResize` methods, which "will not increase the capacity" ([NativeList][nativelist]). A job that needs scratch space uses its own slice of a preallocated buffer: one slice per unit index, or one per worker thread through `[NativeSetThreadIndex]` ([NativeSetThreadIndex][set-thread-index]), with the buffer sized by `JobsUtility.ThreadIndexCount` ([ThreadIndexCount][thread-index-count]).
5. **One sync point per frame.** Chain a tick's jobs with `JobHandle` dependencies and complete them once, at one fixed place in the frame. Never call `Complete()` inside a loop, and never read a job's output before its handle completes.
6. **Hold handles, not indices.** Anything kept across ticks stores a unit handle and checks its generation before use.
7. **All orders go through the command queue,** applied at the start of a tick, whether they come from the player or the commander AI. No system writes another system's arrays outside the scheduled job chain. The commander AI never reads the unit arrays, only its faction's visibility-filtered summaries.
8. **Neighbour and target searches use the spatial grid,** never a loop over every unit.
9. **Drawing.** A job fills the per-instance data, and the driver draws each mesh and material in chunks within the documented instance limit ([RenderMeshInstanced][rmi]). Share one material per unit type and faction. Never use `renderer.material`, per-unit materials or per-unit property blocks.
10. **Keep safety checks on.** Leave the job and Burst safety checks at their Editor defaults, and keep Burst warnings out of the console (AGENTS.md §4).
11. **Prove it.** A PR that changes these paths includes its ladder entry (below) as the before/after numbers AGENTS.md §4 asks for. It also includes a Profiler capture from a development build showing 0 B GC Alloc in simulation and drawing code after warm-up.

### Performance ladder

This section answers open question 3 in `docs/design/vision.md`: the test scenario, what counts as an "active" unit, and whether 60 fps is an average or a minimum. It is both (see "Pass rule").

**Steps.** 10, 50, 100, 200, 300, 400, 500, 1,000 and 2,000 active units. Every ladder runs all steps in that order, even after a step fails, so the curve is complete. 2,000 is the top step and the pass mark: the vision target is met when the 2,000 step passes on reference-class hardware (see "Pass rule"). The **ladder height** is the highest step that passes with every step below it passing too.

**Test scene.** One scene used only for the ladder:
- flat plane, no obstacles
- units are Unity's built-in cube, with one prebuilt URP material per faction and GPU instancing on; no art, textures, animation or audio
- one directional light, and the project's default Windows quality level, unchanged
- a fixed camera at the gameplay angle that frames the whole area, so every unit is drawn every frame (the worst case for drawing)
- no UI during measurement
- the scenario: N/2 units per faction spawn in blocks at opposite ends. Both sides are ordered to the far end on a fixed cycle, so the groups keep crossing in the middle. Once combat exists, units fight but can't die, so the count stays at N. The random seed is fixed.
- Every scenario parameter (plane size, spawn layout, order cycle, seed) lives in one ScriptableObject with a scenario version. Any change bumps the version and starts a new comparison series. Old entries stay.

**Active unit.** A unit is active when, for the whole measurement window:
- it is in the simulation and belongs to one of the two factions (N/2 each)
- it runs every per-unit system the build has, at its normal-play rate. Spreading work over ticks the way normal play does is fine. Turning a system off or slowing it down for the test is not.
- it has a current order and is drawn every frame

Projectiles, effects, buildings, dead units and markers don't count.

**Measurement.**
- **Player build, never the Editor.** Use a Windows x64 non-development Player with Mono, at 1920×1080 in borderless full screen. Play mode "runs in the same application and main thread as the Editor", and profiling there "doesn't give you an accurate reflection" of how the application performs ([collecting performance data][collect]). The Editor is closed during runs. A development build is used only to attach the Profiler when investigating.
- **Uncapped.** Set `QualitySettings.vSyncCount = 0` and `Application.targetFrameRate = -1`, so "content is rendered unsynchronized as fast as possible" ([targetFrameRate][target-fps]). The log then shows headroom instead of a 16.67 ms cap.
- **Frame data** comes from the FrameTimingManager ([FrameTimingManager][ftm]). Release builds need the Frame Timing Stats Player setting turned on ([enabling it][ftm-enable]), and the harness Issue does that.
  - Frame time is `FrameTiming.cpuFrameTime`, "the time between ends of two frames", which includes all waiting ([FrameTiming][frame-timing]).
  - `cpuMainThreadFrameTime` and `gpuFrameTime` are recorded too, to tell CPU-bound from GPU-bound.
  - Samples go into a buffer allocated before the window starts.
  - Results arrive "with a set delay of four frames", and the GPU "might fail to return them correctly" ([FrameTimingManager][ftm]). `GetLatestTimings` returns "the number of FrameTimings it actually was able to get", which can be fewer than requested ([GetLatestTimings][ftm-latest]). So the harness matches each sample to the frame it describes, and counts the frames the window renders separately from the samples. The harness Issue decides how.
- **One run:** launch a fresh Player with the unit count on the command line, spawn the units and give orders, discard 10 s of warm-up, measure 60 s, write the summary, and quit.
  - The warm-up covers spawning, first-use shader and pipeline work, and loading Burst's library, which happens "the first time a Burst compiled method is invoked" ([Burst builds][burst-build]). 10 s is a judgment call. If the baseline shows start-up spikes after it, a new ADR lengthens it.
  - 60 s at 60 fps or more is at least 3,600 frames. The slowest 1% is then at least 36 frames, so one hitch can't decide a result. 60 s also covers several order cycles.
- **Invalid runs.** A run is invalid, neither a pass nor a fail, when any of these happens:
  - `FrameTimingManager.IsFeatureEnabled()` returns false ([IsFeatureEnabled][ftm-enabled]).
  - Any frame in the window has no `cpuFrameTime` sample, more than one, or a value that isn't positive. A missing frame could be the slow one, so the run can't show a pass.
  - The Player is paused or loses focus during the window. The FrameTimingManager "automatically excludes" paused time from CPU timings ([FrameTimingManager][ftm]), so a pause would hide time.

  Missing or non-positive `cpuMainThreadFrameTime` and `gpuFrameTime` samples don't invalidate a run, because the pass rule doesn't use them. Their averages cover only the valid samples, and the run's summary records how many were missing. The summary also records an invalid run and why, and the harness replaces it with a new run. A step that still lacks three valid runs after six attempts (a judgment call) has the result `invalid`. An `invalid` step never counts as a pass: not for the ladder height, the vision target or a PR's evidence.
- **Three valid runs per step.** Each reported number is the median of the three valid runs.
- **Numbers per run:** frame count, average frame time, 1% low frame time, maximum frame time, average CPU main-thread time and average GPU time. Average fps is 1000 ÷ average frame time.
- **1% low frame time** is the 99th-percentile frame time by nearest rank: sort the window's frame times and take the one at position ⌈0.99 × n⌉. 99% of frames took that long or less.
- **The machine:** Windows power mode set to best performance, and no other heavy programs running.

**Pass rule.** A step passes when the medians of its three valid runs show both of these:
1. average frame time ≤ 16.67 ms (60 fps)
2. 1% low frame time ≤ 16.67 ms

Why:
- **Both, not one.** An average alone allows regular stutter, which goes against Pillars 1 and 3 and the "smooth frame rate" in AGENTS.md §4. On a 60 Hz display, a frame longer than 16.67 ms misses a refresh, and the player sees a stutter. Using the single slowest frame would let one Windows hiccup fail a run. The 99th percentile is the strict but repeatable form of "at least 60 fps".
- **One rule at every step** keeps it simple for agents and the owner. The low steps will pass easily; their value is the curve, and catching regressions early (below).
- **The median of three** stops one disturbed run from passing or failing a step.

**Where a pass counts.** A pass counts toward the vision target only on reference-class hardware: a PC with an RTX 3050 6 GB and a mid-range 6-core CPU. Entries from other hardware count for trends and regressions only, and say `reference-class: no`.

**Regression check.** Each step is compared with the newest entry already on `main` that has the same hardware summary and scenario version.
- A step is **regressed** when its average or 1% low frame time got worse by more than 10% **and** by more than 0.5 ms. Both thresholds apply because at low steps frame times are a few milliseconds and 10% is noise, while at high steps 0.5 ms alone would be too lax. Both values are judgment, not measurement. The baseline records each step's three-run spread, and if that spread exceeds the tolerance, a new ADR adjusts it.
- The PR that causes a regression either fixes it or states the added per-unit cost and why (Pillar 4). The reviewer treats an unexplained regression as Request changes.
- A PR that makes a previously passing step fail gets Request changes. If the author shows the cost comes from a designed feature, the reviewer escalates it to the owner as a design trade-off (AGENTS.md §6).

**Log entry.** Each full ladder run gets one entry with:
- the date (YYYY-MM-DD) and the context: baseline, PR #n, or a re-run with its reason
- the full SHA of the measured commit. The entry may land in a later commit whose only change is the entry itself.
- the scenario version and the simulation tick rate
- the build: Unity version, scripting backend, graphics API, quality level, resolution, VSync off
- a hardware summary: CPU model, cores and threads; GPU model, VRAM and driver version; RAM; Windows version and build; reference-class yes or no. The harness Issue decides how to read the driver version without personal information.
- per step: the medians (average, 1% low and maximum frame time, CPU main-thread and GPU time, average fps), the three valid runs' average frame times, any invalid runs with their reasons, and the result. The result is `pass`, `fail` or `invalid`, plus `regressed` when that applies.

An entry never records the computer name, the Windows user name, a local path (the Player's `Application.persistentDataPath`, for example, can contain the user name), serial numbers, device IDs, or IP or MAC addresses (AGENTS.md §5). The harness never reads `SystemInfo.deviceName` or `SystemInfo.deviceUniqueIdentifier`. Raw per-frame files stay on the machine and are never committed.

**Log location.** `docs/performance/unit-ladder/` holds one Markdown file per entry, named `YYYY-MM-DD-<first 7 hex digits of the SHA>.md`, with `-2`, `-3` and so on for repeats. One file per entry means parallel PRs never conflict over the log, and the folder sorted by name is the history. Entries are never edited or deleted. A correction is a new entry that names the one it corrects.

**When to re-run.** The full ladder runs:
1. once as the baseline, when the sandbox and harness first merge
2. in every PR that changes unit update loops, movement or pathfinding, unit or commander AI, targeting, or unit drawing
   - The author's entry is the PR's before/after evidence. "Before" is the newest `main` entry on the same hardware and scenario version.
   - QA re-runs the ladder on the PR head as its performance check. If `main` has had such changes since its newest entry, QA first re-runs `main`.
3. after any Unity, URP or package upgrade or removal, any change to quality, graphics or Player settings, and any scripting-backend change (ADR 0001's IL2CPP evaluation)
4. when the scenario or the harness changes, which starts a new series
5. when the measuring machine, its GPU driver or its Windows version changes, which starts a new hardware series
6. at the end of every milestone, so the history has regular points even without hot-path changes

## Alternatives considered

| | (a) GameObjects and MonoBehaviours | (b) Entities, Burst and jobs | (c) Arrays, Burst, jobs and instancing (chosen) |
|---|---|---|---|
| Per-unit CPU work | Managed Mono code on the main thread | Native and parallel | Native and parallel |
| Drawing | One renderer per unit; the GPU Resident Drawer can batch them | Entities Graphics (BatchRendererGroup) | A few instanced calls |
| Packages to add | None | Entities, Entities Graphics, Burst, Collections, Mathematics | Burst, Collections, Mathematics |
| Concepts agents must master | Few, well known | Many, with recent API churn | Few, stable |
| Cost to reverse | Rewrite all unit code | Rewrite all gameplay code | A moderate port to Entities later |

**(a) GameObjects with MonoBehaviours**
- *How it works:* each unit is a GameObject with a MeshRenderer, a unit script with `Update`, and a `NavMeshAgent`. Colliders serve selection and range checks.
- *Evidence:*
  - Unity's own test of 10,000 `Update` calls found that most of the time went to the engine's per-call work (iterating behaviours, checks, calling into managed code) rather than to the scripts. A manager looping over an array on Mono took 0.23 ms ([Unity blog][update-blog]). That test used Unity 5.2 on iOS in 2015, so it is indicative only.
  - The GPU Resident Drawer can draw MeshRenderer GameObjects with GPU instancing on the Forward+ path ([GPU Resident Drawer][grd]).
  - NavMesh queries can't run in jobs ([NavMeshQuery][navquery]).
- *Cost at 2,000 units:* all unit logic runs on one core as Mono code, plus each object's transform, renderer and agent upkeep. We found no official per-GameObject cost figure, and nobody has measured it here.
- *Complexity:* the lowest. Every agent knows it, and the Inspector, Animator, physics and audio all work directly.
- *Risk and reversibility:* the highest risk to Pillar 4, and it shows up late. If the game stalls between 1,000 and 2,000 units, every unit script gets rewritten.
- *Variant, GameObjects plus jobs:* logic runs in Burst jobs that write transforms back, and the GPU Resident Drawer draws the units. That keeps a GameObject and a transform sync per unit, two copies of the truth (arrays and transforms), and the pull to add per-unit components. It costs about as much as (c) to build and gives less of its safety.

**(b) Unity Entities (DOTS/ECS) with Burst, jobs and Entities Graphics**
- *How it works:* units are entities with component structs, and systems update them in parallel. Authoring GameObjects are baked into entities in SubScenes, and Entities Graphics draws them.
- *Evidence:*
  - Unity says ECS's data-oriented design lets creators "scale processing in a highly performant manner" ([ECS feature set][ecs-feature]).
  - Entities 1.5.0 and Entities Graphics 1.5.0 are the versions released for 6000.3 ([Entities][entities-pkg], [Entities Graphics][entities-graphics-pkg]), both under the Unity Companion License ([Entities license][entities-license], [Entities Graphics license][entities-graphics-license]).
  - On URP, Entities Graphics supports "only Forward+", and it doesn't support multiple Worlds ([requirements][entities-graphics-req]). Our PC renderer already uses Forward+.
  - Entities 1.4.0-exp.2 marked `Entities.ForEach` and `IAspect` obsolete ([Entities changelog][entities-changelog]), and Unity plans "structural changes" to the API through 6.7 ([ECS status][ecs-status]).
- *Cost at 2,000 units:* the most headroom of the three, with chunked memory, parallel work by default, and built-in batching and culling. That is more than this target needs.
- *Complexity:* the highest. Agents must master archetypes, structural changes and command buffers, system ordering, and baking with SubScenes (scene YAML, which AGENTS.md §4 prefers not to hand-edit). Much public Entities code predates 1.4, so agents are likely to write obsolete APIs, and the warnings break the clean-console rule.
- *Risk and reversibility:* all gameplay code becomes ECS-specific, so leaving means a rewrite. The 6.7 LTS upgrade, needed before 6.3 support ends, would bring ECS API migrations.
- *Not chosen* because its main gain is headroom well beyond 2,000 units, while its costs land on every Issue.

**(c) with the BatchRendererGroup from day one.** The BatchRendererGroup is "an API for high-performance custom rendering in projects that use a Scriptable Render Pipeline" and the SRP Batcher ([BatchRendererGroup][brg]). It needs no package, but it takes more setup and shader work. `RenderMeshInstanced` is enough for grey-box cubes, and the rendering ADR decides when art needs more.

**(c) without Burst.** Jobs alone run on worker threads as managed Mono code. That avoids the license question, but it gives up the native compilation that makes per-unit work cheap. It is the fallback only if the owner declines Burst's license, and only through a superseding ADR.

## Consequences

- **Benefits.** Per-unit cost stays low and parallel, and main-thread cost stays nearly flat as the unit count grows. Burst turns part of the no-allocation rule into compile errors. The game adds only three packages, all already in the project as indirect dependencies. The ladder gives a dated history for tracing any regression.
- **Tradeoffs.** We build what GameObjects or Entities would give us: the spatial grid, selection picking, unit handles, instanced drawing, and per-instance culling when it's needed. (`RenderMeshInstanced` uses one call's bounds to "cull and sort all the instances of this Mesh as a single entity" ([RenderMeshInstanced][rmi]).) Units don't appear in the Hierarchy or Inspector, so the sandbox needs a debug overlay that shows a selected unit's data.
- **Ladder time.** A full ladder is 27 runs of about 75 s each, roughly 35 minutes of machine time, plus any runs that replace invalid ones. This is an estimate.
- **Risks.**
  - Burst and NUnit can't be approved until §5 covers their license (owner decision 1).
  - Absolute passes count only on reference-class hardware (owner decision 2).
  - The ladder needs Player builds. The harness Issue must show that the agent who runs the ladder can build and run the Player with one command.
  - Burst 1.8.30 lists a known issue: Windows Smart App Control or WDAC policies may block its JIT-compiled libraries at load time ([Burst changelog][burst-changelog]). That affects Burst's JIT compilation in the Editor, not the ahead-of-time library in Player builds.
  - `RenderMeshInstanced` may not be enough once units have art and animation. The rendering ADR settles that.
- **Reversibility.** Moving from (c) to Entities later is a port, not a rewrite, because job structs and data layouts carry over into systems. Changing the data layout is cheap while only the sandbox exists. Dropping Burst would leave the same code running, only slower.
- **ADR 0001.** This ADR answers ADR 0001's follow-up "an ADR on the unit simulation architecture" and doesn't supersede it. Burst code is native under either backend, so ADR 0001's IL2CPP evaluation only concerns main-thread managed code.
- **Owner decisions:**
  1. **Burst's and NUnit's license.** Approve a Critical handbook change (security perimeter) that adds the Unity Package Distribution License to the AGENTS.md §5 exception for Unity packages. *Recommended:* Burst comes from Unity's registry like the packages the exception already covers, URP already depends on it, and both options that scale need it. The install Issue waits for that change, so all three packages arrive together. The same change lets this ADR's approval of `com.unity.ext.nunit` take effect. Every Unity test needs that package, so declining the change also leaves Unity tests without an approved route, a studio-wide question beyond this ADR. If the owner declines, a superseding ADR revisits this decision before any unit code is written.
  2. **The ladder machine.** Which PC runs the ladder, and does it count as reference-class? *Recommended:* run every ladder on one fixed studio PC for trends. If that PC isn't reference-class, certify the 2,000 step on one that is before release.
- **Follow-up Issues,** in order:
  1. The handbook change for owner decision 1 (Critical, security perimeter; the Issue names `AGENTS.md`).
  2. Add Burst, Collections and Mathematics to `Game/Packages/manifest.json` at the versions above. `packages-lock.json` should change only their `depth`. Critical, security perimeter, after this ADR and Issue 1 merge.
  3. The folder-layout ADR (0005, already planned).
  4. A spike (`type:spike`, time box one day): 2,000 cubes, a simple seek-and-separate job over a grid, and instanced drawing. Measure the 1,000 and 2,000 steps with the ladder method, and report frame, main-thread and GPU times and whether `RenderMeshInstanced` is enough. It can become the first sandbox Issue.
  5. The grey-box sandbox: ladder scene, scenario ScriptableObject, simulation driver, spawner, instanced drawing, move orders and the debug overlay.
  6. The ladder harness: unit count from the command line, warm-up and measurement, summary output, Frame Timing Stats on, one command that builds and runs the full ladder, and the baseline entry in `docs/performance/unit-ladder/`.
  7. A movement and pathfinding ADR (flow fields or another method, avoidance, formations, tick rate).
  8. A unit AI, targeting and commander AI ADR, including the commander's summaries and their visibility filter.
  9. A rendering-at-scale ADR when art starts (animation, LOD, the BatchRendererGroup, custom shaders and the SRP Batcher).
  10. game-designer: mark open question 3 in `docs/design/vision.md` as answered by this ADR (Owner tier).
  11. Suggested: add the hot-path rules to the gameplay-programmer and code-reviewer role files. These are protected paths, so that Issue must name them.

[burst-pkg]: https://docs.unity3d.com/6000.3/Documentation/Manual/com.unity.burst.html
[collections-pkg]: https://docs.unity3d.com/6000.3/Documentation/Manual/com.unity.collections.html
[mathematics-pkg]: https://docs.unity3d.com/6000.3/Documentation/Manual/com.unity.mathematics.html
[entities-pkg]: https://docs.unity3d.com/6000.3/Documentation/Manual/com.unity.entities.html
[entities-graphics-pkg]: https://docs.unity3d.com/6000.3/Documentation/Manual/com.unity.entities.graphics.html
[burst-license]: https://docs.unity3d.com/Packages/com.unity.burst@1.8/license/LICENSE.html
[collections-license]: https://docs.unity3d.com/Packages/com.unity.collections@2.6/license/LICENSE.html
[mathematics-license]: https://docs.unity3d.com/Packages/com.unity.mathematics@1.3/license/LICENSE.html
[urp-license]: https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@17.3/license/LICENSE.html
[inputsystem-license]: https://docs.unity3d.com/Packages/com.unity.inputsystem@1.20/license/LICENSE.html
[test-framework-license]: https://docs.unity3d.com/Packages/com.unity.test-framework@1.6/license/LICENSE.html
[nunit-license]: https://docs.unity3d.com/Packages/com.unity.ext.nunit@2.0/license/LICENSE.html
[test-assembly]: https://docs.unity.com/en-us/engine/6000.3/manual/programming-environment/test-framework-introduction/getting-started/workflow-create-test-assembly
[entities-license]: https://docs.unity3d.com/Packages/com.unity.entities@1.5/license/LICENSE.html
[entities-graphics-license]: https://docs.unity3d.com/Packages/com.unity.entities.graphics@1.5/license/LICENSE.html
[updl]: https://unity.com/legal/licenses/unity-package-distribution-license
[burst-changelog]: https://docs.unity3d.com/Packages/com.unity.burst@1.8/changelog/CHANGELOG.html
[collections-changelog]: https://docs.unity3d.com/Packages/com.unity.collections@2.7/changelog/CHANGELOG.html
[entities-changelog]: https://docs.unity3d.com/Packages/com.unity.entities@1.5/changelog/CHANGELOG.html
[burst-build]: https://docs.unity3d.com/Packages/com.unity.burst@1.8/manual/building-projects.html
[hpc]: https://docs.unity3d.com/Packages/com.unity.burst@1.8/manual/csharp-hpc-overview.html
[jobs]: https://docs.unity3d.com/6000.3/Documentation/Manual/job-system-overview.html
[nativearray]: https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Unity.Collections.NativeArray_1.html
[nativelist]: https://docs.unity3d.com/Packages/com.unity.collections@2.6/api/Unity.Collections.NativeList-1.html
[set-thread-index]: https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Unity.Collections.LowLevel.Unsafe.NativeSetThreadIndexAttribute.html
[thread-index-count]: https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Unity.Jobs.LowLevel.Unsafe.JobsUtility.ThreadIndexCount.html
[rmi]: https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Graphics.RenderMeshInstanced.html
[gpu-instancing]: https://docs.unity3d.com/6000.3/Documentation/Manual/GPUInstancing.html
[brg]: https://docs.unity3d.com/6000.3/Documentation/Manual/batch-renderer-group.html
[grd]: https://docs.unity3d.com/6000.3/Documentation/Manual/urp/gpu-resident-drawer.html
[navquery]: https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Experimental.AI.NavMeshQuery.html
[ecs-feature]: https://docs.unity3d.com/6000.3/Documentation/Manual/ECSFeature.html
[entities-graphics-req]: https://docs.unity3d.com/Packages/com.unity.entities.graphics@1.5/manual/requirements-and-compatibility.html
[ecs-status]: https://discussions.unity.com/t/ecs-development-status-december-2025/1699284
[update-blog]: https://unity.com/blog/engine-platform/10000-update-calls
[collect]: https://docs.unity3d.com/6000.3/Documentation/Manual/profiling-collect-data-introduction.html
[target-fps]: https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Application-targetFrameRate.html
[ftm]: https://docs.unity3d.com/6000.3/Documentation/Manual/frame-timing-manager.html
[ftm-enable]: https://docs.unity3d.com/6000.3/Documentation/Manual/frame-timing-manager-enable.html
[ftm-latest]: https://docs.unity3d.com/6000.3/Documentation/ScriptReference/FrameTimingManager.GetLatestTimings.html
[ftm-enabled]: https://docs.unity3d.com/6000.3/Documentation/ScriptReference/FrameTimingManager.IsFeatureEnabled.html
[frame-timing]: https://docs.unity3d.com/6000.3/Documentation/ScriptReference/FrameTiming.html
