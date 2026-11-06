# AGENTS.md - `Editor/Baking/Gpu/Shaders`

Read [the GPU guide](../AGENTS.md) first.

- Unity compiles `BvhBake.compute`; do not add an external shader compiler or native DLL build.
- Keep buffer field order/alignment, kernel names and parameters in lockstep with
  `ComputeBakeBackend`. Consult the actual upload structs before editing HLSL layouts.
- Bound odd-sized dispatches and fixed traversal stacks. Explicitly signal faults through the
  backend's error channel; an overflow must not silently become a visibility result.
- Keep hit ordering, edges, sidedness, near/far and transparency semantics aligned with the
  independent CPU traversal. Do not assume GPU bitwise equality across devices.
- Preserve the shader's existing `.meta` GUID; it is used to resolve the installed resource.
