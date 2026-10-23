# ADR 0007: Code name Northwatch for the root namespace and assemblies

## Status

Accepted

## Date

2026-10-22

## Context

[ADR 0005](0005-game-assets-folder-layout.md) set the root namespace `Rts` and named its six assemblies `Rts.*`, with each assembly's name also its root namespace. Its reason: "The game has no title yet, and a neutral code name means a later title never forces a rename."

On 2026-10-22, in the Producer session, the owner chose the code name Northwatch (Issue #79).

Constraints:
- ADR 0005 says renaming an assembly or a namespace "touches every file in it". So names are cheapest to fix before any code exists, and a rename needs a superseding ADR.
- AGENTS.md §9: an accepted ADR isn't edited. A new ADR supersedes it.

What exists today (checked on this branch):
- `Game/Assets/` has no assembly definition and no game code. Its only scripts are the URP template's two `TutorialInfo/` scripts. They compile into Unity's predefined assemblies, and ADR 0005's cleanup Issue removes them.
- Only ADR 0005 names an `Rts` namespace or assembly. ADR 0006 (PR #80, in review, not on this branch) also names `Rts.EditorTools`.
- No package in `Game/Library/PackageCache/` uses the name `Northwatch`.

So the rename costs this ADR and no code. Nothing here depends on a measurement, so no spike is proposed.

## Decision

### The code name

- **The code name is `Northwatch`.** It is internal: it is used in namespaces, assembly names and the file names derived from them. It isn't a player-facing title.
  - This ADR doesn't name the game. It doesn't change Player Settings: Product Name stays `Game` and Company Name stays `DefaultCompany` until a later decision sets them.
  - So a later title forces no rename, as ADR 0005 intended.
  - Internal isn't secret: the repository is public, and assembly names ship inside Player builds.
- It is one word in PascalCase, as the .NET namespace guidelines ask ([namespace names][ms-ns]).

### Names it replaces

| ADR 0005 name | New name |
|---|---|
| Root namespace `Rts` | `Northwatch` |
| A file's namespace: `Rts.` plus its folder path below `Code/` | `Northwatch.` plus its folder path below `Code/` |
| Assembly `Rts.Simulation` | `Northwatch.Simulation` |
| Assembly `Rts.CommanderAI` | `Northwatch.CommanderAI` |
| Assembly `Rts.Presentation` | `Northwatch.Presentation` |
| Assembly `Rts.EditorTools` | `Northwatch.EditorTools` |
| Assembly `Rts.Tests.EditMode` | `Northwatch.Tests.EditMode` |
| Assembly `Rts.Tests.PlayMode` | `Northwatch.Tests.PlayMode` |
| Example namespace `Rts.Simulation.Movement` | `Northwatch.Simulation.Movement` |
| Example namespace `Rts.Tests.EditMode.Simulation.Movement` | `Northwatch.Tests.EditMode.Simulation.Movement` |
| Forbidden-namespace example `Rts.Editor` | `Northwatch.Editor`, still forbidden |

Each assembly's name is still its root namespace (ADR 0005).

**What follows from the new names**
1. **Assembly definition files.** ADR 0005 names each file `<Name>.asmdef`, so the files are `Northwatch.Simulation.asmdef`, `Northwatch.CommanderAI.asmdef`, `Northwatch.Presentation.asmdef`, `Northwatch.EditorTools.asmdef`, `Northwatch.Tests.EditMode.asmdef` and `Northwatch.Tests.PlayMode.asmdef`. Name and Root Namespace in each are set to the same name. This is the only file-name convention ADR 0005 derives from assembly names. `AssemblyInfo.cs` keeps its name.
2. **Strings that name an assembly.** References by name in the assembly definition files use the new names. `InternalsVisibleTo` takes the friend assembly's name, "without a directory path or file name extension" ([InternalsVisibleToAttribute][ivt]). So the only friend names that ADR 0005 reference rule 4 allows are exactly these:
   ```csharp
   [assembly: InternalsVisibleTo("Northwatch.Tests.EditMode")]
   [assembly: InternalsVisibleTo("Northwatch.Tests.PlayMode")]
   ```
3. **Everywhere else ADR 0005 writes `Rts` as a namespace or assembly name,** read `Northwatch`: the planning hint, reference rules 1 to 3, the test assembly settings and follow-up 3. For example, reference rule 2's "no other `Rts` assembly" means no other `Northwatch` assembly. The rule against a type named after a namespace it's in now also rules out a type named `Northwatch`.
4. **Nothing else in ADR 0005 changes.** The folders (no folder name contains `Rts`), the six assembly boundaries, platforms, reference rules, assembly settings, package references, the test package and the plan for existing items all stay as ADR 0005 sets them. The repository name `rts-game` and the `Game/` folder stay too.

### What this ADR supersedes

- It supersedes ADR 0005 **only** for the root namespace and the assembly names in the table, together with the file names and strings that follow from them. ADR 0005 stays Accepted for everything else, and it isn't edited (AGENTS.md §9).
- Any other ADR that names an `Rts` namespace or an `Rts.*` assembly, including ADR 0006 (PR #80) whenever it merges, is read with the `Northwatch` name, and only an ADR that explicitly supersedes this one changes these names again.

## Alternatives considered

- **(A) Keep `Rts`** (ADR 0005). It is short and neutral. *Not chosen:* the owner chose a code name, and no code exists yet, so this is the cheapest moment to change it.
- **(B) Use `Northwatch` now (chosen).** It is the owner's code name, and it costs only this ADR.
- **(C) Wait for the player-facing title and use it.** *Not chosen:* a title can still change before release, and each change would rename every file. ADR 0005 chose a code name to avoid exactly that. Waiting would also mean renaming whatever code exists by then.
- **(D) Rename only the root namespace and keep the `Rts.*` assembly names.** *Not chosen:* ADR 0005 makes each assembly's name its root namespace, so a reviewer checks one name per assembly definition against one table. Two prefixes would break that and invite mistakes.

## Consequences

- **Benefits.** The names match the owner's code name and are fixed before any code exists. A later title doesn't touch code.
- **Tradeoffs.** Names are longer (`Northwatch.` instead of `Rts.`). Editors complete them, so the cost is small.
- **Cost at thousands of units.** None. Names change no code that runs, so there is no CPU, GPU, memory or allocation cost, and nothing to profile.
- **Risks.**
  - An agent copying from ADR 0005 or ADR 0006 may write `Rts.`. Reviewers check every assembly definition and namespace against the table above. ADR 0005 follow-up 3's namespace test catches what review misses, once the test assemblies exist.
  - Code merged before this ADR would need a rename. So this ADR merges before the first Issue that creates an assembly definition or game code (Issue #79). The Producer sequences that.
- **Reversibility.** Free until the first assembly definition merges. After that, a rename touches every file and every by-name reference, and needs a superseding ADR, as ADR 0005 says.
- **Dependencies.** None: no package, no Unity change and no handbook change.
- **Owner decisions.** None new. The player-facing title, and Product Name and Company Name in Player Settings, are left for later. Nothing here blocks them.
- **Follow-up Issues.** None new. ADR 0005's follow-ups use the new names:
  - The sandbox Issue (ADR 0005 follow-up 2) creates the `Northwatch.*` runtime and editor assembly definitions.
  - The first tests (follow-up 3) check `Northwatch.Simulation`, and check each script's namespace against `Northwatch.` plus its folder path.
  - The role-file rules (follow-up 4) use the new names.

[ms-ns]: https://learn.microsoft.com/en-us/dotnet/standard/design-guidelines/names-of-namespaces
[ivt]: https://learn.microsoft.com/en-us/dotnet/api/system.runtime.compilerservices.internalsvisibletoattribute
