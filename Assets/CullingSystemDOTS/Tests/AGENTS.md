# AGENTS.md - `Tests`

Read [the package guide](../AGENTS.md) first.

- [Core](Core/AGENTS.md) tests data/codec/evaluation contracts.
- [Editor](Editor/AGENTS.md) tests capture, authoring, backends, lifetime and output.
- [Runtime](Runtime/AGENTS.md) tests World, receiver and presentation ownership in EditMode.

These tests travel with the package. Keep fixtures independent of root development paths and
commercial assets. Keep NUnit/test-runner references isolated in test assemblies; do not add
game-specific metadata conventions. Confirm runner mode and discovery from the actual asmdef.

Add regressions for observable failures, not assertions that duplicate implementation details.
Live rendered-pixel tests live in the source checkout's development harness and are separate
evidence from these package tests.
