# Unity Editor, assets and metadata

Read [the repository guide](../../AGENTS.md). The user's task authorizes necessary in-scope work;
a tool, role, or optional skill does not broaden that scope.

## Select the correct Editor

Prefer the connected Coplay Unity MCP for live Editor operations. Inspect available instances,
confirm the project path matches this checkout, and pin the exact instance before actions.
Discover available capabilities; do not assume another client's tool names or stale instance IDs.
Keep one operator per instance. If the connection is unavailable, continue independent source
work and report the exact live verification that remains unavailable.

Unity CLI/Pipeline tooling is disabled here. Do not install or use a CLI wrapper, launch batchmode
Unity, or create a second Editor to work around a busy instance. A deliberately required, owned
installation consumer is separate from the main Editor and follows `Tools/CreateDeliveryConsumer.py`.

## Preserve user state

- Inspect readiness, Play Mode and test state before mutations. Do not edit C# or assets, import,
  compile, build or change scenes while tests are running. Wait for their actual completion.
- Never save or discard the user's scene to clear a modal. Avoid scene replacement in the main
  Editor; owned additive scenes or a disposable consumer can isolate a necessary check.
- Never invoke broad `SaveAssets` or scene-save operations merely to persist new Markdown.
- A disconnected tool or lost test job ID is not evidence that Unity is hung. Inspect the state
  and recover the existing operation before starting another one.

## Edit assets deliberately

Prefer source/text changes. For serialized assets, use the appropriate Editor API when live state
owns the asset. Report changed scenes, prefabs, materials or settings and their intended effect.
Do not perform broad YAML edits or regenerate sample content to resolve an unrelated problem.

Unity creates `.meta` files for new imported assets. Never invent GUIDs or hand-write metadata.
Preserve existing metadata on moves and renames; deleting or changing a GUID can break consumers.
New Markdown under ordinary `Assets` directories also needs Unity-generated metadata. Folders
ending in `~` follow their existing non-imported documentation/sample conventions.

After an authorized refresh, wait for readiness and inspect compilation errors if source changed.
Confirm the expected files were imported before staging them. A successful request to refresh is
not by itself evidence of completed compilation or test discovery.

Delete temporary content only after confirming resolved paths remain inside the exact owned
destination and its Editor/build/player processes have stopped. Inspect each operation's result
before performing dependent cleanup. Never use broad Git-clean patterns on the project root.
