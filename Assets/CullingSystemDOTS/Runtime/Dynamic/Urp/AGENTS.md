# AGENTS.md - `Runtime/Dynamic/Urp`

Read [the Dynamic guide](../AGENTS.md) first. This optional URP assembly owns the dynamic
presentation bridge. Transfer only ready current packet handles before Entities Graphics updates.
Restore stock graphics before suspending or disposing the GPU owner. Validate actual URP settings,
camera stacks and late publication changes; raw Camera flags are insufficient. Never disable ECS
simulation. Shader resources are direct serialized references, independent of the install path.
