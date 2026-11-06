# AGENTS.md - `Runtime/Dynamic`

Read [the Runtime guide](../AGENTS.md) first.

This is the independent geometric runtime occlusion algorithm. It must not depend on the legacy
ordered evaluator, baked assets or URP. The main-thread registry owns geometry copies, generations
and immutable snapshot leases. Jobs read native snapshots; they never access Mesh/Material objects.
Register their dependencies on the lease before disposal. Readers keep retired geometry alive.

Uncertain or stale visibility keeps ordinary rendering. Geometry mutation requires explicit
replacement even when the source Mesh reference is unchanged. Render ownership and GPU retirement
belong to the presentation layer; registry removal cannot free a queued renderer's leased data.

Block preparation is bounded/coalesced CPU work. `Prepared` does not assert GPU upload completion;
the GPU owner validates its own resource revisions. Geometry, instance and occluder-block updates
have separate keys. Moving an occludee must not dirty an occluder block. Rejected pose/geometry
capture withdraws its old occluder immediately; unknown state must not preserve a hidden result.

- [Ecs](Ecs/AGENTS.md) owns full-Entity capture and explicit graphics registration.
