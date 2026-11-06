# Culling System DOTS

Unity compute visibility baking with a CPU Jobs/Burst fallback, compressed PVS data and ECS rendering integration.

## Install

In Unity Package Manager, choose **Add package from Git URL**:

```text
https://github.com/vertoker/culling-system-dots.git?path=Assets/CullingSystemDOTS
```

The repository remains a Unity development project. The complete installable package is in `Assets/CullingSystemDOTS`, including Runtime, Editor, tests, documentation and an importable sample. Unity resolves the dependencies declared in its package.json; no embedded Unity package is needed.

Validated baseline: Unity 6000.6.8f1, Entities / Entities Graphics / Collections 6.6.0, Burst 2.0.0, Mathematics 1.4.0 and URP 17.6.0. Entities Graphics is the ordinary UPM package, resolved into Library/PackageCache. CPU and GPU code are compiled by Unity.

Import **Two rooms with LOD and materials** from the package Samples tab. Open Basic.unity and enter Play Mode. For baking, create Bake Settings and use **Tools → Culling System DOTS → Bake**. See [Setup](Assets/CullingSystemDOTS/Documentation~/Setup.md) and [Baking workflow](Assets/CullingSystemDOTS/Documentation~/BakingWorkflow.md).

Development tests and scenes stay in the root project. A single disposable Git-install consumer is used only for installation and Player verification; it is generated on demand and is not part of the deliverable. Fresh validation is recorded under Docs/Validation. Do not interpret earlier validation of a different implementation as validation of this package revision.

## Contributing with agents

Start with [AGENTS.md](AGENTS.md) for repository navigation and working rules. Each maintained code
directory has local instructions. The package's [own guide](Assets/CullingSystemDOTS/AGENTS.md)
also works after Git URL installation. Reusable, client-independent procedures are listed in
[.agents/README.md](.agents/README.md); no workflow plugin is required.

Run `python Tools/ValidateAgentDocs.py` for maintained documentation and
`python -m unittest discover -s Tools/tests -v` for portable tooling checks. GitHub Actions runs
both on a clean checkout. The repository and installable package use the [MIT license](LICENSE).
