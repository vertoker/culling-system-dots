# AGENTS.md - `Runtime/Dynamic/Gpu/Shaders`

Read [the GPU guide](../AGENTS.md) first. Linear positive eye depth is independent of hardware
reversed Z. Maximum reduction preserves uncovered samples; invalid inputs become far depth.
Bounds tests must cover every overlapped hierarchy cell and bias toward visibility.
Keep CPU/reference and real GPU tests coupled when changing layouts, projection or reduction.
