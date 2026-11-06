# AGENTS.md - `Runtime/Rendering`

Read [the Runtime guide](../AGENTS.md) first.

- Use public stock Entities Graphics APIs. Complete tracked readers before changing enableable
  `MaterialMeshInfo` visibility and before restoring the enclosing/baseline camera scope.
- Preserve ordered immutable masks and explicit camera-order faults. Replaying operations cannot
  be replaced by a final union or one globally applied camera mask.
- Shadow companions are graphics-only. Original and companion presentation must remain mutually
  exclusive; do not duplicate simulation, physics or unrelated authoring behavior.
- Cover every material/LOD render entity registered by authoring. Changes to binding enumeration
  also affect `Editor/Authoring` and its additional-entity identity filtering.
- Test observed pixels/shadows as well as data structure state. Synchronization costs are a known
  tradeoff; do not promise zero allocations or speedup without measurements.

See [RenderingIntegration](../../Documentation~/RenderingIntegration.md).
