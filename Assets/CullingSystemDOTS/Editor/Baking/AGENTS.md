# AGENTS.md - `Editor/Baking`

Read [the Editor guide](../AGENTS.md) first. Backend instructions are in
[Jobs](Jobs/AGENTS.md), [GPU](Gpu/AGENTS.md), and [shader guidance](Gpu/Shaders/AGENTS.md).

- Capture a validated immutable scene snapshot before scheduling. Keep Unity object access on the
  main thread and snapshot-reader ownership explicit. Capturing a skinned pose must not mutate
  the user's renderer or add helper objects to the scene.
- Validate finite numeric inputs, geometry indices/ranges, bounds and invertible affine transforms
  before backend work. Malformed geometry must not be accepted as an empty successful bake.
- Auto/Gpu faults discard the entire GPU attempt and restart CPU sampling from the beginning.
  Cpu must not create GPU work. Cancellation is terminal, not a fallback trigger.
- Publish only completed results that still match current inputs. Output replacement is atomic;
  cancellation/error/invalidation preserves prior asset bytes and GUID. Never save user scenes.
- Keep progress and cancellation meaningful. Scheduled phases advance on Editor updates, but
  synchronous capture/BVH/publication phases can block repaint; do not imply full responsiveness.
- Backend and postprocess changes must preserve group mapping, transparent/opaque semantics and
  deterministic hit ordering defined by this package. External output is not automatically correct.

See [BakingSnapshots](../../Documentation~/BakingSnapshots.md) and
[BakingWorkflow](../../Documentation~/BakingWorkflow.md).
