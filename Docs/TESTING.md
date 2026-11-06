# Selecting validation

Read [the repository guide](../AGENTS.md), the local layer instructions and
[Unity workflow](AgentRules/unity-editor.md). Choose checks for the behavior changed; do not run
the entire suite for every reversible text edit.

| Change | Relevant checks |
|---|---|
| Markdown or agent instructions | Links, instruction inheritance, English, skill metadata, diff review |
| Repository Python tools or package-layout contracts | `python -m unittest discover -s Tools/tests -v` |
| Core codec, grid or evaluator | `CullingSystemDOTS.Core.Tests` in EditMode |
| Baking, geometry, Jobs/GPU lifetime, authoring or output | Relevant fixtures in `CullingSystemDOTS.Editor.Tests` in EditMode |
| World, registry, camera or companion ownership | `CullingSystemDOTS.Runtime.Tests` in EditMode |
| Fixture generation | `CullingSystemDOTS.Delivery.Tests` in EditMode |
| Actual cameras, shadows, materials, LOD or URP stacks | Relevant fixtures in `CullingSystemDOTS.Rendering.Tests` in PlayMode |
| Importable sample behavior | `CullingSystemDOTS.Delivery.Rendering.Tests` in PlayMode |
| Distribution, dependency closure or installed resource resolution | Python checks and a fresh Git-installed consumer; build/Player when relevant |

Inspect current asmdefs and runner discovery rather than assuming old totals. Start with affected
fixtures; broaden to coupled assemblies when contracts or lifecycle boundaries change. For a bug,
prefer a regression that fails for the original cause. Avoid tests that only duplicate the
implementation, match prose, or test mocks rather than behavior.

## Unity runs

Use the connected Editor test runner through Coplay MCP. After source edits, complete import and
compilation before tests. Retain the returned job identifier, await completion, and read discovered,
passed, failed and skipped counts plus failures. Zero discovered tests is not a passing suite.
Do not edit scripts or mutate the Editor during the run.

GPU tests may be unavailable on a given device. State what actually ran and why something skipped;
do not treat CPU success as proof of GPU behavior. Editor rendering tests do not establish support
for every Player, graphics API or pipeline. Benchmark claims require measurements with scope.

## One disposable installation consumer

When installation verification is required, generate the single supported destination:

```powershell
python Tools/CreateDeliveryConsumer.py --revision <commit>
```

The tool refuses an existing destination. It installs the exact committed package through a local
Git subpath URL, imports its sample and copies only development fixtures/settings. It does not
launch Unity. Open that consumer normally, verify its identity, and keep its Editor separate from
the main instance. Do not create parallel retained project copies or replace Git installation with
a copied production package. Unsaved working-tree edits are not part of the selected commit.

Capture useful results before cleanup. Stop only verified owned consumer processes, confirm they
have exited, then remove the exact owned temporary tree. Restore routing to the main Editor.

## Evidence

`Docs/Validation` contains historical acceptance reports. Record tested revision, package source,
Unity/package versions, mode/platform/API, actual counts and material limitations for new evidence.
Do not overwrite old reports to imply that they tested newer code. Report fresh results separately
from unchanged behavior inferred from an old run.
