# AGENTS.md - `Assets/DeliveryTools/Editor`

Read [the DeliveryTools guide](../AGENTS.md) first.

- Generate only into a new, explicitly owned destination. Preserve the builder's overwrite refusal
  and host-state protection; never overwrite existing user/sample assets as an implicit cleanup.
- Use owned additive scenes and restore the host setup without saving/discarding user scenes.
- Keep fixtures independently authored and synthetic. No commercial assets or private source tree
  may become a prerequisite.
- Builder changes need its regression tests plus sample consistency checks when output changes.
  Do not rerun generation for unrelated edits.
