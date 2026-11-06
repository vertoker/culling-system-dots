# Baking workflow

Create CullingVolumeAuthoring with groups/renderers in a SubScene, create a Bake Settings asset, and open **Tools → Culling System DOTS → Bake**. Select the volume, settings and output asset.

Auto and Gpu start the Unity compute shader and can restart the entire attempt on CPU Jobs/Burst. Cpu never starts a GPU session. GPU startup, dispatch, readback or completion faults discard all GPU results and start CPU sampling from the beginning. The window preserves the effective backend and recovery reason. Cancellation does not trigger recovery.

The window shows progress and offers Cancel while scheduled work advances through Editor updates. Snapshot collection, BVH construction and final validation/publication still contain synchronous main-thread phases; the indicator cannot repaint during those phases. No background-thread Unity object access is attempted.

Publication validates current inputs and atomically replaces only the selected output asset. Cancellation, errors or invalidation preserve the previous asset and GUID. The workflow never saves or discards user scenes.

Ray resolution controls sampling density and cost. Thin geometry can be missed by discrete rays; this is a sampling limitation, not a conservative rasterization guarantee. Capture static occluders at their intended pose. Rebuild after changing geometry, settings or group mapping.
