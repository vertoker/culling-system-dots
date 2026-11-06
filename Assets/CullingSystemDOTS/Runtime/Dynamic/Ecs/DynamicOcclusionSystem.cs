using Unity.Entities;
using Unity.Rendering;

namespace CullingSystemDOTS.Dynamic.Ecs
{
    [DisableAutoCreation]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    [UpdateAfter(typeof(UpdatePresentationSystemGroup))]
    [UpdateBefore(typeof(EntitiesGraphicsSystem))]
    public partial class DynamicOcclusionSystem : SystemBase
    {
        public OcclusionWorld Registry { get; private set; }
        public DynamicOcclusionBindings Bindings { get; private set; }
        /// <summary>Configure once before registering geometry or instances. Existing registries cannot be replaced live.</summary>
        public void Configure(OcclusionSettings settings)
        {
            if (Registry.Revision != 1) throw new System.InvalidOperationException("Configure before registering geometry or instances.");
            if (settings == null) throw new System.ArgumentNullException(nameof(settings));
            var next = new OcclusionWorld(settings);
            DynamicOcclusionBindings bindings;
            try { bindings = new DynamicOcclusionBindings(World, next); }
            catch { next.Dispose(); throw; }
            Bindings.Dispose(); Registry.Dispose(); Registry = next; Bindings = bindings;
        }
        protected override void OnCreate()
        {
            Registry = new OcclusionWorld();
            Bindings = new DynamicOcclusionBindings(World, Registry);
        }
        protected override void OnUpdate()
        {
            Bindings.Capture();
            Registry.ProcessUpdates(new OcclusionUpdateBudget(int.MaxValue, int.MaxValue));
        }
        protected override void OnDestroy()
        {
            Bindings?.Dispose();
            Registry?.Dispose();
        }
    }
}
