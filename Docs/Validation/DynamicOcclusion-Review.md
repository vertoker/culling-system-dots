# Whole-feature review and correction

A fresh-context read-only review covered `cce6ac6..1af2102`: registry/block data, ECS ownership,
GPU streaming/cache/depth/Hi-Z/compaction/frame fences, URP fallback, sample and oracle. It found
one Important correctness defect and no Critical/additional Important finding. Pending Player and
performance gates were not treated as passing evidence.

Opaque ShadowsOnly objects could enter camera depth while their color submission was omitted.
A shadow-only wall could therefore hide a camera-visible target. The correction excludes these
rows from camera depth; independent full-shadow submissions are unchanged. A real rendered-pixel
regression first failed with red channel zero, then passed after the correction.

The review also identified a coverage weakness: the late-mutation test accumulated restoration
over four frames. It now records only the first affected end-camera event, requires its frame
number to equal the mutation frame, and checks the corresponding output before the next camera
render. This strengthened test passed before the depth correction and in the coupled run.

| Check | Job | Result |
| --- | --- | --- |
| ShadowsOnly pixel regression before correction | `61df9eb917254322ab302e25c3987479` | 1/2; shadow-only wall incorrectly hid the target |
| Coupled functional rendering after correction | `0c196c2af2f14170aa2549b03edc4dc1` | 56/56, no failures/skips |

This records the single consolidated correction pass after the whole-feature review. Actual
Git-installed Player execution and subsequent performance/stress comparison remain separate gates.
