# AGENTS.md - `Runtime/Core`

Read [the Runtime guide](../AGENTS.md) first.

- Snapshots and borrowed blobs remain immutable and alive until every dependent job completes.
  Previous and output state have distinct ownership; copying a native-container struct is aliasing.
- Preserve ordered pass replay, queued versus applied state and dirty/invalidation semantics.
  A boolean union cannot replace the evaluator's sequence of operations.
- Encoded-cell failure must reject publication and retain the last accepted state. Validate
  shapes/ranges before scheduling; preserve error coordinates and dependency failure propagation.
- Coordinate codec/grid changes with Editor output and `Tests/Core`. Group identity and generation
  must remain stable across reorder/removal and distinguish independent volume instances.

The detailed contract is [RuntimeEvaluator](../../Documentation~/RuntimeEvaluator.md).
