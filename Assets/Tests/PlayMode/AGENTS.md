# AGENTS.md - `Assets/Tests/PlayMode`

Read [the development Tests guide](../AGENTS.md) first.

- Use observed pixel/shadow output with independently expressed expected scenes. Internal adapter
  state alone cannot prove correct camera, material, LOD, stack or shadow presentation.
- Keep explicit coverage for simulation remaining alive while rendering is hidden, camera order,
  full Entity versions under streaming, and URP base/overlay restoration.
- Record graphics/API prerequisites and meaningful skip reasons. GPU/API unavailability does not
  establish equivalent rendering behavior.
- Restore owned render targets, cameras, Worlds and scenes even after assertions fail. Do not save
  or discard the user's live scene or mutate source while the runner owns the Editor.
