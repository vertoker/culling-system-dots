using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;

namespace CullingSystemDOTS.Dynamic.Ecs
{
    internal struct OcclusionCaptureRow
    {
        internal Entity Entity;
        internal InstanceHandle Handle;
        internal Bounds LocalBounds;
        internal OcclusionInstanceState State;
        internal bool Baseline, Owned, Valid, Restore;
        internal byte Status; // 0 unchanged, 1 changed, 2 invalid, 3 removed
    }

    // This helper never updates automatically. Bindings run its numeric job once before presentation.
    [DisableAutoCreation]
    public partial class DynamicOcclusionCaptureSystem : SystemBase
    {
        internal void Capture(NativeArray<OcclusionCaptureRow> rows)
        {
            // This manually invoked helper has no BeforeOnUpdate dependency import. Complete
            // the actual read types explicitly, including writers scheduled by other systems.
            EntityManager.CompleteDependencyBeforeRO<LocalToWorld>();
            EntityManager.CompleteDependencyBeforeRO<DynamicOcclusionBinding>();
            EntityManager.CompleteDependencyBeforeRO<MaterialMeshInfo>();
            EntityManager.CompleteDependencyBeforeRO<Disabled>();
            EntityManager.CompleteDependencyBeforeRO<MeshLODComponent>();
            var job = new CaptureJob { Rows = rows, Transforms = GetComponentLookup<LocalToWorld>(true),
                Options = GetComponentLookup<DynamicOcclusionBinding>(true), Graphics = GetComponentLookup<MaterialMeshInfo>(true),
                Disabled = GetComponentLookup<Disabled>(true), Lods=GetComponentLookup<MeshLODComponent>(true) };
            job.Run();
        }
        protected override void OnUpdate() { }

        [BurstCompile(CompileSynchronously = true)]
        private struct CaptureJob : IJob
        {
            public NativeArray<OcclusionCaptureRow> Rows;
            [ReadOnly] public ComponentLookup<LocalToWorld> Transforms;
            [ReadOnly] public ComponentLookup<DynamicOcclusionBinding> Options;
            [ReadOnly] public ComponentLookup<MaterialMeshInfo> Graphics;
            [ReadOnly] public ComponentLookup<Disabled> Disabled;
            [ReadOnly] public ComponentLookup<MeshLODComponent> Lods;
            public void Execute()
            {
                for (int i = 0; i < Rows.Length; i++)
                {
                    var row = Rows[i]; var entity = row.Entity;
                    if (!Transforms.HasComponent(entity) || !Options.HasComponent(entity) || !Graphics.HasComponent(entity) ||
                        !Options[entity].Handle.Equals(row.Handle))
                    { row.Status = 3; Rows[i] = row; continue; }
                    var options = Options[entity]; var matrix = Transforms[entity].Value;
                    if (!row.Owned) row.Baseline = Graphics.IsComponentEnabled(entity);
                    bool active = row.Baseline && !Disabled.HasComponent(entity) && Options.IsComponentEnabled(entity);
                    row.Restore = row.Owned && !active;
                    float3 extents = row.LocalBounds.extents;
                    float3 center = math.transform(matrix, (float3)row.LocalBounds.center);
                    float3 size = 2 * (math.abs(matrix.c0.xyz) * extents.x + math.abs(matrix.c1.xyz) * extents.y + math.abs(matrix.c2.xyz) * extents.z);
                    bool valid = !Lods.HasComponent(entity) && !Graphics[entity].HasMaterialMeshIndexRange &&
                        math.all(math.isfinite(matrix.c0)) && math.all(math.isfinite(matrix.c1)) && math.all(math.isfinite(matrix.c2)) &&
                        math.all(math.isfinite(matrix.c3)) && matrix.c0.w == 0 && matrix.c1.w == 0 && matrix.c2.w == 0 && matrix.c3.w == 1 &&
                        math.all(math.isfinite(center)) && math.all(math.isfinite(size)) && math.all(math.isfinite(center - size * 0.5f)) &&
                        math.all(math.isfinite(center + size * 0.5f)) && math.all(math.isfinite(options.Color)) && ((int)options.Roles & ~7) == 0 &&
                        ((options.Roles & OcclusionRole.Occluder) == 0 || options.Opaque != 0);
                    if (!valid) { row.Status = 2; row.Restore = row.Owned; row.Valid = false; Rows[i] = row; continue; }
                    var state = new OcclusionInstanceState { LocalToWorld = matrix, WorldBounds = new Bounds(center, size),
                        Color = options.Color, Roles = active ? options.Roles : OcclusionRole.None, Opaque = options.Opaque != 0 };
                    bool same = row.State.LocalToWorld.Equals(state.LocalToWorld) &&
                        math.all((float3)row.State.WorldBounds.center == center) && math.all((float3)row.State.WorldBounds.size == size) &&
                        math.all(row.State.Color == state.Color) && row.State.Roles == state.Roles && row.State.Opaque == state.Opaque;
                    row.Status = row.Valid && same ? (byte)0 : (byte)1;
                    row.State = state; row.Valid = true; Rows[i] = row;
                }
            }
        }
    }
}
