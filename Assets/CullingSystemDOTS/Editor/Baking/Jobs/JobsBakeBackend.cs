using System;
namespace CullingSystemDOTS.Editor.Baking.Jobs
{
    public sealed class JobsBakeBackend : IBakeBackend
    {
        public string Name => "CPU Jobs/Burst";
        public IBakeSession Begin(BakeSnapshot snapshot, BakeBackendOptions options = default) => new BakeJobHandle(snapshot,options);
    }
}
