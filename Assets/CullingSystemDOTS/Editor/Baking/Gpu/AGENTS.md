# AGENTS.md - `Editor/Baking/Gpu`

Read [the Baking guide](../AGENTS.md) and [shader instructions](Shaders/AGENTS.md) before changing
the CPU/GPU interface.

- C# upload structs, strides, shader field layouts and traversal semantics form one contract.
  Coordinate every layout change with Jobs/native data and the compute shader.
- Clone mutable shader state per session. Guard dispatch bounds and traversal stack overflow;
  carry GPU faults through readback instead of publishing partial visibility.
- Copy async readback data while its view is valid. Generation tokens prevent stale callbacks
  from completing a newer session. Drain outstanding requests before freeing their buffers.
- A startup, dispatch, readback or completion fault must reach the resilient controller, which
  discards GPU results before CPU restart. Do not merge results from two attempts.
- Preserve stable shader resource identity and installation-independent lookup.

Use backend/lifetime tests plus an actual supported GPU run for changed compute behavior.
