# AGENTS.md - `Editor/Authoring`

Read [the Editor guide](../AGENTS.md) first.

- Discover extra renderer entities through public `AdditionalEntitiesBakingData`, filtering by
  authoring component identity and `MaterialMeshInfo`. Primary-only registration misses LODs and
  material entities; unfiltered enumeration can bind another author's entities.
- Do not depend on internal renderer baking component types or patched Entities Graphics.
- Coordinate serialized authoring changes with `Runtime/Ecs/Authoring` and conversion tests.
- The compressed-data importer supports this package's data contract; do not add reflection-based
  commercial scene importers or make paid assets necessary for test discovery.

See [Authoring](../../Documentation~/Authoring.md) and
[RenderingIntegration](../../Documentation~/RenderingIntegration.md).
