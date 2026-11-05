using System;
using System.Collections.Generic;
using PerfectCullingDOTS.Authoring;
using PerfectCullingDOTS.Runtime;
using Unity.Collections;
using Unity.Entities;
using Unity.Rendering;
using UnityEditor;
using UnityEngine;

namespace PerfectCullingDOTS.Editor
{
    public sealed class CullingVolumeBaker : Baker<CullingVolumeAuthoring>
    {
        public override void Bake(CullingVolumeAuthoring authoring)
        {
            DependsOn(authoring.Bake);
            if (authoring.Bake == null) throw new ArgumentException("Culling volume needs an immutable bake asset.");
            if (!authoring.Bake.TryCreateBlob(out var blob, out _, out var diagnostic)) throw new ArgumentException(diagnostic);
            AddBlobAsset(ref blob, out _);
            var objectId = GlobalObjectId.GetGlobalObjectIdSlow(authoring);
            string identity = objectId.targetObjectId == 0 ? authoring.GetEntityId().ToString() : objectId.ToString();
            var id = new Unity.Entities.Hash128(UnityEngine.Hash128.Compute(identity).ToString()).Value;
            AddComponent(GetEntity(TransformUsageFlags.Dynamic), new CullingVolume {
                Bake = blob, InstanceId = id, Size = authoring.Size, Layers = authoring.Layers,
                OutOfBounds = authoring.OutOfBounds, EmptyCell = authoring.EmptyCell, SearchNonEmpty = authoring.SearchNonEmpty
            });
        }
    }

    [TemporaryBakingType]
    public struct CullingRendererSource : IBufferElementData
    {
        public UnityObjectRef<Renderer> Renderer;
    }

    public sealed class CullingGroupBaker : Baker<CullingGroupAuthoring>
    {
        public override void Bake(CullingGroupAuthoring authoring)
        {
            if (authoring.Volume == null || authoring.Volume.Bake == null)
                throw new ArgumentException("Culling group needs an explicit volume and bake.");
            DependsOn(authoring.Volume.Bake);
            if (authoring.GroupIndex < 0 || authoring.GroupIndex >= authoring.Volume.Bake.GroupCount)
                throw new ArgumentException("Group index must address the original immutable bake mapping.");
            var entity = GetEntity(TransformUsageFlags.None);
            var sources = AddBuffer<CullingRendererSource>(entity);
            int vertices = 0;
            foreach (var renderer in authoring.Renderers ?? Array.Empty<Renderer>())
            {
                if (renderer == null) continue;
                DependsOn(renderer);
                GetEntity(renderer, TransformUsageFlags.Renderable);
                sources.Add(new CullingRendererSource { Renderer = renderer });
                var filter = GetComponent<MeshFilter>(renderer);
                var mesh = renderer is SkinnedMeshRenderer skinned ? skinned.sharedMesh : filter == null ? null : filter.sharedMesh;
                DependsOn(mesh);
                if (mesh != null) vertices = checked(vertices + mesh.vertexCount);
            }
            AddComponent(entity, new CullingGroupDefinition { Volume = GetEntity(authoring.Volume, TransformUsageFlags.Dynamic),
                GroupIndex = (ushort)authoring.GroupIndex, StateGeneration = 1, VertexCount = vertices });
            AddBuffer<CullingBakedRenderBinding>(entity);
        }
    }

    // Uses the stock baker's exact renderer -> all generated graphics entities mapping.
    [WorldSystemFilter(WorldSystemFilterFlags.BakingSystem)]
    [UpdateInGroup(typeof(PostBakingSystemGroup))]
    public partial class CullingRendererBindingBakingSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            var options = EntityQueryOptions.IncludePrefab | EntityQueryOptions.IncludeDisabledEntities;
            var mapping = new Dictionary<Renderer, List<Entity>>();
            using (var query = EntityManager.CreateEntityQuery(new EntityQueryDesc { All = new[] { ComponentType.ReadOnly<MeshRendererBakingData>() }, Options = options }))
            using (var entities = query.ToEntityArray(Allocator.Temp))
                foreach (var entity in entities) Add(mapping, EntityManager.GetComponentData<MeshRendererBakingData>(entity).MeshRenderer.Value, entity);
            using (var query = EntityManager.CreateEntityQuery(new EntityQueryDesc { All = new[] { ComponentType.ReadOnly<SkinnedMeshRendererBakingData>() }, Options = options }))
            using (var entities = query.ToEntityArray(Allocator.Temp))
                foreach (var entity in entities) Add(mapping, EntityManager.GetComponentData<SkinnedMeshRendererBakingData>(entity).SkinnedMeshRenderer.Value, entity);
            using var groupsQuery = EntityManager.CreateEntityQuery(new EntityQueryDesc { All = new[] { ComponentType.ReadOnly<CullingRendererSource>(), ComponentType.ReadWrite<CullingBakedRenderBinding>() }, Options = options });
            using var groups = groupsQuery.ToEntityArray(Allocator.Temp);
            foreach (var group in groups)
            {
                var sources = EntityManager.GetBuffer<CullingRendererSource>(group, true);
                var bindings = EntityManager.GetBuffer<CullingBakedRenderBinding>(group);
                bindings.Clear();
                foreach (var source in sources)
                {
                    var renderer = source.Renderer.Value;
                    if (renderer == null || !mapping.TryGetValue(renderer, out var entities)) continue;
                    foreach (var entity in entities)
                        bindings.Add(new CullingBakedRenderBinding { Main = entity, OriginalShadowMode = renderer.shadowCastingMode,
                            InitialRendererEnabled = renderer.enabled ? (byte)1 : (byte)0,
                            InitialForceRenderingOff = renderer.forceRenderingOff ? (byte)1 : (byte)0 });
                }
            }
        }

        private static void Add(Dictionary<Renderer, List<Entity>> mapping, Renderer renderer, Entity entity)
        {
            if (renderer == null) return;
            if (!mapping.TryGetValue(renderer, out var entities)) mapping.Add(renderer, entities = new List<Entity>());
            entities.Add(entity);
        }
    }
}
