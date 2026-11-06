using CullingSystemDOTS.Dynamic.Ecs;
using NUnit.Framework;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;

namespace CullingSystemDOTS.Dynamic.Ecs.Tests
{
    public class DynamicCaptureDependencyTests
    {
        [Test] public void CaptureCompletesExternalParallelTransformWriters()
        {
            var world=new World("Capture dependency regression");var mesh=new Mesh {vertices=new[]{Vector3.zero,Vector3.right,Vector3.up},triangles=new[]{0,1,2}};
            try
            {
                var system=world.GetOrCreateSystemManaged<DynamicOcclusionSystem>();var geometry=system.Registry.RegisterGeometry(mesh);
                var entity=world.EntityManager.CreateEntity(typeof(LocalToWorld),typeof(MaterialMeshInfo));
                world.EntityManager.SetComponentData(entity,new LocalToWorld {Value=float4x4.identity});
                system.Bindings.Bind(entity,new OcclusionInstanceDescriptor {Geometry=geometry,State=new OcclusionInstanceState {Roles=OcclusionRole.Occluder,Opaque=true}});
                world.GetOrCreateSystemManaged<DynamicCaptureParallelWriter>().Update();
                Assert.DoesNotThrow(()=>system.Update());
                using(var snapshot=system.Registry.AcquireSnapshot())Assert.That(snapshot.Instances[0].LocalToWorld.c3.x,Is.EqualTo(9));
            }
            finally{world.Dispose();Object.DestroyImmediate(mesh);}
        }
    }
    [DisableAutoCreation]
    public partial class DynamicCaptureParallelWriter : SystemBase
    {
        protected override void OnUpdate(){Dependency=new Write().ScheduleParallel(Dependency);}
        private partial struct Write : IJobEntity
        {private void Execute(ref LocalToWorld pose){pose.Value.c3.x=9;}}
    }
}
