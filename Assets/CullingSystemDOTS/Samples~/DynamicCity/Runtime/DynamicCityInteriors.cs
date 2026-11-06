using System.Collections.Generic;
using CullingSystemDOTS.Dynamic;
using Unity.Entities;
using Unity.Mathematics;

namespace CullingSystemDOTS.Samples.DynamicCity
{
    // Simple workshop fixtures stay away from the demonstrated agent corridors.
    internal static class DynamicCityInteriors
    {
        internal static Entity[] Build(DynamicCityBootstrap sample)
        {
            var entities=new List<Entity>();
            for(int building=0;building<sample.BuildingCount;building++)
            for(int floor=0;floor<2;floor++)
            {
                float3 origin=DynamicCityBootstrap.Home(building)+new float3(0,floor*3.25f,0);
                int frame=building+8,crate=building+3,machine=building+11;
                Add(sample,entities,origin+new float3(4.5f,1.1f,-1),new float3(2,.15f,.9f),frame);
                foreach(float x in new[]{3.8f,5.2f})Add(sample,entities,origin+new float3(x,.6f,-1),new float3(.15f,.9f,.75f),frame);
                foreach(float z in new[]{1.7f,3.9f})Add(sample,entities,origin+new float3(-4.5f,1.3f,z),new float3(.15f,2.25f,.15f),frame);
                foreach(float y in new[]{.55f,1.2f,1.85f})
                {
                    Add(sample,entities,origin+new float3(-4.5f,y,2.8f),new float3(.85f,.1f,2.5f),frame);
                    foreach(float z in new[]{2.1f,3.4f})Add(sample,entities,origin+new float3(-4.5f,y+.28f,z),new float3(.55f,.45f,.65f),crate);
                }
                Add(sample,entities,origin+new float3(4.3f,.25f,4.5f),new float3(1.5f,.2f,1.5f),frame);
                Add(sample,entities,origin+new float3(4.3f,1.05f,4.5f),new float3(1.25f,1.4f,1.25f),machine);
                Add(sample,entities,origin+new float3(4.3f,1.2f,3.8f),new float3(.7f,.45f,.12f),crate);
                Add(sample,entities,origin+new float3(-3.8f,.25f,-2),new float3(2,.15f,1),frame);
                foreach(float x in new[]{-4.3f,-3.3f})Add(sample,entities,origin+new float3(x,.72f,-2),new float3(.8f,.75f,.8f),crate);
            }
            return entities.ToArray();
        }
        private static void Add(DynamicCityBootstrap sample,List<Entity> entities,float3 center,float3 size,int color)
        {
            entities.Add(sample.CreateRenderEntity(float4x4.TRS(center,quaternion.identity,size),color,
                OcclusionRole.Renderable|OcclusionRole.Occludee));
        }
    }
}
