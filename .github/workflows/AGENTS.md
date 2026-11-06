# AGENTS.md - `.github/workflows`

Read [the .github guide](../AGENTS.md) first.

- `agent-docs.yml` runs [the validator](../../Tools/ValidateAgentDocs.py) and the standard-library
  Python suite. Run `python Tools/ValidateAgentDocs.py` and
  `python -m unittest discover -s Tools/tests -v` locally from the repository root.
- The validator also accepts `--root <checkout>` and discovers its own repository when omitted,
  so its absolute script path works from another working directory. Git supplies maintained
  indexed and nonignored new paths; missing working-tree files are excluded from source coverage.
- Pin official actions to immutable commits. Keep permissions read-only and avoid secrets,
  generated Unity caches and Editor/CLI execution in this documentation job.
- Static checks cover Markdown links/heading anchors, code-folder guidance, parent navigation,
  inherited size and skill metadata/catalog links. Non-Latin letters flag agent prose for review;
  this heuristic cannot prove English language or whether an agent follows the instructions.
  Network links and dynamically generated anchors require separate review.
