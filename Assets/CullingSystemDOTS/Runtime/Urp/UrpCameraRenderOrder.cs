using System.Collections.Generic;
using CullingSystemDOTS.Runtime;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
namespace CullingSystemDOTS.URP
{
    /// <summary>URP17.6 callback order: sorted bases followed by each base's active
    /// compatible overlays. This runs before URP sorts/expands its camera list.</summary>
    public static class UrpCameraRenderOrder
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Install() => CullingCameraDriver.AutomaticRenderOrderResolver = Resolve;
        public static Camera[] Resolve(IReadOnlyList<Camera> cameras)
        {
            var sorted = new List<Camera>(cameras.Count);
            for (int i = 0; i < cameras.Count; i++) sorted.Add(cameras[i]);
            if (!(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset)) return sorted.ToArray();
            // Match the installed URP source, including its integer depth casts.
            sorted.Sort((a,b)=>(int)a.depth-(int)b.depth);
            var order = new List<Camera>();
            foreach (var camera in sorted)
            {
                bool game = camera.cameraType == CameraType.Game || camera.cameraType == CameraType.VR;
                camera.TryGetComponent<UniversalAdditionalCameraData>(out var data);
                if (game && data != null && data.renderType == CameraRenderType.Overlay) continue;
                order.Add(camera);
                if (!game || data == null) continue;
                var renderer = data.scriptableRenderer;
                if (renderer == null || !renderer.SupportsCameraStackingType(CameraRenderType.Base)) continue;
                var stack = data.cameraStack; if (stack == null) continue;
                foreach (var overlay in stack)
                {
                    if (overlay == null || !overlay.isActiveAndEnabled || !overlay.TryGetComponent<UniversalAdditionalCameraData>(out var overlayData) ||
                        overlayData.renderType != CameraRenderType.Overlay) continue;
                    var overlayRenderer = overlayData.scriptableRenderer;
                    if (overlayRenderer == null || overlayRenderer.GetType() != renderer.GetType() || !overlayRenderer.SupportsCameraStackingType(CameraRenderType.Overlay)) continue;
                    order.Add(overlay);
                }
            }
            return order.ToArray();
        }
    }
}
