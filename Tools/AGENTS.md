# AGENTS.md - `Tools`

Read [the repository guide](../AGENTS.md) first. [tests](tests/AGENTS.md) checks portable Python
tooling and distribution contracts.

- Keep helpers independent of a particular agent client and avoid extra dependencies when the
  standard library suffices. Discover checkout paths rather than baking in device-local ones.
- `CreateDeliveryConsumer.py` creates one exact, new disposable destination and a Git dependency
  pinned to a committed revision. Preserve refusal of existing/outside paths and safe archive extraction.
- Do not replace actual Git installation with copied production sources or clone multiple retained
  Unity projects. The helper must not silently launch Unity or a CLI wrapper.
- Use structured arguments and check exit codes before dependent operations. Never interpolate
  untrusted paths into shell commands or use broad cleanup patterns on the repository root.

Run `python -m unittest discover -s Tools/tests -v` for affected tooling/layout changes.
