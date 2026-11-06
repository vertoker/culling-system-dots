# AGENTS.md - `Runtime/Ecs`

Read [the Runtime guide](../AGENTS.md) first. Authoring components have
[narrower guidance](Authoring/AGENTS.md).

- All caches, tickets, native state and receiver registrations belong to one World. Validate full
  Entity identity including version, not only its index, across streaming and replacement.
- Reject foreign, cancelled and stale publication tickets. Finish jobs before replacing or
  disposing state, including World teardown and unpublished outputs.
- Preserve registration inheritance, removal restoration and ordered last-operation semantics.
  Preparing a camera frame must not apply its final receiver state early.
- Keep simulation/Behaviour receivers separate from graphics visibility. Never disable an entire
  entity as a shortcut for rendering culling.
- Lifecycle changes need runtime tests and, when presentation is affected, root rendering tests
  in the development checkout.

See [RuntimeEvaluator](../../Documentation~/RuntimeEvaluator.md) for ownership and publication.
