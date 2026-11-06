using Unity.Mathematics;
namespace CullingSystemDOTS.Authoring
{
    // Main-thread bake extension; its results are copied into the immutable sample snapshot.
    public interface ICullingSamplingProvider
    {
        void Initialize();
        bool IsActive(float3 worldPosition);
    }
}
