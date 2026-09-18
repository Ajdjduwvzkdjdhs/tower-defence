using NUnit.Framework;
using KakigoriTD.Core;

namespace KakigoriTD.Tests.EditMode
{
    /// <summary>キー長押し確定（ADR-34：ランキング消去の誤操作防止）。</summary>
    public class HoldToConfirmTests
    {
        [Test]
        public void FiresOnceWhenHoldReachesThreshold()
        {
            var h = new HoldToConfirm(5f);
            for (int i = 0; i < 4; i++) Assert.That(h.Tick(1f, true), Is.False);
            Assert.That(h.Tick(1f, true), Is.True, "5 秒到達で発火");
            Assert.That(h.Tick(1f, true), Is.False, "押し続けても再発火しない");
            Assert.That(h.HasFired, Is.True, "離すまで発火済みのまま");
            Assert.That(h.Progress01, Is.EqualTo(1f));
            h.Tick(0f, false);
            Assert.That(h.HasFired, Is.False);
        }

        [Test]
        public void ReleaseResetsProgress()
        {
            var h = new HoldToConfirm(5f);
            h.Tick(3f, true);
            Assert.That(h.Progress01, Is.EqualTo(0.6f).Within(1e-4f));
            h.Tick(0.1f, false);
            Assert.That(h.IsHolding, Is.False);
            Assert.That(h.Progress01, Is.EqualTo(0f));
            Assert.That(h.Tick(3f, true), Is.False, "離した後は積算し直し");
        }

        [Test]
        public void RearmsAfterRelease()
        {
            var h = new HoldToConfirm(1f);
            Assert.That(h.Tick(1f, true), Is.True);
            h.Tick(0f, false);
            Assert.That(h.Tick(1f, true), Is.True, "離して押し直せばもう一度発火");
        }

        [Test]
        public void NotHeld_NeverFires()
        {
            var h = new HoldToConfirm(0.5f);
            for (int i = 0; i < 10; i++) Assert.That(h.Tick(1f, false), Is.False);
        }

        [Test]
        public void NonPositiveHoldSec_IsClampedNotZero()
        {
            Assert.That(new HoldToConfirm(0f).HoldSec, Is.GreaterThan(0f));
        }
    }
}
