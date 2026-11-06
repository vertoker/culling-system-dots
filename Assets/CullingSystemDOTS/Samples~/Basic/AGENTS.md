# Basic sample: agent guide

This folder may be the package sample source or an imported copy in a consumer project. Follow
the host project's governing instructions. In the package source, also read `../AGENTS.md` when
available; this guide does not require it after import.

- Keep scenes, SubScene, bake, settings and resources self-contained. Preserve their GUIDs and
  serialized references. Do not save unrelated open scenes when modifying the sample.
- [Runtime](Runtime/AGENTS.md) owns the sample interaction code. Sample code demonstrates public
  package APIs; it must not depend on developer tools or Editor-only assemblies.
- Read [README](README.md) for the expected interaction. Validate normal visibility, inversion and
  restoration without disabling simulation entities.
- In the source repository, coordinate changes with the `Assets/DeliverySample` development mirror
  and sample tests. In a consumer project, edit only the imported copy within the user's task.
