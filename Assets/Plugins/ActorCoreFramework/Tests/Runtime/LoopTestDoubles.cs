using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.LowLevel;

namespace ActorCoreFramework.PlayModeTests
{
    /// <summary>
    /// PlayerLoopのどのフェーズで何が走ったかを記録する共有ログ。
    /// EditModeテスト側にも同名の型があるが、アセンブリが分かれているため独立している。
    /// </summary>
    internal sealed class CallLog
    {
        public readonly List<string> Entries = new();

        public void Add(string entry) => Entries.Add(entry);

        public override string ToString() => string.Join(", ", Entries);
    }


    /// <summary>Tickされた回数とdeltaTimeを数えるだけのActor。</summary>
    internal class CountingActor : Actor
    {
        public int TickCount;
        public float TotalDeltaTime;
        public CallLog? Log;

        public CountingActor(TickGroup group = TickGroup.Tick)
        {
            Name = group.ToString();
            PrimaryActorTick.CanEverTick = true;
            PrimaryActorTick.Group = group;
        }

        protected override void OnTick(float deltaTime)
        {
            TickCount++;
            TotalDeltaTime += deltaTime;
            Log?.Add($"actor.{Name}");
        }
    }


    /// <summary>一度だけTickで例外を投げるActor。</summary>
    internal sealed class ThrowOnceActor : CountingActor
    {
        public bool ThrowOnTick = true;

        protected override void OnTick(float deltaTime)
        {
            base.OnTick(deltaTime);

            if (!ThrowOnTick) { return; }

            ThrowOnTick = false;
            throw new InvalidOperationException("play mode test");
        }
    }


    /// <summary>MonoBehaviourの各フェーズを同じログへ記録し、Actorとの前後関係を見る。</summary>
    internal sealed class PhaseProbeBehaviour : MonoBehaviour
    {
        // テストから実行時に差し込むだけで、Inspectorからは設定しない。
        // publicフィールドはUnityのシリアライズ対象になるため、
        // 対象外であることを明示しないとUAC1001の警告になる。
        [NonSerialized] public CallLog? Log;

        void FixedUpdate() => Log?.Add("mb.FixedUpdate");
        void Update() => Log?.Add("mb.Update");
        void LateUpdate() => Log?.Add("mb.LateUpdate");
    }


    internal static class PlayerLoopAssert
    {
        /// <summary>PlayerLoopの木を辿って、指定した型のシステムが差し込まれているか調べる。</summary>
        public static bool Contains(Type systemType)
        {
            return Contains(PlayerLoop.GetCurrentPlayerLoop(), systemType);
        }

        static bool Contains(in PlayerLoopSystem parent, Type systemType)
        {
            var children = parent.subSystemList;
            if (children == null) { return false; }

            for (var i = 0; i < children.Length; i++)
            {
                if (children[i].type == systemType) { return true; }
                if (Contains(children[i], systemType)) { return true; }
            }

            return false;
        }
    }
}
