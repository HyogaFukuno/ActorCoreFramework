using System;
using System.Collections.Generic;
using Unity.Scripting.LifecycleManagement;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.PlayerLoop;

namespace ActorCoreFramework
{
    /// <summary>
    /// WorldをUnityのPlayerLoopに接続する。
    /// 静的状態はAutoStaticsCleanupによってPlay mode遷移時に自動リセットされるため、
    /// Domain Reload無効時も前回の再生の状態は持ち越されない。
    /// </summary>
    [AutoStaticsCleanup]
    public static partial class WorldLoop
    {
        public struct ActorCoreFixedTick { }
        public struct ActorCoreTick { }
        public struct ActorCoreUnscaledTick { }
        public struct ActorCorePostTick { }

        static readonly List<World> s_worlds = new();
        static readonly List<World> s_snapshot = new(); // 反復中の登録変更に耐えるため
        static bool installed;

        /// <summary>
        /// 引数のWorldをループ可能なWorldとして登録する。
        /// 戻り値のIDisposableが登録解除の唯一の手段。破棄すると解除される。
        /// </summary>
        /// <exception cref="ArgumentNullException">worldがnull。</exception>
        /// <exception cref="InvalidOperationException">
        /// 既に登録済みか、PlayerLoopへの差し込みに失敗した。
        /// </exception>
        public static IDisposable Register(World world)
        {
            if (world == null) { throw new ArgumentNullException(nameof(world)); }

            if (s_worlds.Contains(world))
            {
                throw new InvalidOperationException("World is already registered.");
            }

            if (!installed && !Install())
            {
                // 差し込めていないWorldは一度もTickされない。
                // 登録が成功したように見せないため、ここで失敗させる。
                throw new InvalidOperationException(
                    "[ActorCoreFramework] Failed to install into the PlayerLoop.");
            }

            s_worlds.Add(world);

            return new Subscription(world);
        }

        /// <summary>
        /// 引数のWorldをループから解除する。
        /// 解除経路をSubscriptionに一本化するため、外部へは公開しない。
        /// </summary>
        static void Unregister(World world)
        {
            if (!s_worlds.Remove(world)) { return; }

            if (s_worlds.Count <= 0) { Uninstall(); }
        }

        /// <summary>
        /// PlayerLoopへの差し込みを行う。
        /// </summary>
        static bool Install()
        {
            var root = PlayerLoop.GetCurrentPlayerLoop();

            var fixedTick = new PlayerLoopSystem
            {
                type = typeof(ActorCoreFixedTick),
                updateDelegate = OnFixedTick
            };

            var tick = new PlayerLoopSystem
            {
                type = typeof(ActorCoreTick),
                updateDelegate = OnTick
            };

            var unscaledTick = new PlayerLoopSystem
            {
                type = typeof(ActorCoreUnscaledTick),
                updateDelegate = OnUnscaledTick
            };

            var postTick = new PlayerLoopSystem
            {
                type = typeof(ActorCorePostTick),
                updateDelegate = OnPostTick
            };

            // rootはローカルのコピーなので、途中で失敗しても実際のPlayerLoopは変わらない。
            // UnscaledTickはTickの直後に置きたいので、アンカーに挿入済みのTickを指定する。
            var ok = TryInsert(ref root, typeof(FixedUpdate.ScriptRunBehaviourFixedUpdate), fixedTick) &&
                     TryInsert(ref root, typeof(Update.ScriptRunBehaviourUpdate), tick) &&
                     TryInsert(ref root, typeof(ActorCoreTick), unscaledTick) &&
                     TryInsert(ref root, typeof(PreLateUpdate.ScriptRunBehaviourLateUpdate), postTick);

            if (!ok)
            {
                Debug.LogError("[ActorCoreFramework] Failed to install into the PlayerLoop.");
                return false;
            }

            PlayerLoop.SetPlayerLoop(root);
            installed = true;
            return true;
        }

        static void Uninstall()
        {
            if (!installed) { return; }
            
            var root = PlayerLoop.GetCurrentPlayerLoop();
            TryRemove(ref root, typeof(ActorCoreFixedTick));
            TryRemove(ref root, typeof(ActorCoreTick));
            TryRemove(ref root, typeof(ActorCoreUnscaledTick));
            TryRemove(ref root, typeof(ActorCorePostTick));
            
            PlayerLoop.SetPlayerLoop(root);
            installed = false;
        }
        
        static bool TryInsert(ref PlayerLoopSystem parent, Type anchor, in PlayerLoopSystem system)
        {
            var children = parent.subSystemList;
            if (children == null) { return false; }

            for (var i = 0; i < children.Length; i++)
            {
                if (children[i].type == anchor)
                {
                    var inserted = new PlayerLoopSystem[children.Length + 1];
                    Array.Copy(children, 0, inserted, 0, i + 1);
                    inserted[i + 1] = system;
                    Array.Copy(children, i + 1, inserted, i + 2, children.Length - i - 1);

                    parent.subSystemList = inserted;
                    return true;
                }

                if (TryInsert(ref children[i], anchor, system))
                {
                    parent.subSystemList = children;
                    return true;
                }
            }

            return false;
        }

        static bool TryRemove(ref PlayerLoopSystem parent, Type target)
        {
            var children = parent.subSystemList;
            if (children == null) { return false; }

            for (var i = 0; i < children.Length; i++)
            {
                if (children[i].type == target)
                {
                    var removed = new PlayerLoopSystem[children.Length - 1];
                    Array.Copy(children, 0, removed, 0, i);
                    Array.Copy(children, i + 1, removed, i, children.Length - i - 1);

                    parent.subSystemList = removed;
                    return true;
                }

                if (TryRemove(ref children[i], target))
                {
                    parent.subSystemList = children;
                    return true;
                }
            }

            return false;
        }
        
        
        
        static void OnFixedTick() => Dispatch(static (w, dt) => w.FixedTick(dt), Time.fixedDeltaTime);
        static void OnTick() => Dispatch(static (w, dt) => w.Tick(dt), Time.deltaTime);
        static void OnUnscaledTick() => Dispatch(static (w, dt) => w.UnscaledTick(dt), Time.unscaledDeltaTime);
        static void OnPostTick() => Dispatch(static (w, dt) => w.PostTick(dt), Time.deltaTime);

        static void Dispatch(Action<World, float> action, float deltaTime)
        {
            if (s_worlds.Count <= 0) { return; }
            
            // Tick中にWorldの登録・解除が行われても問題ないようにする
            s_snapshot.Clear();
            s_snapshot.AddRange(s_worlds);

            for (var i = 0; i < s_snapshot.Count; i++)
            {
                var world = s_snapshot[i];
                if (!s_worlds.Contains(world)) { continue; }

                try
                {
                    action(world, deltaTime);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
            
            s_snapshot.Clear();
        }

        
#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        static void HookPlayModeStateChanged()
        {
            UnityEditor.EditorApplication.playModeStateChanged += state =>
            {
                if (state != UnityEditor.PlayModeStateChange.ExitingPlayMode) { return; }

                // 静的状態のリセットとは別枠。AutoStaticsCleanupの守備範囲外である
                // 「EditorのPlayerLoopから自分のシステムを物理的に外す」処理。
                for (var i = s_worlds.Count - 1; i >= 0; i--) { Unregister(s_worlds[i]); }
                Uninstall();
            };
        }
#endif

        sealed class Subscription : IDisposable
        {
            World? world;
            
            public Subscription(World world) => this.world = world;
            
            public void Dispose()
            {
                if (world == null) { return; }

                // 二重解除や、解除済みWorldの再登録を巻き添えにしないよう、
                // 自分の参照を先に切ってから解除する。
                var target = world;
                world = null;
                Unregister(target);
            }
        }
    }
}
