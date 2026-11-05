using PerfectCullingDOTS.Authoring;
using UnityEngine;

namespace PerfectCullingDOTS.Editor.Baking
{
    public static class MaterialClassification
    {
        public static bool IsTransparent(Material material, bool enabled, BakeSurfaceOverride forced = BakeSurfaceOverride.Automatic)
        {
            if (forced == BakeSurfaceOverride.Opaque) return false;
            if (forced == BakeSurfaceOverride.Transparent) return true;
            if (material == null || !enabled) return false;
            string name = material.name.ToLower();
            if (name.Contains("pc_trans")) return true;
            if (name.Contains("pc_opaque")) return false;
            if (material.IsKeywordEnabled("_ALPHATEST_ON") || material.IsKeywordEnabled("ALPHACLIPPING_ON")) return true;
            return material.renderQueue >= 2450;
        }
    }
}
