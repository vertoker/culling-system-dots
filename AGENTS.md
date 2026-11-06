# Culling System DOTS: agent guide

Universal instructions for agents working in this checkout. Read this file first, then the
nearest `AGENTS.md` governing each file you change and its parent instructions. Nested files
are not necessarily loaded automatically. Read only the linked rules relevant to the task.

## Product and boundaries

This repository develops a Unity visibility-culling **package**, not a game. The root is a
Unity development and validation project; `Assets/CullingSystemDOTS` is the complete Git URL
installable package. Its own [guide](Assets/CullingSystemDOTS/AGENTS.md) also works without this
checkout. Production code, resources, package tests, documentation and importable samples must
remain inside that package boundary.

GPU compute baking is the primary path, with a genuine CPU Jobs/Burst fallback. Unity compiles
all baker code. Rendering uses stock Entities Graphics through UPM. Public contracts and
limitations belong in [package documentation](Assets/CullingSystemDOTS/Documentation~/AGENTS.md),
not assumptions imported from another project.

## Working rules

- Work and commit on `master` only. Do not create branches or additional worktrees. If the
  checkout is elsewhere, report that before editing; do not switch or move existing work.
- Commit completed, verified task changes yourself. Stage exact owned paths and preserve
  unrelated edits and deletions. Local commit authorization does not authorize pushing,
  publishing, tagging, or rewriting shared history.
- Write repository prose, comments, identifiers and new agent guidance in English. Follow the
  user's current language preference in conversation. Keep explanations concrete and concise.
- Prefer source and text changes. Preserve file encoding, newline style, asset GUIDs and
  serialized compatibility. Explain a contract change before replacing established behavior.
- Do not introduce native baker binaries, paid source copies, patched Unity packages, or a
  dependency on the root project's scenes, settings, tools or MCP package in shipped code.
- Use existing authorization to finish the task. Ask when missing information changes the
  outcome or an action exceeds scope; continue independent work while waiting. Do not add
  approval gates merely because a tool or optional workflow suggests them.
- Preserve the user's live scene. Never save, discard, or close it just to automate a task.
  One operator owns a Unity instance at a time. Do not edit scripts or mutate the Editor during
  a test run. Unity CLI/Pipeline tooling is disabled in this repository.
- Use available tools rather than assuming a particular agent client, plugin or shell. Optional
  skills assist the workflow; they do not grant permissions or override these instructions.

## Read when relevant

| Task | Guidance |
|---|---|
| Start work, hand off, or maintain durable knowledge | [Agent workflow](Docs/AGENT-WORKFLOW.md) |
| Package layout, dependencies, assemblies or public API | [Package boundaries](Docs/AgentRules/package-boundaries.md) |
| Unity interaction, assets or metadata | [Unity workflow](Docs/AgentRules/unity-editor.md) |
| Code or comments | [Code guidance](Docs/AgentRules/code.md) |
| Select checks or interpret results | [Testing](Docs/TESTING.md) |
| Markdown, instructions or documentation | [Writing guidance](Docs/AgentRules/markdown.md) |
| Reusable repository skills | [Skill catalog](.agents/README.md) |

## Folder map

| Area | Start here |
|---|---|
| Shipped package and its internal layers | [Package guide](Assets/CullingSystemDOTS/AGENTS.md) |
| Root-only development content | [Assets guide](Assets/AGENTS.md) |
| Rendering and delivery integration tests | [Development tests](Assets/Tests/AGENTS.md) |
| Installable-sample development mirror | [Delivery sample](Assets/DeliverySample/AGENTS.md) |
| Synthetic fixture generation | [Delivery tools](Assets/DeliveryTools/AGENTS.md) |
| Portable repository tooling | [Tools](Tools/AGENTS.md) |
| Maintainer docs, plans and validation evidence | [Docs](Docs/AGENTS.md) |
| Root project package configuration | [Packages](Packages/AGENTS.md) |
| Development project settings | [Project settings](ProjectSettings/AGENTS.md) |

## Completion

Run the checks appropriate to the change and report observed results, failures and limits.
Old reports are historical evidence, not a fresh run. Documentation-only changes need document
and link checks, not a full Unity suite. Give manual steps when a remaining behavior requires
visual inspection. Never claim a test, build, platform or performance result that was not observed.
