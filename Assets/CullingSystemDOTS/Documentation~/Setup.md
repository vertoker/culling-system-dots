# Setup

Use Unity 6000.6.8f1 and install this Git package:

```
https://github.com/vertoker/culling-system-dots.git?path=Assets/CullingSystemDOTS
```

For a pinned revision, append `#<commit-or-tag>` after the path query. Git must be available to Unity. Unity resolves Entities, the unmodified Entities Graphics package, Collections, Mathematics and Burst from package.json. No package override or Library patch is required. The development checkout edits the same package under Assets/CullingSystemDOTS; installed projects load it through UPM. Compute shader loading uses its preserved asset GUID in both layouts.

URP 17.6.x provides automatic base/overlay camera-order integration. Install a compatible URP version in the receiving project. Other SRPs require render-order integration and separate validation.

Import the Basic sample from Package Manager. It includes a SubScene, three groups, LOD/material bindings, a CPU bake, camera motion and inversion controls. The CPU bake can be regenerated with the current baker.

Package tests use Test Framework assemblies. Add `com.vertoker.culling-system-dots` to the receiving manifest's testables array to run them. The root development project includes independent renderer, streaming and sample checks. Coplay MCP is development tooling, not a consumer dependency.

The supported install layout follows Unity's [Git dependency documentation](https://docs.unity.com/en-us/engine/6000.6/manual/packages-list/upm-git-dependencies/upm-git).
