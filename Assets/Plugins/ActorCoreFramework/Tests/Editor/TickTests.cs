using System;
using NUnit.Framework;

namespace ActorCoreFramework.Tests
{
    public sealed class TickTests
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


        TestActor Ticking(string name, TickGroup group = TickGroup.Tick, int priority = 0, float interval = 0.0f)
        {
            var actor = new TestActor { Name = name, Log = log };
            actor.PrimaryActorTick.CanEverTick = true;
            actor.PrimaryActorTick.Group = group;
            actor.PrimaryActorTick.Priority = priority;
            actor.PrimaryActorTick.Interval = interval;

            return actor;
        }


        [Test]
        public void Tick_IsNotDeliveredWhenCanEverTickIsFalse()
        {
            var actor = world.Register(new TestActor());

            world.Tick(0.016f);

            Assert.That(actor.TickCount, Is.Zero);
        }

        [Test]
        public void Tick_IsSkippedWhileDisabled()
        {
            var actor = world.Register(Ticking("a"));
            actor.PrimaryActorTick.Enabled = false;

            world.Tick(0.016f);
            Assert.That(actor.TickCount, Is.Zero);

            actor.PrimaryActorTick.Enabled = true;
            world.Tick(0.016f);
            Assert.That(actor.TickCount, Is.EqualTo(1));
        }

        [Test]
        public void Tick_RunsInAscendingPriorityOrder()
        {
            world.Register(Ticking("late", priority: 100));
            world.Register(Ticking("early", priority: -100));
            world.Register(Ticking("middle"));
            log.Entries.Clear();

            world.Tick(0.016f);

            Assert.That(log.Entries, Is.EqualTo(new[] { "early.Tick", "middle.Tick", "late.Tick" }));
        }

        [Test]
        public void Tick_KeepsRegistrationOrderForEqualPriority()
        {
            world.Register(Ticking("first"));
            world.Register(Ticking("second"));
            world.Register(Ticking("third"));
            log.Entries.Clear();

            world.Tick(0.016f);

            Assert.That(log.Entries, Is.EqualTo(new[] { "first.Tick", "second.Tick", "third.Tick" }));
        }

        [Test]
        public void TickGroups_AreIndependent()
        {
            var update = world.Register(Ticking("update"));
            var fixedUpdate = world.Register(Ticking("fixed", TickGroup.FixedTick));
            var unscaled = world.Register(Ticking("unscaled", TickGroup.UnscaledTick));
            var post = world.Register(Ticking("post", TickGroup.PostTick));

            world.Tick(0.016f);

            Assert.That(update.TickCount, Is.EqualTo(1));
            Assert.That(fixedUpdate.TickCount, Is.Zero);
            Assert.That(unscaled.TickCount, Is.Zero);
            Assert.That(post.TickCount, Is.Zero);

            world.FixedTick(0.02f);
            world.UnscaledTick(0.016f);
            world.PostTick(0.016f);

            Assert.That(fixedUpdate.TickCount, Is.EqualTo(1));
            Assert.That(unscaled.TickCount, Is.EqualTo(1));
            Assert.That(post.TickCount, Is.EqualTo(1));
            Assert.That(update.TickCount, Is.EqualTo(1));
        }

        [Test]
        public void UnscaledTick_UsesTheDeltaTimeItIsGiven()
        {
            var unscaled = world.Register(Ticking("unscaled", TickGroup.UnscaledTick));

            // timeScale=0を想定して、Tick側は0秒、Unscaled側は実時間で回す
            world.Tick(0.0f);
            world.UnscaledTick(0.016f);

            Assert.That(unscaled.TickCount, Is.EqualTo(1));
            Assert.That(unscaled.LastDeltaTime, Is.EqualTo(0.016f).Within(0.0001f));
        }


        [Test]
        public void Interval_DoesNotDriftWithTheFrameRate()
        {
            // Interval 0.5秒に対してdeltaTimeが0.375秒。
            // 超過分を持ち越さないと2フレームに1回(=0.75秒間隔)へ間延びし、
            // 12フレーム(4.5秒)で9回のはずが6回しか回らない。
            // 境界比較を浮動小数点誤差に左右されないよう、2の冪で表せる値を使う。
            var actor = world.Register(Ticking("a", interval: 0.5f));

            for (var i = 0; i < 12; i++) { world.Tick(0.375f); }

            Assert.That(actor.TickCount, Is.EqualTo(9));
        }

        [Test]
        public void Interval_PassesTheAccumulatedDeltaTime()
        {
            var actor = world.Register(Ticking("a", interval: 0.5f));

            world.Tick(0.375f);
            Assert.That(actor.TickCount, Is.Zero);

            world.Tick(0.375f);

            Assert.That(actor.TickCount, Is.EqualTo(1));
            // 間引いた分をまとめて渡す。持ち越し分(0.25)は次回に回すので含めない
            Assert.That(actor.LastDeltaTime, Is.EqualTo(0.5f).Within(0.0001f));
        }

        [Test]
        public void Interval_FiresAtMostOncePerFrame()
        {
            var actor = world.Register(Ticking("a", interval: 0.5f));

            world.Tick(4.0f); // Interval 8回分のヒッチ

            // 溜まった分を同一フレームで一気に消化しない
            Assert.That(actor.TickCount, Is.EqualTo(1));
            Assert.That(actor.LastDeltaTime, Is.EqualTo(4.0f).Within(0.0001f));
        }


        [Test]
        public void Register_DuringTickIsDeferredToTheNextFrame()
        {
            TestActor? spawned = null;
            var actor = Ticking("a");
            actor.TickAction = _ =>
            {
                if (spawned != null) { return; }
                spawned = world.Register(Ticking("spawned"));
            };
            world.Register(actor);

            world.Tick(0.016f);

            // BeginPlayは即時、Tickは次フレームから
            Assert.That(spawned!.BeginPlayCount, Is.EqualTo(1));
            Assert.That(spawned.TickCount, Is.Zero);

            world.Tick(0.016f);
            Assert.That(spawned.TickCount, Is.EqualTo(1));
        }

        [Test]
        public void PendingChanges_AreAppliedEvenWhenTickThrows()
        {
            TestActor? spawned = null;
            var thrower = new ThrowingActor { Name = "thrower", Log = log };
            thrower.PrimaryActorTick.CanEverTick = true;
            thrower.TickAction = _ => spawned ??= world.Register(Ticking("spawned"));

            world.Register(thrower);

            // 例外はWorldの外へ抜ける(実行時はWorldLoopが捕捉してログに出す)
            Assert.Throws<InvalidOperationException>(() => world.Tick(0.016f));

            thrower.ThrowOnTick = false;

            // 例外で保留分が取り残されると、以降このActorは永久にTickされない
            world.Tick(0.016f);
            Assert.That(spawned!.TickCount, Is.EqualTo(1));
        }

        [Test]
        public void Register_ThenDestroyDuringTickRemovesTheActor()
        {
            TestActor? spawned = null;
            var actor = Ticking("a", TickGroup.PostTick);
            actor.TickAction = _ =>
            {
                if (spawned != null) { return; }

                spawned = world.Register(Ticking("spawned", TickGroup.PostTick));
                world.Destroy(spawned);
            };
            world.Register(actor);

            world.PostTick(0.016f);

            Assert.That(spawned!.State, Is.EqualTo(ActorState.Disposed));

            // 追加と削除の順序が入れ替わると、破棄済みActorがTick対象に残る
            world.PostTick(0.016f);
            Assert.That(spawned.TickCount, Is.Zero);
        }

        [Test]
        public void TickSettings_CannotBeChangedAfterRegistration()
        {
            var actor = world.Register(Ticking("a"));

            Assert.Throws<InvalidOperationException>(() => actor.PrimaryActorTick.CanEverTick = false);
            Assert.Throws<InvalidOperationException>(() => actor.PrimaryActorTick.Group = TickGroup.PostTick);
            Assert.Throws<InvalidOperationException>(() => actor.PrimaryActorTick.Priority = 10);
        }

        [Test]
        public void TickSettings_AreLockedEvenWhenTheActorDoesNotTick()
        {
            // CanEverTickがfalseだとTickグループへ登録されないが、
            // その後の有効化も黙って無視されるだけなので同じく弾く
            var actor = world.Register(new TestActor());

            Assert.Throws<InvalidOperationException>(() => actor.PrimaryActorTick.CanEverTick = true);
        }

        [Test]
        public void RuntimeTickSettings_RemainWritableAfterRegistration()
        {
            var actor = world.Register(Ticking("a"));

            // 実行時に変えてよいものは従来どおり
            Assert.DoesNotThrow(() => actor.PrimaryActorTick.Enabled = false);
            Assert.DoesNotThrow(() => actor.PrimaryActorTick.Interval = 0.5f);

            Assert.That(actor.PrimaryActorTick.Enabled, Is.False);
            Assert.That(actor.PrimaryActorTick.Interval, Is.EqualTo(0.5f));
        }

        [Test]
        public void Tick_RejectsReentrantCalls()
        {
            var actor = Ticking("a");
            actor.TickAction = _ => world.Tick(0.016f);
            world.Register(actor);

            Assert.Throws<InvalidOperationException>(() => world.Tick(0.016f));
        }

        [Test]
        public void Tick_RejectsDrivingAnotherGroupFromWithinATick()
        {
            var actor = Ticking("a");
            actor.TickAction = _ => world.PostTick(0.016f);
            world.Register(actor);

            Assert.Throws<InvalidOperationException>(() => world.Tick(0.016f));
        }

        [Test]
        public void Tick_IsAcceptedAgainAfterAReentrantCallFailed()
        {
            var actor = Ticking("a");
            actor.TickAction = _ => world.Tick(0.016f);
            world.Register(actor);

            Assert.Throws<InvalidOperationException>(() => world.Tick(0.016f));

            // 例外で抜けてもフラグは戻る
            actor.TickAction = null;
            Assert.DoesNotThrow(() => world.Tick(0.016f));
        }
    }
}
