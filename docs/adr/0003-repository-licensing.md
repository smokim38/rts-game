# ADR 0003: Repository licensing

## Status

Accepted

## Date

2026-10-20

## Context

**This ADR is not legal advice.** The owner makes the final call: the owner chose Option C, based on the architect's analysis of the official sources cited. The owner should still ask a lawyer before any sale. Not assessed here: the law of the owner's country, and how far AI-generated work is protected (see Consequences).

- **No license today.** The repository is public and has no LICENSE file. Without one, "the default copyright laws apply, meaning that you retain all rights to your source code" ([GitHub: licensing a repository][gh-licensing]), and "the work is under exclusive copyright by default" ([choosealicense.com][cal-none]). GitHub's Terms of Service still let other users view and fork a public repository on GitHub ([GitHub Terms, D.5][gh-tos]).
- **Blender's rule.** Blender says its Python API "is an integral part of the software", and "the GNU GPL license therefore requires that such scripts (if published) are being shared under a GPL compliant license" ([Blender license][blender-license]). Scripts in `Art/` are published in this public repo. ADR 0001 requires a license before the first one is merged. `Art/` holds only `.gitkeep` today.
- **"GPL compliant" isn't defined.** The strictest reading comes from the Blender Extensions Platform's submission policy: "For add-ons, the required license is GNU General Public License v3.0 or later" ([Blender manual: extension licenses][blender-ext]). That policy governs extensions submitted to the platform, not scripts published elsewhere. This ADR adopts it as the strictest reading of Blender's general "GPL compliant" rule.
- **Owner context (2026-10-20):**
  - Selling the game is undecided. An open license can't be taken back for versions already published, as long as recipients meet its conditions. Under the GPL, "this right cannot be withdrawn" ([GPL FAQ][faq-withdraw]), and GPLv3 §2 makes its grants "irrevocable provided the stated conditions are met" ([GPL-3.0 text][gpl3]). A sole copyright holder can still release future versions under other terms ([GPL FAQ][faq-dual]).
  - Reuse by others is not a concern now, and the repo may go private later. Going private doesn't pull back forks: "Public forks are not made private" ([GitHub: repository visibility][gh-visibility]).
  - Outside contributions are disabled. Only the owner and the studio's agents contribute.
- **Constraints.** AGENTS.md §5 allows only permissive dependencies (MIT, Apache-2.0, BSD, CC0). The game runs on Unity, a proprietary engine.

## Decision

**Option C, split licensing.** The owner chose it on 2026-10-20, and it takes effect when the owner merges this ADR. Only the Blender scripts get an open license. Everything else stays all rights reserved.

| Part | License (SPDX) | Others may |
|---|---|---|
| Python scripts in `Art/` | `GPL-3.0-or-later` | use, study, change, share and sell them, if they pass them on with source under the same license |
| Game code (`Game/`), `.blend` sources, exported art, docs, tools, everything else the owner made | None: all rights reserved, stated in a notice. SPDX lists licenses, so there is no identifier; tooling that needs one can use a custom `LicenseRef-` id ([SPDX spec][spdx-ref]) | nothing beyond what GitHub's Terms of Service and applicable law allow. On GitHub, users may "use, display, perform and reproduce (by forking)" it "through the Service as permitted by GitHub's functionality" ([GitHub Terms, D.5][gh-tos]). Copyright exceptions in applicable law still apply. |
| Third-party content (Unity packages, dependencies) | its own license, unchanged | what that license allows |

Why:
- **It meets Blender's rule even in the strictest reading.** The Blender Extensions Platform requires `GPL-3.0-or-later` for add-ons submitted to it ([Blender manual][blender-ext]), and this ADR takes that policy as the strictest reading of "GPL compliant" (see Context). `GPL-3.0-or-later` is that license's SPDX identifier ([SPDX list][spdx]).
- **It keeps the game and art exclusive.** The scripts run inside Blender while art is being made and never ship in the game. "The output of a program is not, in general, covered by the copyright on the code of the program" ([GPL FAQ][faq-output-case]), and "copyright law does not give you any say in the use of the output people make from their data using your program" ([GPL FAQ][faq-output]). There is a limit. The program's license can reach output when "substantial parts of the output are copied (more or less) from text in your program" ([GPL FAQ][faq-output]), and then "the copied text in the output is covered by the same license that covers it in the source code" ([GPL FAQ][faq-tools]). Rule 5 below handles that case. Blender also says "What you create with Blender is your sole property" ([Blender license][blender-license]).
- **It is cheap to reverse in the direction that matters.** Opening more later only means adding a license to future versions. The one irrevocable grant, which holds as long as recipients meet the GPL's conditions, covers small build-time scripts that the game doesn't need to sell.
- **It is closest to today.** Everything but the scripts stays as it is now. It adds the explicit notice choosealicense.com suggests: "a copyright notice and statement that you are not offering any license" ([choosealicense.com][cal-none]).

**Effect on the AGENTS.md §5 dependency rule: none, but §5 alone doesn't establish GPL compatibility.** §5 stays as it is for the whole repo: ask the owner before adding any dependency, permissive licenses only, and Critical tier. Its permissive-only rule keeps copyleft dependencies out of the whole repo, including `Game/`, and this split relies on that. Permissive isn't the same as GPL-compatible, though. On GNU's list, Expat (SPDX `MIT`) ([GNU][gnu-expat]), `BSD-2-Clause` ([GNU][gnu-bsd2]), `BSD-3-Clause` ([GNU][gnu-bsd3]) and CC0 ([GNU][gnu-cc0]) are compatible with the GNU GPL, and Apache-2.0 is "compatible with version 3 of the GNU GPL" ([GNU][gnu-apache]). The original 4-clause BSD license (`BSD-4-Clause`) has "practical problems, including incompatibility with the GNU GPL" ([GNU][gnu-bsd4]). So before an `Art/` script uses a dependency, its actual license must be checked against GNU's list (rule 4). `bpy` belongs to the installed Blender tool. It isn't a repo dependency.

**Rules for agents:**
1. Blender Python lives only in `Art/`, as `.py` files beside their `.blend`. Never embed a script in a `.blend` text block, because it would put GPL code inside an all-rights-reserved file.
2. Every `.py` file in `Art/` starts with `# SPDX-License-Identifier: GPL-3.0-or-later` and a `# Copyright (C) <year> smokim38` line. The follow-up Issue may also add GNU's longer per-file notice ([GNU how-to][gpl-howto]).
3. Never copy code between `Art/` scripts and `Game/`. This is a studio rule that keeps each part's license easy to check. It isn't a legal limit on the owner. Game code copied into a script would be published under the GPL. The owner's own code is a different matter: the developer "is not bound by" the GPL ([GPL FAQ][faq-dev]), and a sole copyright holder can license the same code under other terms ([GPL FAQ][faq-dual]). Third-party GPL code would need its own analysis.
4. `Art/` scripts import only Blender's modules (`bpy`, `mathutils` and the like), the Python standard library, and dependencies approved under §5 whose actual license GNU's list shows as compatible with GPLv3 (see above). `BSD-4-Clause` isn't.
5. If an export contains code, templates or artwork copied from an `Art/` script (for example hard-coded mesh data, an embedded image, or a text or C# template the script writes out), the copied part may carry the script's GPL ([GPL FAQ][faq-tools]). The author flags it in the PR, and the reviewer escalates it for an owner decision before merge (AGENTS.md §6).

## Alternatives considered

**Option A: `GPL-3.0-or-later` for the whole repository**
- *Covers* game code, scripts, `.blend` sources and exported art. "Any material that can be copyrighted can be licensed under the GPL" ([GPL FAQ][faq-any]).
- *Others may* copy, change, share and sell all of it, including the game, if they pass it on with source ([GPL FAQ][faq-money]).
- *§5:* unchanged, but every dependency, including those `Game/` uses, would need its actual license checked for GPL compatibility, because not every permissive license is compatible (`BSD-4-Clause`, above). The game also uses Unity's proprietary engine. GPL code that uses a library outside the "system library" exception needs "an explicit notice giving permission to link your program with them" ([GPL FAQ][faq-nonfree]). That adds legal complexity we haven't assessed.
- *Rejected:* the GPL wouldn't stop the owner selling the game ([GPL FAQ][faq-money]), and as sole copyright holder the owner could still release future versions under other terms ([GPL FAQ][faq-dual]). But everyone who gets a copy of a published version could share, change and sell it too, and that can't be withdrawn as long as they meet the license's conditions ([GPL FAQ][faq-withdraw], [GPL-3.0 text][gpl3]). The tradeoff is reduced exclusivity: the owner would keep the right to sell but lose exclusive rights to every published version of the game and art.

**Option B: permissive for the whole repository (`MIT` for code and scripts, `CC-BY-4.0` for `.blend` sources and exported art)**
- *Covers* everything.
- *Others may* do almost anything, including closed commercial reuse, if they meet each license's conditions. MIT requires that "the above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software" ([MIT][mit]). CC-BY-4.0 requires identifying the creator, keeping the copyright notice and the notices that refer to the license and its warranty disclaimer, linking to the material where practical, saying whether it was modified, and including the license text or a link to it (§3(a)). It also forbids adding terms or technological measures that restrict what recipients may do (§2(a)(5)(B)) ([CC BY 4.0 legal code][cc-by]).
- *§5:* unchanged. `CC-BY-4.0` isn't on §5's list, but §5 governs the dependencies the repo takes in. Licensing the repo's own assets under it wouldn't change the dependency policy.
- *Rejected:* MIT is "compatible with the GNU GPL" ([GNU license list][gnu-expat]), so it arguably meets Blender's general "GPL compliant" rule. It doesn't meet the strictest reading this ADR uses, the Blender Extensions Platform's submission policy, which requires `GPL-3.0-or-later` for add-ons ([Blender manual][blender-ext]). It also grants the game code and art for published versions, like Option A, and recipients keep that grant as long as they meet each license's conditions.

**Option D: no license anywhere, and keep the scripts unpublished**
- *Covers* nothing. Others get no license beyond what GitHub's Terms of Service and applicable law allow (see the Decision table).
- *§5:* unchanged.
- *Rejected:* Blender's rule applies only "if published", but AGENTS.md §4 puts the export scripts in `Art/`, and review works through this repo. Scripts would need a private repo, or this repo would have to go private, and the owner hasn't decided that. Public scripts without a license would break Blender's terms.

**Option C with another script license:** `GPL-2.0-or-later` is Blender's default for its own source ([Blender license][blender-license]), and it would also work. Apache-2.0 "is not compatible with GPL version 2" ([GNU license list][gnu-apache]), but that rules out only `GPL-2.0-only`. Code under "version 2 or later" "is compatible with GPLv3 because GPLv3 is one of the options it permits" ([GPL FAQ][faq-v2v3]), so a script combined with an Apache-2.0 dependency could be passed on under GPLv3. `GPL-3.0-or-later` stays the explicit baseline because it is what Blender's extensions platform requires for add-ons ([Blender manual][blender-ext]), and it doesn't rely on that "or later" upgrade. `MIT` has the ambiguity described under Option B.

## Consequences

- **Benefits.** Scripts comply before the first one is merged, and the game code and art keep all rights. The owner can still sell, open-source later through a new ADR, or make the repo private.
- **Tradeoffs.** The repo has two licenses, so GitHub may not correctly detect or display the repository's licenses. GitHub suggests explaining that kind of complexity in the README ([GitHub: licensing a repository][gh-licensing]). Reviewers check rules 1 to 5 on `Art/` PRs and on PRs that add exports.
- **Irreversible part.** Every published version of an `Art/` script stays `GPL-3.0-or-later` for anyone who has a copy and meets the license's conditions, even if the repo goes private ([GPL FAQ][faq-withdraw], [GitHub][gh-visibility]). Under GPLv3 §2 the grants are "irrevocable provided the stated conditions are met" ([GPL-3.0 text][gpl3]). Detached public forks of everything else carry no license beyond what GitHub's Terms of Service and applicable law allow.
- **Gap: AI authorship.** AI agents write most of the code and art. The US Copyright Office says copyright doesn't extend to purely AI-generated material ([USCO report, Part 2][usco]). "All rights reserved", and any later relicensing, cover only what copyright protects. Not assessed; ask a lawyer before any sale.
- **Outside contributions (no decision now).** GitHub's terms license a contribution under the repo's license notice ([GitHub Terms, D.6][gh-tos]). For the all-rights-reserved parts that gives the owner nothing, so a contributor agreement is worth exploring with counsel to keep the right to sell or relicense. A [Developer Certificate of Origin][dco] could cover the GPL scripts. A DCO certifies a contribution's provenance, that the contributor has "the right to submit it under the open source license indicated in the file" ([DCO][dco]). It doesn't assign copyright. Any future sign-off process must follow AGENTS.md §5's privacy rule: no real names or private emails in the public repo. Opening contributions needs a new ADR.
- **ADR 0001.** This ADR answers ADR 0001's follow-up on the license for `Art/` scripts. It doesn't supersede ADR 0001.
- **Owner decisions (2026-10-20):** the owner chose Option C and set the copyright holder text for notices to the GitHub handle `smokim38`, not a real name (AGENTS.md §5). No decisions are pending.
- **Triggers for a superseding ADR:** a decision to sell or open-source the game, opening outside contributions, or shipping a first public build.
- **Follow-up Issues:**
  - Add the license files (Critical; merge before the first `Art/` script):
    - a root `LICENSE` with the all-rights-reserved notice and the scope table above
    - `Art/LICENSE` with the verbatim GPL-3.0 text ([gnu.org][gpl3])
    - a License section in `README.md`
  - The art pipeline doc, once it is written, adopts rules 1 to 5.
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
[gnu-bsd2]: https://www.gnu.org/licenses/license-list.html#FreeBSD
[gnu-bsd3]: https://www.gnu.org/licenses/license-list.html#ModifiedBSD
[gnu-bsd4]: https://www.gnu.org/licenses/license-list.html#OriginalBSD
[gnu-cc0]: https://www.gnu.org/licenses/license-list.html#CC0
[faq-output]: https://www.gnu.org/licenses/gpl-faq.html#GPLOutput
[faq-output-case]: https://www.gnu.org/licenses/gpl-faq.html#WhatCaseIsOutputGPL
[faq-tools]: https://www.gnu.org/licenses/gpl-faq.html#CanIUseGPLToolsForNF
[faq-dev]: https://www.gnu.org/licenses/gpl-faq.html#DeveloperViolate
[faq-v2v3]: https://www.gnu.org/licenses/gpl-faq.html#v2v3Compatibility
[faq-withdraw]: https://www.gnu.org/licenses/gpl-faq.html#CanDeveloperThirdParty
[faq-dual]: https://www.gnu.org/licenses/gpl-faq.html#ReleaseUnderGPLAndNF
[faq-money]: https://www.gnu.org/licenses/gpl-faq.html#DoesTheGPLAllowMoney
[faq-nonfree]: https://www.gnu.org/licenses/gpl-faq.html#NonFreeTools
[faq-any]: https://www.gnu.org/licenses/gpl-faq.html#GPLHardware
[gpl-howto]: https://www.gnu.org/licenses/gpl-howto.html
[gpl3]: https://www.gnu.org/licenses/gpl-3.0.txt
[spdx]: https://spdx.org/licenses/
[spdx-ref]: https://spdx.github.io/spdx-spec/v2.3.1/other-licensing-information-detected/
[mit]: https://opensource.org/license/mit
[cc-by]: https://creativecommons.org/licenses/by/4.0/legalcode.en
[dco]: https://developercertificate.org/
[usco]: https://www.copyright.gov/ai/Copyright-and-Artificial-Intelligence-Part-2-Copyrightability-Report.pdf
