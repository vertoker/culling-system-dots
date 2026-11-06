namespace CullingSystemDOTS.Editor.Baking
{
    public enum BakeExecutionMode { Auto, Gpu, Cpu }
    public interface IBakeSessionDiagnostics
    {
        string BackendName { get; }
        string FallbackReason { get; }
    }
}
