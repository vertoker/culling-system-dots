# Dynamic geometric occlusion

This independent experimental renderer updates visibility from current rigid triangle geometry.
It does not require a baked visibility asset or building/room knowledge. The existing static
baker and ordered evaluator remain available. Persistent runtime PVS generation and algorithm 2
are separate work; this implementation is algorithm 3.

## Initial scope

Use Unity 6000.6, stock Entities/Entities Graphics 6.6, URP 17.6 and Windows DX12 with compute,
graphics fences and R32 floating-point render/blend/load-store support. Up to four registered
cameras can use current-view occlusion. Rendering uses a package-owned RenderMeshIndirect path
for explicit rigid opaque bindings; other objects continue through stock Entities Graphics.
The supplied indirect shader is unlit with an independent shadow pass. Arbitrary URP materials
are not automatically converted. An extension shader must implement the explicit instance layout
and supported opaque coverage contract.

The accepted reference grid is the camera's full single-sample output size. Uncovered samples
remain far in the maximum-depth hierarchy. Invalid bounds, near crossings and unknown input keep
visibility. Unsupported views use full rendering. TAA, jitter, stereo, dynamic resolution,
non-unit URP render scale and camera stacks restore stock graphics. Other pipelines, custom
render features/material deformation and lighting equivalence need their own validation.

Occluders whose enclosing bounds touch or cross the camera near plane do not contribute
depth for that view. This includes a camera inside a wall's thickness: uncertain front-surface
coverage must not hide the room beyond it. Other views can still use the same wall. Occluder
depth uses one-sided front coverage with orientation derived from the explicit view/projection
matrices; it does not turn ordinary materials double-sided.

## Explicit ECS setup

Assign `OcclusionShaderResources.asset` through a serialized reference. Create a material using
`CullingSystemDOTS/Dynamic Indirect Unlit`. Keep both referenced by scene/prefab data for Player
shader inclusion. Runtime never uses AssetDatabase or an assumed package path.

```csharp
using CullingSystemDOTS.Dynamic;
using CullingSystemDOTS.Dynamic.Ecs;
using CullingSystemDOTS.Dynamic.Urp;
using Unity.Mathematics;
using UnityEngine.Rendering;

// world already contains the normal Entities Graphics systems.
var presentation = DynamicOcclusionPresentation.Install(world, shaderResources,
    new OcclusionSettings());
presentation.RegisterView(camera);
var system = presentation.System;
var geometry = system.Registry.RegisterGeometry(readableMesh);
var instance = system.Bindings.Bind(entity, new OcclusionInstanceDescriptor {
    Geometry = geometry,
    Material = indirectMaterial,
    SubMesh = 0,
    Layer = layer,
    Shadows = ShadowCastingMode.On,
    State = new OcclusionInstanceState {
        Color = new float4(1, 1, 1, 1), // Linear shader color.
        Opaque = true,
        Roles = OcclusionRole.Renderable | OcclusionRole.Occludee
    }
});
```

The Entity must already have valid stock graphics and LocalToWorld. Its original material is the
fallback; assign a visually corresponding indirect material deliberately. Bindings capture current
transforms and enclosing registered-mesh bounds after presentation transforms, including hidden
objects. Add `Occluder` only for genuinely opaque rigid coverage. Small moving details can remain
occludees without becoming occluders. Uncertain/deformed geometry cannot safely provide coverage.

Roles are independent: Renderable opts into the new graphics path, Occludee permits camera
occlusion, and Occluder contributes current triangle depth. Material/color/layer/submesh/shadow
groups are explicit. LOD, lightmapped, material-range and known deformed Entities retain stock
graphics. Registration reserves exclusive graphics ownership; overlapping static-bake bindings
reject. Do not modify MaterialMeshInfo manually while the presentation owner holds it.

## Mutation and lifetime

Publish simulation transforms/options before DynamicOcclusionSystem in the presentation group.
For an ECS binding, modify `DynamicOcclusionBinding.Color`, `Roles` or `Opaque`; capture reads
those options and LocalToWorld. Disable that enableable binding to opt out while preserving the
Entity and its simulation. Rendering-layout changes require unbind/rebind. In-place Mesh edits
require `Registry.ReplaceGeometry(handle, mesh)` even when the Mesh object identity is unchanged.
Registered geometry is copied; mutating the source alone does not update its registered copy.

Use `Bindings.Unbind(entity)` before destroying a registered Entity when possible; capture also
prunes dead/reused Entity versions. Remove unused geometry after all dependent instances are
unregistered. Registry mutation after publication invalidates the camera handoff and restores
stock rendering. Transform writers still belong before capture; arbitrary post-capture gameplay
writes are not an alternative simulation schedule.

Set `presentation.Enabled = false` to restore ordinary graphics immediately. To remove capture
overhead during a comparison, also set `presentation.System.Enabled = false`; re-enable the
system before enabling presentation again. Neither operation disables agent/gameplay Entities.
Dispose presentation when its host is removed. System stop/destruction also suspends/disposes it.
GPU geometry, snapshots and frame/view buffers retire after the final color/shadow readers,
including after World teardown. Normal frame submission performs no visibility readback or GPU wait.

## Budgets and diagnostics

Configure OcclusionSettings before attaching presentation or registering objects. Sparse blocks
coalesce logical geometry/instance changes. CPU preparation is not GPU readiness: only completely
uploaded matching geometry and instance packets can take graphics ownership. Geometry and instance
payloads share a per-frame upload budget; frame slots and depth/compaction resources are bounded.
Pending, unsupported and unselected objects retain stock rendering. Raising a budget changes
coverage/cost, not a guaranteed speedup or a wall-clock bound on individual engine calls.

Read `ReadyInstances`, `UploadedBytes`, `InFlightSlots`, `AccountedResourceBytes`, `LastFault` and
`ViewFallback(camera)` for setup/fallback diagnostics. Memory accounting covers owned payloads,
not Unity/driver/process memory. Camera occlusion never shortens shadow arguments: accepted
casters use a separate full-caster submission. This favors correctness and may be expensive.

Import **Dynamic City construction** from Package Manager for serialized setup, a first-person
camera, individual piece edits and independent ECS agents. Its ordinary-rendering switch uses
the same simulation. Performance comparison must use the same camera path, simulation and edits;
fewer indirect instances alone do not prove an end-to-end improvement.

The city starts intact with automatic edits and the exterior tour disabled. Street and workshop
buttons select useful views; the free camera has no collision response. Seeded construction and
the tour are explicit controls. Interior fixtures are independent renderable/occludee bindings.

The Windows DX12 cube-city Player comparison has not established a speedup over
ordinary Entities Graphics. Current CPU capture, registry updates and frame packet
preparation add substantial cost to a fully moving population; the cheap geometry
does not repay that cost in the measured workload. Keep this renderer experimental
and measure your own content before adopting it as an optimization. Functional
rendering acceptance is separate from performance acceptance.
