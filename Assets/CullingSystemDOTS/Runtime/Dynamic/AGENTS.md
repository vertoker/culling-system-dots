# AGENTS.md - `Runtime/Dynamic`

Read [the Runtime guide](../AGENTS.md) first.

This is the independent geometric runtime occlusion algorithm. It must not depend on the legacy
ordered evaluator, baked assets or URP. The main-thread registry owns geometry copies, generations
and immutable snapshot leases. Jobs read native snapshots; they never access Mesh/Material objects.
Register their dependencies on the lease before disposal. Readers keep retired geometry alive.

Uncertain or stale visibility keeps ordinary rendering. Geometry mutation requires explicit
replacement even when the source Mesh reference is unchanged. Render ownership and GPU retirement
belong to the presentation layer; registry removal cannot free a queued renderer's leased data.
