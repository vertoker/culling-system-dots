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
- Keep the final source fingerprint check adjacent to the atomic write, without another update or
  worker gap. Undo/hierarchy notifications do not catch all scripted edits. Validation-only snapshots
  may reuse derived bounds, but must reread/hash all source data and must never reach a tracer.
- Keep progress and cancellation meaningful. The window uses incremental capture/publication and
  owned background numeric work. Join workers before disposing borrowed data. Individual Unity
  APIs, copies/uploads and final atomic import can still delay repaint; do not promise a hard limit.
- Backend and postprocess changes must preserve group mapping, transparent/opaque semantics and
  deterministic hit ordering defined by this package. External output is not automatically correct.

See [BakingSnapshots](../../Documentation~/BakingSnapshots.md) and
[BakingWorkflow](../../Documentation~/BakingWorkflow.md).
