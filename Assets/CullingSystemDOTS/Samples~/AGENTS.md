# AGENTS.md - `Samples~`

Read [the package guide](../AGENTS.md) first.

- [Basic](Basic/AGENTS.md) is the importable package sample declared in `package.json`.
- Keep it self-contained after import into an arbitrary consumer folder. Do not depend on root
  development scenes, fixture generators or tools.
- Preserve sample asset GUIDs and coordinate behavior/assets with the source checkout's
  `Assets/DeliverySample` mirror. Never import both identical-GUID copies into the same asset tree.
- A sample's root instructions must survive copying without requiring these parent instructions.
