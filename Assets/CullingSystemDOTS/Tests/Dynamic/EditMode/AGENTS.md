# AGENTS.md - `Tests/Dynamic/EditMode`

Read [the Dynamic tests guide](../AGENTS.md) first. Test actual handle reuse, copy/lease ownership,
budget failures and stale-result rejection. Register native job readers on the snapshot lease
and dispose every owned mesh/allocation, including assertion-failure paths.
