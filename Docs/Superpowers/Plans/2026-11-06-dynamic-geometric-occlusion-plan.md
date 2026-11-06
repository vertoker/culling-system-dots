# Dynamic geometric occlusion implementation plan

> For agentic workers: execute sequentially with the repository's `execute-plan` skill and, when
> available, `superpowers:executing-plans`. Keep one owner for live Unity. No branches or worktrees.

**Status:** Execution authorized; Task 0 integration investigation is in progress. Tasks 1-7 remain
conditional on Gate G1, not authorization to build a replacement renderer.

**Goal:** Implement independent algorithm 3: configurable runtime geometric occlusion for a
changing ECS world, with incremental block geometry updates and a reproducible construction demo.

**Architecture:** World-owned live geometry/bounds registry, GPU conservative hierarchical depth
tests and a separately validated presentation boundary. No building/room knowledge or PVS is
required. Preserve the existing static bake and leave algorithm 2 and composition to separate plans.

**Tech stack:** Unity C#/Burst/Jobs for runtime registration and numeric preparation, HLSL compute
and URP RenderGraph for depth testing, stock Entities Graphics through a route proven at G1.

**Spec:** [Design and acceptance contract](2026-11-06-dynamic-geometric-occlusion-design.md).
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
- [ ] Run the probe assertions through the live PlayMode runner and capture RED/capability evidence.
- [ ] Inspect stock renderer source read-only and the public 6000.6 APIs. Prototype a supported
  extension only if one exists. Separately measure strict asynchronous readback/stock masks and
  optional blocking-readback diagnostic; do not use private callback replacement.
- [ ] For the strict async reference, reject on any view signature/global occluder epoch/bounds
  revision mismatch; verify it does not incorrectly hide during continuous motion. Record the
  fraction of movement frames with useful accepted occlusion, including zero.
- [ ] Run idle/moving-camera and 0/10/100-edits-per-second cases for 512/4,096 targets; measure the
  complete path, not just the compute dispatch. Record source revision/hardware/settings.
- [ ] Write Gate G1 verdict: demonstrated stock fast path, safe but ineffective reference, or no
  acceptable stock route. An acceptable fast path must avoid missing visible objects and provide
  useful culling while moving; asynchronous output invalidated every movement frame is not enough.
- [ ] If G1 fails, report the limitation and choices to the user, stop feature implementation and
  leave Tasks 1-7 unchecked. A package-owned BRG indirect renderer or CPU rasterizer requires an
  explicit new decision; a prototype is not release acceptance.
- [ ] If G1 passes, freeze the concrete rendering/dataflow addendum described above and rerun its
  tests. Commit exact owned prototype/evidence paths on master. Retain the probe as development
  evidence, separate from shipped code, without a second retained Unity project.

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

- [ ] Add failing tests `OldGenerationCannotMutateReplacement`, `WorldIdentityRejectsForeignHandle`,
  `SnapshotRetainsGeometryUntilLastReader`, `SameMeshReferenceReplacementChangesRevision`,
  `InvalidSettingsFailBeforeAllocation`, and `LegacyOwnershipConflictIsExplicit`.
- [ ] Add validity tests that change each view-signature field, global occluder epoch and candidate
  generation/bounds revision independently; every stale hidden result becomes PotentiallyVisible.
- [ ] Run the new EditMode fixtures and record RED before implementation.
- [ ] Implement contracts, checked setting validation, World ownership and snapshot retirement.
  Keep this assembly independent of legacy Core and URP. Document provider mutation obligations.
- [ ] Run the fixtures to GREEN, inspect native/managed lifetime under repeated create/dispose,
  validate folder guidance and commit exact owned paths on master.

## Task 2: incremental block data and ECS capture

**Files:** Blocks/update queue, ECS components/system/bindings, block tests and lifetime tests.

**Interfaces:** `OcclusionBlocks.GetOverlappingBlocks(in Bounds bounds, NativeList<int3> output)`
uses configured block size and floor division. `OcclusionWorld.ProcessUpdates(in OcclusionUpdateBudget
budget)` coalesces changes and prepares at most the configured upload payload. Dynamic ECS components
opt in explicit occluder/occludee roles and geometry handles. `DynamicOcclusionSystem` owns one
registry per World and captures current world bounds/transforms before presentation; managed mesh
registration is separate from numeric Jobs work. Update budget limits accounted payload, not a hard
wall-clock bound for indivisible engine calls. Apply render bindings only through Task 4's boundary.

- [ ] Add failing tests for negative coordinates, cross-block bounds, empty blocks, off-grid rotated
  pieces, repeated edits to the same block, and overflow-safe huge/invalid coordinates.
- [ ] Add `MovingOccludeeDoesNotRebuildOccluderGeometry`, `RemovalInvalidatesBeforeQueuedUpload`,
  `NewRevisionSupersedesPendingBuild`, `EditFloodBoundsQueueMemory`, and
  `WorldDisposeJoinsJobsAndRetiresGpuReaders` (GPU half completed with Task 3).
- [ ] Observe RED; implement sparse hashed block membership, revision coalescing, bounded queues,
  immediate global invalidation and omission of unavailable occluders from the depth source.
- [ ] Implement full-Entity binding capture and explicit scene/World unload cleanup. Geometry must
  be current even when last frame's object was hidden. Unknown state retains normal rendering.
- [ ] Observe GREEN; profile capture/upload preparation at 512 and 4,096 instances without claiming
  render speedup yet. Commit exact owned paths on master.

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
map to the task numbers above. The critical unresolved fact is the renderer route; G1 explicitly
prevents pretending it is solved. No implementation tests or new Player results exist yet.

This is one algorithm's plan. Algorithm 2, including evaluation of a dynamic/loose octree versus
sparse blocks and structure-derived visibility, will be planned separately after this work.
No code implementation follows from this planning-only turn.

Independent design review raised one important question, resolved before handoff: initial
coverage is explicitly single-sample, with MSAA fail-open. The renderer feasibility gate remains
an investigation outcome, not an assumed capability.
