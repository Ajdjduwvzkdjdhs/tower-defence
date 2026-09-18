using KakigoriTD.Core;
using NUnit.Framework;

namespace KakigoriTD.Tests.EditMode
{
    /// <summary>
    /// 盤面人数維持の湧きクロック（ADR-36）。倒した分だけ補充し、満員なら湧かない。
    /// </summary>
    public class PopulationSpawnClockTests
    {
        [Test]
        public void Tick_WhenAtDesired_SpawnsNothing()
        {
            var clock = new PopulationSpawnClock(10f);
            Assert.That(clock.Tick(1f, aliveCount: 8, desiredCount: 8), Is.EqualTo(0));
            Assert.That(clock.Tick(1f, aliveCount: 9, desiredCount: 8), Is.EqualTo(0));
        }

        [Test]
        public void Tick_RefillsAtRateWhileBelowDesired()
        {
            var clock = new PopulationSpawnClock(4f); // 0.25 秒に 1 体
            Assert.That(clock.Tick(0.2f, 0, 10), Is.EqualTo(0));
            Assert.That(clock.Tick(0.1f, 0, 10), Is.EqualTo(1)); // 0.3 秒 → 1 体
            Assert.That(clock.Tick(0.5f, 1, 10), Is.EqualTo(2)); // 残り 0.05 + 0.5 = 0.55 秒 → 2 体
        }

        [Test]
        public void Tick_NeverExceedsDeficit()
        {
            var clock = new PopulationSpawnClock(100f);
            Assert.That(clock.Tick(1f, aliveCount: 7, desiredCount: 10), Is.EqualTo(3)); // 100 体分の予算があっても不足分まで
        }

        [Test]
        public void Tick_DoesNotBankBudgetWhileFull()
        {
            var clock = new PopulationSpawnClock(4f);
            clock.Tick(10f, aliveCount: 10, desiredCount: 10); // 満員のまま 10 秒（予算は貯まらない）
            Assert.That(clock.Tick(0.1f, aliveCount: 5, desiredCount: 10), Is.EqualTo(0)); // 空いた直後に一気に吐かない
            Assert.That(clock.Tick(0.15f, aliveCount: 5, desiredCount: 10), Is.EqualTo(1)); // 0.25 秒で 1 体
        }

        [Test]
        public void Tick_AfterWipe_RefillsUpToDesiredOverTime()
        {
            var clock = new PopulationSpawnClock(5f);
            int alive = 0;
            float t = 0f;
            while (alive < 12 && t < 10f)
            {
                alive += clock.Tick(0.05f, alive, 12);
                t += 0.05f;
            }
            Assert.That(alive, Is.EqualTo(12));
            Assert.That(t, Is.EqualTo(2.4f).Within(0.1f)); // 12 体 ÷ 5 体/秒
        }

        [Test]
        public void Tick_DesiredCanRampUp()
        {
            var clock = new PopulationSpawnClock(10f);
            Assert.That(clock.Tick(1f, aliveCount: 6, desiredCount: 6), Is.EqualTo(0));
            Assert.That(clock.Tick(1f, aliveCount: 6, desiredCount: 8), Is.EqualTo(2)); // 目標が上がれば差分を補充
        }

        [Test]
        public void Reset_ClearsBudget()
        {
            var clock = new PopulationSpawnClock(4f);
            clock.Tick(0.2f, 0, 10); // 0.8 体分の予算
            clock.Reset();
            Assert.That(clock.Tick(0.2f, 0, 10), Is.EqualTo(0)); // 0.8 → 1 体にならない
        }

        [Test]
        public void Ctor_ClampsNonPositiveRate()
        {
            var clock = new PopulationSpawnClock(0f);
            Assert.That(clock.RefillRate, Is.GreaterThan(0f));
        }
    }
}
