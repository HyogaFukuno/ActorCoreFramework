using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace ActorCoreFramework.Tests
{
    public sealed class WorldTests
    {
        World world = null!;

        [SetUp]
        public void SetUp() => world = new World();

        [TearDown]
        public void TearDown() => world.Dispose();


        static TestActor Ticking(string name = "actor", TickGroup group = TickGroup.Tick, int priority = 0)
        {
            var actor = new TestActor { Name = name };
            actor.PrimaryActorTick.CanEverTick = true;
            actor.PrimaryActorTick.Group = group;
            actor.PrimaryActorTick.Priority = priority;

            return actor;
        }


        [Test]
        public void Register_DispatchesBeginPlayAndTracksActor()
        {
            var actor = world.Register(new TestActor());

            Assert.That(actor.BeginPlayCount, Is.EqualTo(1));
            Assert.That(actor.State, Is.EqualTo(ActorState.Playing));
            Assert.That(actor.World, Is.SameAs(world));
            Assert.That(world.Actors, Has.Member(actor));
        }

        [Test]
        public void Register_RejectsAlreadyRegisteredActor()
        {
            var actor = world.Register(new TestActor());

            Assert.Throws<InvalidOperationException>(() => world.Register(actor));
        }

        [Test]
        public void Register_RollsBackWhenBeginPlayThrows()
        {
            var actor = new TestActor { BeginPlayAction = _ => throw new InvalidOperationException("boom") };

            Assert.Throws<InvalidOperationException>(() => world.Register(actor));

            // 失敗したActorをPlayingのままWorldに残さない
            Assert.That(world.Actors, Has.No.Member(actor));
            Assert.That(actor.State, Is.EqualTo(ActorState.Disposed));
            Assert.That(actor.DisposeCount, Is.EqualTo(1));
        }

        [Test]
        public void Spawn_ThrowsWhenFactoryReturnsNull()
        {
            Assert.Throws<InvalidOperationException>(() => world.Spawn<TestActor>(() => null!));
        }

        [Test]
        public void Register_ThrowsAfterDispose()
        {
            world.Dispose();

            Assert.Throws<ObjectDisposedException>(() => world.Register(new TestActor()));
        }


        [Test]
        public void Destroy_IsDeferredUntilPostTick()
        {
            var actor = world.Register(Ticking());

            world.Destroy(actor);

            // 予約直後はまだPlaying。ただしIsPendingDestroyで判別できる
            Assert.That(actor.IsPendingDestroy, Is.True);
            Assert.That(actor.State, Is.EqualTo(ActorState.Playing));
            Assert.That(world.Actors, Has.Member(actor));

            world.PostTick(0.016f);

            Assert.That(actor.State, Is.EqualTo(ActorState.Disposed));
            Assert.That(actor.LastEndPlayReason, Is.EqualTo(EndPlayReason.Destroyed));
            Assert.That(actor.DisposeCount, Is.EqualTo(1));
            Assert.That(world.Actors, Has.No.Member(actor));
        }

        [Test]
        public void Destroy_StopsTickBeforeActualDisposal()
        {
            var actor = world.Register(Ticking());

            world.Tick(0.016f);
            Assert.That(actor.TickCount, Is.EqualTo(1));

            world.Destroy(actor);

            // 破棄はPostTickまで遅延するが、その間のTickは配送しない
            world.Tick(0.016f);
            Assert.That(actor.TickCount, Is.EqualTo(1));
        }

        [Test]
        public void Destroy_IsIdempotent()
        {
            var actor = world.Register(new TestActor());

            world.Destroy(actor);
            world.Destroy(actor);
            world.PostTick(0.016f);

            Assert.That(actor.EndPlayCount, Is.EqualTo(1));
            Assert.That(actor.DisposeCount, Is.EqualTo(1));
        }

        [Test]
        public void Destroy_IgnoresActorOwnedByAnotherWorld()
        {
            using var other = new World();
            var actor = other.Register(new TestActor());

            world.Destroy(actor);
            world.PostTick(0.016f);

            Assert.That(actor.IsPendingDestroy, Is.False);
            Assert.That(actor.State, Is.EqualTo(ActorState.Playing));
        }

        [Test]
        public void Destroy_DuringFlushIsHandledInTheSameFrame()
        {
            var first = world.Register(new TestActor { Name = "first" });
            var second = world.Register(new TestActor { Name = "second" });

            // EndPlayの中から別のActorを破棄しても、同じフレームで回収される
            first.EndPlayAction = _ => world.Destroy(second);

            world.Destroy(first);
            world.PostTick(0.016f);

            Assert.That(second.State, Is.EqualTo(ActorState.Disposed));
            Assert.That(world.Actors, Is.Empty);
        }


        [Test]
        public void Dispose_EndsAllActorsWithWorldShutdown()
        {
            var actor = world.Register(new TestActor());

            world.Dispose();

            Assert.That(actor.LastEndPlayReason, Is.EqualTo(EndPlayReason.WorldShutdown));
            Assert.That(actor.State, Is.EqualTo(ActorState.Disposed));
            Assert.That(world.Actors, Is.Empty);
        }

        [Test]
        public void Dispose_IsIdempotent()
        {
            var actor = world.Register(new TestActor());

            world.Dispose();
            world.Dispose();

            Assert.That(actor.EndPlayCount, Is.EqualTo(1));
            Assert.That(actor.DisposeCount, Is.EqualTo(1));
        }

        [Test]
        public void Tick_DoesNothingAfterDispose()
        {
            var actor = world.Register(Ticking());

            world.Dispose();
            world.Tick(0.016f);
            world.FixedTick(0.016f);
            world.PostTick(0.016f);

            Assert.That(actor.TickCount, Is.Zero);
        }

        [Test]
        public void GetActors_CollectsOnlyTheMatchingType()
        {
            var first = world.Register(new TestActor());
            var second = world.Register(new TestActor());
            world.Register(new OtherTestActor());

            var results = new List<TestActor>();

            Assert.That(world.GetActors(results), Is.EqualTo(2));
            Assert.That(results, Is.EqualTo(new[] { first, second }));
        }

        [Test]
        public void GetActors_SkipsActorsPendingDestroy()
        {
            var kept = world.Register(new TestActor());
            var doomed = world.Register(new TestActor());

            world.Destroy(doomed);

            // 破棄予約済みのActorは、実際の破棄を待たずに検索対象から外れる
            var results = new List<TestActor>();
            Assert.That(world.GetActors(results), Is.EqualTo(1));
            Assert.That(results, Is.EqualTo(new[] { kept }));
        }

        [Test]
        public void Dispose_EndsEveryActorEvenWhenOneEndPlayThrows()
        {
            var first = world.Register(new TestActor { Name = "first" });
            var thrower = world.Register(new TestActor
            {
                Name = "thrower",
                EndPlayAction = _ => throw new InvalidOperationException("boom")
            });
            var last = world.Register(new TestActor { Name = "last" });

            // 例外は最初の1件だけ呼び出し元へ伝える
            Assert.Throws<InvalidOperationException>(() => world.Dispose());

            // 1体の失敗で残りが後始末を受け取れなくなってはいけない
            Assert.That(first.State, Is.EqualTo(ActorState.Disposed));
            Assert.That(thrower.State, Is.EqualTo(ActorState.Disposed));
            Assert.That(last.State, Is.EqualTo(ActorState.Disposed));
            Assert.That(first.EndPlayCount, Is.EqualTo(1));
            Assert.That(last.EndPlayCount, Is.EqualTo(1));
            Assert.That(world.Actors, Is.Empty);
        }

        [Test]
        public void FlushPendingDestroy_ContinuesWhenOneEndPlayThrows()
        {
            var thrower = world.Register(new TestActor
            {
                Name = "thrower",
                EndPlayAction = _ => throw new InvalidOperationException("boom")
            });
            var other = world.Register(new TestActor { Name = "other" });

            world.Destroy(thrower);
            world.Destroy(other);

            Assert.Throws<InvalidOperationException>(() => world.PostTick(0.016f));

            Assert.That(thrower.State, Is.EqualTo(ActorState.Disposed));
            Assert.That(other.State, Is.EqualTo(ActorState.Disposed));

            // 予約が残ったままだと次フレームで破棄済みActorを再処理してしまう
            Assert.That(world.Actors, Is.Empty);
        }

        [Test]
        public void Dispose_RejectsBeingCalledFromWithinATick()
        {
            var actor = new TestActor { TickAction = _ => world.Dispose() };
            actor.PrimaryActorTick.CanEverTick = true;
            world.Register(actor);

            // 反復中のリストをClearすることになり、そのフレームの残りが黙って落ちる
            Assert.Throws<InvalidOperationException>(() => world.Tick(0.016f));
            Assert.That(actor.State, Is.EqualTo(ActorState.Playing));
        }

        [Test]
        public void PostTick_FlushesPendingDestroyEvenWhenTickThrows()
        {
            var doomed = world.Register(new TestActor { Name = "doomed" });

            var thrower = new TestActor
            {
                Name = "thrower",
                TickAction = _ => throw new InvalidOperationException("boom")
            };
            thrower.PrimaryActorTick.CanEverTick = true;
            thrower.PrimaryActorTick.Group = TickGroup.PostTick;
            world.Register(thrower);

            world.Destroy(doomed);

            Assert.Throws<InvalidOperationException>(() => world.PostTick(0.016f));

            // 破棄予約を飛ばすと、Destroyされたはずのactorが次フレームまで生き残る
            Assert.That(doomed.State, Is.EqualTo(ActorState.Disposed));
        }


        [Test]
        public void Tick_LogsEveryExceptionAfterTheFirst()
        {
            for (var i = 0; i < 3; i++)
            {
                var thrower = new ThrowingActor { Name = $"thrower{i}" };
                thrower.PrimaryActorTick.CanEverTick = true;
                world.Register(thrower);
            }

            // 最初の1件は呼び出し元へ、残りはその場でログへ。黙って消えるものがないこと
            LogAssert.Expect(LogType.Exception, new Regex("test"));
            LogAssert.Expect(LogType.Exception, new Regex("test"));

            Assert.Throws<InvalidOperationException>(() => world.Tick(0.016f));
        }

        [Test]
        public void Destroy_OfManyActorsKeepsTheOrderOfTheSurvivors()
        {
            var spawned = new List<TestActor>();
            for (var i = 0; i < 10; i++) { spawned.Add(world.Register(Ticking($"a{i}"))); }

            for (var i = 0; i < spawned.Count; i += 2) { world.Destroy(spawned[i]); }
            world.PostTick(0.016f);

            // 破棄はまとめて取り除かれるが、残ったActorの並びは登録順のまま
            var survivors = new List<Actor>();
            for (var i = 1; i < spawned.Count; i += 2) { survivors.Add(spawned[i]); }

            Assert.That(world.Actors, Is.EqualTo(survivors));

            world.Tick(0.016f);
            foreach (var actor in spawned)
            {
                Assert.That(actor.TickCount, Is.EqualTo(actor.IsPendingDestroy ? 0 : 1), actor.Name);
            }
        }

        [Test]
        public void Register_RethrowsTheBeginPlayExceptionEvenWhenTheRollbackThrows()
        {
            // 初期化が途中で止まったActorは、巻き戻しのEndPlayでも例外を投げやすい
            var actor = new TestActor
            {
                BeginPlayAction = _ => throw new InvalidOperationException("begin"),
                EndPlayAction = _ => throw new ArgumentException("rollback"),
            };

            // 本来の原因であるBeginPlayの例外が伝わり、巻き戻しの例外はログへ回る
            LogAssert.Expect(LogType.Exception, new Regex("rollback"));
            var thrown = Assert.Throws<InvalidOperationException>(() => world.Register(actor));

            Assert.That(thrown!.Message, Is.EqualTo("begin"));
            Assert.That(world.Actors, Has.No.Member(actor));
            Assert.That(actor.State, Is.EqualTo(ActorState.Disposed));
        }
    }
}
