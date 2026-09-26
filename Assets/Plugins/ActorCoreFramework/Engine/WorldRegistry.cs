using System.Collections.Generic;
using System.Diagnostics;

namespace ActorCoreFramework
{
    /// <summary>
    /// 生きているWorldの一覧。World Debuggerが表示対象を見つけるためだけに使う。
    ///
    /// Editorでのみ記録し、ビルドでは呼び出しごと消える(Conditional)。
    /// Disposeされていない限り一覧に残り続けるので、Play modeを抜けても残っているWorldは
    /// Disposeし忘れとしてDebuggerに表示できる。
    /// </summary>
    internal static class WorldRegistry
    {
        static readonly List<World> s_worlds = new();

        /// <summary>一覧が変わるたびに進む。表示側が作り直しの要否を判定するのに使う。</summary>
        internal static int Version { get; private set; }

        [Conditional("UNITY_EDITOR")]
        internal static void Add(World world)
        {
            // Worldの生成は所有スレッドに縛られないので、一覧の更新だけは排他する
            lock (s_worlds)
            {
                s_worlds.Add(world);
                Version++;
            }
        }

        [Conditional("UNITY_EDITOR")]
        internal static void Remove(World world)
        {
            lock (s_worlds)
            {
                if (s_worlds.Remove(world)) { Version++; }
            }
        }

        /// <summary>生きているWorldを生成順に詰める。</summary>
        internal static void CopyTo(List<World> results)
        {
            results.Clear();

            lock (s_worlds)
            {
                results.AddRange(s_worlds);
            }
        }
    }
}
