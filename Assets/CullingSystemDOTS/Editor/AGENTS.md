# AGENTS.md - `Editor`

Read [the package guide](../AGENTS.md) first.

- [Authoring](Authoring/AGENTS.md) owns ECS conversion and compressed-data import.
- [Baking](Baking/AGENTS.md) owns capture, orchestration, validation and output, with separate
  [Jobs](Baking/Jobs/AGENTS.md) and [GPU](Baking/Gpu/AGENTS.md) backend contracts.

Keep Editor-only APIs in this assembly boundary. Snapshot Unity objects on the main thread before
worker execution. Do not make installed-package behavior depend on root developer tools, settings
or absolute asset paths. Preserve user scenes and prior valid output on failure/cancellation.
