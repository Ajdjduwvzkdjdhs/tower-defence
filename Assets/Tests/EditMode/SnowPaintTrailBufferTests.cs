using KakigoriTD.Gameplay;
using NUnit.Framework;
using UnityEngine;

namespace KakigoriTD.Tests.EditMode
{
    /// <summary>EditMode tests for <see cref="SnowPaintTrailBuffer"/>.</summary>
    public class SnowPaintTrailBufferTests
    {
        [Test]
        public void SectorIndexFor_SamePointsWithinSectorMapToSameIndex()
        {
            var buffer = new SnowPaintTrailBuffer(sectorCount: 24, sectorDeg: 15f, fadeSec: 2f);

            // 方位角0度〜14度は同じ扇形（距離が違っても同じ扇形になる）。
            int near = buffer.SectorIndexFor(new Vector3(1f, 0f, 0f));
            int far = buffer.SectorIndexFor(new Vector3(10f, 0f, 2f));

            Assert.AreEqual(0, near);
            Assert.AreEqual(near, far);
        }

        [Test]
        public void SectorIndexFor_DifferentAnglesMapToDifferentSectors()
        {
            var buffer = new SnowPaintTrailBuffer(sectorCount: 24, sectorDeg: 15f, fadeSec: 2f);

            int sector0 = buffer.SectorIndexFor(new Vector3(1f, 0f, 0f));   // 0度
            int sector1 = buffer.SectorIndexFor(new Vector3(0f, 0f, 1f));   // 90度 → 扇形6

            Assert.AreEqual(0, sector0);
            Assert.AreEqual(6, sector1);
        }

        [Test]
        public void MarkSector_RecordsTimeAndRadiusForCorrectSectorOnly()
        {
            var buffer = new SnowPaintTrailBuffer(sectorCount: 24, sectorDeg: 15f, fadeSec: 2f);
            buffer.MarkSector(new Vector3(3f, 0f, 0f), now: 5f); // 扇形0・半径3

            var dest = new Vector4[buffer.SectorCount];
            buffer.BuildUploadArray(dest);

            Assert.AreEqual(5f, dest[0].x);
            Assert.AreEqual(3f, dest[0].y);
            Assert.AreEqual(SnowPaintTrailBuffer.NeverPainted, dest[1].x);
        }

        [Test]
        public void MarkSector_WhileStillAlive_KeepsInnermostRadiusAsOrigin()
        {
            var buffer = new SnowPaintTrailBuffer(sectorCount: 24, sectorDeg: 15f, fadeSec: 2f);
            buffer.MarkSector(new Vector3(5f, 0f, 0f), now: 0f);  // 出現位置：半径5
            buffer.MarkSector(new Vector3(8f, 0f, 0.1f), now: 1f); // 同じ雪玉が外側へ滑走（生存中）

            var dest = new Vector4[buffer.SectorCount];
            buffer.BuildUploadArray(dest);

            // 雪玉が外側へ進んでも、積もった雪の内側の縁（起点半径）は後退しない＝5のまま。
            Assert.AreEqual(5f, dest[0].y, 0.01f);
            Assert.AreEqual(1f, dest[0].x);
        }

        [Test]
        public void MarkSector_AfterFullyFaded_StartsNewOriginAtLatestRadius()
        {
            var buffer = new SnowPaintTrailBuffer(sectorCount: 24, sectorDeg: 15f, fadeSec: 2f);
            buffer.MarkSector(new Vector3(2f, 0f, 0f), now: 0f);   // 1体目：半径2
            buffer.MarkSector(new Vector3(9f, 0f, 0f), now: 10f);  // fadeSec(2)を大きく超えて経過＝別の雪玉

            var dest = new Vector4[buffer.SectorCount];
            buffer.BuildUploadArray(dest);

            // 完全にフェードした後の新規出現なので、起点半径は新しい位置(9)にリセットされる。
            Assert.AreEqual(9f, dest[0].y, 0.01f);
            Assert.AreEqual(10f, dest[0].x);
        }

        [Test]
        public void BuildUploadArray_UnpaintedSectorsStayNeverPainted()
        {
            var buffer = new SnowPaintTrailBuffer(sectorCount: 24, sectorDeg: 15f, fadeSec: 2f);
            var dest = new Vector4[buffer.SectorCount];
            buffer.BuildUploadArray(dest);

            for (int i = 0; i < dest.Length; i++)
                Assert.AreEqual(SnowPaintTrailBuffer.NeverPainted, dest[i].x);
        }
    }
}
