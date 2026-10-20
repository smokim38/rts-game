# ADR 0002: Remove the experimental Unity Pipeline package

## Status

Accepted

## Date

2026-10-20

## Context

`Game/Packages/manifest.json` lists `com.unity.pipeline` 0.8.0-exp.1. In `Game/Packages/packages-lock.json` it is a direct dependency (`depth: 0`, source `registry`), no other package requires it, and it is the only package that requires `com.unity.nuget.newtonsoft-json`, resolved at 3.2.2 (`depth: 1`). AGENTS.md §4 and ADR 0001 require an accepted ADR for upgrading Unity or packages. The owner also requires one for package removals; a handbook change that would make this a general rule is proposed in #27.

Investigation #22 found the following, and the owner accepted the finding:
- **Experimental, not supported for production.** Unity doesn't support experimental packages and says "you shouldn't use experimental packages in production" ([experimental packages][exp]). The changelog lists changes in 0.6, 0.7 and 0.8 that force updates to user code or settings ([changelog][changelog]).
- **Never approved.** It arrived in commit dd91b0a ("Add Unity 6.3 LTS project (URP) in Game/"), the initial project commit, before the handbook existed. So it never went through the ask-first dependency rule in AGENTS.md §5. Unity Hub's "Use Unity CLI" option at project creation probably added it; that is unconfirmed.
- **License.** Since 0.8.0 it is distributed under the Unity Terms of Service instead of the Unity Package Distribution License ([changelog][changelog]). Neither is one of the permissive licenses AGENTS.md §5 asks for (MIT, Apache-2.0, BSD, CC0).
- **Nothing depends on it.** No other package requires it, and nothing in `Game/Assets/` or `Game/ProjectSettings/` references it (re-checked for this ADR).
- **Editor automation server.** The package lets an outside client drive a running Editor over a local HTTP API ([package docs][docs], [Unity docs][unity-docs]). Its `Documentation~/connectivity.md` says the Editor server starts on its own on 127.0.0.1, ports 7800–7849, with a token in `Game/Library/Pipeline/.unity-pipeline-port`. It accepts commands including `eval` (run C#), `package_add` and `build`. Any local process that can read that token can run code in the Editor and add packages, outside the review and ask-first rules of AGENTS.md §5–6. *Assessed from the package documentation, not tested.*
- **Build warning.** `Editor/BuildProcessors/PipelineRuntimeBuildProcessor.cs` logs a "Pipeline: No RuntimePipelineConfig asset found…" warning on Player builds. That would break the clean-console rule (AGENTS.md §4). *Inferred from the package source, not observed in a build.*
- **Ordinary release builds don't include it.** Its runtime assemblies and bundled Roslyn DLLs compile only under `UNITY_EDITOR || ENABLE_PROFILER || ENABLE_RUNTIME_PIPELINE`. So Unity leaves them out of a non-development Player build that isn't instrumented for the Profiler and doesn't set `ENABLE_RUNTIME_PIPELINE` ([runtime setup][runtime-setup]). A development build, a Profiler-instrumented build, or a build that sets that define can include them. Removing the package is therefore expected not to change frame time, memory or behavior in an ordinary release Player. *Expected from the package documentation, not measured.*

## Decision

Remove `com.unity.pipeline` 0.8.0-exp.1 from the Unity project, and with it `com.unity.nuget.newtonsoft-json` 3.2.2, which only it pulls in.

- The removal itself is done under Issue #25, not in this ADR's PR. It changes `Game/Packages/manifest.json` and `Game/Packages/packages-lock.json`, so it is Critical tier (AGENTS.md §6).
- `com.unity.nuget.mono-cecil` 1.11.6 is also a dependency of the pipeline package, but it stays: `com.unity.collections` 2.6.8 requires the same version.

Why this fits: the package is experimental and unsupported, was never approved, uses a license outside §5, and nothing uses it. It also opens a code-execution channel into the Editor and likely adds a build warning. Removing it costs the project nothing it uses today, and it is easy to reverse.

## Alternatives considered

- **Keep it as is.** It would keep an unsupported, unapproved, non-permissively licensed package, an automation server that can run code in every Editor session, and a likely warning on every Player build. Nothing uses it, so keeping it gains nothing.
- **Keep it with the automation server turned off.** That closes the server risk only while the setting stays off in every agent's Editor, and nobody would notice if it changed. It still leaves the experimental status, the license, the missing §5 approval, the likely build warning and the API churn between versions, all for a package nothing uses.
- **Downgrade to 0.7.x, before the license change.** It is still experimental and unused. Its earlier license, the Unity Package Distribution License, isn't on the §5 list either, and a downgrade is a package change that would need its own approval anyway.
- **Replace it now with another Editor-control integration (for example a Unity MCP server).** AGENTS.md §4 expects "Unity MCP tools" later, but no Issue needs one yet. A new integration is a new dependency with its own security questions, so it belongs in its own ADR (see Consequences).

## Consequences

- **Benefits.** This removes an unapproved dependency, an unreviewed way to run code in the Editor, and a likely build warning. Upgrades have two fewer packages to track.
- **Tradeoff.** The project loses the Pipeline/Unity CLI route for controlling a running Editor (open scenes, run code, build). No current Issue needs that.
- **Command-line testing is ordinary testing.** Running Unity's built-in test runner from the command line (`-batchmode -runTests`, with `-testPlatform` and `-testResults`) is a standard Unity feature from the Unity Test Framework (`com.unity.test-framework`, already in the manifest). It works independently of this package ([command-line tests][cli-tests]). Agents may do it under their approved Issues without an ADR.
- **Editor control needs an ADR.** Re-adding this package needs a new accepted ADR. So does adopting any new integration that lets agents control the Editor, for example an Editor automation server such as Unity Pipeline/CLI, an MCP server, or custom automation scripts. That ADR must cover the dependency rules in AGENTS.md §5 (source, license, owner approval) and how access stays local and authenticated.
- **Order.** #25 may start only after this ADR is merged.
- **Checks for #25.** #25's acceptance criteria define the removal's checks. Note that `com.unity.nuget.mono-cecil` must stay in `packages-lock.json` at 1.11.6, because `com.unity.collections` 2.6.8 requires it, and that `Game/Library/Pipeline/` is local and untracked.
- **Reversibility.** It is cheap to reverse: re-adding the package restores it, but that needs a new accepted ADR (above). Ordinary release builds (not development builds, not Profiler-instrumented, without `ENABLE_RUNTIME_PIPELINE`) leave out its runtime assemblies ([runtime setup][runtime-setup]), so neither direction is expected to change what players get. That is an expectation from the documentation, not a measured result.
- **Follow-up Issues.**
  - #25: the removal.
  - Suggested: confirm whether Unity Hub's "Use Unity CLI" option adds this package to new projects. If it does, note in the setup docs to leave that option off.

[exp]: https://docs.unity3d.com/6000.3/Documentation/Manual/pack-exp.html
[changelog]: https://docs.unity3d.com/Packages/com.unity.pipeline@0.8/changelog/CHANGELOG.html
[docs]: https://docs.unity3d.com/Packages/com.unity.pipeline@0.8/manual/index.html
[runtime-setup]: https://docs.unity3d.com/Packages/com.unity.pipeline@0.8/manual/runtime-setup.html
[unity-docs]: https://docs.unity.com/en-us/unity-production-pipeline/local-tools-cli/unity-pipeline-package
[cli-tests]: https://docs.unity3d.com/6000.3/Documentation/Manual/test-framework/run-tests-from-command-line.html
