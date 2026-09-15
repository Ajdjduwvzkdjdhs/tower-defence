using NUnit.Framework;
using KakigoriTD.Core;

namespace KakigoriTD.Tests.EditMode
{
    public class KillTallyTests
    {
        [Test]
        public void Total_Initially_IsZero()
        {
            var t = new KillTally();
            Assert.That(t.Total, Is.EqualTo(0));
        }

        [Test]
        public void Add_SingleCall_AccumulatesCount()
        {
            var t = new KillTally();
            t.Add(1);
            Assert.That(t.Total, Is.EqualTo(1));
        }

        [Test]
        public void Add_MultipleCalls_SumsAll()
        {
            var t = new KillTally();
            for (int i = 0; i < 10; i++) t.Add(1);
            Assert.That(t.Total, Is.EqualTo(10));
        }

        [Test]
        public void Reset_AfterAdds_ClearsToZero()
        {
            var t = new KillTally();
            t.Add(1);
            t.Add(1);
            t.Reset();
            Assert.That(t.Total, Is.EqualTo(0));
        }

        [Test]
        public void Add_ZeroOrNegative_NoChange()
        {
            var t = new KillTally();
            t.Add(0);
            t.Add(-1);
            Assert.That(t.Total, Is.EqualTo(0));
        }

        [Test]
        public void Reset_ThenAdd_AccumulatesFromZero()
        {
            var t = new KillTally();
            t.Add(1);
            t.Reset();
            t.Add(1);
            Assert.That(t.Total, Is.EqualTo(1));
        }
    }
}
