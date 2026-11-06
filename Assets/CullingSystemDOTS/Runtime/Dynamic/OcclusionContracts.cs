using System;
using System.Runtime.CompilerServices;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

[assembly: InternalsVisibleTo("CullingSystemDOTS.Dynamic.GPU")]
[assembly: InternalsVisibleTo("CullingSystemDOTS.Dynamic.ECS")]
[assembly: InternalsVisibleTo("CullingSystemDOTS.Dynamic.Tests")]

namespace CullingSystemDOTS.Dynamic
{
    public enum OcclusionVisibility : byte { PotentiallyVisible, Occluded, Unavailable }
    public enum OcclusionFallbackReason : byte
    {
        None, StaleView, StaleGeometry, StaleInstance, UnsupportedView, UnsupportedHardware,
        UnsupportedMaterial, MissingSetup, ResourceBudget, UploadPending, InvalidProjection, Disposed
    }
    [Flags] public enum OcclusionRole : byte { None = 0, Occluder = 1, Occludee = 2, Renderable = 4 }

    public readonly struct GeometryHandle : IEquatable<GeometryHandle>
    {
        public readonly ulong World;
        public readonly int Index;
        public readonly uint Generation;
        internal GeometryHandle(ulong world, int index, uint generation) { World = world; Index = index; Generation = generation; }
        public bool IsValid => World != 0 && Index >= 0 && Generation != 0;
        public bool Equals(GeometryHandle other) => World == other.World && Index == other.Index && Generation == other.Generation;
        public override bool Equals(object other) => other is GeometryHandle handle && Equals(handle);
        public override int GetHashCode() => unchecked(((int)World * 397 ^ Index) * 397 ^ (int)Generation);
    }

    public readonly struct InstanceHandle : IEquatable<InstanceHandle>
    {
        public readonly ulong World;
        public readonly int Index;
        public readonly uint Generation;
        internal InstanceHandle(ulong world, int index, uint generation) { World = world; Index = index; Generation = generation; }
        public bool IsValid => World != 0 && Index >= 0 && Generation != 0;
        public bool Equals(InstanceHandle other) => World == other.World && Index == other.Index && Generation == other.Generation;
        public override bool Equals(object other) => other is InstanceHandle handle && Equals(handle);
        public override int GetHashCode() => unchecked(((int)World * 397 ^ Index) * 397 ^ (int)Generation);
    }

    public struct OcclusionInstanceState
    {
        public float4x4 LocalToWorld;
        public Bounds WorldBounds;
        public float4 Color;
        public OcclusionRole Roles;
        public bool Opaque;
    }

    public struct OcclusionInstanceDescriptor
    {
        public GeometryHandle Geometry;
        public OcclusionInstanceState State;
        public Material Material;
        public int SubMesh, Layer;
        public ShadowCastingMode Shadows;
    }

    public struct OcclusionInstanceSnapshot
    {
        public InstanceHandle Handle;
        public GeometryHandle Geometry;
        public float4x4 LocalToWorld;
        public Bounds WorldBounds;
        public float4 Color;
        public uint BoundsRevision;
        public OcclusionRole Roles;
        public byte Opaque;
        public int SubMesh, Layer;
        public ShadowCastingMode Shadows;
    }

    /// <summary>Exact view identity. Providers supply current matrices and actual target dimensions.</summary>
    public struct OcclusionViewSignature : IEquatable<OcclusionViewSignature>
    {
        public ulong ViewId;
        public Matrix4x4 View, Projection, NonJitteredProjection;
        public Rect Viewport;
        public int2 TargetSize;
        public float RenderScale;
        public int Eye;
        public bool Orthographic;
        public bool Equals(OcclusionViewSignature other) => ViewId == other.ViewId && View.Equals(other.View) &&
            Projection.Equals(other.Projection) && NonJitteredProjection.Equals(other.NonJitteredProjection) &&
            Viewport.Equals(other.Viewport) && TargetSize.Equals(other.TargetSize) && RenderScale.Equals(other.RenderScale) &&
            Eye == other.Eye && Orthographic == other.Orthographic;
        public override bool Equals(object other) => other is OcclusionViewSignature view && Equals(view);
        public override int GetHashCode() => ViewId.GetHashCode();
    }

    public struct OcclusionResultStamp
    {
        public OcclusionViewSignature View;
        public ulong OccluderEpoch;
        public InstanceHandle Instance;
        public uint BoundsRevision;
        public OcclusionVisibility Visibility;
    }

    public static class OcclusionValidity
    {
        public static OcclusionVisibility Resolve(in OcclusionResultStamp result, in OcclusionViewSignature view,
            ulong epoch, InstanceHandle instance, uint boundsRevision)
        {
            if (!instance.IsValid || epoch == 0 || boundsRevision == 0 || !ValidView(view) ||
                !result.View.Equals(view) || result.OccluderEpoch != epoch || !result.Instance.Equals(instance) ||
                result.BoundsRevision != boundsRevision || (uint)result.Visibility > (uint)OcclusionVisibility.Unavailable)
                return OcclusionVisibility.PotentiallyVisible;
            return result.Visibility;
        }

        private static bool ValidView(in OcclusionViewSignature view) => view.ViewId != 0 &&
            view.TargetSize.x > 0 && view.TargetSize.y > 0 && Finite(view.RenderScale) && view.RenderScale > 0 &&
            Finite(view.Viewport.x) && Finite(view.Viewport.y) && Finite(view.Viewport.width) && Finite(view.Viewport.height) &&
            view.Viewport.width > 0 && view.Viewport.height > 0 && MatrixFinite(view.View) && MatrixFinite(view.Projection) && MatrixFinite(view.NonJitteredProjection);
        internal static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        internal static bool MatrixFinite(Matrix4x4 matrix)
        {
            for (int i = 0; i < 16; i++) if (!Finite(matrix[i])) return false;
            return true;
        }
    }
}
