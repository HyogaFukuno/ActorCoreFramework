using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.PlayerLoop;
using UnityEngine.TestTools;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace ActorCoreFramework.PlayModeTests
{
    /// <summary>
    /// Actor.TickとMonoBehaviour.Updateの配送コストを同数で比較する計測。
    ///
    /// 計測対象のPlayerLoopシステム(ScriptRunBehaviourUpdate / ActorCoreTick)の直前と直後に
    /// 印を差し込み、その間の経過時間を1フレームごとに記録する。
    /// 本体の処理はどちらも整数のインクリメントだけなので、差はほぼ配送の仕組みの差になる。
    ///
    /// 通常のテスト実行に混ざると遅いため Explicit にしている。名前を指定して実行すること。
    /// 結果は Logs/TickVsUpdateBenchmark.md に書き出す。
    /// </summary>
    [Explicit("計測用。通常のテスト実行には含めない")]
    [Category("Benchmark")]
    public sealed class TickVsUpdateBenchmark
    {
        const int WarmupFrames = 60;
        const int MeasureFrames = 600;

        static readonly int[] Counts = { 100, 1000, 10000 };

        struct BeginMarker { }
        struct EndMarker { }

        static long s_beginStamp;
        static bool s_recording;
        static readonly List<double> s_samples = new(MeasureFrames);

        readonly List<GameObject> gameObjects = new();
        World? world;
        IDisposable? subscription;

        [TearDown]
        public void TearDown()
        {
            RemoveMarkers();
            subscription?.Dispose();
            world?.Dispose();
            foreach (var gameObject in gameObjects) { Object.Destroy(gameObject); }
            gameObjects.Clear();
        }

        [UnityTest]
        public IEnumerator Measure()
        {
            var rows = new List<Row>();

            // 印だけを挟んだ空の計測。各行からこれを引いて配送1回あたりの時間を出す
            var updateBaseline = new Row("Update (baseline)", 0);
            yield return MeasureUpdate(updateBaseline, 0, enabled: true);
            rows.Add(updateBaseline);

            var tickBaseline = new Row("Tick (baseline)", 0);
            yield return MeasureTick(tickBaseline, 0, TickKind.Actor, enabled: true);
            rows.Add(tickBaseline);

            foreach (var count in Counts)
            {
                var update = new Row("MonoBehaviour.Update", count, updateBaseline);
                yield return MeasureUpdate(update, count, enabled: true);
                rows.Add(update);

                var tick = new Row("Actor.OnTick", count, tickBaseline);
                yield return MeasureTick(tick, count, TickKind.Actor, enabled: true);
                rows.Add(tick);

                var component = new Row("ActorComponent.OnTick (Actor 1体に1個)", count, tickBaseline);
                yield return MeasureTick(component, count, TickKind.Component, enabled: true);
                rows.Add(component);

                var updateDisabled = new Row("MonoBehaviour.Update (enabled=false)", count, updateBaseline);
                yield return MeasureUpdate(updateDisabled, count, enabled: false);
                rows.Add(updateDisabled);

                var tickDisabled = new Row("Actor.OnTick (Enabled=false)", count, tickBaseline);
                yield return MeasureTick(tickDisabled, count, TickKind.Actor, enabled: false);
                rows.Add(tickDisabled);
            }

            Report(rows);
        }


        IEnumerator MeasureUpdate(Row row, int count, bool enabled)
        {
            // Tick側と同じPlayerLoopの構成で測るため、空のWorldも登録しておく
            BeginScenario();

            for (var i = 0; i < count; i++)
            {
                var gameObject = new GameObject("UpdateBench");
                var behaviour = gameObject.AddComponent<CountingBehaviour>();
                behaviour.enabled = enabled;
                gameObjects.Add(gameObject);
            }

            yield return Record(row, typeof(Update.ScriptRunBehaviourUpdate));

            EndScenario();
        }

        IEnumerator MeasureTick(Row row, int count, TickKind kind, bool enabled)
        {
            BeginScenario();

            for (var i = 0; i < count; i++)
            {
                Actor actor = kind == TickKind.Actor ? new CountingBenchActor() : new ComponentHostActor();
                actor.PrimaryActorTick.Enabled = enabled;
                world!.Register(actor);
            }

            yield return Record(row, typeof(WorldLoop.ActorCoreTick));

            EndScenario();
        }

        void BeginScenario()
        {
            world = new World();
            subscription = WorldLoop.Register(world);
        }

        void EndScenario()
        {
            RemoveMarkers();

            subscription?.Dispose();
            subscription = null;
            world?.Dispose();
            world = null;

            foreach (var gameObject in gameObjects) { Object.Destroy(gameObject); }
            gameObjects.Clear();
        }

        IEnumerator Record(Row row, Type target)
        {
            // Start等の初回処理とJITを計測から外す
            InsertMarkers(target);
            for (var i = 0; i < WarmupFrames; i++) { yield return null; }

            // 計測中のGCによる外れ値を減らす
            GC.Collect();
            GC.WaitForPendingFinalizers();
            yield return null;

            s_samples.Clear();
            s_recording = true;
            for (var i = 0; i < MeasureFrames; i++) { yield return null; }
            s_recording = false;

            row.SetSamples(s_samples);
        }


        // --- PlayerLoopへの印の差し込み ---

        static void OnBegin() => s_beginStamp = Stopwatch.GetTimestamp();

        static void OnEnd()
        {
            if (!s_recording) { return; }

            var elapsed = Stopwatch.GetTimestamp() - s_beginStamp;
            s_samples.Add(elapsed * 1_000_000.0 / Stopwatch.Frequency);
        }

        static void InsertMarkers(Type target)
        {
            var root = PlayerLoop.GetCurrentPlayerLoop();
            var begin = new PlayerLoopSystem { type = typeof(BeginMarker), updateDelegate = OnBegin };
            var end = new PlayerLoopSystem { type = typeof(EndMarker), updateDelegate = OnEnd };

            if (!TryInsertAround(ref root, target, begin, end))
            {
                throw new InvalidOperationException($"{target.Name} was not found in the PlayerLoop.");
            }

            PlayerLoop.SetPlayerLoop(root);
        }

        static void RemoveMarkers()
        {
            var root = PlayerLoop.GetCurrentPlayerLoop();
            var removed = TryRemove(ref root, typeof(BeginMarker));
            removed |= TryRemove(ref root, typeof(EndMarker));

            if (removed) { PlayerLoop.SetPlayerLoop(root); }
        }

        static bool TryInsertAround(ref PlayerLoopSystem parent, Type target,
                                    in PlayerLoopSystem begin, in PlayerLoopSystem end)
        {
            var children = parent.subSystemList;
            if (children == null) { return false; }

            for (var i = 0; i < children.Length; i++)
            {
                if (children[i].type == target)
                {
                    var inserted = new List<PlayerLoopSystem>(children);
                    inserted.Insert(i + 1, end);
                    inserted.Insert(i, begin);

                    parent.subSystemList = inserted.ToArray();
                    return true;
                }

                if (TryInsertAround(ref children[i], target, begin, end))
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

            var kept = new List<PlayerLoopSystem>(children.Length);
            var removed = false;

            for (var i = 0; i < children.Length; i++)
            {
                if (children[i].type == target)
                {
                    removed = true;
                    continue;
                }

                var child = children[i];
                removed |= TryRemove(ref child, target);
                kept.Add(child);
            }

            if (removed) { parent.subSystemList = kept.ToArray(); }
            return removed;
        }


        // --- 結果の集計と出力 ---

        static void Report(List<Row> rows)
        {
            var builder = new StringBuilder();
            builder.AppendLine("# Tick vs Update benchmark");
            builder.AppendLine();
            builder.AppendLine($"- Unity {Application.unityVersion} / {Application.platform} / " +
                               $"{(Application.isEditor ? "Editor" : "Player")} / " +
                               $"{SystemInfo.processorType}");
            builder.AppendLine($"- Scripting backend: {ScriptingBackend()} / " +
                               $"Code optimization: {CodeOptimization()}");
            builder.AppendLine($"- Warmup {WarmupFrames} frames, measured {MeasureFrames} frames");
            builder.AppendLine();
            builder.AppendLine("| ケース | 数 | 1フレームの中央値 (µs) | 平均 (µs) | p95 (µs) | 1回あたり (ns) |");
            builder.AppendLine("|---|---:|---:|---:|---:|---:|");

            foreach (var row in rows)
            {
                var perCall = row.Count > 0 && row.Baseline != null
                    ? $"{(row.Median - row.Baseline.Median) * 1000.0 / row.Count:F1}"
                    : "-";

                builder.AppendLine($"| {row.Name} | {row.Count} | {row.Median:F1} | {row.Mean:F1} | " +
                                   $"{row.P95:F1} | {perCall} |");
            }

            var report = builder.ToString();
            Debug.Log("[TickVsUpdateBenchmark]\n" + report);

            var path = Path.Combine(Application.dataPath, "..", "Logs", "TickVsUpdateBenchmark.md");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, report);
        }

        static string ScriptingBackend()
        {
#if ENABLE_IL2CPP
            return "IL2CPP";
#else
            return "Mono";
#endif
        }

        /// <summary>
        /// Editorでは Debug.isDebugBuild が常にtrueなので、実際のコード最適化の設定を読む。
        /// </summary>
        static string CodeOptimization()
        {
#if UNITY_EDITOR
            return UnityEditor.Compilation.CompilationPipeline.codeOptimization.ToString();
#else
            return Debug.isDebugBuild ? "Debug" : "Release";
#endif
        }

        enum TickKind { Actor, Component }

        sealed class Row
        {
            public readonly string Name;
            public readonly int Count;
            public readonly Row? Baseline;
            public double Median;
            public double Mean;
            public double P95;

            public Row(string name, int count, Row? baseline = null)
            {
                Name = name;
                Count = count;
                Baseline = baseline;
            }

            public void SetSamples(List<double> samples)
            {
                var sorted = new List<double>(samples);
                sorted.Sort();

                var sum = 0.0;
                foreach (var sample in sorted) { sum += sample; }

                Median = sorted[sorted.Count / 2];
                Mean = sum / sorted.Count;
                P95 = sorted[(int)(sorted.Count * 0.95)];
            }
        }
    }


    // --- 計測対象。本体の処理はどれも同じにする ---

    internal sealed class CountingBehaviour : MonoBehaviour
    {
        public int TickCount;

        void Update() => TickCount++;
    }

    internal sealed class CountingBenchActor : Actor
    {
        public int TickCount;

        public CountingBenchActor() => PrimaryActorTick.CanEverTick = true;

        protected override void OnTick(float deltaTime) => TickCount++;
    }

    /// <summary>自身はOnTickを持たず、本体の処理をComponentに置くActor。</summary>
    internal sealed class ComponentHostActor : Actor
    {
        public ComponentHostActor()
        {
            PrimaryActorTick.CanEverTick = true;
            AddComponent(new CountingBenchComponent());
        }
    }

    internal sealed class CountingBenchComponent : ActorComponent
    {
        public int TickCount;

        protected override void OnTick(float deltaTime) => TickCount++;
    }
}
