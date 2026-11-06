# Dynamic occlusion current-geometry cache

Tested source **`e6b67fe`**, Unity 6000.6.8f1 Windows Editor/DX12, RTX 4060 Ti, 2026-11-06.
This integrates the [streaming transport](DynamicOcclusion-Upload.md) into the depth prepass.

The prepass now requires a ready GPU mesh lease matching the snapshot's geometry revision.
Its earlier direct CPU Mesh route is removed. Pending, removed or superseded geometry cannot
populate depth; previously queued readers retain their old immutable GPU mesh until retirement.
Fresh reader requests reject stale registry snapshots.

The cache shares the explicit upload budget across repeated calls in a frame, rotates upload
priority and limits new geometry allocation attempts with `MaxGeometryCreationsPerFrame`
(default 8). This cap bounds engine-call count, not each call's wall time or driver traffic.

- Cache RED `b4a1e71f11c84e4482e1e608b8f3ce6e`: 0/6; GREEN
  `3f501d50790d4cca8decb5a4264a051f`: 6/6. Covers replacement/removal, retained readers,
  foreign snapshots, shared byte budget and per-frame allocation cap.
- Integration RED `88f4805e4dda4804a7725ef02aec891f`: 1/2. CPU-ready but GPU-unuploaded
  geometry incorrectly drew depth 2.5 instead of far 100. The strict GPU lease gate fixes this;
  the stale-mesh test also remains covered after switching to streamed buffers.
- GPU integration GREEN `4889ad53a34d4d5f88b33be5e19e8894`: 22/22.
- Coupled EditMode `f8398daada8a4511ab753017c7fbd752`: **115/115**.
- Coupled PlayMode `92a76300da5b4202a00557d2d103daaf`: **48/48** (22 GPU/cache checks and
  26 existing rendering/ownership/transport checks). Zero failed/skipped in both final runs.
- Documentation validation: 95 files before adding this evidence file.

Task 3 remains open for per-instance payload, reusable frame/view resources and view capability
gates. Indirect color/shadows, ownership transfer, the demo, full-frame performance and installed
consumer/Player acceptance are not established by this cache result.
