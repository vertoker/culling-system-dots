# Readiness review and fix pass

One fresh read-only review covered the integrated readiness changes, starting at e4ebfb5,
including the separately approved template cleanup. It found no Critical issues, three Important
issues and two Minor issues. One coordinated fix pass addressed all findings.

## Important

- **Late scripted input edits could publish stale results.** Incremental validation followed by
  background encoding left a gap not covered by Undo/hierarchy events. A new regression was
  observed failing because no exception was raised, then passed for settings, mesh vertices and
  renderer transforms changed after encoding. Publication now performs a final synchronous source
  fingerprint check immediately before the atomic write. Validation-only snapshots reuse derived
  instance bounds but reread/hash all source vertices, indices, mappings, transforms, policies and
  samples. Those snapshots never enter a tracer. Identical source data implies identical derived
  bounds; changed source data must reject the result regardless of notifications.
- **Bake benchmark sampling could depend on open scenes.** The fixture now owns a volume-restricted
  AlwaysInclude region, creates an unrelated unrestricted Exclude region in its own additive scene,
  and asserts/reports the actual active sample count. Both measured backends must trace 128 cells.
  The additive scene is opened through the existing owned-scene helper, which preserves an
  untitled user scene; NewScene(Additive) is not used as a save-dialog workaround.
- **Invalid YAML metadata passed the validator.** A restricted scalar parser now rejects mapping-like
  plain scalars, numeric/bool/null descriptions and malformed quotes while supporting the documented
  plain/quoted/block string subset. New fixtures were observed RED, then the full Python suite passed.
  This is deliberately not a general YAML parser and adds no dependency.

## Minor and measurement corrections

- Phase wall time now closes the phase at its actual transition, including final publication.
- Windows drive and UNC paths are rejected before URI handling, including encoded forms.
- Mono returned zero for process working set and main-thread allocation deltas. Such counters are
  reported as unavailable, not as zero cost. External Windows process-memory sampling records the
  actual Editor working set independently. GPU timing remains explicitly unavailable.

## Scope

The reviewer did not run Unity, mutate files or independently rerun measurements. The coordinator
ran the regressions and suites and records final counts/revisions in the readiness summary.
The initial IO-retry regression also failed as intended and was fixed: failed output writes retain
Ready results for retry. The final synchronous source guard and Unity asset import remain possible
main-thread stalls. Measurements are synthetic Editor workloads on the recorded device/API, not
universal performance or platform acceptance. Remote publication belongs to the user.
