using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace CullingSystemDOTS.Samples.DynamicCity
{
    public struct DynamicCityAgent : IComponentData
    {
        public uint Tick;
        public float3 Home;
        public float Elapsed,Phase;
        public byte Upper;
    }
    [DisableAutoCreation]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class DynamicCityAgents : SystemBase
    {
        public bool Paused;
        protected override void OnUpdate(){if(!Paused)Schedule((float)World.Time.DeltaTime);}
        private void Schedule(float delta)
        {if(delta>0)Dependency=new Move {Delta=delta}.ScheduleParallel(Dependency);}
        public void Advance(float delta){Dependency.Complete();if(delta>0)new Move {Delta=delta}.Run();}
        internal static float4x4 Pose(DynamicCityAgent agent)
        {
            float phase=agent.Elapsed*.6f+agent.Phase;int segment=(int)math.floor(phase)%6;float blend=math.frac(phase);
            float3 position=math.lerp(Point(segment,agent.Upper),Point((segment+1)%6,agent.Upper),blend)+agent.Home;
            return float4x4.TRS(position,quaternion.identity,new float3(.35f,1.2f,.35f));
        }
        private static float3 Point(int index,byte upper)
        {
            float y=upper!=0?4:.75f;
            switch(index)
            {
                case 0:return new float3(-1.5f,y,upper!=0?-4:-10);
                case 1:return new float3(-1.5f,y,-3);
                case 2:return new float3(3,y,-3);
                case 3:return new float3(3,y,3);
                case 4:return new float3(-1.5f,y,3);
                default:return new float3(-1.5f,y,upper!=0?-3:-10);
            }
        }
        [BurstCompile]
        private partial struct Move : IJobEntity
        {
            public float Delta;
            private void Execute(ref DynamicCityAgent agent,ref LocalToWorld pose)
            {agent.Elapsed+=Delta;agent.Tick++;pose.Value=Pose(agent);}
        }
    }
}
