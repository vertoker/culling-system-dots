# AGENTS.md - `Tests/Dynamic/PlayMode`

Read [the Dynamic tests guide](../AGENTS.md) first. Verify real GPU coverage and depth against
the exhaustive CPU reference. Readback is assertion-only, outside performance measurements.
Complete queued GPU readers before releasing resources or geometry leases, including failures.
Use owned synthetic objects and resources; never save or replace the host scene.
