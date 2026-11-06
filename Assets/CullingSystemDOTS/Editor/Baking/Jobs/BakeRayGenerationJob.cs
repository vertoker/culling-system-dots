using Unity.Mathematics;
namespace CullingSystemDOTS.Editor.Baking.Jobs
{
    // Generated in the traversal tile, avoiding a batch*cubemap-pixels ray allocation.
    internal static class BakeRayGeneration
    {
        internal const int TileHeight = 8;
        internal static BakeRay Ray(in BakeRaySettings settings, float3 origin, int face, int x, int y) =>
            ReferenceCpuTraversal.GenerateRay(in settings,origin,face,x,y);
    }
}
