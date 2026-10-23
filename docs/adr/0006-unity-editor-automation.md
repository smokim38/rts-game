# ADR 0006: How agents work with the Unity Editor

## Status

Accepted

## Date

2026-10-22

## Context

The grey-box sandbox (ADR 0004, follow-up 5) is the first Issue where agents must create a scene, prefabs, ScriptableObjects and materials. Every later Unity Issue also needs them to compile, run tests and build the Player. AGENTS.md §4 says to "prefer working through Unity (and later the Unity MCP tools) over hand-editing `.unity` or `.prefab` YAML". ADR 0002 removed the experimental `com.unity.pipeline` package. It also requires an accepted ADR before agents get any integration "that lets agents control the Editor", custom automation scripts included, and says that ADR must cover AGENTS.md §5 and "how access stays local and authenticated". This ADR makes that decision.

**Constraints**
- Unity 6000.3.23f1 with URP 17.3.0 and Mono (ADR 0001). ADR 0005 sets the layout: Editor-only code goes in `Game/Assets/Code/EditorTools/` (assembly `Rts.EditorTools`, namespace `Rts.EditorTools`), scenes in `Scenes/`, prefabs in `Prefabs/`, ScriptableObjects in `Data/` and materials in `Materials/`.
- **Who touches Unity.** The Codex gameplay-programmer, tech-artist and qa-test-engineer do, each on one Issue in its own worktree under `.studio/worktrees/`, started only by the launcher. The launcher runs Codex in `workspace-write` with `on-request` approvals answered by auto-review. It stops the run if Codex reports network access or writable roots outside the worktree, and it declines MCP elicitation requests (`verifySettings` and `declineRequest` in `tools/codex-launch.js`). Claude roles never run Unity: the Producer's shell runs only `git` and `gh`, the tech-architect and game-designer have no shell, and the code-reviewer runs only read commands (`docs/studio/claude-staff.md`).
- **The Claude guard** checks shell commands and file edits, and it makes `.mcp.json` owner-only (`docs/studio/claude-staff.md`). Any other tool call, MCP tools included, passes through unchecked (`check` in `.claude/hooks/guard.js`).
- **Dependencies (AGENTS.md §5).** Ask first, and a new dependency is Critical tier. A Unity Technologies package under the Unity Companion License, the Unity Package Distribution License or the Unity Terms of Service needs an accepted ADR that names it. Anything else needs MIT, Apache-2.0, BSD or CC0. MCP server configuration, `Game/Packages/manifest.json` and `packages-lock.json` are in the security perimeter (AGENTS.md §6). "Writing, deleting, installing or changing configuration outside the repository stays forbidden" (AGENTS.md §5).
- **Parallel work.** Two agents never edit the same scene at once (AGENTS.md §4). ADR 0004's ladder runs a Player with the Editor closed and no other heavy programs running.
- **Batch mode is needed in every option.** QA runs tests from the command line, which ADR 0002 already allows, and ADR 0004's harness must build and run the Player with one command. So the real question is how agents create and change content, and whether to add a live Editor channel on top.

**Evidence gap.** Nobody has run Unity under the launcher's sandbox yet. Codex's Windows sandbox runs commands as "dedicated lower-privilege sandbox users" on "a private desktop" ([Windows sandbox][codex-windows]). Unity, meanwhile, keeps per-user state outside the project:
- EditorPrefs, in the `HKCU` registry ([EditorPrefs][editorprefs])
- the package cache, in `%LOCALAPPDATA%\Unity\cache\upm` ([package cache][upm-cache])
- the license. A Unity Personal license is activated only by signing in to the Hub ([licenses][licenses]).

Nobody knows yet whether a batch-mode Editor inside the sandbox finds the license and resolves packages with the network off. A spike measures this (Consequences, follow-up 1).

## Decision

**Option A: no MCP and no Editor automation server.** Agents create and change Unity content with committed Editor scripts ("builders"). The Unity Editor runs them headless, in batch mode, from the agent's own worktree, and compiles, tests and builds the same way. This ADR adds no package, tool or MCP configuration.

### Why

- **AGENTS.md §4, working through Unity.** Unity's own Editor API makes every scene, prefab, ScriptableObject and material (`EditorSceneManager.NewScene`, [NewScene][new-scene]; `PrefabUtility.SaveAsPrefabAsset`, [SaveAsPrefabAsset][save-prefab]; `AssetDatabase`). Unity writes the YAML, the `.meta` files and the GUIDs. Each change is a C# script that reviewers can read and anyone can re-run, the same thing §4 asks of Blender export scripts. Through MCP, the same change is a series of tool calls, and only the YAML is left to review.
- **AGENTS.md §5, dependencies.** Nothing is added. Batch mode is part of the Editor we already use, and "using Unity in batch mode is subject to Unity's Terms of Service" ([command line][cli-args]), the Editor's existing terms (ADR 0001). Unity's current route is beta and experimental, and its older MCP server is deprecated. The open-source servers bring in Python, uv and packages from outside Unity's registry.
- **AGENTS.md §5, permissions.** In batch mode, Unity runs as a child of the agent's Codex command, so Editor code the agent writes runs inside the same sandbox, with the same writable roots and no network. An MCP server instead drives an Editor that someone started outside the sandbox, so code reaching it runs with the owner's full rights. Filtering tools doesn't close that gap. An `[InitializeOnLoad]` class runs "when Unity loads, and when your scripts are recompiled" ([InitializeOnLoad][init-on-load]), so any script an agent saves into an open project runs at the next recompile.
- **No network listener,** so nothing needs to be authenticated (ADR 0002's requirement).
- **Parallel work is natural.** Unity refuses only a second Editor on the same project: "You can't open a project in batch mode while the Editor has the same project open" ([command line][cli-args]). Each worktree is its own project.
- **Cheap to extend.** Builders are plain static methods. A later ADR that adds an Editor channel can call the same methods as custom commands: the Pipeline package runs static methods marked `[CliCommand]` ([Unity CLI blog][cli-blog]), and MCP for Unity has `execute_custom_tool` ([tools][coplay-tools]).

### How it runs

```text
Unity.exe -batchmode -nographics -projectPath <worktree>\Game -logFile <worktree>\Game\Logs\<step>.log -executeMethod Rts.EditorTools.<Builder>.<Method> -quit
Unity.exe -batchmode -nographics -projectPath <worktree>\Game -logFile <worktree>\Game\Logs\tests.log -runTests -testPlatform EditMode -testResults <worktree>\Game\Logs\editmode.xml
```

- `-executeMethod` runs "the static method as soon as Unity opens the project", and an exception makes Unity exit with code 1 ([command line][cli-args]).
- Test runs leave out `-quit`, which "is not supported while tests are running" ([test command line][test-cli]).
- `Game/Logs/` is git-ignored.
- `-nographics` stays only if the spike shows builders work without a graphics device. Player builds for the ladder follow the harness Issue (ADR 0004).
- `Unity.exe` is the 6000.3.23f1 install from the Hub, usually under `C:\Program Files\Unity\Hub\Editor\6000.3.23f1\Editor\`. Each Unity Issue names the install folder, because AGENTS.md §5 lets agents read outside the repository only at locations their Issue names.

### Rules for agents working with Unity

For the gameplay-programmer, tech-artist and qa-test-engineer, and for reviewers checking their PRs:

1. **Batch mode, your own worktree.** Every Unity run uses `-batchmode`, with `-projectPath` set to the `Game/` folder of your own worktree and `-logFile` under that worktree's `Game/Logs/`. Never open an Editor window, and never point Unity at the main checkout or at another worktree.
2. **Only 6000.3.23f1,** the version in `Game/ProjectSettings/ProjectVersion.txt`. Changing the Editor version needs an ADR (AGENTS.md §4).
3. **Builders make every Unity asset.** Scenes, prefabs, ScriptableObjects, materials and Build Settings are created and changed only by static methods in `Rts.EditorTools`, run with `-executeMethod`. Never hand-edit `.unity`, `.prefab`, `.asset` or `.mat` YAML, and never write a `.meta` file or a GUID.
4. **Builders can be re-run.** A builder:
   - loads each asset it owns, or creates it if it's missing
   - sets every property it owns
   - saves explicitly (`EditorSceneManager.SaveScene`, `AssetDatabase.SaveAssets`)
   - throws on any failure, so Unity exits with code 1
   - never depends on what an Editor has open

   Running it again with unchanged inputs must leave `git status` clean. If the spike shows Unity can't meet this, a new ADR changes the rule.
5. **The builder ships with its output.** The PR includes the builder, every asset it produced and their `.meta` files. The reviewer reviews the builder line by line, and checks the YAML only for scope (AGENTS.md §6).
6. **Conflicts are re-built, not hand-merged.** Resolve a merge conflict in a Unity asset by re-running its builder on the merged branch.
7. **Tests after building.** After a builder run, run the EditMode tests. A compiler error or warning from project code in the log fails the step (AGENTS.md §4, clean console).
8. **Editor code stays inside `Game/`.** Builders, Editor tools and tests don't open sockets, start processes, use the network or write outside the worktree's `Game/` folder. Reviewers treat any of these as Request changes.
9. **No Editor channels.** No MCP server, no Editor automation package (`com.unity.pipeline`, MCP for Unity or any other) and no `unity` CLI commands, until a new ADR adopts one. The Hub installs and updates the `unity` CLI on its own ([Unity CLI][cli-intro], [using the CLI][cli-use]). Having it on the PC doesn't approve it.
10. **Every run ends.** Builder runs end with `-quit`, and test runs end when the test runner finishes. No Editor is left running after a Codex turn.

### First sandbox Issues

1. The gameplay-programmer writes builders in `Game/Assets/Code/EditorTools/`, for example `SandboxSceneBuilder.Build()`. The builder:
   - makes the scenario ScriptableObject in `Data/` and the two faction materials, with GPU instancing on, in `Materials/`
   - makes the match driver, camera rig and debug overlay prefabs in `Prefabs/`
   - makes `Scenes/Sandbox.unity` from an empty scene: prefab instances, a directional light and the ground
   - puts the scene in Build Settings in place of `SampleScene` (ADR 0005). That changes `ProjectSettings/EditorBuildSettings.asset`, which the PR calls out (AGENTS.md §2).
2. The agent runs the builder, then the tests (above), then commits the builder, its output and the `.meta` files.
3. The Claude code-reviewer reviews the builder. QA re-runs the builder in its own worktree on the PR head, checks that `git status` stays clean (the committed assets match the builder), and runs the tests and the ladder.
4. **The owner does nothing during the run.** To see the result after the merge, the owner opens `Game/` from the main checkout in the Hub with 6000.3.23f1 and opens `Scenes/Sandbox.unity`.

### Parallel engineers

- **One Editor per worktree.** Unity runs one batch Editor per worktree, and several worktrees can run at once. Each worktree has its own `Game/Library/`, so its first run imports the whole project. QA worktrees are re-created for every check (`tools/codex-launch.js`), so every QA check pays that cost. The spike measures it.
- **Asset ownership.** Each Unity Issue lists, in its acceptance criteria, the scenes, prefabs, ScriptableObjects and settings files it creates or changes. The Producer doesn't start an Issue whose list overlaps one that is still open. That carries AGENTS.md §4's one-scene-at-a-time rule across worktrees.
- **Ladder runs are exclusive.** While a QA ladder run is going, no other Unity work runs and every Editor is closed (ADR 0004).

### What the owner does

- Keep Unity 6000.3.23f1 installed through the Hub and its license active (for Unity Personal, stay signed in to the Hub; [licenses][licenses]).
- Never open a `.studio/worktrees/` project in the Editor. The main checkout is fine for looking at merged work, but close the Editor before ladder runs and before the Producer's next sitting, so Unity can't change files in the checkout the Producer commits from.
- Don't add a Unity MCP server to the user-level Codex config (`~/.codex/config.toml`, [Codex MCP][codex-mcp]) or to Claude's config. The launcher doesn't check MCP servers, so every Codex role would get it unreviewed.

### Packages, tools and configuration

None are added. This choice uses the Unity Editor 6000.3.23f1 (Unity Technologies, Unity Terms of Service, already installed; ADR 0001), the Test Framework 1.6.0 (already in the manifest; license in ADR 0004), and Node.js, which the launcher already uses, for the wrapper in follow-up 2.

| Files the follow-ups add or change | Protected or in the security perimeter? |
|---|---|
| Builders and their `.meta` files in `Game/Assets/Code/EditorTools/` | No (Standard tier) |
| Assets the builders make in `Scenes/`, `Prefabs/`, `Data/` and `Materials/`, and `ProjectSettings/EditorBuildSettings.asset` | No (Standard; call out the settings change, AGENTS.md §2) |
| `tools/unity-batch.js` and `tools/unity-batch.test.js` | No (Standard) |
| Unchanged: `Game/Packages/manifest.json`, `packages-lock.json`, `.mcp.json`, `.codex/`, `.claude/`, `tools/codex-launch.js` | These are protected or in the perimeter, and this choice leaves them alone |

## Alternatives considered

| | A. Command line (chosen) | B. Unity CLI `unity mcp` and `com.unity.pipeline` | C. MCP for Unity (CoplayDev) |
|---|---|---|---|
| Publisher | Unity Technologies (the Editor we already have) | Unity Technologies | Coplay, "not affiliated with Unity Technologies" ([repo][coplay-repo]) |
| License | Unity Terms of Service, as for the Editor ([command line][cli-args]) | Pipeline: Unity Terms of Service ([license][pipeline-license]). CLI: none stated in its docs (gap) | MIT ([repo][coplay-repo]) |
| Version for 6000.3.23f1 | The Editor itself; Test Framework 1.6.0 | CLI 1.0.0-beta.14, 2026-10-21 ([release notes][cli-notes]). Pipeline 0.9.0-exp.1 ([package][pipeline-pkg]), which "requires Unity 6.0 or later" ([walkthrough][cli-walkthrough]) | v10.3.0, 2026-10-04 ([releases][coplay-releases]). Claims "2021.3 LTS → 6.x"; 6000.3 isn't named (gap) |
| What agents can do | Anything the Editor API can do, through committed scripts: import, compile, tests, Player builds. No live view. | Editor commands over a local HTTP API: Play mode, recompile, tests, builds, `eval`. The CLI also wraps batch mode (`unity run`, `test`, `build`) ([reference][cli-ref]) | 50 tools: scenes, GameObjects, prefabs, components, scripts, materials, packages, builds, tests, console, Profiler ([tools][coplay-tools]) |
| Editor running on the project? | No. Each run starts and quits a headless Editor | Yes: a window Editor, or a headless one from `unity open --batchmode --until-ready` ([release notes][cli-notes]) | Yes. The plugin "Receives commands on the Unity main thread" ([docs][coplay-docs]) |
| Code an agent can run | Its own Editor scripts and tests, inside its sandbox (menu items too, through `EditorApplication.ExecuteMenuItem`) | Any C#: `eval` is "compiled with Roslyn and run on the Editor's main thread" ([blog][cli-blog]). Unity says to "treat it like remote code execution" ([walkthrough][cli-walkthrough]). ADR 0002 also found `package_add` and `build` | Any C# (`execute_code`), menu items (`execute_menu_item`), package installs and removals (`manage_packages`), and third-party AI services (`asset_gen`) ([tools][coplay-tools]) |
| Network listener | None added | Local HTTP. "By default the server binds to localhost" ([walkthrough][cli-walkthrough]), on ports 7800–7849 with a token in `Library/Pipeline/` (ADR 0002, from the 0.8 docs). `eval` is "gated behind a security token" ([blog][cli-blog]) | HTTP on `localhost:8080`, loopback by default. No authentication is described for local use ([transports][coplay-transports]) |
| Codex sandbox | Unity is a child of a sandboxed command: same writable roots, no network, provided Unity runs there at all (spike) | The Editor runs outside the agent's sandbox. Codex's network proxy "does not filter … MCP server connections" ([approvals][codex-security]) | As B. The agent itself picks which Editor to drive (`set_active_instance`, or `unity_instance` on a call) ([instances][coplay-instances]) |
| Claude guard | No change | `.mcp.json` is owner-only, and the guard doesn't inspect MCP tool calls | As B |
| What it adds | Nothing | The CLI binary, with sign-in (`unity auth login`, [Pipeline install][pipeline-install]); `com.unity.pipeline` in the manifest (reversing ADR 0002); MCP configuration | Python 3.10+ and uv on the PC, the server from PyPI, the Unity package by git URL or OpenUPM in the manifest, and MCP configuration ([install][coplay-install]) |

**B0. The AI Assistant's in-Editor MCP server** (`com.unity.ai.assistant`). It opens named pipes and runs a relay binary from `~/.unity/relay/`, and external clients need approval in Project Settings. Unity now says the "Unity MCP server is deprecated" ([Unity MCP][ai-mcp]) and recommends the CLI's `unity mcp` instead ([replacement][cli-replace]). So it isn't evaluated further.

**B. Unity CLI with `unity mcp` and the Pipeline package**
- *How it works:* the Pipeline package starts a local server in the Editor. The CLI forwards commands to it (`unity command`), or serves them to an AI client as MCP tools (`unity mcp`, which can filter tools with `--tools` and `--exclude-tools`; [release notes][cli-notes]).
- *Evidence:* the CLI is "experimental. The features and documentation might change" ([Unity CLI][cli-intro]). The Pipeline package is "currently experimental" ([blog][cli-blog]), and Unity says "you shouldn't use experimental packages in production" ([experimental packages][exp]). In its favor: on Windows, "only a Unity-signed Editor started by `unity open` can get your access token" ([release notes][cli-notes]). The Hub updates the CLI automatically, and "You can't disable these updates" ([using the CLI][cli-use]), so the Hub, not the studio, decides its version.
- *Cost at scale:* the Pipeline's runtime assemblies can be included in development and Profiler builds (ADR 0002). Those are the builds ADR 0004 rule 11 uses for its 0 B GC Alloc capture, so the capture could include code the game doesn't ship.
- *Complexity:* a beta binary (1.0.0-beta.14), an experimental package, and a Unity account sign-in on the agents' PC.
- *Risk:* `eval` gives an agent's code the Editor's rights. The token lives in a project's `Library/` folder. The sandbox is built to limit writes and network, and nothing in the studio's setup stops an agent reading another worktree's files, so the token doesn't keep agents out of each other's Editors. Reversing it means removing the package again.
- *Not chosen now,* because it is beta and experimental. It is the first candidate to revisit (follow-up 7).

**C. MCP for Unity (CoplayDev)**
- *How it works:* a Python server, started through uv, talks to MCP clients, and a Unity plugin carries out its commands in a running Editor. The default transport is HTTP. Several Editors are told apart by IDs of the form `Name@hash` ([instances][coplay-instances]).
- *Evidence:* MIT license, active releases, and "no telemetry by default" ([docs][coplay-docs]). Its own docs warn that retrying a command that has already started in Unity can apply it twice, because there is no idempotency key ([instances][coplay-instances]).
- *Cost at scale:* we didn't check whether any of it ships in Player builds. An Editor stays open for every Issue in progress, using memory nobody has measured.
- *Complexity:* the lowest effort per edit for an agent. But it means new Python tooling on the PC, a client configuration per role, and an Editor kept running per worktree.
- *Risk:* `execute_code` and `manage_packages` let an agent run any code with the owner's rights and install packages, outside AGENTS.md §5's ask-first rule. Any local process can reach the unauthenticated port, unless the sandbox blocks loopback. Whether it does depends on the sandbox mode: MXC "permits connections to and from services on host loopback" ([Windows sandbox][codex-windows]). Codex's documented approvals for MCP calls key on destructive or side-effect annotations ([approvals][codex-security]), which the server declares itself. A per-server `default_tools_approval_mode` and the `enabled_tools` and `disabled_tools` filters can tighten that ([Codex MCP][codex-mcp]), but none of them closes the `[InitializeOnLoad]` route (Decision).
- *Not chosen* for those risks, and because a live Editor per worktree adds an owner chore and shared state that a builder script doesn't need.

**Other open-source servers.** IvanMurzak/Unity-MCP is Apache-2.0. It offers `script-execute` ("Compiles and executes C# code dynamically using Roslyn") and `reflection-method-call` ("Call any C# method"), and its authorization defaults to `none` on port 8080 ([repo][murzak-repo]). It has the same class of risk as C and wasn't evaluated further.

## Consequences

- **Benefits.** No new dependency, no listener and no change to the perimeter. Every asset change is a reviewable, re-runnable script. QA can prove that the committed assets match their builder. Nothing is added to Player builds, so the ladder is unaffected.
- **Tradeoffs.** Agents get no live view of the Editor. Each change costs a batch run, including a full import on a fresh worktree. Look-and-feel work (the tech-artist) has to come from scripts and rendered captures rather than interactive tuning. AGENTS.md §4's "later the Unity MCP tools" stays deferred.
- **Risks.**
  - Unity may not run under the launcher's sandbox (Context, evidence gap). If it needs a run outside the sandbox, agent-written Editor code runs with the owner's rights, much as in B and C. It would also clash with §5's ban on writes outside the repository. That is owner decision 2.
  - A Player started from the sandbox runs on a private desktop ([Windows sandbox][codex-windows]), so it may not go full screen or keep focus. ADR 0004 treats a focus loss as an invalid ladder run.
  - Batch imports may be slow, or memory-heavy with several worktrees running at once. Nobody has measured either.
- **Reversibility.** Easy. A later ADR can add B or C, and builders become their custom commands. Leaving A behind deletes no work.
- **Owner decisions:**
  1. **Command line now, MCP later.** Approve option A and defer MCP. *Recommended:* it adds no dependency or listener, keeps agent code inside the sandbox, and leaves a reviewable script for every asset. Unity's own route is still beta and experimental.
  2. **If the spike shows Unity can't run inside the launcher's sandbox,** choose how it runs instead:
     - allow sandbox escalations for Unity (a handbook change to §5)
     - add fixed writable roots for Unity (a launcher change)
     - have the launcher run a fixed set of Unity commands itself after the agent's turn (a launcher change)

     All three are Critical and in the security perimeter. *Recommended:* decide with the spike's findings, and prefer whichever keeps Unity sandboxed. Don't rely on auto-review approving unsandboxed Unity runs one at a time.
- **Follow-up Issues,** in order:
  1. **Spike** (`type:spike`, gameplay-programmer through the launcher, time box one day; the Issue names the Unity install folder; throwaway branch, only the finding is kept). In the role's worktree, measure:
     - a batch import from an empty `Library/` (exit code, wall time, peak memory), then a warm re-run
     - a builder that makes a scene, a prefab, a ScriptableObject and a URP material, run with and without `-nographics`, then run again to check that `git status` stays clean
     - the exit code and the log output for a deliberate compile error
     - EditMode tests through `-runTests`
     - a Windows Player build, and whether a Player launched from the sandbox goes full screen and keeps focus
     - two batch Editors in two worktrees at once
     - whether Unity found its license and resolved packages with the network off
     - any sandbox block or auto-review escalation, from the launcher log
     - the paths outside the worktree that Unity wrote or tried to write, summarized without user names (AGENTS.md §5)
  2. **`tools/unity-batch.js`** with `node --test` tests (Standard tier, Node built-ins only), after the spike. It:
     - finds the Editor for the version in `ProjectVersion.txt` and refuses any other version
     - always passes `-batchmode` and the worktree's own `-projectPath` and `-logFile`
     - offers three commands: run a builder, run tests, build the Player
     - fails on compiler errors or warnings in the log
  3. **The grey-box sandbox** (ADR 0004 follow-up 5), built by builders under these rules, with its asset list in its acceptance criteria.
  4. **`docs/studio/workflow.md`:** asset lists in Unity Issues, no overlapping open Issues, and no Unity work during ladder runs.
  5. **If owner decision 2 is needed,** the handbook or launcher change it picks (Critical, security perimeter; the Issue names `AGENTS.md` or `tools/codex-launch.js`).
  6. **Suggested:** add these rules to the gameplay-programmer, tech-artist and qa-test-engineer role files. `.codex/agents/` is a protected path, so that Issue must name the files.
  7. **A superseding ADR** if the Unity CLI and the Pipeline package leave beta and experimental with a license §5 covers and a way to isolate each worktree's Editor, or if Issues show work that builders can't do.

[cli-args]: https://docs.unity3d.com/6000.3/Documentation/Manual/EditorCommandLineArguments.html
[test-cli]: https://docs.unity3d.com/6000.3/Documentation/Manual/test-framework/reference-command-line.html
[new-scene]: https://docs.unity3d.com/6000.3/Documentation/ScriptReference/SceneManagement.EditorSceneManager.NewScene.html
[save-prefab]: https://docs.unity3d.com/6000.3/Documentation/ScriptReference/PrefabUtility.SaveAsPrefabAsset.html
[init-on-load]: https://docs.unity3d.com/6000.3/Documentation/ScriptReference/InitializeOnLoadAttribute.html
[editorprefs]: https://docs.unity3d.com/6000.3/Documentation/ScriptReference/EditorPrefs.html
[upm-cache]: https://docs.unity3d.com/6000.3/Documentation/Manual/upm-cache.html
[licenses]: https://docs.unity3d.com/6000.3/Documentation/Manual/ManagingYourUnityLicense.html
[exp]: https://docs.unity3d.com/6000.3/Documentation/Manual/pack-exp.html
[ai-mcp]: https://docs.unity3d.com/Packages/com.unity.ai.assistant@2.20/manual/integration/unity-mcp-overview.html
[cli-replace]: https://docs.unity.com/en-us/unity-cli/replace-mcp-server-unity-cli
[cli-intro]: https://docs.unity.com/en-us/unity-cli
[cli-use]: https://docs.unity.com/en-us/unity-cli/use-unity-cli
[cli-ref]: https://docs.unity.com/en-us/unity-cli/unity-cli-reference
[cli-notes]: https://docs.unity.com/en-us/unity-cli/release-notes
[pipeline-install]: https://docs.unity.com/en-us/unity-cli/unity-pipeline/unity-pipeline-package
[pipeline-pkg]: https://docs.unity3d.com/Packages/com.unity.pipeline@0.9/index.html
[pipeline-license]: https://docs.unity3d.com/Packages/com.unity.pipeline@0.9/license/LICENSE.html
[cli-blog]: https://unity.com/blog/meet-the-unity-cli
[cli-walkthrough]: https://unity.com/resources/unity-pipeline-cli-technical-walkthrough
[coplay-repo]: https://github.com/CoplayDev/unity-mcp
[coplay-releases]: https://github.com/CoplayDev/unity-mcp/releases
[coplay-docs]: https://coplaydev.github.io/unity-mcp/
[coplay-tools]: https://coplaydev.github.io/unity-mcp/reference/tools
[coplay-transports]: https://coplaydev.github.io/unity-mcp/architecture/transports
[coplay-instances]: https://coplaydev.github.io/unity-mcp/guides/multi-instance
[coplay-install]: https://coplaydev.github.io/unity-mcp/getting-started/install
[murzak-repo]: https://github.com/IvanMurzak/Unity-MCP
[codex-mcp]: https://developers.openai.com/codex/mcp
[codex-security]: https://developers.openai.com/codex/agent-approvals-security
[codex-windows]: https://developers.openai.com/codex/windows
