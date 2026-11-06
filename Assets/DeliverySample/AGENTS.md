# AGENTS.md - `Assets/DeliverySample`

Read [the Assets guide](../AGENTS.md) first.

- This is the root development mirror of `Assets/CullingSystemDOTS/Samples~/Basic`, not another
  production package. Reconcile sample code and asset changes between the two intentionally.
- [Runtime](Runtime/AGENTS.md) owns sample interaction. Content holds the synthetic demonstration
  scenes, bake and materials. Preserve GUIDs and SubScene/bake references.
- Never activate another imported copy with the same GUIDs inside this development project.
- Regeneration is an explicit fixture operation, not part of routine code or documentation checks.
