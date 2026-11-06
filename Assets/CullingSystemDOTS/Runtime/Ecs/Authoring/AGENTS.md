# AGENTS.md - `Runtime/Ecs/Authoring`

Read [the Ecs guide](../AGENTS.md) first.

- These are consumer-facing authoring components and sampling-provider contracts. Keep them in
  Runtime without UnityEditor dependencies; conversion belongs in `Editor/Authoring`.
- Serialized field/default changes can invalidate existing scenes or baked data. Reconcile the
  converter, snapshot extraction, tests and [Authoring guide](../../../Documentation~/Authoring.md).
- Sampling providers must yield a stable, validated snapshot; never retain mutable scene objects
  as job inputs or invent group identity from traversal order.
