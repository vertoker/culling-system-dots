# Bake snapshots

Scene collection copies triangle meshes, material classification, transforms, group mapping and selected sample positions into session-owned numeric arrays. Repeated mesh geometry is shared. Only the first non-empty LOD is used as bake geometry. Grouped and additional SkinnedMeshRenderers use BakeMesh at their current static pose without changing their source renderer or adding MeshFilter.

CPU and GPU use the same mesh vertices and binary acceleration structure. All numeric inputs are validated before work begins. Settings and actual scene input fingerprints are checked again before publication. Geometry, pose or settings changes invalidate the unpublished bake.

Snapshots register CPU reader dependencies and join them on disposal. GPU sessions own uploaded copies and bounded asynchronous readbacks. Callback data is copied while valid; generation tokens reject stale callbacks. Cancellation joins outstanding work before releasing buffers.

Only static captured geometry is supported. Moving occluders or animated poses require rebaking. Animated/deformed shadow companions have not been accepted.
