# Changelog

## Unreleased

### Removed

- Retired the experimental algorithm-3 GPU geometric occlusion renderer, its ECS/URP integration
  and Dynamic City sample after the full-population Player comparison failed the performance gate.
  The dynamic API and sample are no longer shipped. Existing imported Dynamic City copies must be
  removed before updating; the static baker, runtime evaluator and Basic sample remain available.

### Changed

- Bake window uses incremental scene/sample/mesh capture, background numeric BVH construction
  and merge/encoding, staged input validation and publication, with cancellation and Ready-state
  retry after output IO errors. Individual Unity APIs and atomic asset import remain synchronous.
- Repository instruction checks and portable Python tests run in a dependency-free CI workflow.

## 0.1.0

- Unity-compiled GPU compute visibility baking with a CPU Jobs/Burst backend and complete
  CPU restart after supported GPU failures. CPU mode does not start GPU work.
- Bake settings and Editor window with progress, cancellation, input fingerprint validation
  and publication that preserves the previous valid asset on failure or invalidation.
- ECS volume/group authoring, immutable bake data and runtime evaluation with stock
  Entities Graphics, ordered cameras and graphics-only shadow companions.
- Optional URP camera-order integration and an importable two-room sample with a CPU bake,
  SubScene, LOD/material bindings, camera motion and inversion controls.
- Self-contained setup, baking, rendering and compatibility documentation; MIT licensing
  and package links for source, documentation and changes.

The [compatibility scope](Documentation~/Compatibility.md) and
[baking limitations](Documentation~/BakingWorkflow.md) apply to this version.
