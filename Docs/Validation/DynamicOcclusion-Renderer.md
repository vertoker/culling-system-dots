# Internal current-frame indirect renderer checkpoint

Source: the commit introducing this report, based on `2f3cac7`. Environment: Unity 6000.6.8f1,
URP 17.6, Windows Editor DX12, RTX 4060 Ti. This is an internal GPU renderer checkpoint.
There is no public ECS/URP setup or new sample/Player acceptance yet.

`OcclusionIndirectRenderer` connects uploaded geometry and bounded instance packets to current
camera depth, farthest-depth hierarchy, World-AABB tests, GPU compaction and RenderMeshIndirect.
Unknown/unsupported views use separate immutable full-draw arguments. Shadows use the independent
full-instance arguments. At most the configured in-flight frame count can own pending readers;
completed slots reuse matching instance buffers and per-camera depth/compaction resources.
The current internal registration limit is four occluding cameras; other views use full draws.

Each publication stays alive across render contexts until the next preparation. End-context fences
cover submitted color/shadow readers; the latest fence is retained if additional contexts consume
the publication. Retirement uses that fence, or a new graphics fence when only uploads occurred.
Disposal inside a camera callback retains the end-context callback until those readers are sealed.
A repeated callback for one camera uses immutable full data instead of rewriting its pending
compaction buffers. These are conservative lifetime choices, not temporal visibility reuse.

The package fixture uses an occluder-only synthetic wall and an indirect red target, without ECS
graphics ownership changes. It checks actual rendered center pixels and resource accounting:

- Opposite cameras simultaneously hide/show the target from current geometry.
- A four-sample target keeps the full target draw; it cannot consume single-sample depth proof.
- Stable frames reuse packet/view resources within the two-slot setting.
- A camera-excluded layer contributes no occluder depth.
- Resizing to a depth target beyond the one-MiB test budget retains the full target draw.
- Removing the wall reveals the target; teardown returns the registry payload accounting to zero.

| Check | Job | Result |
| --- | --- | --- |
| Missing renderer RED | `7f921c3902e74c1aafcf5da7a9b5e747` | 0/1; opposite view remained black |
| Initial complete pipeline | `a7918fecd8f442a6a7dd46ae876d6b2c` | 1/1 |
| Extended reuse/layer/resize-budget/teardown fixture | `7bab6ca081a44d1ca9f9c60a7808f90d` | 1/1 |
| Coupled EditMode | `1bac852666d9473da6161662af665e62` | 115/115, no failures/skips |
| Coupled functional PlayMode | `3ea664a042f5444f8b700479f7f8c8fa` | 53/53, no failures/skips |

ReadPixels and shutdown drain are outside performance measurements. The runtime frame path has
no visibility readback or GPU wait. Tests do not establish a speedup or a complete ordinary-render
image oracle. The [shader checkpoint](DynamicOcclusion-Color.md) separately verifies directional
shadow pixels; a complete ECS renderer shadow scene remains part of presentation acceptance.

The ECS/URP bridge must still transfer/restore stock graphics, reject unsupported binding features,
validate actual URP render scale/AA/stack/jitter settings, handle stale publication and World
replacement, and expose meaningful setup/diagnostics. Providers publish changes before frame
preparation; mutations after publication are not yet accepted as current rendering. The camera
checks in this internal owner alone are not a claim of general URP integration.

The [plan](../Superpowers/Plans/2026-11-06-dynamic-geometric-occlusion-plan.md) remains open for
that bridge, the dynamic construction demo, full paired-image oracle and actual Player benchmarks.
