# Algorithm 3 indirect transport proof

**Result:** Task 0b passes its scoped transport gate. The user authorized this package-owned
continuation after the [stock route failed](DynamicOcclusion-Integration.md). General Hi-Z,
dynamic registration and Player/performance acceptance remain pending.

Environment: Unity 6000.6.8f1, URP 17.6.0, Windows DX12, RTX 4060 Ti. Development-only fixture in
`Assets/Tests/DynamicPrototype`; production has no dependency on these files or its Editor lookup.

## Observed tests

- RED job `7fd35b3ca52747a1b993c367a9abad8d`: no indirect submission left the reference pixel
  black rather than red. Public indirect submission then passed in `edafd78a0d684492881ce6bca2bc9666`.
- Shadow RED job `a0bec4c5be1c449b9e910f8706b60db5`: omitting independent shadow submission
  produced 493 different pixels. Enabling the all-caster submission passed in the two-test
  job `62a5ac4fc62641a4bcf296839303095a`.
- Final coupled job `4f0eb7901a3f460f85d94470e19a47f5`: **23/23 passed**, zero failures/skips.
  This includes the existing 20 functional checks, same-frame alternating GPU counts for two
  cameras, exact 256x256 hidden-caster shadow comparison, fence-based retirement and injected
  partial argument-allocation failure cleanup.
- The interrupted previous job `b9df0c5133c8442f80cd48dad8a0b397` did not initialize because a
  misplaced `using System` caused CS1529. It is not test evidence. The compilation error was
  corrected after cancelling the actual pending Unity run through public `TestRunnerApi` via
  MCP. No scene save/discard, GUI automation or private reflection was needed for recovery.

Pixel readback is assertion-only, after rendering. The submission path never reads a visibility
result on CPU and never changes stock `MaterialMeshInfo` bits. The GPU producer merely writes
one/zero instance count from the test's requested state; it is not an occlusion algorithm.

## Proven public dataflow

1. `RenderPipelineManager.beginCameraRendering` selects the view's dedicated argument buffer.
2. `Graphics.ExecuteCommandBuffer` queues a compute write to its instance count on the graphics queue.
3. `Graphics.RenderMeshIndirect(RenderParams, Mesh, GraphicsBuffer, ...)` registers that camera's
   color submission with `shadowCastingMode = Off`. `RenderParams.camera` scopes it to one view.
4. A separate immutable full-count argument buffer registers `ShadowsOnly` when required.
   Hidden camera color arguments cannot shorten the light's caster set.
5. Installed URP calls `context.Cull` and submits its RenderGraph before the enclosing public
   `endContextRendering` event. A graphics fence created there follows both color and shadows.
6. Dispose detaches new submissions, waits for the context-end boundary if necessary, then polls
   fence completion before releasing buffers. It does not block the CPU. The root fixture uses
   `EditorApplication.update` only to poll retirement; shipped code must supply a runtime owner.

Argument allocation uses `GraphicsBuffer.IndirectDrawIndexedArgs.size` and typed initialization.
The tested DX12 compute view is `RWByteAddressBuffer`; instance count is at byte offset 4 in the
installed `UnityIndirect.cginc` layout. The shader initializes Unity's indirect draw helpers.
These findings do not establish another graphics API's layout or synchronization support.

## Production decisions following this proof

Use this public transport, without a custom BRG callback or required renderer feature. Keep
per-view culled IDs/arguments and independent full-visible/full-shadow data. Explicit indirect
materials remain opt-in; unsupported stock materials/LOD/deformation stay in stock rendering.
World-owned immutable snapshots and a runtime fence-retirement owner must replace the fixture's
Editor-only lifetime helper. No product speedup, arbitrary material compatibility or whole-scene
GPU occlusion claim follows from this transport test.
