# Frame evaluator

`CullingFrameEvaluator` evaluates immutable native snapshots in a strict Burst `IJob`.
It reproduces the reference camera's ordered passes, hash/dirty state, neighbor loops,
layer masks, inversion, queued visibility, applied visibility and statistics.
This is a pure state evaluator. Rendering and ECS world integration are separate stages.

## Ownership and scheduling

1. Capture fully populated `CullingFrameInput` arrays before scheduling. Keep their
   contents and referenced blobs immutable until the returned handle completes.
2. Bootstrap with `CullingFrameState.Create(0, 0, 0, 0, allocator)` as the previous buffer.
3. Create a distinct output with counts matching groups, views, volumes and passes.
4. Schedule with the previous evaluation's handle as a dependency when necessary.
   The returned handle also covers disposal of temporary scratch containers.
5. Complete the handle and inspect `output.Result[0]`. Publish only `Success`, and
   only if `SnapshotVersion` is still current for the receiving world/registry.
6. Transfer ownership of the output; dispose the old buffer after all its readers
   finish. Dispose unpublished outputs as well. Struct copies do not create another
   owner: never dispose both a buffer and an alias of it.

The evaluator does not own bake blobs. The world/asset owner must keep them alive.
Groups retain state by `CullingGroupKey` and `StateGeneration` across volume reorder
and removal. Replacing a binding requires a new generation. New/replaced groups use
their explicitly captured initial queued/applied values. Restoring removed bindings
belongs to the registry's removal operation before their handles disappear.

`CullingVolumeSnapshot.InstanceId` separates a runtime volume instance from a shared
immutable bake. A nonzero instance ID becomes the volume part of snapshot group keys;
group indices and the original blob mapping remain unchanged. Zero retains the bake's
original volume ID for existing captures. Instances must have distinct nonzero resolved
IDs, so two instances using one bake cannot share cached visibility accidentally.

Scheduling validates snapshot shapes, IDs, mapping and ranges on the calling thread.
The actual evaluation contains no managed objects. Current scheduling validation and
buffer allocation are deliberately outside the job; allocation/performance acceptance
has not been measured. Radii 0, 1 and 2 match the reference inspector's supported values.

## Cache invalidation

`SnapshotVersion` identifies a publication, and does not force recomputation. An
unmoving camera can retain the same legacy hash across many publication versions.
`InvalidationVersion` represents an explicit source-equivalent `SetDirty` event.
Increment it for the affected views when replacing bake data, bindings or registry
data that the legacy cell-index hash cannot identify. Never infer data equality from
the cell-index hash. Keep this contract at the world capture/registry boundary.

The source has observable quirks that this evaluator preserves:

- Layer edits alone can retain an unchanged hash and stale applied state until dirty.
- The caller's neighbor radius is used while hashing **all** registered cameras.
- A hash hit skips that camera's entire pass and preserves its old statistics.
- A dirty pass clears the queue once per frame, even for unmatched volumes. It does
  not immediately apply that clear. `IgnoreDoNothing` can leave a changed queue and
  unchanged applied visibility.
- Camera removal restores all visibility without resetting the frame-clear marker
  or dirtying other cameras. Capture this as `RestoreAllVisibility` and removal of
  the view from the ordered snapshot. A later same-frame pass can therefore retain
  visibility that a simple union of PVS sets would not produce.
- `Visible` counts raw selected PVS candidates, including when inversion hides them.
- The dirty sentinel remains `-1`, including the source's hash-collision behavior.

These are compatibility decisions, not corrected source behavior. A future behavior
change needs a separately versioned contract and its own acceptance tests.

## Applying an accepted result

`PassStates` stores applied group values after **each** pass, in pass-major order.
The ordered pass list can contain repeated views and `ViewIndex = -1` for an ordinary
camera without a culling component. Unknown views leave visibility untouched.

`PassVolumeApplications` marks each source `ExecuteQueue` operation. Replay marked
volumes in snapshot order and groups in bake order, even if the boolean did not change:
different groups/volumes can bind the same renderer or explicit Behaviour receiver.
When `Control[0].RestoreApplied` is set, restore all bindings first, including when
there are no render passes. The final queue alone cannot reconstruct these operations.

The evaluator does not make per-camera renderer mutations safe. A rendering adapter
must publish these states through a supported view-aware culling path before graphics
jobs read them; global entity disabling does not satisfy this contract.

## Failure behavior

Invalid snapshot structure throws before scheduling. A damaged encoded cell reports
`InvalidCellData` with the decoder status and pass/volume/cell coordinates. Queued,
applied and view state are reconstructed from the previous buffer, and application
flags are cleared; the previous buffer itself remains read-only. Reject that output.
A job depending on a rejected result reports `InvalidPreviousState`, so a failed
frame cannot silently become a successful publication farther down a job chain.

The ownership and failure contracts above are covered by the package tests. The development
repository records historical acceptance evidence under `Docs/Validation`; that evidence is
optional context and is not included in a Git URL package installation. Rendering, lifecycle
across Unity worlds and bake pipeline acceptance remain independent checks. See
[Compatibility](Compatibility.md) for the validated scope.

## Per-World ownership

`CullingWorldSystem` is explicitly created in its target World. `Schedule` copies all
five input arrays; immutable bake blobs remain borrowed until completion or cancellation.
Only one evaluation can be pending. The returned ticket includes World sequence,
request, source publication version and registry revision. A foreign or cancelled ticket
cannot consume newer work. `Invalidate` rejects pending work and forces the next view
evaluation; corrupt/stale work retains the last accepted state. World destruction completes
jobs and disposes native state before releasing managed receiver registrations.

`Sequential` directly executes the managed evaluator; `Jobs` schedules its Burst job.
Both preserve ordered passes. Publication accepts the native result but does not apply
the final state early to camera-dependent receivers. `TryGetGroupState`, `TryGetViewState`
and `TryGetPassState` copy individual values without transferring ownership of native arrays.

The World-owned `Registry` only controls explicitly registered Behaviour, state-only
Entity or selected simulation enableable-component receivers. It never disables an
entire Entity or graphics enableable component. Last ordered group operation wins for
overlapping receivers. Registration inherits the group's current state immediately;
removal/clear/destruction restores visibility. Full Entity versions and World-scoped
handles prevent stale streaming bindings from targeting replacement entities.
These registration/lifecycle semantics are explicit stage-3 requirements; legacy raw
renderer-list push/pop alone did not perform this immediate inheritance/restoration.

Before any source group application, `AppliedValid`/`StartAppliedValid` are zero.
Keep each renderer or Behaviour's original flags in that state; a bootstrap group
boolean alone does not establish a prior ExecuteQueue operation. Real volume application
or camera-disable restoration makes the state valid. Known groups provide immediate
inheritance to new bindings. `ClearGroup` restores/removes bindings but retains known
group visibility; `ForgetGroup` also forgets it for volume unload/replacement.

`CullingApplySystem.Publish(owner, adapter, volumeRanges, renderBindings)` prepares
copied receiver operations and publishes the matching graphics frame before rendering.
The adapter's ordered pass callback replays every marked volume/group operation,
including unchanged booleans. Preparation does not apply the final frame early.
Out-of-order or invalidated receiver presentations throw before applying operations.
Use `Prepare`/`ApplyPass` for a custom presentation boundary; `Attach(null)` detaches
the render adapter. Reattaching the same adapter preserves event subscription order.

## Parallel evaluation

Jobs mode computes independent view/volume hash contributions in parallel. A stable
view reduction reproduces the exact unchecked 32-bit source hash, including collisions
and the -1 sentinel. Only dirty views that actually have a pass decode their PVS cells.
Each candidate job owns a disjoint byte-mask interval for one view/volume; no workers
OR into a shared bitset. Allocation sizes use checked arithmetic and worker scratch is
bounded by that volume's group count.

An ordered Burst replay consumes the immutable masks, preserving the once-per-frame
clear, inversion, pending/applied distinction, volume operations and first decode error
in source pass order. Candidate errors from unused/cache-hit views do not reject frames.
Sequential mode keeps the direct original sampling/decoder loop as a comparison path.
The returned dependency includes every phase and all scratch disposal. Previous state
remains read-only throughout. Worker-count source traces exercise both modes.
