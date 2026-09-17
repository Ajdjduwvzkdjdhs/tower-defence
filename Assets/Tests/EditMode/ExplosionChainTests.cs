using System.Collections.Generic;
using NUnit.Framework;
using KakigoriTD.Core;

namespace KakigoriTD.Tests.EditMode
{
    /// <summary>
    /// 爆発兵の誘爆連鎖（ADR-30）。連鎖は無制限で、制御は出現率と爆発半径という
    /// 可視のパラメータだけが行う（段数制限・減衰などの不可視ルールは入れない）。
    /// </summary>
    public class ExplosionChainTests
    {
        // 座標列から入力を組み立てる小さなヘルパ（xs, zs, isBomber, alive）。
        private static void Build(
            (float x, float z, bool bomber, bool alive)[] src,
            out List<float> xs, out List<float> zs, out List<bool> bombers, out List<bool> alive)
        {
            xs = new List<float>(); zs = new List<float>();
            bombers = new List<bool>(); alive = new List<bool>();
            foreach (var s in src)
            {
                xs.Add(s.x); zs.Add(s.z); bombers.Add(s.bomber); alive.Add(s.alive);
            }
        }

        private static List<int> Resolve(
            (float x, float z, bool bomber, bool alive)[] src, int origin, float radius,
            List<int> detonated = null)
        {
            Build(src, out var xs, out var zs, out var bombers, out var alive);
            var killed = new List<int>();
            ExplosionChain.Resolve(xs, zs, bombers, alive, origin, radius, killed, detonated);
            return killed;
        }

        [Test]
        public void KillsEveryoneInRadius_ExcludingOrigin()
        {
            var killed = Resolve(new[]
            {
                (0f, 0f, true,  true),   // 0: 起点の爆発兵
                (1f, 0f, false, true),   // 1: 半径内
                (0f, 2f, false, true),   // 2: 半径内
                (9f, 0f, false, true),   // 3: 半径外
            }, origin: 0, radius: 3f);

            Assert.That(killed, Is.EquivalentTo(new[] { 1, 2 }));
            Assert.That(killed.Contains(0), Is.False, "起点は呼び出し側が別途撃破する");
        }

        [Test]
        public void DeadEnemies_AreSkipped()
        {
            var killed = Resolve(new[]
            {
                (0f, 0f, true,  true),
                (1f, 0f, false, false), // 既に死んでいる
            }, origin: 0, radius: 3f);

            Assert.That(killed, Is.Empty);
        }

        [Test]
        public void BomberInRadius_Detonates_AndPropagates()
        {
            // 0 —2— 1 —2— 2 : 半径 2.5 なら 0→1→2 と伝播する（0 と 2 は 4 離れていて直接は届かない）。
            var detonated = new List<int>();
            var killed = Resolve(new[]
            {
                (0f, 0f, true, true),
                (2f, 0f, true, true),
                (4f, 0f, true, true),
            }, origin: 0, radius: 2.5f, detonated: detonated);

            Assert.That(killed, Is.EquivalentTo(new[] { 1, 2 }));
            Assert.That(detonated, Is.EquivalentTo(new[] { 0, 1, 2 }), "爆発兵は全て爆発する");
        }

        [Test]
        public void Chain_IsUnbounded_AcrossManyHops()
        {
            // 一列に 20 体の爆発兵を間隔 1 で並べ、半径 1.5 で端から起爆 → 全部倒れる。
            var src = new (float, float, bool, bool)[20];
            for (int i = 0; i < 20; i++) src[i] = (i * 1f, 0f, true, true);

            var killed = Resolve(src, origin: 0, radius: 1.5f);

            Assert.That(killed.Count, Is.EqualTo(19), "段数制限を設けない（ADR-30）");
        }

        [Test]
        public void NonBomberVictim_DoesNotPropagate()
        {
            // 0(爆発兵) —1— 1(通常) —1— 2(通常): 半径 1.5 なら 1 までしか届かない。
            var killed = Resolve(new[]
            {
                (0f, 0f, true,  true),
                (1f, 0f, false, true),
                (2f, 0f, false, true),
            }, origin: 0, radius: 1.5f);

            Assert.That(killed, Is.EquivalentTo(new[] { 1 }));
        }

        [Test]
        public void ShieldLikeVictims_AreKilledRegardless()
        {
            // 盾兵も「生存している敵」として一律に巻き込む（爆発は耐久を無視する）。
            var killed = Resolve(new[]
            {
                (0f, 0f, true,  true),
                (1f, 1f, false, true),
            }, origin: 0, radius: 3f);

            Assert.That(killed, Is.EquivalentTo(new[] { 1 }));
        }

        [Test]
        public void NoDoubleKill_WhenReachableFromMultipleBombers()
        {
            // 2 は 0 からも 1 からも半径内。二重に積まれないこと。
            var killed = Resolve(new[]
            {
                (0f, 0f, true,  true),
                (1f, 0f, true,  true),
                (0.5f, 0.5f, false, true),
            }, origin: 0, radius: 3f);

            Assert.That(killed.Count, Is.EqualTo(2));
            Assert.That(killed, Is.EquivalentTo(new[] { 1, 2 }));
        }

        [Test]
        public void OriginOutOfRange_DoesNothing()
        {
            var killed = Resolve(new[]
            {
                (0f, 0f, true, true),
            }, origin: 5, radius: 3f);

            Assert.That(killed, Is.Empty);
        }

        [Test]
        public void ZeroRadius_KillsNobody()
        {
            var killed = Resolve(new[]
            {
                (0f, 0f, true,  true),
                (0.5f, 0f, false, true),
            }, origin: 0, radius: 0f);

            Assert.That(killed, Is.Empty);
        }
    }
}
