# Markdown and repository instructions

Read [the repository guide](../../AGENTS.md). Write English, plain Markdown with relative links
that resolve from the document containing them. Preserve exact path casing for portable checkouts.

## Own each fact once

- `AGENTS.md` carries navigation, constraints and non-obvious local obligations.
- `.agents/skills/*/SKILL.md` carries reusable procedures and precise activation descriptions.
- `Assets/CullingSystemDOTS/Documentation~` carries shipped package behavior and usage.
- `Docs` carries repository workflow, plans and scoped evidence.

Link the owner instead of copying its contents. Code constants and manifests own their values;
documents normally name the owner. A validation report may record exact tested values because
its revision and environment are part of the evidence.

## Improve without changing the contract

Inspect referenced code and documents before strengthening a statement. Preserve exceptions,
authorization limits, failure behavior and unvalidated cases while simplifying prose. A shorter
sentence that promises more is a behavior change, not a copy edit.

Use descriptive headings, small paragraphs and tables for real comparisons. Remove generic
introductions, repeated summaries, stale file inventories and client-specific incantations.
Do not impose game copywriting rules or add a tool installation requirement to ordinary Markdown.

Every code directory has local instructions. Keep inherited rules in the parent and add only
the child's useful boundary, traps and cross-folder obligations. Register new instruction files
in their parent's navigation. Do not create instructions inside generated caches or external code.

Package instructions must remain readable in an installed package. Use package-relative parent
links and shipped documentation; a root-repository reference may only be optional context.

## Check the result

Verify links, parent chains, code-folder coverage, skill frontmatter and agreement with current
manifests/code. Inspect the diff for accidental rule changes and non-English text. Documentation
checks do not establish runtime correctness. Preserve the original encoding/newlines when editing
existing files; use UTF-8 for new files.
