# Dynamic occlusion depth foundation: Task 3 evidence

Unity 6000.6.8f1, Windows Editor/DX12, RTX 4060 Ti, 2026-11-06. Sources are based on `8812ae1`.
This records the low-level CPU/GPU foundation, not completion of Task 3 or a production renderer.

## Observed results

| Check | RED | GREEN |
| --- | --- | --- |
| Exhaustive CPU depth/projection reference | `96a44e2096324b09844a0e63a43f4aa2`, 6/11 | `f848e0c1de084e6ba8a384b292448002`, 11/11 |
| GPU depth, maximum pyramid, projected footprint tests | `e4802e6f17d5418e8dd6b94f45ac1138`, 0/3 | `0c15ad0a873c48808c746492d0f86651`, 3/3 |
| Pending GPU consumer / World teardown and disposed-World allocation | `7e0fab0f3247423e9c0b6e5d7796b755`, 4/6 | `131a23bcc18e4197afd735f25fb25ded`, 6/6 |
| GPU World-AABB projection | `1220cc01cf2c4826bb6d775795ea4ee0`, 0/1 | `2389e7c18fcd425eb604437f3f50eeb9`, 7/7 combined |
| Omission of unsupported mirrored occluders | `ad10a35b9c414643b3871484e048fed4`, 7/8 | Final coupled PlayMode below |

Final EditMode `d6418df8162d44d3992be4a2ade752c8`: **115/115**, zero failed/skipped
(58 dynamic numeric/registry/update, 15 ECS and 42 existing Core/Runtime tests).
Final PlayMode `e4f1e8634ac34db990d7558a4226df58`: **34/34**, zero failed/skipped
(8 new depth/resource tests plus the prior 26 rendering/ownership/transport regressions).
Portable tools: 35/35 Python tests. A compile-blocked early run discovered zero tests and is
excluded from acceptance evidence.

The GPU tests ran on the device; no GPU checks skipped. Readback is assertion-only. This is
Editor evidence; no new Player or installed-consumer claim is made.

## Numeric and coverage scope

- Full reference-grid `R32_SFloat` positive linear eye depth; checked render/blend/load-store/sample
  support. Minimum blend selects the nearest actual triangle surface, independently of hardware Z.
- Tilted cube surfaces were checked with perspective and orthographic projection and both depth
  projection directions. The center depth matches the analytic ray/surface intersection.
- Separate ceil-sized maximum-reduction levels preserve every uncovered sample. Odd edges and
  invalid/nonpositive depth become far depth. Tested sizes include 7x5, 17x9, 1x11 and 8x8.
- A fixed-seed set of 256 footprints keeps every reference-visible rectangle; a solid one-pixel
  footprint actually hides. The comparison allows conservative extra visibility from coarse cells.
- World AABBs project all corners, retain a guard pixel and test all overlapped hierarchy cells.
  Doorway pixels, camera-inside/near-plane crossing and invalid numeric inputs remain visible.
- A separate CPU fixture demonstrates why low-resolution center-only depth can erase a real
  opening. No lower-resolution setting is promoted to a safe occluding path.
- Mirrored/singular transforms are not accepted depth sources. The mirrored-cube regression
  observed the opposite surface before the exclusion; current depth recording omits it.

The dedicated depth projection uses `GL.GetGPUProjectionMatrix(projection, false)` in these
fixtures. World tests apply the backend raster Y convention when mapping clip coordinates to
texture indices. Depth is linear, so the predicate has no hardware depth-direction branch.
Final rendered-object ID/depth oracle coverage is still required for presentation acceptance.

## Resource ownership and limits

The serialized `OcclusionShaderResources.asset` references the three shipped shaders. Runtime
consumes direct references; tests locate that asset by its Unity-generated GUID, independent of
an Assets-versus-Packages installation path. The GPU assembly has no Editor, ECS or URP dependency.

Texture payload is charged to the registry budget. Partial-allocation failure releases earlier
textures and its reservation. A disposed World cannot allocate new depth resources. An internal
submission retains its snapshot and GPU resources after cancellation/World destruction until
the caller seals its final GPU-consumer fence and polls completion. Registered CPU readers are
completed before final release. The test queues a real depth draw after registry teardown.

The low-level owner is not yet a production ring/retirement pump. Actual bounded GPU mesh and
instance upload scheduling, view/MSAA capability handling, resource reuse, automatic retirement
after disable/unload, indirect color ownership, paired image oracle, dynamic sample, performance
and Git-consumer/Player validation remain later work. Current depth recording still uses the
registry's Mesh directly; its engine upload cost is not claimed to meet the preparation byte cap.
