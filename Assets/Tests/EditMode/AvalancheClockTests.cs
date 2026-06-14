using KakigoriTD.Core;
using NUnit.Framework;

namespace KakigoriTD.Tests.EditMode
{
    /// <summary>
    /// EditMode tests for <see cref="AvalancheClock"/> (pure avalanche timing).
    /// Covers Phase 1 acceptance: judgment starts after avalancheDelay, then one row per
    /// killInterval, and stopping/changing channel resets progress.
    /// </summary>
    public class AvalancheClockTests
    {
        // Feed dt in small steps and sum the rows the clock says to kill.
        private static int Accumulate(AvalancheClock clock, float totalTime, float step, bool active)
        {
            int sum = 0;
            for (float t = 0f; t < totalTime - 1e-6f; t += step)
                sum += clock.Tick(step, active);
            return sum;
        }

        [Test]
        public void Tick_WithZeroDelay_KillsOnFirstActiveFrame()
        {
            var clock = new AvalancheClock(0f, 0.4f);

            int n = clock.Tick(0.1f, true);

            Assert.That(n, Is.EqualTo(1)); // 即時（avalancheDelay=0）
        }

        [Test]
        public void Tick_BeforeDelay_KillsNothing()
        {
            var clock = new AvalancheClock(0.5f, 0.4f);

            Assert.That(clock.Tick(0.2f, true), Is.EqualTo(0)); // 0.2 < 0.5
            Assert.That(clock.Tick(0.2f, true), Is.EqualTo(0)); // 0.4 < 0.5
        }

        [Test]
        public void Tick_AfterDelay_StartsKilling()
        {
            var clock = new AvalancheClock(0.5f, 0.4f);
            clock.Tick(0.2f, true); // 0.2
            clock.Tick(0.2f, true); // 0.4

            int n = clock.Tick(0.2f, true); // 0.6 >= 0.5 -> first kill

            Assert.That(n, Is.EqualTo(1));
        }

        [Test]
        public void Tick_KillsOneRowPerInterval()
        {
            var clock = new AvalancheClock(0f, 0.4f);

            // 1.0s in 0.1 steps: kills at shaveTime 0,0.4,0.8 -> 3 rows total.
            int total = Accumulate(clock, 1.0f, 0.1f, true);

            Assert.That(total, Is.EqualTo(3));
        }

        [Test]
        public void Tick_WithLargeDt_CatchesUpCumulatively()
        {
            var clock = new AvalancheClock(0f, 0.4f);

            // One big frame of 1.0s -> kills for shaveTime 0,0.4,0.8 -> 3.
            int n = clock.Tick(1.0f, true);

            Assert.That(n, Is.EqualTo(3));
        }

        [Test]
        public void Tick_WhenInactive_ResetsProgress()
        {
            var clock = new AvalancheClock(0f, 0.4f);
            clock.Tick(0.1f, true); // kill 1, shaveTime 0.1

            int stopped = clock.Tick(0.1f, false); // stop -> reset
            int restarted = clock.Tick(0.1f, true); // fresh session -> immediate kill again

            Assert.That(stopped, Is.EqualTo(0));
            Assert.That(clock.ShaveTime, Is.EqualTo(0.1f).Within(1e-5f)); // reset then one 0.1 step
            Assert.That(restarted, Is.EqualTo(1));
        }

        [Test]
        public void Tick_DelayAppliesAgainAfterReset()
        {
            var clock = new AvalancheClock(0.5f, 0.4f);
            clock.Tick(0.6f, true); // past delay, 1 kill

            clock.Tick(0f, false);  // reset

            Assert.That(clock.Tick(0.2f, true), Is.EqualTo(0)); // delay applies again (0.2 < 0.5)
        }
    }
}
