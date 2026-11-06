---
name: package-validation
description: Use when changing package distribution, dependency declarations, installed resource resolution, or investigating a Git-install-only failure.
---

# Validate the installable boundary

Read [package-boundary rules](../../../Docs/AgentRules/package-boundaries.md) and
[testing](../../../Docs/TESTING.md). The root development project can hide missing dependencies.

Inspect `Assets/CullingSystemDOTS/package.json`, the actual package files and affected asmdefs.
Check required resources travel with the package, direct engine modules are declared, Editor/test
references stay isolated, and resource lookup works outside the development `Assets` location.

Run the relevant portable checks:

```powershell
python -m unittest discover -s Tools/tests -v
```

When a live installation check is needed, use the existing single-consumer helper with an explicit
committed revision. It installs a local Git subpath dependency; copying production sources is not
equivalent evidence. Confirm package source, version and actual installed payload before tests.

Select affected Unity checks and built-Player coverage according to the change. Do not create
retained project clones or launch Unity CLI/batchmode. Preserve the main Editor's scene and routing.

Report revision, dependency closure, actual discovered/pass/fail/skip counts and platform limits.
Capture evidence before scoped consumer cleanup. Successful local Git installation does not
publish the package or prove the remote branch contains the tested revision.
