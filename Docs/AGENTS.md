# AGENTS.md - `Docs`

Read [the repository guide](../AGENTS.md) first. This folder contains maintainer workflow and
evidence; it is not part of the installable UPM package.

- Keep user-facing package instructions in `Assets/CullingSystemDOTS/Documentation~` so they ship.
- `AgentRules` owns operation-specific guidance, `Superpowers/Plans` owns existing implementation
  records, and `Validation` owns scoped test/build/Player evidence.
- Preserve historical outcomes and their tested revision, environment and limitations. Correct
  stale navigation; do not silently update old results to imply newer acceptance.
- Use current code and package manifests as factual sources. Link the authoritative owner rather
  than repeating numeric constants, dependency versions or whole subsystem descriptions.
- Existing plans retain their paths. New plans may use the same folder without requiring the
  Superpowers plugin. State status explicitly inside the document.

See [agent workflow](AGENT-WORKFLOW.md), [testing](TESTING.md), and
[Markdown guidance](AgentRules/markdown.md).
