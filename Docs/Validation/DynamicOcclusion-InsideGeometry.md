# Camera inside geometry: defect and correction

Corrected production revision: `60545b1887756988858067c3198fed0d9b281e9c`.
Unity 6000.6.8f1, stock Entities/Graphics 6.6, URP 17.6, Windows Editor DX12,
RTX 4060 Ti. Built-Player acceptance of this correction is still pending.

The user's observation was a real visibility defect. Previous eight-state
captures did not put the camera inside the thickness of a restored, existing
wall while simultaneously validating an outside camera.

## Reproduction and isolation

The extended oracle now runs ten frozen states in two simultaneous views. It
restores the previously removed wall, asserts that the primary camera is inside
its bounds, and compares each view with its own ordinary reference. The first
run `fc2c56de4e524b90835961b244dd0f89` failed on a lost reference-visible ID in
state 8. Diagnostic `fd4f6d01033a4c129b4c9b1ff82189b4` lost IDs 9,10,15,11 with
normal depth and none when omitting only the containing wall.

A one-sided plane independently confirmed the deeper orientation defect.
An ordinary blue material rendered its front and rejected its back. The GPU
depth path did the reverse: the front did not hide a red target, while the back
incorrectly hid it. RED `44b3dc949fc14740ac9bc62ffeae20b7` recorded ordinary
front/back blue 255/0 and GPU front/back target red 255/0. Cube-only tests had
not exposed this because an outside view can still encounter the farther exit
surface of a closed cube.

## Correction and checks

The depth shader supplies its own view/projection matrix. Depth recording now
sets raster winding from that explicit transform, including the handedness
flip in Camera.worldToCameraMatrix and the projection's XY orientation, then
restores the prior culling setting. Materials remain single-sided.

Containing and near-clipped occluder bounds are also omitted for that view:
there is no accepted opaque front-surface coverage proof there. The renderer
passes the actual camera near plane. The separate outside view can still use
the same wall. A low-level test requires uncertain depth to stay far/clear and
an entirely forward wall to retain useful depth.

| Check | Job | Result |
| --- | --- | --- |
| Near-plane guard, two-view ten-state oracle | `311b93279934428baac8f87572273ca2` | 1/1 |
| Depth, winding and two-view image fixtures after orientation correction | `23db5390e8244ab2aef78d2786817ea5` | 14/14 |
| Coupled dynamic and legacy camera/LOD/shadow/streaming rendering | `5c2bb5c762544f97a3f86139d524b774` | 57/57, no failures/skips |

This extends finite correctness coverage; it does not establish acceleration or
universal correctness. Historical Player/performance evidence predates this
correction. The [new acceptance plan](../Superpowers/Plans/2026-11-07-dynamic-visual-acceptance-and-scale-plan.md)
owns the stable demo, corrected Player execution and the requested scale tests.
