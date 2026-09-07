using System;
using System.Collections.Generic;
using UnityEngine;

namespace ActorCoreFramework.Tests
{
    /// <summary>
    /// 呼び出し順を検証するための共有ログ。
    /// 複数のActor / Componentに同じインスタンスを渡して使う。
    /// </summary>
    internal sealed class CallLog
    {
        public readonly List<string> Entries = new();

        public void Add(string entry) => Entries.Add(entry);

        public override string ToString() => string.Join(", ", Entries);
    }


    internal class TestActor : Actor
    {
        public CallLog? Log;

        public int BeginPlayCount;
        public int EndPlayCount;
        public int TickCount;
        public int DisposeCount;

        public EndPlayReason? LastEndPlayReason;
        public float LastDeltaTime;
        public float TotalDeltaTime;

        /// <summary>Dispose時点でWorld参照が生きているかの確認用。</summary>
        public World? WorldAtDispose;

        public Action<TestActor>? BeginPlayAction;
        public Action<TestActor>? TickAction;
        public Action<TestActor>? EndPlayAction;

        /// <summary>protectedなAddComponentをテストから呼ぶための入口。</summary>
        public T Add<T>(T component) where T : ActorComponent => AddComponent(component);

        /// <summary>protectedなRemoveComponentをテストから呼ぶための入口。</summary>
        public bool Remove(ActorComponent component) => RemoveComponent(component);

        protected override void OnBeginPlay()
        {
            BeginPlayCount++;
            Log?.Add($"{Name}.BeginPlay");
            BeginPlayAction?.Invoke(this);
        }

        protected override void OnTick(float deltaTime)
        {
            TickCount++;
            LastDeltaTime = deltaTime;
            TotalDeltaTime += deltaTime;
            Log?.Add($"{Name}.Tick");
            TickAction?.Invoke(this);
        }

        protected override void OnEndPlay(EndPlayReason reason)
        {
            EndPlayCount++;
            LastEndPlayReason = reason;
            Log?.Add($"{Name}.EndPlay");
            EndPlayAction?.Invoke(this);
        }

        protected override void OnDispose()
        {
            DisposeCount++;
            WorldAtDispose = World;
            Log?.Add($"{Name}.Dispose");
        }
    }


    internal sealed class TestComponent : ActorComponent
    {
        public string Name = "component";
        public CallLog? Log;

        public int BeginPlayCount;
        public int EndPlayCount;
        public int TickCount;
        public int DisposeCount;

        /// <summary>Dispose時点で所有者からWorldをたどれるかの確認用。</summary>
        public World? WorldAtDispose;

        public Action<TestComponent>? TickAction;

        protected override void OnBeginPlay()
        {
            BeginPlayCount++;
            Log?.Add($"{Name}.BeginPlay");
        }

        protected override void OnTick(float deltaTime)
        {
            TickCount++;
            Log?.Add($"{Name}.Tick");
            TickAction?.Invoke(this);
        }

        protected override void OnEndPlay(EndPlayReason reason)
        {
            EndPlayCount++;
            Log?.Add($"{Name}.EndPlay");
        }

        protected override void OnDispose()
        {
            DisposeCount++;
            WorldAtDispose = Owner.World;
            Log?.Add($"{Name}.Dispose");
        }
    }


    internal sealed class TestPawn : Pawn
    {
        public int PossessedCount;
        public int UnpossessedCount;

        public Action<TestPawn>? PossessedAction;
        public Action<TestPawn>? UnpossessedAction;

        public TestPawn(Transform transform) : base(transform) { }

        public override void OnPossessed()
        {
            PossessedCount++;
            PossessedAction?.Invoke(this);
        }

        public override void OnUnpossessed()
        {
            UnpossessedCount++;
            UnpossessedAction?.Invoke(this);
        }
    }


    internal sealed class TestController : Controller<TestPawn>
    {
        public TestController(TestPawn? pawn) : base(pawn) { }

        /// <summary>protectedなTryGetControlledPawnをテストから呼ぶための入口。</summary>
        public bool TryGetPawn(out TestPawn? pawn) => TryGetControlledPawn(out pawn);
    }


    /// <summary>Tickでわざと例外を投げるActor。</summary>
    internal sealed class ThrowingActor : TestActor
    {
        /// <summary>falseにすると以降は投げない。例外の後始末を検証するために使う。</summary>
        public bool ThrowOnTick = true;

        protected override void OnTick(float deltaTime)
        {
            base.OnTick(deltaTime);

            if (ThrowOnTick) { throw new InvalidOperationException("test"); }
        }
    }
}
