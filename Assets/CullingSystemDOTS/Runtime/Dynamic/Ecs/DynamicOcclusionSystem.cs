using Unity.Entities;
using Unity.Rendering;

namespace CullingSystemDOTS.Dynamic.Ecs
{
    public interface IDynamicOcclusionPresentation : System.IDisposable
    {
        void Prepare();
        void Suspend();
    }
    [DisableAutoCreation]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    [UpdateAfter(typeof(UpdatePresentationSystemGroup))]
    [UpdateBefore(typeof(EntitiesGraphicsSystem))]
    public partial class DynamicOcclusionSystem : SystemBase
    {
        public OcclusionWorld Registry { get; private set; }
        public DynamicOcclusionBindings Bindings { get; private set; }
        public IDynamicOcclusionPresentation Presentation {get;private set;}
        public void AttachPresentation(IDynamicOcclusionPresentation presentation)
        {
            if(presentation==null)throw new System.ArgumentNullException(nameof(presentation));
            if(Presentation!=null)throw new System.InvalidOperationException("A dynamic World already has a presentation owner.");
            Presentation=presentation;
        }
        public void DetachPresentation(IDynamicOcclusionPresentation presentation) {if(ReferenceEquals(Presentation,presentation))Presentation=null;}
        /// <summary>Configure once before registering geometry or instances. Existing registries cannot be replaced live.</summary>
        public void Configure(OcclusionSettings settings)
        {
            if (Registry.Revision != 1 || Presentation!=null) throw new System.InvalidOperationException("Configure before registering geometry, instances or presentation.");
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
            Presentation?.Prepare();
        }
        protected override void OnStopRunning() {Presentation?.Suspend();}
        protected override void OnDestroy()
        {
            var presentation=Presentation;Presentation=null;presentation?.Dispose();
            Bindings?.Dispose();
            Registry?.Dispose();
        }
    }
}
