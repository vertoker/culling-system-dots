using System;
using Unity.Mathematics;
namespace PerfectCullingDOTS.Editor.Baking
{
    internal static class BakeNumericValidation
    {
        public static void Validate(BakeSnapshot snapshot)
        {
            var rays = snapshot.Rays;
            if (rays.Width <= 0 || rays.Height <= 0 || !math.isfinite(rays.Far) || rays.Far <= 0 ||
                !math.all(math.isfinite(rays.CellSize)) || !math.all(math.isfinite(rays.Rotation.value)) ||
                math.abs(math.lengthsq(rays.Rotation.value)-1f) > .001f)
                throw new ArgumentException("Bake rays require positive dimensions/far distance, finite cell sizes and a unit rotation quaternion.");
            foreach (var sample in snapshot.Samples)
                if (!math.all(math.isfinite(sample.Position))) throw new ArgumentException("Bake sample positions must be finite. No visibility data was published.");
        }
    }
}
