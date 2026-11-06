# AGENTS.md - `Assets/Tests/DynamicPrototype`

Read [the development test guide](../AGENTS.md) first.

This is the root-only algorithm-3 integration investigation, not a shipped renderer or public API.
Keep the expected failing capability assertion explicit and separate from regression tests. GPU
visibility uses a deliberately restricted orthographic wall fixture to isolate readback/presentation;
it does not implement or validate general Hi-Z. Never promote its timings to product performance.

The probe owns its entities, cameras, requests and buffers. Restore graphics flags after each view,
join outstanding readbacks before freeing buffers, and leave the user's scene/settings intact.
Record limitations and observed results in
[the integration report](../../../Docs/Validation/DynamicOcclusion-Integration.md).

The separate indirect transport proof uses public GPU-written draw arguments, independent
camera buffers and all-caster shadow arguments. Its
[evidence](../../../Docs/Validation/DynamicOcclusion-Indirect.md) establishes submission ordering,
not general occlusion correctness. Fence retirement must cover the later color/shadow consumers.
