# Dynamic geometric occlusion: algorithm 3

**Status:** Proposed design for the requested implementation plan. No runtime implementation or
performance acceptance is implied. Algorithm 2 will have a separate design and plan.

**Intent:** Add general runtime geometric occlusion for a changing ECS world, preserving the
existing static baker. Demonstrate construction and demolition of individual building pieces,
moving entities outdoors and indoors, and a first-person camera in a small reproducible city.

## Decisions and boundaries

The user selected two independent future algorithms: structure-assisted visibility (algorithm 2)
and geometry/depth visibility (algorithm 3). This document covers only algorithm 3. They are not
two settings of one algorithm. Their simultaneous use and mask composition remain undecided.

Algorithm 3 must not require a building grid, room graph, portal authoring, voxel world, or
precomputed PVS. The demonstration uses modular grid buildings because they make changes easy
to reproduce, but arbitrary rotations, off-grid placement and nonuniform sizes must also be tested.
Shared infrastructure must not force algorithm 2 to consume depth textures or algorithm 3 to
consume rooms. Do not add a speculative common algorithm framework before a second implementation.

The existing bake asset format, Editor bake workflow, CPU/GPU recovery and ordered legacy
evaluator retain their contracts. Register an object with only one visibility owner in this
milestone; reject overlapping legacy/dynamic ownership. No automatic intersection of masks.
Rendering visibility never disables an Entity, physics, logistics or gameplay simulation.

Baseline: Unity 6000.6.8f1, Entities/Entities Graphics 6.6.0, URP 17.6.x, Windows DX12.
Initial occlusion coverage is single-sample only. MSAA targets return Unavailable and retain
ordinary rendering until every-sample coverage and depth reduction have separate acceptance.
Keep stock UPM packages, C#/HLSL compiled by Unity, and the complete shipped implementation
inside `Assets/CullingSystemDOTS`. Do not introduce native DLLs, private reflection or cache patches.
Other APIs/platforms, XR, reflection cameras and deformed/skinned occluders are future acceptance.

## Approaches and integration gate

The selected algorithm is conservative depth-based occlusion with a hierarchical depth buffer
(Hi-Z). It can test live bounds without knowing the game's building semantics. Ray sampling from
the current Editor baker is not a per-frame conservative visibility substitute.

The unresolved issue is how to consume its GPU result efficiently in stock Entities Graphics.
The installed renderer owns a private `OnPerformCulling` callback. Public BRG indirect commands
exist in Unity 6.6, but that does not expose the stock Entities Graphics callback or transfer its
rendering features to a package-owned renderer automatically.

| Integration candidate | What a prototype can establish | Decision boundary |
| --- | --- | --- |
| Supported stock-renderer extension | Current-view GPU results actually exclude stock ECS draws without broad job completion or GPU-to-CPU waiting | Preferred if an actual public path is demonstrated |
| Strict asynchronous GPU readback and stock visibility mask | Safe reference with exact view/occluder/bounds validation; changed or pending data renders normally | Diagnostic/reference route; continuous camera motion may invalidate every result |
| Package-owned BRG indirect submission | GPU-resident result consumption for explicitly opted-in rigid meshes | Separate rendering ownership and feature scope; needs user decision before product implementation |

A blocking readback may be measured as a diagnostic lower bound but cannot be silently selected
as the production solution. A vertex/fragment discard that leaves all draw/vertex work submitted
is not evidence of draw occlusion. Disabling safety checks is not a performance solution.

**Gate G1:** Before building the complete feature, record actual APIs, render ordering, correctness,
CPU/GPU costs and useful culling during continuous first-person movement in a minimal prototype.
If no supported stock route meets correctness and yields useful movement-time culling, stop and
explain the evidence. Present a package-owned indirect renderer or a CPU software-occlusion
alternative with scope/cost differences; do not substitute either silently. The remaining plan
is conditional on a recorded route. Existing user instructions explicitly prefer discussing an
unresolved correctness/integration limitation to hiding it behind a weaker claim.

## Runtime data and block updates

Use a World-owned registry with generation-bearing handles. Separate immutable reusable mesh
geometry, occluder instances and occludee bounds. ECS bindings store full Entity versions and World
identity. Read transform/bounds changes after transform updates and before the visibility snapshot.
External providers can submit batches; they do not have to expose building semantics.

Spatial blocks are units of geometry membership, upload scheduling and diagnostics, not baked
lists of objects visible from a region. Default block edge is 16 world units, configurable per
world. Use floor division for negative coordinates. Track every block touched by an object's
bounds; avoid duplicate draw/test submission. Moving occludees update bounds without rebuilding
occluder geometry. Distinct mesh data revisions invalidate cached geometry even if the mesh
object reference stays the same. Runtime-created meshes must have a documented update API.

Coalesce repeated edits to a block and process the newest revision. Maintain separate geometry,
instance-transform and bounds revisions. Keep the old immutable geometry alive until GPU/Jobs
readers finish; cancel publication of superseded work rather than freeing its memory early.

Removing/moving/changing an occluder immediately invalidates dependent visibility. The first
implementation conservatively increments a global occluder epoch: a local upload change may
invalidate all view results. Localized *visibility* invalidation requires a separate dependency
proof and is not promised. Queued/stale/missing occluders are omitted from the depth source;
that reduces culling, while their actual graphics remain rendered.

Start with sparse hashed fixed blocks, not an octree requirement. This does not decide algorithm 2's
index. Expose stable bounds/change data so that later indexing choices remain replaceable.

## Depth and visibility contract

Only opted-in opaque rigid geometry contributes occlusion. Initially use actual triangles;
optional proxies must stay inside the opaque surface and preserve openings from supported views.
An outer AABB enclosing a building is not a safe solid occluder for its windows/interior.
Alpha clipping, transparency, uncertain material displacement and unsupported geometry do not
contribute occlusion in the first version. Their graphics remain handled by the normal renderer.

Depth must include eligible occluders independently of their previous visibility; otherwise a
hidden object cannot reliably become visible again. Use a dedicated current-view depth source
or prove the ordering/coverage of a pipeline-provided source. Do not read this frame's post-opaque
depth and claim to have removed those same opaque draws earlier in the frame.

At the reference render grid, uncovered samples contain far depth. Reduce by the farthest depth
in a footprint (maximum for forward Z, minimum for reversed Z). Handle odd mip sizes by ceil
division and treating outside/unknown coverage as uncovered. Compare the candidate's nearest
possible depth against every overlapped hierarchy texel, with a bias toward keeping it visible.
Projected bounds enclose the complete candidate. Camera-inside, near-plane crossing, invalid
projection, NaN/overflow, uncertain coverage or unsupported projection all return PotentiallyVisible.

Low-resolution center-sampled occluder rasterization alone is not conservative at the final image
resolution. Gate G1 must choose full reference-grid depth plus conservative reduction, or prove
underestimated coverage for each coarse footprint. Unproven lower-resolution settings cannot hide.
Bias is an additional numerical precaution, not a replacement for correct coverage.

Three results are sufficient: PotentiallyVisible, Occluded, Unavailable. Unavailable renders
normally. Keep a reason counter for fallback; never reuse the last hidden state on failure.
First implementation has no heuristic temporal reuse for hiding. Asynchronous results can hide
only with an identical view signature, current global occluder epoch and unchanged candidate
generation/bounds revision. View signature includes matrices, jitter, viewport, target dimensions,
render scale, projection and eye. An age limit alone is not proof of validity.

All applied results must be validated at their presentation boundary. Camera movement, resize,
teleport, construction, demolition and entity replacement invalidate before drawing. GPU errors,
unsupported hardware, exhausted budgets and missing setup restore ordinary rendering. Unsupported
camera stacks fail open explicitly. Enabling/disabling/destroying the feature restores original
render flags. Shadow visibility is a separate pass decision: preserve normal shadow casting
until a separately proven shadow path exists; a camera-hidden object may still cast a visible shadow.

## Budgets and configuration

Provide validated settings for block edge, maximum active instances, geometry bytes, per-frame
upload bytes, maximum in-flight requests, candidate count and depth dimensions. Initial demo
defaults: 16-unit blocks, 256 KiB upload target per frame, two in-flight requests, 65,536 registered
instances and 128 MiB of accounted feature-owned GPU/native payload. These are proposed starting
values, not measured capacities or whole-process memory limits. Overflow renders affected objects
normally and reports a reason. Do not allocate unbounded work queues under continuous editing.

Capability failures disable only dynamic occlusion. CPU fallback for this algorithm initially
means ordinary rendering, not the Editor CPU bake or an unimplemented software rasterizer.
Expose CPU capture/apply time, GPU depth/pyramid/test time when available, queued blocks, uploaded
bytes, pending/accepted/rejected results, candidates and fallback reasons. No synchronous readback
or broad `CompleteAllTrackedJobs` is allowed in an accepted fast path without an explicit measured
and documented decision at G1. Zero-allocation and frame-time guarantees require measurement.

## Demonstration and acceptance

Ship an importable `DynamicCity` sample, separate from `Basic`, usable without Editor APIs or MCP.
Build the geometry from shared cubes and simple colored materials. Default bounded world is
128 by 128 units, a 4 by 4 arrangement of two-storey buildings, and 512 moving ECS occludees split
between street and indoor routes. Walls, floors, roof pieces and doors are individually editable;
provide an off-grid/rotated fixture as evidence that the algorithm does not require the grid.

Provide first-person movement/look, deterministic guided tour, piece placement/removal, automatic
seeded construction/demolition, moving-entity pause, and an ordinary-rendering comparison switch.
Freeze the simulation for an A/B image capture so both renders see identical state. Display
block bounds, dirty/pending status, backend capability, culling and fallback counters on demand.
Commands are distinct from visibility: hidden entities continue following their routes.

Deterministic cases: reveal an occupied room by deleting one wall; open a door; remove a floor;
turn/teleport the camera; move an entity across a doorway/block boundary; rapidly replace an
Entity at a reused index; edit geometry faster than it uploads; resize the render target; unload
and reload the World. Verify both images and preservation of normal shadows/simulation.

Pixel acceptance uses paired same-state object-ID/depth captures against ordinary rendering.
Use identical single-sample render targets; resolved MSAA depth is not an equivalent oracle.
Do not accept missing reference-visible interiors of an object. Edge-only raster tolerances must
be fixed and reported in the test method; they cannot excuse a whole missing object or a frame
of stale occlusion. CPU predicate tests compare the hierarchy test to exhaustive reference-grid
depth coverage. Passing finite tests is scoped evidence, not a proof for arbitrary geometry.

Benchmark 512, 4,096 and 16,384 occludees, one and four independently moving cameras, no-occlusion
and heavily occluded scenes, and 0/10/100 piece edits per second. Use fixed seeds and paths,
at least 300 warm-up and 1,800 measured frames for Player performance runs. Report median/p95/p99
CPU/GPU/frame time, feature-owned memory, GC where reliable, upload/backlog age and useful culling
while moving. Reduced draw counts alone do not pass performance acceptance. A releasable fast
path must improve end-to-end frame time in at least one declared moving-camera occlusion workload
without correctness failures; report regressions and limits in every other case.

## Delivery and deferred work

Use pure runtime contracts, ECS registration, renderer integration and URP-specific code in
separate assemblies. Do not make existing Core/legacy evaluation depend on this algorithm.
Serialize shader/resource references in a shipped resources asset referenced by the renderer
feature. Player loading must not use AssetDatabase or assume the package is installed under Assets.
The sample supplies its own renderer/pipeline setup and explicit setup instructions; it does not
depend on root development settings or silently replace a consumer's active render pipeline.
The Editor owns only setup/authoring conveniences. Exact file/task boundaries are in the
[implementation plan](2026-11-06-dynamic-geometric-occlusion-plan.md).

Algorithm 2, runtime PVS generation, octree implementation, room extraction, portal traversal,
algorithm composition, animation/simulation LOD and a general replacement renderer are deferred.
A replacement renderer can become a separately approved continuation after G1, not a hidden task.

The independent builder check identified the raster sample domain as an important missing
decision. This revision resolves it with the single-sample/fail-open MSAA contract above.
The check found no remaining important planning questions about G1 or temporal validity.

Work and commit on master only. Do not push. Preserve the live Unity scene and unrelated files.
Use the existing single Git-install consumer when needed, capture results, then remove only owned
temporary content after its Editor has exited. See [testing](../../TESTING.md).

## Research and current evidence

- [Current rendering contract](../../../Assets/CullingSystemDOTS/Documentation~/RenderingIntegration.md)
  and [measured adapter cost](../../Validation/Readiness.md) describe the existing implementation;
  they do not validate the new algorithm.
- [Unity GPU occlusion](https://docs.unity.com/en-us/engine/6000.6/manual/analysis/graphics-performance-profiling/in-urp/gpu-culling)
  documents the GRD dependency; enabling that setting is not proof of stock ECS integration.
- [BRG indirect commands](https://docs.unity.com/en-us/engine/6000.6/script-reference/unityengine/rendering/batchdrawcommandindirect)
  establish a public renderer-building primitive, not an Entities Graphics extension hook.
- [NVIDIA hierarchical occlusion discussion](https://developer.nvidia.com/gpugems/gpugems2/part-i-geometric-complexity/chapter-6-hardware-occlusion-queries-made-useful)
  explains grouping, latency and the correctness risk of unverified previous-frame results.
- [Voxel Tools block/LOD hierarchy](https://voxel-tools.readthedocs.io/en/latest/smooth_terrain/)
  and [NVIDIA sparse voxel octrees](https://research.nvidia.com/publication/2010-02_efficient-sparse-voxel-octrees-analysis-extensions-and-implementation)
  inform the separate algorithm-2 discussion; they do not establish dynamic visibility correctness.
