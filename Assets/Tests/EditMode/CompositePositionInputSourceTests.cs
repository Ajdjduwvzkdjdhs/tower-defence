using KakigoriTD.Input;
using NUnit.Framework;
using UnityEngine;

namespace KakigoriTD.Tests.EditMode
{
    /// <summary>
    /// EditMode tests for <see cref="CompositePositionInputSource"/> (LiDAR OR マウス疑似合成)。
    /// LiDAR に検知点があれば優先し、無いときだけマウス疑似入力へフォールバックすることを検証する。
    /// </summary>
    public class CompositePositionInputSourceTests
    {
        private sealed class StubPositionInput : IPositionInputSource
        {
            public bool HasPoint { get; set; }
            public Vector3 WorldPoint { get; set; }
            public float DistanceMm { get; set; }
            public bool IsShaving { get; set; }
            public bool IsLost { get; set; }
        }

        [Test]
        public void UsesLidar_WhenLidarHasPoint()
        {
            var lidar = new StubPositionInput { HasPoint = true, WorldPoint = new Vector3(1, 0, 2), DistanceMm = 50f, IsShaving = true };
            var sim = new StubPositionInput { HasPoint = true, WorldPoint = new Vector3(9, 0, 9), DistanceMm = 200f, IsShaving = false };
            var c = new CompositePositionInputSource(lidar, sim);

            Assert.That(c.HasPoint, Is.True);
            Assert.That(c.WorldPoint, Is.EqualTo(new Vector3(1, 0, 2)));
            Assert.That(c.DistanceMm, Is.EqualTo(50f));
            Assert.That(c.IsShaving, Is.True);
        }

        [Test]
        public void FallsBackToSim_WhenLidarHasNoPoint()
        {
            var lidar = new StubPositionInput { HasPoint = false };
            var sim = new StubPositionInput { HasPoint = true, WorldPoint = new Vector3(3, 0, 4), DistanceMm = 80f, IsShaving = true };
            var c = new CompositePositionInputSource(lidar, sim);

            Assert.That(c.HasPoint, Is.True);
            Assert.That(c.WorldPoint, Is.EqualTo(new Vector3(3, 0, 4)));
            Assert.That(c.DistanceMm, Is.EqualTo(80f));
            Assert.That(c.IsShaving, Is.True);
        }

        [Test]
        public void HasPoint_False_WhenNeitherHasPoint()
        {
            var lidar = new StubPositionInput { HasPoint = false };
            var sim = new StubPositionInput { HasPoint = false };
            var c = new CompositePositionInputSource(lidar, sim);

            Assert.That(c.HasPoint, Is.False);
        }

        [Test]
        public void IsLost_False_WhenLidarLostButFallbackActive()
        {
            // LiDAR 未接続（IsLost=true）でもマウスへフォールバックしている間は
            // 「ロスト＝操作不可」扱いにならないこと（LidarAvalancheVfx の !IsLost ゲート対策）。
            var lidar = new StubPositionInput { HasPoint = false, IsLost = true };
            var sim = new StubPositionInput { HasPoint = true, IsShaving = true };
            var c = new CompositePositionInputSource(lidar, sim);

            Assert.That(c.HasPoint, Is.True);
            Assert.That(c.IsShaving, Is.True);
            Assert.That(c.IsLost, Is.False);
        }

        [Test]
        public void IsLost_ReflectsLidar_WhenLidarActive()
        {
            var lidar = new StubPositionInput { HasPoint = true, IsLost = true }; // 通常はHasPoint時にIsLostにはならないが念のため
            var sim = new StubPositionInput { HasPoint = true };
            var c = new CompositePositionInputSource(lidar, sim);

            Assert.That(c.IsLost, Is.True);
        }

        [Test]
        public void NullFallback_UsesLidarOnly()
        {
            var lidar = new StubPositionInput { HasPoint = true, WorldPoint = Vector3.one };
            var c = new CompositePositionInputSource(lidar, null);

            Assert.That(c.HasPoint, Is.True);
            Assert.That(c.WorldPoint, Is.EqualTo(Vector3.one));
        }

        [Test]
        public void NullLidar_UsesFallbackOnly()
        {
            var sim = new StubPositionInput { HasPoint = true, WorldPoint = Vector3.one };
            var c = new CompositePositionInputSource(null, sim);

            Assert.That(c.HasPoint, Is.True);
            Assert.That(c.WorldPoint, Is.EqualTo(Vector3.one));
            Assert.That(c.IsLost, Is.False);
        }
    }
}
