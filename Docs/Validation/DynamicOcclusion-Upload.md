# Dynamic occlusion GPU mesh streaming and retirement

Tested source: **`56326ec`**. Windows Editor, Unity 6000.6.8f1, DX12, RTX 4060 Ti, 2026-11-06.
This extends the [depth foundation](DynamicOcclusion-Depth.md); Task 3 remains in progress.

## Transport and bounded payload

`OcclusionGpuMesh` creates an independently owned position/normal/index layout. It fixes CPU
submesh metadata and bounds before acquiring Mesh GPU buffers. Source geometry is retained while
uploading. Explicit `GraphicsBuffer.SetData` chunks consume one shared, monotonic frame budget;
an incomplete mesh cannot acquire a rendering reader. No later CPU Mesh mutation is permitted.

Unity documents that CPU Mesh edits can recreate the GPU buffers, whereas
[GetVertexBuffer](https://docs.unity3d.com/6000.6/Documentation/ScriptReference/Mesh.GetVertexBuffer.html)
accesses their GPU copy directly. The test checks both GPU bytes and a real depth draw, so correct
CPU staging data alone cannot pass.

With a **28-byte/frame** budget, a triangle's 72-byte vertex stream and 12-byte index stream
complete over four frames. The final GPU positions/normals/indices match the source; a draw reads
the streamed triangle and produces the expected linear depth of 3. Readers remain valid through
World destruction. Resource accounting includes owned mesh CPU/GPU stream estimates and staging
payload; failed reservations preserve source ownership and budget totals.

The budget counts explicit geometry payload writes. Unity allocations, initial buffer creation/
zeroing, driver traffic, CPU mesh copying and API wall-clock cost are not hard bandwidth/time
guarantees. The production prepass still needs the revision-aware cache; this isolated transport
proof does not yet establish bounded uploads for the whole renderer or its per-instance data.

## Automatic retirement

Cancelled submissions and retired meshes enter an owner independent of the originating World.
Runtime update and a separate Editor-only adapter poll fences without waiting during normal
frames. Final release joins CPU readers and preserves queued GPU consumers.

At Play exit, Editor reload or application shutdown, a separate 4-byte sentinel readback drains
queued GPU work. It contains no visibility data and is absent from frame submission. The lifecycle
drain also destroys only the package's typed, marked hidden helper objects. Runtime has no
UnityEditor dependency, and user scenes are never saved or replaced by this mechanism.

## Results

- Streaming RED `4015961101004a84b285862f0a1253f8`: 0/4. Initial GREEN
  `ff2821762d6245d599e8c3645a9b7e45`: 4/4. Extended actual draw proof
  `5122e5d9a3784382aae56c07cbe1e9c9`: 4/4.
- Automatic retirement RED `d44ccdd95d8d4ff49484e4b42a87b762`: 0/1; integrated GPU GREEN
  `0904d3489a874e79a2a1f0de11d1db7b`: 12/12.
- Off-thread disposal RED `136f5591a3e94601aee3965b9dcbd28b`: 0/1. Corrected detachment ordering
  preserves the main-thread lease after rejecting the worker call.
- Hidden helper cleanup RED `fbab061fe29447a0ad84f8cad7ec96f3`: 0/1, three retained helpers.
  Explicit lifecycle cleanup removes them, including across domain reloads.
- Final coupled PlayMode **`b94d4ab952844bf8be42b58388cc4f0c`: 40/40**, zero failed/skipped
  (14 GPU depth/upload/lifetime checks and 26 previous rendering/ownership/transport regressions).
- Coupled EditMode with the new snapshot geometry-retain helper:
  **`e125c52c52df437486fa828571a7ad5c`: 115/115**, zero failed/skipped. Subsequent changes were
  GPU lifecycle fixes covered by the final PlayMode run.
- Final live inventory: zero owned registry meshes, streamed meshes and retirement helpers;
  original `Assets/Scenes/SampleScene.unity` remains active and clean. Documentation validator:
  94 Markdown files before this evidence document was added.

No Player, performance, full geometry-cache/presentation, dynamic city or Git-consumer result
is claimed here. Those remain in the [implementation plan](../Superpowers/Plans/2026-11-06-dynamic-geometric-occlusion-plan.md).
