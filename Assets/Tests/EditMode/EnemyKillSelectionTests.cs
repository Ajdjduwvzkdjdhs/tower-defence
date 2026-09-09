using System.Collections.Generic;
using KakigoriTD.Gameplay;
using NUnit.Framework;
using UnityEngine;

namespace KakigoriTD.Tests.EditMode
{
    /// <summary>
    /// EditMode tests for <see cref="EnemyKillSelection.SelectFront"/> (360度化 / phase-preview-feedback).
    /// Covers: killRadius filter, nearest pick, the "older overlapping enemy shields the newer one"
    /// depth-control rule, dead enemies ignored, older shield out of killRadius still shields.
    /// </summary>
    public class EnemyKillSelectionTests
    {
        private static readonly bool[] AllAlive3 = { true, true, true };

        private static int Sel(Vector3[] pos, bool[] alive, Vector3 wp, float killRadius, float overlapRadius)
            => EnemyKillSelection.SelectFront(pos, alive, wp, killRadius, overlapRadius);

        [Test]
        public void NoEnemiesInKillRadius_ReturnsMinusOne()
        {
            var pos = new[] { new Vector3(5f, 0f, 0f), new Vector3(-5f, 0f, 0f) };
            Assert.That(Sel(pos, new[] { true, true }, Vector3.zero, 1f, 0.7f), Is.EqualTo(-1));
        }

        [Test]
        public void PicksNearest_WhenNoOverlap()
        {
            // 3 体、互いに離れている（overlap しない）→ worldPos に一番近い添字。
            var pos = new[]
            {
                new Vector3(2f, 0f, 0f),   // 0
                new Vector3(0.3f, 0f, 0f), // 1  ← いちばん近い
                new Vector3(-2f, 0f, 0f),  // 2
            };
            Assert.That(Sel(pos, AllAlive3, Vector3.zero, killRadius: 3f, overlapRadius: 0.5f), Is.EqualTo(1));
        }

        [Test]
        public void OlderOverlappingEnemy_ShieldsNewer_KillsOlderFirst()
        {
            // 0（古）と 1（新）がほぼ同じ位置（overlap 内）。worldPos は 1 のわずかに手前。
            // 1 は「より古い 0 が overlap 内」なので除外 → 0 を撃破。
            var pos = new[]
            {
                new Vector3(1.0f, 0f, 0f),   // 0 古
                new Vector3(1.05f, 0f, 0f),  // 1 新（0 と 0.05 しか離れていない）
            };
            int hit = Sel(pos, new[] { true, true }, new Vector3(1.1f, 0f, 0f), killRadius: 1f, overlapRadius: 0.5f);
            Assert.That(hit, Is.EqualTo(0), "重なっていたら先にいた方（古い）が前列");
        }

        [Test]
        public void AfterOlderDies_NewerBecomesFront()
        {
            var pos = new[]
            {
                new Vector3(1.0f, 0f, 0f),   // 0 古（もう死んでいる）
                new Vector3(1.05f, 0f, 0f),  // 1 新
            };
            int hit = Sel(pos, new[] { false, true }, new Vector3(1.1f, 0f, 0f), killRadius: 1f, overlapRadius: 0.5f);
            Assert.That(hit, Is.EqualTo(1), "前列が消えたら次の古い方が前列になる");
        }

        [Test]
        public void OlderShield_OutsideKillRadius_StillShields()
        {
            // 0（古）は worldPos から 1.3 = killRadius(1.0) 外。1（新）は 0.9 で killRadius 内。
            // 0 と 1 は 0.4 しか離れておらず overlap(0.5) 内 → 1 は前列でない。他に候補なし → -1。
            var pos = new[]
            {
                new Vector3(1.3f, 0f, 0f),   // 0 古（killRadius 外）
                new Vector3(0.9f, 0f, 0f),   // 1 新（killRadius 内）
            };
            int hit = Sel(pos, new[] { true, true }, Vector3.zero, killRadius: 1.0f, overlapRadius: 0.5f);
            Assert.That(hit, Is.EqualTo(-1), "killRadius 外の古い敵でも overlap 内なら新しい方を庇う");
        }

        [Test]
        public void DeadEnemies_AreSkipped_AsCandidatesAndAsShields()
        {
            var pos = new[]
            {
                new Vector3(1.0f, 0f, 0f),   // 0 死
                new Vector3(1.05f, 0f, 0f),  // 1 生（0 は死んでいるので庇われない）
                new Vector3(0.2f, 0f, 0f),   // 2 生・より近い・overlap 相手なし
            };
            int hit = Sel(pos, new[] { false, true, true }, Vector3.zero, killRadius: 3f, overlapRadius: 0.5f);
            Assert.That(hit, Is.EqualTo(2), "死んだ敵は候補にも庇いにもならない → 最近傍の生存敵");
        }

        [Test]
        public void YZ_Ignored_OnlyXZ()
        {
            // Y が大きく違っても XZ で判定する（斜面の高さ差は無視）。
            var pos = new[] { new Vector3(0.3f, 5f, 0f) };
            Assert.That(Sel(pos, new[] { true }, Vector3.zero, 1f, 0.5f), Is.EqualTo(0));
        }
    }
}
