# Package boundaries and dependencies

Read [the repository guide](../../AGENTS.md). The installable root is
`Assets/CullingSystemDOTS`; `package.json` there owns its name, dependencies and samples.

- Keep production sources and required resources inside the package. The root `Packages/manifest.json`
  configures the development project and cannot satisfy an undeclared consumer dependency.
- Declare directly used required packages and engine modules in the package descriptor. Use normal
  UPM resolution; do not embed Unity packages or modify `Library/PackageCache`.
- Keep development-only MCP, fixture generation and root test dependencies out of shipped code.
  Optional integrations need a real compilation boundary and version/define gating.
- Resolve resources by stable asset identity or their installed location. A hardcoded
  `Assets/CullingSystemDOTS` or assumed `Packages/...` path in production code breaks one of the
  supported installation layouts.
- Preserve assembly layering. Runtime must not acquire UnityEditor or NUnit dependencies; test
  assemblies remain isolated. Maintain asmdefs when required within authorized task scope.
- Treat public API, serialized data, group identity and resource GUID changes as compatibility
  decisions. Describe the effect and migration/rebake needs; do not import another product's
  generation-number or versioning scheme.
- All baker implementation must be compiled by Unity from repository C#/HLSL. Do not reintroduce
  native binaries or commercial source copies. Use independently authored synthetic fixtures.

For a distribution change, inspect the actual Git subpath payload and its dependency closure.
The disposable consumer procedure is in [testing](../TESTING.md). Development-project success
alone does not prove Git installation works.
