# Compatibility

Baseline: Unity 6000.6.8f1, Entities / Entities Graphics / Collections 6.6.0, Burst 2.0.0, Mathematics 1.4.0, URP 17.6.0, Windows and DX12. Entities Graphics is unmodified and installed through UPM.

Both bake backends contain C#/HLSL compiled by Unity. There are no prebuilt native baker binaries or external compiler/SDK requirements. CPU execution is a genuine fallback, with bounded work and explicit cancellation/failure.

The independent binary tracer defines its own deterministic coincident-hit order and closed triangle-edge rule. Stored cell encoding and group identifiers remain stable. No equivalence with an external baker is asserted.

GPU support requires compute shaders and asynchronous readback; Auto/Gpu can recover to CPU when unavailable. Other GPU APIs/platforms, XR, reflection cameras, custom SRPs, animated occluders and deformed shadow companions need separate validation. Native payload budgets are not total-memory caps. Stock per-camera integration synchronizes outstanding ECS readers and may cost CPU time.

Only fresh reports under the repository's Docs/Validation apply to this revision. A successful Editor test run does not establish Player/platform support by itself.

The independent experimental [dynamic geometric renderer](DynamicOcclusion.md) has a narrower
explicit rigid-opaque/unlit, URP 17.6, Windows DX12 scope. It owns indirect graphics for opted-in
bindings; other objects retain stock rendering. Its camera/material/lifetime rules and sample
setup are separate from the static-bake adapter. Dynamic Player acceptance and performance must
be assessed from their own evidence rather than inferred from bake/backend tests.
