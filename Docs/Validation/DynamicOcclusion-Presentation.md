# Dynamic ECS/URP presentation checkpoint

Source: the commit introducing this report, based on `4ca4174`. Unity 6000.6.8f1, Entities/Graphics
6.6, URP 17.6, Windows Editor DX12, RTX 4060 Ti. No Player speedup is claimed by this checkpoint.

The optional Dynamic.URP assembly attaches one presentation owner to DynamicOcclusionSystem.
After numeric capture/preparation and before Entities Graphics updates, only uploaded packet
handles take stock graphics ownership. A batch changes enable bits only when ownership changes;
it completes tracked jobs once for that batch, not once per object/camera. Simulation stays enabled.
System stop/destruction, owner disable/dispose and unavailable configuration restore stock baselines.

Actual pipeline render scale and all active game cameras are checked. TAA, jitter, dynamic
resolution, stereo and camera stacks retain stock rendering. Registered single-sample cameras can
occlude; unsupported raw views retain full draws. A guard before the GPU camera callback restores
stock ownership when registry data changed after frame publication. Rendering-layout changes
require unbind/rebind; initial LOD, lightmap, material-range and known deformation bindings reject
before taking graphics. Capture also withdraws LOD/material-range rows that become unsupported.

| Check | Job | Result |
| --- | --- | --- |
| Missing batch transfer RED | `211c8306687b4cacaaaa7f867f304787` | 0/1, stock bit stayed enabled |
| Missing presentation bridge RED | `113f68a35b9747fda0b9a2f0b7a42347` | 0/1, ready graphics were not transferred |
| Missing LOD rejection RED | `e3a8d46e87a74fa5a057120258ebd4f2` | 0/1, unsupported bind succeeded |
| Extended real ECS pixels/ownership fixture | `dc5186eee8484cd9a5557ab2034c684b` | 1/1 |
| Coupled EditMode | `1220c130c73e407880bfc6c12c08338d` | 117/117, no failures/skips |
| Coupled functional PlayMode | `328ddc70f3b34773a4f2e4c0a2d580d8` | 54/54, no failures/skips |

The real ECS fixture first proves ordinary pixels, then current wall occlusion, disable/re-enable,
TAA and stack restoration, late wall removal and missing resources. The late mutation occurs in
beginContextRendering after presentation capture; endCameraRendering observes restored stock
graphics in that same camera render. Rendered target pixels return. An earlier LOD test accidentally
failed on a missing renderable material; the fixture was corrected to an occluder-only descriptor
before recording the meaningful LOD RED above.

The construction sample, complete paired-image/shadow oracle, remaining motion/World-replacement
scenarios, installation and Player execution remain open in the
[plan](../Superpowers/Plans/2026-11-06-dynamic-geometric-occlusion-plan.md).
