using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace CullingSystemDOTS.Dynamic
{
    /// <summary>Inclusive minimum/exclusive maximum pixels at the reference sample grid.</summary>
    public struct ProjectedBounds
    {
        public int2 Min, Max;
        public float NearestDepth;
        public bool Valid;
    }
    /// <summary>Borrowed, single-sample positive linear eye depth. This value never owns its array.</summary>
    public readonly struct ReferenceDepthImage
    {
        public readonly NativeArray<float>.ReadOnly Depth;
        public readonly int2 Size;
        public readonly float FarDepth;
        public ReferenceDepthImage(NativeArray<float> depth, int2 size, float farDepth)
        { Depth = depth.IsCreated ? depth.AsReadOnly() : default; Size = size; FarDepth = farDepth; }
    }
    public static class OcclusionReference
    {
        public static ProjectedBounds Project(in Bounds bounds, Matrix4x4 view, Matrix4x4 projection, int2 size, float nearPlane)
        {
            if (math.any(size <= 0) || math.any(size > 1048576) || !OcclusionValidity.Finite(nearPlane) || nearPlane <= 0 ||
                !OcclusionValidity.MatrixFinite(view) || !OcclusionValidity.MatrixFinite(projection) ||
                view.m30 != 0 || view.m31 != 0 || view.m32 != 0 || view.m33 != 1 ||
                !math.all(math.isfinite((float3)bounds.center)) || !math.all(math.isfinite((float3)bounds.extents)) || math.any((float3)bounds.extents < 0)) return default;
            float2 minimum = new float2(float.PositiveInfinity), maximum = new float2(float.NegativeInfinity);
            float nearest = float.PositiveInfinity;
            for (int i = 0; i < 8; i++)
            {
                var sign = new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1);
                Vector3 corner = bounds.center + Vector3.Scale(bounds.extents, sign);
                Vector4 eye = view * new Vector4(corner.x, corner.y, corner.z, 1);
                Vector4 clip = projection * eye;
                if (!math.all(math.isfinite((float4)eye)) || !math.all(math.isfinite((float4)clip)) || -eye.z <= nearPlane || clip.w <= 0) return default;
                float2 uv = new float2(clip.x, clip.y) / clip.w * .5f + .5f;
                if (!math.all(math.isfinite(uv))) return default;
                minimum = math.min(minimum, uv); maximum = math.max(maximum, uv); nearest = math.min(nearest, -eye.z);
            }
            if (math.any(maximum < 0) || math.any(minimum > 1)) return default;
            // A guard pixel also covers finite projection/raster rounding differences. It can
            // reduce culling but cannot shrink a visible silhouette or fill a thin opening.
            var min = math.max(0, (int2)math.floor(math.clamp(minimum, 0, 1) * size) - 1);
            var max = math.min(size, (int2)math.ceil(math.clamp(maximum, 0, 1) * size) + 1);
            return new ProjectedBounds { Min = min, Max = max, NearestDepth = nearest, Valid = math.all(max > min) };
        }
        /// <summary>Exhaustive single-sample oracle. Bias scales by max(1, linear depth) toward visibility.</summary>
        public static OcclusionVisibility Test(in ProjectedBounds bounds, in ReferenceDepthImage image, float bias = 0.001f)
        {
            if (!bounds.Valid || !image.Depth.IsCreated || math.any(image.Size <= 0) || (long)image.Size.x * image.Size.y != image.Depth.Length ||
                !OcclusionValidity.Finite(image.FarDepth) || image.FarDepth <= 0 || !OcclusionValidity.Finite(bias) || bias < 0 ||
                !OcclusionValidity.Finite(bounds.NearestDepth) || bounds.NearestDepth <= 0 || math.any(bounds.Min < 0) ||
                math.any(bounds.Max > image.Size) || math.any(bounds.Max <= bounds.Min)) return OcclusionVisibility.PotentiallyVisible;
            for (int y = bounds.Min.y; y < bounds.Max.y; y++)
            for (int x = bounds.Min.x; x < bounds.Max.x; x++)
            {
                float depth = image.Depth[y * image.Size.x + x];
                if (!OcclusionValidity.Finite(depth) || depth <= 0 || depth >= image.FarDepth ||
                    !(bounds.NearestDepth - depth > bias * math.max(1, depth))) return OcclusionVisibility.PotentiallyVisible;
            }
            return OcclusionVisibility.Occluded;
        }
    }
}
