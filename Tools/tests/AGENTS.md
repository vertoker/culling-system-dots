# AGENTS.md - `Tools/tests`

Read [the Tools guide](../AGENTS.md) first.

- Tests use temporary repositories/directories and actual helper behavior, not mocks of Git output.
- Preserve coverage for Git subpath installation, portable resource lookup, explicit engine module
  dependencies and refusal of existing or outside destinations.
- Temporary fixtures must not launch Unity, fetch remote repositories, rewrite the real repository,
  or alter unrelated working-tree files. Clean up only their owned temporary directories.
