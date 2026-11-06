# AGENTS.md - `DynamicCity/Runtime`

Read [the sample guide](../AGENTS.md) first. This is demonstration code, not the culling algorithm.
Construction uses stable piece IDs and public registry/binding operations. Agent simulation must
continue with rendering disabled. Serialize shader/material references for Player stripping.
Keep camera controls independent of a particular input package. Never alter host render settings.
