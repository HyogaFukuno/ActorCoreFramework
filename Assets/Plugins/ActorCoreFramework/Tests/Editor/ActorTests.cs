using System;
using NUnit.Framework;

namespace ActorCoreFramework.Tests
{
    public sealed class ActorTests
    {
        World world = null!;
        CallLog log = null!;

        [SetUp]
        public void SetUp()
        {
            world = new World();
            log = new CallLog();
        }

        [TearDown]
        public void TearDown() => world.Dispose();


        [Test]
        public void AddComponent_DefersBeginPlayUntilTheActorBeginsPlay()
        {
            var actor = new TestActor { Name = "a", Log = log };
            var component = actor.Add(new TestComponent { Name = "c", Log = log });

            Assert.That(component.Owner, Is.SameAs(actor));
            Assert.That(component.BeginPlayCount, Is.Zero);

            world.Register(actor);

            Assert.That(component.BeginPlayCount, Is.EqualTo(1));
            Assert.That(log.Entries, Is.EqualTo(new[] { "c.BeginPlay", "a.BeginPlay" }));
        }

        [Test]
        public void AddComponent_DispatchesBeginPlayImmediatelyWhilePlaying()
        {
            var actor = world.Register(new TestActor { Name = "a", Log = log });
            var component = actor.Add(new TestComponent { Name = "c", Log = log });

            Assert.That(component.BeginPlayCount, Is.EqualTo(1));
        }

        [Test]
        public void AddComponent_ThrowsAfterEndPlay()
        {
            var actor = world.Register(new TestActor());

            world.Destroy(actor);
            world.PostTick(0.016f);

            Assert.Throws<InvalidOperationException>(() => actor.Add(new TestComponent()));
        }

        [Test]
        public void TryGetComponent_FindsByType()
        {
            var actor = new TestActor();
            var component = actor.Add(new TestComponent());
            world.Register(actor);

            Assert.That(actor.TryGetComponent<TestComponent>(out var found), Is.True);
            Assert.That(found, Is.SameAs(component));
            Assert.That(actor.Components, Has.Count.EqualTo(1));
        }

        [Test]
        public void Tick_IsDeliveredToComponentsBeforeTheOwner()
        {
            var actor = new TestActor { Name = "a", Log = log };
            actor.PrimaryActorTick.CanEverTick = true;
            actor.Add(new TestComponent { Name = "c1", Log = log });
            actor.Add(new TestComponent { Name = "c2", Log = log });

            world.Register(actor);
            log.Entries.Clear();

            world.Tick(0.016f);

            Assert.That(log.Entries, Is.EqualTo(new[] { "c1.Tick", "c2.Tick", "a.Tick" }));
        }

        [Test]
        public void Component_IsSkippedWhileDisabled()
        {
            var actor = new TestActor();
            actor.PrimaryActorTick.CanEverTick = true;
            var component = actor.Add(new TestComponent());
            world.Register(actor);

            component.Enabled = false;
            world.Tick(0.016f);

            Assert.That(component.TickCount, Is.Zero);
            Assert.That(actor.TickCount, Is.EqualTo(1));
        }

        [Test]
        public void EndPlayAndDispose_UnwindInReverseOrder()
        {
            var actor = new TestActor { Name = "a", Log = log };
            actor.Add(new TestComponent { Name = "c1", Log = log });
            actor.Add(new TestComponent { Name = "c2", Log = log });

            world.Register(actor);
            log.Entries.Clear();

            world.Destroy(actor);
            world.PostTick(0.016f);

            Assert.That(log.Entries, Is.EqualTo(new[]
            {
                // EndPlayは所有者が先、Componentは合成と逆順
                "a.EndPlay", "c2.EndPlay", "c1.EndPlay",
                // Disposeも合成と逆順で、所有者が最後
                "c2.Dispose", "c1.Dispose", "a.Dispose"
            }));
        }

        [Test]
        public void Dispose_KeepsTheWorldReferenceUntilEveryCallbackHasRun()
        {
            var actor = new TestActor();
            var component = actor.Add(new TestComponent());
            world.Register(actor);

            world.Destroy(actor);
            world.PostTick(0.016f);

            // 後始末からWorld経由の処理を書けるよう、参照を切るのは最後
            Assert.That(component.WorldAtDispose, Is.SameAs(world));
            Assert.That(actor.WorldAtDispose, Is.SameAs(world));
            Assert.That(actor.World, Is.Null);
        }

        [Test]
        public void RemoveComponent_EndsAndDisposesTheComponent()
        {
            var actor = new TestActor { Name = "a", Log = log };
            var component = actor.Add(new TestComponent { Name = "c", Log = log });
            world.Register(actor);
            log.Entries.Clear();

            Assert.That(actor.Remove(component), Is.True);

            Assert.That(log.Entries, Is.EqualTo(new[] { "c.EndPlay", "c.Dispose" }));
            Assert.That(component.IsDisposed, Is.True);
            Assert.That(actor.Components, Is.Empty);
            Assert.That(actor.State, Is.EqualTo(ActorState.Playing));
        }

        [Test]
        public void RemoveComponent_StopsDeliveringTick()
        {
            var actor = new TestActor();
            actor.PrimaryActorTick.CanEverTick = true;
            var component = actor.Add(new TestComponent());
            world.Register(actor);

            world.Tick(0.016f);
            Assert.That(component.TickCount, Is.EqualTo(1));

            actor.Remove(component);
            world.Tick(0.016f);

            Assert.That(component.TickCount, Is.EqualTo(1));
            Assert.That(actor.TickCount, Is.EqualTo(2));
        }

        [Test]
        public void RemoveComponent_BeforeBeginPlaySkipsEndPlay()
        {
            var actor = new TestActor { Name = "a", Log = log };
            var component = actor.Add(new TestComponent { Name = "c", Log = log });

            Assert.That(actor.Remove(component), Is.True);

            // BeginPlayを迎えていないので、対になるEndPlayも配送しない
            Assert.That(log.Entries, Is.EqualTo(new[] { "c.Dispose" }));
            Assert.That(component.EndPlayCount, Is.Zero);
            Assert.That(component.IsDisposed, Is.True);
        }

        [Test]
        public void RemoveComponent_IsIdempotent()
        {
            var actor = new TestActor();
            var component = actor.Add(new TestComponent());
            world.Register(actor);

            Assert.That(actor.Remove(component), Is.True);
            Assert.That(actor.Remove(component), Is.False);

            Assert.That(component.EndPlayCount, Is.EqualTo(1));
            Assert.That(component.DisposeCount, Is.EqualTo(1));
        }

        [Test]
        public void RemoveComponent_RejectsAComponentOwnedByAnotherActor()
        {
            var owner = new TestActor();
            var component = owner.Add(new TestComponent());
            var other = new TestActor();
            world.Register(owner);
            world.Register(other);

            Assert.That(other.Remove(component), Is.False);

            Assert.That(component.IsDisposed, Is.False);
            Assert.That(owner.Components, Has.Count.EqualTo(1));
        }

        [Test]
        public void TryGetComponent_IgnoresAComponentPendingRemoval()
        {
            var actor = new TestActor();
            var component = actor.Add(new TestComponent());
            world.Register(actor);

            actor.Remove(component);

            Assert.That(actor.TryGetComponent<TestComponent>(out _), Is.False);
        }

        [Test]
        public void RemoveComponent_DuringTickIsDeferredWithoutSkippingOthers()
        {
            var actor = new TestActor { Name = "a", Log = log };
            actor.PrimaryActorTick.CanEverTick = true;

            var first = actor.Add(new TestComponent { Name = "c1", Log = log });
            var second = actor.Add(new TestComponent { Name = "c2", Log = log });
            var third = actor.Add(new TestComponent { Name = "c3", Log = log });

            // 自分自身を取り外すComponent。即時に詰めると後続を読み飛ばす
            first.TickAction = c => actor.Remove(c);

            world.Register(actor);
            log.Entries.Clear();

            world.Tick(0.016f);

            // 後続のComponentは同じTickで取りこぼされない。除去はTickの完了後
            Assert.That(log.Entries, Is.EqualTo(new[]
            {
                "c1.Tick", "c2.Tick", "c3.Tick", "a.Tick", "c1.EndPlay", "c1.Dispose"
            }));

            Assert.That(actor.Components, Is.EqualTo(new[] { second, third }));

            world.Tick(0.016f);
            Assert.That(first.TickCount, Is.EqualTo(1));
            Assert.That(second.TickCount, Is.EqualTo(2));
        }

        [Test]
        public void Name_DefaultsToTheTypeName()
        {
            var actor = new TestActor();

            Assert.That(actor.Name, Is.EqualTo(nameof(TestActor)));
        }

        [Test]
        public void Name_FallsBackToTheTypeNameWhenCleared()
        {
            var actor = new TestActor { Name = "player" };
            Assert.That(actor.Name, Is.EqualTo("player"));

            actor.Name = "";
            Assert.That(actor.Name, Is.EqualTo(nameof(TestActor)));
        }

        [Test]
        public void Id_IsUniqueAndIncreasing()
        {
            var first = new TestActor();
            var second = new TestActor();

            Assert.That(first.Id, Is.GreaterThan(0));
            Assert.That(second.Id, Is.GreaterThan(first.Id));
        }

        [Test]
        public void ToString_CombinesNameAndId()
        {
            var actor = new TestActor { Name = "player" };

            Assert.That(actor.ToString(), Is.EqualTo($"player#{actor.Id}"));
        }
    }
}
