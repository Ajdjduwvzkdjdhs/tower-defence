using NUnit.Framework;
using KakigoriTD.Core;

namespace KakigoriTD.Tests.EditMode
{
    /// <summary>
    /// 撃破数の進捗（クリア条件＋難易度ランプの軸・ADR-30）。
    /// </summary>
    public class KillProgressTests
    {
        [Test]
        public void Add_IncrementsCount()
        {
            var p = new KillProgress(100);
            p.Add();
            p.Add(5);
            Assert.That(p.Count, Is.EqualTo(6));
        }

        [Test]
        public void Add_NonPositive_Ignored()
        {
            var p = new KillProgress(100);
            p.Add(0);
            p.Add(-3);
            Assert.That(p.Count, Is.EqualTo(0));
        }

        [Test]
        public void Progress01_IsCountOverTarget()
        {
            var p = new KillProgress(100);
            p.Add(25);
            Assert.That(p.Progress01, Is.EqualTo(0.25f).Within(1e-4f));
        }

        [Test]
        public void Progress01_ClampsAtOne()
        {
            var p = new KillProgress(10);
            p.Add(25);
            Assert.That(p.Progress01, Is.EqualTo(1f).Within(1e-4f));
        }

        [Test]
        public void IsComplete_AtOrAboveTarget()
        {
            var p = new KillProgress(10);
            p.Add(9);
            Assert.That(p.IsComplete, Is.False);
            p.Add();
            Assert.That(p.IsComplete, Is.True);
        }

        [Test]
        public void Target_BelowOne_ClampedToOne()
        {
            var p = new KillProgress(0);
            Assert.That(p.Target, Is.EqualTo(1));
        }

        [Test]
        public void Reset_ClearsCount()
        {
            var p = new KillProgress(10);
            p.Add(5);
            p.Reset();
            Assert.That(p.Count, Is.EqualTo(0));
            Assert.That(p.Progress01, Is.EqualTo(0f).Within(1e-4f));
        }
    }
}
