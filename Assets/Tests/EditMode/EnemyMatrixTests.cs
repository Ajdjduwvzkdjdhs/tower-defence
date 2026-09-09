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

        // ---- AddRow / RemoveRowAt（Phase2 拡張）----

        [Test]
        public void AddRow_CreatesNewIsolatedRowAtBack()
        {
            var m = new EnemyMatrix<int>(3);
            m.AddRow(99);
            Assert.That(m.RowCount, Is.EqualTo(1));
            Assert.That(m.FrontRow, Is.EqualTo(new[] { 99 }));
        }

        [Test]
        public void AddRow_AddToBackAfterward_DoesNotFillIsolatedRow()
        {
            var m = new EnemyMatrix<int>(3);
            m.AddRow(99);        // 隔離行（最奥）
            m.AddToBack(1);     // 新しい行が作られるはず
            Assert.That(m.RowCount, Is.EqualTo(2), "隔離行と敵行で 2 行");
            Assert.That(m.RowAt(0), Is.EqualTo(new[] { 99 }), "先頭＝隔離行はそのまま");
            Assert.That(m.RowAt(1), Is.EqualTo(new[] { 1 }), "後続は新しい行");
        }

        [Test]
        public void AddRow_MultipleAllies_EachGetsOwnIsolatedRow()
        {
            var m = new EnemyMatrix<int>(3);
            m.AddRow(10);
            m.AddRow(20);
            Assert.That(m.RowCount, Is.EqualTo(2));
            Assert.That(m.RowAt(0), Is.EqualTo(new[] { 10 }));
            Assert.That(m.RowAt(1), Is.EqualTo(new[] { 20 }));
        }

        [Test]
        public void AddRow_AfterEnemyRow_IsolatedRowIsBack()
        {
            // 敵行（3体）→ 隔離行（味方）の順。AddToBack は隔離行をスキップして新しい行に積む。
            var m = new EnemyMatrix<int>(3);
            m.AddToBack(1); m.AddToBack(2); m.AddToBack(3); // 行0 満杯
            m.AddRow(99);                                     // 行1 隔離（味方）
            m.AddToBack(4);                                   // 行2 新しい敵行
            Assert.That(m.RowCount, Is.EqualTo(3));
            Assert.That(m.RowAt(1), Is.EqualTo(new[] { 99 }), "行1 は隔離行のまま");
            Assert.That(m.RowAt(2), Is.EqualTo(new[] { 4 }), "行2 は新しい敵行");
        }

        [Test]
        public void RemoveRowAt_RemovesSpecifiedRow_SubsequentShiftUp()
        {
            var m = new EnemyMatrix<int>(3);
            m.AddToBack(1); m.AddToBack(2); m.AddToBack(3); // 行0
            m.AddRow(99);                                     // 行1 隔離
            m.AddToBack(4);                                   // 行2
            var removed = m.RemoveRowAt(1);
            Assert.That(removed, Is.EqualTo(new[] { 99 }));
            Assert.That(m.RowCount, Is.EqualTo(2));
            Assert.That(m.RowAt(0), Is.EqualTo(new[] { 1, 2, 3 }));
            Assert.That(m.RowAt(1), Is.EqualTo(new[] { 4 }), "奥の行が繰り上がる");
        }

        [Test]
        public void RemoveRowAt_OutOfRange_ReturnsEmpty_NoChange()
        {
            var m = new EnemyMatrix<int>(3);
            m.AddToBack(1);
            var r1 = m.RemoveRowAt(-1);
            var r2 = m.RemoveRowAt(5);
            Assert.That(r1, Is.Empty);
            Assert.That(r2, Is.Empty);
            Assert.That(m.RowCount, Is.EqualTo(1), "マトリックスは変化しない");
        }

        [Test]
        public void RemoveRowAt_IsolatedRow_AfterRemoval_AddToBackFillsNewBack()
        {
            // 隔離行を除去した後、AddToBack が通常の行に積めるようになることを確認。
            var m = new EnemyMatrix<int>(3);
            m.AddRow(99);    // 行0 隔離
            m.RemoveRowAt(0);
            m.AddToBack(1);  // 隔離行が消えた後は通常どおり
            m.AddToBack(2);
            Assert.That(m.RowCount, Is.EqualTo(1), "1 行に 2 体が積まれる");
            Assert.That(m.FrontRow, Is.EqualTo(new[] { 1, 2 }));
        }

        [Test]
        public void GuardRow_AvalancheScenario_FrontEnemiesRemovedThenGuardIsNext()
        {
            // 手前敵行 → ガード行（味方）→ 奥敵行 を組み、
            // RemoveFrontRow で手前が消え、次の先頭がガード行になる（止められる）。
            var m = new EnemyMatrix<int>(3);
            m.AddToBack(1); m.AddToBack(2); m.AddToBack(3); // 行0 手前敵
            m.AddRow(99);                                     // 行1 ガード（味方）
            m.AddToBack(4);                                   // 行2 奥敵
            m.RemoveFrontRow(); // 手前敵行を雪崩で消す
            Assert.That(m.RowAt(0), Is.EqualTo(new[] { 99 }), "次の先頭はガード行");
        }

        [Test]
        public void GuardRow_AfterAllyExpires_BackEnemyShiftsForward()
        {
            // ガード行（味方）が RemoveRowAt で自走退場 → 奥の敵が先頭側へ繰り上がる。
            var m = new EnemyMatrix<int>(3);
            m.AddRow(99);    // 行0 ガード（味方）
            m.AddToBack(4);  // 行1 奥敵
            m.RemoveRowAt(0);
            Assert.That(m.RowCount, Is.EqualTo(1));
            Assert.That(m.FrontRow, Is.EqualTo(new[] { 4 }), "奥の敵が先頭へ繰り上がる");
        }

        //--- Remove(item): 雪玉が半径内の敵を 1 体だけ撃破する（phase-preview-feedback §3.4）---------

        [Test]
        public void Remove_MiddleOfRow_LeavesPartialRow_CountDrops()
        {
            var m = new EnemyMatrix<int>(3);
            m.AddToBack(1); m.AddToBack(2); m.AddToBack(3); // 行0 = [1,2,3]
            Assert.That(m.Remove(2), Is.True);
            Assert.That(m.RowCount, Is.EqualTo(1));
            Assert.That(m.Count, Is.EqualTo(2));
            Assert.That(m.RowAt(0), Is.EqualTo(new[] { 1, 3 }));
        }

        [Test]
        public void Remove_LastOfRow_DropsRow_SubsequentShiftUp()
        {
            var m = new EnemyMatrix<int>(3);
            m.AddRow(1);                    // 行0 = [1]（隔離行にして 1 体だけの行を作る）
            m.AddToBack(2); m.AddToBack(3); m.AddToBack(4); // 行1 = [2,3,4]
            Assert.That(m.Remove(1), Is.True);
            Assert.That(m.RowCount, Is.EqualTo(1));
            Assert.That(m.FrontRow, Is.EqualTo(new[] { 2, 3, 4 }), "行が消えて後続が繰り上がる");
        }

        [Test]
        public void Remove_NotPresent_ReturnsFalse_NoChange()
        {
            var m = new EnemyMatrix<int>(3);
            m.AddToBack(1); m.AddToBack(2);
            Assert.That(m.Remove(9), Is.False);
            Assert.That(m.Count, Is.EqualTo(2));
        }

        [Test]
        public void Remove_IsolatedRowMember_DropsRow_AndClearsIsolationFlag()
        {
            var m = new EnemyMatrix<int>(3);
            m.AddRow(99);        // 行0 = 隔離行（味方）
            m.AddToBack(1);      // 行1
            Assert.That(m.Remove(99), Is.True);
            Assert.That(m.RowCount, Is.EqualTo(1));
            // 隔離フラグが解除されているので、AddToBack が先頭行を埋められる
            m.AddToBack(2); m.AddToBack(3);
            Assert.That(m.FrontRow, Is.EqualTo(new[] { 1, 2, 3 }));
        }
    }
}
