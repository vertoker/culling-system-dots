# Dynamic occlusion: full-population Player comparison

**Status:** All 24 scale cases and the 31,200-row CSV audit are complete.
Every pair regresses against ordinary Entities Graphics. Lifetime stress is
still running. The performance gate failed; this remains experimental.

## Revisions and environment

Production package: `29d7034fc91d12ed2f71a40572e5ef24ef0db7b3`, including the
inside/near-wall correction in `60545b1`. Unity 6000.6.8f1, stock Entities/Graphics
6.6, URP 17.6, Windows DX12, Ryzen 7 7700 and RTX 4060 Ti. The single Git-install
consumer resolved that committed package. Development harnesses are outside it.

Scale build `build-21ae5b5034` used harness
`6e5d2452d97029ff29cd753db2c1e41f86e991dc`: 7.923 seconds, zero errors,
two warnings, 104.62 MB. It completed cases 0-19, then failed the next layout's
reference triangle-counter check. Its report remains `Complete: false`; no timed
case failed. The reference workflow formerly waited a fixed twelve frames after
switching from GPU presentation to stock rendering. Diagnostic build
`build-57b0a939a2` waited for fresh frames and a stable counter threshold. Both
409,600-object proofs then passed. No production change was made for this
harness correction.

Build `build-3ae569da2e` used harness
`47300ff1eb4543ace70c05cd12ce42f8296b954a`: 9.291 seconds, zero errors,
two warnings, 104.62 MB. It adds bounded resume and stronger reference diagnostics;
the timed Measure method is unchanged. Its separate all-scale proof run passed
all six layouts, including eight moving GPU frames between layout transitions.
Cases 20-23 completed in this build. The combined audit preserves both harness revisions
and the first report's failure.

## Workload and physical coverage

The user-selected populations are **4,096, 40,960 and 409,600** cubes. A fixed
orthographic camera renders a 2560 by 1440 single-sample texture, previewed in a
visible 1280 by 720 Player window. A non-overlapping grid gives every cube enough
pixel footprint to identify it even at the largest size. This unlit fixture has
no ground, lights or shadows. Open and partially occluded layouts each run static
and fully moving populations, with ordinary/feature pairs: 24 cases. Moving cubes
execute identical bounded deterministic jitter within their cells. This is not
the moving-camera game workload required by the original fast-path criterion.

Before timing, every bounds must fit the frustum. Unique 24-bit color IDs prove
all N objects rasterize in the open stock and GPU images. The occluded stock
image must contain the independently calculated exposed grid population; the GPU
image must lose none of those IDs. All stock bindings must be enabled, and the
reference triangle counter must reach at least twelve triangles per cube. Every
eligible target, including the optional wall, must be ready before and during
every measured feature frame. Partial transfer is never accepted as full coverage.

| Targets | Open stock/GPU IDs | Occluded stock/GPU IDs | Lost IDs |
| ---: | ---: | ---: | ---: |
| 4,096 | 4,096 | 1,472 | 0 |
| 40,960 | 40,960 | 14,296 | 0 |
| 409,600 | 409,600 | 143,360 | 0 |

The 4,096/40,960 cases warm for 300 frames and measure 1,800. The largest
diagnostic warms for 60 and measures 300; it does not satisfy the original
1,800-frame fast-path gate. Both paths advance the same fixed 1/60-second
simulation. The Player checks exact update counts and matching final transforms
across every pair. Setup, proof readback, teardown and serialization are outside
timing. There is no assertion readback in measured frames.

Explicit scale settings: MaxInstances N+1,024; update queue 2N+4,096; block
membership 4N+4,096; upload 128 MiB/frame; accounted payload 2 GiB. These are test
settings, not library defaults or whole-process memory limits. Only one GPU
workload runs at a time.

## Measurement interpretation

End-of-frame wall intervals provide total median/p95/p99. FrameTimingManager
supplies main/render CPU and GPU samples where available; missing values remain
-1 with zero available samples. Delayed samples are not synchronized stage
durations to subtract. The CSV audit recomputes every distribution and verifies
every row and paired state. Per-frame allocation recording is probed with a known
allocation; an unavailable recorder does not mean zero allocations. Collection
counts include warmup.

Whole-frame draw/triangle counters can lag or aggregate frames. Some observed
samples contain only UI or roughly two frames' work. They are not a per-view
visibility mask and do not alone prove a stock-rendering defect. Frozen color-ID
and enabled-binding checks establish population coverage separately from timing.

Feature-off disables capture and presentation while retaining registered data
and reusable resources. It measures the package's switch, not uninstalling it.
Its UploadedBytes can retain the last enabled value and is not an off-path upload
measurement. Accounted payload excludes engine/driver/process memory; managed
heap is recorded separately. GPU timing alone cannot establish improvement.

## Observed frame times

[Machine-readable evidence](DynamicOcclusion-Performance.json) contains all
distributions, sample availability, hashes of the 24 raw CSV files, source-run
failure status and the identical timed-method SHA256. The audit checked all
31,200 rows, exact paired state hashes and simulation counts. All 15,600
measured feature frames used the complete eligible set, with no fallback,
pending updates or queue-overflow frames.

Total frame times below are milliseconds. OFF is ordinary rendering; ON is the
experimental route. Lower is better.

| Targets | Layout | Motion | OFF median / p95 / p99 | ON median / p95 / p99 | Median slowdown |
| ---: | --- | --- | ---: | ---: | ---: |
| 4,096 | open | static | 0.286 / 0.734 / 1.530 | 7.119 / 8.598 / 9.015 | 24.91x |
| 4,096 | open | all moving | 0.312 / 0.796 / 1.598 | 12.924 / 14.606 / 15.329 | 41.46x |
| 4,096 | occluded | static | 0.292 / 0.910 / 1.755 | 8.197 / 9.533 / 10.161 | 28.07x |
| 4,096 | occluded | all moving | 0.319 / 0.950 / 1.415 | 13.773 / 14.941 / 15.973 | 43.15x |
| 40,960 | open | static | 0.405 / 0.969 / 1.579 | 66.563 / 69.474 / 72.083 | 164.23x |
| 40,960 | open | all moving | 0.594 / 1.221 / 2.063 | 125.943 / 131.231 / 134.026 | 211.92x |
| 40,960 | occluded | static | 0.405 / 0.763 / 1.451 | 66.639 / 69.693 / 73.654 | 164.58x |
| 40,960 | occluded | all moving | 0.602 / 1.357 / 2.483 | 129.810 / 143.067 / 151.485 | 215.81x |
| 409,600 | open | static | 2.551 / 3.188 / 3.670 | 669.996 / 707.230 / 736.596 | 262.62x |
| 409,600 | open | all moving | 8.743 / 10.624 / 12.013 | 1337.897 / 1411.462 / 1486.336 | 153.03x |
| 409,600 | occluded | static | 2.607 / 3.718 / 4.342 | 719.120 / 756.308 / 791.598 | 275.83x |
| 409,600 | occluded | all moving | 8.131 / 10.310 / 11.486 | 1398.154 / 1451.973 / 1478.947 | 171.96x |

The enabled main-thread medians range from 7.113 to 1,398.140 ms; enabled
render-thread medians range from 0.056 to 0.077 ms. GPU timing availability
varies by case (253-1,555 enabled samples); missing samples are retained as
unavailable. The per-frame GC-allocation recorder was unavailable in every
case despite the allocation probe. Collection-counter totals are available
and substantially higher when enabled; no zero-allocation claim is made.

Enabled accounted payload peaks at 293.76 MiB, maximum managed heap at
675.09 MiB, and uploads at 46.88 MiB/frame. Even static feature cases rebuild
and upload the complete packet. All remain below the deliberately raised
test budgets; these are not whole-process caps. The off path retains resources,
and its cached upload diagnostic must not be interpreted as current uploads.

## Limiting work and decision

The completed cases spend most of the enabled frame on the main CPU thread and
record many more collection-counter increments than the ordinary path. This is
end-to-end evidence, not an isolated per-pass profiler capture. Source inspection
shows that [binding capture](../../Assets/CullingSystemDOTS/Runtime/Dynamic/Ecs/DynamicOcclusionBindings.cs)
walks all bindings into temporary rows and applies updates on the owning thread;
[snapshot publication](../../Assets/CullingSystemDOTS/Runtime/Dynamic/OcclusionWorld.cs)
allocates and fills a complete native instance snapshot plus a material array.
These are concrete candidates for reducing CPU work, not measured percentages
of the frame. There is no explicit GC.Collect in this runtime path.

Keep algorithm 3 experimental. A future optimization effort would need persistent
native instance storage, dirty/batched updates, fewer complete CPU packet rebuilds
and resident GPU data, followed by the same correctness and A/B gates. A looser
visibility oracle or partial population does not solve this performance result.
Algorithm 2 remains separate work. This result does not prove runtime geometric
occlusion impossible; it rejects this implementation as a demonstrated speedup.

## Reproduction

Build the imported DynamicCity scene as a non-development Windows Mono Player
with DX12 and Frame Timing Stats. Use a visible window: hidden execution did not
produce camera callbacks in the controlled attempt.

```text
--scale-dynamic-city --scale-output <absolute-output-directory>
--source-revision 29d7034fc91d12ed2f71a40572e5ef24ef0db7b3
--harness-revision <harness-commit> -screen-fullscreen 0
-screen-width 1280 -screen-height 720 -logFile <absolute-log-file>
```

`--scale-proof-only` runs six proofs without measurements. To resume the last
four cases, keep the default population order and use `--scale-skip 20
--scale-limit 4`. Without harness flags, the executable starts the readable city.

## Lifetime stress

Run separately after comparative timings:

```text
--benchmark-dynamic-city --benchmark-mode stress --stress-seconds 1800
--benchmark-output <absolute-output-directory>
--source-revision 29d7034fc91d12ed2f71a40572e5ef24ef0db7b3
-screen-fullscreen 0 -screen-width 1280 -screen-height 720
-logFile <absolute-log-file>
```

The workload uses 4,096 moving agents, four moving cameras, 100 edits per logical
second, extra 1,000-edit bursts, rendering toggles and World recreation every
60 real seconds. Heartbeats update every 30 real seconds. Final acceptance
requires no captured errors and zero accounted payload in every retired registry.
Destructive edits are intentional resource stress, separate from the normal city
and the [frozen image oracle](DynamicOcclusion-ReadablePlayer.md).

Earlier 72-case measurements were rejected for mismatched simulation phase; a
corrected older run stopped after user feedback and predates the depth fix.
Neither substitutes for this requested full-population comparison.
