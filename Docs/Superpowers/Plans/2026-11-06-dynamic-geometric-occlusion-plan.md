# Dynamic geometric occlusion implementation plan

> For agentic workers: execute sequentially with the repository's `execute-plan` skill and, when
> available, `superpowers:executing-plans`. Keep one owner for live Unity. No branches or worktrees.

**Status:** Task 0 stock investigation completed; Gate G1 failed. At the planned boundary the user
selected and authorized a package-owned GPU indirect continuation. Task 0b passed its transport
proof; Task 1 registry foundation and Task 2 block/ECS capture are complete. Task 3 CPU/GPU depth
foundation is verified; upload scheduling and production resource lifetime are in progress. See the
[integration evidence and continuation options](../../Validation/DynamicOcclusion-Integration.md).

**Goal:** Implement independent algorithm 3: configurable runtime geometric occlusion for a
changing ECS world, with incremental block geometry updates and a reproducible construction demo.

**Architecture:** World-owned live geometry/bounds registry, GPU conservative hierarchical depth
tests and a separately validated presentation boundary. No building/room knowledge or PVS is
required. Preserve the existing static bake and leave algorithm 2 and composition to separate plans.

**Tech stack:** Unity C#/Burst/Jobs for runtime registration and numeric preparation, HLSL compute
and public pre-camera graphics-queue depth/indirect submission, with stock Entities Graphics
remaining untouched for other bindings. URP 17.6 is the initial accepted pipeline target.

**Spec:** [Design and acceptance contract](2026-11-06-dynamic-geometric-occlusion-design.md).
The approved continuation is recorded in the
[indirect rendering addendum](2026-11-06-dynamic-indirect-rendering-design.md).
The user selected algorithm 3 first; numerical defaults and proposed API names are engineering
proposals in that design/this plan, not measured capacities or previously approved public APIs.

## Global constraints

- Unity 6000.6.8f1, Entities/Entities Graphics 6.6.0, URP 17.6.x, Windows DX12 baseline.
- Work/commit on master only; no push, native DLLs, package forks, private reflection or cache edits.
- Preserve current static bake assets, ordered evaluator behavior and CPU/GPU bake workflows.
- Production code/resources/tests/sample stay inside `Assets/CullingSystemDOTS`.
- Runtime has no UnityEditor, NUnit, MCP or root-project dependencies; URP is separately gated.
- Missing, stale, unsupported, over-budget or failed results render normally; simulation continues.
- Occlusion initially supports single-sample targets only; MSAA retains ordinary rendering.
- Default blocks 16 units; initial targets: 256 KiB upload/frame, two in-flight requests,
  65,536 instances, 128 MiB accounted feature-owned GPU/native payload. Validate every setting.
- No implicit combination with algorithm 2 or the legacy evaluator; conflicting ownership fails explicitly.
- Keep the user's live scene; use Coplay MCP for Unity, never CLI/batchmode. Preserve unrelated edits.

## Review focus

1. A tiny doorway visible at output resolution must survive coarse depth reduction (Task 3).
2. Wall removal, camera jitter or target resize invalidates hidden state before its next use (Tasks 1, 4).
3. Moving/reused Entities and World teardown must not target freed/replaced bindings (Tasks 1, 2, 4).
4. An edit flood/budget overflow must bound work and retain normal rendering (Tasks 2, 5).
5. A camera-hidden caster can still cast a visible shadow; multiple views cannot share a mask (Tasks 0, 4).

## Existing code to inspect before implementation

| Owner | Relevant boundary |
| --- | --- |
| `Assets/CullingSystemDOTS/Runtime/Rendering/EcsRenderVisibilityAdapter.cs` | Current per-camera masks, broad reader synchronization and restoration |
| `Assets/CullingSystemDOTS/Runtime/Ecs/CullingCameraDriver.cs` | Existing camera scheduling; do not hijack legacy ownership |
| `Assets/CullingSystemDOTS/Runtime/Ecs/CullingViewCaptureSystem.cs` | Legacy snapshot/World ownership, not a dynamic-depth registry |
| `Assets/CullingSystemDOTS/Runtime/Urp/UrpCameraRenderOrder.cs` | URP stack behavior to preserve |
| `Assets/CullingSystemDOTS/Editor/Baking/Gpu/ComputeBakeBackend.cs` | Editor-only AssetDatabase loading; cannot be called by a Player |
| `Assets/CullingSystemDOTS/Editor/Baking/Gpu/Shaders/BvhBake.compute` | Ray-bake shader, not a conservative image-space occlusion kernel |
| `Assets/Tests/PlayMode/ReadinessBenchmarks.cs` | Existing adapter cost measurement and unavailable-counter lessons |
| `Tools/CreateDeliveryConsumer.py` | One disposable Git-install consumer |

## Proposed file and assembly boundaries

All paths below are relative to `Assets/CullingSystemDOTS`, except explicitly root-only evidence.
Create local AGENTS guides with nearest-parent links and register them in the parent navigator
when adding code folders. Let Unity create imported metadata and preserve existing GUIDs.

| Area / proposed files | Responsibility |
| --- | --- |
| `Runtime/Dynamic/OcclusionContracts.cs`, `OcclusionSettings.cs` | Handles, versions, settings and result/fallback meanings |
| `Runtime/Dynamic/OcclusionWorld.cs`, `OcclusionSnapshot.cs` | Registry, immutable snapshot leases, World-owned cleanup |
| `Runtime/Dynamic/OcclusionBlocks.cs`, `OcclusionUpdateQueue.cs` | Sparse membership, dirty revisions, bounded/coalesced preparation |
| `Runtime/Dynamic/CullingSystemDOTS.Dynamic.asmdef` | Core runtime contracts; Collections/Mathematics/Entities, no Editor/URP |
| `Runtime/Dynamic/Ecs/DynamicOcclusionComponents.cs`, `DynamicOcclusionSystem.cs`, `DynamicOcclusionBindings.cs` | Full-Entity bindings, transform/bounds capture and restoration |
| `Runtime/Dynamic/Ecs/CullingSystemDOTS.Dynamic.ECS.asmdef` | Explicit Dynamic/Entities/Transforms/Entities Graphics references |
| `Runtime/Dynamic/Gpu/OcclusionDepthResources.cs`, `OcclusionGpuPipeline.cs` | GPU resource lifetime and pyramid/test dispatch |
| `Runtime/Dynamic/Gpu/OcclusionShaderResources.cs`, `OcclusionShaderResources.asset` | Serialized shipped shader references, no Editor-dependent runtime lookup |
| `Runtime/Dynamic/Gpu/Shaders/OcclusionDepth.shader`, `OcclusionPyramid.compute`, `OcclusionBounds.compute` | Safe depth coverage and conservative bounds tests |
| `Runtime/Dynamic/Gpu/CullingSystemDOTS.Dynamic.GPU.asmdef` | Dynamic/Collections/Mathematics plus engine graphics APIs |
| `Runtime/Dynamic/Urp/DynamicOcclusionFeature.cs`, `DynamicOcclusionPass.cs` | G1-approved RenderGraph ordering, view identity and presentation |
| `Runtime/Dynamic/Urp/CullingSystemDOTS.Dynamic.URP.asmdef` | Separate optional URP 17.6.x version/define gate |
| `Runtime/Dynamic/Ecs/DynamicOcclusionPresentation.cs` | Stock ECS presentation only if G1 proves this route |
| `Tests/Dynamic/EditMode/DynamicOcclusionContractsTests.cs`, `DynamicOcclusionBlocksTests.cs`, `OcclusionReferenceTests.cs` | Numeric/lifecycle contracts without the demo |
| `Tests/Dynamic/PlayMode/DynamicOcclusionRenderingTests.cs`, `DynamicOcclusionLifetimeTests.cs` | Real GPU/rendering correctness and lifetime |
| `Tests/Dynamic/PlayMode/DynamicOcclusionPerformanceTests.cs` | Explicit repeatable performance cases, not timing assertions in functional tests |
| `Tests/Dynamic/EditMode/CullingSystemDOTS.Dynamic.Tests.asmdef`, `Tests/Dynamic/PlayMode/CullingSystemDOTS.Dynamic.Rendering.Tests.asmdef` | Test isolation and required assembly references |
| `Samples~/DynamicCity/Runtime/DynamicCityBootstrap.cs`, `DynamicCityConstruction.cs`, `DynamicCityAgents.cs`, `DynamicCityCamera.cs`, `DynamicCityDiagnostics.cs` | Minimal reproducible runtime demo |
| `Samples~/DynamicCity/Runtime/CullingSystemDOTS.DynamicCity.asmdef`, `Samples~/DynamicCity/README.md` | Importable sample and complete usage/limitations |
| `Samples~/DynamicCity/Settings/DynamicCityPipeline.asset`, `DynamicCityRenderer.asset` | Sample-owned URP setup with the feature/resources assigned |
| `Documentation~/DynamicOcclusion.md`, `package.json`, `README.md`, `CHANGELOG.md` | Shipped setup, sample registration and actual supported scope |

The renderer-specific file list is provisional until G1. After G1, add the exact selected
public API signatures, pass ordering, resource formats and owning files to this record *before*
starting Tasks 3-4. If the selected route needs a new renderer, stop for the user's decision and
write its own scope; do not implement that scope under the stock-presentation filename.

## Task 0: prove presentation and current-view ordering

**Files:** Create root-only `Assets/Tests/DynamicPrototype/` with its own AGENTS and test asmdef;
create `DynamicOcclusionIntegrationProbe.cs` and `DynamicOcclusionIntegrationProbeTests.cs` there.
Create `Docs/Validation/DynamicOcclusion-Integration.md` and a machine-readable companion JSON.
Do not expose a product API or install another renderer/package in this task.

**Interfaces:** Probe input is a fixed-seed scene of occluder walls, colored rigid ECS targets,
one/four cameras and scripted edits. Output is a report with actual API/pipeline ordering,
reference-visible ID losses, submitted draws, accepted visibility while moving, CPU/GPU timing
availability, waits, tracked-job completion and restoration results.

- [ ] Add the smallest failing rendering assertions: a hidden target skips real rendering, becomes
  visible after deleting its wall, stays visible through a camera turn/teleport, and retains its
  normal shadow on a visible receiver. Compare same-state ordinary rendering; a missing API is a
  recorded capability failure, never a skipped-success result.
- [x] Run the probe assertions through the live PlayMode runner and capture RED/capability evidence.
- [x] Inspect stock renderer source read-only and the public 6000.6 APIs. Prototype a supported
  extension only if one exists. Separately measure strict asynchronous readback/stock masks and
  optional blocking-readback diagnostic; do not use private callback replacement.
- [x] For the strict async reference, reject on any view signature/global occluder epoch/bounds
  revision mismatch; verify it does not incorrectly hide during continuous motion. Record the
  fraction of movement frames with useful accepted occlusion, including zero.
- [x] Run idle/moving-camera and 0/10/100-edits-per-second cases for 512/4,096 targets; measure the
  complete path, not just the compute dispatch. Record source revision/hardware/settings.
- [x] Write Gate G1 verdict: demonstrated stock fast path, safe but ineffective reference, or no
  acceptable stock route. An acceptable fast path must avoid missing visible objects and provide
  useful culling while moving; asynchronous output invalidated every movement frame is not enough.
- [x] If G1 fails, report the limitation and choices to the user, stop feature implementation and
  leave Tasks 1-7 unchecked. A package-owned BRG indirect renderer or CPU rasterizer requires an
  explicit new decision; a prototype is not release acceptance.
- [ ] If G1 passes, freeze the concrete rendering/dataflow addendum described above and rerun its
  tests. Commit exact owned prototype/evidence paths on master. Retain the probe as development
  evidence, separate from shipped code, without a second retained Unity project.

**Outcome:** Root-only probe/source commit `39f782f`; 3 new functional tests and 15 existing
rendering/sample tests passed (18/18), plus 72 diagnostic cases against committed probe source.
Strict async accepted 0/1,800 moving view samples; blocking consumption worked but remains a
diagnostic. No stock GPU-resident hook was found in installed public API/source. Broad job
completion and per-entity masks are measured costs, not an accepted fast path.

Ruling: use a restricted orthographic wall GPU classifier to isolate presentation rather than
implementing general Hi-Z before finding its consumer. This preserves the G1 question but leaves
full geometry/GPU algorithm cost unmeasured. Image assertions compare same-state opposite views,
wall removal, turn/teleport and render restoration. The probe has shadows off; existing shadow
companion parity passed separately. No new shadow-capable path is claimed. Stock counters are
sampled World snapshots, not isolated per-view draw counts. The source/evidence report records
these limits. The successful-G1 addendum above is inapplicable; Tasks 1-7 remain pending a new
renderer/backend decision. See the linked report for job IDs, hardware, timings and alternatives.

The first checkbox remains partial: render removal/reveal, movement and opposite-view checks
exist, but there is no new shadow-capable GPU route to exercise. The legacy shadow regression
was rerun, and new-route shadow acceptance remains deferred rather than marked passed.
Independent review found constructor cleanup leaks; two injected allocation-failure regressions
went RED then GREEN, and the coupled suite passed 20/20. The evidence report records the jobs.

## Task 0b: prove the authorized indirect transport

**Files:** Root-only `Assets/Tests/DynamicPrototype/IndirectTransportProbe.cs`,
`IndirectTransportProbeTests.cs`, `IndirectTransport.shader`, `IndirectTransport.compute`.
Keep this proof in the existing root test assembly and project, without shipped API changes.
Record evidence in `Docs/Validation/DynamicOcclusion-Indirect.md`.

**Interfaces:** Candidate owner holds one reusable mesh/material and per-view graphics buffers.
Its camera-begin handler consumes `Hidden[view]`, records a GPU-write to the argument buffer and
calls the public indirect API. Test setup uses no stock visibility bits or CPU result readback.
Separate full-caster arguments feed the shadow submission.

- [x] Add a failing pixel test: ordinary indirect count is one, GPU-written count zero removes
  its color, and the next moving-camera/edited submission restores it without waiting for readback.
- [x] Add two simultaneous camera tests with opposite masks, plus camera-hidden caster/visible
  receiver parity against ordinary rendering. Do not infer shadow support from the legacy adapter.
- [x] Implement the minimal candidate using public `Graphics.RenderMeshIndirect`,
  `GraphicsBuffer.IndirectDrawIndexedArgs`, `UnityIndirect.cginc`, and graphics-queue compute.
  If its registration boundary is too late, move only registration to an earlier public boundary
  and prove where current-view arguments become final; use an owned BRG if necessary.
- [x] Prove resource retirement covers the later color/shadow reader; test failure cleanup.
- [x] Observe GREEN, record actual API/pass/resource/lifetime signatures, and commit the proof.
  This is the G1 replacement for the explicitly authorized route, not product/performance acceptance.

**Outcome:** [Transport evidence](../../Validation/DynamicOcclusion-Indirect.md), coupled 23/23
PlayMode checks. Selected API: `Graphics.RenderMeshIndirect` with per-view arguments, independent
all-caster arguments and graphics-queue compute in `beginCameraRendering`; retire after a fence
in `endContextRendering`, following URP's submissions. No required renderer feature/custom BRG.

**Frozen production dataflow:** Task 3 uses full reference-grid positive linear eye depth in
`R32_SFloat`, far clear, minimum blending of eligible triangles, then maximum-depth reduction in
separate per-level textures with ceil dimensions. Positive eye depth makes the farthest reduction
independent of hardware reversed Z; projection/raster tests must still cover the installed backend.
Check render/blend/load-store format support; unsupported formats retain ordinary rendering.
Conservative bounds tests compact IDs into per-view indirect buffers, consumed only after the
same queue's passes. Full-visible fallback and full-shadow buffers remain separate. Task 3 must
validate all coverage/reference cases; selecting formats is not a claim that those shaders passed.
Initial occluding views require single-sample URP rendering with no temporal jitter, dynamic
resolution or unsupported stacks; other views keep full-visible submission until separately tested.

### Conditional file-map amendments for Tasks 3-4

`Runtime/Dynamic/Gpu/OcclusionIndirectRenderer.cs` owns the proven indirect transport and buffer
layout; shipped `Shaders/OcclusionIndirect.shader` plus an HLSL instance-layout include own its
explicit material contract. `DynamicOcclusionPresentation.cs` becomes an ownership-transfer and
snapshot bridge, not a per-camera stock enable-bit mask. Transfer/restore only at binding lifecycle
boundaries. Keep full-shadow data separate from camera compaction. Use the Task 0b evidence to
freeze the exact fence/submission boundary before writing this production presentation layer.
If the candidate uses public pre-camera submission without a renderer feature, omit unused
RenderGraph feature scaffolding rather than requiring a redundant feature from consumers.

## Task 1: define live registry, validity and ownership

**Files:** Dynamic root contracts/settings/world/snapshot and EditMode contract tests from the map.

**Interfaces:** Namespace `CullingSystemDOTS.Dynamic`. Define `GeometryHandle` and `InstanceHandle`
with index/generation, `OcclusionVisibility` (PotentiallyVisible/Occluded/Unavailable), and
`OcclusionFallbackReason`. Define these provider methods on `OcclusionWorld : IDisposable`:

```csharp
GeometryHandle RegisterGeometry(Mesh mesh);
void ReplaceGeometry(GeometryHandle handle, Mesh mesh);
InstanceHandle RegisterInstance(in OcclusionInstanceDescriptor descriptor);
void UpdateInstance(InstanceHandle handle, in OcclusionInstanceState state);
void RemoveInstance(InstanceHandle handle);
void RemoveGeometry(GeometryHandle handle);
OcclusionSnapshotLease AcquireSnapshot();
```

Descriptors specify geometry handle (optional for occludee-only), role flags, local-to-world,
enclosing world bounds and opaque eligibility. Instance state updates transform/bounds/role;
mesh changes require `ReplaceGeometry`, including edits to the same Mesh reference. The registry
retains its owned geometry copy; callers retain their Mesh. Removal rejects new use and retires
storage after all readers; snapshots are immutable leases with one clear disposal owner.

- [x] Add failing tests `OldGenerationCannotMutateReplacement`, `WorldIdentityRejectsForeignHandle`,
  `SnapshotRetainsGeometryUntilLastReader`, `SameMeshReferenceReplacementChangesRevision`,
  and `InvalidSettingsFailBeforeAllocation`. `LegacyOwnershipConflictIsExplicit` belongs to Task 2's
  real ECS binding boundary; this independent registry has no fake caller-supplied legacy flag.
- [x] Add validity tests that change each view-signature field, global occluder epoch and candidate
  generation/bounds revision independently; every stale hidden result becomes PotentiallyVisible.
- [x] Run the new EditMode fixtures and record RED before implementation.
- [x] Implement contracts, checked setting validation, World ownership and snapshot retirement.
  Keep this assembly independent of legacy Core and URP. Document provider mutation obligations.
- [x] Run the fixtures to GREEN, inspect native/managed lifetime under repeated create/dispose,
  validate folder guidance and commit exact owned paths on master.

**Outcome:** Independent `CullingSystemDOTS.Dynamic` assembly with generation/World handles,
owned geometry copies, checked settings, immutable native snapshots and registered Job readers.
Sources remain caller-owned. Replacement grows dependent bounds; rejected replacement retains old
bytes but withdraws occluders and invalidates the epoch. Payload accounting includes owned native
arrays and mesh stream payload estimates, not all Unity/managed/driver overhead. Leases retain
retired geometry and the caller must keep a GPU lease through its retirement fence.

RED `96895bbdc34e4caf92c50731e1e2621e`: 0/23. Initial GREEN
`8b0c1b60ddd049c59629e3976e890140`: 23/23. Boundary regressions exposed stale replacement bounds
and overflowing extrema (`44f3046d401a4151b7e9294222b8b547`: 27/29); fixed, coupled suite
`bbc611d590d347d79f52b2afb5b42257`: 71/71. Final repeated-lifetime case (32 cycles) plus coupled
legacy Core/Runtime job `a82415d39770401da1d4841aec9239e1`: 72/72, zero failures/skips.
Document checks: 86 files; Python tooling: 35/35. This is registry acceptance, not GPU feature acceptance.

## Task 2: incremental block data and ECS capture

**Files:** Blocks/update queue, ECS components/system/bindings, block tests and lifetime tests.

**Interfaces:** `OcclusionBlocks.GetOverlappingBlocks(in Bounds bounds, NativeList<int3> output)`
uses configured block size and floor division. `OcclusionWorld.ProcessUpdates(in OcclusionUpdateBudget
budget)` coalesces changes and prepares at most the configured upload payload. Dynamic ECS components
opt in explicit occluder/occludee roles and geometry handles. `DynamicOcclusionSystem` owns one
registry per World and captures current world bounds/transforms before presentation; managed mesh
registration is separate from numeric Jobs work. Update budget limits accounted payload, not a hard
wall-clock bound for indivisible engine calls. Apply render bindings only through Task 4's boundary.

- [x] Add failing tests for negative coordinates, cross-block bounds, empty blocks, off-grid rotated
  pieces, repeated edits to the same block, and overflow-safe huge/invalid coordinates.
- [ ] Add `MovingOccludeeDoesNotRebuildOccluderGeometry`, `RemovalInvalidatesBeforeQueuedUpload`,
  `NewRevisionSupersedesPendingBuild`, `EditFloodBoundsQueueMemory`, and
  `WorldDisposeJoinsJobsAndRetiresGpuReaders` (GPU half completed with Task 3).
- [x] Add `LegacyOwnershipConflictIsExplicit` at actual ECS/graphics ownership registration,
  including both registration orders and rejected-publication rollback.
- [x] Observe RED; implement sparse hashed block membership, revision coalescing, bounded queues,
  immediate global invalidation and omission of unavailable occluders from the depth source.
- [x] Implement full-Entity binding capture and explicit scene/World unload cleanup. Geometry must
  be current even when last frame's object was hidden. Unknown state retains normal rendering.
- [x] Observe GREEN; profile capture/upload preparation at 512 and 4,096 instances without claiming
  render speedup yet. Commit exact owned paths on master.

**Core update outcome (ECS portion still pending):** Sparse block membership with checked negative
coordinate floor/range limits, separate geometry/instance/block revisions, coalesced bounded queue,
fair partial slices, upload-preparation byte limits and overflow rescan. Pending/rejected occluders
are omitted immediately. CPU `Prepared` flags never substitute for actual GPU upload validation.
Managed block/membership/queue counts have explicit configurable caps; they are not a whole-process
memory bound. Engine Mesh copying remains an indivisible registration operation outside the slice loop.

Standalone RED `7f4cd025237a4e99aaf427fb07a1c3e0`: 0/9; dynamic GREEN
`1ad4343181c7482d8c08367e88d2bf64`: 39/39. World integration RED
`e0770ea169e448c2b2f718f7f41732d3`: 40/44; coupled GREEN
`15e0ce84796845ba853499da24382729`: 86/86. A rejected-pose regression then exposed an obsolete
occluder epoch (`800d046df87d410bb79a2c7056b1b288`: 46/47); fixed by withdrawing failed capture.
Final coupled `9d032a3687254ddfa858bea3766314c2`: 89/89 (47 dynamic, 42 existing Core/Runtime),
zero failed/skipped. The earlier zero-discovery filter run is not acceptance evidence.

**ECS outcome:** Explicit full-Entity bindings, opt-out and baseline restoration; Burst numeric
capture after transforms, including hidden graphics; accounted temporary capture memory and
exclusive graphics claims shared with the legacy adapter. Rejected legacy publications preserve
their previous claims. `Configure` accepts checked settings before first registration.
Final coupled EditMode `ae552c14bf7a4195ae9ff8914f0c131e`: 104/104; coupled PlayMode
`29e45925a717472d8d498c6d5c230772`: 26/26. Scoped Editor profile completed at 512/4096 instances;
see [capture evidence](../../Validation/DynamicOcclusion-Capture.md). Fully moving 4096 capture
is still expensive and saturates the default preparation ceiling; this is not render-performance
acceptance. The unchecked aggregate test item above retains its GPU-retirement half for Task 3;
its CPU movement/removal/coalescing/flood/reader contracts are covered by the recorded suites.

## Task 3: conservative GPU depth, hierarchy and tests

**Files:** GPU resources/pipeline/shaders, numeric reference tests and GPU rendering tests.

**Interfaces:** `OcclusionGpuPipeline` owns view-local depth/mips/results and records passes using
the exact G1 render-dataflow addendum. Snapshot/view token and checked resource settings are input;
an owned GPU submission plus immutable validity metadata is output. The CPU depth predicate
`OcclusionReference.Test(in ProjectedBounds bounds, in ReferenceDepthImage image)` returns the
three-state contract and is the exhaustive coverage oracle for shader tests. No CPU readback is
required by the algorithm contract; readback for automated assertions occurs outside measured runs.

- [ ] Add failing CPU/GPU pairs for full wall, doorway/sliver, uncovered texel, far-depth clear,
  standard/reversed Z, odd target dimensions, mip boundaries and enclosing projected bounds.
- [ ] Add camera-inside/near-plane-crossing/NaN tests and a proxy with an oversized silhouette;
  uncertain coverage is PotentiallyVisible, and an unsafe proxy cannot be registered as safe.
- [ ] Add `MsaaTargetKeepsOrdinaryRendering`; resolved MSAA depth cannot pass as a single-sample
  visibility proof. Use matched single-sample targets for the initial image oracle.
- [ ] Verify that low-resolution center-only raster depth fails the thin-opening reference before
  choosing a conservative full-grid or underestimated-coverage implementation. Document the proof
  obligation and never promote an unverified resolution setting to an occluding path.
- [ ] Implement eligible opaque triangle capture, current-view depth, farthest-depth mip reduction
  and bounds testing with conservative bias and complete overlap coverage. Never seed solely from
  previous visible geometry. Keep unavailable/unsupported geometry out of occlusion depth.
- [ ] Implement resource rings, bounded growth and fence/readback retirement; observe GPU failure,
  cancellation and World teardown tests to GREEN without early buffer destruction.
- [ ] Serialize compute/depth shader references in `OcclusionShaderResources.asset` and reference
  it from the feature; verify runtime code has no AssetDatabase or assumed install-path lookup.
- [ ] Run the CPU/reference and GPU fixtures; record actual GPU skips/failures, then commit on master.

**Foundation outcome (Task 3 still open):** Full-grid positive linear R32 depth, ceil-sized maximum
hierarchy, exhaustive CPU predicate and GPU World-AABB projection. Numeric/coverage and low-level
retirement tests pass: coupled EditMode `d6418df8162d44d3992be4a2ade752c8` 115/115;
PlayMode `e4f1e8634ac34db990d7558a4226df58` 34/34, zero failed/skipped. Shader references are
serialized and runtime code has no AssetDatabase lookup. See [depth evidence](../../Validation/DynamicOcclusion-Depth.md).
Remaining: actual bounded mesh/instance uploads, resource rings/automatic retirement, MSAA/view
capabilities and the rendered-object oracle. Current direct Mesh recording is not an upload-budget proof.

**Streaming/lifetime outcome:** `56326ec` adds independently owned GPU mesh streams, shared upload
budgets, ready-reader leases and automatic runtime/Editor retirement. The direct-stream transport
matches GPU bytes and rendered depth. Final coupled PlayMode
`b94d4ab952844bf8be42b58388cc4f0c`: 40/40; snapshot-retain coupled EditMode
`e125c52c52df437486fa828571a7ad5c`: 115/115. See [upload evidence](../../Validation/DynamicOcclusion-Upload.md).
At that checkpoint the prepass still used its earlier direct Mesh recording; cache integration
was the next prerequisite before instance data, resource rings and view capability gates.

**Cache integration outcome:** `e6b67fe` removes the direct Core Mesh prepass route. Only uploaded,
matching-revision GPU leases contribute depth. Replacement/removal retains earlier readers;
shared upload and creation limits keep pending geometry unavailable. Coupled EditMode
`f8398daada8a4511ab753017c7fbd752`: 115/115; PlayMode
`92a76300da5b4202a00557d2d103daaf`: 48/48. See [cache evidence](../../Validation/DynamicOcclusion-Cache.md).
Per-instance payload, reusable frame/view resources, capability gates and final presentation
remain pending; Task 3 is not marked complete.

## Task 4: present safely through the proven renderer route

**Files:** G1-approved presentation file, URP feature/pass/assembly, rendering/lifetime tests.

**Interfaces:** The G1 addendum owns exact public graphics APIs. Expose `RegisterView(Camera camera)`
and `UnregisterView(Camera camera)` on the dynamic presentation owner. Only a view-local submission
with current registry/occluder/target/bounds identity may affect that view. Disable/dispose restores
original render settings and retires pending GPU work. Do not route dynamic results through legacy
`CullingFrameEvaluator` or alter its ordered replay semantics.

- [ ] Add failing real rendering tests for wall removal before presentation, target resize, jitter,
  camera teleport, candidate motion, two views with opposite visibility, disable/re-enable and
  World replacement with reused Entity indices.
- [ ] Add a shadow test where an occluded caster affects a visible surface; preserve ordinary
  shadow rendering until independent light-view occlusion is proven. Add a conflicting legacy
  binding test and a camera-stack test that either works as declared or visibly fails open.
- [ ] Observe RED; implement only the selected public route, validity checks at presentation,
  capability diagnostics and restoration. Missing compute support/feature setup renders normally.
- [ ] Run new tests plus coupled legacy runtime/rendering/URP fixtures to GREEN. Measure tracked-job
  completions and GPU waits against the G1 record; a worse route returns to G1, not a silent fallback.
- [ ] Record the supported material/view scope and commit exact owned paths on master.

## Task 5: ship the minimal dynamic construction sample

**Files:** `Samples~/DynamicCity` files from the map, its local guides, sample registration in
`package.json`, and root-only `Assets/Tests/PlayMode/DynamicCitySampleTests.cs` if integration
with repository delivery tests is needed. Do not duplicate production source into the sample.

**Interfaces:** `DynamicCityConstruction.Apply(in ConstructionCommand command)` applies a seeded
place/remove/door edit. Commands contain stable piece ID, operation, pose and logical tick.
`DynamicCityAgents` advances ECS routes independently of rendering. `DynamicCityBootstrap` builds
the 128 by 128 scene with 16 two-storey buildings and 512 agents from shared cube resources;
configuration raises counts without changing the production algorithm. Avoid a new Input System
dependency: use existing engine input support or a sample-only declared integration.

- [ ] Add failing scenario tests for deleting one occupied-room wall, adding it back, opening a
  door, removing a floor and moving an agent between indoor/street routes. Hidden agents advance
  their simulation tick; rendering disable does not pause construction or movement.
- [ ] Implement minimal colored cubes, individual piece edits, first-person camera, guided tour,
  automatic seeded edits, agent pause, ordinary-rendering comparison and optional block diagnostics.
- [ ] Supply sample-owned URP renderer/pipeline assets with explicit setup instructions; do not
  silently replace consumer render settings or reference the root project's pipeline assets.
- [ ] Add a non-grid rotated fixture and batched edit stress. Freeze simulation and camera for
  paired reference/culling captures; do not compare two different ticks.
- [ ] Run sample tests and manual first-person inspection through construction/demolition. Record
  stale-result/fallback counters during upload backlog; no pending state may leave a visible hole.
- [ ] Confirm the imported sample works without repository fixtures, MCP or Editor APIs; commit.

## Task 6: correctness oracle, Player measurement and decision

**Files:** Explicit performance tests, rendering oracle helpers, and new scoped
`Docs/Validation/DynamicOcclusion-*` reports. Preserve historical readiness evidence.

**Interfaces:** Report source revision, Unity/dependency/API/hardware, seed/path, camera/edit/entity
counts, capability/route, frame distributions, image mismatch IDs and fallback reasons. Mark
unavailable counters unavailable. GPU timing collection must not introduce blocking readback into
the measured path. Include both feature-off and feature-on cases with identical simulation.

- [ ] Extend paired ID/depth assertions across the deterministic cases in the design; document
  edge-only tolerances in the oracle. Reject a lost reference-visible object interior or any frame
  of stale hiding. Record finite tested scope rather than claiming universal correctness.
- [ ] Run 512/4,096/16,384 targets, one/four moving cameras, 0/10/100 edits per second, and both open
  and occluded layouts. Use at least 300 warm-up and 1,800 measured frames per Player case.
- [ ] Record median/p95/p99 total frame, main/render CPU, GPU where available, GC, accounted memory,
  upload bytes/backlog age, submitted work and useful moving-camera occlusion. Include no-occlusion
  regressions; do not infer acceleration from fewer drawn instances alone.
- [ ] Run repeated edit/cancel/enable/disable and World unload/reload for 30 minutes; compare settled
  resource counts and inspect leaks, queue bounds and stale callback handles.
- [ ] Declare accepted fast path only if correct and end-to-end faster in a declared moving-camera
  workload. Otherwise retain experimental status, report the limiting stage and discuss options.
- [ ] Commit reports independently from source changes so the tested package revision is identifiable.

## Task 7: package delivery and regression acceptance

**Files:** Shipped DynamicOcclusion documentation/README/changelog/descriptor, relevant assembly
guides, consumer helper only if needed to import the second sample, and this plan's outcome.

- [ ] Document actual setup, runtime geometry mutation API, block semantics, fail-open behavior,
  renderer requirements, supported materials/views and measured limitations. State that algorithm 2
  and persistent runtime PVS generation are not implemented by algorithm 3.
- [ ] Run `python Tools/ValidateAgentDocs.py` and `python -m unittest discover -s Tools/tests -v`.
  For documentation/tool changes, passing commands and exit codes are the acceptance evidence.
- [ ] Run all affected/new EditMode and PlayMode assemblies through Coplay; retain job IDs,
  discovered/pass/fail/skip counts, and actual GPU capability. Include existing legacy bake tests.
- [ ] Commit source on master, then run `python Tools/CreateDeliveryConsumer.py --revision <commit>`.
  Open only the supported single consumer normally; verify actual Git payload, stock UPM closure,
  Basic and DynamicCity sample installation, package tests and no required root resources.
- [ ] Build and actually run Windows Player for the dynamic demo; verify first-person edits,
  moving agents, shadow preservation, feature disable and ordinary-rendering comparison. Build
  success alone is not Player acceptance. Capture evidence before scoped consumer cleanup.
- [ ] Update this record with actual outcomes and unresolved limitations, remove only owned scratch
  content after its process exits, restore main Editor routing and commit final docs. Do not push.

## Self-review and handoff

The design's ownership, independence, incremental blocks, conservative coverage, current-view
validity, demo, budgets, measurements and delivery each have an owning task. Review-focus cases
map to the task numbers above. The stock route failed G1; the user selected the indirect
continuation, Task 0b passed and Tasks 1-2 are implemented. Task 3 is in progress. No complete
runtime renderer/demo, full-frame performance acceptance or new Player acceptance is claimed.

This is one algorithm's plan. Algorithm 2, including evaluation of a dynamic/loose octree versus
sparse blocks and structure-derived visibility, will be planned separately after this work.
Current acceptance is limited to the scoped outcomes recorded for each task above.

Independent design review raised one important question, resolved before handoff: initial
coverage is explicitly single-sample, with MSAA fail-open. The renderer feasibility gate remains
an investigation outcome, not an assumed capability.
