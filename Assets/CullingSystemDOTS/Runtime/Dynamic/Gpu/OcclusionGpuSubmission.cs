using System;
using System.Threading;
using UnityEngine;
using UnityEngine.Rendering;

namespace CullingSystemDOTS.Dynamic.Gpu
{
    // The presentation owner seals after its final color/shadow consumers and keeps polling
    // retired submissions, including after the originating World has been destroyed.
    internal sealed class OcclusionGpuSubmission : IDisposable, IOcclusionGpuRetirement
    {
        private readonly OcclusionSnapshotLease snapshot;
        private readonly IDisposable resources;
        private readonly int thread = Thread.CurrentThread.ManagedThreadId;
        private GraphicsFence fence;
        private bool hasFence, disposeRequested;
        internal bool Released { get; private set; }
        internal OcclusionGpuSubmission(OcclusionSnapshotLease snapshot, IDisposable resources)
        { this.snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot)); this.resources = resources ?? throw new ArgumentNullException(nameof(resources)); }
        internal void SealAfterLastConsumer()
        {
            CheckThread(); if (hasFence || Released) return;
            SealAfterLastConsumer(Graphics.CreateGraphicsFence(GraphicsFenceType.AsyncQueueSynchronisation, SynchronisationStageFlags.AllGPUOperations));
        }
        internal void SealAfterLastConsumer(GraphicsFence lastConsumer)
        { CheckThread(); if (hasFence || Released) return; fence=lastConsumer; hasFence=true; Poll(); }
        internal void Poll()
        {
            CheckThread(); if (Released || !disposeRequested || !hasFence || !fence.passed) return;
            snapshot.CompleteReaders();
            try { resources.Dispose(); }
            finally { snapshot.Dispose(); Released = true; }
        }
        public void Dispose()
        {
            CheckThread(); disposeRequested = true; Poll();
            if (!Released) OcclusionGpuRetirement.Register(this);
        }
        bool IOcclusionGpuRetirement.PollRetirement() { Poll(); return Released; }
        void IOcclusionGpuRetirement.SealForShutdown() => SealAfterLastConsumer();
        private void CheckThread()
        {
            if (Thread.CurrentThread.ManagedThreadId != thread) throw new InvalidOperationException("GPU submissions belong to their creating thread.");
        }
    }
}
