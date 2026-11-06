# AGENTS.md - `Runtime/Dynamic/Gpu`

Read [the Dynamic guide](../AGENTS.md) first. This assembly owns current-view positive linear
eye-depth resources and GPU tests, independently of URP and the legacy evaluator.

Depth uses the complete single-sample reference grid. Clear/uncovered samples remain far through
maximum reduction, including odd edges. Shader results may hide only with complete current inputs.
Resources and snapshot leases must survive every queued reader; low-level owners release only
after their submission fence. CPU preparation is not GPU readiness.

Stream mesh payload through the owned GPU buffers after fixing all CPU Mesh metadata. Later CPU
Mesh mutation can recreate the GPU buffers and invalidate streamed contents. Share one upload
budget across writers/views for a frame. Partially uploaded geometry cannot be published.
The retirement queue outlives Worlds; normal polling never waits. A sentinel readback is reserved
for lifecycle shutdown/reload, outside frame submission and performance measurements.

- [Shaders](Shaders/AGENTS.md) owns coverage and the GPU numeric contract.
