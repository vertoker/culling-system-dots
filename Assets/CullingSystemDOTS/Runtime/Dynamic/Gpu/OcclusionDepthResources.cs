using System;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace CullingSystemDOTS.Dynamic.Gpu
{
    // The submission owns fencing. Never dispose while a recorded command can still read a level.
    internal sealed class OcclusionDepthResources : IDisposable
    {
        internal readonly RenderTexture[] Levels;
        internal readonly int2 Size;
        private readonly OcclusionMemoryBudget budget;
        private long bytes;
        private bool disposed;
        internal OcclusionDepthResources(OcclusionWorld registry, int2 size, Func<RenderTextureDescriptor, RenderTexture> allocate = null)
        {
            if (registry == null) throw new ArgumentNullException(nameof(registry));
            if (math.any(size <= 0) || math.any(size > math.min(16384, SystemInfo.maxTextureSize))) throw new ArgumentOutOfRangeException(nameof(size));
            Size = size; budget = registry.ResourceBudget;
            int count = 1; var next = size;
            while (math.any(next > 1)) { next = (next + 1) / 2; count++; }
            Levels = new RenderTexture[count]; next = size;
            for (int i = 0; i < count; i++) { bytes = checked(bytes + (long)next.x * next.y * 4); next = (next + 1) / 2; }
            budget.Reserve(bytes);
            try
            {
                next = size;
                for (int i = 0; i < count; i++)
                {
                    var descriptor = new RenderTextureDescriptor(next.x, next.y) { graphicsFormat = GraphicsFormat.R32_SFloat,
                        depthStencilFormat = GraphicsFormat.None, msaaSamples = 1, enableRandomWrite = true, useMipMap = false, autoGenerateMips = false };
                    var texture = allocate != null ? allocate(descriptor) : new RenderTexture(descriptor);
                    Levels[i] = texture;
                    if (texture == null) throw new InvalidOperationException("Depth allocation returned no texture.");
                    texture.name = "Runtime occlusion linear depth " + i; texture.hideFlags = HideFlags.HideAndDontSave;
                    texture.filterMode = FilterMode.Point; texture.wrapMode = TextureWrapMode.Clamp;
                    if (!texture.Create()) throw new InvalidOperationException("Unable to create runtime depth texture.");
                    next = (next + 1) / 2;
                }
            }
            catch { Dispose(); throw; }
        }
        public void Dispose()
        {
            if (disposed) return; disposed = true;
            foreach (var texture in Levels) if (texture != null)
            { texture.Release(); if (Application.isPlaying) UnityEngine.Object.Destroy(texture); else UnityEngine.Object.DestroyImmediate(texture); }
            budget.Release(bytes); bytes = 0;
        }
    }
}
