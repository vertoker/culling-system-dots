# AGENTS.md - `Tests/Runtime`

Read [the Tests guide](../AGENTS.md) first. Despite the folder name, this assembly is configured
for EditMode; inspect its asmdef before selecting a runner mode.

- Create and dispose owned Worlds, drivers and native allocations in failure-safe teardown.
- Exercise cross-World/foreign tickets, cancellation, streaming/reused Entity versions and
  registration inheritance/restoration. A final visibility boolean alone misses ordered behavior.
- State-based adapter tests are not substitutes for actual camera/shadow pixel tests in the
  development harness.
