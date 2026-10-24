# Unit performance ladder

The protocol and pass rules are in [ADR 0004](../../adr/0004-unit-simulation-architecture.md#performance-ladder). Scenario version 1 uses the sandbox's native-array simulation and instanced cubes. Units hold position: the build currently applies spawn commands at 30 Hz and fills draw matrices and draws every frame. Movement, targeting, combat and commander AI are not implemented. The seed is stored but no random work occurs. Layout uses the sandbox's compact block placement, small cubes for the first faction and large cubes for the second.

Before measuring, read Issue #97's speed-test finding, commit the measured sources, close the Editor, set Windows power mode to best performance and leave this PC idle. The Producer reserves the PC: no other Unity run may overlap a ladder. The Player requires a real 1920×1080 borderless desktop with focus. A sandbox without these cannot produce valid measurements; an offscreen render request is not a substitute.

From the repository root, use these two PowerShell commands. The direct Unity command is the only escalated command. The pipe waits for the GUI executable to finish. It generates the scene/scenario and builds both release and development Windows x64 Mono Players without changing Editor Build Settings.

```powershell
& 'C:\Program Files\Unity\Hub\Editor\6000.3.23f1\Editor\Unity.exe' -batchmode -nographics -projectPath "$PWD\Game" -logFile "$PWD\Game\Logs\ladder-build.log" -executeMethod Northwatch.EditorTools.PerformanceLadderBuilder.BuildWindows -ladder-commit FULL_40_HEX_COMMIT_SHA -quit | Out-Null
node tools/unit-ladder.js baseline no
```

Replace `FULL_40_HEX_COMMIT_SHA` with the Producer's full measured commit SHA. Replace `baseline` with `"PR #123"` or `"re-run: reason"` as appropriate. The final argument is the reference-class decision: `yes` only for the ADR's RTX 3050 6 GB and mid-range six-core CPU; otherwise `no`. A pass on other hardware supports trends only. Check the build log before running. Changes to the scenario or harness start a new scenario version/comparison series.

The runner refuses uncommitted sources, a build for another SHA, changed source hashes, or changes to sources/hardware during measurement. It runs every step in order, requesting three valid fresh Players within six attempts per step. Each Player warms up for 10 seconds, records 60 seconds and drains delayed timings for two seconds. The preallocated buffer holds one million frames; overflow invalidates the run. Callback timestamps use native Windows QueryPerformanceCounter, sharing the FrameTimingManager clock origin and frequency. Native frame-start timestamps match disjoint intervals between consecutive script callbacks; rendered frames are counted independently. Overlapping timing polls are deduplicated by timestamp; two distinct samples matching one frame invalidate it. Metadata records matched and unmatched sample counts; unmatched samples invalidate the run. Frames after the closed window are ignored during the drain, while delayed window samples still match. Missing samples remain missing after the drain. Setup failures log their reason and exit with code 1; named exceptions in Player logs also invalidate a run.

Raw CSV, metadata, Player logs and development captures stay under ignored `Game/Logs/`. CPU/core counts, GPU driver versions and Windows versions come from explicitly selected CIM properties; Unity supplies the rendered GPU and memory sizes. No device name or identifier is read. Public entries contain an allow-listed summary with all attempt results, three-run spread and optional timing missing counts. Comparison entries come from the local `main` ref's addition history; update that ref before launching the worktree. A failed step still records its curve; invalid runs, regressions and previously passing steps that fail give the command a nonzero exit code. Every entry is append-only and names the full measured commit; the Producer can commit the entry alone afterward.

For the separate allocation check, after building and with no other Unity run active, run the development Player with `-ladder-units 2000 -ladder-profile -ladder-output "$PWD\Game\Logs\unit-ladder\profile" -logFile "$PWD\Game\Logs\unit-ladder\profile.log"`. Its executable is `Game/Builds/PerformanceLadderDevelopment/PerformanceLadder.exe`. After it exits, run the same direct Unity batch command with `-executeMethod Northwatch.EditorTools.LadderProfileAnalysis.Analyze` and a separate log. Analysis rejects fewer than 200 samples or any managed allocation beneath `Northwatch.Simulation` or `Northwatch.Drawing`, and writes `Game/Logs/unit-ladder/profile-summary.txt`. Keep the raw capture locally for QA. Development captures are not ladder measurements.

Tests:

```powershell
node --test tools/unit-ladder.test.js
& 'C:\Program Files\Unity\Hub\Editor\6000.3.23f1\Editor\Unity.exe' -batchmode -nographics -projectPath "$PWD\Game" -logFile "$PWD\Game\Logs\ladder-tests.log" -runTests -testPlatform EditMode -testResults "$PWD\Game\Logs\ladder-editmode.xml" | Out-Null
```
