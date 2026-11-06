# AGENTS.md - `Tests/Core`

Read [the Tests guide](../AGENTS.md) first.

- Exercise actual encoded data, native snapshots and ordered output. Preserve tests distinguishing
  queued/applied state, invalid input, stale publication and independent volume/group identity.
- Compare against explicit expected behavior or an independently expressed oracle; do not compute
  the expected result by calling the same implementation path.
- Complete job dependencies and dispose every owned native allocation even when an assertion fails.
- Codec/grid changes need both writer/reader compatibility and malformed-input coverage.
