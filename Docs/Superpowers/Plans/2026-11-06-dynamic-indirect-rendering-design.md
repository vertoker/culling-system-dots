# Algorithm 3: package-owned GPU indirect continuation

**Status:** User selected and authorized this continuation after the stock G1 investigation.
This addendum extends the [original design](2026-11-06-dynamic-geometric-occlusion-design.md);
its registry, coverage, lifetime, budgets, demo and correctness requirements still apply.

## Selected approach

The [stock integration investigation](../../Validation/DynamicOcclusion-Integration.md) found no
usable GPU-result input and measured zero useful strict async results during camera motion.
The user chose package-owned GPU indirect submission rather than CPU software occlusion.
This changes graphics ownership for explicitly registered rigid meshes. It does not replace
Unity's render pipeline, patch Entities Graphics, or disable ECS simulation.

Use a public Unity indirect API with GPU-resident compacted instance IDs and draw arguments.
The first transport candidate is `Graphics.RenderMeshIndirect`, verified in the installed API,
with `GraphicsBuffer.IndirectDrawIndexedArgs` and the installed `UnityIndirect.cginc` accessors.
If that cannot satisfy per-view ordering/shadows, evaluate a package-owned BRG indirect callback
within the same authorized scope. Do not return to synchronous readback as a production solution.
Task 0b must prove the concrete API/order before freezing presentation files.

## Rendering ownership and supported material boundary

Shipped resources include an explicit indirect-compatible opaque material/shader contract, with
per-instance transform/color and a separate shadow pass. The baseline is rigid opaque triangles,
single-sample views, URP 17.6, Windows DX12. The first package shader may be unlit; an extension
must explicitly implement the instance-buffer layout to add other lighting/material behavior.
Do not silently replace a consumer's material or claim arbitrary URP shader compatibility.

One opt-in renderer binding covers one geometry/submesh/material. Multi-material meshes use
explicit submesh bindings. Existing static-bake/stock rendering and LOD behavior remain unchanged.
Stock LOD/skinned/alpha-clipped/transparent/motion-vector/lightmapped rendering is not silently
transferred to an incomplete implementation: unsupported bindings remain in stock rendering.
Automatic new-route LOD selection is not assumed; providers can change registered geometry.
These are builder-selected initial support limits, not claims that the user requested those limits.

Transfer the stock graphics enable bit only after a complete supported binding/resources exists,
and record its original state. Reject legacy/dynamic overlap. Restore on disable, unregister,
World destruction or unavailable setup; never disable the Entity. Non-ECS providers opt in
through the same geometry registry without requiring stock Entities Graphics registration.

Color/depth visibility uses each camera's current-frame result. Shadows use an independent
all-caster submission until conservative light-view culling is implemented; camera occlusion
cannot shorten its argument buffer. The new shadow pixel test is mandatory before shipping.

## Current-view dataflow and failure behavior

Prepare immutable geometry/instance snapshots before presentation. At the proven camera boundary:
render all eligible occluder triangles at the reference target sample grid, build the conservative
farthest-depth hierarchy, test enclosing current bounds, compact visible instance indices, update
indirect arguments, then consume those buffers in that camera's opaque submission. No visibility
readback or broad per-camera ECS enable-bit loop is part of this path.

The prepass must precede consumption on the same graphics queue; preserve exact camera matrices,
jitter, dimensions and viewport. A resource/view/geometry mismatch selects ordinary rendering.
Keep distinct immutable full-visible and culled draw data so a failed compute setup cannot leave
a partially compacted full-count draw. Unsupported view/MSAA renders normally.

GPU resources, retired geometries and snapshots remain alive through the last queued color and
shadow reader. Record the actual end-of-submission fence boundary in Task 0b, not a fence that
covers only the earlier depth compute. Reuse/reclaim only completed slots; budget exhaustion
uses ordinary rendering and records a reason. Constructor failure frees partial allocations.

## Proof and acceptance

Task 0b adds a root-only minimal transport fixture. Prove GPU-written zero/nonzero instance counts
change rendered pixels; two simultaneous views have independent arguments; moving cameras and
edits can consume current-frame buffers without CPU readback; a camera-hidden caster leaves the
same visible shadow as ordinary rendering. Readback is permitted only outside measured intervals
for assertions. These prove transport, not general Hi-Z correctness or performance.

Then execute original Tasks 1-7 with the presentation amendments in the implementation plan.
Retain paired reference images, thin openings, demolition, moving Entities, budgets and actual
Player execution. A fast-path claim still requires measured end-to-end improvement in a declared
moving-camera workload. Keep experimental status if it fails acceptance and explain the result.
