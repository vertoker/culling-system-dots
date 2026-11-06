# AGENTS.md - `Runtime`

Read [the package guide](../AGENTS.md) first.

- [Core](Core/AGENTS.md) owns native snapshots and ordered evaluation, independently of rendering.
- [Ecs](Ecs/AGENTS.md) owns World/registry lifetimes and
  [authoring components](Ecs/Authoring/AGENTS.md).
- [Rendering](Rendering/AGENTS.md) owns graphics presentation and companions.
- [Urp](Urp/AGENTS.md) owns optional pipeline-specific camera order.

Keep these assembly boundaries intact. Runtime must compile without Editor APIs or developer
tooling. Serialized/data and ownership changes must reconcile the evaluator, authoring and tests.
