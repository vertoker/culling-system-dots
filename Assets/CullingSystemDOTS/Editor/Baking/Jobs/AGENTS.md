# AGENTS.md - `Editor/Baking/Jobs`

Read [the Baking guide](../AGENTS.md) first.

- This is the genuine CPU-only fallback. Do not create compute resources or require GPU readback.
- Work items own disjoint visibility storage; reduction follows their handles. Avoid unsynchronized
  shared bitset writes and retain checked stack bounds even when Burst safety checks are off.
- Snapshot readers, all traversal/reduction jobs and disposal must be joined on success, cancel,
  failure and teardown. A session fault invalidates all partial results.
- Preserve the independent binary BVH and direct triangle contract. Stable primitive tie order,
  edge rules and transparent groups ahead of the closest opaque hit must agree with GPU behavior.
- Coordinate native layout changes with GPU upload packing and `BvhBake.compute`, even if a
  CPU-only test passes. Payload budgets do not include all managed or driver allocations.

See [JobsBakeBackend](../../../Documentation~/JobsBakeBackend.md).
