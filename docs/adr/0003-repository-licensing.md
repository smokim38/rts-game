# ADR 0003: Repository licensing

## Status

Accepted

## Date

2026-10-20

## Context

**This ADR is not legal advice.** The owner makes the final call: the owner chose Option C, based on the architect's analysis of the official sources cited. The owner should still ask a lawyer before any sale. Not assessed here: the law of the owner's country, and how far AI-generated work is protected (see Consequences).

- **No license today.** The repository is public and has no LICENSE file. Without one, "the default copyright laws apply, meaning that you retain all rights to your source code" ([GitHub: licensing a repository][gh-licensing]), and "the work is under exclusive copyright by default" ([choosealicense.com][cal-none]). GitHub's Terms of Service still let other users view and fork a public repository on GitHub ([GitHub Terms, D.5][gh-tos]).
- **Blender's rule.** Blender says its Python API "is an integral part of the software", and "the GNU GPL license therefore requires that such scripts (if published) are being shared under a GPL compliant license" ([Blender license][blender-license]). Scripts in `Art/` are published in this public repo. ADR 0001 requires a license before the first one is merged. `Art/` holds only `.gitkeep` today.
- **"GPL compliant" isn't defined.** Blender's own extensions platform reads it strictly: "For add-ons, the required license is GNU General Public License v3.0 or later" ([Blender manual: extension licenses][blender-ext]).
- **Owner context (2026-10-20):**
  - Selling the game is undecided. An open license can't be taken back for versions already published. Under the GPL, "this right cannot be withdrawn" ([GPL FAQ][faq-withdraw]). A sole copyright holder can still release future versions under other terms ([GPL FAQ][faq-dual]).
  - Reuse by others is not a concern now, and the repo may go private later. Going private doesn't pull back forks: "Public forks are not made private" ([GitHub: repository visibility][gh-visibility]).
  - Outside contributions are disabled. Only the owner and the studio's agents contribute.
- **Constraints.** AGENTS.md §5 allows only permissive dependencies (MIT, Apache-2.0, BSD, CC0). The game runs on Unity, a proprietary engine.

## Decision

**Option C, split licensing.** The owner chose it on 2026-10-20, and it takes effect when the owner merges this ADR. Only the Blender scripts get an open license. Everything else stays all rights reserved.

| Part | License (SPDX) | Others may |
|---|---|---|
| Python scripts in `Art/` | `GPL-3.0-or-later` | use, study, change, share and sell them, if they pass them on with source under the same license |
| Game code (`Game/`), `.blend` sources, exported art, docs, tools, everything else the owner made | None: all rights reserved, stated in a notice. SPDX lists licenses, so there is no identifier; tooling that needs one can use a custom `LicenseRef-` id ([SPDX spec][spdx-ref]) | only view and fork on GitHub, under GitHub's terms |
| Third-party content (Unity packages, dependencies) | its own license, unchanged | what that license allows |

Why:
- **It meets Blender's rule even in the strictest reading.** `GPL-3.0-or-later` is what Blender's own platform requires for add-ons ([SPDX list][spdx]).
- **It keeps the option to sell.** The scripts run inside Blender while art is being made and never ship in the game. Their license doesn't reach their output: "copyright law does not give you any say in the use of the output people make" ([GPL FAQ][faq-output]). Blender also says "What you create with Blender is your sole property" ([Blender license][blender-license]).
- **It is cheap to reverse in the direction that matters.** Opening more later only means adding a license to future versions. The one irrevocable grant covers small build-time scripts that the game doesn't need to sell.
- **It is closest to today.** Everything but the scripts stays as it is now. It adds the explicit notice choosealicense.com suggests: "a copyright notice and statement that you are not offering any license" ([choosealicense.com][cal-none]).

**Effect on the AGENTS.md §5 dependency rule: none.** MIT, BSD and CC0 code can be used under both GPL-3.0-or-later and all rights reserved. Apache-2.0 is "compatible with version 3 of the GNU GPL" ([GNU license list][gnu-apache]). §5's permissive-only rule keeps copyleft dependencies out of the whole repo, including `Game/`, and this split relies on that. `bpy` belongs to the installed Blender tool. It isn't a repo dependency.

**Rules for agents:**
1. Blender Python lives only in `Art/`, as `.py` files beside their `.blend`. Never embed a script in a `.blend` text block, because it would put GPL code inside an all-rights-reserved file.
2. Every `.py` file in `Art/` starts with `# SPDX-License-Identifier: GPL-3.0-or-later` and a `# Copyright (C) <year> smokim38` line. The follow-up Issue may also add GNU's longer per-file notice ([GNU how-to][gpl-howto]).
3. Never copy code between `Art/` scripts and `Game/`. GPL code in `Game/` would bind the game, and game code copied into a script gets published under the GPL.
4. `Art/` scripts import only Blender's modules (`bpy`, `mathutils` and the like), the Python standard library, and dependencies approved under §5.

## Alternatives considered

**Option A: `GPL-3.0-or-later` for the whole repository**
- *Covers* game code, scripts, `.blend` sources and exported art. "Any material that can be copyrighted can be licensed under the GPL" ([GPL FAQ][faq-any]).
- *Others may* copy, change, share and sell all of it, including the game, if they pass it on with source ([GPL FAQ][faq-money]).
- *§5:* permissive dependencies still fit, so §5 is unchanged. But the game uses Unity's proprietary engine. GPL code that uses a library outside the "system library" exception needs "an explicit notice giving permission to link your program with them" ([GPL FAQ][faq-nonfree]). That adds legal complexity we haven't assessed.
- *Rejected:* anyone could give away or sell the published game, and that can't be withdrawn. It closes the door the owner wants to keep open.

**Option B: permissive for the whole repository (`MIT` for code and scripts, `CC-BY-4.0` for `.blend` sources and exported art)**
- *Covers* everything.
- *Others may* do almost anything, including closed commercial reuse, if they give attribution.
- *§5:* unchanged, and the repo's own licenses would match the licenses §5 prefers.
- *Rejected:* MIT is "compatible with the GNU GPL" ([GNU license list][gnu-expat]), so it arguably meets Blender's rule, but Blender's own platform requires `GPL-3.0-or-later` for add-ons. It also grants the game code and art irrevocably for published versions, like Option A.

**Option D: no license anywhere, and keep the scripts unpublished**
- *Covers* nothing. Others may only view and fork on GitHub.
- *§5:* unchanged.
- *Rejected:* Blender's rule applies only "if published", but AGENTS.md §4 puts the export scripts in `Art/`, and review works through this repo. Scripts would need a private repo, or this repo would have to go private, and the owner hasn't decided that. Public scripts without a license would break Blender's terms.

**Option C with another script license:** `GPL-2.0-or-later` is Blender's default for its own source ([Blender license][blender-license]), but Apache-2.0 "is not compatible with GPL version 2" ([GNU license list][gnu-apache]), and §5 allows Apache-2.0 dependencies. `MIT` has the ambiguity described under Option B.

## Consequences

- **Benefits.** Scripts comply before the first one is merged, and the game code and art keep all rights. The owner can still sell, open-source later through a new ADR, or make the repo private.
- **Tradeoffs.** The repo has two licenses, so GitHub won't show a single detected license. GitHub suggests explaining that kind of complexity in the README ([GitHub: licensing a repository][gh-licensing]). Reviewers check rules 1 to 4 on `Art/` PRs.
- **Irreversible part.** Every published version of an `Art/` script stays `GPL-3.0-or-later` for anyone who has a copy, even if the repo goes private ([GPL FAQ][faq-withdraw], [GitHub][gh-visibility]). Detached public forks of everything else carry only GitHub's view-and-fork terms.
- **Gap: AI authorship.** AI agents write most of the code and art. The US Copyright Office says copyright doesn't extend to purely AI-generated material ([USCO report, Part 2][usco]). "All rights reserved", and any later relicensing, cover only what copyright protects. Not assessed; ask a lawyer before any sale.
- **Outside contributions (no decision now).** GitHub's terms license a contribution under the repo's license notice ([GitHub Terms, D.6][gh-tos]). For the all-rights-reserved parts that gives the owner nothing, so a contributor agreement would be needed to keep the right to sell or relicense. A [Developer Certificate of Origin][dco] could cover the GPL scripts. Opening contributions needs a new ADR.
- **ADR 0001.** This ADR answers ADR 0001's follow-up on the license for `Art/` scripts. It doesn't supersede ADR 0001.
- **Owner decisions (2026-10-20):** the owner chose Option C and set the copyright holder text for notices to the GitHub handle `smokim38`, not a real name (AGENTS.md §5). No decisions are pending.
- **Triggers for a superseding ADR:** a decision to sell or open-source the game, opening outside contributions, or shipping a first public build.
- **Follow-up Issues:**
  - Add the license files (Critical; merge before the first `Art/` script):
    - a root `LICENSE` with the all-rights-reserved notice and the scope table above
    - `Art/LICENSE` with the verbatim GPL-3.0 text ([gnu.org][gpl3])
    - a License section in `README.md`
  - The art pipeline doc, once it is written, adopts rules 1 to 4.
  - Suggested: add the `Art/` header check to the code-reviewer and tech-artist role files. These are protected paths, so that Issue must name them.
  - Before the first player build ships: collect the third-party notices that Unity's terms and the dependency licenses require.

[gh-licensing]: https://docs.github.com/en/repositories/managing-your-repositorys-settings-and-features/customizing-your-repository/licensing-a-repository
[gh-tos]: https://docs.github.com/en/site-policy/github-terms/github-terms-of-service
[gh-visibility]: https://docs.github.com/en/repositories/managing-your-repositorys-settings-and-features/managing-repository-settings/setting-repository-visibility
[cal-none]: https://choosealicense.com/no-permission/
[blender-license]: https://www.blender.org/about/license/
[blender-ext]: https://docs.blender.org/manual/en/latest/advanced/extensions/licenses.html
[gnu-apache]: https://www.gnu.org/licenses/license-list.html#apache2
[gnu-expat]: https://www.gnu.org/licenses/license-list.html#Expat
[faq-output]: https://www.gnu.org/licenses/gpl-faq.html#GPLOutput
[faq-withdraw]: https://www.gnu.org/licenses/gpl-faq.html#CanDeveloperThirdParty
[faq-dual]: https://www.gnu.org/licenses/gpl-faq.html#ReleaseUnderGPLAndNF
[faq-money]: https://www.gnu.org/licenses/gpl-faq.html#DoesTheGPLAllowMoney
[faq-nonfree]: https://www.gnu.org/licenses/gpl-faq.html#NonFreeTools
[faq-any]: https://www.gnu.org/licenses/gpl-faq.html#GPLHardware
[gpl-howto]: https://www.gnu.org/licenses/gpl-howto.html
[gpl3]: https://www.gnu.org/licenses/gpl-3.0.txt
[spdx]: https://spdx.org/licenses/
[spdx-ref]: https://spdx.github.io/spdx-spec/v2.3.1/other-licensing-information-detected/
[dco]: https://developercertificate.org/
[usco]: https://www.copyright.gov/ai/Copyright-and-Artificial-Intelligence-Part-2-Copyrightability-Report.pdf
