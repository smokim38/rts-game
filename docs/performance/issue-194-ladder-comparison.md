# Issue #194: committed-head ladder comparison

Context: Issue 194 route-request fix.

Measured commit: `1c6e2294887cbd6f2e60768c097ad1f5e95786fd`.

The full ladder passed all nine steps through 2,000 units, with 27 valid runs and no replacement attempts. No step regressed against the requested [450c41c comparison](unit-ladder/2026-11-03-450c41c.md), using ADR 0004 and ADR 0009. Hardware and scenario version match that entry; no three-run average spread exceeds the tolerance.

The unchanged runner selected [136c1ab](unit-ladder/2026-11-02-136c1ab.md) through its `main` addition-history lookup. Its [generated entry](unit-ladder/2026-11-03-1c6e229.md) is preserved as written and also reports no regression. The table below supplies the comparison with `450c41c` requested by Issue #194. Each value is the median of three runs, in milliseconds.

| Units | Average before | Average after | 1% low before | 1% low after | Maximum before | Maximum after | Result |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 10 | 0.753 | 0.741 | 2.200 | 2.207 | 5.016 | 4.382 | pass, no regression |
| 50 | 0.751 | 0.748 | 2.251 | 2.252 | 5.171 | 3.438 | pass, no regression |
| 100 | 0.754 | 0.752 | 2.267 | 2.269 | 4.795 | 3.603 | pass, no regression |
| 200 | 0.759 | 0.756 | 2.276 | 2.260 | 4.786 | 3.425 | pass, no regression |
| 300 | 0.762 | 0.760 | 2.264 | 2.257 | 5.254 | 3.391 | pass, no regression |
| 400 | 0.762 | 0.761 | 2.234 | 2.217 | 4.601 | 3.356 | pass, no regression |
| 500 | 0.763 | 0.763 | 2.166 | 2.142 | 3.588 | 3.418 | pass, no regression |
| 1000 | 0.785 | 0.783 | 2.268 | 2.261 | 3.477 | 3.618 | pass, no regression |
| 2000 | 0.843 | 0.840 | 2.231 | 2.217 | 3.629 | 3.494 | pass, no regression |

## Validation

- Reused the existing `PerformanceLadderBuilder.BuildWindows` build stamped with `-ladder-commit 1c6e2294887cbd6f2e60768c097ad1f5e95786fd`. All 287 source fingerprints matched the clean checkout before measurement. No code or Unity asset changes were made in this ladder-only run.
- `node --test tools/unit-ladder.test.js`: 18 passed.
- `node tools/unit-ladder.js "re-run: Issue 194 route-request fix" no`: exit 0; ladder height 2,000.
- Recomputed every attempt from its raw CSV and metadata with the ladder summarizer and checked Player logs: all 27 runs valid, all recorded attempt metrics match, 2,114,130 frames total. Raw files remain under ignored `Game/Logs/unit-ladder/2026-11-03T08-06-35-331Z/`.
- The existing post-build EditMode result, `Game/Logs/194-ladder-editmode.xml`, records 285 passed and zero failed. EditMode tests were not repeated in this ladder-only run.
- `node tools/unity-tree-check.js`: no unexpected changes remain (ADR 0010); no rewrites to restore.

Reference-class: no. The RTX 3050 matches the target, but the i7-12700KF is stronger than the reference CPU, so this is trend evidence. GPU timing samples were unavailable for every frame, as in the comparison; ADR 0004 permits missing optional GPU samples. This holding-position ladder is regression evidence; the RequestFields stress measurements belong to the preceding implementation run.
