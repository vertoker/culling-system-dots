# Dynamic visual acceptance and scale validation

> Execute sequentially using the repository execute-plan and inline executing-plans
> workflows. Keep one Unity operator. Work and commit on master; do not push.

**Status:** In progress. User feedback reopened visual correctness and replaced
the pending performance workload with explicit populations of 4,096, 40,960 and
409,600 objects. The earlier frame-timing work is not final performance acceptance.

**Goal:** Deliver a readable construction demo, correct camera-inside-solid
behavior, and an honest comparison where the requested populations really render.

**Architecture:** Preserve the independent geometric algorithm and stock Entities
Graphics fallback. Correct depth validity/orientation before changing presentation.
Keep the normal city separate from the opt-in development stress harness.

**Tech stack:** Unity 6000.6.8f1, stock Entities/Graphics 6.6, URP 17.6, Windows DX12.

**Spec:** This is an acceptance addendum to the
[original implementation plan](2026-11-06-dynamic-geometric-occlusion-plan.md)
and its [design](2026-11-06-dynamic-geometric-occlusion-design.md).
The user explicitly selected the three populations above, requested ordinary
buildings with interior details, and reported disappearance while inside a wall.

## Constraints and review focus

- Preserve static baking, the user's live scene, unrelated Readiness-Cameras.json,
  source ownership, GUIDs, and package-only dependencies. No CLI/batchmode, DLLs,
  cache patches, extra worktrees, remote publication or unrequested delegates.
- A passing frozen oracle does not establish universal correctness. Compare the
  same state and both views, including a camera inside an existing wall.
- A back-facing triangle must not supply coverage absent from ordinary rendering.
  Do not fix this by making all materials double-sided.
- Normal demo startup must not destroy the city, penetrate a closed wall on an
  automatic camera path, or present a timing harness as an interactive map.
- Large tests must not silently transfer only a small subset to the new renderer.
  Raise test settings explicitly; report capacity failures rather than success.
- Registration count is not rendering count. Verify frustum coverage, actual
  reference pixels and submitted triangle counters outside timed readback.

## Task 1: camera/near-plane and face-orientation correctness

**Files:** Runtime/Dynamic/Gpu/OcclusionGpuPipeline.cs and
OcclusionIndirectRenderer.cs; DynamicDepthTests.cs, DynamicIndirectRendererTests.cs;
root DynamicCityImageTests.cs. Paths use the existing package/test boundaries.

- [x] Reproduce inside a restored, existing wall with a simultaneous outside view.
  RED `fc2c56de4e524b90835961b244dd0f89`: reference-visible ID lost in scenario 8.
- [x] Isolate the depth contributor. Diagnostic
  `fd4f6d01033a4c129b4c9b1ff82189b4` lost IDs 9,10,15,11 normally and none when
  omitting only the containing wall from occlusion depth.
- [x] Add near-plane validity to RecordDepth, pass the actual camera near distance,
  omit containing/near-clipped bounds, and retain useful forward-wall depth.
  Two-view ten-state oracle GREEN `311b93279934428baac8f87572273ca2`.
- [x] Compare a one-sided plane against an independent ordinary blue material.
  RED `44b3dc949fc14740ac9bc62ffeae20b7`: ordinary front blue 255/back blue 0,
  GPU front target red 255/back red 0, proving reversed occluder face selection.
- [x] Correct explicit view/projection winding and restore raster state. Run all
  depth, renderer, camera and coupled legacy rendering regressions before committing.
  Source `60545b1`, coupled PlayMode `5c2bb5c762544f97a3f86139d524b774`: 57/57.
- [ ] Update built-Player oracle to include both views and inside/near-wall cases;
  actually run it against the committed installed package.

## Task 2: readable normal city

**Files:** Samples~/DynamicCity/Runtime bootstrap, agents, camera, construction/UI
and interior-detail helper as needed; serialized DynamicCity scene; matching
Assets/DeliveryDynamic files; root scenario/image tests.

**Interfaces:** Keep existing construction commands and controls. Add a serialized
OcclusionSettings field with the sample's existing budget defaults, so a stress
harness can deliberately configure a larger population. Interior fixtures use
shared cube resources and explicit renderable/occludee bindings.

- [ ] Add a failing startup/scenario check for a stable city, independent interior
  details, and no automatic construction or camera tour by default.
- [ ] Supply recognizable workshop/shelf/container details inside buildings, avoid
  overlapping moving routes, and distribute ground/upper agents across buildings.
- [ ] Keep seeded construction, manual piece editing and tour as explicit controls.
  Preserve simulation when rendering is disabled. Do not add game navigation or
  hide the inside-solid regression by preventing its test camera position.
- [ ] Keep canonical sample/mirror bytes and GUIDs identical; verify actual visible
  Player controls, both rendering paths and a stable initial view before committing.

## Task 3: actual rendering at 1x/10x/100x

**Files:** New opt-in root Player scale harness in the existing Player assembly,
its development tests and validation reports. Production does not depend on it.

**Workload:** One 2560 by 1440 camera, deterministic non-overlapping cube grid,
open and partially occluded layouts, ordinary/feature pairs at all three requested
populations. Every target bounds must be inside the frustum. The open reference
must render every target's unique ID; pixel footprint must remain large enough
at 409,600 objects. All tested motion counts and edit rates are explicit. Use the
same simulation/camera/command trace checks as the corrected timing harness.

- [ ] Add the failing full-population reference and feature visibility checks first.
- [ ] Configure MaxInstances/queue/membership/upload/resource limits for the actual
  population. Require the complete eligible set ready, not a partial success.
- [ ] Report registration progress separately from measurement. Keep status visible
  so the running build is identifiable; no assertion readback in measured frames.
- [ ] Pilot the small case, then run the selected scale workloads. Target 300 warmup
  and 1,800 measured frames; if a large case hits a declared time/resource bound,
  preserve its achieved count and failure instead of calling it a passing run.
- [ ] Audit raw frame rows and paired states. Report total/main/render/GPU timing
  distributions, actual transfer coverage, memory/upload pressure and unavailable
  counters. Do not extrapolate acceleration from entity or draw counts.
- [ ] Run the separate 30-minute edit/toggle/World-reload stress after correctness
  and comparative measurements. Preserve truthful failure/early-stop evidence.

## Task 4: delivery and outcome

- [ ] Keep historical evidence, append corrections and record exact tested revisions.
  Retain experimental status unless the declared fast-path gate actually passes.
- [ ] Run relevant Unity suites, documentation validation and Python tooling checks.
- [ ] Verify actual Git installation and Player execution. Remove only the owned
  temporary consumer after its process exits; restore main Editor routing.
- [ ] Commit verified source and evidence separately on master. Report results and
  remaining limitations directly. Do not push.
