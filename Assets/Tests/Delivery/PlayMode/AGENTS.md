# AGENTS.md - `Assets/Tests/Delivery/PlayMode`

Read [the Delivery tests guide](../AGENTS.md) first.

- Validate the sample's public interaction and normal/inverted/restored visibility lifecycle.
- Keep scene/sample discovery valid in the disposable Git consumer as well as the root project.
- Restore owned cameras, Worlds and scene state in teardown. A missing sample or zero discovered
  tests is a validation failure, not a successful smoke test.
