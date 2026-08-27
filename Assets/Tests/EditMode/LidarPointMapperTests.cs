using KakigoriTD.Input;
using NUnit.Framework;
using UnityEngine;

namespace KakigoriTD.Tests.EditMode
{
    /// <summary>
    /// EditMode tests for <see cref="LidarPointMapper"/> (docs/phase-preview-feedback.md §3.2).
    /// Covers: center subtraction, distance clamp incl. tolerance band, scale, world rotation, flips.
    /// </summary>
    public class LidarPointMapperTests
    {
        private static readonly Vector2 Center = new Vector2(0f, 300f); // 山中心（270° 方向へ 300mm）

        [Test]
        public void PointAtCenter_MapsToOrigin_DistanceZero()
        {
            var r = LidarPointMapper.Map(Center, Center, 0f, false, false, 0.03333f, 300f);
            Assert.That(r.WorldOffsetXZ.magnitude, Is.EqualTo(0f).Within(1e-4f));
            Assert.That(r.DistanceMm, Is.EqualTo(0f).Within(1e-4f));
        }

        [Test]
        public void Scale_AppliedToLocalOffset()
        {
            // 中心から +X に 150mm → local (150,0)
            var r = LidarPointMapper.Map(Center + new Vector2(150f, 0f), Center, 0f, false, false, 0.02f, 300f);
            Assert.That(r.WorldOffsetXZ.x, Is.EqualTo(3f).Within(1e-4f)); // 150 * 0.02
            Assert.That(r.WorldOffsetXZ.y, Is.EqualTo(0f).Within(1e-4f));
            Assert.That(r.DistanceMm, Is.EqualTo(150f).Within(1e-4f));
        }

        [Test]
        public void DefaultMapping_XtoX_YtoZ()
        {
            // local (0, +200) → world XZ (0, +200*scale)（y → Z）
            var r = LidarPointMapper.Map(Center + new Vector2(0f, 200f), Center, 0f, false, false, 0.01f, 300f);
            Assert.That(r.WorldOffsetXZ.x, Is.EqualTo(0f).Within(1e-4f));
            Assert.That(r.WorldOffsetXZ.y, Is.EqualTo(2f).Within(1e-4f));
        }

        [Test]
        public void DistanceClamp_ToleranceBand_ClampsToRadius()
        {
            // 中心から 380mm（radius 300 + 許容 100 の帯内）→ d は 300 にクランプ
            var r = LidarPointMapper.Map(Center + new Vector2(380f, 0f), Center, 0f, false, false, 0.01f, 300f);
            Assert.That(r.DistanceMm, Is.EqualTo(300f).Within(1e-4f));
            // 座標自体はクランプしない（円フィルタは Python 側。ここは d のみクランプ）
            Assert.That(r.WorldOffsetXZ.x, Is.EqualTo(3.8f).Within(1e-4f));
        }

        [Test]
        public void WorldRotation90_RotatesOffset()
        {
            // local (100, 0)、+90° 回転 → (0, 100)
            var r = LidarPointMapper.Map(Center + new Vector2(100f, 0f), Center, 90f, false, false, 1f, 300f);
            Assert.That(r.WorldOffsetXZ.x, Is.EqualTo(0f).Within(1e-3f));
            Assert.That(r.WorldOffsetXZ.y, Is.EqualTo(100f).Within(1e-3f));
            Assert.That(r.DistanceMm, Is.EqualTo(100f).Within(1e-3f)); // 回転は d 不変
        }

        [Test]
        public void FlipX_NegatesX_AfterRotation()
        {
            var r = LidarPointMapper.Map(Center + new Vector2(100f, 0f), Center, 0f, true, false, 1f, 300f);
            Assert.That(r.WorldOffsetXZ.x, Is.EqualTo(-100f).Within(1e-3f));
        }

        [Test]
        public void FlipY_NegatesY_AfterRotation()
        {
            var r = LidarPointMapper.Map(Center + new Vector2(0f, 120f), Center, 0f, false, true, 1f, 300f);
            Assert.That(r.WorldOffsetXZ.y, Is.EqualTo(-120f).Within(1e-3f));
        }

        [Test]
        public void FieldOffset_ShiftsCenter()
        {
            // キャリブ field_offset (10, -5) を足した中心で引く
            Vector2 center = Center + new Vector2(10f, -5f);
            var r = LidarPointMapper.Map(center, center, 0f, false, false, 1f, 300f);
            Assert.That(r.DistanceMm, Is.EqualTo(0f).Within(1e-4f));
        }

        [Test]
        public void ZeroRadius_DistanceIsZero_NoDivideByZeroLater()
        {
            var r = LidarPointMapper.Map(Center + new Vector2(50f, 0f), Center, 0f, false, false, 1f, 0f);
            Assert.That(r.DistanceMm, Is.EqualTo(0f).Within(1e-4f));
        }
    }
}
