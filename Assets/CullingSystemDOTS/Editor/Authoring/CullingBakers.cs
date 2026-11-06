using System;
using System.Collections.Generic;
using CullingSystemDOTS.Authoring;
using CullingSystemDOTS.Runtime;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Hybrid.Baking;
using Unity.Rendering;
using UnityEditor;
using UnityEngine;

namespace CullingSystemDOTS.Editor
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
                OutOfBounds = authoring.OutOfBounds, EmptyCell = authoring.EmptyCell, SearchNonEmpty = authoring.SearchNonEmpty,
                RegistrationOrder = authoring.RegistrationOrder
            });
        }
    }

    [TemporaryBakingType]
    public struct CullingRendererSource : IBufferElementData
    {
        public UnityObjectRef<Renderer> Renderer;
        public Entity Primary;
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
                var primary = GetEntity(renderer, TransformUsageFlags.Renderable);
                sources.Add(new CullingRendererSource { Renderer = renderer, Primary = primary });
                var filter = GetComponent<MeshFilter>(renderer);
                var mesh = renderer is SkinnedMeshRenderer skinned ? skinned.sharedMesh : filter == null ? null : filter.sharedMesh;
                DependsOn(mesh);
                if (mesh != null) vertices = checked(vertices + mesh.vertexCount);
            }
            AddComponent(entity, new CullingGroupDefinition { Volume = GetEntity(authoring.Volume, TransformUsageFlags.Dynamic),
                GroupIndex = (ushort)authoring.GroupIndex, StateGeneration = 1,
                VertexCount = authoring.UseImportedVertexCount ? authoring.ImportedVertexCount : vertices });
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
                    if (renderer == null) continue;
                    var entities = new List<Entity>();
                    if (EntityManager.HasComponent<MaterialMeshInfo>(source.Primary)) entities.Add(source.Primary);
                    if (EntityManager.HasBuffer<AdditionalEntitiesBakingData>(source.Primary))
                    {
                        var additional = EntityManager.GetBuffer<AdditionalEntitiesBakingData>(source.Primary, true);
                        foreach (var candidate in additional)
                            if (candidate.AuthoringComponentID == renderer.GetEntityId() &&
                                EntityManager.HasComponent<MaterialMeshInfo>(candidate.Value) && !entities.Contains(candidate.Value))
                                entities.Add(candidate.Value);
                    }
                    foreach (var entity in entities)
                        bindings.Add(new CullingBakedRenderBinding { Main = entity, OriginalShadowMode = renderer.shadowCastingMode,
                            InitialRendererEnabled = renderer.enabled ? (byte)1 : (byte)0,
                            InitialForceRenderingOff = renderer.forceRenderingOff ? (byte)1 : (byte)0 });
                }
            }
        }

    }
}
