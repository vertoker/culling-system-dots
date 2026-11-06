---
name: folder-instructions
description: Use when adding a code directory, documenting a non-obvious local contract, or maintaining AGENTS.md inheritance and navigation.
---

# Maintain folder context

Read [the root guide](../../../AGENTS.md) and the target's nearest parent instructions.
Every maintained code directory needs local guidance; generated caches and third-party code do not.

- Open with the folder's real path and a relative link to the nearest maintained parent.
- State the boundary, cross-folder obligations and traps that are easy to miss from code alone.
  A small child can point to its parent without repeating every inherited rule.
- Link detailed behavior to its maintained document. Keep the file short enough to read before
  an edit; a class/file inventory is usually less useful than an invariant.
- Register the child in its parent's navigation. Check parent links terminate at the correct root.
- In the package, use only shipped mandatory references. Imported samples need instructions that
  survive copying into a consumer project without their original ancestors.
- Let Unity generate metadata for imported asset Markdown; preserve existing GUIDs on moves.

Check code-directory coverage, link casing and duplication before completion. When a new folder
changes an assembly or ownership boundary, reconcile its tests and the owning layer's guide too.
