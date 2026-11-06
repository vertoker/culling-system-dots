# AGENTS.md - `Assets/Tests`

Read [the Assets guide](../AGENTS.md) and [repository testing](../../Docs/TESTING.md) first.

- [PlayMode](PlayMode/AGENTS.md) owns real rendering integration tests.
- [Delivery](Delivery/AGENTS.md) owns fixture generation and sample behavior tests.
- [DynamicPrototype](DynamicPrototype/AGENTS.md) owns the root-only runtime occlusion integration probe.
- [DynamicCity](DynamicCity/AGENTS.md) owns dynamic construction scenario and image checks.
- Fixtures holds independently authored synthetic materials, prefabs and streaming content.

These are development-harness tests, separate from the package-contained suites. Keep production
code free of dependencies on them. Preserve asset references and failure-safe cleanup. Empty old
test directories or unrelated user-deleted templates do not need restoration or new boilerplate.
