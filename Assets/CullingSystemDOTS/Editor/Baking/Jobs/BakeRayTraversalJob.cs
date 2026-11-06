using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
namespace CullingSystemDOTS.Editor.Baking.Jobs
{
    [BurstCompile(CompileSynchronously = true, FloatMode = FloatMode.Strict)]
    internal struct BakeRayTraversalJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<NativeBvhNode>.ReadOnly Nodes;
        [ReadOnly] public NativeArray<int>.ReadOnly Order, GeometryRoots;
        [ReadOnly] public NativeArray<BakeTrianglePlanes>.ReadOnly Triangles;
        [ReadOnly] public NativeArray<BakeInstance>.ReadOnly Instances;
        [ReadOnly] public NativeArray<BakeSampleLocation>.ReadOnly Samples;
        // Work item w exclusively owns [w*Words,(w+1)*Words); no two workers share a word.
        [NativeDisableParallelForRestriction] public NativeArray<uint> TileBits;
        [WriteOnly] public NativeArray<uint> TileNeighbors;
        [WriteOnly] public NativeArray<byte> BurstFlags;
        [WriteOnly] public NativeArray<byte> TileErrors;
        public BakeRaySettings Settings;
        public int SampleStart, TilesPerFace, Words, OpaqueRoot, TransparentRoot;
        [BurstDiscard] private static void MarkManaged(ref byte flag) { flag = 0; }
        public void Execute(int work)
        {
            byte burst = 1; MarkManaged(ref burst); BurstFlags[work] = burst; TileErrors[work] = 0;
            int tilesPerCell = 6 * TilesPerFace, cell = work / tilesPerCell, remainder = work % tilesPerCell;
            int face = remainder / TilesPerFace, firstY = (remainder % TilesPerFace) * BakeRayGeneration.TileHeight;
            int endY = math.min(firstY + BakeRayGeneration.TileHeight,Settings.Height), bits = work * Words;
            uint neighbors = 0;
            for (int y = firstY; y < endY; y++) for (int x = 0; x < Settings.Width; x++)
            {
                var ray = BakeRayGeneration.Ray(in Settings,Samples[SampleStart+cell].Position,face,x,y);
                float closest = ray.Far; int bestInstance = int.MaxValue, bestTriangle = int.MaxValue, group = -1;
                if (!Trace(in ray,OpaqueRoot,false,bits,ref closest,ref bestInstance,ref bestTriangle,ref group)) { TileErrors[work] = 1; return; }
                Set(bits,group); neighbors |= ReferenceCpuTraversal.NeighborVisibility(ray.Direction,closest,in Settings);
                if (!Trace(in ray,TransparentRoot,true,bits,ref closest,ref bestInstance,ref bestTriangle,ref group)) { TileErrors[work] = 1; return; }
            }
            TileNeighbors[work] = neighbors;
        }
        private void Set(int start, int group)
        {
            if (group >= 0) TileBits[start + (group >> 5)] |= 1u << (group & 31);
        }
        private bool Trace(in BakeRay world, int root, bool transparent, int bits, ref float closest,
            ref int bestInstance, ref int bestTriangle, ref int bestGroup)
        {
            if (root < 0) return true;
            // Median split depth <=31 for Int32 arrays; 512 bytes can hold 127 pending siblings.
            var stack = new FixedList512Bytes<int>(); stack.Add(root);
            while (stack.Length > 0)
            {
                int last = stack.Length - 1, current = stack[last]; stack.RemoveAt(last);
                var node = Nodes[current];
                if (!ReferenceCpuTraversal.MeetsBounds(in world,node.Min,node.Max,closest)) continue;
                if (node.Count == 0) { if (!Push(ref stack, node.Right) || !Push(ref stack, node.Left)) return false; continue; }
                for (int i = 0; i < node.Count; i++)
                {
                    int instanceId = Order[node.Start+i]; var instance = Instances[instanceId];
                    if (!ReferenceCpuTraversal.MeetsBounds(in world,instance.BoundsMin,instance.BoundsMax,closest)) continue;
                    if (transparent && instance.GroupIndex < 0) continue;
                    var local = ReferenceCpuTraversal.Local(in world,in instance);
                    var localBoundsRay = local; localBoundsRay.Near = 0;
                    int geometryRoot = GeometryRoots[instance.Geometry]; if (geometryRoot < 0) continue;
                    var blas = new FixedList512Bytes<int>(); blas.Add(geometryRoot); bool foundTransparent = false;
                    while (blas.Length > 0 && !foundTransparent)
                    {
                        int tail = blas.Length - 1, id = blas[tail]; blas.RemoveAt(tail); var triangleNode = Nodes[id];
                        if (!ReferenceCpuTraversal.MeetsBounds(in localBoundsRay,triangleNode.Min,triangleNode.Max,closest)) continue;
                        if (triangleNode.Count == 0) { if (!Push(ref blas, triangleNode.Right) || !Push(ref blas, triangleNode.Left)) return false; continue; }
                        for (int t = 0; t < triangleNode.Count; t++)
                        {
                            int triangleId = Order[triangleNode.Start+t]; var plane = Triangles[triangleId];
                            // Use original far for opaque tests: traversal order must not decide coincident ties.
                            float hit = ReferenceCpuTraversal.Intersect(in local,in plane,transparent ? closest : world.Far,instance.DoubleSided != 0);
                            if (transparent)
                            {
                                if (hit < closest) { Set(bits,instance.GroupIndex); foundTransparent = true; break; }
                            }
                            else if (hit < closest || (hit == closest && hit < world.Far &&
                                (instanceId < bestInstance || (instanceId == bestInstance && triangleId < bestTriangle))))
                            {
                                closest = hit; bestInstance = instanceId; bestTriangle = triangleId; bestGroup = instance.GroupIndex;
                            }
                        }
                    }
                }
            }
            return true;
        }
        private static bool Push(ref FixedList512Bytes<int> stack, int node)
        {
            if (stack.Length == stack.Capacity) return false;
            stack.Add(node); return true;
        }
    }
}
