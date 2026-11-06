# Changelog

## Unreleased

### Added

- Experimental dynamic geometric occlusion: versioned runtime geometry, independent ECS capture,
  bounded GPU uploads/frame resources, conservative current-view depth/Hi-Z and explicit indirect
  rendering with independent full-caster shadows. Optional URP 17.6 integration restores stock
  graphics for unsupported configuration. No Player performance improvement is assumed.
- Dynamic City sample with a separate ECS World, editable building pieces, indoor/street agents,
  first-person/tour controls, diagnostics and ordinary-rendering comparison.

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
