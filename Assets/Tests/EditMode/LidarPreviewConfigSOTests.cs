using KakigoriTD.Data;
using NUnit.Framework;
using UnityEngine;

namespace KakigoriTD.Tests.EditMode
{
    /// <summary>
    /// EditMode tests for <see cref="LidarPreviewConfigSO"/>'s d-linked curves
    /// (phase-preview-feedback.md §3.4): cooldown 0.6→0.0 and spread 25→3 over d in [0, radiusMm],
    /// with the tolerance band clamped to the edge value.
    /// </summary>
    public class LidarPreviewConfigSOTests
    {
        private LidarPreviewConfigSO _cfg;

        [SetUp]
        public void SetUp() => _cfg = ScriptableObject.CreateInstance<LidarPreviewConfigSO>();

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_cfg);

        [Test]
        public void Cooldown_Center_Is06_Edge_Is0_Mid_Is03()
        {
            Assert.That(_cfg.CooldownSecFor(0f), Is.EqualTo(0.6f).Within(1e-4f));
            Assert.That(_cfg.CooldownSecFor(150f), Is.EqualTo(0.3f).Within(1e-4f));
            Assert.That(_cfg.CooldownSecFor(300f), Is.EqualTo(0f).Within(1e-4f));
        }

        [Test]
        public void Cooldown_ToleranceBand_ClampsToEdge()
        {
            // d は Mapper で radiusMm にクランプ済みだが、念のため config 側でも Clamp01。
            Assert.That(_cfg.CooldownSecFor(400f), Is.EqualTo(0f).Within(1e-4f));
        }

        [Test]
        public void Spread_Center_Is25_Edge_Is3()
        {
            Assert.That(_cfg.SpreadDegFor(0f), Is.EqualTo(25f).Within(1e-4f));
            Assert.That(_cfg.SpreadDegFor(300f), Is.EqualTo(3f).Within(1e-4f));
            Assert.That(_cfg.SpreadDegFor(150f), Is.EqualTo(14f).Within(1e-4f)); // lerp midpoint
        }

        [Test]
        public void NormalizedDistance_ClampedZeroToOne()
        {
            Assert.That(_cfg.NormalizedDistance(-10f), Is.EqualTo(0f).Within(1e-4f));
            Assert.That(_cfg.NormalizedDistance(150f), Is.EqualTo(0.5f).Within(1e-4f));
            Assert.That(_cfg.NormalizedDistance(999f), Is.EqualTo(1f).Within(1e-4f));
        }

        [Test]
        public void ConsumeIce_DefaultsFalse_IceIsInfinite()
        {
            Assert.That(_cfg.ConsumeIce, Is.False);
        }
    }
}
