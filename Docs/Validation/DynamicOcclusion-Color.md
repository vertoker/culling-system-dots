# Dynamic indirect color and shadow contract

Source: the source commit introducing this report, based on `c335e72`. Unity 6000.6.8f1,
URP 17.6, Windows Editor DX12, RTX 4060 Ti. This is a package shader/transport check, not
acceptance of the complete dynamic ECS renderer or a Player performance result.

`DynamicIndirectColorTests` verifies the shipped 80-byte matrix/color instance layout,
GPU compaction into separate camera buffers and two command groups with distinct
`startInstance` offsets. Opposite masks exchange red/green rendered pixels between two
views. The shader uses the installed `GetIndirectInstanceID_Base` helper.

The shadow fixture first proves that an ordinary URP Lit caster changes receiver pixels.
It then removes that caster and submits the package shader with zero camera-visible
instances and independent full-shadow arguments. Both views match ordinary receiver
pixels exactly, in both camera orders. This validates directional hard shadows in this
fixture; punctual lights and general material/pipeline parity remain unverified.

| Check | Job | Result |
| --- | --- | --- |
| Color before shader/compaction implementation | `cd6630ef052b4bcdb549c331f1dac609` | 0/1, expected failure |
| Color after implementation | `e8b0166ce2cf43c6a66e973803b94978` | 1/1 |
| Shadow before shadow pass | `cea835ad32564c5d8b3d5469bc9e12fc` | 0/1; 209 differing receiver pixels |
| Both color/shadow fixtures | `b035e7af015d4a00ac2dc16d7e05adfc` | 2/2 |
| Coupled functional rendering and dynamic GPU suites | `bd9c0330939349129b6691648b1b4dbc` | 51/51, no failures/skips |

An earlier shadow setup used URP Unlit, which has no ShadowCaster pass; its prerequisite
failed and was corrected to Lit before recording the shadow-pass RED above.
ReadPixels is assertion-only. Production visibility never uses this readback.

The serialized shader-resource asset references both the new shader and compaction compute
shader. The tests resolve that asset by GUID in the Editor. Runtime consumers must retain a
direct serialized reference. Frame/view resource rings, production ECS ownership transfer,
the dynamic construction sample, paired image oracle and Player measurements remain pending
in the [implementation plan](../Superpowers/Plans/2026-11-06-dynamic-geometric-occlusion-plan.md).
