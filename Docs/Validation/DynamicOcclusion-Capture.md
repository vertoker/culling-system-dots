# Dynamic occlusion ECS capture: Task 2 evidence

Measured on 2026-11-06, Unity 6000.6.8f1 Editor/Windows DX12, stock Entities Graphics 6.6,
Ryzen 7 7700 and RTX 4060 Ti. The implementation is based on `76eccbc`; exact measured source
SHA-256 values are in the [current capture report](DynamicOcclusion-Capture.json). This report
covers registry capture and logical preparation, not GPU uploads, rendering or Player acceptance.

## Functional results

- Initial six ECS contracts: `f6c3d3b5d87848c497c4f8b7b5a87f9a`, 1 passed / 5 failed against stubs.
- Real legacy/dynamic ownership RED: `b3b0fdc4abaa42b9b8cab6733e60d774`, 0/3. Both registration
  orders and rejected-publication rollback initially failed; the common graphics claim owner fixes them.
- Configuration RED: `9d8bb5d3cdca4d599d40bd23a6ec4c29`, 13/15. Configuration was unimplemented;
  the other failure was a test fixture exceeding the documented block cap, corrected to a bounded size.
- Final coupled EditMode: `ae552c14bf7a4195ae9ff8914f0c131e`, 104/104, zero failed/skipped:
  15 ECS capture contracts, 47 dynamic registry/update contracts and 42 existing Core/Runtime contracts.
- Final coupled PlayMode: `29e45925a717472d8d498c6d5c230772`, 26/26, zero failed/skipped:
  three ownership regressions plus the previous 23 camera/shadow/sample/transport checks.

Full-Entity destruction/reuse, hidden graphics transform capture, opt-out, original disabled
baselines, invalid transforms, capture-budget exhaustion/recovery, rotated/scaled geometry,
unchanged inputs, rejected registrations and World teardown are covered. The capture helper
completes registered component writers once and executes a Burst numeric Job; it never disables
simulation. Temporary capture payload is charged to the registry resource budget.

Unity occasionally reports its pre-existing `DefaultScenario` Stopping/Idle assertion at test
exit. The functional runs above completed; this is not recorded as a package compile error.

## Scoped capture measurements

Explicit PlayMode profile `73a834316390408b9a7221422af0bb18`: 1/1, 20 warmup and 120 measured
frames per case. All instances in moving cases change pose each frame. Input transform writes,
registration, snapshots, rendering and actual GPU upload are outside the measured intervals.

| Instances | Moving | Capture median / p95 (ms) | Preparation median / p95 (ms) | Pending at end |
| --- | --- | --- | --- | --- |
| 512 | No | 0.261 / 0.457 | 0.003 / 0.005 | 0 |
| 512 | All | 1.921 / 2.181 | 0.111 / 0.137 | 0 |
| 4096 | No | 1.862 / 2.124 | 0.003 / 0.005 | 0 |
| 4096 | All | 15.060 / 16.657 | 0.413 / 0.674 | 2458 |

The preparation ceiling is 256 KiB/frame. The moving 4096 case saturates it; the registry's
160-byte instance preparation payload exceeds that ceiling when all objects change. Pending
occluders are omitted; `Prepared` never proves current GPU upload. These are configured work
limits and reduced culling availability, not dropped simulation updates or stale hidden results.

The [earlier scalar report](DynamicOcclusion-Capture-Scalar.json), job
`57d311f0747c4071871ed3258b10a661`, recorded capture medians 2.531/4.119 ms (512 stationary/moving)
and 19.918/32.617 ms (4096). It used the former 64-item preparation limit. The optimized run uses
Burst component capture, cached owned geometry bounds and a byte-bounded preparation loop.
The old source fingerprints preserve that comparison; it is not an end-to-end speedup claim.

The installed Mono allocation counter fails a known-allocation availability probe. Current
allocation values are `-1` (unavailable); the earlier report's zeros are not allocation evidence.
Managed registry/block update cost remains material for fully moving large populations. Task 6
must measure and, if needed, optimize the complete path before any release performance claim.

GPU retirement, current-view conservative depth correctness, production indirect ownership
transfer, the dynamic city sample and Player/Git-consumer acceptance remain Tasks 3-7.
