using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace ActorCoreFramework.Tests
{
    public sealed class ControllerTests
    {
        World world = null!;
        readonly List<GameObject> gameObjects = new();

        [SetUp]
        public void SetUp() => world = new World();

        [TearDown]
        public void TearDown()
        {
            world.Dispose();

            foreach (var gameObject in gameObjects) { UnityEngine.Object.DestroyImmediate(gameObject); }
            gameObjects.Clear();
        }

        TestPawn NewPawn(string name = "pawn")
        {
            var gameObject = new GameObject(name);
            gameObjects.Add(gameObject);

            return new TestPawn(gameObject.transform);
        }


        [Test]
        public void Possess_HappensOnBeginPlayNotInConstructor()
        {
            var pawn = world.Register(NewPawn());
            var controller = new TestController(pawn);

            // コンストラクタ時点では通知しない
            Assert.That(controller.ControlledPawn, Is.Null);
            Assert.That(pawn.PossessedCount, Is.Zero);

            world.Register(controller);

            Assert.That(controller.ControlledPawn, Is.SameAs(pawn));
            Assert.That(pawn.Controller, Is.SameAs(controller));
            Assert.That(pawn.PossessedCount, Is.EqualTo(1));
        }

        [Test]
        public void TryGetControlledPawn_ReturnsTypedPawn()
        {
            var pawn = world.Register(NewPawn());
            var controller = world.Register(new TestController(pawn));

            Assert.That(controller.TryGetPawn(out var controlled), Is.True);
            Assert.That(controlled, Is.SameAs(pawn));

            controller.SwitchControlledPawn(null);

            Assert.That(controller.TryGetPawn(out var none), Is.False);
            Assert.That(none, Is.Null);
        }

        [Test]
        public void SwitchControlledPawn_BeforeBeginPlayOnlyStoresTheTarget()
        {
            var first = world.Register(NewPawn("first"));
            var second = world.Register(NewPawn("second"));

            var controller = new TestController(first);
            controller.SwitchControlledPawn(second);

            Assert.That(first.PossessedCount, Is.Zero);
            Assert.That(second.PossessedCount, Is.Zero);

            world.Register(controller);

            // BeginPlayで最後に指定された対象だけがPossessされる
            Assert.That(controller.ControlledPawn, Is.SameAs(second));
            Assert.That(first.PossessedCount, Is.Zero);
            Assert.That(second.PossessedCount, Is.EqualTo(1));
        }

        [Test]
        public void SwitchControlledPawn_UnpossessesThePreviousPawn()
        {
            var first = world.Register(NewPawn("first"));
            var second = world.Register(NewPawn("second"));
            var controller = world.Register(new TestController(first));

            controller.SwitchControlledPawn(second);

            Assert.That(first.UnpossessedCount, Is.EqualTo(1));
            Assert.That(first.Controller, Is.Null);
            Assert.That(second.Controller, Is.SameAs(controller));
            Assert.That(controller.ControlledPawn, Is.SameAs(second));
        }

        [Test]
        public void SwitchControlledPawn_WithTheSamePawnIsANoOp()
        {
            var pawn = world.Register(NewPawn());
            var controller = world.Register(new TestController(pawn));

            controller.SwitchControlledPawn(pawn);

            Assert.That(pawn.PossessedCount, Is.EqualTo(1));
            Assert.That(pawn.UnpossessedCount, Is.Zero);
        }

        [Test]
        public void SwitchControlledPawn_IsIgnoredAfterEndPlay()
        {
            var first = world.Register(NewPawn("first"));
            var second = world.Register(NewPawn("second"));
            var controller = world.Register(new TestController(first));

            world.Destroy(controller);
            world.PostTick(0.016f);

            controller.SwitchControlledPawn(second);

            Assert.That(second.PossessedCount, Is.Zero);
            Assert.That(controller.ControlledPawn, Is.Null);
        }

        [Test]
        public void Possess_StealsThePawnFromAnotherController()
        {
            var pawn = world.Register(NewPawn());
            var first = world.Register(new TestController(pawn));
            var second = world.Register(new TestController(null));

            second.SwitchControlledPawn(pawn);

            Assert.That(first.ControlledPawn, Is.Null);
            Assert.That(first.HasControlledPawn, Is.False);
            Assert.That(second.ControlledPawn, Is.SameAs(pawn));
            Assert.That(pawn.Controller, Is.SameAs(second));
            Assert.That(pawn.UnpossessedCount, Is.EqualTo(1));
            Assert.That(pawn.PossessedCount, Is.EqualTo(2));
        }

        [Test]
        public void DestroyingThePawn_ClearsTheControllerReference()
        {
            var pawn = world.Register(NewPawn());
            var controller = world.Register(new TestController(pawn));

            world.Destroy(pawn);
            world.PostTick(0.016f);

            Assert.That(controller.ControlledPawn, Is.Null);
            Assert.That(pawn.UnpossessedCount, Is.EqualTo(1));
        }

        [Test]
        public void DestroyingTheController_UnpossessesThePawn()
        {
            var pawn = world.Register(NewPawn());
            var controller = world.Register(new TestController(pawn));

            world.Destroy(controller);
            world.PostTick(0.016f);

            Assert.That(controller.ControlledPawn, Is.Null);
            Assert.That(pawn.Controller, Is.Null);
            Assert.That(pawn.UnpossessedCount, Is.EqualTo(1));
            Assert.That(pawn.State, Is.EqualTo(ActorState.Playing));
        }

        [Test]
        public void WorldDispose_UnpossessesThePawn()
        {
            var pawn = world.Register(NewPawn());
            var controller = world.Register(new TestController(pawn));

            world.Dispose();

            Assert.That(controller.ControlledPawn, Is.Null);
            Assert.That(pawn.Controller, Is.Null);
        }

        [Test]
        public void Possess_RejectsReentrancyFromPossessedCallback()
        {
            var first = world.Register(NewPawn("first"));
            var second = world.Register(NewPawn("second"));
            var controller = world.Register(new TestController(first));

            // 差し替えの途中でさらに差し替えると、中途半端な状態の上に処理が乗る
            second.PossessedAction = _ => controller.SwitchControlledPawn(null);

            Assert.Throws<InvalidOperationException>(() => controller.SwitchControlledPawn(second));
        }

        [Test]
        public void Possess_RejectsReentrancyFromUnpossessedCallback()
        {
            var first = world.Register(NewPawn("first"));
            var second = world.Register(NewPawn("second"));
            var third = world.Register(NewPawn("third"));
            var controller = world.Register(new TestController(first));

            first.UnpossessedAction = _ => controller.SwitchControlledPawn(third);

            Assert.Throws<InvalidOperationException>(() => controller.SwitchControlledPawn(second));
        }

        [Test]
        public void Possess_IgnoresADestroyedPawn()
        {
            var pawn = world.Register(NewPawn());
            world.Destroy(pawn);
            world.PostTick(0.016f);
            Assert.That(pawn.State, Is.EqualTo(ActorState.Disposed));

            // 破棄済みPawnを掴むと、参照を切る機会(PawnのEndPlay)がもう来ない
            var controller = world.Register(new TestController(pawn));

            Assert.That(controller.ControlledPawn, Is.Null);
            Assert.That(pawn.Controller, Is.Null);
            Assert.That(pawn.PossessedCount, Is.Zero);
        }

        [Test]
        public void SwitchControlledPawn_IgnoresAPawnPendingDestroy()
        {
            var alive = world.Register(NewPawn("alive"));
            var doomed = world.Register(NewPawn("doomed"));
            var controller = world.Register(new TestController(alive));

            world.Destroy(doomed); // 実際の破棄はPostTickまで遅延する

            controller.SwitchControlledPawn(doomed);

            // 破棄予約済みは検索系と同様に見つからないものとして扱い、現在のPawnだけ解除する
            Assert.That(controller.ControlledPawn, Is.Null);
            Assert.That(doomed.Controller, Is.Null);
            Assert.That(doomed.PossessedCount, Is.Zero);
            Assert.That(alive.UnpossessedCount, Is.EqualTo(1));
        }

        [Test]
        public void Possess_AcceptsAPawnThatIsNotRegisteredYet()
        {
            // Controllerを先に登録する順序でも通る。まだ死んでいないだけで、
            // 後から登録されればBeginPlayが届く。
            var pawn = NewPawn();
            var controller = world.Register(new TestController(pawn));

            Assert.That(controller.ControlledPawn, Is.SameAs(pawn));
            Assert.That(pawn.PossessedCount, Is.EqualTo(1));

            world.Register(pawn);

            Assert.That(pawn.State, Is.EqualTo(ActorState.Playing));
            Assert.That(controller.ControlledPawn, Is.SameAs(pawn));
        }
    }
}
