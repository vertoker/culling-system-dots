# Package readiness results

Source revision: `3f2e1e31b7f854b164c7b76ee8a52e1c1c854ed8`. The later evidence/document commit
does not change the tested package payload. Machine-readable results are in
[Readiness-Summary.json](Readiness-Summary.json); review findings and fixes are in
[Readiness-Review.md](Readiness-Review.md).

## Functional and distribution checks

- Main project: 182/182 EditMode and 16/16 PlayMode tests. PlayMode includes the camera measurement.
- Fresh local Git URL consumer: 181/181 EditMode and 16/16 PlayMode. The main-only fixture-generator
  test accounts for the EditMode difference. No commercial fixtures or copied production package.
- Portable Python checks: 35/35. The standard-library instruction validator passes; GitHub Actions
  is configured to run it and these tests after publication. Remote CI has not been executed.
- All 281 installed payload files match the committed package, with text line endings normalized
  and Unity's added package fingerprint ignored. Entities Graphics comes from ordinary UPM.
- MIT licenses ship at both repository/package roots. Package README, changelog and metadata are present.

## Bake measurement

Unity 6000.6.8f1, Windows DX12, Ryzen 7 7700 (16 logical processors, 15 job workers), RTX 4060 Ti,
about 128 GiB system RAM; Editor update-driven workload with the Editor unfocused.
The independently generated fixture has 512 groups/renderers, 16 unique meshes, 32,768 unique
triangles, 1,048,576 triangles including instances, 128 active cells and resolution 32 (786,432 rays).
An owned unrelated Exclude region verifies that benchmark sampling does not depend on host scenes.

| Requested/effective backend | End-to-end seconds | Sampled whole-Editor working set peak |
|---|---:|---:|
| CPU Jobs/Burst | 16.117 | 2799.64 MiB |
| GPU compute, no CPU recovery | 57.939 | 2816.66 MiB |

These totals include capture, tracing, revalidation, encoding and publication. Background Editor
update cadence contributes to wall time; they are not isolated GPU kernel timings. GPU was slower
on this workload. No general speedup follows from using the GPU backend.

Capture yields between chunks; numeric BVH construction and merge/encoding run in owned workers.
The final source check plus atomic asset write/import still produced the largest observed main-thread
tick: about 482 ms. The progress UI exposes that phase, but there is no hard responsiveness bound.
Memory is sampled, includes the Editor baseline, and is neither exact transient peak nor VRAM usage.
Mono's zero working-set result is marked unavailable; the table uses external Windows sampling
started before both runs. See [raw bake phases](Readiness-Bake.json) and
[process sampling](Readiness-ProcessMemory.json).

## Camera synchronization measurement

The PlayMode fixture renders 3000 synthetic entities into offscreen targets. Each case warms five
frames and samples 30. Event handlers bracket the actual adapter callbacks, including tracked-reader
completion and render-mask mutation. The baseline has the same render entities/cameras without
the culling adapter. This is an Editor measurement, not a Player FPS claim.

| Cameras | Adapter | Publication ms/frame | Begin mask ms/camera | End restore ms/camera | Wall ms/frame |
|---:|---|---:|---:|---:|---:|
| 1 | Off | ~0 | ~0 | ~0 | 3.978 |
| 1 | On | 13.054 | 4.104 | 4.201 | 26.860 |
| 4 | Off | ~0 | ~0 | ~0 | 6.331 |
| 4 | On | 16.858 | 4.062 | 4.098 | 58.249 |

This exposes substantial CPU overhead at this entity count. It establishes a performance problem
to optimize separately, rather than evidence that culling is always beneficial. Wall time includes
the render loop, Editor and test runner. GPU timing and main-thread allocation counters were not
reliably available and are explicitly marked unavailable, not zero. See
[raw camera results](Readiness-Cameras.json).

## Reproduce and limits

Run `python Tools/ValidateAgentDocs.py` and `python -m unittest discover -s Tools/tests -v`.
Use **Tools > Culling System DOTS > Measure Bake Readiness** in an idle development Editor for the
owned bake fixture. It removes its temporary scene/assets on completion. The camera measurement
is `ReadinessBenchmarks.MeasureCameraSynchronizationWithThreeThousandRenderEntities` in PlayMode.
External process sampling uses Windows Get-Process working-set samples at nominal 100 ms intervals
while `.utmp/CullingReadiness.status` reports Cpu/Gpu; do not use Mono's zero counter as a substitute.

The main user scene stayed unnamed, clean and at two roots; it was never saved/discarded.
The Git-installed sample's Windows development Mono build passed in 133.096 seconds with zero
errors/warnings. The new executable was not run; build success is not a fresh Player runtime smoke.
The temporary consumer was removed. Its graceful Editor exit stalled; only the verified owned
temporary process was stopped before deletion. Main Editor routing was restored.
Other platforms/APIs, arbitrary floating-point edge cases, XR/custom rendering and animated occluders
remain outside acceptance. Publication/force push belongs to the user. Build and temporary consumer
cleanup outcomes are recorded in the final machine-readable summary.
