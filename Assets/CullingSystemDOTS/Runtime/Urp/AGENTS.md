# AGENTS.md - `Runtime/Urp`

Read [the Runtime guide](../AGENTS.md) first.

- Keep the URP assembly optional and constrained by its existing package version define. Do not
  add unconditional URP references to Core, Ecs or Rendering.
- Preserve base/overlay order, shared overlays and scoped restoration. Pipeline callbacks and
  stack enumeration must agree with the graphics adapter's actual pass sequence.
- Widening the version range or supporting another camera kind requires corresponding validation;
  successful compilation alone does not establish rendering compatibility.

See [Compatibility](../../Documentation~/Compatibility.md). In the development checkout, real
URP stack coverage lives in `Assets/Tests/PlayMode`.
