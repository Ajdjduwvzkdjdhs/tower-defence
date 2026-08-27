using System.Collections.Generic;
using KakigoriTD.Gameplay;
using NUnit.Framework;
using UnityEngine;

namespace KakigoriTD.Tests.EditMode
{
    /// <summary>
    /// EditMode tests for <see cref="EnemyRegistry"/> (phase-preview-feedback.md §3.4/§3.6).
    /// Covers: globally-nearest selection across channels, the shared kill cooldown gate
    /// (time injected), no-enemy case, zero-cooldown back-to-back.
    /// </summary>
    public class EnemyRegistryTests
    {
        private readonly List<GameObject> _spawned = new List<GameObject>();

        private Enemy MakeEnemy(Vector3 pos)
        {
            var go = new GameObject("enemy");
            _spawned.Add(go);
            go.transform.position = pos;
            return go.AddComponent<Enemy>();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _spawned) Object.DestroyImmediate(go);
            _spawned.Clear();
        }

        private class FakeChannel : IEnemyChannel
        {
            public Enemy Nearest;
            public Enemy LastKilled;
            public int KillCalls;
            public Enemy FindNearestLive(Vector3 worldPos, float maxRadius) => Nearest;
            public void KillOne(Enemy e) { LastKilled = e; KillCalls++; }
        }

        [Test]
        public void TryKillNearest_PicksGloballyNearestAcrossChannels()
        {
            var near = MakeEnemy(new Vector3(1f, 0f, 0f));
            var far = MakeEnemy(new Vector3(5f, 0f, 0f));
            var chNear = new FakeChannel { Nearest = near };
            var chFar = new FakeChannel { Nearest = far };
            var reg = new EnemyRegistry(new IEnemyChannel[] { chFar, chNear });

            bool killed = reg.TryKillNearest(Vector3.zero, radius: 10f, cooldownSec: 0.3f, now: 0f);

            Assert.That(killed, Is.True);
            Assert.That(chNear.KillCalls, Is.EqualTo(1));
            Assert.That(chNear.LastKilled, Is.SameAs(near));
            Assert.That(chFar.KillCalls, Is.EqualTo(0));
        }

        [Test]
        public void TryKillNearest_CooldownGate_BlocksWithinInterval()
        {
            var e = MakeEnemy(new Vector3(1f, 0f, 0f));
            var ch = new FakeChannel { Nearest = e };
            var reg = new EnemyRegistry(new IEnemyChannel[] { ch });

            Assert.That(reg.TryKillNearest(Vector3.zero, 10f, 0.3f, now: 0f), Is.True);
            Assert.That(reg.TryKillNearest(Vector3.zero, 10f, 0.3f, now: 0.1f), Is.False); // 0.1 < 0.3
            Assert.That(reg.TryKillNearest(Vector3.zero, 10f, 0.3f, now: 0.29f), Is.False);
            Assert.That(reg.TryKillNearest(Vector3.zero, 10f, 0.3f, now: 0.4f), Is.True);  // 0.4 - 0 >= 0.3? gate is vs last kill (0.0) → 0.4 ok; last kill now 0.4
            Assert.That(ch.KillCalls, Is.EqualTo(2));
        }

        [Test]
        public void TryKillNearest_NoEnemies_ReturnsFalse_NoCooldownStamp()
        {
            var ch = new FakeChannel { Nearest = null };
            var reg = new EnemyRegistry(new IEnemyChannel[] { ch });

            Assert.That(reg.TryKillNearest(Vector3.zero, 10f, 0.3f, now: 0f), Is.False);
            // 敵が居ないだけならクールダウンは消費しない：直後に敵が現れたら撃てる
            ch.Nearest = MakeEnemy(new Vector3(1f, 0f, 0f));
            Assert.That(reg.TryKillNearest(Vector3.zero, 10f, 0.3f, now: 0.01f), Is.True);
        }

        [Test]
        public void TryKillNearest_ZeroCooldown_AllowsBackToBack()
        {
            var e = MakeEnemy(new Vector3(1f, 0f, 0f));
            var ch = new FakeChannel { Nearest = e };
            var reg = new EnemyRegistry(new IEnemyChannel[] { ch });

            Assert.That(reg.TryKillNearest(Vector3.zero, 10f, 0f, now: 0f), Is.True);
            Assert.That(reg.TryKillNearest(Vector3.zero, 10f, 0f, now: 0f), Is.True);
            Assert.That(reg.TryKillNearest(Vector3.zero, 10f, 0f, now: 0f), Is.True);
            Assert.That(ch.KillCalls, Is.EqualTo(3));
        }

        [Test]
        public void TryKillNearest_NullChannelEntry_IsSkipped()
        {
            var e = MakeEnemy(new Vector3(2f, 0f, 0f));
            var ch = new FakeChannel { Nearest = e };
            var reg = new EnemyRegistry(new IEnemyChannel[] { null, ch, null });
            Assert.That(reg.TryKillNearest(Vector3.zero, 10f, 0.3f, now: 0f), Is.True);
            Assert.That(ch.KillCalls, Is.EqualTo(1));
        }
    }
}
