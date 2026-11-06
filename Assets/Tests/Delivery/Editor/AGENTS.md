# AGENTS.md - `Assets/Tests/Delivery/Editor`

Read [the Delivery tests guide](../AGENTS.md) first.

- Test new-destination generation, refusal of overwrite and preservation of existing host state.
- Use unique owned temporary asset folders and failure-safe teardown. Never reuse or delete the
  maintained sample or a user's open scene as test scratch space.
- This is an EditMode assembly; do not move its NUnit references into production tools.
