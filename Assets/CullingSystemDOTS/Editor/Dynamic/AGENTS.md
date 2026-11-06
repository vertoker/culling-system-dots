# AGENTS.md - `Editor/Dynamic`

Read [the Editor guide](../AGENTS.md) first. This adapter polls the runtime GPU retirement queue
outside Play Mode and drains it at Editor reload or Play exit. It must not create, save or replace
user scenes. Runtime owns resource/fence contracts and has no UnityEditor dependency.
The shutdown sentinel readback is a lifecycle barrier, never a visibility-consumption path.
