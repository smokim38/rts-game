# ADR 0005: Folder layout, assemblies and namespaces in `Game/Assets`

## Status

Accepted

## Date

2026-10-22

## Context

AGENTS.md §2 tells agents to ask before creating a top-level folder in `Game/Assets/` "until an ADR sets the folder layout". This is that ADR. Engineers are about to build the grey-box sandbox, several at once in separate worktrees. So every file needs one obvious home, and the boundaries between parts of the code should fail at compile time, not in review.

Inputs:
- **[ADR 0004](0004-unit-simulation-architecture.md)** (accepted). Units are rows in native arrays. Burst jobs update them, and `Graphics.RenderMeshInstanced` draws them. A driver MonoBehaviour ticks the jobs. The commander AI is plain C# that "never reads the unit arrays", only its faction's summaries (rule 7). EditMode tests run jobs without a scene. ADR 0004 needs two scenes (the sandbox and the ladder scene) and a ladder scenario ScriptableObject. Its owner decision 1, adding the Unity Package Distribution License to AGENTS.md §5, still gates direct use of Burst and of NUnit (`com.unity.ext.nunit`).
- **AGENTS.md §4.** One public type per file, and namespaces mirror folders. Two agents don't edit the same scene at once. Prefabs and ScriptableObjects are preferred.
- **Owner context (Issue #71).** Core systems and AI come first, with no art yet. Two factions, and a 2,000-unit ladder.
- **Today's `Game/Assets/`** is the URP template: `Readme.asset`, `TutorialInfo/`, `InputSystem_Actions.inputactions`, `Scenes/SampleScene.unity` and `Settings/`. There is no game code and no assembly definition. Project Settings point at some of these by GUID:
  - `EditorBuildSettings.asset` points at the scene, and at the input actions as the project-wide actions.
  - `GraphicsSettings.asset` and `QualitySettings.asset` point at the URP assets.
- **Why now.** An asset moved together with its `.meta` keeps its GUID (AGENTS.md §4), so folders are cheap to move later. Renaming an assembly or a namespace touches every file in it, so names are cheapest to fix before any code exists.

**Evidence gap.** Nobody has measured compile times or iteration speed for any layout in this project. Nothing in this decision depends on such a measurement, so no spike is proposed.

## Decision

### Top-level folders

`Game/Assets/` holds exactly these folders:

| Folder | Holds |
|---|---|
| `Code/` | All C#, each part in its own assembly (below): runtime code (`Simulation/`, `CommanderAI/`, `Presentation/`), editor code (`EditorTools/`) and tests (`Tests/EditMode/`, `Tests/PlayMode/`). Every `.cs` file under `Game/Assets/` lives here. |
| `Scenes/` | `Sandbox.unity`, the grey-box sandbox. `PerformanceLadder.unity`, the scene ADR 0004 uses only for the ladder. The folder exists today. |
| `Prefabs/` | The few GameObjects: the match driver, the camera rig, UI and the debug overlay. Never a unit, or anything else whose count grows with the unit count (ADR 0004 rule 1). |
| `Data/` | ScriptableObject assets, for example unit types, factions, the ladder scenario (ADR 0004) and commander tuning. Their classes live in `Code/`. |
| `Materials/` | Materials made in Unity, for example the grey-box faction materials with GPU instancing (ADR 0004), and selection and debug materials. |
| `Settings/` | Assets that Project Settings point to: URP assets and renderers, URP global settings, volume profiles and the project-wide input actions. The folder exists today. |
| `Art/` | Reserved for exports from the repository's `Art/` folder (AGENTS.md §4). Not created until the first export. The art pipeline doc sets its inner layout, including where art materials and shaders go. |

**Folder rules**
1. **Nothing else at the top.** A new top-level folder of ours, for example for audio or UI documents when they're first needed, needs a superseding ADR.
   - Folders that Unity or an approved package creates on its own are allowed, in the place Unity requires. For example, `StreamingAssets/` is valid at the "Root of the `Assets` folder only" ([special folders][special]). The PR that adds such a folder says so.
   - No loose files at the top once the cleanup Issue (below) is done.
2. **Subfolders by feature,** in PascalCase with no spaces, for example `Code/Simulation/Movement/` or `Data/Units/`. Unity says "Don't use spaces in file and folder names", because its "command line tools have issues with path names that include spaces" ([organizing][organizing]), and the ladder builds from the command line.
3. **No empty folders.** Create a folder with its first file. Git ignores empty folders, so a folder's `.meta` would be committed without the folder, and Unity deletes it on other machines ([organizing][organizing]).
4. **No `Resources/` folder.** Assets in one "increase the size of Player builds" ([special folders][special]). Reference assets from scenes, prefabs and ScriptableObjects instead. A `Resources/` folder needs a new ADR.
5. **No folder named `Editor`.** When an assembly definition sits above an `Editor` folder, the scripts in it go into that assembly, "where they might not belong" ([assemblies][asm-intro]). Editor code goes in `Code/EditorTools/`.
6. **Scenes stay thin.** A scene holds prefab instances, lights and the ground. Behaviour and tuning live in prefabs and in `Data/` (AGENTS.md §4). An Issue that must edit a scene names the scene, so the Producer can keep two agents out of it at the same time.

### Assemblies

There are six assembly definitions. Each assembly's name is also its root namespace.

| Assembly | Folder | Platforms | May reference | Holds |
|---|---|---|---|---|
| `Rts.Simulation` | `Code/Simulation/` | Any | `Unity.Collections`, `Unity.Mathematics`, `Unity.Burst` (see "Package references") | All unit state and everything that reads or writes it: unit arrays and handles, the command queue, the spatial grid, every per-unit Burst job (movement, unit AI and targeting, selection, filling draw data) and the faction summaries |
| `Rts.CommanderAI` | `Code/CommanderAI/` | Any | `Rts.Simulation`, `Unity.Collections`, `Unity.Mathematics` | The commander AI (ADR 0004): plain C# that reads its faction's summaries and gives orders through the command queue |
| `Rts.Presentation` | `Code/Presentation/` | Any | `Rts.Simulation`, `Rts.CommanderAI`, `Unity.Collections`, `Unity.Mathematics`, and the manifest packages it needs, today `Unity.InputSystem` and `Unity.RenderPipelines.Universal.Runtime` | Everything Unity-facing: the match driver MonoBehaviour that ticks the simulation and the commander, input, the camera, the `Graphics.RenderMeshInstanced` calls, UI, the debug overlay, match setup and the ladder harness |
| `Rts.EditorTools` | `Code/EditorTools/` | Editor only | The three runtime assemblies | Inspectors, menu items, and build and ladder-run scripts |
| `Rts.Tests.EditMode` | `Code/Tests/EditMode/` | Editor only | The four assemblies above, plus the test references | Tests that run jobs and logic without a scene (ADR 0004) |
| `Rts.Tests.PlayMode` | `Code/Tests/PlayMode/` | Any | The three runtime assemblies, plus the test references | Tests that need Play mode, such as the driver ticking in a scene |

The draft core loop (PR #74, not yet approved) lists twelve sandbox systems. As a planning hint only, they fit as subfolders, not as new assemblies:
- the AI commander goes in `Rts.CommanderAI`
- the camera, selection input, UI and match setup go in `Rts.Presentation`
- the rules and per-unit state of the rest (movement, pathing, orders, combat, unit behaviour, fog, economy, buildings, production, victory) go in `Rts.Simulation`

**Reference rules**
1. **Only the references in the table.** References point one way: tests and `Rts.EditorTools` toward the runtime assemblies, and those toward `Rts.Simulation`. No runtime assembly references `Rts.EditorTools` or a test assembly. Unity doesn't allow "Cyclical references, which is when two assemblies reference each other" ([referencing][asm-ref]).
2. **Simulation stands alone.** It references no other `Rts` assembly and no input, rendering or UI package, so it builds and tests without them.
3. **Unit state is `internal` to `Rts.Simulation`.** The unit arrays, the grid and the job structs are `internal`. Other assemblies use only Simulation's public API, for example to schedule and complete a tick, queue orders, read faction summaries and draw data, run a selection query, or read one unit for the debug overlay.
   - So the compiler stops code outside Simulation from looping over units (ADR 0004 rule 2), and stops the commander from reading the unit arrays (rule 7).
   - The AI ADR (ADR 0004 follow-up 8) sets which public calls the commander may use. Until then, reviewers check it.
4. **`InternalsVisibleTo` names only the two test assemblies.** It goes in one `AssemblyInfo.cs` at the root of the assembly's folder.
5. **Simulation has no MonoBehaviour or ScriptableObject types.** It takes plain structs. A ScriptableObject class lives in the assembly that reads it, and that assembly copies the values into Simulation at match start. The compiler can't enforce this rule: Simulation needs `NativeArray` and the job system, which live in `UnityEngine.CoreModule` ([NativeArray][nativearray]), so it keeps its engine references. Reviewers check it.
6. **Runtime assemblies never use `UnityEditor`,** not even under `#if UNITY_EDITOR`. The ladder measures Player builds (ADR 0004), and Editor-only branches would make the Editor behave differently from what is measured.

**Package references**
- An assembly references a package only when that package is in `Game/Packages/manifest.json` and approved for direct use (AGENTS.md §5). Today that covers the Input System, URP's runtime and the Test Framework.
- Burst, Collections and Mathematics are in `packages-lock.json` only because URP depends on them. No assembly references them until ADR 0004's install Issue (its follow-up 2) adds them to the manifest. That Issue waits for owner decision 1, which Burst needs anyway.
- `Unity.RenderPipelines.Core.Runtime` comes from `com.unity.render-pipelines.core` 17.3.0. That package is in the lock only through URP and isn't approved for direct use (AGENTS.md §5). Presentation doesn't reference it unless a later ADR approves it. The rendering ADR (ADR 0004 follow-up 9) is the natural place.
- No precompiled DLL references, except `nunit.framework.dll` in the test assemblies.

**Settings for each assembly definition**
- **Create it in Unity,** so Unity writes its `.meta` (AGENTS.md §4). Use Assets > Create > Scripting > Assembly Definition, or Assets > Create > Testing > Test Assembly Folder for a test assembly ([test assembly][test-assembly]).
- **Name the file `<Name>.asmdef`.** "Changing the file name of the assembly definition file doesn't affect the value of the **Name** property" ([test assembly][test-assembly]), so set both.
- **Name and Root Namespace:** the assembly's name from the table. Root Namespace is "The default namespace for scripts in this assembly definition" ([inspector][asm-inspector]).
- **Auto Referenced: off.** "The predefined assemblies can only use code in auto-referenced assemblies" ([referencing][asm-ref]). So a script left outside `Code/` can't use game code, and it fails to compile instead of quietly joining it.
- **Allow 'unsafe' Code: off.** Raw pointers aren't covered by the job safety checks that ADR 0004 rule 10 keeps on. Turning it on needs a new ADR.
- **References by name, not GUID** (Use GUIDs off). Unity accepts either form, but "You must use the same form for all references in the list" ([file format][asm-format]). Names let a reviewer check the table above straight from the diff. The cost is that renaming an assembly means editing its references by hand, and a rename needs a superseding ADR anyway.
- **Platforms** as in the table. "Editor only" means only the Editor platform is included.
- **Test assemblies keep what the Test Runner generates,** including the references to `UnityEngine.TestRunner`, `UnityEditor.TestRunner` and `nunit.framework.dll`. "This combination of references is what identifies an assembly as a test assembly" ([test assembly][test-assembly]).
  - `Rts.Tests.PlayMode` switches to Any Platform, which "makes it possible to run any Play mode tests in the assembly on standalone Players" ([test assembly][test-assembly]).
  - Test code never ships, because test assemblies are "excluded from a standard project build" ([creating assemblies][asm-create]).
- **Everything else** stays at Unity's defaults.

### Namespaces

- **The root namespace is `Rts`.** The game has no title yet, and a neutral code name means a later title never forces a rename. No package in the project uses it (checked in `Game/Library/PackageCache/`). PascalCase follows the .NET namespace guidelines ([namespace names][ms-ns]).
- **A file's namespace is `Rts.` plus its folder path below `Code/`,** with `/` replaced by `.`. For example:
  - `Code/Simulation/Movement/SeparationJob.cs` is in `Rts.Simulation.Movement`.
  - `Code/Tests/EditMode/Simulation/Movement/SeparationJobTests.cs` is in `Rts.Tests.EditMode.Simulation.Movement`.
- **Only the six folders in the table have an assembly definition.** A new assembly takes in the scripts in its folder "plus those in any subfolders that don't have their own Assembly Definition or Reference file" ([creating assemblies][asm-create]), so every subfolder belongs to the assembly above it. `Code/` and `Code/Tests/` hold no scripts.
- **Tests mirror what they test.** Tests for `Code/Simulation/Movement/` go in `Code/Tests/EditMode/Simulation/Movement/` or the PlayMode equivalent.
- **No folder named after a type or namespace the code uses,** such as `Editor`, `Input`, `Camera`, `Debug`, `Random`, `Time`, `Graphics`, `Physics`, `Unity` or `System`.
  - Why: C# looks a name up in each enclosing namespace before it checks the `using` directives at the top of the file ([C# spec §7.7.1][cs-names]). So a folder `Code/Presentation/Camera/` would turn every `Camera.main` in `Rts.Presentation` into a compile error. A namespace `Rts.Editor` would do the same to `: Editor` in inspector code, which is why the editor assembly is `EditorTools`.
  - Use a plural or a more specific name instead (`Cameras`, `PlayerInput`). .NET also advises: "CONSIDER using plural namespace names where appropriate" ([namespace names][ms-ns]).
- **No type named after a namespace it's in,** for example no class `Simulation` in `Rts.Simulation`. .NET says: "DO NOT use the same name for a namespace and a type in that namespace" ([namespace names][ms-ns]).

### Test package

- Tests use the **Unity Test Framework, `com.unity.test-framework` 1.6.0. It is already in `manifest.json`,** so no Issue is needed to add it. ADR 0004 records its license, the Unity Companion License.
- **NUnit** comes from `com.unity.ext.nunit` 2.0.5, which is in the lock only because Test Framework depends on it. "Unity Test Framework tests must be in a test assembly, which is any assembly that references NUnit" ([test assembly][test-assembly]).
  - ADR 0004 approved that direct use with effect from its owner decision 1, and it keeps the package out of the manifest.
  - **So neither test assembly is created until that handbook change merges.** This ADR doesn't decide that question.
- **`com.unity.test-framework.performance` isn't used.** ADR 0004 doesn't approve it, and the ladder measures with the FrameTimingManager.

### Existing items at the top of `Game/Assets/`

Every move and removal happens in a later Issue (see Consequences).

| Item | What it is | Decision |
|---|---|---|
| `Readme.asset` | The URP template's tutorial page | Removed, in the cleanup Issue |
| `TutorialInfo/` | The readme's scripts, a window layout and an icon. `ReadmeEditor.cs` is `[InitializeOnLoad]` and selects the readme when the Editor starts. The scripts compile into Unity's predefined assemblies, outside this layout. | Removed with `Readme.asset`, in the cleanup Issue |
| `InputSystem_Actions.inputactions` | The project-wide input actions, with the template's default bindings. `EditorBuildSettings.asset` points at it by GUID. | Moves to `Settings/`, in the cleanup Issue. Move it in the Unity Editor so its GUID and that reference stay. Then check that Edit > Project Settings > Input System Package still shows it as the project-wide actions ([project-wide actions, Input System 1.18 manual][pwa]). |
| `Scenes/` | Holds `SampleScene.unity`, the only scene in Build Settings | The folder stays. `SampleScene.unity` is removed once `Sandbox.unity` replaces it in Build Settings, so the build always has a scene. That happens in the sandbox Issue (ADR 0004 follow-up 5) or a later one. |
| `Settings/` | The URP assets that `GraphicsSettings` and `QualitySettings` use, URP global settings and volume profiles | Stays, unchanged. `SampleSceneProfile.asset` stays despite its name, because both URP assets use it as their volume profile. The `Mobile` quality level's assets stay too: removing them changes quality settings, which means a ladder re-run (ADR 0004). |

## Alternatives considered

**Folder layout**
- **(A) Type folders at the root (chosen).**
  - Unity's guide shows layouts "based on splitting up your project by asset type" ([organizing][organizing]).
  - `Scenes/` and `Settings/`, which Project Settings point at, stay where they are.
  - Folder rule 1 keeps our content apart from third-party content: anything at the top that isn't in the fixed list isn't ours.
- **(B) One wrapper folder,** for example `Game/Assets/_Project/`, around the same folders.
  - Unity's guide says "Keep your internal assets separate from third-party ones" ([organizing][organizing]), and a wrapper does that by location.
  - *Not chosen:* AGENTS.md §5 already keeps third-party content rare and owner-approved, and Unity packages live outside `Assets/`. So the wrapper would add a folder level to every path, and move `Scenes/` and `Settings/`, for little gain.
  - It is cheap to adopt later: namespaces follow `Code/`, so moving it under a wrapper changes no namespace.
- **(C) Feature folders at the root,** for example `Movement/` with its own code, prefabs and data.
  - *Not chosen:* ADR 0004 runs every per-unit system on one set of arrays and one job chain, so features don't separate into code of their own. Scenes, settings and materials are shared too.
  - Features become subfolders instead (folder rule 2).
- **(D) Game code as embedded packages under `Game/Packages/`,** using Unity's package layout: `Runtime/`, `Editor/` and `Tests/`, with one assembly definition each ([package layout][pkg-layout]).
  - *Not chosen:* AGENTS.md §2 puts C# scripts in `Game/Assets/`. Every code change would also sit beside `manifest.json` and `packages-lock.json`, which are in the security perimeter (AGENTS.md §6).
  - This ADR borrows that layout's split into runtime, editor and test assemblies.

**Assemblies**
- **(1) One runtime assembly.** It has the fewest files, but every boundary rests on review. Nothing would stop main-thread code from looping over units, or the commander from reading the unit arrays.
- **(2) Three runtime assemblies in layers (chosen).** The compiler enforces the direction of dependencies and keeps unit state internal (reference rules 1 to 3). It may also speed up the Editor: in the default setup, "Every time you change one script, Unity has to recompile all the other scripts" ([assemblies][asm-intro]). That speed-up hasn't been measured here.
- **(3) One assembly per sandbox system.**
  - The systems share the unit arrays and the job chain, so the arrays would have to be public across about a dozen assemblies. That loses the guard that (2) gives.
  - Assemblies can't reference each other in a cycle ([referencing][asm-ref]), so systems that call each other would need extra layers.
  - Systems become subfolders instead.

## Consequences

- **Benefits.**
  - Every file has one home, so parallel worktrees rarely touch the same folders.
  - The compiler enforces the direction of dependencies, keeps Simulation independent, and enforces part of ADR 0004 rules 2 and 7.
  - Test code can't ship.
  - Names are fixed before any code exists.
- **Tradeoffs.**
  - Presentation and the commander get only what Simulation's public API offers, so each new need is an explicit addition to that API.
  - Six assembly definitions have to match the table, and reviewers check them.
  - References by name need hand edits if an assembly is renamed.
- **Cost at thousands of units.** None expected at runtime: the split changes which assembly code compiles into, not what code runs. This hasn't been measured. The ladder (ADR 0004) would show any change.
- **Risks.**
  - No test assembly can exist until ADR 0004's owner decision 1 merges, so code PRs before then can't meet AGENTS.md §8's "new or updated tests". ADR 0004 already flags this. The Producer sequences the work around it.
  - Moving the input actions outside the Editor could leave the project-wide actions unassigned. The check in the cleanup Issue covers this.
  - Reference rule 3 puts the draw-data and selection jobs in Simulation. If the rendering ADR needs more (animation, the BatchRendererGroup), it may add an assembly by superseding this ADR.
- **Reversibility.** Folders are cheap to move in the Editor. Because namespaces follow `Code/`, moving top-level folders changes no code. Renaming an assembly or a namespace segment touches every file in it, so it needs a superseding ADR.
- **AGENTS.md §2.** This is the ADR §2 refers to. Once it merges, the folder list here replaces "ask before creating a new top-level folder", and a new top-level folder needs a superseding ADR (folder rule 1). No handbook change is needed.
- **ADR 0004.** This ADR is ADR 0004's follow-up 3. It doesn't supersede ADR 0004.
- **Owner decisions.** None new. ADR 0004's owner decision 1 still gates the test assemblies and Burst.
- **Follow-up Issues:**
  1. **Cleanup** (Standard; changes `Game/`). Remove `Readme.asset` and `TutorialInfo/`, and move the input actions into `Settings/` with the check above.
  2. **The sandbox Issue** (ADR 0004 follow-up 5), or one after it:
     - creates `Sandbox.unity`, puts it in Build Settings in place of `SampleScene.unity`, and removes `SampleScene.unity`. The PR calls out the `EditorBuildSettings.asset` change (AGENTS.md §2).
     - creates the runtime and editor assembly definitions it needs, each with its first scripts.
  3. **After owner decision 1's handbook change merges:** create the two test assemblies. Suggested first tests:
     - one that fails if any type in `Rts.Simulation` derives from `UnityEngine.Object` (reference rule 5)
     - one that checks each script's namespace against its folder
  4. **Suggested:** add the folder, assembly and naming rules to the gameplay-programmer and code-reviewer role files, together with ADR 0004's follow-up 11. These are protected paths, so that Issue must name them.

[organizing]: https://unity.com/how-to/organizing-your-project
[special]: https://docs.unity3d.com/6000.3/Documentation/Manual/SpecialFolders.html
[asm-intro]: https://docs.unity.com/en-us/engine/6000.3/manual/programming-environment/script-compilation/assembly-definition-files/assembly-definitions-intro
[asm-create]: https://docs.unity.com/en-us/engine/6000.3/manual/programming-environment/script-compilation/assembly-definition-files/assembly-definitions-creating
[asm-ref]: https://docs.unity3d.com/6000.3/Documentation/Manual/assembly-definitions-referencing.html
[asm-inspector]: https://docs.unity3d.com/6000.3/Documentation/Manual/class-AssemblyDefinitionImporter.html
[asm-format]: https://docs.unity3d.com/6000.3/Documentation/Manual/assembly-definition-file-format.html
[pkg-layout]: https://docs.unity3d.com/6000.3/Documentation/Manual/cus-layout.html
[test-assembly]: https://docs.unity.com/en-us/engine/6000.3/manual/programming-environment/test-framework-introduction/getting-started/workflow-create-test-assembly
[nativearray]: https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Unity.Collections.NativeArray_1.html
[pwa]: https://docs.unity3d.com/Packages/com.unity.inputsystem@1.18/manual/ProjectWideActions.html
[ms-ns]: https://learn.microsoft.com/en-us/dotnet/standard/design-guidelines/names-of-namespaces
[cs-names]: https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/language-specification/basic-concepts#771-general
