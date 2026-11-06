# Dynamic occlusion integration investigation

**Status:** Task 0 complete; Gate G1 failed. Feature implementation stops pending a rendering
architecture decision. The user subsequently selected the package-owned GPU indirect continuation;
this report remains the stock-route investigation. No shipped algorithm-3 implementation is claimed.

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
- Benchmark job `526f1f64952046a3a38fc24407518b8f` passed 1/1 in 70.88 seconds: 72 cases,
  zero readback errors. Source: `39f782fa9fc0bc9c7cea6c614e6a65b9946bd045`; raw results are in
  [the machine-readable report](DynamicOcclusion-Integration.json).

After some PlayMode exits/domain reloads the Editor emitted a Unity assertion:
`Unity.PlayMode.Editor.DefaultScenario.StateSyncedSanityCheck`, "was Stopping but expected Idle".
It did not fail the completed test runs; later console checks were empty. It is recorded as an
Editor lifecycle limitation, not hidden as clean execution of every intermediate operation.

The explicit missing-capability test is an executable record of an unresolved requirement,
not a reflection-based proof of API absence. Normal regression runs exclude that explicit test.
No native code, private reflection, Unity package edits, scene saves or pipeline changes were used.

## Decisions and limits

### Gate G1 verdict

No supported stock GPU-resident input was found, and the measured strict async reference is safe
but ineffective during continuous motion under the chosen exact-view validity contract. It
accepted **0/1,800 moving view samples**, across all counts/camera counts/edit rates, with zero
hidden applications. This is not a proof that every future conservative temporal technique is
impossible; such a technique was explicitly outside this baseline contract.

For idle views, the async route accepted useful hiding in 600/600 samples without edits,
169/600 at 10 requested wall toggles/second, and 11/600 at 100 requested toggles/second.
The file records actual edit counts/rates; requested rates are not guaranteed by an Editor loop.
Across these short runs, actual rates were 7.36-9.78 and 95.35-98.25 toggles/second respectively.
The bounded two-slot rings rejected 1,118 submissions while occupied; no extra slots were allocated.
That is diagnostic backpressure, not a guarantee of accepting every frame's request.

Representative moving-camera cases with no edits, milliseconds:

| Targets | Cameras | Ordinary frame median | Strict async median | Blocking median | Strict async begin + end per view |
| --- | --- | --- | --- | --- | --- |
| 512 | 1 | 4.634 | 5.981 | 6.646 | 0.506 + 0.522 |
| 512 | 4 | 6.064 | 10.942 | 15.539 | 0.466 + 0.483 |
| 4,096 | 1 | 3.335 | 11.811 | 12.343 | 3.422 + 3.476 |
| 4,096 | 4 | 6.537 | 36.016 | 40.910 | 3.379 + 3.474 |

Hardware: Ryzen 7 7700, RTX 4060 Ti, Windows 11, Direct3D12. These are short **Editor diagnostic**
distributions, 10 warmup/60 measured frames per case, not Player capacity/speedup estimates.
Each view targets 128x128 single-sample rendering. The mask producer is intentionally much
simpler than full Hi-Z. Each active route completes tracked jobs twice per view and loops over
every target to apply/restore its enable bit. Blocking GPU wait alone averaged 0.847-1.052 ms
per view in the four moving/no-edit cases; its full begin/end/frame cost is larger.

Stock statistics in the JSON are **snapshots sampled once per coroutine frame**, averaged over
the run, despite the historical `*PerFrame` field names. They include other cameras/Entities,
can lag view application and are not an isolated sum of submitted draws for our cameras. For
example, 4,096 targets/one idle camera has sampled instances 4,096 ordinarily versus 2,112 with
strict async hiding, while draw commands stay 1 due to instancing. This supports instance-list
filtering, not a claim that one hidden object removes one draw call. Multi-camera moving cases
show why the counters must not be used as exact draw-saving acceptance.

GPU/GC isolated measurements are unavailable, explicitly recorded rather than set to zero.
The finite image tests validate this fixture only. The new reference masks disable shadows;
the passing legacy shadow checks do not validate shadows for an unimplemented new renderer.

### Continuation options

1. **Package-owned BRG indirect rendering for explicit opt-ins.** Keep visibility and draw
   arguments on the GPU, preserve ECS simulation, and leave other objects in stock Entities
   Graphics. This requires a separate supported-material/LOD/shadow/render-ownership contract,
   ordinary-rendering fallback and new tests; it does not mean replacing Unity's entire pipeline.
   No DLL, private reflection or patched UPM package is required by this approach.
2. **CPU software occlusion in C#/Jobs/Burst.** Keep the geometric algorithm and stock renderer,
   produce a conservative current-view mask on CPU, and avoid GPU readback. This is distinct
   from the existing Editor CPU baker and from algorithm 2. Raster cost and the measured stock
   mask synchronization/mutation costs still require optimization and measurement.

After these measurements, the user explicitly selected option 1 and requested extending the plan
and implementing it. The stock G1 failure remains valid; the selected continuation needs its own
render-transport proof and concrete rendering/dataflow addendum. Tasks 1-7 remain unchecked here.

### Independent review and cleanup regression

One read-only independent review confirmed the scoped G1 conclusion, source hashes and JSON
aggregates. It found an important constructor failure leak in GPU/stand setup. The fix validates
prerequisites before live allocation, disposes partially allocated buffers/stand resources, and
places GPU construction within the test's camera cleanup scope. Test-only allocation factories
reproduce failure after a real successful allocation; they are not shipped product abstractions.

RED job `bbc2c02b2c254156961510496d2b8452` failed both new cleanup assertions as expected: the
earlier buffer remained valid and the earlier GPU owner remained usable after later setup failure.
After the fix, coupled PlayMode job `20bef766594840148080af9a1c3c6523` passed **20/20**, zero
failures/skips, including both cleanup regressions and the earlier 18 functional cases. No second
review round was requested. Benchmark values above remain the actual `39f782f` run; the subsequent
fix changes construction/error cleanup, not the measured steady-state camera handlers/shader.

The reviewer did not independently rerun jobs or judge general Hi-Z, new-renderer shadows,
Player performance, or exhaustive impossibility of every public integration route. Those remain
explicitly outside this investigation's acceptance; the current-view renderer route still needs proof.

Ruling: isolate presentation with the restricted GPU fixture before implementing general Hi-Z.
A missing consumption path is independent of the mask producer. The cost of this narrower probe
is that arbitrary-geometry correctness and full GPU feature cost remain unmeasured.

No Player, Git-install, feature memory-capacity or dynamic-city release acceptance is claimed by
this investigation. The existing static baker and runtime are unchanged.
After the runs, the original `Assets/Scenes/SampleScene.unity` was still loaded, clean, with the
same three roots; Play Mode/compilation were stopped and the console contained no errors.
