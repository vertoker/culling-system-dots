using System.Runtime.CompilerServices;
using UnityEngine;

[assembly: InternalsVisibleTo("CullingSystemDOTS.Dynamic.Rendering.Tests")]

namespace CullingSystemDOTS.Dynamic.Gpu
{
    /// <summary>Assign serialized shader references; runtime loading never assumes an installation path.</summary>
    public sealed class OcclusionShaderResources : ScriptableObject
    {
        public Shader Depth;
        public ComputeShader Pyramid, Bounds;
    }
}
