# Baking workflow

Create CullingVolumeAuthoring with groups/renderers in a SubScene, create a Bake Settings asset, and open **Tools → Culling System DOTS → Bake**. Select the volume, settings and output asset.

Auto and Gpu start the Unity compute shader and can restart the entire attempt on CPU Jobs/Burst. Cpu never starts a GPU session. GPU startup, dispatch, readback or completion faults discard all GPU results and start CPU sampling from the beginning. The window preserves the effective backend and recovery reason. Cancellation does not trigger recovery.

The window shows the current phase and offers Cancel while capture, tracing and publication
advance through Editor updates. Scene, mesh and sampling capture yield between chunks on the
main thread. Numeric BVH construction, merging and cell encoding run in owned background tasks.
Cancel and teardown join those tasks before freeing borrowed data; CPU mode still creates no GPU work.

Progress is phase-based: publication starts its own validation/encoding progress after tracing.
Individual Unity calls (such as reading/baking one mesh), native array copies, GPU uploads and
the final atomic asset write/import remain indivisible and can delay a repaint. The update budget
is a cooperative target, not a hard frame-time guarantee. Keep scene inputs static during capture;
normal Editor hierarchy/Undo edits during publication invalidate it rather than publishing stale data.

For custom Editor automation, `BakeController.StartIncremental()` starts without capturing the scene
inline; call `Tick()` until Ready, then `BeginPublish(path)` and continue ticking until Published.
Cancel is available during preparation and publication. Settings are cloned for the attempt;
publication recaptures current inputs and rejects changed fingerprints. Existing `Start`, `Complete`
and `Publish` helpers remain synchronous. An output path/IO failure retains Ready results for retry.

Immediately before the atomic write, a final synchronous fingerprint check closes the gap left by
background encoding, including edits made by scripts without Undo events. It rereads source data
but reuses derived instance bounds for comparison; only an identical source fingerprint is accepted.
This final guard and asset import can still cause a short main-thread stall. It never supplies reused
bounds to tracing, and does not treat Editor change notifications alone as proof that inputs match.

Publication validates current inputs and atomically replaces only the selected output asset. Cancellation, errors or invalidation preserve the previous asset and GUID. The workflow never saves or discards user scenes.

Ray resolution controls sampling density and cost. Thin geometry can be missed by discrete rays; this is a sampling limitation, not a conservative rasterization guarantee. Capture static occluders at their intended pose. Rebuild after changing geometry, settings or group mapping.
