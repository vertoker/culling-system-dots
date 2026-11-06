# ECS authoring and streaming

Attach `CullingVolumeAuthoring` and assign an imported immutable `CullingBakeAsset`.
Set its size, layers and sampling policies. Each `CullingGroupAuthoring` explicitly
references that volume, its original group index and renderer list. Group indices
are never regenerated from renderer enumeration. Empty groups still keep their slot.

Attachable MonoBehaviours live in the runtime assembly; Bakers and their managed
renderer mapping live in the Editor assembly. The volume baker registers its blob
with Unity's BlobAssetStore. Scene object GlobalObjectId produces a stable instance
identity independent of the shared bake identity. Unsaved editor fixtures use the
object's session identity until they are saved.

The binding baking system consumes the stock renderer baker's temporary mapping
for both MeshRenderer and SkinnedMeshRenderer. It records all generated graphics
entities, including additional material entities, in a remappable Entity buffer.
The local Graphics fork exposes these existing temporary mapping types; no private
reflection or legacy assembly is needed by production Bakers or runtime code.

`CullingVolume`, `CullingGroupDefinition` and `CullingBakedRenderBinding` are unmanaged
runtime components. Their blob and Entity references survive SubScene serialization
and streaming. The development fixture loads/unloads twice through SceneSystem,
checking stable instance identities, immutable bake data and all material bindings.

`CullingViewCaptureSystem` discovers loaded ECS volumes and complete group buffers.
Register cameras with stable nonzero IDs and configure their returned view registration.
`Capture` copies a native frame on the main thread; callers must keep borrowed scene
blobs alive through evaluation. `EvaluateAndRender` captures, completes evaluation and
publishes through the World's owned graphics adapter before returning. Call it after
transform updates and before camera rendering, with an explicit pass order for custom
camera stacks. Equal-depth default passes follow camera registration order.

`EvaluateAndPublish(..., adapter: null)` supports simulation-only ordered receiver
replay. A custom adapter remains caller-owned. The World's default adapter, receiver
registry, shadow companions and native frame buffers are released on World destruction.
Never yield or permit scene unloading between raw `Capture` and evaluation completion.

Volume registration order is explicit; equal-order volumes keep their observed loading
order. Group buffers retain original ushort slots, including empty slots. Portals use
`CullingPortalLink` and their own registration order. Capture reads world position and
composes LocalTransform quaternions through parents without applying transform scale
to volume size. Baked cell count and cell size remain immutable.

Original-On bindings automatically get graphics-only ShadowsOnly companions. The helper
copies exactly one main entity through Unity 6.6's public explicit-set Instantiate
overload, which ignores LinkedEntityGroup, then immediately strips simulation components.
Graphics, material properties and deformation data remain; an identity child transform
follows the main. This isolated overload is deprecated and must be checked on Unity
upgrades. It never clones gameplay hierarchies or runs simulation on an intermediate copy.
Unused companions are retired after successful publication and dead mains are pruned.

Camera removal requests source-equivalent visibility restoration without forcing all
surviving view hashes dirty. Registration/data/configuration changes invalidate captured
work; camera layer/radius edits retain the documented legacy hash behavior unless the
caller invokes SetDirty. Inversion invokes SetDirty automatically, like the source setter.

Stage 3's combined Editor gate passes: 221 EditMode and 10 PlayMode tests, including
parallel independent candidates with ordered replay. Deformation, arbitrary custom camera
stacks, Player platforms and transform-boundary precision require broader acceptance.
