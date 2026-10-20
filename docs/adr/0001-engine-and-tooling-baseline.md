# ADR 0001: Engine and tooling baseline

## Status

Accepted

## Date

2026-10-19

## Context

The owner has already chosen the engine and tools, and the Unity project in `Game/` was created with them. Nothing in the repo records those choices or why they were made. Later ADRs (unit simulation, rendering, folder layout), every upgrade and every agent need one written baseline to point to.

Constraints:
- Thousands of units at a smooth frame rate on a mid-range PC. The reference GPU is an RTX 3050 with 6 GB (AGENTS.md §4).
- The team is AI agents who each work on one Issue at a time, so fast, predictable iteration matters more than peak tuning at this stage.
- The repository is public. Adding any dependency remains governed by AGENTS.md §5: it must come from an official source under a permissive license (MIT, Apache-2.0, BSD or CC0), the owner is asked first, and the change is Critical tier. Recording the tools already in use doesn't change that rule.
- Unity and package versions change only through an accepted ADR (AGENTS.md §4).

This ADR records decisions that were already made. It is not a fresh evaluation, and it adds no package, setting or dependency.

## Decision

- **Engine:** Unity 6000.3.23f1 (6.3 LTS)
- **Render pipeline:** URP (`com.unity.render-pipelines.universal` 17.3.0, as pinned in `Game/Packages/manifest.json`)
- **Scripting backend:** Mono for desktop builds; IL2CPP is adopted only through a new ADR, when measurement shows a benefit or a target platform requires it
- **3D art:** Blender 5.2 LTS
- **Code editor:** VS Code

Why each one fits:
- **Unity 6000.3.23f1:** 6.3 is an LTS release supported until December 2027 ([Unity 6 support][unity-support]), and the project already uses this exact version (`Game/ProjectSettings/ProjectVersion.txt`).
- **URP 17.3.0:** Unity positions URP for "scalable graphics across a wide range of platforms" ([render pipelines][rp-overview]), which matches a mid-range GPU target.
- **Mono:** Unity says Mono "offers fast iteration and good tooling" ([Mono backend][mono]), and we have no measurement yet showing that scripting speed limits the game.
- **Blender 5.2 LTS:** It is free and open source. Python lets the export scripts in `Art/` re-run with the same result every time (AGENTS.md §4), and LTS releases get critical fixes for two years ([Blender LTS][blender-lts]).
- **VS Code:** It is free and lightweight, and Microsoft's Unity extension adds editing and debugging ([VS Code for Unity][vscode-unity]). The extension needs the Visual Studio Editor package at 2.0.20 or later. That package (`com.unity.ide.visualstudio` 2.0.28) is already in the manifest.

The manifest also lists `com.unity.ide.rider`. This ADR doesn't add, remove or change any package.

## Alternatives considered

**Engine**
- *Unity 6.0 LTS:* Its support ends in October 2026 ([Unity 6 support][unity-support]), so we would have to upgrade almost at once.
- *A newer Unity 6.x Update release:* Each one is supported only until the next release ships ([Unity 6 support][unity-support]). That would force frequent upgrades, and each one needs an ADR.
- *Another engine (for example Unreal Engine or Godot):* Not evaluated. The owner chose Unity, and the project, handbook and agent tooling already target it.

**Render pipeline**
- *HDRP:* Unity aims HDRP at "high-end platforms" and "high-fidelity graphics" ([render pipelines][rp-overview]). That trades away the headroom we need for thousands of units on an RTX 3050.
- *Built-in Render Pipeline:* Unity calls it "a general purpose render pipeline with limited options for customization" ([render pipelines][rp-overview]), and the project is already set up on URP.

**Scripting backend**
- *IL2CPP now:* IL2CPP compiles ahead of time, and Unity says Mono "has lower runtime performance" than IL2CPP ([Mono backend][mono]). But Windows builds with IL2CPP need Visual Studio 2019 or later with the C++ tools and Windows SDK 10.0.19041.0 or newer ([system requirements][sysreq]). Those are new installs (AGENTS.md §5), and we have no Profiler data showing a gain for this game.
- *Mono for development and IL2CPP for release builds:* That gives us two backends to build and test, with no measured need for the second one yet.

**3D art**
- *Autodesk Maya or 3ds Max:* Both need paid commercial licensing. The owner chose Blender, and the tech-artist's export scripts target it.
- *A non-LTS Blender release (for example 5.3):* The next release replaces it within months, so export scripts would keep chasing API changes. LTS gives a stable target for two years ([Blender LTS][blender-lts]).

**Code editor**
- *Visual Studio Community:* It is a full IDE but Windows-only and heavier. The owner prefers VS Code.
- *JetBrains Rider:* It has strong Unity support, and `com.unity.ide.rider` is already in the manifest. But commercial use needs a paid license.

## Consequences

- **Upgrades need an ADR.** Upgrading Unity (including a 6000.3.x patch release) or any package needs a new accepted ADR (AGENTS.md §4). Reviewers check these versions against `ProjectVersion.txt` and `manifest.json`.
- **IL2CPP triggers.** A Profiler-confirmed scripting bottleneck triggers an *evaluation* of IL2CPP in a new ADR, not an automatic switch. A target platform that requires IL2CPP is also a trigger: Unity says IL2CPP is "required on several platforms where Mono and JIT are not supported" ([scripting backends][backends]). That evaluation should measure both backends on separate player builds with matching settings, one per backend. It should also account for whatever the simulation-architecture ADR decides (for example, Burst-compiled code runs as native code under either backend).
- **Keep IL2CPP cheap to adopt.** Game code must not use `System.Reflection.Emit` or the `dynamic` keyword, because AOT can't support them ([scripting restrictions][aot]). Don't rely on reflection to reach code that nothing references directly, because code stripping can remove it.
- **Out of scope.** The unit simulation architecture (for example DOTS/ECS or GameObjects) is left to a later ADR. So are the rendering approach for large unit counts and the `Game/Assets/` folder layout (AGENTS.md §2).
- **Unity support window.** Unity 6.3 LTS support ends in December 2027 ([Unity 6 support][unity-support]). An ADR for the upgrade to a newer LTS is needed before then.
- **Blender licensing.** Art made with Blender belongs to its creator. However, Blender says scripts that use its Python API must, "if published", be shared "under a GPL compliant license" ([Blender license][blender-license]). Export scripts in `Art/` are published in this public repo, and the repo has no LICENSE file yet. **The owner must choose the license for those scripts before the first one is merged.**
- **VS Code licensing.** Microsoft's Unity extension is built on C# Dev Kit ([C# Dev Kit FAQ][devkit]), which uses the same license model as Visual Studio ([C# Dev Kit announcement][devkit-launch]). Academic and open-source use is free. Other use, including commercial use, is free only for those who qualify under the Visual Studio Community license terms: individuals, and commercial teams of up to five developers per Microsoft's FAQ. Anyone who doesn't qualify needs an eligible Visual Studio subscription (Professional or higher) or another entitlement; the FAQ names GitHub Codespaces and Microsoft Dev Box ([C# Dev Kit FAQ][devkit]). Recheck this if the human team grows.
- **Installing tools.** Editors and their extensions are local tools, not project packages. Installing them still follows AGENTS.md §5: ask first.
- **Reversibility.** Changing the engine becomes impractical once content exists. Leaving URP would mean reworking shaders and materials. Switching the backend is a Player setting that is cheap to try but needs a full test pass. Changing Blender or VS Code is cheap.
- **Follow-up Issues:**
  - an owner decision on the license for `Art/` export scripts
  - an ADR on the unit simulation architecture

[unity-support]: https://unity.com/releases/unity-6/support
[rp-overview]: https://docs.unity3d.com/6000.3/Documentation/Manual/render-pipelines-overview.html
[backends]: https://docs.unity3d.com/6000.3/Documentation/Manual/scripting-backends.html
[mono]: https://docs.unity3d.com/6000.3/Documentation/Manual/scripting-backends-mono.html
[sysreq]: https://docs.unity3d.com/6000.3/Documentation/Manual/system-requirements.html
[aot]: https://docs.unity3d.com/6000.3/Documentation/Manual/scripting-restrictions.html
[blender-lts]: https://www.blender.org/download/lts/
[blender-license]: https://www.blender.org/about/license/
[vscode-unity]: https://code.visualstudio.com/docs/other/unity
[devkit]: https://code.visualstudio.com/docs/csharp/cs-dev-kit-faq
[devkit-launch]: https://devblogs.microsoft.com/visualstudio/announcing-csharp-dev-kit-for-visual-studio-code/
