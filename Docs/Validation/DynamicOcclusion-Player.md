# Dynamic City Git installation and Player acceptance

Tested package revision: `908bc3e9eda23921f95a393a49b38f84771f5151`.
Unity 6000.6.8f1, stock Entities/Entities Graphics 6.6 and URP 17.6, Windows
Direct3D12, NVIDIA RTX 4060 Ti, AMD Ryzen 7 7700. This report records functional
acceptance; performance is a separate measurement.

## Installed payload

The single owned Git consumer resolved the package from the repository Git URL
with `?path=/Assets/CullingSystemDOTS#<revision>`. Package Manager installed it in
Library/PackageCache. Entities Graphics remained the stock UPM package. Both
samples were imported; Dynamic City references its imported content and package
resources, without root-project resources or Editor APIs in its runtime.

Consumer checks before the final shutdown correction: EditMode 257/257
(`1b48be7cfa634242b8b2cf7680f2919b`), PlayMode 45/45
(`99d082f42c654c7bb449c3fbe789d939`). The root-only additional fixtures explain
the difference from the main project's 258 EditMode and 56 PlayMode checks.
The final coupled main-project PlayMode run after the correction passed 56/56
(`f11a7179f3044c2196f82f6ef8679f7a`), with no failures or skips.

## Actual Player execution

Coplay build `build-1fb9630b01` succeeded in 12.861 seconds with zero errors and
one warning. The non-development Windows Player actually rendered the imported
sample with 512 agents and 689 editable pieces. The opt-in development harness
is outside the shipped package and activates only with its explicit argument.

[Machine-readable results](DynamicOcclusion-Player.json) contain eight frozen
reference/culling pairs covering construction, doors, floors, indoor/near-wall
views, teleport and advanced agent poses. All eight had zero lost reference IDs,
zero changed reference interiors and zero changed color pixels in this run.
Twelve confirmed GPU frames include startup and three World reloads; the 36
ordinary/fallback frames include deliberately disabled reference captures.
Simulation continues with rendering disabled. Retired resource accounting reached
zero after teardown. The Player log contained no exceptions, errors, unreleased
non-evictable buffers or GraphicsBuffer garbage-collection warnings.

This is finite tested coverage, not a proof for arbitrary shaders or geometry.
The oracle's boundary tolerance remains described in the
[sample report](DynamicOcclusion-Sample.md).

## Validation corrections

A hidden Player window updated gameplay without executing camera rendering.
Those attempts did not establish rendering acceptance. Repeating with a visible
window produced the recorded result; performance runs also require a visible
window and use neither batchmode nor nographics.

Actual Player shutdown exposed an ordering bug: application quitting drained
retired buffers before the active published frame had retired. Revision
`908bc3e` stops registered renderer owners before the final shutdown drain.
The regression first retained 74,704 accounted bytes, then passed with zero;
normal frame submission still uses fences without blocking visibility readback.

The independent whole-feature review's important shadow-only-depth finding was
fixed in `751ec24`. See [review evidence](DynamicOcclusion-Review.md).

Subsequent user feedback exposed a camera-inside-solid and one-sided-depth
orientation defect beyond this report's eight-state scope. See the
[correction and expanded oracle](DynamicOcclusion-InsideGeometry.md).
