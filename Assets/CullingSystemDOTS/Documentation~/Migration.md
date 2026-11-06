# Data and scene integration

Bind authoring volumes/groups explicitly and keep stable group indices. Store renderers, LODs and material entities in a SubScene; keep runtime camera authoring on its GameObject. The Editor bake asset importer accepts the package's validated compressed cell layout through managed input records and does not load an external assembly.

There is no external scene importer. Create authoring components using Authoring.md or the Basic sample. Runtime Behaviour and enableable ECS receivers require explicit registration in the receiving World. Culling does not disable whole entities or simulation.
