# Dynamic City

Open `DynamicCity.unity` with URP 17.6 and enter Play mode. The scene builds a 128 by 128 city
from colored cubes: sixteen two-storey buildings, separate walls/floors/doors, a rotated fixture
and 512 ECS agents following indoor/street routes. The sample owns a separate ECS World.

Use `Content/DynamicCityPipeline.asset` as the project's render pipeline and for the active
quality level if your current pipeline is incompatible. The sample does not change those settings
automatically. Its serialized resources and shader/material assets travel with the sample.
The accepted experimental GPU path requires Windows DX12, URP 17.6, render scale 1 and compatible
single-sample cameras. Unsupported setups keep ordinary rendering and show the reason.

On-screen controls toggle dynamic occlusion, seeded edits, agent pause and the guided tour.
With the tour disabled, use WASD to move, Shift to accelerate, Space/Ctrl to move vertically and
hold the right mouse button to look. T toggles the tour. These controls use IMGUI events and
require no particular input package. Aim the center of the view at a building piece, then use
Remove/Restore. Doors and every building piece can be restored together.

The two render paths use the same rigid geometry and linear palette colors. The ordinary sample
shader supports ECS transforms and casts shadows; standard URP Unlit does not have a shadow pass.
Camera-hidden objects retain full shadow submissions in the indirect path. Both paths leave
agents and construction running. Pause agents, disable automatic edits and stop the tour before
comparing images of the same state.

`DynamicCityConstruction.Apply` uses stable piece IDs and logical ticks. Piece removal unregisters
the graphics binding before destroying its Entity; restoring it creates a new Entity/handle.
Agents demonstrate movement, not pathfinding or collision avoidance. Geometry/block updates use
the package's public runtime APIs; no city/room rules enter the culling algorithm.

The HUD shows ready indirect objects, upload bytes, frame slots and accounted payload memory.
These are diagnostics, not a whole-process memory cap or a measured speedup. The feature is
experimental until Player comparison results establish a useful workload on the target hardware.
