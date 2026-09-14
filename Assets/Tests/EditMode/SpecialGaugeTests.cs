using NUnit.Framework;
using KakigoriTD.Core;

namespace KakigoriTD.Tests.EditMode
{
    /// <summary>
    /// 必殺ビーム砲のゲージ（ADR-30）。撃破数がメイン・コンボがボーナス。
    /// </summary>
    public class SpecialGaugeTests
    {
        [Test]
        public void AddKill_NoComboBonus_AddsPerKill()
        {
            var g = new SpecialGauge(max: 10f, perKill: 1f, comboBonus: 0f);
            g.AddKill(1);
            g.AddKill(50); // bonus 0 なのでコンボは効かない
            Assert.That(g.Value, Is.EqualTo(2f).Within(1e-4f));
        }

        [Test]
        public void AddKill_ComboIsBonus()
        {
            // combo=1 は素の 1.0 倍、combo=3 は 1 + 0.5*(3-1) = 2.0 倍。
            var g = new SpecialGauge(max: 100f, perKill: 1f, comboBonus: 0.5f);
            g.AddKill(1);
            Assert.That(g.Value, Is.EqualTo(1f).Within(1e-4f));
            g.AddKill(3);
            Assert.That(g.Value, Is.EqualTo(3f).Within(1e-4f));
        }

        [Test]
        public void Value_ClampsToMax()
        {
            var g = new SpecialGauge(max: 2f, perKill: 1.5f, comboBonus: 0f);
            g.AddKill(1);
            g.AddKill(1);
            g.AddKill(1);
            Assert.That(g.Value, Is.EqualTo(2f).Within(1e-4f));
            Assert.That(g.Normalized, Is.EqualTo(1f).Within(1e-4f));
        }

        [Test]
        public void IsFull_OnlyAtMax()
        {
            var g = new SpecialGauge(max: 2f, perKill: 1f, comboBonus: 0f);
            g.AddKill(1);
            Assert.That(g.IsFull, Is.False);
            g.AddKill(1);
            Assert.That(g.IsFull, Is.True);
        }

        [Test]
        public void Consume_ResetsToZero_AndCountsFire()
        {
            var g = new SpecialGauge(max: 1f, perKill: 1f, comboBonus: 0f);
            g.AddKill(1);

            Assert.That(g.Consume(), Is.True);
            Assert.That(g.Value, Is.EqualTo(0f).Within(1e-4f));
            Assert.That(g.FireCount, Is.EqualTo(1));
            Assert.That(g.IsFull, Is.False);
        }

        [Test]
        public void Consume_WhenNotFull_DoesNothing()
        {
            var g = new SpecialGauge(max: 10f, perKill: 1f, comboBonus: 0f);
            g.AddKill(1);

            Assert.That(g.Consume(), Is.False);
            Assert.That(g.Value, Is.EqualTo(1f).Within(1e-4f));
            Assert.That(g.FireCount, Is.EqualTo(0));
        }

        [Test]
        public void ComboBelowOne_TreatedAsOne()
        {
            var g = new SpecialGauge(max: 100f, perKill: 2f, comboBonus: 1f);
            g.AddKill(0);
            Assert.That(g.Value, Is.EqualTo(2f).Within(1e-4f));
        }

        [Test]
        public void Reset_ClearsValueAndFireCount()
        {
            var g = new SpecialGauge(max: 1f, perKill: 1f, comboBonus: 0f);
            g.AddKill(1);
            g.Consume();
            g.Reset();
            Assert.That(g.Value, Is.EqualTo(0f).Within(1e-4f));
            Assert.That(g.FireCount, Is.EqualTo(0));
        }
    }
}
