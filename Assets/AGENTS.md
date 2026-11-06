# AGENTS.md - `Assets`

Read [the repository guide](../AGENTS.md) first. Asset ownership has two distinct boundaries:

| Folder | Responsibility |
|---|---|
| [CullingSystemDOTS](CullingSystemDOTS/AGENTS.md) | Complete shipped UPM package |
| [DeliverySample](DeliverySample/AGENTS.md) | Development mirror of the importable sample |
| [DeliveryTools](DeliveryTools/AGENTS.md) | Development-only fixture generation |
| [Tests](Tests/AGENTS.md) | Development integration tests and synthetic assets |
| [Scenes](Scenes/AGENTS.md) | Root-only development scenes |
| [Settings](Settings/AGENTS.md) | Root-only rendering settings |

Do not move production dependencies into development folders. Preserve unrelated user deletions;
missing template/tutorial assets are not an invitation to restore them. Unity generates metadata
for new imported instructions; never invent their GUIDs.
