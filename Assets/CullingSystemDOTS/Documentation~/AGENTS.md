# AGENTS.md - `Documentation~`

Read [the package guide](../AGENTS.md) first. This is the documentation that ships with the
package; assume repository-root docs and developer tooling are unavailable.

- Setup/Authoring describe consumer configuration. BakingSnapshots/BakingWorkflow/JobsBakeBackend
  describe bake contracts. RuntimeEvaluator/RenderingIntegration describe evaluation/presentation.
  Compatibility owns supported scope; Migration owns consumer transition guidance.
- Use package-relative links and make root-repository context optional. Never require a removed
  report or private source tree to understand or use a public contract.
- Preserve failure/cancellation behavior, ownership and limitations when simplifying prose.
  Separate current guarantees from historical compatibility decisions and proposed improvements.
- Do not claim universal GPU equality, conservative sampling, total-memory caps, zero allocations,
  full Editor responsiveness or untested platform support.
- Link manifests/constants rather than duplicating values unnecessarily. Validate paths and
  factual statements against current source before making a stronger claim.
