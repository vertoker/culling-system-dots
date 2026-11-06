# AGENTS.md - `Runtime/Dynamic/Gpu`

Read [the Dynamic guide](../AGENTS.md) first. This assembly owns current-view positive linear
eye-depth resources and GPU tests, independently of URP and the legacy evaluator.

Depth uses the complete single-sample reference grid. Clear/uncovered samples remain far through
maximum reduction, including odd edges. Shader results may hide only with complete current inputs.
Resources and snapshot leases must survive every queued reader; low-level owners release only
after their submission fence. CPU preparation is not GPU readiness.

- [Shaders](Shaders/AGENTS.md) owns coverage and the GPU numeric contract.
