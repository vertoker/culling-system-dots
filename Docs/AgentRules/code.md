# Code and comments

Read [the repository guide](../../AGENTS.md) and the local layer instructions.

- Keep Unity APIs on their permitted threads; Jobs/Burst inputs are immutable native snapshots
  with explicit ownership, dependencies and disposal. Struct copies do not create another owner.
- Validate untrusted geometry, encoded data, ranges and numeric inputs at their boundaries.
  A failed or stale result must not become a successful publication.
- Prefer the established local style and assembly boundary over a repository-wide cleanup.
  Do not add a dependency, abstraction or public API solely to simplify a test.
- Verify version-sensitive Unity APIs against installed sources or reflection; use official
  documentation when needed. Generated IDE project files and cache contents are not source owners.
- Comments explain a non-obvious invariant, trap or coupling. Avoid narrating the code or keeping
  change history in comments. Link larger explanations to their maintained documentation.
- Keep XML documentation attached to its declaration and attributes. Document observable public
  contracts and ownership; do not repeat a complete design inside every source file.

Use meaningful tests for changed behavior. See [testing](../TESTING.md); do not copy Bullet Hero's
game-specific author/category metadata, Burst settings, logging framework or assembly-generation
conventions into this package.
