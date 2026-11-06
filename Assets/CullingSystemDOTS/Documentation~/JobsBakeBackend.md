# CPU Jobs/Burst baker

The CPU and compute backends consume the same immutable mesh/instance/sample snapshot. Geometry uses an independently implemented balanced binary BVH: median split on the longest centroid axis, stable primitive-index tie breaking and small leaves. Instance and geometry trees share numeric node/index arrays. Triangle records contain original vertices; intersection uses edge/cross/dot products, respects the near/far interval and includes shared edges.

Each Jobs worker exclusively owns one eight-row cubemap tile and its bitset. A dependent reduction merges tiles by sample. Samples retain their full-grid indices even when sampling providers omit other cells. Transparent groups before the closest opaque hit remain visible; transparent surfaces behind it do not. Additional opaque occluders block without acquiring a group ID. Coincident opaque hits choose the earliest instance and triangle in the immutable input order.

Fixed traversal stacks check capacity even with Burst safety checks disabled. A per-tile error faults the entire session before publication. A session registers snapshot readers, joins jobs on cancellation/disposal and releases all owned native arrays. Partial batches cannot become a bake asset.

The memory option bounds tracked native payload and batch size. It does not cap managed builder scratch, Unity/driver overhead or total process RAM. Performance and GPU bitwise agreement across unrelated devices are not promised.
