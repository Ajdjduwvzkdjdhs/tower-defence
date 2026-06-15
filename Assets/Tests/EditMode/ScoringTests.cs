using NUnit.Framework;
using KakigoriTD.Core;

namespace KakigoriTD.Tests.EditMode
{
    public class ScoringTests
    {
        [Test]
        public void Compute_NoPenalty_ReturnsFortHp()
        {
            Assert.That(Scoring.Compute(720), Is.EqualTo(720));
        }

        [Test]
        public void Compute_ZeroHp_ReturnsZero()
        {
            Assert.That(Scoring.Compute(0), Is.EqualTo(0));
        }

        [Test]
        public void Compute_WithPenalty_SubtractsPenalty()
        {
            Assert.That(Scoring.Compute(500, 150), Is.EqualTo(350));
        }

        [Test]
        public void Compute_PenaltyExceedsHp_ClampsToZero()
        {
            Assert.That(Scoring.Compute(100, 250), Is.EqualTo(0));
        }
    }
}
