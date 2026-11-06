# AGENTS.md - `Tests/Dynamic`

Read [the Tests guide](../AGENTS.md) first. These tests travel with the package and exercise the
independent runtime geometric algorithm. Use synthetic meshes and owned Worlds; no root scenes,
Editor resource paths or external commercial fixtures.

- [EditMode](EditMode/AGENTS.md) owns numeric, registration and snapshot contracts.
- [Ecs](Ecs/AGENTS.md) owns live World capture and graphics claims.
- [PlayMode](PlayMode/AGENTS.md) owns real GPU depth and visibility contracts.
- [Urp](Urp/AGENTS.md) owns live ECS graphics transfer and pipeline fallback tests.

Do not infer GPU rendering, shadow or Player acceptance from numeric registry tests.
