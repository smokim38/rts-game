# Issue #97: 2,000-cube speed-test finding

Source: [Issue #97, finding comment, part 1/1](https://github.com/smokim38/rts-game/issues/97).

Result: both steps pass with a wide margin. RenderMeshInstanced is enough for 2,000 grey-box cubes on this PC.

Setup as the Issue asks: fresh Windows x64 non-development Mono Player per run, Direct3D 11, 1920x1080 borderless full screen, VSync off, uncapped, Frame Timing Stats on, 10 s warm-up, 60 s window, three valid runs per step. Scenario 97-seek-separate-v1: 160x84 plane, blocks at opposite ends, 18 s order cycle, 30 Hz simulation, Burst grid and seek-and-separate jobs, chunks of at most 511 instances (four unit draws at 2,000), fixed orthographic camera, no UI.

| Step | Frames | Average | 1% low (p99) | Max | CPU main thread | GPU | Avg fps | Result |
|---|---|---|---|---|---|---|---|---|
| 1,000 | 75,140 | 0.80 ms | 2.43 ms | 3.84 ms | 0.39 ms | unavailable | 1,252 | pass |
| 2,000 | 68,227 | 0.88 ms | 2.34 ms | 3.38 ms | 0.40 ms | unavailable | 1,137 | pass |

Run averages: 1,000: 0.795 / 0.799 / 0.810 ms; 2,000: 0.879 / 0.879 / 0.880 ms. All six runs kept focus and had one CPU frame-time sample per frame. One early run was invalid (timer origin mismatch, fixed with the native Windows counter) and was replaced.

Gap: GPU timings were unavailable in every frame, with Graphics Jobs on and off, so CPU-bound versus GPU-bound is not determined. ADR 0004 records this as missing GPU samples, which do not invalidate a run. The ladder tool (#103) has to resolve GPU timing collection.

Hardware: Intel Core i7-12700KF (12 cores, 20 threads); NVIDIA GeForce RTX 3050, 6 GB, driver 32.0.16.1742; about 64 GiB RAM; Windows 11 10.0.26200. Reference-class: no. The GPU matches; the CPU is much stronger than the mid-range 6-core target. These numbers are for trends, not certification.

Recommendations:

- Sandbox (#99): keep native arrays, Burst jobs, one completion point and chunked RenderMeshInstanced. No reason seen for the BatchRendererGroup at this scale.
- Ladder (#103): use the native Windows high-resolution counter for timestamps; test delayed, overlapping, missing and duplicate timing batches, focus loss and the six-attempt limit; resolve GPU and render-thread timing; preallocate buffers, drain delayed samples, count frames independently, keep raw data local, and recompute summaries independently.
- Certify the 2,000 step on a reference-class PC before release (ADR 0004 owner decision 2).

Launcher run 01a167f8-2ad3-7630-b6c5-a35f99d332d7. Throwaway code was not committed; there is no PR.
