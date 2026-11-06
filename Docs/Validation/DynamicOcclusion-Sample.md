# Dynamic City sample and Editor correctness checkpoint

Source: the commit introducing this report, based on `fa9584a`. Unity 6000.6.8f1, Entities/Graphics
6.6, URP 17.6, Windows Editor DX12, RTX 4060 Ti. Player validation/performance is a later gate.

The importable Dynamic City sample has a development mirror in Assets/DeliveryDynamic. Both
copies have identical files and Unity-generated GUIDs. The scene references only its own assets
and package resources; a dependency inventory found zero outside sample/package Assets references.
The scene builder refuses existing content and preserved the host SampleScene and its clean flag.

Default content is a 128 by 128 city with sixteen two-storey buildings, 689 separately registered
wall/floor/roof/door pieces including a rotated non-grid fixture, and 512 independently simulated
ECS agents. The sample owns its World. Controls provide a guided/first-person camera, piece
selection/removal/restoration, door operations, seeded edits, agent pause, renderer toggle and
resource diagnostics. Shader resources, ordinary/indirect materials, cube, scene and URP settings
are serialized sample content. Runtime does not change consumer pipeline settings.

The parallel movement job exposed a real capture bug: a manually invoked SystemBase helper had
not imported external component dependencies. Completing that helper's own Dependency was
insufficient. Capture now explicitly completes writes for its five read component types before
the synchronous numeric job. A package regression schedules an external transform writer.

The ordinary sample shader has DOTS transforms, optional per-instance BaseColor and a ShadowCaster
pass. Standard URP Unlit lacks the latter. Matching linear vector colors avoid comparing two
different material/lighting features. Readback is assertion-only, outside frame measurements.

| Check | Job | Result |
| --- | --- | --- |
| Missing construction RED | `d8df8e3c3b724c1883b514315c586898` | 0/1, zero pieces |
| Parallel-writer capture RED | `002154bd7ced4c168dad99c4bae37bf5` | 0/1, pending LocalToWorld writer |
| Piece edits and independent simulation | `c1a892bc68db43fd846af06120fdcbf7` | 1/1 |
| Frozen color/shadow oracle | `8ac74e48338c432b93639954c19914c2` | 1/1, eight states |
| Missing ordinary ID metadata RED | `3bab47017c5c499ea446a020b06960c7` | 0/1, no reference IDs |
| Unique-ID and interior oracle | `1ae03d420396449ea4099d2c6b8fba66` | 1/1, eight states |
| All affected EditMode, including legacy bake/delivery | `c4164009178e4395a1287a5c65a4b4a6` | 258/258, no failures/skips |
| Coupled functional PlayMode, including the sample | `3cd142f8088247f7b33b72d2b75ad28f` | 56/56, no failures/skips |

The eight frozen states cover closed/open doors, removed wall/floor, a camera in/near geometry,
an indoor viewpoint, teleport and advanced agent poses. The oracle requires actual GPU occlusion,
assigns unique object IDs to both routes, rejects complete loss of any reference-visible ID and
rejects changed uniform 3-by-3 reference interiors with a one-byte channel tolerance. Nonuniform
boundary pixels are not an assertion of exact raster/shadow-edge equality. The earlier full run
`e2e56b8a0df74b8187e3ca37da82b08c` completed 56 without recorded failures but returned no result
summary; the quiet complete-summary run above is the acceptance evidence.

Portable tooling: Python 35/35. The Git consumer helper now imports both samples and preserves
their committed scene GUIDs; its regression first failed because Dynamic City was absent, then
passed 2/2 after changing archive extraction. Documentation validation covers 110 Markdown files.
The opt-in Player harness is compiled, but has not yet established a built-Player result here.

Remaining gates are actual Git installation, built-Player execution, whole-feature review and
the requested subsequent performance/stress comparison. See the
[implementation plan](../Superpowers/Plans/2026-11-06-dynamic-geometric-occlusion-plan.md).
