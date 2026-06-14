using KakigoriTD.Core;
using NUnit.Framework;

namespace KakigoriTD.Tests.EditMode
{
    /// <summary>EditMode tests for <see cref="SpawnClock"/> (wave spawn timing).</summary>
    public class SpawnClockTests
    {
        [Test]
        public void Tick_BeforeInterval_SpawnsNothing()
        {
            var clock = new SpawnClock(1.5f);
            Assert.That(clock.Tick(0.5f), Is.EqualTo(0));
            Assert.That(clock.Tick(0.5f), Is.EqualTo(0)); // 1.0 < 1.5
        }

        [Test]
        public void Tick_AtInterval_SpawnsOne()
        {
            var clock = new SpawnClock(1.5f);
            clock.Tick(1.0f);
            Assert.That(clock.Tick(0.6f), Is.EqualTo(1)); // 1.6 >= 1.5
        }

        [Test]
        public void Tick_AccumulatesOnePerInterval()
        {
            var clock = new SpawnClock(1.0f);
            int total = 0;
            // Use 0.25 (exactly representable) and step past 3.0 to 3.25 so float drift
            // can't land on the integer boundary: spawns at t=1,2,3 -> 3 total.
            for (int i = 0; i < 13; i++) total += clock.Tick(0.25f);
            Assert.That(total, Is.EqualTo(3));
        }

        [Test]
        public void Tick_WithLargeDt_CatchesUp()
        {
            var clock = new SpawnClock(1.0f);
            Assert.That(clock.Tick(3.2f), Is.EqualTo(3)); // floor(3.2/1.0)=3
        }

        [Test]
        public void Reset_ClearsProgress()
        {
            var clock = new SpawnClock(1.0f);
            clock.Tick(2.5f); // 2 spawns emitted
            clock.Reset();
            Assert.That(clock.Tick(0.5f), Is.EqualTo(0)); // fresh: 0.5 < 1.0
        }
    }
}
