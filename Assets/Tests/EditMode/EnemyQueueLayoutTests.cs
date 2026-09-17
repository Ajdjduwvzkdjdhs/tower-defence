using System.Collections.Generic;
using KakigoriTD.Gameplay;
using NUnit.Framework;
using UnityEngine;

namespace KakigoriTD.Tests.EditMode
{
    /// <summary>
    /// EditMode tests for <see cref="EnemyQueueLayout"/>（敵が重なって表示される不具合の修正）。
    /// Covers: 最前列から埋まる / 同じ方位は背後の段へ / 満段なら隣の列へ / 全列満段なら false /
    /// 撃破で空いた段の再利用 / 列の逆写像（ColumnOf）/ 隣の列は互いのスロットを塞がない。
    /// </summary>
    public class EnemyQueueLayoutTests
    {
        private const int Steps = 24;      // 15° 刻み
        private const float Rest = 8f;
        private const float Spacing = 1f;
        private const int Depth = 3;

        private static bool Alloc(IReadOnlyList<Vector3> occupied, int preferred, out int column, out int slot,
            int depth = Depth, int steps = Steps)
            => EnemyQueueLayout.TryAllocate(occupied, steps, preferred, Rest, Spacing, depth, out column, out slot);

        private static Vector3 Slot(int column, int slot, int steps = Steps)
            => EnemyQueueLayout.SlotPosition(column, slot, steps, Rest, Spacing);

        [Test]
        public void EmptyField_TakesFrontSlotOfPreferredColumn()
        {
            Assert.That(Alloc(new List<Vector3>(), preferred: 5, out int column, out int slot), Is.True);
            Assert.That(column, Is.EqualTo(5));
            Assert.That(slot, Is.EqualTo(0));
            Assert.That(EnemyQueueLayout.SlotRadius(slot, Rest, Spacing), Is.EqualTo(Rest).Within(1e-4f));
        }

        [Test]
        public void SameColumn_SecondEnemyGoesBehind_NotOnTop()
        {
            // これが本題: 同じ方位に湧いた 2 体目は 1 段外（背後）に並び、重ならない。
            var occupied = new List<Vector3> { Slot(5, 0) };

            Assert.That(Alloc(occupied, preferred: 5, out int column, out int slot), Is.True);
            Assert.That(column, Is.EqualTo(5));
            Assert.That(slot, Is.EqualTo(1));

            float d = Vector3.Distance(Slot(column, slot), occupied[0]);
            Assert.That(d, Is.EqualTo(Spacing).Within(1e-3f), "前の敵と段間隔ぶん離れていること");
        }

        [Test]
        public void ThirdEnemy_StacksOutward()
        {
            var occupied = new List<Vector3> { Slot(5, 0), Slot(5, 1) };
            Assert.That(Alloc(occupied, preferred: 5, out int column, out int slot), Is.True);
            Assert.That(column, Is.EqualTo(5));
            Assert.That(slot, Is.EqualTo(2));
        }

        [Test]
        public void FullColumn_SpillsToNeighbourColumn_AtFrontSlot()
        {
            var occupied = new List<Vector3> { Slot(5, 0), Slot(5, 1), Slot(5, 2) };
            Assert.That(Alloc(occupied, preferred: 5, out int column, out int slot), Is.True);
            Assert.That(column, Is.EqualTo(6).Or.EqualTo(4), "満段なら隣の方位へ逃がす");
            Assert.That(slot, Is.EqualTo(0));
        }

        [Test]
        public void AllColumnsFull_ReturnsFalse()
        {
            var occupied = new List<Vector3>();
            for (int c = 0; c < Steps; c++)
                for (int s = 0; s < Depth; s++)
                    occupied.Add(Slot(c, s));

            Assert.That(Alloc(occupied, preferred: 0, out _, out _), Is.False, "重ねるのではなくスポーンを見送る");
        }

        [Test]
        public void FrontSlotFreedByKill_IsReused()
        {
            // 最前列が倒されて後段がまだ繰り上がっていない（= slot1 のみ占有）状況。
            var occupied = new List<Vector3> { Slot(5, 1) };
            Assert.That(Alloc(occupied, preferred: 5, out int column, out int slot), Is.True);
            Assert.That(column, Is.EqualTo(5));
            Assert.That(slot, Is.EqualTo(0));
        }

        [Test]
        public void NeighbourColumns_DoNotBlockEachOther()
        {
            // 隣の方位（15°）は最前列でも 2 units 以上離れるので、互いのスロットを塞がない。
            var occupied = new List<Vector3> { Slot(4, 0), Slot(6, 0) };
            Assert.That(Alloc(occupied, preferred: 5, out int column, out int slot), Is.True);
            Assert.That(column, Is.EqualTo(5));
            Assert.That(slot, Is.EqualTo(0));
        }

        [Test]
        public void ColumnOf_RoundTripsEverySlot()
        {
            for (int c = 0; c < Steps; c++)
                for (int s = 0; s < Depth; s++)
                    Assert.That(EnemyQueueLayout.ColumnOf(Slot(c, s), Steps), Is.EqualTo(c), $"column {c} slot {s}");
        }

        [Test]
        public void NormalizeColumn_WrapsBothDirections()
        {
            Assert.That(EnemyQueueLayout.NormalizeColumn(-1, Steps), Is.EqualTo(Steps - 1));
            Assert.That(EnemyQueueLayout.NormalizeColumn(Steps, Steps), Is.EqualTo(0));
            Assert.That(EnemyQueueLayout.NormalizeColumn(Steps + 3, Steps), Is.EqualTo(3));
        }

        [Test]
        public void Direction_IsUnitLength_AndSlotRadiusGrowsBySpacing()
        {
            for (int c = 0; c < Steps; c++)
                Assert.That(EnemyQueueLayout.Direction(c, Steps).magnitude, Is.EqualTo(1f).Within(1e-4f));

            Assert.That(EnemyQueueLayout.SlotRadius(2, Rest, Spacing), Is.EqualTo(Rest + 2f * Spacing).Within(1e-4f));
        }

        [Test]
        public void DepthOne_NeverStacks()
        {
            // 現場で「絶対に重ねない」運用にしたときの挙動: 1 方位 1 体。
            var occupied = new List<Vector3> { Slot(5, 0) };
            Assert.That(Alloc(occupied, preferred: 5, out int column, out int slot, depth: 1), Is.True);
            Assert.That(column, Is.Not.EqualTo(5));
            Assert.That(slot, Is.EqualTo(0));
        }

        // ---- ペア確保（爆発兵＋盾兵を同じ列に前後で置く・ADR-30） ----

        [Test]
        public void PairAlloc_EmptyField_TakesPreferredColumn()
        {
            bool ok = EnemyQueueLayout.TryAllocateColumnWithFreeDepth(new List<Vector3>(), Steps, 5, Rest, Spacing, 2, 2, out int column);
            Assert.That(ok, Is.True);
            Assert.That(column, Is.EqualTo(5));
        }

        [Test]
        public void PairAlloc_ColumnWithFrontOccupied_IsSkipped()
        {
            // 希望列の 1 段目に誰かいる → 2 段とも空いている隣の列へ逃がす
            var occupied = new List<Vector3> { Slot(5, 0) };
            bool ok = EnemyQueueLayout.TryAllocateColumnWithFreeDepth(occupied, Steps, 5, Rest, Spacing, 2, 2, out int column);
            Assert.That(ok, Is.True);
            Assert.That(column, Is.Not.EqualTo(5));
            Assert.That(Mathf.Abs(column - 5), Is.EqualTo(1), "隣の列へ逃がす");
        }

        [Test]
        public void PairAlloc_ColumnWithOnlyRearOccupied_IsSkipped()
        {
            // 2 段目だけ埋まっていてもペアは置けない（前後で並べたいので両方空きが必要）
            var occupied = new List<Vector3> { Slot(5, 1) };
            bool ok = EnemyQueueLayout.TryAllocateColumnWithFreeDepth(occupied, Steps, 5, Rest, Spacing, 2, 2, out int column);
            Assert.That(ok, Is.True);
            Assert.That(column, Is.Not.EqualTo(5));
        }

        [Test]
        public void PairAlloc_DepthExceedsMaxDepth_ReturnsFalse()
        {
            bool ok = EnemyQueueLayout.TryAllocateColumnWithFreeDepth(new List<Vector3>(), Steps, 5, Rest, Spacing, maxDepth: 1, depth: 2, out _);
            Assert.That(ok, Is.False);
        }

        [Test]
        public void PairAlloc_EveryColumnHasFrontEnemy_ReturnsFalse()
        {
            var occupied = new List<Vector3>();
            for (int c = 0; c < Steps; c++) occupied.Add(Slot(c, 0));
            bool ok = EnemyQueueLayout.TryAllocateColumnWithFreeDepth(occupied, Steps, 3, Rest, Spacing, 2, 2, out _);
            Assert.That(ok, Is.False, "全列の先頭が埋まっていればペアはどこにも置けない");
        }
    }
}
