using System;
using Unity.Mathematics;
namespace CullingSystemDOTS.Editor.Baking
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
            foreach (var vertex in snapshot.Vertices)
                if (!math.all(math.isfinite(vertex))) throw new ArgumentException("Bake mesh vertices must be finite. No visibility data was published.");
            foreach (var geometry in snapshot.Geometry)
            {
                if (geometry.VertexOffset < 0 || geometry.VertexCount < 0 || geometry.VertexOffset > snapshot.Vertices.Length ||
                    geometry.VertexCount > snapshot.Vertices.Length - geometry.VertexOffset || geometry.IndexOffset < 0 || geometry.IndexCount < 0 ||
                    geometry.IndexOffset > snapshot.Indices.Length || geometry.IndexCount > snapshot.Indices.Length - geometry.IndexOffset ||
                    geometry.IndexOffset % 3 != 0 || geometry.IndexCount % 3 != 0)
                    throw new ArgumentException("Bake geometry requires valid vertex and triangle ranges.");
                for (int i = geometry.IndexOffset; i < geometry.IndexOffset + geometry.IndexCount; i++)
                    if ((uint)snapshot.Indices[i] >= (uint)geometry.VertexCount) throw new ArgumentException("Bake triangle index is outside its geometry.");
            }
            foreach (var instance in snapshot.Instances)
                if ((uint)instance.Geometry >= (uint)snapshot.Geometry.Length || instance.GroupIndex < -1 || instance.GroupIndex >= snapshot.Groups.Length ||
                    instance.Transparent > 1 || instance.DoubleSided > 1 || !ValidTransform(instance.LocalToWorld) || !ValidTransform(instance.WorldToLocal) ||
                    !math.all(math.isfinite(instance.BoundsMin)) || !math.all(math.isfinite(instance.BoundsMax)) || !math.all(instance.BoundsMin <= instance.BoundsMax))
                    throw new ArgumentException("Bake instances require finite invertible affine transforms, ordered finite bounds and valid geometry/group indices.");
        }
        private static bool ValidTransform(float4x4 matrix)
        {
            if (!math.all(math.isfinite(matrix.c0)) || !math.all(math.isfinite(matrix.c1)) ||
                !math.all(math.isfinite(matrix.c2)) || !math.all(math.isfinite(matrix.c3)) ||
                matrix.c0.w != 0 || matrix.c1.w != 0 || matrix.c2.w != 0 || matrix.c3.w != 1) return false;
            // Scaling each axis independently avoids determinant underflow/overflow
            // for otherwise finite small/large affine transforms.
            float x = math.cmax(math.abs(matrix.c0.xyz)), y = math.cmax(math.abs(matrix.c1.xyz)), z = math.cmax(math.abs(matrix.c2.xyz));
            return x > 0 && y > 0 && z > 0 && math.dot(matrix.c0.xyz / x, math.cross(matrix.c1.xyz / y, matrix.c2.xyz / z)) != 0;
        }
    }
}
