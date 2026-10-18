<!--
Rules: AGENTS.md §3 and §8, and docs/studio/workflow.md.
One approved Issue, one branch, one PR. Aim for under ~400 changed lines (not counting .meta and generated files).
Only the owner merges. This repo is public: no secrets or personal info.
-->

Closes #<issue>
Tier: Light | Owner | Standard | Critical
What changed: 1–3 lines
Why: 1 line, or "see Issue"
How tested: commands run, test results, Profiler numbers
Risks / notes: anything the reviewer or owner should know

### Definition of done (AGENTS.md §8; last item from §6)
- [ ] The Issue's acceptance criteria are met.
- [ ] It compiles with no new errors or warnings.
- [ ] Code changes have new or updated tests, and the tests pass.
- [ ] Profiler numbers are included if a hot path changed.
- [ ] The PR has only in-scope changes, includes its `.meta` files, and contains no secrets or personal info.
- [ ] Docs and ADRs are updated if behavior or a decision changed.
- [ ] Protected paths are changed only if the Issue names them (tier is then Critical).
