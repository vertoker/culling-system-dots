# Data and scene integration

Bind authoring volumes/groups explicitly and keep stable group indices. Store renderers, LODs and material entities in a SubScene; keep runtime camera authoring on its GameObject. The Editor bake asset importer accepts the package's validated compressed cell layout through managed input records and does not load an external assembly.

There is no external scene importer. Create authoring components using Authoring.md or the Basic sample. Runtime Behaviour and enableable ECS receivers require explicit registration in the receiving World. Culling does not disable whole entities or simulation.

## Retired dynamic geometric experiment

The unreleased algorithm-3 dynamic geometric renderer and Dynamic City sample were removed after
their Player comparison failed the performance gate. Dynamic namespaces, shader resources and
registration APIs are no longer shipped. Remove imported Dynamic City sample copies and custom
references to that experimental API before updating. There is no automatic conversion to a new
dynamic algorithm. Static bake assets, authoring, the runtime evaluator and the Basic sample retain
their existing contracts; this retirement alone does not require rebaking static data.
