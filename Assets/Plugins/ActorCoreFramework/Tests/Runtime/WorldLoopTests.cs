using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace ActorCoreFramework.PlayModeTests
{
    public sealed class WorldLoopTests
    {
        readonly List<World> worlds = new();
        readonly List<IDisposable> subscriptions = new();
        readonly List<GameObject> gameObjects = new();

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = 1.0f;

            // Subscription.Disposeは冪等なので、テスト内で解除済みでも安全
            foreach (var subscription in subscriptions) { subscription.Dispose(); }
            subscriptions.Clear();

            foreach (var world in worlds) { world.Dispose(); }
            worlds.Clear();

            foreach (var gameObject in gameObjects) { UnityEngine.Object.Destroy(gameObject); }
            gameObjects.Clear();
        }

        World NewWorld()
        {
            var world = new World();
            worlds.Add(world);

            return world;
        }

        IDisposable RegisterToLoop(World world)
        {
            var subscription = WorldLoop.Register(world);
            subscriptions.Add(subscription);

            return subscription;
        }


        [UnityTest]
        public IEnumerator Register_TicksTheWorldOncePerFrame()
        {
            var world = NewWorld();
            var actor = world.Register(new CountingActor());
            RegisterToLoop(world);

            yield return null;
            var afterFirstFrame = actor.TickCount;

            yield return null;
            yield return null;

            Assert.That(afterFirstFrame, Is.GreaterThan(0));
            Assert.That(actor.TickCount - afterFirstFrame, Is.EqualTo(2));
        }

        [Test]
        public void Register_ThrowsWhenTheSameWorldIsRegisteredTwice()
        {
            var world = NewWorld();
            RegisterToLoop(world);

            Assert.Throws<InvalidOperationException>(() => WorldLoop.Register(world));
        }

        [Test]
        public void Register_ThrowsOnNull()
        {
            Assert.Throws<ArgumentNullException>(() => WorldLoop.Register(null!));
        }

        [UnityTest]
        public IEnumerator Subscription_StopsTickingWhenDisposed()
        {
            var world = NewWorld();
            var actor = world.Register(new CountingActor());
            var subscription = RegisterToLoop(world);

            yield return null;
            var before = actor.TickCount;
            Assert.That(before, Is.GreaterThan(0));

            subscription.Dispose();

            yield return null;
            yield return null;

            Assert.That(actor.TickCount, Is.EqualTo(before));
        }

        [UnityTest]
        public IEnumerator Subscription_DisposeIsIdempotentAndDoesNotCancelALaterRegistration()
        {
            var world = NewWorld();
            var actor = world.Register(new CountingActor());
            var first = RegisterToLoop(world);

            yield return null;

            first.Dispose();
            first.Dispose(); // 二重解除しても落ちない

            RegisterToLoop(world);

            var beforeReregistration = actor.TickCount;
            yield return null;
            Assert.That(actor.TickCount, Is.GreaterThan(beforeReregistration));

            // 古いSubscriptionを後から捨てても、新しい登録を巻き添えにしない
            first.Dispose();

            var beforeStaleDispose = actor.TickCount;
            yield return null;
            Assert.That(actor.TickCount, Is.GreaterThan(beforeStaleDispose));
        }

        [UnityTest]
        public IEnumerator MultipleWorlds_AreAllTicked()
        {
            var first = NewWorld();
            var firstActor = first.Register(new CountingActor());
            RegisterToLoop(first);

            var second = NewWorld();
            var secondActor = second.Register(new CountingActor());
            RegisterToLoop(second);

            yield return null;
            yield return null;

            Assert.That(firstActor.TickCount, Is.GreaterThan(0));
            Assert.That(secondActor.TickCount, Is.EqualTo(firstActor.TickCount));
        }

        [UnityTest]
        public IEnumerator TickGroups_RunInTheExpectedPlayerLoopPhases()
        {
            var log = new CallLog();

            var gameObject = new GameObject("phase probe");
            gameObjects.Add(gameObject);
            gameObject.AddComponent<PhaseProbeBehaviour>().Log = log;

            var world = NewWorld();
            foreach (TickGroup group in Enum.GetValues(typeof(TickGroup)))
            {
                world.Register(new CountingActor(group) { Log = log });
            }
            RegisterToLoop(world);

            yield return null; // 差し込み直後のフレームは捨てる
            log.Entries.Clear();

            for (var i = 0; i < 4; i++) { yield return null; }

            // FixedTickは1フレームに0回以上なので、まずスケール依存のフェーズだけを見る
            var phases = log.Entries
                .Where(entry => entry != "mb.FixedUpdate" && entry != "actor.FixedTick")
                .ToList();

            // 記録開始はフレームの途中なので、最初のmb.Updateまで読み飛ばす
            var start = phases.IndexOf("mb.Update");
            Assert.That(start, Is.GreaterThanOrEqualTo(0), $"mb.Updateが記録されていない: {log}");

            var cycle = new[] { "mb.Update", "actor.Tick", "actor.UnscaledTick", "mb.LateUpdate", "actor.PostTick" };
            Assert.That(phases.Count - start, Is.GreaterThanOrEqualTo(cycle.Length * 2), $"フレーム数が足りない: {log}");

            // Update -> Tick -> UnscaledTick -> LateUpdate -> PostTick が毎フレーム繰り返される
            Assert.That(phases.Skip(start).Take(cycle.Length * 2), Is.EqualTo(cycle.Concat(cycle)));

            // FixedTickは必ずMonoBehaviourのFixedUpdateの直後
            for (var i = 0; i < log.Entries.Count; i++)
            {
                if (log.Entries[i] != "actor.FixedTick") { continue; }

                Assert.That(i, Is.GreaterThan(0));
                Assert.That(log.Entries[i - 1], Is.EqualTo("mb.FixedUpdate"), $"順序が崩れている: {log}");
            }
        }

        [UnityTest]
        public IEnumerator UnscaledTick_KeepsAdvancingWhileTimeScaleIsZero()
        {
            var world = NewWorld();
            var scaled = world.Register(new CountingActor(TickGroup.Tick));
            var unscaled = world.Register(new CountingActor(TickGroup.UnscaledTick));
            RegisterToLoop(world);

            Time.timeScale = 0.0f;
            yield return null;

            var scaledCount = scaled.TickCount;
            var scaledElapsed = scaled.TotalDeltaTime;
            var unscaledElapsed = unscaled.TotalDeltaTime;

            for (var i = 0; i < 3; i++) { yield return null; }

            // 回数はどちらも増えるが、進むのはUnscaledTickの時間だけ
            Assert.That(scaled.TickCount, Is.GreaterThan(scaledCount));
            Assert.That(scaled.TotalDeltaTime - scaledElapsed, Is.EqualTo(0.0f).Within(0.0001f));
            Assert.That(unscaled.TotalDeltaTime - unscaledElapsed, Is.GreaterThan(0.0f));
        }

        [UnityTest]
        public IEnumerator PlayerLoop_IsCleanedUpWhenTheLastWorldUnregisters()
        {
            Assert.That(PlayerLoopAssert.Contains(typeof(WorldLoop.ActorCoreTick)), Is.False,
                "テスト開始時点でPlayerLoopに差し込みが残っている");

            var world = NewWorld();
            var subscription = RegisterToLoop(world);

            Assert.That(PlayerLoopAssert.Contains(typeof(WorldLoop.ActorCoreFixedTick)), Is.True);
            Assert.That(PlayerLoopAssert.Contains(typeof(WorldLoop.ActorCoreTick)), Is.True);
            Assert.That(PlayerLoopAssert.Contains(typeof(WorldLoop.ActorCoreUnscaledTick)), Is.True);
            Assert.That(PlayerLoopAssert.Contains(typeof(WorldLoop.ActorCorePostTick)), Is.True);

            yield return null;

            subscription.Dispose();

            Assert.That(PlayerLoopAssert.Contains(typeof(WorldLoop.ActorCoreFixedTick)), Is.False);
            Assert.That(PlayerLoopAssert.Contains(typeof(WorldLoop.ActorCoreTick)), Is.False);
            Assert.That(PlayerLoopAssert.Contains(typeof(WorldLoop.ActorCoreUnscaledTick)), Is.False);
            Assert.That(PlayerLoopAssert.Contains(typeof(WorldLoop.ActorCorePostTick)), Is.False);
        }

        [UnityTest]
        public IEnumerator ExceptionInOneWorld_DoesNotStopTheOthers()
        {
            var faulty = NewWorld();
            var thrower = faulty.Register(new ThrowOnceActor());
            RegisterToLoop(faulty);

            var healthy = NewWorld();
            var actor = healthy.Register(new CountingActor());
            RegisterToLoop(healthy);

            LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException"));

            yield return null;
            yield return null;

            Assert.That(thrower.ThrowOnTick, Is.False, "例外が一度も発生していない");

            // 先に登録されたWorldが落ちても、後続のWorldは回る
            Assert.That(actor.TickCount, Is.GreaterThan(0));

            // 例外を出したWorld自身も次のフレームからは通常どおり回る
            var before = thrower.TickCount;
            yield return null;
            Assert.That(thrower.TickCount, Is.GreaterThan(before));
        }

        [UnityTest]
        public IEnumerator DisposedWorld_IsSafeToLeaveRegistered()
        {
            var world = NewWorld();
            var actor = world.Register(new CountingActor());
            RegisterToLoop(world);

            yield return null;
            var before = actor.TickCount;

            world.Dispose();

            yield return null;
            yield return null;

            Assert.That(actor.TickCount, Is.EqualTo(before));
        }
    }
}
