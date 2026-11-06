# Culling System DOTS

Visibility baking and runtime culling for Unity Entities Graphics. Unity compiles the
C#/HLSL baker; GPU compute is the primary path, with a CPU Jobs/Burst fallback.
Runtime integration uses unmodified Entities Graphics from UPM and changes rendering
visibility without disabling entities, physics or gameplay logic.

## Install and try the sample

Use the Unity 6000.6.8f1 baseline described in [Compatibility](Documentation~/Compatibility.md).
In Package Manager, choose **Install package from Git URL** and enter:

```text
https://github.com/vertoker/culling-system-dots.git?path=Assets/CullingSystemDOTS
```

Append `#<commit-or-tag>` to pin a revision. Git must be available to Unity; required
package dependencies are declared in [package.json](package.json). For automatic
URP base/overlay camera ordering, install a compatible URP 17.6.x version.

Import **Two rooms with LOD and materials** from this package's Samples tab. Open
`Basic.unity` and enter Play mode to try the moving camera and inversion controls.
The sample includes a CPU bake, a SubScene and LOD/material bindings. See the
[sample guide](Samples~/Basic/README.md) and [setup guide](Documentation~/Setup.md).

## Bake your scene

1. Add `CullingVolumeAuthoring` and `CullingGroupAuthoring` components in a SubScene.
   Assign each group's volume, stable group index and renderers as described in
   [Authoring](Documentation~/Authoring.md).
2. Create a **Culling System DOTS > Bake Settings** asset. Open
   **Tools > Culling System DOTS > Bake** and select the volume, settings and output asset.
3. Choose `Auto`, `Gpu` or `Cpu`, then bake. `Auto` and `Gpu` attempt compute baking;
   supported GPU failures discard that attempt and restart the entire bake on CPU.
   `Cpu` never starts GPU work. The window reports the effective backend and recovery reason.
4. Use **Validate saved bake** after changing inputs; rebake changed geometry,
   settings or group mappings. Register runtime cameras as described in
   [Authoring](Documentation~/Authoring.md).

Cancellation does not trigger CPU recovery. Cancellation, failure or changed inputs
preserve the previous valid output; the baker never saves or discards your scenes.
The [baking workflow](Documentation~/BakingWorkflow.md) describes publication and cancellation.

## Scope and limits

- Discrete rays can miss thin geometry. Bake static occluders at their intended pose;
  this is not a conservative rasterization or external-baker equivalence guarantee.
- Capture and publication advance in steps, with background numeric BVH/encoding work and
  phase-based progress/cancellation. Individual Unity APIs, copies/uploads and atomic output
  import can still delay repaint. Payload budgets do not cap whole-process memory.
- The documented baseline is Windows/DX12. Other GPU APIs/platforms, XR, reflection
  cameras, custom SRPs, animated occluders and deformed shadow companions need separate
  validation. Editor results do not establish Player support.
- Per-camera rendering synchronization may stall the main thread. No measured speedup
  is promised; see [rendering integration](Documentation~/RenderingIntegration.md).

See [Compatibility](Documentation~/Compatibility.md) for supported scope,
[Runtime evaluator](Documentation~/RuntimeEvaluator.md) for evaluation contracts and
[Migration](Documentation~/Migration.md) for transition guidance.

## License and changes

[MIT License](LICENSE.md), copyright 2026 vertoker. See [CHANGELOG](CHANGELOG.md).
