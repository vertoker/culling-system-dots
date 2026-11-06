using System;
using System.Collections.Generic;
using System.Threading;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace CullingSystemDOTS.Dynamic.Gpu
{
    internal sealed class OcclusionIndirectRenderer : IDisposable
    {
        private sealed class View : IDisposable
        {
            internal readonly OcclusionGpuPipeline Pipeline;
            internal readonly GraphicsBuffer Visible,Ids,Arguments;
            internal readonly int Count,Groups;
            private readonly OcclusionMemoryBudget memory;
            private long reserved;
            internal View(OcclusionWorld registry,OcclusionShaderResources resources,int count,int groups,int2 size,float far)
            {
                Count=count;Groups=groups;memory=registry.ResourceBudget;
                try
                {
                    long bytes=(long)count*8+(long)groups*GraphicsBuffer.IndirectDrawIndexedArgs.size;
                    memory.Reserve(bytes);reserved=bytes;
                    Pipeline=new OcclusionGpuPipeline(registry,resources,size,far);
                    Visible=new GraphicsBuffer(GraphicsBuffer.Target.Structured,count,4);Ids=new GraphicsBuffer(GraphicsBuffer.Target.Structured,count,4);
                    Arguments=new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments|GraphicsBuffer.Target.Raw|GraphicsBuffer.Target.CopyDestination,groups,GraphicsBuffer.IndirectDrawIndexedArgs.size);
                }
                catch {Dispose();throw;}
            }
            public void Dispose()
            {Pipeline?.Dispose();Visible?.Dispose();Ids?.Dispose();Arguments?.Dispose();if(reserved>0){memory.Release(reserved);reserved=0;}}
        }
        private sealed class Slot : IDisposable
        {
            internal OcclusionIndirectPacket Packet;
            internal OcclusionSnapshotLease Snapshot;
            internal OcclusionGpuMeshSet Meshes;
            internal bool Busy,HasFence;
            internal GraphicsFence Fence;
            internal readonly Dictionary<Camera,View> Views=new Dictionary<Camera,View>();
            internal readonly HashSet<Camera> Touched=new HashSet<Camera>();
            public void Dispose() {Packet?.Dispose();Packet=null;foreach(var view in Views.Values)view.Dispose();Views.Clear();}
        }
        private sealed class Reader : IDisposable
        {
            private readonly Slot slot;
            private readonly OcclusionIndirectRenderer owner;
            internal Reader(Slot slot,OcclusionIndirectRenderer owner) {this.slot=slot;this.owner=owner;}
            public void Dispose()
            {
                slot.Meshes.Dispose();slot.Meshes=null;slot.Snapshot=null;slot.Busy=false;
                if(owner.disposed)slot.Dispose();
            }
        }
        private readonly OcclusionWorld registry;
        private readonly OcclusionShaderResources resources;
        private readonly OcclusionGpuGeometryCache cache;
        private readonly OcclusionGpuUploadBudget upload;
        private readonly List<Slot> slots=new List<Slot>();
        private readonly HashSet<Camera> registered=new HashSet<Camera>();
        private readonly Dictionary<Camera,OcclusionFallbackReason> reasons=new Dictionary<Camera,OcclusionFallbackReason>();
        private readonly int thread=Thread.CurrentThread.ManagedThreadId,limit;
        private Slot active;
        private bool disposed,awaitingContext,suspended;
        private ulong currentFrame;
        internal int UploadedBytes => upload.Used;
        internal int SlotCount => slots.Count;
        internal int PacketCreations {get;private set;}
        internal int ViewCreations {get;private set;}
        internal string LastFault {get;private set;}
        internal ulong PublicationRevision => active?.Snapshot.Revision??0;
        internal void Suspend()
        {CheckThread();if(disposed)return;suspended=true;if(!awaitingContext)RetireActive();}
        internal OcclusionIndirectRenderer(OcclusionWorld registry,OcclusionShaderResources resources)
        {
            this.registry=registry??throw new ArgumentNullException(nameof(registry));
            this.resources=resources??throw new ArgumentNullException(nameof(resources));
            if(!OcclusionGpuPipeline.IsSupported || resources.Indirect==null || resources.IndirectArguments==null)
                throw new NotSupportedException("Complete supported runtime indirect shader resources are required.");
            limit=registry.InFlightLimit;upload=new OcclusionGpuUploadBudget(registry.UploadByteLimit);cache=new OcclusionGpuGeometryCache(registry);
            RenderPipelineManager.beginCameraRendering+=Begin;RenderPipelineManager.endContextRendering+=End;
            OcclusionGpuRetirement.RegisterShutdownOwner(ShutdownForApplication);
        }
        internal IReadOnlyList<InstanceHandle> PrepareFrame(ulong frame)
        {
            CheckAlive();
            if(frame==currentFrame && active!=null && !suspended)return active.Packet.Handles;
            if(awaitingContext)throw new InvalidOperationException("Prepare the next snapshot only after the preceding render context has finished.");
            upload.BeginFrame(frame);currentFrame=frame;LastFault=null;suspended=true;RetireActive();OcclusionGpuRetirement.Poll();
            OcclusionSnapshotLease snapshot=null;OcclusionGpuMeshSet meshes=null;
            try
            {
                snapshot=registry.AcquireSnapshot();cache.Process(snapshot,upload);
                Slot slot=null;foreach(var candidate in slots)if(!candidate.Busy){slot=candidate;break;}
                if(slot==null && slots.Count<limit){slot=new Slot();slots.Add(slot);}
                if(slot==null){LastFault="All bounded frame slots still have GPU readers.";return Array.Empty<InstanceHandle>();}
                meshes=cache.Acquire(snapshot);
                var packet=OcclusionIndirectPacket.Create(registry,snapshot,meshes,resources,upload,slot.Packet);
                if(packet==null)return Array.Empty<InstanceHandle>();
                var removed=new List<Camera>();foreach(var pair in slot.Views)if(!registered.Contains(pair.Key))removed.Add(pair.Key);
                foreach(var camera in removed){slot.Views[camera].Dispose();slot.Views.Remove(camera);}
                if(!ReferenceEquals(packet,slot.Packet))PacketCreations++;
                slot.Packet=packet;slot.Snapshot=snapshot;slot.Meshes=meshes;slot.HasFence=false;slot.Touched.Clear();slot.Busy=true;
                snapshot=null;meshes=null;active=slot;suspended=false;
                return packet.Handles;
            }
            catch(Exception exception)when(Expected(exception)){LastFault=exception.Message;return Array.Empty<InstanceHandle>();}
            finally {meshes?.Dispose();snapshot?.Dispose();}
        }
        internal void RegisterView(Camera camera)
        {
            CheckAlive();if(camera==null)throw new ArgumentNullException(nameof(camera));
            if(!registered.Contains(camera) && registered.Count>=4)throw new InvalidOperationException("This renderer accepts up to four occluding views; other cameras use full draws.");
            registered.Add(camera);
        }
        internal void UnregisterView(Camera camera) {CheckAlive();registered.Remove(camera);reasons.Remove(camera);}
        internal OcclusionFallbackReason ViewFallback(Camera camera) => reasons.TryGetValue(camera,out var reason)?reason:OcclusionFallbackReason.MissingSetup;
        private void Begin(ScriptableRenderContext context,Camera camera)
        {
            if(disposed || suspended || active==null || camera==null)return;
            awaitingContext=true;
            var slot=active;var packet=slot.Packet;
            var ids=packet.Ids;var arguments=packet.Arguments;
            reasons[camera]=OcclusionFallbackReason.UnsupportedView;
            // Repeated callbacks may still have earlier readers. Keep immutable full data rather
            // than rewriting that camera's compacted buffers within the same frame publication.
            if(registered.Contains(camera) && slot.Touched.Add(camera) && SupportedView(camera) && slot.Snapshot.Revision==registry.Revision)
            {
                try
                {
                    int2 size=new int2(camera.pixelWidth,camera.pixelHeight);
                    if(!slot.Views.TryGetValue(camera,out var view) || view.Count!=packet.Count || view.Groups!=packet.GroupCount ||
                        !view.Pipeline.Depth.Size.Equals(size) || view.Pipeline.FarDepth!=camera.farClipPlane)
                    {
                        if(view!=null){view.Dispose();slot.Views.Remove(camera);}
                        view=new View(registry,resources,packet.Count,packet.GroupCount,size,camera.farClipPlane);slot.Views.Add(camera,view);ViewCreations++;
                    }
                    using(var command=new CommandBuffer {name="Current dynamic occlusion and indirect compaction"})
                    {
                        var projection=GL.GetGPUProjectionMatrix(camera.projectionMatrix,false);
                        view.Pipeline.RecordDepth(command,slot.Snapshot,camera.worldToCameraMatrix,projection,slot.Meshes,camera.cullingMask,camera.nearClipPlane);
                        view.Pipeline.RecordHierarchy(command);
                        view.Pipeline.RecordWorldTests(command,packet.Candidates,view.Visible,packet.Count,camera.worldToCameraMatrix,projection,camera.nearClipPlane);
                        command.CopyBuffer(packet.Arguments,view.Arguments);
                        OcclusionIndirectCommands.Compact(command,resources.IndirectArguments,view.Visible,packet.Ids,packet.GroupIds,view.Arguments,view.Ids,packet.Count,packet.GroupCount);
                        Graphics.ExecuteCommandBuffer(command);
                    }
                    ids=view.Ids;arguments=view.Arguments;reasons[camera]=OcclusionFallbackReason.None;
                }
                catch(Exception exception)when(Expected(exception)){LastFault=exception.Message;reasons[camera]=OcclusionFallbackReason.ResourceBudget;}
            }
            for(int i=0;i<packet.GroupCount;i++)
            {
                var group=packet.Groups[i];
                if(group.Shadows!=ShadowCastingMode.ShadowsOnly)
                    OcclusionIndirectCommands.Draw(camera,group.Mesh,group.Material,group.Bounds,group.Layer,packet.Models,ids,arguments,i);
                if(group.Shadows!=ShadowCastingMode.Off)
                    OcclusionIndirectCommands.Draw(camera,group.Mesh,group.Material,group.Bounds,group.Layer,packet.Models,packet.Ids,packet.Arguments,i,ShadowCastingMode.ShadowsOnly);
            }
        }
        private static bool SupportedView(Camera camera) => camera.cameraType==CameraType.Game && !camera.stereoEnabled && !camera.allowMSAA &&
            !camera.allowDynamicResolution && camera.rect.Equals(new Rect(0,0,1,1)) && camera.pixelWidth>0 && camera.pixelHeight>0 &&
            camera.pixelWidth==camera.scaledPixelWidth && camera.pixelHeight==camera.scaledPixelHeight &&
            (camera.targetTexture==null || camera.targetTexture.antiAliasing==1) && camera.projectionMatrix.Equals(camera.nonJitteredProjectionMatrix);
        private void End(ScriptableRenderContext context,List<Camera> views)
        {
            awaitingContext=false;
            if(active!=null){active.Fence=Graphics.CreateGraphicsFence(GraphicsFenceType.AsyncQueueSynchronisation,SynchronisationStageFlags.AllGPUOperations);active.HasFence=true;}
            if(disposed){RetireActive();RenderPipelineManager.endContextRendering-=End;}
        }
        private void RetireActive()
        {
            if(active==null)return;var slot=active;active=null;
            var submission=new OcclusionGpuSubmission(slot.Snapshot,new Reader(slot,this));
            if(slot.HasFence)submission.SealAfterLastConsumer(slot.Fence);else submission.SealAfterLastConsumer();
            submission.Dispose();
        }
        public void Dispose()
        {
            CheckThread();if(disposed)return;disposed=true;RenderPipelineManager.beginCameraRendering-=Begin;cache.Dispose();
            OcclusionGpuRetirement.UnregisterShutdownOwner(ShutdownForApplication);
            foreach(var slot in slots)if(!slot.Busy)slot.Dispose();
            if(!awaitingContext){RetireActive();RenderPipelineManager.endContextRendering-=End;}
            registered.Clear();reasons.Clear();
        }
        private void ShutdownForApplication()
        {
            Dispose();
            // No later render context can consume this publication during application shutdown.
            // Seal even an interrupted context so the lifecycle barrier owns every buffer.
            RetireActive();awaitingContext=false;RenderPipelineManager.endContextRendering-=End;
        }
        private void CheckAlive(){CheckThread();if(disposed)throw new ObjectDisposedException(nameof(OcclusionIndirectRenderer));}
        private void CheckThread(){if(Thread.CurrentThread.ManagedThreadId!=thread)throw new InvalidOperationException("Indirect rendering belongs to its creating thread.");}
        private static bool Expected(Exception exception) => exception is ArgumentException || exception is InvalidOperationException ||
            exception is OverflowException || exception is NotSupportedException || exception is UnityException;
    }
}
