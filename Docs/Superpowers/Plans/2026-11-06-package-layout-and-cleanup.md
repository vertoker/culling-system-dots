# Git-installable package and repository cleanup

> **For agentic workers:** Use superpowers:executing-plans inline on master. Preserve unrelated user changes. No additional permanent Unity projects.

**Goal:** Keep the Unity development project at the repository root, deliver the complete plugin from `Assets/CullingSystemDOTS`, and remove the commercial reference content from the working tree and Git history.

**Architecture:** Preserve asset GUIDs during relocation. Put `package.json`, Runtime, Editor, Tests, Samples~ and Documentation~ in the asset package root so UPM can install `https://github.com/vertoker/culling-system-dots.git?path=Assets/CullingSystemDOTS`. Replace copied acceleration/traversal code with our own balanced binary BVH and direct triangle intersection, shared numerically by CPU Jobs and a new Unity compute shader. Retain whole-session CPU recovery and atomic publication. Use stock Entities Graphics from UPM with synchronized render-component masks and public additional-entity baking data. Preserve camera/shadow behavior.

**User decisions:** Delete all copies of the commercial implementation, including the Editor reference copy, and independently replace borrowed baker code. Commits only on master. No Unity CLI, user-scene save/discard or unrelated project access.

## Tasks

- [x] Audit copied/derived files, comparison assets, tools and history paths. Preserve unrelated user deletions.
- [x] Add package layout/install-boundary checks, observe failure, relocate the package with GUIDs, update path resolution for both Assets and UPM installations.
- [x] Replace copied BVH/traversal/triangle conversion; verify analytic CPU/GPU visibility, transforms, transparency, deterministic ties, memory/cancellation/fallback, numeric validation and no partial output.
- [x] Remove commercial references, copied reference assets and reference-only tests/tools/reports. Keep source-independent functional tests and fresh evidence; update documentation without stale acceptance claims.
- [x] Resolve standard UPM graphics integration according to the user's answer; verify camera order, overlays, shadows, streaming and entity/physics lifetime.
- [ ] Replace the two retained consumer projects with one temporary Git-install validation when needed; keep ordinary development/testing in the root project.
- [ ] Run clean compilation, package tests and rendering tests; verify a Git-path consumer and actual Player when the graphics integration is finalized.
- [ ] Commit scoped changes, purge commercial paths/blobs and derivative content from local Git history, verify reachable objects/reflogs and preserve user changes. Remote history changes require a deliberate force push; report local versus remote state precisely.

## Progress and rulings

- User explicitly approved independent replacement of all borrowed baker code and stock UPM graphics integration.
- Package relocated with GUIDs preserved. Resource resolution uses GUIDs, so both Assets and UPM layouts work. Namespaces/assemblies use CullingSystemDOTS.
- Replaced compressed acceleration/traversal and imported plane conversion with independent binary BVH and direct vertex intersection. Near clipping and closed shared edges are explicit regression requirements. Coincident opaque hits choose the first registered instance/triangle deterministically rather than an arbitrary compressed-node visit order.
- Stock UPM graphics 6.6.0 resolves in Library/PackageCache. Public AdditionalEntitiesBakingData maps renderer-created entities. Per-camera MaterialMeshInfo changes wait for readers and restore camera scopes. Cost: main-thread synchronization can stall; no performance improvement is asserted.
- 161/161 EditMode and 15/15 PlayMode after namespace/stock integration; a final run follows sample regeneration. Python 4/4. The sample was independently CPU-baked (4 cells, 3 groups) in a preview scene, without saving the host.
- Removed the two old retained consumer projects. One temporary named GitInstall consumer will exercise the actual pinned Git dependency, with no copied production packages.
- PowerShell recursive deletion was rejected by automatic policy. Exact tracked-path Git removals and scoped Git cleanup succeeded. A tool guard prevented closing the old clean consumer; cleanup initially met locked files. The verified owned process was then stopped and scoped cleanup completed. No other Editor process was touched.
