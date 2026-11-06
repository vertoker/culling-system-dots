# Dynamic City sample: agent guide

This folder may be the source checkout's development mirror or an imported package sample.
Optional source context: [the containing guide](../AGENTS.md).
Follow the host project's instructions. The package copy is `Samples~/DynamicCity`; keep its
content and GUIDs synchronized with this mirror in the source checkout. Import only one copy.

- [Runtime](Runtime/AGENTS.md) owns the self-contained construction, agents, camera and UI.
- Use only public package APIs and serialized resources. Preserve unrelated scenes and settings.
- The sample owns its ECS World and generated objects; dispose them without touching other Worlds.
- [README](README.md) describes setup and controls. Rendering comparisons freeze the same state.
