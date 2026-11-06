# AGENTS.md - `ProjectSettings`

Read [the repository guide](../AGENTS.md) and
[Unity workflow](../Docs/AgentRules/unity-editor.md) first.

- These settings belong to the development harness, not the installed package. A consumer must
  not need undocumented copies of project settings for basic package compilation.
- Change only settings required by the task and explain their effect. Do not upgrade Unity or
  broad rendering/player settings as part of an unrelated fix.
- Use the live Editor's owning APIs when it holds the settings; verify serialized values instead
  of relying on comments or guessed enum ordinals.
- Keep the recorded validation baseline distinct from newly proposed platform support.
