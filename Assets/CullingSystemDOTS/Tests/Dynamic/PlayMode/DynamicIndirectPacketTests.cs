using System.Collections;
using CullingSystemDOTS.Dynamic.Gpu;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace CullingSystemDOTS.Dynamic.Rendering.Tests
{
    public class DynamicIndirectPacketTests
    {
        [UnityTest] public IEnumerator OnlyUploadedSupportedInstancesWithinPayloadBudgetEnterThePacket()
        {
            #if UNITY_EDITOR
            var resources=UnityEditor.AssetDatabase.LoadAssetAtPath<OcclusionShaderResources>(UnityEditor.AssetDatabase.GUIDToAssetPath("2a976d8c5c1e1c9459592e9215f498d3"));
            var cube=GameObject.CreatePrimitive(PrimitiveType.Cube); cube.SetActive(false);
            var material=new Material(resources.Indirect); var other=new Material(resources.Indirect);
            try
            {
                using(var registry=new OcclusionWorld())
                using(var cache=new OcclusionGpuGeometryCache(registry))
                {
                    var geometry=registry.RegisterGeometry(cube.GetComponent<MeshFilter>().sharedMesh);
                    var handles=new InstanceHandle[4];
                    for(int i=0;i<4;i++) handles[i]=registry.RegisterInstance(new OcclusionInstanceDescriptor {Geometry=geometry,Material=i==1?other:material,
                        State=new OcclusionInstanceState {LocalToWorld=float4x4.Translate(new float3(i,0,0)),WorldBounds=new Bounds(new Vector3(i,0,0),Vector3.one),
                            Color=new float4(i,0,0,1),Roles=OcclusionRole.Renderable|OcclusionRole.Occludee,Opaque=true}});
                    registry.ProcessUpdates(new OcclusionUpdateBudget(int.MaxValue,int.MaxValue));
                    using(var snapshot=registry.AcquireSnapshot())
                    {
                        var budget=new OcclusionGpuUploadBudget(262144); budget.BeginFrame(1);
                        using(var pending=cache.Acquire(snapshot))
                            Assert.That(OcclusionIndirectPacket.Create(registry,snapshot,pending,resources,budget),Is.Null,"No ownership before geometry upload.");
                        cache.Process(snapshot,budget);
                        using(var meshes=cache.Acquire(snapshot))
                        {
                            var small=new OcclusionGpuUploadBudget(280); small.BeginFrame(2);
                            using(var packet=OcclusionIndirectPacket.Create(registry,snapshot,meshes,resources,small))
                            {
                                Assert.That(packet,Is.Not.Null); Assert.That(packet.Handles,Is.EqualTo(new[]{handles[0],handles[1]}));
                                Assert.That(small.Used,Is.EqualTo(280)); Assert.That(packet.GroupCount,Is.EqualTo(2));
                                var args=new GraphicsBuffer.IndirectDrawIndexedArgs[2]; packet.Arguments.GetData(args);
                                Assert.That(args[0].instanceCount,Is.EqualTo(1)); Assert.That(args[1].startInstance,Is.EqualTo(1));
                                var models=new OcclusionGpuInstanceData[2]; packet.Models.GetData(models);
                                Assert.That(models[1].Color.x,Is.EqualTo(1)); Assert.That(models[1].LocalToWorld.c3.x,Is.EqualTo(1));
                                small.BeginFrame(3);
                                var reuse=OcclusionIndirectPacket.Create(registry,snapshot,meshes,resources,small,packet);
                                try { Assert.That(reuse,Is.SameAs(packet),"A GPU-completed slot reuses its matching payload buffers."); }
                                finally {if(!ReferenceEquals(reuse,packet))reuse?.Dispose();}
                            }
                            using(var complete=OcclusionIndirectPacket.Create(registry,snapshot,meshes,resources,budget))
                            using(var visible=new GraphicsBuffer(GraphicsBuffer.Target.Structured,4,4))
                            using(var output=new GraphicsBuffer(GraphicsBuffer.Target.Structured,4,4))
                            using(var args=new GraphicsBuffer(GraphicsBuffer.Target.Raw|GraphicsBuffer.Target.IndirectArguments|GraphicsBuffer.Target.CopyDestination,2,GraphicsBuffer.IndirectDrawIndexedArgs.size))
                            using(var command=new CommandBuffer())
                            {
                                // Source order A,B,A,A differs from the full-draw permutation A,A,A,B.
                                // Visibility and group membership are indexed by model, not by that permutation.
                                visible.SetData(new uint[]{1,1,0,0});command.CopyBuffer(complete.Arguments,args);
                                OcclusionIndirectCommands.Compact(command,resources.IndirectArguments,visible,complete.Ids,complete.GroupIds,args,output,4,2);
                                Graphics.ExecuteCommandBuffer(command);
                                var compacted=new uint[4];output.GetData(compacted);
                                var counts=new GraphicsBuffer.IndirectDrawIndexedArgs[2];args.GetData(counts);
                                Assert.That(counts[0].instanceCount,Is.EqualTo(1));Assert.That(counts[1].instanceCount,Is.EqualTo(1));
                                Assert.That(compacted[0],Is.EqualTo(0));Assert.That(compacted[3],Is.EqualTo(1),"The B command must retain B's model index.");
                            }
                            material.renderQueue=3000;
                            var next=new OcclusionGpuUploadBudget(262144); next.BeginFrame(3);
                            using(var supported=OcclusionIndirectPacket.Create(registry,snapshot,meshes,resources,next))
                                Assert.That(supported.Handles,Is.EqualTo(new[]{handles[1]}),"Transparent bindings must remain stock-rendered.");
                        }
                    }
                }
            }
            finally {UnityEngine.Object.Destroy(cube);UnityEngine.Object.Destroy(material);UnityEngine.Object.Destroy(other);OcclusionGpuRetirement.DrainForShutdown();}
            yield return null;
            #else
            throw new System.NotSupportedException("This Editor fixture requires serialized Player shader references.");
            #endif
        }
    }
}
