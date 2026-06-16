using NUnit.Framework;
using KakigoriTD.Core;

namespace KakigoriTD.Tests.EditMode
{
    public class PenaltyTallyTests
    {
        [Test]
        public void Total_Initially_IsZero()
        {
            var t = new PenaltyTally();
            Assert.That(t.Total, Is.EqualTo(0));
        }

        [Test]
        public void Add_SingleCall_AccumulatesPoints()
        {
            var t = new PenaltyTally();
            t.Add(50);
            Assert.That(t.Total, Is.EqualTo(50));
        }

        [Test]
        public void Add_MultipleCalls_SumsAll()
        {
            var t = new PenaltyTally();
            t.Add(50);
            t.Add(50);
            t.Add(50);
            Assert.That(t.Total, Is.EqualTo(150));
        }

        [Test]
        public void Reset_AfterAdds_ClearsToZero()
        {
            var t = new PenaltyTally();
            t.Add(200);
            t.Reset();
            Assert.That(t.Total, Is.EqualTo(0));
        }

        [Test]
        public void Add_ZeroOrNegative_NoChange()
        {
            var t = new PenaltyTally();
            t.Add(0);
            t.Add(-10);
            Assert.That(t.Total, Is.EqualTo(0));
        }

        [Test]
        public void Reset_ThenAdd_AccumulatesFromZero()
        {
            var t = new PenaltyTally();
            t.Add(100);
            t.Reset();
            t.Add(30);
            Assert.That(t.Total, Is.EqualTo(30));
        }
    }
}
