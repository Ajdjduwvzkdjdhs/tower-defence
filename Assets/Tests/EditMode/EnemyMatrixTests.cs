using NUnit.Framework;
using KakigoriTD.Core;

namespace KakigoriTD.Tests.EditMode
{
    public class EnemyMatrixTests
    {
        [Test]
        public void IsEmpty_NewMatrix_IsTrue()
        {
            var m = new EnemyMatrix<int>(3);
            Assert.That(m.IsEmpty, Is.True);
            Assert.That(m.RowCount, Is.EqualTo(0));
            Assert.That(m.Count, Is.EqualTo(0));
        }

        [Test]
        public void FrontRow_NewMatrix_IsEmpty()
        {
            var m = new EnemyMatrix<int>(3);
            Assert.That(m.FrontRow.Count, Is.EqualTo(0));
        }

        [Test]
        public void AddToBack_FirstItem_CreatesFrontRow()
        {
            var m = new EnemyMatrix<int>(3);
            m.AddToBack(1);
            Assert.That(m.RowCount, Is.EqualTo(1));
            Assert.That(m.FrontRow.Count, Is.EqualTo(1));
            Assert.That(m.FrontRow[0], Is.EqualTo(1));
        }

        [Test]
        public void AddToBack_FillsBackRowUpToRowWidth()
        {
            var m = new EnemyMatrix<int>(3);
            m.AddToBack(1);
            m.AddToBack(2);
            m.AddToBack(3);
            Assert.That(m.RowCount, Is.EqualTo(1), "3 体までは 1 行に収まる");
            Assert.That(m.FrontRow.Count, Is.EqualTo(3));
        }

        [Test]
        public void AddToBack_FourthItem_StartsNewBackRow()
        {
            var m = new EnemyMatrix<int>(3);
            for (int i = 0; i < 4; i++) m.AddToBack(i);
            Assert.That(m.RowCount, Is.EqualTo(2), "4 体目で新しい最奥行ができる");
            Assert.That(m.FrontRow.Count, Is.EqualTo(3), "先頭行は満杯のまま");
        }

        [Test]
        public void RemoveFrontRow_RemovesAndReturnsFrontRow()
        {
            var m = new EnemyMatrix<int>(3);
            m.AddToBack(10);
            m.AddToBack(11);
            var removed = m.RemoveFrontRow();
            Assert.That(removed.Count, Is.EqualTo(2));
            Assert.That(removed[0], Is.EqualTo(10));
            Assert.That(removed[1], Is.EqualTo(11));
            Assert.That(m.IsEmpty, Is.True);
        }

        [Test]
        public void RemoveFrontRow_PromotesNextRowToFront()
        {
            var m = new EnemyMatrix<int>(3);
            // row0: 0,1,2  row1: 3
            for (int i = 0; i < 4; i++) m.AddToBack(i);
            m.RemoveFrontRow();
            Assert.That(m.RowCount, Is.EqualTo(1));
            Assert.That(m.FrontRow.Count, Is.EqualTo(1));
            Assert.That(m.FrontRow[0], Is.EqualTo(3), "奥の行が先頭に繰り上がる（深さ制御）");
        }

        [Test]
        public void RemoveFrontRow_WhenEmpty_ReturnsEmpty()
        {
            var m = new EnemyMatrix<int>(3);
            var removed = m.RemoveFrontRow();
            Assert.That(removed.Count, Is.EqualTo(0));
            Assert.That(m.IsEmpty, Is.True);
        }

        [Test]
        public void IsEmpty_AfterRemovingAllRows_IsTrue()
        {
            var m = new EnemyMatrix<int>(3);
            m.AddToBack(1);
            m.RemoveFrontRow();
            Assert.That(m.IsEmpty, Is.True);
        }

        [Test]
        public void RemoveAll_ReturnsAllItems_AndEmptiesMatrix()
        {
            var m = new EnemyMatrix<int>(3);
            for (int i = 0; i < 5; i++) m.AddToBack(i);
            var all = m.RemoveAll();
            Assert.That(all.Count, Is.EqualTo(5));
            Assert.That(m.IsEmpty, Is.True);
            Assert.That(m.Count, Is.EqualTo(0));
        }

        [Test]
        public void Count_ReflectsTotalItems()
        {
            var m = new EnemyMatrix<int>(3);
            for (int i = 0; i < 7; i++) m.AddToBack(i);
            Assert.That(m.Count, Is.EqualTo(7));
            Assert.That(m.RowCount, Is.EqualTo(3), "7 体 = 3+3+1 で 3 行");
        }

        [Test]
        public void RowWidth_BelowOne_ClampsToOne()
        {
            var m = new EnemyMatrix<int>(0);
            Assert.That(m.RowWidth, Is.EqualTo(1));
            m.AddToBack(1);
            m.AddToBack(2);
            Assert.That(m.RowCount, Is.EqualTo(2), "行幅 1 なら 1 体ごとに改行");
        }
    }
}
