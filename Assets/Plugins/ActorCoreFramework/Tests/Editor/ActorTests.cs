using System;
using System.Collections.Generic;
using System.Threading;
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

        [Test]
        public void AddComponent_RejectsAComponentThatIsAlreadyAttached()
        {
            var actor = new TestActor();
            var component = actor.Add(new TestComponent());
            var other = new TestActor();

            // 同じActorへの二重合成も、他Actorとの共有も許さない
            Assert.Throws<InvalidOperationException>(() => actor.Add(component));
            Assert.Throws<InvalidOperationException>(() => other.Add(component));

            Assert.That(actor.Components, Has.Count.EqualTo(1));
            Assert.That(other.Components, Is.Empty);
            Assert.That(component.Owner, Is.SameAs(actor));
        }

        [Test]
        public void AddComponent_RejectsADisposedComponent()
        {
            var actor = world.Register(new TestActor());
            var component = actor.Add(new TestComponent());
            actor.Remove(component);

            var other = world.Register(new TestActor());

            Assert.Throws<InvalidOperationException>(() => other.Add(component));
        }

        [Test]
        public void Component_HasBegunPlayMirrorsTheOwnerLifecycle()
        {
            var actor = new TestActor();
            var component = actor.Add(new TestComponent());
            Assert.That(component.HasBegunPlay, Is.False);

            world.Register(actor);
            Assert.That(component.HasBegunPlay, Is.True);

            world.Destroy(actor);
            world.PostTick(0.016f);
            Assert.That(component.HasBegunPlay, Is.False);
        }

        [Test]
        public void RemoveComponent_DuringOwnerEndPlayStillDispatchesComponentEndPlay()
        {
            var actor = new TestActor { Name = "a", Log = log };
            var component = actor.Add(new TestComponent { Name = "c", Log = log });
            world.Register(actor);
            log.Entries.Clear();

            // 所有者のEndPlay中はStateが既にEndedなので、
            // それを配送条件にしているとComponentのEndPlayが飛ぶ
            actor.EndPlayAction = a => a.Remove(component);

            world.Destroy(actor);
            world.PostTick(0.016f);

            Assert.That(component.EndPlayCount, Is.EqualTo(1));
            Assert.That(component.IsDisposed, Is.True);
            Assert.That(log.Entries, Is.EqualTo(new[] { "a.EndPlay", "c.EndPlay", "c.Dispose", "a.Dispose" }));
        }

        [Test]
        public void GetComponents_CollectsEveryMatchingComponent()
        {
            var actor = new TestActor();
            var first = actor.Add(new TestComponent { Name = "c1" });
            var second = actor.Add(new TestComponent { Name = "c2" });
            actor.Add(new OtherTestComponent());
            world.Register(actor);

            var results = new List<TestComponent>();

            // TryGetComponentと違い、同じ型を複数持っていてもすべて取れる
            Assert.That(actor.GetComponents(results), Is.EqualTo(2));
            Assert.That(results, Is.EqualTo(new[] { first, second }));
        }

        [Test]
        public void GetComponents_ClearsTheListAndSkipsPendingRemoval()
        {
            var actor = new TestActor();
            var first = actor.Add(new TestComponent());
            var second = actor.Add(new TestComponent());
            world.Register(actor);

            // リストを使い回す前提なので、前回の内容は捨てられる
            var results = new List<TestComponent> { second };
            actor.Remove(second);

            Assert.That(actor.GetComponents(results), Is.EqualTo(1));
            Assert.That(results, Is.EqualTo(new[] { first }));
        }

        // --- DestroyToken ---

        [Test]
        public void DestroyToken_IsNotCanceledWhilePlaying()
        {
            var actor = world.Register(new TestActor());

            Assert.That(actor.DestroyToken.IsCancellationRequested, Is.False);
            Assert.That(actor.DestroyToken.CanBeCanceled, Is.True);
        }

        [Test]
        public void DestroyToken_IsCanceledWhenTheActorIsDestroyed()
        {
            var actor = world.Register(new TestActor());
            var token = actor.DestroyToken;

            world.Destroy(actor);
            Assert.That(token.IsCancellationRequested, Is.False, "破棄予約だけでは発火しない");

            world.PostTick(0.016f);

            Assert.That(token.IsCancellationRequested, Is.True);
        }

        [Test]
        public void DestroyToken_IsCanceledOnWorldShutdown()
        {
            var actor = world.Register(new TestActor());
            var token = actor.DestroyToken;

            world.Dispose();

            Assert.That(token.IsCancellationRequested, Is.True);
        }

        [Test]
        public void DestroyToken_IsAlreadyCanceledInsideOnEndPlay()
        {
            var canceledInEndPlay = false;
            var actor = world.Register(new TestActor
            {
                EndPlayAction = self => canceledInEndPlay = self.DestroyToken.IsCancellationRequested
            });

            // OnEndPlayが資源を解放する時点で、非同期側は既に停止を通知されている
            _ = actor.DestroyToken;
            world.Destroy(actor);
            world.PostTick(0.016f);

            Assert.That(canceledInEndPlay, Is.True);
        }

        [Test]
        public void DestroyToken_IsAlreadyCanceledWhenTakenAfterEndPlay()
        {
            var actor = world.Register(new TestActor());
            world.Destroy(actor);
            world.PostTick(0.016f);

            // 破棄済みActorへ非同期処理を積もうとしても、即座に終わるようにする
            Assert.That(actor.DestroyToken.IsCancellationRequested, Is.True);
        }

        [Test]
        public void DestroyToken_FiresRegisteredCallbacks()
        {
            var actor = world.Register(new TestActor());
            var fired = 0;
            using var registration = actor.DestroyToken.Register(() => fired++);

            world.Destroy(actor);
            world.PostTick(0.016f);

            Assert.That(fired, Is.EqualTo(1));
        }

        [Test]
        public void DestroyToken_IsCanceledWhenRegistrationRollsBack()
        {
            // BeginPlayが失敗したActorはEndPlayを経ずにDisposeされる。
            // その経路でもトークンは発火させる。
            CancellationToken token = default;
            var actor = new TestActor
            {
                BeginPlayAction = self =>
                {
                    token = self.DestroyToken;
                    throw new InvalidOperationException("boom");
                }
            };

            Assert.Throws<InvalidOperationException>(() => world.Register(actor));

            Assert.That(token.IsCancellationRequested, Is.True);
        }

        [Test]
        public void DestroyToken_IsNotAllocatedUntilItIsUsed()
        {
            // 使わないActorに確保と破棄の負担をかけないための遅延生成。
            // 一度も触らずに破棄しても壊れないことを確認する。
            var actor = world.Register(new TestActor());

            world.Destroy(actor);
            Assert.DoesNotThrow(() => world.PostTick(0.016f));

            Assert.That(actor.State, Is.EqualTo(ActorState.Disposed));
        }

        // --- BindTo ---

        [Test]
        public void BindTo_DestroysTheActorWhenTheLifetimeEnds()
        {
            using var lifetime = new CancellationTokenSource();
            var actor = world.Register(new TestActor()).BindTo(lifetime.Token);

            lifetime.Cancel();

            // 破棄は予約されるだけ。実際の破棄はPostTickの末尾
            Assert.That(actor.IsPendingDestroy, Is.True);
            Assert.That(actor.State, Is.EqualTo(ActorState.Playing));

            world.PostTick(0.016f);

            Assert.That(actor.State, Is.EqualTo(ActorState.Disposed));
        }

        [Test]
        public void BindTo_StopsTickImmediatelyWithoutWaitingForTheFlush()
        {
            using var lifetime = new CancellationTokenSource();
            var actor = new TestActor();
            actor.PrimaryActorTick.CanEverTick = true;
            world.Register(actor).BindTo(lifetime.Token);

            world.Tick(0.016f);
            Assert.That(actor.TickCount, Is.EqualTo(1));

            // 紐づけ先が壊れた時点でTickが止まらないと、
            // 破棄済みのGameObjectを掴んだままTickされる
            lifetime.Cancel();

            world.Tick(0.016f);
            Assert.That(actor.TickCount, Is.EqualTo(1));
        }

        [Test]
        public void BindTo_ReturnsTheActorWithItsConcreteType()
        {
            using var lifetime = new CancellationTokenSource();

            // 生成にそのまま繋げられること(具象型が保たれること)
            TestActor actor = world.Register(new TestActor()).BindTo(lifetime.Token);

            Assert.That(actor.State, Is.EqualTo(ActorState.Playing));
        }

        [Test]
        public void BindTo_BeforeRegistrationSubscribesAtBeginPlay()
        {
            using var lifetime = new CancellationTokenSource();
            var actor = new TestActor().BindTo(lifetime.Token);

            // World未登録なら破棄を予約する先がない。購読はBeginPlayまで待つ
            lifetime.Cancel();
            Assert.That(actor.IsPendingDestroy, Is.False);
            Assert.That(actor.State, Is.EqualTo(ActorState.Created));
        }

        [Test]
        public void BindTo_WithAnAlreadyCanceledLifetimeDestroysOnRegistration()
        {
            using var lifetime = new CancellationTokenSource();
            lifetime.Cancel();

            // 既に壊れたGameObjectに対してActorを生成した場合。
            // Registerがその場でコールバックを走らせる
            var actor = world.Register(new TestActor()).BindTo(lifetime.Token);

            Assert.That(actor.BeginPlayCount, Is.EqualTo(1), "BeginPlayは配送される");
            Assert.That(actor.IsPendingDestroy, Is.True);

            world.PostTick(0.016f);
            Assert.That(actor.State, Is.EqualTo(ActorState.Disposed));
        }

        [Test]
        public void BindTo_UnsubscribesWhenTheActorDiesFirst()
        {
            using var lifetime = new CancellationTokenSource();
            var actor = world.Register(new TestActor()).BindTo(lifetime.Token);

            world.Destroy(actor);
            world.PostTick(0.016f);
            Assert.That(actor.State, Is.EqualTo(ActorState.Disposed));

            // 購読が残っているとトークン側がActorを掴み続ける。
            // 解除されていれば、後からキャンセルされても何も起きない
            Assert.DoesNotThrow(() => lifetime.Cancel());
            Assert.That(actor.State, Is.EqualTo(ActorState.Disposed));
        }

        [Test]
        public void BindTo_IsSafeWhenTheWorldIsDisposedFirst()
        {
            using var lifetime = new CancellationTokenSource();
            world.Register(new TestActor()).BindTo(lifetime.Token);

            // シーンのアンロードでは破棄順が保証されない。
            // Worldが先に畳まれた後にGameObjectが壊れても壊れないこと
            world.Dispose();

            Assert.DoesNotThrow(() => lifetime.Cancel());
        }

        [Test]
        public void BindTo_RejectsASecondBinding()
        {
            using var first = new CancellationTokenSource();
            using var second = new CancellationTokenSource();
            var actor = world.Register(new TestActor()).BindTo(first.Token);

            Assert.Throws<InvalidOperationException>(() => actor.BindTo(second.Token));
        }

        [Test]
        public void BindTo_RejectsAnEndedActor()
        {
            using var lifetime = new CancellationTokenSource();
            var actor = world.Register(new TestActor());

            world.Destroy(actor);
            world.PostTick(0.016f);

            Assert.Throws<InvalidOperationException>(() => actor.BindTo(lifetime.Token));
        }

        [Test]
        public void BindTo_ThrowsOnNullActor()
        {
            TestActor? actor = null;

            Assert.Throws<ArgumentNullException>(() => actor!.BindTo(CancellationToken.None));
        }
    }
}
