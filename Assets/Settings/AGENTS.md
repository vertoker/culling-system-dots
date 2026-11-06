# AGENTS.md - `Assets/Settings`

Read [the Assets guide](../AGENTS.md) and
[Unity workflow](../../Docs/AgentRules/unity-editor.md) first.

These rendering settings belong to the development harness and its explicit test consumer.
Do not treat them as implicit package dependencies or broaden compatibility based on their names.
Change only task-relevant serialized values through their owning workflow, preserve GUIDs, and
verify the actual pipeline/settings used by any new rendering evidence.
