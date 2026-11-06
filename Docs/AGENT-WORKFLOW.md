# Working with repository agents

The shared entry point is [AGENTS.md](../AGENTS.md). Folder instructions and linked rules are
the repository context for any agent client. No private memory store, proprietary task system,
particular model, or workflow plugin is required.

## Read enough context

Inspect Git status and the current branch before editing. Read the nearest maintained folder
instructions and follow their parent pointers. Use the root map to find operation-specific rules.
Read the code, tests and existing documentation that establish the behavior being changed.

The root Unity project is a development harness. When editing the package, also reason about an
installation containing only `Assets/CullingSystemDOTS`. Its instructions cannot require root
documents or developer tooling that a package consumer does not receive.

`.agents/local-context.md`, if present, is ignored device-local context. It may name local paths
or tool connection details but cannot override project rules. Do not copy credentials, personal
paths or session identifiers into shared instructions. Discover tools and live Editor identities
from the current environment rather than preserving yesterday's instance IDs.

## Choose a proportionate workflow

Small, clear edits can be performed directly. For work spanning contracts or several subsystems,
record a short plan with the goal, owned files, constraints, ordered steps and verification.
Existing plans live under `Docs/Superpowers/Plans`; the directory name does not require a plugin.
Mark the status and record the actual outcome in the plan.

Ask focused questions where a missing decision changes the result. Explain the tradeoff and
continue work that does not depend on the answer. A plan is not permission to publish or overwrite
user work. Absorb factual path corrections; surface changes to the requested behavior.

Use subagents only when the user or governing instructions authorize delegation and the runtime
supports it. A handoff names the goal, owned paths, forbidden mutations, applicable instructions,
verification and expected evidence. Children read their own context. Keep shared root edits and
Unity control with one coordinator; integrate and inspect results before claiming completion.

## Work and verify

Use the [skill catalog](../.agents/README.md) for workflows that fit the task. Skills are plain
Markdown that can be read manually when the client does not discover them automatically. Do not
install a plugin merely to follow this repository's instructions.

Follow [Unity workflow](AgentRules/unity-editor.md) before touching the Editor or serialized assets.
Follow [testing](TESTING.md) to select meaningful checks. Check results before starting dependent
operations: a failed command, lost connection or issued refresh is not successful completion.

Commit only verified task-owned changes on `master`. Report the result, what was checked, and
remaining limitations. A request for a status update does not cancel the active task.

## Maintain knowledge in place

Keep root instructions short and navigational. Local instructions carry non-obvious contracts,
cross-folder obligations and traps. Detailed explanations belong in maintained documents linked
from the owning instructions. Each code directory has an `AGENTS.md`; a small child may primarily
point to its parent and identify its narrower responsibility.

When changing a contract, reconcile its current documentation and tests. Preserve the distinction
between current rules, historical evidence and unvalidated proposals. Do not add universal rules
from one isolated incident or copy unrelated policies from the reference game project.
