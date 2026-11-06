# Dynamic indirect color and shadow contract

Shader/transport source: `0e52a32`, based on `c335e72`. Unity 6000.6.8f1,
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

## Bounded instance-packet checkpoint

The source commit adding this section introduces `OcclusionIndirectPacket`. It selects only
CPU-prepared, current uploaded rigid geometry with an explicit compatible opaque material.
It uploads 120 bytes per instance plus one 20-byte indirect argument per group through the
same byte budget as geometry. Selection reserves the worst case of one group per instance;
unselected handles are not candidates for ownership transfer. GPU/CPU upload-array payloads
are charged to the World resource budget. A completed slot can reuse matching buffer sizes;
the future presentation ring must enforce the final-consumer fence before requesting reuse.

The synthetic test covers pending geometry, a 280-byte/two-instance limit, exact model/color
bytes, separate material groups, reusable buffers and rejection of a transparent material.
It also covers source order A,B,A,A with full-draw order A,A,A,B. That case exposed an index
mix-up in compaction: visibility/group lookup must use the model ID from the permutation.

| Check | Job | Result |
| --- | --- | --- |
| Missing packet implementation | `705619330c694d1d9ae7f9dba6aab57e` | 0/1, expected failure |
| Nonidentity group permutation before fix | `0e09b97521a5425fac877dbbfbd09670` | 0/1; B received model 2 instead of 1 |
| Matching-buffer reuse before implementation | `7b911fcf13f547ce9eb602195703d0fb` | 0/1, expected failure |
| Coupled functional rendering and dynamic GPU suites | `e33d4eda73694859a7dd515a5ef05e3c` | 52/52, no failures/skips |

Buffer copies require explicit CopySource/CopyDestination targets; an intermediate fixture
failure identified and corrected those flags before the permutation RED above. This test
inspects packet membership, not actual ECS ownership or complete frame-ring safety.
