using Unity.Mathematics;
using UnityEngine;
using UnityEngine.AI;
namespace PerfectCullingDOTS.Authoring
{
    public enum CullingSamplingMode { BelowCollider, BelowColliderArray, Floating, InsideColliders, FarFromNavMesh }
    public sealed class CullingSamplingProviderAuthoring : MonoBehaviour, ICullingSamplingProvider
    {
        private readonly Collider[] overlap = new Collider[128];
        public CullingSamplingMode Mode;
        public Collider[] Colliders = System.Array.Empty<Collider>();
        public float OffsetY = 1;
        public bool ExcludeInsideColliders = true;
        public LayerMask Layers = ~0;
        public float GroundDistance = 5;
        public float NavMeshDistance = 2.5f;
        public float MaxDistanceX = .25f, MaxDistanceZ = .25f;
        public bool ExcludeBelowNavMesh = true;
        public void Initialize() { }
        public bool IsActive(float3 worldPosition)
        {
            Vector3 position = worldPosition;
            switch (Mode)
            {
                case CullingSamplingMode.BelowCollider:
                case CullingSamplingMode.BelowColliderArray:
                    if (Colliders == null) return true;
                    bool backfaces = Physics.queriesHitBackfaces;
                    try
                    {
                        Physics.queriesHitBackfaces = true;
                        foreach (var collider in Colliders)
                        {
                            if (collider == null) continue;
                            if (collider.Raycast(new Ray(position-new Vector3(0,OffsetY,0),Vector3.up),out _,float.MaxValue)) return false;
                            if (Mode == CullingSamplingMode.BelowColliderArray && ExcludeInsideColliders)
                            {
                                int count = Physics.OverlapSphereNonAlloc(position,.01f,overlap);
                                for (int i = 0; i < count; i++) if (overlap[i] == collider) return false;
                            }
                            if (Mode == CullingSamplingMode.BelowCollider) break;
                        }
                        return true;
                    }
                    finally { Physics.queriesHitBackfaces = backfaces; }
                case CullingSamplingMode.Floating:
                    return Physics.Raycast(position,-Vector3.up,GroundDistance,Layers.value);
                case CullingSamplingMode.InsideColliders:
                    return Physics.OverlapSphereNonAlloc(position,.01f,overlap,Layers.value) <= 0;
                case CullingSamplingMode.FarFromNavMesh:
                    if (!NavMesh.SamplePosition(position,out var hit,NavMeshDistance,NavMesh.AllAreas)) return false;
                    Vector3 direction = (position-hit.position).normalized;
                    if (ExcludeBelowNavMesh && direction.y < float.Epsilon) return false;
                    return Mathf.Abs(hit.position.x-position.x) <= MaxDistanceX && Mathf.Abs(hit.position.z-position.z) <= MaxDistanceZ;
                default: throw new System.ArgumentOutOfRangeException(nameof(Mode));
            }
        }
    }
}
