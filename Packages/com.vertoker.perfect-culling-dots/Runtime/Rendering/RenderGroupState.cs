using PerfectCullingDOTS.Core;
using Unity.Entities;
using UnityEngine.Rendering;

namespace PerfectCullingDOTS.Rendering
{
    public enum CullingRenderToggleMode { RendererComponent, ShadowcastMode, ForceRenderingOff }

    public struct RenderVolumeRange
    {
        public int GroupOffset;
        public int GroupCount;
    }

    public struct RenderGroupBinding
    {
        public CullingGroupKey Group;
        public Entity Main;
        public Entity ShadowOnly;
        public ShadowCastingMode OriginalShadowMode;
        public CullingRenderToggleMode ToggleMode;
        public bool InitialRendererEnabled;
        public bool InitialForceRenderingOff;
    }

    public struct RenderGroupState
    {
        public bool Main;
        public bool ShadowOnly;

        public static RenderGroupState Evaluate(bool visible, in RenderGroupBinding binding)
        {
            switch (binding.ToggleMode)
            {
                case CullingRenderToggleMode.RendererComponent:
                    return new RenderGroupState { Main = visible && !binding.InitialForceRenderingOff };
                case CullingRenderToggleMode.ForceRenderingOff:
                    return new RenderGroupState { Main = visible && binding.InitialRendererEnabled };
                case CullingRenderToggleMode.ShadowcastMode:
                    if (binding.OriginalShadowMode == ShadowCastingMode.Off || binding.OriginalShadowMode == ShadowCastingMode.TwoSided)
                        return new RenderGroupState { Main = visible && binding.InitialRendererEnabled };
                    bool active = binding.InitialRendererEnabled && !binding.InitialForceRenderingOff;
                    return new RenderGroupState {
                        Main = active && (visible || binding.OriginalShadowMode == ShadowCastingMode.ShadowsOnly),
                        ShadowOnly = active && !visible && binding.OriginalShadowMode == ShadowCastingMode.On
                    };
                default:
                    throw new System.ArgumentOutOfRangeException(nameof(binding.ToggleMode));
            }
        }
    }
}
