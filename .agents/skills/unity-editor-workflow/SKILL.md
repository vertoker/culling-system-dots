---
name: unity-editor-workflow
description: Use when interacting with live Unity state, importing assets or metadata, running Editor tests, or changing serialized assets.
---

# Operate the intended Unity Editor

Read [Unity workflow](../../../Docs/AgentRules/unity-editor.md) and
[testing](../../../Docs/TESTING.md). This skill adds no permissions and requires no particular
agent client. Prefer the connected Coplay MCP capabilities that are actually available.

1. Discover instances and confirm the exact project path. Pin the intended instance, then inspect
   readiness, Play Mode, tests and relevant scene/asset state. Do not act on a guessed instance.
2. Respect the current owner. During tests, wait without editing scripts or mutating the Editor.
   A disconnect does not prove a hang; recover/inspect the existing operation before rerunning.
3. Perform only the in-scope operation. Preserve the user's scene; never save/discard it to clear
   a dialog. Use appropriate live APIs for assets and let Unity create new imported metadata.
4. Await readiness after refresh/compile. Inspect errors and test discovery before dependent work.
   For tests, retain the job identifier and read actual completion and counts.
5. Verify the result and report it. Restore routing after an owned disposable-consumer check.
   Before cleanup, confirm exact owned paths and that their processes have exited.

If the tool is unavailable, continue independent source/document work and identify the precise
live check still missing. Do not install CLI/Pipeline, launch batchmode, or open another project as
an implicit fallback.
