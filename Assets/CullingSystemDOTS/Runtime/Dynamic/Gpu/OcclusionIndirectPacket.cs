using System;
using System.Collections.Generic;
using System.Threading;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace CullingSystemDOTS.Dynamic.Gpu
{
    internal sealed class OcclusionIndirectPacket : IDisposable
    {
        internal readonly struct Group
        {
            internal readonly GeometryHandle Geometry;
            internal readonly Mesh Mesh;
            internal readonly Material Material;
            internal readonly int SubMesh, Layer;
            internal readonly ShadowCastingMode Shadows;
            internal readonly Bounds Bounds;
            internal Group(OcclusionInstanceSnapshot row,Mesh mesh,Material material,Bounds bounds)
            { Geometry=row.Geometry;Mesh=mesh;Material=material;SubMesh=row.SubMesh;Layer=row.Layer;Shadows=row.Shadows;Bounds=bounds; }
            internal bool Matches(OcclusionInstanceSnapshot row,Material material) => Geometry.Equals(row.Geometry) && Material==material &&
                SubMesh==row.SubMesh && Layer==row.Layer && Shadows==row.Shadows;
        }
        private sealed class Builder
        {
            internal OcclusionInstanceSnapshot Row;
            internal Mesh Mesh;
            internal Material Material;
            internal Bounds Bounds;
            internal readonly List<uint> Ids=new List<uint>();
        }
        private struct WorldCandidate
        {
            internal float3 Center;
            internal uint Valid;
            internal float3 Extents;
            internal uint Padding;
        }
        internal InstanceHandle[] Handles { get; private set; }
        internal GraphicsBuffer Models, Candidates, Ids, GroupIds, Arguments;
        internal Group[] Groups { get; private set; }
        internal int Count => Handles.Length;
        internal int GroupCount => Groups.Length;
        private readonly OcclusionMemoryBudget memory;
        private readonly int thread=Thread.CurrentThread.ManagedThreadId;
        private long reserved;
        private OcclusionIndirectPacket(OcclusionMemoryBudget memory) { this.memory=memory; }

        // A complete immutable frame payload. The presentation owner retains this packet, its
        // snapshot and its MeshSet until the final color AND shadow reader has finished.
        // Reuse is permitted only after that slot's final fence has passed.
        internal static OcclusionIndirectPacket Create(OcclusionWorld registry,OcclusionSnapshotLease snapshot,OcclusionGpuMeshSet meshes,
            OcclusionShaderResources resources,OcclusionGpuUploadBudget budget,OcclusionIndirectPacket reusable=null)
        {
            if(registry==null || snapshot==null || meshes==null || budget==null || budget.Frame==0 ||
                snapshot.WorldIdentity!=registry.Identity || budget.Limit>registry.UploadByteLimit)
                throw new ArgumentException("A matching current snapshot, mesh readers and shared frame budget are required.");
            if(snapshot.Revision!=registry.Revision || resources==null || resources.Indirect==null || resources.IndirectArguments==null) return null;
            // 120 bytes per instance plus at most one 20-byte argument per instance. Geometry
            // uses the same budget before this step; omitted rows remain outside graphics ownership.
            int limit=Math.Min(snapshot.Instances.Length,budget.Remaining/140);
            if(limit==0) return null;
            var rows=new List<OcclusionInstanceSnapshot>(); var builders=new List<Builder>(); var groupIds=new List<uint>();
            for(int index=0;index<snapshot.Instances.Length && rows.Count<limit;index++)
            {
                var row=snapshot.Instances[index]; var material=snapshot.GetMaterial(index);
                if(row.Prepared==0 || row.Opaque==0 || (row.Roles&OcclusionRole.Renderable)==0 || !SupportedMaterial(material) ||
                    row.Shadows==ShadowCastingMode.TwoSided) continue;
                float determinant=math.determinant(new float3x3(row.LocalToWorld.c0.xyz,row.LocalToWorld.c1.xyz,row.LocalToWorld.c2.xyz));
                if(!OcclusionValidity.Finite(determinant) || determinant<=0) continue;
                var geometry=snapshot.GetGeometry(row.Geometry);
                if(!geometry.Available || !meshes.TryGet(row.Geometry,geometry.Revision,out var mesh)) continue;
                int group=0;
                for(;group<builders.Count;group++)
                {
                    var prior=builders[group];
                    if(new Group(prior.Row,prior.Mesh,prior.Material,prior.Bounds).Matches(row,material)) break;
                }
                if(group==builders.Count) builders.Add(new Builder {Row=row,Mesh=mesh,Material=material,Bounds=row.WorldBounds});
                var builder=builders[group]; builder.Bounds.Encapsulate(row.WorldBounds); builder.Ids.Add((uint)rows.Count);
                groupIds.Add((uint)group); rows.Add(row);
            }
            if(rows.Count==0) return null;
            int bytes=checked(rows.Count*120+builders.Count*GraphicsBuffer.IndirectDrawIndexedArgs.size);
            bool reuse=reusable!=null && reusable.Models!=null && reusable.Count==rows.Count && reusable.GroupCount==builders.Count;
            if(!reuse) reusable?.Dispose();
            var packet=reuse?reusable:new OcclusionIndirectPacket(registry.ResourceBudget);
            try
            {
                // GPU payload and corresponding CPU upload arrays; engine/managed-object overhead
                // is not a whole-process memory accounting guarantee.
                if(!reuse) {packet.memory.Reserve((long)bytes*2); packet.reserved=(long)bytes*2;}
                packet.Handles=new InstanceHandle[rows.Count]; packet.Groups=new Group[builders.Count];
                var models=new OcclusionGpuInstanceData[rows.Count]; var candidates=new WorldCandidate[rows.Count]; var ids=new uint[rows.Count];
                var arguments=new GraphicsBuffer.IndirectDrawIndexedArgs[builders.Count];
                for(int i=0;i<rows.Count;i++)
                {
                    var row=rows[i]; packet.Handles[i]=row.Handle;
                    models[i]=new OcclusionGpuInstanceData {LocalToWorld=row.LocalToWorld,Color=row.Color};
                    candidates[i]=new WorldCandidate {Center=row.WorldBounds.center,Extents=row.WorldBounds.extents,Valid=(row.Roles&OcclusionRole.Occludee)!=0?1u:0u};
                }
                int offset=0;
                for(int i=0;i<builders.Count;i++)
                {
                    var builder=builders[i]; var row=builder.Row;
                    packet.Groups[i]=new Group(row,builder.Mesh,builder.Material,builder.Bounds);
                    arguments[i]=new GraphicsBuffer.IndirectDrawIndexedArgs {indexCountPerInstance=builder.Mesh.GetIndexCount(row.SubMesh),
                        instanceCount=(uint)builder.Ids.Count,startIndex=builder.Mesh.GetIndexStart(row.SubMesh),
                        baseVertexIndex=builder.Mesh.GetBaseVertex(row.SubMesh),startInstance=(uint)offset};
                    foreach(uint id in builder.Ids) ids[offset++]=id;
                }
                if(!reuse)
                {
                    packet.Models=new GraphicsBuffer(GraphicsBuffer.Target.Structured,rows.Count,80);
                    packet.Candidates=new GraphicsBuffer(GraphicsBuffer.Target.Structured,rows.Count,32);
                    packet.Ids=new GraphicsBuffer(GraphicsBuffer.Target.Structured,rows.Count,4);
                    packet.GroupIds=new GraphicsBuffer(GraphicsBuffer.Target.Structured,rows.Count,4);
                    packet.Arguments=new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments|GraphicsBuffer.Target.Raw|GraphicsBuffer.Target.CopySource,builders.Count,GraphicsBuffer.IndirectDrawIndexedArgs.size);
                }
                if(budget.TakeAligned(bytes,4)!=bytes) throw new InvalidOperationException("Shared upload budget changed during packet construction.");
                packet.Models.SetData(models);packet.Candidates.SetData(candidates);packet.Ids.SetData(ids);
                packet.GroupIds.SetData(groupIds.ToArray());packet.Arguments.SetData(arguments);
                return packet;
            }
            catch { packet.Dispose(); throw; }
        }
        private static bool SupportedMaterial(Material material) => material!=null && material.shader!=null && material.shader.isSupported &&
            material.GetTag("CullingSystemDOTSIndirect",false,"")=="1" && material.renderQueue<=2500 &&
            !material.IsKeywordEnabled("_ALPHATEST_ON") && material.FindPass("Forward")>=0 && material.FindPass("ShadowCaster")>=0 &&
            (!material.HasProperty("_Surface") || material.GetFloat("_Surface")==0);
        public void Dispose()
        {
            if(Thread.CurrentThread.ManagedThreadId!=thread) throw new InvalidOperationException("Indirect packets belong to their creating thread.");
            Models?.Dispose();Candidates?.Dispose();Ids?.Dispose();GroupIds?.Dispose();Arguments?.Dispose();
            Models=Candidates=Ids=GroupIds=Arguments=null;
            if(reserved>0) {memory.Release(reserved);reserved=0;}
        }
    }
}
