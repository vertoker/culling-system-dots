# Readable city and corrected Player acceptance

Tested Git package: `29d7034fc91d12ed2f71a40572e5ef24ef0db7b3`, including the
depth/winding correction in `60545b1`. Development-only Player oracle source
was subsequently committed as `ee8e6cc`. Unity 6000.6.8f1, Entities/Graphics 6.6,
URP 17.6, Windows DX12, RTX 4060 Ti, Ryzen 7 7700.

## Normal sample

The city starts intact with a stationary street camera. Seeded edits and the
exterior tour are explicit controls. Both floors contain workbenches, shelves,
crates and machines: 640 independent cube components alongside 689 editable
building pieces and 512 moving agents. Ground/upper agents are distributed per
building; the former home/floor index correlation is removed. Interior fixtures
avoid the demonstrated movement corridors.

Street and workshop buttons share the camera SetPose implementation exercised
by the Player oracle. The camera remains free-flying; navigation and collision
response are not implemented. This report does not claim automated keyboard or
mouse interaction coverage.

## Observed checks

- Stable startup regression first failed on automatic edits being enabled:
  `4eb9884c8c394c648171a88651ddaea2`.
- Scenario/interior/two-view image fixtures passed 2/2:
  `7150a9b8f22641b2bb9145ca5cdf5606`.
- Coupled functional dynamic/legacy rendering passed 57/57 with no failures or
  skips: `c67548cfab4343c088c5e02d18f3dd48`.
- Canonical sample and development mirror matched across all 68 files, including
  preserved and newly Unity-generated GUIDs.
- The existing single Git consumer resolved the exact package hash above from
  Git; the sample was imported from that committed archive, not loose source.
- Coplay build `build-b3a0db1d64` succeeded in 12.538 seconds: zero errors, two
  warnings, 104.59 MB. A visible non-development Mono Player actually executed.

[Machine-readable results](DynamicOcclusion-ReadablePlayer.json) passed all
20 frozen reference/culling pairs: ten states in each of two simultaneous
views, including inside an existing wall and just outside its near-clipped
surface. Lost IDs, changed reference interiors and changed color pixels were
all zero. The Player also checked ordinary rendering without pausing simulation,
three World replacements, and zero accounted payload after final teardown.
Its log contained no errors, exceptions or unreleased/garbage-collected GPU
buffer warnings. Normal street and both workshop color captures were inspected.

This supersedes the earlier eight-state Player coverage for the
[inside-geometry defect](DynamicOcclusion-InsideGeometry.md). It is finite
correctness evidence, not performance acceptance. The requested full-population
scale comparison remains governed by the
[acceptance addendum](../Superpowers/Plans/2026-11-07-dynamic-visual-acceptance-and-scale-plan.md).
