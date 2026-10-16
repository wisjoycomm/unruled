using System;
using NUnit.Framework;
using Unruled.Core.Ecs;

namespace Unruled.Tests.Core.Ecs
{
    public class EcsTests
    {
        private struct Health
        {
            public int Value;
        }

        private struct Tag
        {
        }

        [Test]
        public void Create_IssuesSequentialIds_DefaultIsNull()
        {
            var world = new World(initialCapacity: 2);
            var a = world.Create();
            var b = world.Create();
            var c = world.Create();

            Assert.AreEqual(new EntityId(0, 1), a);
            Assert.AreEqual(new EntityId(2, 1), c);
            Assert.AreEqual(3, world.AliveCount);
            Assert.IsTrue(default(EntityId).IsNull);
            Assert.IsFalse(world.IsAlive(default));
            Assert.IsTrue(world.IsAlive(b));
        }

        [Test]
        public void Destroy_ReusesIndexWithNewGeneration_OldIdIsStale()
        {
            var world = new World();
            var health = world.CreateStore<Health>();
            var a = world.Create();
            health.Add(a, new Health { Value = 5 });

            world.Destroy(a);
            var reused = world.Create();

            Assert.AreEqual(a.Index, reused.Index);
            Assert.AreEqual(a.Generation + 1, reused.Generation);
            Assert.IsFalse(world.IsAlive(a));
            Assert.IsFalse(health.Has(a));
            Assert.IsFalse(health.Has(reused));
            Assert.Throws<InvalidOperationException>(() => world.Destroy(a));
        }

        [Test]
        public void Destroy_RemovesEntityFromEveryStore()
        {
            var world = new World();
            var health = world.CreateStore<Health>();
            var tags = world.CreateStore<Tag>();
            var a = world.Create();
            var b = world.Create();
            health.Add(a, new Health { Value = 1 });
            health.Add(b, new Health { Value = 2 });
            tags.Add(a, default);

            world.Destroy(a);

            Assert.AreEqual(1, health.Count);
            Assert.AreEqual(b, health.EntityAt(0));
            Assert.AreEqual(2, health.At(0).Value);
            Assert.AreEqual(0, tags.Count);
        }

        [Test]
        public void Store_IteratesInAscendingEntityOrder_RegardlessOfAddOrder()
        {
            var world = new World();
            var health = world.CreateStore<Health>(initialCapacity: 1);
            var ids = new EntityId[6];
            for (int i = 0; i < ids.Length; i++)
                ids[i] = world.Create();

            foreach (int i in new[] { 4, 1, 5, 0, 3, 2 })
                health.Add(ids[i], new Health { Value = i * 10 });
            health.Remove(ids[3]);

            CollectionAssert.AreEqual(new[] { 0, 1, 2, 4, 5 }, IndicesOf(health));
            for (int i = 0; i < health.Count; i++)
                Assert.AreEqual(health.EntityAt(i).Index * 10, health.At(i).Value);
            Assert.AreEqual(40, health.Get(ids[4]).Value);
        }

        [Test]
        public void Get_ReturnsReference_WritesAreVisible()
        {
            var world = new World();
            var health = world.CreateStore<Health>();
            var a = world.Create();
            health.Add(a, new Health { Value = 1 });

            health.Get(a).Value += 41;

            Assert.AreEqual(42, health.Get(a).Value);
        }

        [Test]
        public void Store_RejectsDuplicateMissingAndStaleAccess()
        {
            var world = new World();
            var health = world.CreateStore<Health>();
            var a = world.Create();
            health.Add(a, default);

            Assert.Throws<InvalidOperationException>(() => health.Add(a, default));
            Assert.Throws<ArgumentException>(() => health.Add(default, default));
            Assert.Throws<InvalidOperationException>(() => health.Get(world.Create()));
            Assert.Throws<InvalidOperationException>(() => health.Get(new EntityId(a.Index, a.Generation + 1)));
            Assert.Throws<ArgumentOutOfRangeException>(() => health.At(1));
        }

        private static int[] IndicesOf<T>(ComponentStore<T> store) where T : struct
        {
            var result = new int[store.Count];
            for (int i = 0; i < store.Count; i++)
                result[i] = store.EntityAt(i).Index;
            return result;
        }
    }
}
