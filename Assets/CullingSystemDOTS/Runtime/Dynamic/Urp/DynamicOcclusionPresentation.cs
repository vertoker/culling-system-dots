using System;
using System.Collections.Generic;
using CullingSystemDOTS.Dynamic.Ecs;
using CullingSystemDOTS.Dynamic.Gpu;
using Unity.Entities;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace CullingSystemDOTS.Dynamic.Urp
{
    /// <summary>Opt-in rigid opaque indirect rendering. Configure the System before attaching this owner.</summary>
    public sealed class DynamicOcclusionPresentation : IDynamicOcclusionPresentation
    {
        public DynamicOcclusionSystem System {get;}
        private readonly OcclusionShaderResources resources;
        private readonly HashSet<Camera> views=new HashSet<Camera>();
        private OcclusionIndirectRenderer renderer;
        private Camera[] cameras=new Camera[16];
        private bool enabled=true,disposed;
        public bool Enabled {get=>enabled;set{CheckAlive();enabled=value;if(!value)Suspend();}}
        public string LastFault {get;private set;}
        public int ReadyInstances {get;private set;}
        public int UploadedBytes => renderer?.UploadedBytes??0;
        public int InFlightSlots => renderer?.SlotCount??0;
        public long AccountedResourceBytes => System.Registry.AccountedResourceBytes;
        public DynamicOcclusionPresentation(DynamicOcclusionSystem system,OcclusionShaderResources resources)
        {
            System=system??throw new ArgumentNullException(nameof(system));this.resources=resources;
            System.AttachPresentation(this);
            // This guard precedes the GPU owner's camera callback, including after lazy creation.
            RenderPipelineManager.beginCameraRendering+=ValidatePublication;
        }
        public static DynamicOcclusionPresentation Install(World world,OcclusionShaderResources resources,OcclusionSettings settings=null)
        {
            if(world==null || !world.IsCreated)throw new ArgumentException("A live ECS World is required.",nameof(world));
            var system=world.GetOrCreateSystemManaged<DynamicOcclusionSystem>();if(settings!=null)system.Configure(settings);
            var presentation=new DynamicOcclusionPresentation(system,resources);
            try
            {
                var group=world.GetOrCreateSystemManaged<PresentationSystemGroup>();group.AddSystemToUpdateList(system);group.SortSystems();return presentation;
            }
            catch {presentation.Dispose();throw;}
        }
        public void RegisterView(Camera camera)
        {
            CheckAlive();if(camera==null)throw new ArgumentNullException(nameof(camera));
            if(!views.Contains(camera) && views.Count>=4)throw new InvalidOperationException("Register up to four occluding cameras. Other cameras retain full rendering.");
            views.Add(camera);renderer?.RegisterView(camera);
        }
        public void UnregisterView(Camera camera){CheckAlive();views.Remove(camera);renderer?.UnregisterView(camera);}
        public OcclusionFallbackReason ViewFallback(Camera camera) => renderer?.ViewFallback(camera)??OcclusionFallbackReason.MissingSetup;
        public void Prepare()
        {
            CheckAlive();LastFault=null;
            if(!enabled){Suspend();return;}
            string reason=null;
            if(views.Count==0 || !SupportedPipeline(out reason)){LastFault=views.Count==0?"Register an occluding camera.":reason;Suspend();return;}
            try
            {
                if(renderer==null)
                {
                    renderer=new OcclusionIndirectRenderer(System.Registry,resources);foreach(var camera in views)if(camera!=null)renderer.RegisterView(camera);
                }
                var handles=renderer.PrepareFrame((ulong)(uint)Time.frameCount+1);
                System.Bindings.ApplyGraphicsOwnership(handles);ReadyInstances=handles.Count;LastFault=renderer.LastFault;
            }
            catch(Exception exception)when(exception is ArgumentException || exception is InvalidOperationException || exception is NotSupportedException || exception is UnityException)
            {LastFault=exception.Message;Suspend();}
        }
        public void Suspend()
        {
            if(disposed)return;
            renderer?.Suspend();System.Bindings.ApplyGraphicsOwnership(Array.Empty<InstanceHandle>());ReadyInstances=0;
        }
        private void ValidatePublication(ScriptableRenderContext context,Camera camera)
        {
            if(disposed || renderer==null || ReadyInstances==0)return;
            if(renderer.PublicationRevision!=System.Registry.Revision){LastFault="Registry changed after presentation capture; stock rendering restored.";Suspend();}
            else if(!SupportedPipeline(out var reason)){LastFault=reason;Suspend();}
        }
        private bool SupportedPipeline(out string reason)
        {
            var pipeline=GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if(pipeline==null || pipeline.renderScale!=1){reason="Use URP 17.6 at render scale 1; other pipelines/scales retain stock rendering.";return false;}
            int count=Camera.allCamerasCount;if(cameras.Length<count)Array.Resize(ref cameras,Mathf.NextPowerOfTwo(count));
            count=Camera.GetAllCameras(cameras);
            for(int i=0;i<count;i++)
            {
                var camera=cameras[i];if(camera==null || camera.cameraType!=CameraType.Game)continue;
                if(camera.stereoEnabled || camera.allowDynamicResolution || !camera.projectionMatrix.Equals(camera.nonJitteredProjectionMatrix))
                {reason="Stereo, dynamic resolution and temporal jitter retain stock rendering.";return false;}
                if(camera.TryGetComponent<UniversalAdditionalCameraData>(out var data) &&
                    (data.antialiasing==AntialiasingMode.TemporalAntiAliasing || data.renderType==CameraRenderType.Overlay ||
                        (data.renderType==CameraRenderType.Base && data.cameraStack!=null && data.cameraStack.Count>0)))
                {reason="TAA and camera stacks retain stock rendering.";return false;}
            }
            reason=null;return true;
        }
        public void Dispose()
        {
            if(disposed)return;Suspend();disposed=true;
            RenderPipelineManager.beginCameraRendering-=ValidatePublication;renderer?.Dispose();renderer=null;
            System.DetachPresentation(this);views.Clear();
        }
        private void CheckAlive(){if(disposed)throw new ObjectDisposedException(nameof(DynamicOcclusionPresentation));}
    }
}
