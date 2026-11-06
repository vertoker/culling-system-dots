# AGENTS.md - `.agents`

Read [the repository guide](../AGENTS.md) first.

- [README](README.md) is the explicit skill catalog for clients without automatic discovery.
- Keep skills as portable `skills/<name>/SKILL.md` files with `name` and a precise `description`
  in YAML frontmatter. Load only the relevant skill; do not require a particular vendor runtime.
- Skills describe reusable procedures. Project permissions and facts stay in AGENTS/rule files;
  link them rather than creating a second authority or an automatic approval flow.
- No private memory, machine paths, credentials or plugin installation belongs in tracked skills.
  `.agents/local-context.md` is ignored optional local context.
- Validate metadata, references and applicability when adding a skill. Do not copy game-specific
  release, localization, SDK generation or personal writing-voice skills into this package.
