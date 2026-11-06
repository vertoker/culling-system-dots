# AGENTS.md - `Packages`

Read [the repository guide](../AGENTS.md) and
[package-boundary rules](../Docs/AgentRules/package-boundaries.md) first.

- This manifest and lockfile configure the development project. Shipped dependencies belong in
  `Assets/CullingSystemDOTS/package.json`; updating only this manifest can hide a consumer failure.
- Let UPM resolve ordinary dependencies into its cache. Do not embed or patch Entities Graphics.
- Keep the package source under `Assets/CullingSystemDOTS`; do not duplicate it here as an embedded package.
- Coplay MCP is development tooling. Unity CLI/Pipeline remains disabled.
- Let Unity resolve dependency/lock changes and inspect their actual diff; do not hand-edit hashes
  or unrelated version upgrades to make a lockfile appear current.
