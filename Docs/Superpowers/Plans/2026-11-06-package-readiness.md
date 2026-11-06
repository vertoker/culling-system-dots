# Package readiness and responsive baking

**Status:** In progress.

**Goal:** Automate agent-document validation, remove unrelated ignore rules, exercise local skills,
make the bake window responsive through preparation/publication, measure a substantial synthetic
workload, and finish the package's public metadata and documentation.

**User decisions:** MIT license. Work and commit on master. The user will publish/force-push;
the agent must not push. Preserve the user's open scene and existing unrelated changes.

## Design

Use a standard-library Python validator shared by local execution and a lightweight GitHub Actions
workflow. Validate actual instruction navigation, code-directory coverage, skill metadata, portable
package links and cumulative instruction size. Test malformed fixtures rather than matching prose.

Keep Unity object capture on the main thread but yield between bounded preparation steps. Run pure
numeric BVH construction and result postprocessing outside the UI thread with explicit lifetime and
cancellation ownership. The window uses nonblocking staged operations; retain synchronous public
helpers for scripts/tests. Revalidate current inputs before the final atomic asset publication.
Unity mesh/API calls and final asset import/replace remain indivisible; expose their phase instead
of promising a hard upper bound on frame time. Preserve deterministic geometry/results and output
bytes/GUID on cancellation, error or invalidation.

Measure a deterministic independently authored synthetic scene, recording dimensions, geometry,
samples, resolution, CPU/GPU backends, phase timings, heartbeat gaps and memory metrics. Use a
separate measured runtime camera workload for synchronization cost. Distinguish payload counters,
sampled process/Unity memory and unavailable GPU timing. No universal speedup claim.

Add identical MIT licenses at repository/package roots, package README/changelog and descriptor
links. Document actual newly validated behavior and remaining limits; do not relabel old evidence.

## Tasks

- [x] Validator, negative/positive fixture tests, CI and usage instructions; remove game-only ignores.
- [x] Package MIT license, README, changelog and descriptor metadata; exercise documentation skills.
- [x] Responsive capture/build/postprocess/publication with cancellation/lifetime regressions.
- [ ] Fresh relevant Unity suites and CPU/GPU large-scene and camera-cost measurements.
- [ ] Review integrated changes, verify installation payload and public docs, commit on master.
- [ ] Provide final local revision and guarded publication command for the user; do not push.

## Verification and rulings

- Starting revision: e4ebfb5. The user approved committing the 21 template deletions separately;
  commit 91e2f99 removes them so clean CI does not restore obsolete template code.
- All live Unity work stays with the coordinator. Parallel document/tool workers must not invoke
  Unity, edit C#, stage/commit, or mutate shared root instructions.
- Skills will be exercised through actual document/tool/install workflows and independent review,
  with findings recorded instead of claiming that frontmatter checks prove agent behavior.
- Tool and documentation workers exercised the local folder-instructions, execute-plan,
  markdown-improver and package-validation workflows. They required no client plugin or extra
  Python dependency. Coordinator validation observed 30/30 Python tests and a passing document
  validator. The four new incremental workflow regressions were observed RED 0/4 then GREEN 4/4.
- The user's latest instruction reserves the final force push to the user. This supersedes the
  earlier publication approval; no remote write is authorized for the agent.
- Independent review found three Important and two Minor issues. The coordinated fix pass is
  recorded in the readiness review. Late-script mutation regression was observed RED, then the
  complete EditMode suite passed 182/182. The full PlayMode run passed 16/16, including the
  explicit synthetic camera measurement. The Python suite passed 35/35 after scalar/path fixes.
- Numeric workers remain joined before disposal. Publication uses a final synchronous source
  fingerprint guard adjacent to the atomic write; comparison snapshots reuse only derived bounds
  and never enter tracing. This guard/import can still stall briefly. No hard frame budget is claimed.
- Benchmark fixture sampling is checked against an owned unrelated Exclude region. Mono process
  and allocation counters returning zero are unavailable; external process-memory sampling is used.
