# Rendering integration

The package uses unmodified Entities Graphics 6.6.0 through UPM. It does not add APIs to Unity assemblies or patch cached package files.

The World-owned rendering adapter prepares ordered visibility masks from accepted Core results. At the beginning of each camera it completes tracked readers and applies that camera's mask to the standard enableable MaterialMeshInfo render component. At camera end it completes readers before restoring the enclosing scope or the registered baseline. Culling jobs cannot observe a later camera changing their input bits. The entity itself, physics and ordinary logic remain enabled.

Shadow behavior is represented by a graphics-only ShadowsOnly companion where required. The camera's visibility mask applies to its rendering and shadow work. LOD and material selection still follow the ordinary Entities Graphics path. Camera order and publication shape errors remain explicit faults.

Authoring stores each renderer's primary entity and uses the public AdditionalEntitiesBakingData buffer to enumerate additional render entities created by that renderer's baker. It filters by the authoring component identity and MaterialMeshInfo, so unrelated additional entities are not registered. It does not depend on internal renderer baking component types.

Synchronization before mask changes can increase main-thread stalls. This is an explicit tradeoff for the stock UPM integration; no speedup or allocation-free claim is made. Functional pixel tests cover camera order, shadows, materials/LODs, automatic URP stacks, streaming and simulation lifetime. Other render pipelines, nested/custom rendering and other package versions require validation.
