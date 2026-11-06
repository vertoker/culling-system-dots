using NUnit.Framework;
using CullingSystemDOTS.Core;
using CullingSystemDOTS.Runtime;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace CullingSystemDOTS.Tests
{
    public class CullingRegistryTests
    {
        public struct LogicReceiver : IComponentData, IEnableableComponent { }
        private static CullingGroupKey Key(int index = 0) => new CullingGroupKey(new uint4(1, 0, 0, 0), (ushort)index);

        [Test]
        public void RegisterInHiddenGroupAppliesCurrentState()
        {
            using var world = new World("Hidden registration");
            var registry = world.GetOrCreateSystemManaged<CullingWorldSystem>().Registry;
            var entity = world.EntityManager.CreateEntity(typeof(LogicReceiver));
            registry.Apply(Key(), false);
            registry.RegisterEntity<LogicReceiver>(Key(), entity);
            Assert.That(world.EntityManager.IsComponentEnabled<LogicReceiver>(entity), Is.False);
            Assert.That(world.EntityManager.IsEnabled(entity), Is.True);
            Assert.That(world.EntityManager.GetComponentData<CullingGroupState>(entity).Visible, Is.Zero);
        }

        [Test]
        public void StreamingReloadRestoresBindings()
        {
            using var world = new World("Streaming receivers");
            var registry = world.GetOrCreateSystemManaged<CullingWorldSystem>().Registry;
            var entity = world.EntityManager.CreateEntity(typeof(LogicReceiver));
            var binding = registry.RegisterEntity<LogicReceiver>(Key(), entity);
            registry.Apply(Key(), false);
            Assert.That(registry.Remove(binding), Is.True);
            Assert.That(world.EntityManager.IsComponentEnabled<LogicReceiver>(entity), Is.True);
            Assert.That(registry.Remove(binding), Is.False);
            registry.RegisterEntity<LogicReceiver>(Key(), entity);
            Assert.That(world.EntityManager.IsComponentEnabled<LogicReceiver>(entity), Is.False);
            registry.ClearGroup(Key());
            Assert.That(world.EntityManager.IsComponentEnabled<LogicReceiver>(entity), Is.True);
            Assert.That(registry.Count, Is.Zero);
            registry.RegisterEntity<LogicReceiver>(Key(), entity);
            Assert.That(world.EntityManager.IsComponentEnabled<LogicReceiver>(entity), Is.False, "Clearing bindings must retain the group's known hidden state.");
        }

        [Test]
        public void ReusedEntityIndexDoesNotBindOldGroup()
        {
            using var world = new World("Entity version guard");
            var registry = world.GetOrCreateSystemManaged<CullingWorldSystem>().Registry;
            var old = world.EntityManager.CreateEntity(typeof(LogicReceiver));
            registry.RegisterEntity<LogicReceiver>(Key(), old);
            registry.Apply(Key(), false);
            Assert.That(world.EntityManager.IsComponentEnabled<LogicReceiver>(old), Is.False);
            world.EntityManager.DestroyEntity(old);
            var replacement = world.EntityManager.CreateEntity(typeof(LogicReceiver));
            Assert.That(replacement.Index, Is.EqualTo(old.Index));
            Assert.That(replacement.Version, Is.Not.EqualTo(old.Version));
            registry.Apply(Key(), false);
            Assert.That(world.EntityManager.IsComponentEnabled<LogicReceiver>(replacement), Is.True);
            Assert.That(world.EntityManager.HasComponent<CullingGroupState>(replacement), Is.False);
        }

        [Test]
        public void BehaviourAndOverlappingGroupsFollowLastOperationAndRestoreOnWorldDestruction()
        {
            var go = new GameObject("Explicit behaviour receiver");
            var receiver = go.AddComponent<AudioSource>();
            receiver.enabled = false;
            var world = new World("Receiver order");
            try
            {
                var registry = world.GetOrCreateSystemManaged<CullingWorldSystem>().Registry;
                registry.RegisterBehaviour(Key(), receiver); registry.RegisterBehaviour(Key(1), receiver);
                Assert.That(receiver.enabled, Is.False, "No source group operation has run yet; preserve the receiver's initial flag.");
                registry.Apply(Key(), false); Assert.That(receiver.enabled, Is.False);
                registry.Apply(Key(1), true); Assert.That(receiver.enabled, Is.True);
                registry.Apply(Key(), false); Assert.That(receiver.enabled, Is.False);
                world.Dispose();
                Assert.That(receiver.enabled, Is.True);
                Assert.That(registry.Count, Is.Zero);
            }
            finally { if (world.IsCreated) world.Dispose(); Object.DestroyImmediate(go); }
        }

        [Test]
        public void ForeignWorldHandleCannotRemoveBinding()
        {
            using var a = new World("Receiver A"); using var b = new World("Receiver B");
            var first = a.GetOrCreateSystemManaged<CullingWorldSystem>().Registry;
            var second = b.GetOrCreateSystemManaged<CullingWorldSystem>().Registry;
            var token = first.RegisterStateEntity(Key(), a.EntityManager.CreateEntity());
            second.RegisterStateEntity(Key(), b.EntityManager.CreateEntity());
            Assert.That(second.Remove(token), Is.False); Assert.That(second.Count, Is.EqualTo(1));
        }

        [Test]
        public void DisableCameraRestoresLegacyState()
        {
            using var world = new World("Receiver camera disable");
            var registry = world.GetOrCreateSystemManaged<CullingWorldSystem>().Registry;
            var entity = world.EntityManager.CreateEntity(typeof(LogicReceiver));
            registry.RegisterEntity<LogicReceiver>(Key(), entity);
            registry.Apply(Key(), false); registry.RestoreAll();
            Assert.That(world.EntityManager.IsComponentEnabled<LogicReceiver>(entity), Is.True);
            registry.RegisterStateEntity(Key(), entity);
            Assert.That(world.EntityManager.GetComponentData<CullingGroupState>(entity).Visible, Is.EqualTo(1));
        }
    }
}
