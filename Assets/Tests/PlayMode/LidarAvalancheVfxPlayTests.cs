using System.Collections;
using System.Collections.Generic;
using KakigoriTD.Data;
using KakigoriTD.Gameplay;
using KakigoriTD.Input;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace KakigoriTD.Tests.PlayMode
{
    /// <summary>
    /// PlayMode smoke tests for <see cref="LidarAvalancheVfx"/> (phase-preview-feedback.md §3.4).
    /// Verifies the frame-driven wiring EditMode can't: while shaving, snowballs are emitted from the
    /// detected point and slide out; the generalized-kill callback reaches the registry; emission
    /// stops (and IsEmitting toggles) when shaving / detection stops or on lost.
    /// Rate-limit maths and nearest-selection are covered by EditMode EnemyRegistryTests.
    /// </summary>
    public class LidarAvalancheVfxPlayTests
    {
        private readonly List<GameObject> _junk = new List<GameObject>();

        private sealed class StubInput : IPositionInputSource
        {
            public bool HasPoint { get; set; } = true;
            public Vector3 WorldPoint { get; set; } = new Vector3(0.5f, 0f, 0f);
            public float DistanceMm { get; set; } = 0f;
            public bool IsShaving { get; set; } = true;
            public bool IsLost { get; set; } = false;
        }

        private sealed class SpyChannel : IEnemyChannel
        {
            public Enemy Target;
            public int KillCalls;
            public Enemy FindNearestLive(Vector3 worldPos, float maxRadius) => Target; // 常に同じ敵を返す
            public void KillOne(Enemy e) { KillCalls++; }                              // 消さない（累計を数える）
        }

        private Snowball MakeSnowball()
        {
            var go = new GameObject("snowball");
            _junk.Add(go);
            return go.AddComponent<Snowball>();
        }

        private int ActiveSnowballs()
        {
            int n = 0;
            foreach (var go in _junk)
                if (go != null && go.GetComponent<Snowball>() != null && go.activeSelf) n++;
            return n;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _junk) if (go != null) Object.Destroy(go);
            _junk.Clear();
        }

        [UnityTest]
        public IEnumerator Emits_WhileShaving_ReachesKillPath_Then_Stops()
        {
            var cfg = ScriptableObject.CreateInstance<LidarPreviewConfigSO>();
            var input = new StubInput { WorldPoint = new Vector3(0.5f, 0f, 0f), DistanceMm = 0f };

            var enemyGo = new GameObject("enemy");
            _junk.Add(enemyGo);
            enemyGo.transform.position = new Vector3(1.0f, 0f, 0f); // 検知点(0.5)から放射方向に少し先
            var spy = new SpyChannel { Target = enemyGo.AddComponent<Enemy>() };
            var registry = new EnemyRegistry(new IEnemyChannel[] { spy });

            var vfxGo = new GameObject("LidarAvalancheVfx");
            _junk.Add(vfxGo);
            var vfx = vfxGo.AddComponent<LidarAvalancheVfx>();
            vfx.Configure(input, registry, (x, z) => 0f, MakeSnowball, cfg, ice: null);

            float t = 0f;
            while (t < 1.0f) { t += Time.deltaTime; yield return null; }

            Assert.That(vfx.IsEmitting, Is.True, "削り中は放出している");
            Assert.That(ActiveSnowballs(), Is.GreaterThan(0), "雪玉が射出され active になっている");
            Assert.That(spy.KillCalls, Is.GreaterThanOrEqualTo(1), "雪玉が放射経路上の敵に届いて撃破パスへ到達した");

            input.IsShaving = false;
            yield return null; yield return null;
            Assert.That(vfx.IsEmitting, Is.False, "削りを止めたら放出が止まる");
            int afterStop = ActiveSnowballs();

            t = 0f;
            while (t < 3.0f) { t += Time.deltaTime; yield return null; }
            Assert.That(ActiveSnowballs(), Is.LessThan(afterStop), "残った雪玉は滑って自動でプールへ返る");
        }

        [UnityTest]
        public IEnumerator NoEmit_WhenNoPoint_Or_Lost_Or_NotShaving()
        {
            var cfg = ScriptableObject.CreateInstance<LidarPreviewConfigSO>();
            var input = new StubInput { IsShaving = true, HasPoint = false };
            var registry = new EnemyRegistry(new IEnemyChannel[0]);

            var vfxGo = new GameObject("LidarAvalancheVfx");
            _junk.Add(vfxGo);
            var vfx = vfxGo.AddComponent<LidarAvalancheVfx>();
            vfx.Configure(input, registry, (x, z) => 0f, MakeSnowball, cfg, ice: null);

            yield return WaitFrames(0.3f);
            Assert.That(vfx.IsEmitting, Is.False, "検知点が無ければ放出しない");

            input.HasPoint = true; input.IsLost = true;
            yield return WaitFrames(0.3f);
            Assert.That(vfx.IsEmitting, Is.False, "ロスト中は放出しない");

            input.IsLost = false; input.IsShaving = false;
            yield return WaitFrames(0.3f);
            Assert.That(vfx.IsEmitting, Is.False, "削っていなければ放出しない");

            input.IsShaving = true;
            yield return WaitFrames(0.2f);
            Assert.That(vfx.IsEmitting, Is.True, "検知点あり・ロストなし・削り中なら放出する");
        }

        private static IEnumerator WaitFrames(float seconds)
        {
            float t = 0f;
            while (t < seconds) { t += Time.deltaTime; yield return null; }
        }
    }
}
