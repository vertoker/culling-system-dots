# Dynamic occlusion integration investigation

**Status:** Task 0 in progress. No shipped algorithm-3 implementation or accepted renderer route.

This record executes Gate G1 of the
[implementation plan](../Superpowers/Plans/2026-11-06-dynamic-geometric-occlusion-plan.md).
The root-only [probe](../../Assets/Tests/DynamicPrototype/AGENTS.md) isolates GPU-result delivery
to stock Entities Graphics. Tasks 1-7 remain conditional on the gate verdict.

## Installed API and ordering

Inspected Unity 6000.6.8f1, Entities Graphics 6.6.0 and URP 17.6.0, read-only. Live public API
reflection confirmed that `EntitiesGraphicsSystem` declares registration/material/mesh access,
statistics and upload-pool operations, with no public culling event or GPU visibility input.
This is a scoped finding about these installed sources, not every future Unity version.

- `EntitiesGraphicsSystem.cs:1023` creates its own `BatchRendererGroup` with private
  `OnPerformCulling` (`:1848`). No supported callback replacement was found.
- Its internal include/exclude path (`:1503`) reads Editor picking/selection-outline lists.
  That is not a runtime camera visibility extension.
- `DrawCommandGeneration.cs:1692` publishes CPU `BatchDrawCommand` storage. Its internal
  `IndirectList<T>` is a CPU container, not a GPU indirect-command hook.
- `UniversalRenderPipeline.cs:1094` calls `context.Cull` before renderer pass registration and
  RenderGraph recording (`:1129-1130`). Adding a post-depth renderer feature cannot retroactively
  remove the same frame's already-generated stock draw list.
- Public [BRG indirect commands](https://docs.unity.com/en-us/engine/6000.6/script-reference/unityengine/rendering/batchdrawcommandindirect)
  exist and were confirmed in the live Editor. Using them requires a renderer-owned submission
  route; their existence does not expose the stock renderer's private output.
- [URP GPU occlusion](https://docs.unity.com/en-us/engine/6000.6/manual/analysis/graphics-performance-profiling/in-urp/gpu-culling)
  documents GPU Resident Drawer setup. Enabling that setting does not establish an ECS hook.

SHA-256 of inspected sources, in the order above:

| Source | SHA-256 |
| --- | --- |
| EntitiesGraphicsSystem.cs | `987c209eceb8188dab73ac0cf3ae995941d54ac413d7569d74a3ceb5a00a0ee2` |
| DrawCommandGeneration.cs | `3c8f4138caf3e6b47b06fe228fb388b5e439abf783027ce4f81879e8b0f72f9d` |
| UniversalRenderPipeline.cs | `0120824f0cbfb3322df96edc7af3f9251969800ae3c0cf0bef455a5debdc0231` |

## Diagnostic scope

The GPU producer classifies axis-aligned cubes behind one known opaque wall using parallel +Z
orthographic rays. It conservatively leaves edge-touching cubes visible. Other orientations and
MSAA return no hidden bits. This closed-form fixture deliberately omits depth rasterization,
pyramid construction and general geometry. Its cost is a handoff diagnostic lower bound, not
the cost or acceptance of Hi-Z.

The strict async route requests at camera end, polls before the next camera draw, and validates
exact view/target/projection state, global wall epoch and candidate revision. Invalid results
restore ordinary rendering. CPU application completes tracked readers and changes only
`MaterialMeshInfo` enable bits, restoring their baseline after each camera. No Entity is disabled.
Two request buffers per view are bounded; teardown waits for outstanding requests before release.
The separate blocking route waits deliberately and is never a production fallback.

The fixture has shadows disabled. It cannot establish a new shadow-capable renderer. Existing
shadow-companion parity remains a separate regression check; preserving shadows in a new GPU
renderer would require its own light-view submission contract.

## Execution evidence

- RED PlayMode job `982d0372da1941f7af4820753dbd9a68`: 2 failures, zero skips. The empty CPU
  mask left the target pixel red (255); the explicit stock GPU capability assertion failed.
- After implementing the render-only CPU mask, job `4911e0e228df4d4ab3dc924e86bf0f32` passed 1/1.
- GPU producer/validity RED job `bb9a1cc7540945f9a5909b4a76ba14da` failed 1/1: hidden fixture
  cube returned 0 instead of 1. After the implementation, `18d22731dc394f82b4f74ff17dfdc5ea`
  passed 2/2 (GPU validity plus CPU render removal/restoration).
- PlayMode job `f4c09c6c6be94e64b2692738522e122e` passed 3/3: GPU validity, CPU render masking,
  and paired 128x128 images for opposite cameras (all pixels equal to ordinary rendering), wall
  removal, turn and teleport. Targets and Entities restored after camera scopes.
- Coupled PlayMode job `3f12a71f21a54007b2d750fe6b9e98bc` passed 18/18, zero failures/skips:
  the three new checks and all 15 existing functional rendering/sample checks, including visible
  shadows, simulation, LOD, camera stacks and streaming. The existing explicit readiness benchmark
  was deliberately excluded to preserve the pre-existing edited readiness report.
- An initial combined group/name filter (`71904cc93dad40359ace62505d40fea7`) discovered zero tests;
  it was rejected as evidence and replaced by the exact-name 18-test run above.
- `python Tools/ValidateAgentDocs.py` passed (81 Markdown files); Python tooling passed 35/35.
- Benchmark results will be recorded after their actual run against committed probe source.

After some PlayMode exits/domain reloads the Editor emitted a Unity assertion:
`Unity.PlayMode.Editor.DefaultScenario.StateSyncedSanityCheck`, "was Stopping but expected Idle".
It did not fail the completed test runs; later console checks were empty. It is recorded as an
Editor lifecycle limitation, not hidden as clean execution of every intermediate operation.

The explicit missing-capability test is an executable record of an unresolved requirement,
not a reflection-based proof of API absence. Normal regression runs exclude that explicit test.
No native code, private reflection, Unity package edits, scene saves or pipeline changes were used.

## Decisions and limits

Ruling: isolate presentation with the restricted GPU fixture before implementing general Hi-Z.
A missing consumption path is independent of the mask producer. The cost of this narrower probe
is that arbitrary-geometry correctness and full GPU feature cost remain unmeasured.

No Player, Git-install, feature memory-capacity or dynamic-city release acceptance is claimed by
this investigation. The existing static baker and runtime are unchanged.
