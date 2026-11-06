# Culling System DOTS package: agent guide

This directory is the complete installable UPM package. These instructions are self-contained
for an installed package; repository-root tools and documents are optional development context.
In the source checkout, also read the governing repository instructions. Paths below are relative
to this package root.

## Contracts

- Unity compiles all C#/HLSL. GPU compute baking is primary; CPU Jobs/Burst is a genuine fallback.
  Do not introduce prebuilt native baker binaries or copied commercial implementation.
- Use stock Entities Graphics from UPM. Do not patch Unity assemblies, embedded packages or caches.
  `package.json` owns required dependencies and engine modules. Root-project packages are not
  automatically available to a consumer.
- Keep required code/resources inside this package. Resolve assets from stable identity or the
  installed location rather than hardcoding an `Assets` or `Packages` layout.
- Keep Runtime independent of UnityEditor and NUnit. Optional URP code stays version-gated in
  its own assembly. Preserve existing GUIDs, serialized identities and compatibility contracts.
- Complete all readers before disposing native data or mutating camera-visible masks. Publish
  only successful, current results; cancellation/failure must preserve the prior valid bake.
- Culling visibility must not disable an entire ECS entity, physics or unrelated gameplay logic.
  Preserve ordered camera operations and the separately owned graphics shadow behavior.
- Never save/discard user scenes to bake or automate verification. Unity creates metadata for
  imported files. Preserve existing `.meta` files when moving assets.
- In the source repository, work and commit on `master` only, preserving unrelated changes.
  In a consumer project, follow that project's Git ownership rules instead; do not create commits
  in a package cache. Publishing or shared-history rewriting requires explicit authorization.
- Write guidance and comments in English. Tools and skills are optional adapters, not permission
  grants. When an operation or validation is unavailable, report its exact limitation.

## Layer map

| Area | Guide |
|---|---|
| Native data, codec, sampling and frame evaluation | [Runtime/Core](Runtime/Core/AGENTS.md) |
| World ownership, registry and authoring components | [Runtime/Ecs](Runtime/Ecs/AGENTS.md) |
| Stock graphics adapter and shadow companions | [Runtime/Rendering](Runtime/Rendering/AGENTS.md) |
| Optional URP camera ordering | [Runtime/Urp](Runtime/Urp/AGENTS.md) |
| Runtime layer navigation | [Runtime](Runtime/AGENTS.md) |
| Authoring conversion and bake workflow | [Editor](Editor/AGENTS.md) |
| Package-contained tests | [Tests](Tests/AGENTS.md) |
| Importable sample | [Samples~](Samples~/AGENTS.md) |
| Shipped usage and behavior contracts | [Documentation~](Documentation~/AGENTS.md) |

## Validation and limits

Read [Compatibility](Documentation~/Compatibility.md) before making platform or support claims.
The supported baseline is recorded there and in `package.json`; do not silently broaden it.
Run affected package test assemblies through Unity Test Runner, with package tests enabled in
the consumer when needed. Check discovery and actual results; zero tests is not success.
Rendering, GPU and built-Player claims need their own corresponding checks. Documentation-only
edits need link and consistency checks rather than a full runtime suite.

The baker uses discrete rays and is not a guarantee of finding arbitrarily thin geometry.
Payload budgets are not whole-process memory caps; synchronization may stall the main thread.
Do not claim external-baker equivalence, universal watertightness or a measured speedup without
evidence. See [baking workflow](Documentation~/BakingWorkflow.md) and
[rendering integration](Documentation~/RenderingIntegration.md).
