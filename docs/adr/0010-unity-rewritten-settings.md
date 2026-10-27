# ADR 0010: Settings files Unity rewrites on its own

## Status

Accepted

## Date

2026-10-26

## Context

Unity changes some tracked settings files without being asked: when someone opens Build Profiles, builds a Player, or runs the EditMode tests with an empty `Library/`. The changes show up in `git status` as edits nobody made, and they can slip into an unrelated PR. Issue #55 asks how the studio handles them.

**What has been seen,** with Unity 6000.3.23f1 and URP 17.3.0:

| File | What Unity changed | When |
|---|---|---|
| `Game/Assets/Settings/PC_RPAsset.asset` | URP shader prefiltering data | The owner's build and Build Profiles ([PR #54][pr54], 2026-10-22). QA's sandbox Player builds through `SandboxSceneBuilder.BuildSelectionPlayers` ([PR #114][pr114], 2026-10-26, commit 5ecf0ea) |
| `Game/Assets/Settings/UniversalRenderPipelineGlobalSettings.asset` | A format refresh | As above |
| `Game/Assets/Settings/DefaultVolumeProfile.asset` | A format refresh | As above |
| `Game/ProjectSettings/ProjectSettings.asset` | A `Standalone` batching entry | As above |
| `Game/ProjectSettings/UnityConnectSettings.asset` | `m_Enabled` from 0 to 1 | As above |
| `Game/Assets/Settings/Mobile_RPAsset.asset` | URP asset version 12 to 13 | A cold batch EditMode run (PR #114 QA). [PR #112][pr112] saw the file grow from 4,345 to 4,650 bytes on first import |

In #25 the first five changes were discarded with the owner's OK. On PR #114, QA restored all six by hand after each run.

**Why Unity writes them.** The links below go to Unity's public Graphics repository for 6000.3, which may be ahead of 17.3.0. Every quoted line was checked against the 17.3.0 package the project resolves.
- **URP asset upgrades.** When URP loads a URP asset older than version 13, it raises the version in memory, finishes the upgrade in a delayed call and marks the asset dirty (`OnAfterDeserialize` and `UpgradeAsset`, [URP asset source][urp-asset-src]). In 17.3.0 the step to version 13 changes only the two version fields. The rest of the growth matches fields that `PC_RPAsset.asset`, already at version 13, has and `Mobile_RPAsset.asset` lacks, such as `m_ReflectionProbeAtlas` and `m_PrefilterReflectionProbeAtlas`. The next save writes the file. `AssetDatabase.SaveAssets` "Writes all unsaved asset changes to disk" ([SaveAssets][save-assets]), and the builder tests call it on purpose, to flush Unity's work before taking a snapshot (PR #112).
- **Prefiltering.** At the start of every Player build, URP recomputes the prefiltering fields (`m_Prefilter*` and `m_PrefilteringMode*`) of each URP asset in the build. It works them out from that asset, its renderers and the Graphics settings, then saves the asset under the comment "Save the asset before build" ([URP build preprocessor][urp-preprocessor]). The build reads these fields through Unity's keyword filter attributes, which act on fields of "a RenderPipelineAsset referenced by QualitySettings" ([FilterAttribute][keyword-filter]). The fields exist only in the Editor, because their source file is wrapped in `#if UNITY_EDITOR` ([source][urp-prefilter-src]).
- **Which URP assets a Windows build uses.** URP includes the default render pipeline asset and the assets of the quality levels for the build target ([URP stripping][urp-strip]). `QualitySettings.asset` excludes the Mobile level from Standalone, so Windows builds use only `PC_RPAsset.asset`.
- **Player settings.** `PerformanceLadderBuilder.BuildWindows` resets `PlayerSettings` in memory after its builds, because "PlayerSettings also saves its in-memory batching changes on shutdown".
- **Global settings and the volume profile.** The URP global settings asset is already at its current version (10), so its change isn't a version upgrade. PR #54 recorded only "format refresh", not which fields changed.
- **Unity services.** Nobody knows what turns on the top-level `m_Enabled` in `UnityConnectSettings.asset`. `ProjectSettings.asset` links the project to a Unity Cloud project (a cloud project ID is set), and that link may play a part. Nobody has measured it.

**What's handled today.**
- `SandboxSceneBuilder.Build()` limits its scene save to the scene. It restores the checkout bytes of its outputs when only line endings differ, and it restores `SceneTemplateSettings.json`. Its tests check that a repeat run leaves every file in `Assets/` and `ProjectSettings/` byte-identical (PR #112).
- `PerformanceLadderBuilder.BuildWindows()` restores every file under `Assets/`, `Packages/` and `ProjectSettings/` after its two Player builds, and resets `PlayerSettings` in memory. Its comment cites #55.
- `SandboxSceneBuilder`'s Player builds (`BuildWindows`, `BuildWindowsDevelopment` and `BuildSelectionPlayers`) restore nothing. QA's five build-time changes came from them.
- Nothing handles the upgrade on first import, or what the owner's Editor writes in the main checkout.
- `tools/unit-ladder.js` won't measure unless `git status --porcelain` is empty. So a leftover rewrite blocks the ladder until someone restores it by hand.

**Constraints.**
- Unity 6000.3.23f1 and URP 17.3.0 stay as they are (ADR 0001, AGENTS.md §4). Nothing here adds, upgrades or removes a package.
- Builders run in batch mode make every asset, and nobody hand-edits `.asset` YAML ([ADR 0006, rules 3 to 5][adr6-rules]). So anything committed must be written by Unity.
- Each Unity Issue lists the assets it changes, and the Producer doesn't start Issues whose lists overlap ([Unity work][workflow-unity]). A file that Unity rewrites behind every Issue's back breaks that rule.
- Any change to quality, graphics or Player settings triggers a full ladder ([ADR 0004, "When to re-run"][adr4-ladder], item 3).
- Every QA check runs in a fresh worktree with an empty `Library/` (`docs/studio/workflow.md`, "Unity cache"), so first-import effects hit every check.
- `.gitignore` is in the security perimeter (AGENTS.md §6).

**Evidence gaps.**
1. Nobody recorded which fields changed in the four build-time files.
2. Nobody has checked that Unity writes the same bytes again on a second build. The source suggests it does. Every prefiltering input is a setting. The one input that differs between development and release builds is debug display stripping, and it doesn't differ here: `s_StripDebugDisplayShaders = !isDevelopmentBuild || shaderStrippingSettings.stripRuntimeDebugShaders` ([source][urp-preprocessor]), and `m_StripRuntimeDebugShaders` is 1 in the global settings.
3. Nobody knows what turns on `m_Enabled`, or whether a Player built while it is on behaves any differently.

The follow-ups measure the first two, and what turns on `m_Enabled`, as part of their work, and each has a fallback if the answer is bad. A separate spike would repeat the same Unity runs, so none is proposed. Whether a Player built with the flag on behaves differently stays unmeasured. Finding out needs a build with `m_Enabled` at 1, which needs a separate owner decision (follow-up 1).

## Decision

**Commit Unity's own version of five files once, through a builder. Keep `UnityConnectSettings.asset` off and out of every commit. Restore any rewrite that a PR's Issue doesn't list.**

1. **Settle once.** A new builder, `Settle()`, makes Unity write its own version of five files, and one Issue commits the result (follow-up 2). After that, a first import, the tests and the Player builds have nothing left to write. Unity provides [ForceReserializeAssets][force-reserialize] for exactly this: it lets you "load, upgrade, and write back to disk" the files you choose, rather than have upgrades "'mixed in' with deliberate changes".
2. **Player builds leave no trace.** Every Player build goes through one helper. It keeps `UnityConnectSettings` off and, after the build, puts back the bytes of every tracked file, as the ladder builder does today (follow-up 1).
3. **One rule for whatever still shows up.** A change to one of these six files that your Issue doesn't list is never committed. Restore the file (see below).

### Each file

| File | Decision | Committed by | When it shows up and your Issue doesn't list it |
|---|---|---|---|
| `Mobile_RPAsset.asset` | Commit once: the version 13 file that URP 17.3.0 writes. Windows builds don't use it (Context) | Follow-up 2 | Restore it. Once follow-up 2 has merged, also report it as unexpected |
| `UniversalRenderPipelineGlobalSettings.asset` | Commit once: the file Unity writes | Follow-up 2 | As above |
| `DefaultVolumeProfile.asset` | Commit once: the file Unity writes | Follow-up 2 | As above |
| `PC_RPAsset.asset` | Commit once: the prefiltering values our builds compute. Later, a PR that changes URP or Graphics settings commits the recomputed values along with its own change (Editor rule 4) | Follow-up 2. Later, the PR that changes the settings | As above |
| `ProjectSettings.asset` | Commit once: the `Standalone` batching entry, which records the values builds already use. The PR calls out the change (AGENTS.md §2) | Follow-up 2 | As above |
| `UnityConnectSettings.asset` | Never committed. `m_Enabled` stays 0 unless the owner decides otherwise through a superseding ADR | Nobody | Always restore it. Unity is expected to do this, so there's nothing to report |

**Review for follow-up 2.** It changes `Game/`, so it is at least Standard tier:
- an Approve from the other vendor
- a `QA: pass` from a fresh worktree
- a full ladder from both the author and QA, because graphics and Player settings files change (ADR 0004)

The Producer may raise the tier.

**If a file doesn't settle,** meaning a second `Settle()` run changes it again, follow-up 2 leaves it out, and its PR says so. That file then follows the `UnityConnectSettings.asset` row: it's never committed, and it's restored after every build and whenever anyone sees the change. The check tool (follow-up 3) lists it.

### How `UnityConnectSettings.asset` stays off

- **Builds.** The build helper sets `m_Enabled` back to 0 in memory before every Player build. `Settle()` does the same. Unity can still save settings at shutdown, and code running inside Unity can't check the file after that. So once Unity has exited, the agent or tool that started the batch run checks the file with `git status` or the check tool (follow-up 3), and restores it if it changed. This applies to `Settle()` runs too.
- **Commits.** An EditMode test fails unless every `m_Enabled` in the file is 0, as committed today. A PR that changes `Game/` can't merge without a `QA: pass` (AGENTS.md §6), so a committed flip can't merge.
- **Review.** Reviewers request changes on any PR that changes the file, unless an accepted ADR records the owner's decision to turn services on.
- **Working trees.** Everyone restores it whenever they see it changed (below).

### When a rewrite shows up in a working tree

The rule is the same for everyone. **A change to one of the six files that your Issue doesn't list is never committed. Restore the file to its committed version.** An Issue that does list one of them makes the change through a builder (`Settle()` for the parts Unity writes), and its PR calls the change out.

**How to restore.** For each such file:
1. Run `git status --short -- <path>`. If the file is staged (a letter in the first column) and you didn't stage it, stop and report it. Don't restore or commit it.
2. Run `git restore --source=HEAD --staged --worktree -- <path>`. This puts the committed version back in both the index and the working tree. Plain `git restore -- <path>` isn't enough: with neither option "the working tree is restored", from the index, so a staged rewrite stays staged and could be committed ([git restore][git-restore]).
3. Check that `git status --short -- <path>` now prints nothing.

If `git restore` doesn't work in the Codex sandbox, list the paths in your report instead, and the Producer restores them (Risks).

| Who | What to do |
|---|---|
| gameplay-programmer, tech-artist | After your last Unity run, check `git status --short`. Restore each of the six files your Issue doesn't list, as above. Use the check tool once it exists (follow-up 3), and the steps above until then. List what you restored in your report, citing "ADR 0010". If any other file changed that you didn't mean to change, stop and report it (ADR 0006). |
| qa-test-engineer | Do the same before posting the QA record, and list what you restored in the record. A restored file isn't a QA failure. Once follow-up 2 has merged, report any restored file that follow-up 2 committed as a bug outside the Issue (`docs/studio/workflow.md`, QA step 3). That excludes `UnityConnectSettings.asset` and any file follow-up 2 left out because it didn't settle. |
| Producer | Before committing, in a launcher worktree or the main checkout, run `git status --short`. Restore each of the six files the Issue doesn't list with the steps above, and never stage them. Name the restored files in the session report, and once follow-up 2 has merged, flag any that follow-up 2 committed. If one of them is staged and you didn't stage it, or anything else changed unexpectedly, stop and ask the owner. |
| code-reviewer, Codex reviewer | Request changes when a PR changes one of the six files and its Issue doesn't list that file, or when the change doesn't come from a builder. |
| Owner | Nothing. If you open the main checkout in the Editor, Unity may rewrite these files. Close the Editor before the Producer's sitting ([ADR 0006][adr6-owner]), and the Producer restores them. To change one of these settings on purpose, ask the Producer for an Issue. |

### Rules for Editor code

These apply to the gameplay-programmer, tech-artist and qa-test-engineer, and to the reviewers of their PRs.

1. **Builders save only what they own.** Call `AssetDatabase.SaveAssetIfDirty` on owned assets, and limit scene saves to the scene, as `SandboxSceneBuilder` does. A builder never calls `AssetDatabase.SaveAssets()`, because it writes every dirty asset, Unity's pending upgrades included. `Settle()` is the one exception. Tests may call it, as PR #112's do, to flush Unity's work before taking a snapshot.
2. **Every Player build goes through the build helper** (follow-up 1). Nothing else calls `BuildPipeline.BuildPlayer`. The helper has one option, used only by `Settle()`, that keeps what the build writes, except in `UnityConnectSettings.asset`.
3. **Only `Settle()` writes the parts Unity owns:** the prefiltering fields, the batching entry, the URP asset version and the format of these files. No other code sets them. Only the build helper touches `UnityConnectSettings`, and only to keep `m_Enabled` at 0.
4. **Run `Settle()` after a settings change.** This applies to an Issue that changes:
   - a URP asset, renderer or volume profile
   - the Graphics, Quality or Player settings
   - the Unity version or a package version (each through an ADR)

   That Issue runs `Settle()` after its own builder, commits what `Settle()` writes, and lists those files. QA runs `Settle()` again on the PR head and expects a clean `git status`. A stale committed value doesn't affect builds, because URP recomputes it, but it would show up as a rewrite in the next PR.
5. **A new rewrite gets an Issue, not a workaround.** If Unity starts rewriting another file, report it. Adding a file to `Settle()` or to the restore list needs an Issue that names it.

### Performance

- **This decision costs nothing per frame.** Committing, restoring or discarding these bytes changes no Player:
  - URP recomputes the prefiltering fields at the start of every build, and the build uses the new values. So the committed values never decide which shader variants a Player keeps, and the fields aren't in the Player at all.
  - Static and dynamic batching act only on Mesh Renderers: batching "supports only Mesh Renderers" ([batching][batching]). Units are drawn with `Graphics.RenderMeshInstanced` (ADR 0004 rule 9), and dynamic batching is off in `PC_RPAsset.asset` (`m_SupportsDynamicBatching: 0`). The batching entry records values that builds already use.
  - Version and format refreshes change how a file is written, not the settings in it. Windows builds don't use `Mobile_RPAsset.asset` anyway.
- **What prefiltering does.** It is URP's first cut of shader variants, based on the features that the URP asset and its renderers turn on. The Editor log shows it for each shader as "After settings filtering" ([variant count][variant-count]). Unity says many variants mean "increased build times, file sizes, runtime memory usage, and loading times", and can cause "shader compilation stutters" ([shader variants][variants]). Prefiltering removes only variants for features that are off, so it doesn't change the variant the GPU runs for any draw. At 2,000 units we expect no change to CPU time, GPU time or allocations per frame. Prefiltering affects build time, Player size, shader memory and stutters when a shader is first used.
- **Keep the strip settings on.** All four are on today in the URP global settings: Strip Unused Variants, Strip Unused Post Processing Variants, Strip Screen Coord Override Variants and Strip Runtime Debug Shaders.
  - With Strip Unused Variants off, URP updates every URP asset "so it doesn't prefilter any keywords" ([source][urp-preprocessor]).
  - Turning any of them off is a Graphics settings change. It belongs to the rendering ADR and needs a ladder (ADR 0004).
  - Strip Runtime Debug Shaders has a side effect: development Players also lose the Rendering Debugger's display variants.
- **Evidence.** None of this has been measured for our builds. Follow-up 2 runs a Windows build before and after settling, and records from the Editor log the total variant counts after settings filtering and after scriptable stripping. The two sets of counts should match. Follow-up 2 also runs the ladder, as ADR 0004 requires for a settings change.

## Alternatives considered

None of these options changes which code or shaders run. So none of them costs anything at thousands of units: there's no change to CPU, GPU, memory or allocations. They differ in agent effort, in what can slip into a PR, and in upkeep.

1. **Keep restoring by hand,** as we do today.
   - *How it works:* whoever sees a rewrite restores it, with the owner's OK each time (#25).
   - *Evidence:* QA restored the files by hand on every run for PR #114. PR #112 left `Mobile_RPAsset.asset` changed in the working tree. The ladder tool refuses a dirty tree, so every fresh QA worktree needs the hand step before a ladder.
   - *Complexity and risk:* there's nothing to build, but every Unity run carries a manual step. Whenever that step is forgotten, the rewrite lands in a PR, which is the problem #55 exists for. It's trivially reversible, because it's what we do now.
   - Not chosen.
2. **Restore after every Unity run, and never commit.** Extend the ladder builder's restore to every builder, test run and build, for example through a wrapper.
   - *Evidence:* it already works for the ladder's builds.
   - *Against it:*
     - The upgrade on first import comes back in every fresh worktree, and the tests save it by design. So the wrapper must also restore after tests.
     - The wrapper must tell a builder's intended output apart from Unity's rewrites, so it needs each Issue's asset list.
     - It can't reach the owner's Editor.
     - The upgrade is never finished. Unity's ForceReserializeAssets page describes exactly this drawback: upgrades "'mixed in' with deliberate changes" ([ForceReserializeAssets][force-reserialize]).
   - *Partly chosen:* Player builds still restore (part 2 of the decision), as a safety net. It would be easy to reverse.
3. **Hide the files from git.**
   - `.gitignore` doesn't work, because "Files already tracked by Git are not affected" ([gitignore][gitignore]). It's also in the security perimeter.
   - `assume-unchanged` and `skip-worktree`: Git's FAQ says these "don't work properly for this purpose and shouldn't be used this way" ([gitfaq][gitfaq]). They would also have to be set in every launcher worktree.
   - Untracking the files: Graphics and Quality settings point at the URP assets by GUID, so a fresh checkout would get different settings from everyone else. That would be hard to undo cleanly.
   - Not chosen.
4. **Let rewrites ride along** in whichever PR they appear in, with a note.
   - Reviewers can't tell Unity's changes from the author's. The files fall outside the Issue's asset list, and two parallel PRs would both change them. Issue #55 exists to stop exactly this.
   - Not chosen.
5. **Remove the Mobile quality level, with its URP asset and renderer,** leaving one file fewer.
   - It reverses ADR 0005's decision to keep them. It changes Quality settings, which needs a ladder. And it deals with only one of the six files.
   - Not chosen. The rendering ADR may revisit it.

## Consequences

- **Benefits.**
  - Rewrites stop at the source for first imports and builds. Fresh QA worktrees stay clean, and the ladder's clean-tree check stops tripping.
  - One mechanical rule covers every role. Follow-up 3 turns it into a command with an exit code, as QA suggested on PR #114.
  - A Unity or URP upgrade (ADR 0001) runs `Settle()`, so its rewrites land in its own PR.
- **Tradeoffs.**
  - There is one more builder and one build helper to maintain, and the settle PR costs a ladder.
  - Restoring after builds hides any new rewrite that a build makes. Such a rewrite surfaces when `Settle()` runs for a Unity upgrade, or in QA's check under Editor rule 4.
  - The list of six files lives in this ADR and in the check tool. Adding a file needs an Issue (Editor rule 5), and moving a file to another row needs a superseding ADR.
- **Risks.**
  - A file may not settle. The fallback is above.
  - URP finishes its asset upgrade in a delayed call. So re-serializing straight after loading could write a half-upgraded file, with `k_AssetVersion: 13` and `k_AssetPreviousVersion: 12`, which then changes again the next time ([URP asset source][urp-asset-src]). Follow-up 2's second-run check catches this.
  - Nobody has checked whether `git restore` works inside the Codex sandbox, by hand or through the check tool (follow-up 3). With `--staged` it also writes the git index. If it doesn't work, agents report the paths and the Producer restores them.
  - The owner's Editor may write something new. The Producer restores it and reports it.
- **Reversibility.** Easy. The settled files are ordinary commits, and the helper and the tool are small. A superseding ADR can move any file to another row.
- **Other ADRs.** This ADR adds to ADR 0006: `Settle()` is a builder under its rules 3 to 5. It supersedes nothing. ADR 0004's ladder rules and ADR 0005's decision to keep the Mobile assets still stand.
- **Dependencies.** None. `ForceReserializeAssets` is part of the Editor (ADR 0001), and the check tool uses only Node built-ins, as `tools/unit-ladder.js` does. None of the follow-ups changes a protected or security-perimeter path, except the suggested role-file change (follow-up 5).
- **Owner decisions.**
  1. **Settle:** decided by the owner in the Producer session on 2026-10-26. Unity's own version of the five files is committed once, in follow-up 2. *Reason:* it stops the rewrites at the source and changes no build.
  2. **A standing OK to restore:** decided by the owner in the Producer session on 2026-10-26. Anyone may restore these six files whenever their Issue doesn't list them, without asking each time. This replaces the one-off OK given in #25. *Reason:* the changes are Unity's rewrites, and a deliberate change always comes through an Issue that lists the file.
  3. **Unity services stay off:** `m_Enabled: 0`, as the owner asked in #55. Turning them on would need a superseding ADR.
- **Follow-up Issues,** in order:
  1. **The build helper and the `UnityConnectSettings` guard** (gameplay-programmer, Standard, QA).
     - One helper for Player builds, used by `SandboxSceneBuilder` and `PerformanceLadderBuilder`. Before the build, it snapshots the tracked files under `Assets/`, `Packages/` and `ProjectSettings/` and sets the in-memory `m_Enabled` to 0. After the build, it restores the bytes and resets `PlayerSettings` and `UnityConnectSettings` in memory, so Unity's shutdown save writes nothing. Start from the ladder builder's current code.
     - An EditMode test that every `m_Enabled` in `ProjectSettings/UnityConnectSettings.asset` is 0.
     - Asset list: `SandboxSceneBuilder.cs`, `PerformanceLadderBuilder.cs`, the new helper and test, and their `.meta` files.
     - Acceptance: starting from an empty `Library/`, running `BuildSelectionPlayers` and then the ladder build leaves `git status` clean. The one allowed exception is `Mobile_RPAsset.asset`, if the first import wrote it before a build; follow-up 2 fixes that.
     - The PR also records, as a measurement and not a pass or fail condition, the first step at which `m_Enabled` turns on in a fresh worktree: first import, EditMode tests, sandbox builds or ladder builds.
     - It makes no build with `m_Enabled` at 1, so it doesn't measure whether the flag reaches the Player. The `UnityConnectSettings.asset` row and Editor rule 3 keep the flag at 0. That question can be studied only after a separate owner decision authorises such a build, through a superseding ADR. If that study finds the flag does reach the Player, its Issue also runs the ladder, because the helper's builds would then differ from earlier ones.
  2. **Settle** (gameplay-programmer, Standard, QA and ladder), after follow-up 1.
     - A builder, `Settle()`, in `Code/EditorTools/` that makes Unity write the five files and nothing else. For example, it could:
       - re-serialize the three Unity reformats with `ForceReserializeAssets`, for the assets only, so the `.meta` files are left alone
       - build a Windows Player into a throwaway folder under `Builds/`, through the helper's keep option
       - save pending changes

       It throws if any other tracked file changed.
     - Asset list: the five files, the builder and its `.meta` file. The PR calls out the `ProjectSettings.asset` change, and summarizes which fields changed in each file (evidence gap 1).
     - Acceptance: in a fresh worktree with an empty `Library/`, on the PR head, running the EditMode tests, `Settle()`, `BuildSelectionPlayers` and the ladder build leaves `git status` clean. A second `Settle()` run changes nothing (evidence gap 2).
     - The PR records the variant counts (see Performance) and the ladder entry.
  3. **The check tool,** `tools/unity-tree-check.js`, with `node --test` tests (gameplay-programmer, Standard, Node built-ins only), after follow-up 2.
     - It lists changed and untracked files under `Game/Assets/`, `Game/Packages/` and `Game/ProjectSettings/`, using `git status --porcelain`.
     - It takes the Issue's asset list as `--allow <path>` arguments.
     - It restores each of the six files that isn't allowed in both the index and the working tree, with `git restore --source=HEAD --staged --worktree -- <path>`. Then it checks that `git status --porcelain -- <path>` prints nothing, and exits with 1 if it doesn't. It prints what it restored and marks settled files as unexpected.
     - It never restores one of these files that is already staged, because it can't tell who staged it. It names the file and exits with 1, and the agent follows the [restore steps](#when-a-rewrite-shows-up-in-a-working-tree).
     - It exits with 1 if any other file changed that isn't allowed. It never restores anything under `Game/Packages/`, because `manifest.json` and `packages-lock.json` are in the security perimeter. A change there stops the run.
     - Agents and QA run it after their last Unity run. The Producer can't run scripts (`docs/studio/claude-staff.md`), so it keeps following the [restore steps](#when-a-rewrite-shows-up-in-a-working-tree) by hand.
  4. **`docs/studio/workflow.md`** (Producer): add a line to "Unity work" that points to this ADR's rule for rewrites.
  5. **Suggested:** add the restore rule and the Editor-code rules to these role files:
     - `.codex/agents/gameplay-programmer.toml`
     - `.codex/agents/tech-artist.toml`
     - `.codex/agents/qa-test-engineer.toml`
     - `.codex/agents/reviewer.toml`
     - `.claude/agents/code-reviewer.md`
     - `.claude/agents/producer.md`

     These are protected paths, so that Issue must name them (Critical tier, security perimeter). It can go together with ADR 0006's follow-up 6.

[pr54]: https://github.com/smokim38/rts-game/pull/54
[pr112]: https://github.com/smokim38/rts-game/pull/112
[pr114]: https://github.com/smokim38/rts-game/pull/114
[adr6-rules]: 0006-unity-editor-automation.md#rules-for-agents-working-with-unity
[adr6-owner]: 0006-unity-editor-automation.md#what-the-owner-does
[adr4-ladder]: 0004-unit-simulation-architecture.md#performance-ladder
[workflow-unity]: ../studio/workflow.md#unity-work
[save-assets]: https://docs.unity3d.com/6000.3/Documentation/ScriptReference/AssetDatabase.SaveAssets.html
[force-reserialize]: https://docs.unity3d.com/6000.3/Documentation/ScriptReference/AssetDatabase.ForceReserializeAssets.html
[keyword-filter]: https://docs.unity3d.com/6000.3/Documentation/ScriptReference/ShaderKeywordFilter.FilterAttribute.html
[urp-strip]: https://docs.unity3d.com/6000.3/Documentation/Manual/urp/shader-stripping-features.html
[variants]: https://docs.unity3d.com/6000.3/Documentation/Manual/shader-variants.html
[variant-count]: https://docs.unity3d.com/6000.3/Documentation/Manual/shader-how-many-variants.html
[batching]: https://docs.unity3d.com/6000.3/Documentation/Manual/DrawCallBatching.html
[urp-preprocessor]: https://github.com/Unity-Technologies/Graphics/blob/6000.3/staging/Packages/com.unity.render-pipelines.universal/Editor/ShaderBuildPreprocessor.cs
[urp-asset-src]: https://github.com/Unity-Technologies/Graphics/blob/6000.3/staging/Packages/com.unity.render-pipelines.universal/Runtime/Data/UniversalRenderPipelineAsset.cs
[urp-prefilter-src]: https://github.com/Unity-Technologies/Graphics/blob/6000.3/staging/Packages/com.unity.render-pipelines.universal/Runtime/Data/UniversalRenderPipelineAssetPrefiltering.cs
[gitignore]: https://git-scm.com/docs/gitignore
[gitfaq]: https://git-scm.com/docs/gitfaq
[git-restore]: https://git-scm.com/docs/git-restore
