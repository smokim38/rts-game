# Issue 232: moving ladder check

Status: blocked. These are working-tree checks, not a committed ladder entry.

Scenario 2 prequeues both factions' move orders through the existing command queue.
Orders execute on ticks 1, 601, 1201 and 1801, independent of render-frame batching.
The asset keeps seed 103 and the 20-second cycle. It places the blocks at x=-30
and x=30 so both speed classes can cross the middle within a cycle. Destinations
alternate between x=-120 and x=120, with seeded z offsets in [-8, 8]. These
parameters are serialized in `Game/Assets/Data/PerformanceLadder.asset`.

The ladder checks the number of units with a current move order every measured
frame, in addition to the faction and draw counts. No normal movement system,
stall rule, timing threshold or pass rule is disabled or changed.

## Blocking result

The deterministic schedule and future-command/add-response checks pass.
The 10-unit test retains all 10 orders throughout the full simulated measurement
window, and both factions cross the middle.

At 2,000 units, the first faction's orders end on tick 500 (16.667 simulation
seconds), with `ArrivalRule=4`, the existing six-second group-stall rule in
`PreferredVelocityJob`. The minimum current-order count is 1,000. Both factions
do cross the middle over the complete window, but this workload fails the
continuous-active-order requirement. The failing assertion remains enabled.
Changing the movement stall behavior needs the Producer to resolve scope; this
Issue does not silently change it or soften the ladder check.

## Comparison boundary

The latest old-series entry is
[`2026-11-05-7392d45.md`](unit-ladder/2026-11-05-7392d45.md).

| Figure | Scenario 1, stationary tanks | Scenario 2, moving orders |
| --- | --- | --- |
| Ladder height | 2,000 | Not measured |
| 2,000 average frame time | 0.844 ms | Not measured |
| 2,000 1% low frame time | 2.305 ms | Not measured |
| 2,000 maximum frame time | 4.385 ms | Not measured |
| 2,000 CPU main time | 0.321 ms | Not measured |
| 2,000 GPU time | Unavailable | Not measured |
| Active-order check | Not present | Fails at tick 500 in EditMode |

**The two scenario versions are not a like-for-like series.** No old entry was
edited. The old PC is recorded as reference-class `no`.

The official command
`node tools/unit-ladder.js "re-run: Issue 232 moving orders" no` refuses this
working tree with `Commit the measured sources first; working tree must be clean.`
This assignment forbids committing, so a SHA-bound full ladder entry cannot be
created here. The Producer must commit the measured sources, rebuild both
Players for that SHA, and run the unchanged full ladder protocol. Source checks
must stay enabled; these uncommitted changes must not be attributed to the base
commit.

## Owned assets

- `Game/Assets/Data/PerformanceLadder.asset`: changed by the ladder builder.
- `Game/Assets/Data/PerformanceLadder.asset.meta`: unchanged.
- `Game/Assets/Scenes/PerformanceLadder.unity`: builder rerun, unchanged.
- `Game/Assets/Scenes/PerformanceLadder.unity.meta`: unchanged.

No package or settings change is intended. Changes are uncommitted; no PR was
opened.

## Validation

- `node --test tools/unit-ladder.test.js`: 18/18 pass.
- Full EditMode suite: 319/323 pass initially. Three follow-up tests pass with
  graphics enabled: both vehicle-image cases and the original #224 movement
  byte baseline. That baseline still uses its original hash, excluding only
  the new diagnostic summary counter. The remaining failure is the enabled
  2,000-unit active-order test described above.
- Ladder builder, release Player build and development Player build succeed.
  The development build is diagnostic working-tree output, not evidence for
  the unchanged base SHA. No project compiler errors or warnings were found.
- A 2,000-unit development capture records 60.0135 seconds after 10.0090 seconds
  of warm-up at the existing diagnostic 25 fps. Its metadata reports
  `activeUnitsValid=false`, confirming the active-order failure in a Player.
- `LadderProfileAnalysis.Analyze`: simulation has 2,998 samples, 0.015638 ms
  average per marker sample and **0 B GC Alloc**; drawing has 1,499 samples,
  0.034049 ms average and **0 B GC Alloc**. These capped development marker
  timings are not release ladder figures. All retained interior frames were
  scanned, including the order-cycle boundaries.
- Raw capture: 119,923,093 bytes; analysis peak working set 2,613,899,264 bytes;
  peak committed memory 2,912,636,928 bytes. Local evidence is under ignored
  `Game/Logs/unit-ladder/232-profile*`; test results are
  `Game/Logs/232-editmode.xml` and `Game/Logs/232-followup-tests.xml`.
- Computer Use could not connect to its native pipe on either attempt. No
  foreground verification was available; the diagnostic Player exited itself.
  Its own metadata reports no focus loss.
- `git diff --check`: pass.

## ADR 0010 tree check

After the final Unity run, the check used one `--allow` for each of the four
owned asset and meta paths above. It exited 1 because that asset-only allowlist
does not contain the intentional C# edits. Every path it named, retaining its
line prefix, follows. All `Unexpected:` entries here are deliberate source/test
edits for this Issue; no unrelated file or settings rewrite remains. No restore
failed and nothing was staged. See [ADR 0010, working-tree rewrites](../adr/0010-unity-rewritten-settings.md#when-a-rewrite-shows-up-in-a-working-tree).

```text
Unexpected: Game/Assets/Code/Presentation/Diagnostics/LadderScenario.cs ( M); left untouched.
Unexpected: Game/Assets/Code/Presentation/Diagnostics/PerformanceLadder.cs ( M); left untouched.
Unexpected: Game/Assets/Code/Simulation/ApplyCommandsJob.cs ( M); left untouched.
Unexpected: Game/Assets/Code/Simulation/FillDrawDataJob.cs ( M); left untouched.
Unexpected: Game/Assets/Code/Simulation/UnitCommand.cs ( M); left untouched.
Unexpected: Game/Assets/Code/Simulation/UnitSimulation.cs ( M); left untouched.
Unexpected: Game/Assets/Code/Simulation/UnitState.cs ( M); left untouched.
Unexpected: Game/Assets/Code/Tests/EditMode/EditorTools/PerformanceLadderBuilderTests.cs ( M); left untouched.
Unexpected: Game/Assets/Code/Tests/EditMode/EditorTools/TankMeshBuilderTests.cs ( M); left untouched.
Unexpected: Game/Assets/Code/Tests/EditMode/Presentation/LadderFrameBufferTests.cs ( M); left untouched.
Allowed: Game/Assets/Data/PerformanceLadder.asset ( M); left untouched.
```

Other intentional changes are `tools/unit-ladder.js`,
`docs/performance/unit-ladder/README.md` and this report.
