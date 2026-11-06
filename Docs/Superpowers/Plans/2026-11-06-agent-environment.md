# Universal agent environment

**Status:** Completed. The Git commit containing this record is the delivery commit.

**Goal:** Provide English, portable agent context for the package and its development harness,
using Bullet Hero's navigator/rules/local-skills structure with package-specific contracts.

**Approach:** Shared root navigator, focused instructions in every maintained code directory,
self-contained package instructions, operation-specific rule documents and a small local skill
catalog. Avoid copied game policies, vendor-specific hooks and duplicated encyclopedic rules.
Client entry-point and assembly-ownership preferences were asked separately. With no reply during
implementation, the stated defaults are thin root Claude/Gemini entry points and in-scope agent
maintenance of asmdefs. These defaults are reversible; no client-specific configuration is installed.

**Constraints:** Work/commit on master; preserve unrelated deletions; no push, runtime change,
scene save, paid source, embedded dependency, native baker binary or retained project copy.

- [x] Inspect reference guidance, local structure, current contracts and Git status.
- [x] Write root/package navigators, rule documents and every maintained code-directory guide.
- [x] Add and inspect focused reusable skills and optional client entry points.
- [x] Check links, inheritance, code coverage, metadata, English and instruction size.
- [x] Import new asset Markdown metadata through the correct Editor, preserving scene state.
- [x] Review the task diff, preserve unrelated work, and commit exact owned paths on master.

**Verification:** Document/link/frontmatter checks and scoped diff review. No runtime code changes
are planned, so a full Unity test/build run is not required. Existing validation remains historical.

## Outcome

- 43 AGENTS.md files, including guidance for all 22 maintained code directories.
- Five portable skills and thin root CLAUDE.md/GEMINI.md entry points; no client hooks/configuration.
- 162 relative Markdown links checked, including exact path casing and package-local references.
- Largest inherited AGENTS chain: 13,323 bytes, below a 32 KiB instruction budget.
- Unity generated all 29 required new imported Markdown metadata files; no new GUID collisions.
- Main scene before/after: unnamed, clean, two roots, one loaded scene. No scene save/discard.
- Independent read-only review found no Critical, Important or Minor issues.
- Corrected the evaluator guide's obsolete report reference without changing its runtime contract.
- The bundled skill validator could not start because its Python environment lacks PyYAML.
  Frontmatter fields/names, links and activation scope were checked directly instead; no new
  Python dependency was added merely for this documentation task.
- No runtime sources, manifests, settings, test evidence or unrelated user deletions changed.
