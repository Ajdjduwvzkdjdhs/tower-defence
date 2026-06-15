using NUnit.Framework;
using KakigoriTD.Core;

namespace KakigoriTD.Tests.EditMode
{
    public class IceStockTests
    {
        private const float Eps = 1e-5f;

        [Test]
        public void Constructor_SetsCurrentToTotal()
        {
            var ice = new IceStock(50f);
            Assert.That(ice.Current, Is.EqualTo(50f).Within(Eps));
            Assert.That(ice.Total, Is.EqualTo(50f).Within(Eps));
        }

        [Test]
        public void Constructor_NegativeTotal_ClampsToZero()
        {
            var ice = new IceStock(-10f);
            Assert.That(ice.Total, Is.EqualTo(0f).Within(Eps));
            Assert.That(ice.IsEmpty, Is.True);
        }

        [Test]
        public void TryDrain_WithIce_ReducesCurrent()
        {
            var ice = new IceStock(50f);
            ice.TryDrain(2f);
            Assert.That(ice.Current, Is.EqualTo(48f).Within(Eps));
        }

        [Test]
        public void TryDrain_WithIce_ReturnsTrue()
        {
            var ice = new IceStock(50f);
            Assert.That(ice.TryDrain(1f), Is.True);
        }

        [Test]
        public void TryDrain_MoreThanRemaining_ClampsToZeroNotNegative()
        {
            var ice = new IceStock(1f);
            ice.TryDrain(5f);
            Assert.That(ice.Current, Is.EqualTo(0f).Within(Eps));
            Assert.That(ice.IsEmpty, Is.True);
        }

        [Test]
        public void TryDrain_WhenEmpty_ReturnsFalse()
        {
            var ice = new IceStock(1f);
            ice.TryDrain(1f); // drain to exactly 0
            Assert.That(ice.TryDrain(0.5f), Is.False);
        }

        [Test]
        public void TryDrain_WhenEmpty_DoesNotChangeCurrent()
        {
            var ice = new IceStock(1f);
            ice.TryDrain(1f);
            ice.TryDrain(0.5f);
            Assert.That(ice.Current, Is.EqualTo(0f).Within(Eps));
        }

        [Test]
        public void IsEmpty_NewStock_IsFalse()
        {
            var ice = new IceStock(50f);
            Assert.That(ice.IsEmpty, Is.False);
        }

        [Test]
        public void Normalized_HalfDrained_IsHalf()
        {
            var ice = new IceStock(10f);
            ice.TryDrain(5f);
            Assert.That(ice.Normalized, Is.EqualTo(0.5f).Within(Eps));
        }
    }
}
