using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace CullingSystemDOTS.Dynamic.Gpu
{
    internal interface IOcclusionGpuRetirement
    {
        bool PollRetirement();
        void SealForShutdown();
    }
    internal static class OcclusionGpuRetirement
    {
        private const string PumpName = "Runtime occlusion retirement";
        private static readonly List<IOcclusionGpuRetirement> Pending = new List<IOcclusionGpuRetirement>();
        private static readonly HashSet<IOcclusionGpuRetirement> Registered = new HashSet<IOcclusionGpuRetirement>();
        private static readonly HashSet<IOcclusionGpuRetirement> Faulted = new HashSet<IOcclusionGpuRetirement>();
        private static OcclusionGpuRetirementPump pump;
        private static bool polling;
        private static ulong mutation;
        internal static int PendingCount => Pending.Count;
        internal static void Register(IOcclusionGpuRetirement resource)
        {
            if (Registered.Add(resource)) { Pending.Add(resource); mutation++; }
            if (Application.isPlaying && pump == null)
            {
                var gameObject = new GameObject(PumpName) { hideFlags = HideFlags.HideAndDontSave };
                UnityEngine.Object.DontDestroyOnLoad(gameObject); pump = gameObject.AddComponent<OcclusionGpuRetirementPump>();
            }
        }
        internal static void Poll()
        {
            if (polling) return; polling = true;
            try
            {
                for (int i = Pending.Count - 1; i >= 0; i--)
                {
                    var resource = Pending[i]; bool released;
                    try { released = resource.PollRetirement(); }
                    catch (Exception exception) { if (Faulted.Add(resource)) Debug.LogException(exception); continue; }
                    if (!released) continue;
                    Registered.Remove(resource); Faulted.Remove(resource);
                    int last = Pending.Count - 1; Pending[i] = Pending[last]; Pending.RemoveAt(last); mutation++;
                }
            }
            finally { polling = false; }
        }
        // Only at Editor reload / Play exit / application shutdown. This lifecycle barrier
        // reads a separate sentinel, never visibility, and is absent from frame submission.
        internal static void DrainForShutdown()
        {
            if (Pending.Count == 0) { ReleasePumps(); return; }
            foreach (var resource in Pending.ToArray()) resource.SealForShutdown();
            using (var sentinel = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, 4))
            {
                sentinel.SetData(new[] { 0u });
                var request = AsyncGPUReadback.Request(sentinel); request.WaitForCompletion();
                if (request.hasError) throw new InvalidOperationException("Unable to complete the GPU retirement shutdown barrier.");
            }
            ulong previous;
            do { previous = mutation; Poll(); } while (Pending.Count > 0 && mutation != previous);
            ReleasePumps();
        }
        private static void ReleasePumps()
        {
            // HideAndDontSave survives scene/Play transitions. Destroy only our typed,
            // marked helpers explicitly, including ones retained across a domain reload.
            foreach (var retiredPump in Resources.FindObjectsOfTypeAll<OcclusionGpuRetirementPump>())
                if (retiredPump != null && retiredPump.gameObject.name == PumpName && retiredPump.gameObject.hideFlags == HideFlags.HideAndDontSave)
                    UnityEngine.Object.DestroyImmediate(retiredPump.gameObject);
            pump = null;
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void InitializeRuntime()
        {
            Application.quitting -= DrainForShutdown; Application.quitting += DrainForShutdown;
        }
    }
}
