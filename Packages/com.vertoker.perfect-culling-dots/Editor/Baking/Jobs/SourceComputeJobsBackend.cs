namespace PerfectCullingDOTS.Editor.Baking.Jobs
{
    public sealed class SourceComputeJobsBackend : IBakeBackend
    {
        public string Name => "CPU Jobs/Burst (source CWBVH)";
        public IBakeSession Begin(BakeSnapshot snapshot,BakeBackendOptions options = default) => new BakeJobHandle(snapshot,options,true);
    }
}
