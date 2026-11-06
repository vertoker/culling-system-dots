# AGENTS.md - `Tests/Editor`

Read [the Tests guide](../AGENTS.md) first.

- Use independently created synthetic geometry and owned temporary assets/scenes. Restore live
  Editor state and clean up only owned content; never save or discard the user's scene.
- Cover CPU without a GPU session, GPU faults/recovery from sample zero, cancellation, reader
  lifetime and atomic output preservation as observable backend contracts.
- GPU-unavailable skips must state why; they do not establish GPU acceptance.
- Resolve package resources in both source and Git-installed layouts. Root-only fixtures and
  developer settings must not become hidden test dependencies.
- For invalid geometry, assert rejection and preserved output, not merely the absence of a crash.
