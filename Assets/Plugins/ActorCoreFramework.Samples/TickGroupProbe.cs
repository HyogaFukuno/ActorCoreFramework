using System;
using System.Collections.Generic;
using UnityEngine;

namespace ActorCoreFramework.Samples
{
    /// <summary>
    /// 各TickGroupが想定どおりの頻度とdeltaTimeで回るかを画面に表示する確認用。
    /// timeScaleを0にしたとき、UnscaledTickだけが累積deltaTimeを伸ばし続ける。
    /// </summary>
    public sealed class TickGroupProbe : MonoBehaviour
    {
        sealed class Probe : Actor
        {
            public int Count;
            public float Elapsed;

            public Probe(TickGroup group)
            {
                Name = group.ToString();
                PrimaryActorTick.CanEverTick = true;
                PrimaryActorTick.Group = group;
            }

            protected override void OnTick(float deltaTime)
            {
                Count++;
                Elapsed += deltaTime;
            }
        }

        readonly List<Probe> probes = new();

        World? world;
        IDisposable? loop;

        void Awake()
        {
            world = new World();

            foreach (TickGroup group in Enum.GetValues(typeof(TickGroup)))
            {
                probes.Add(world.Register(new Probe(group)));
            }

            loop = WorldLoop.Register(world);
        }

        void OnDestroy()
        {
            loop?.Dispose();
            world?.Dispose();
        }

        void OnGUI()
        {
            GUILayout.BeginArea(new Rect(10.0f, 10.0f, 380.0f, 200.0f), GUI.skin.box);

            GUILayout.Label($"Time.timeScale = {Time.timeScale:0.00}");

            var paused = Time.timeScale <= 0.0f;
            if (GUILayout.Button(paused ? "Resume (timeScale = 1)" : "Pause (timeScale = 0)"))
            {
                Time.timeScale = paused ? 1.0f : 0.0f;
            }

            foreach (var probe in probes)
            {
                GUILayout.Label($"{probe.Name,-13} count = {probe.Count,6}   elapsed = {probe.Elapsed,7:0.00}s");
            }

            GUILayout.EndArea();
        }
    }
}
