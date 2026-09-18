using NUnit.Framework;
using KakigoriTD.Core;

namespace KakigoriTD.Tests.EditMode
{
    /// <summary>
    /// 結果の表示文（ADR-30: クリア＝クリアタイム / 失敗＝到達撃破数）。
    /// </summary>
    public class StageResultTests
    {
        [Test]
        public void FormatTime_UnderOneMinute_ShowsSecondsOnly()
        {
            Assert.That(StageResult.FormatTime(42.9f), Is.EqualTo("42秒"));
        }

        [Test]
        public void FormatTime_OverOneMinute_ShowsMinutesAndZeroPaddedSeconds()
        {
            Assert.That(StageResult.FormatTime(102f), Is.EqualTo("1分42秒"));
            Assert.That(StageResult.FormatTime(65f), Is.EqualTo("1分05秒"));
        }

        [Test]
        public void FormatTime_WithTenths_ShowsOneDecimal()
        {
            Assert.That(StageResult.FormatTime(42.96f, showTenths: true), Is.EqualTo("42.9秒")); // 切り捨て
            Assert.That(StageResult.FormatTime(102.34f, showTenths: true), Is.EqualTo("1分42.3秒"));
            Assert.That(StageResult.FormatTime(59.99f, showTenths: true), Is.EqualTo("59.9秒")); // 1分00.0秒 に繰り上がらない
            Assert.That(StageResult.FormatTime(-1f, showTenths: true), Is.EqualTo("0.0秒"));
        }

        [Test]
        public void FormatTime_Negative_ClampsToZero()
        {
            Assert.That(StageResult.FormatTime(-5f), Is.EqualTo("0秒"));
        }

        [Test]
        public void Headline_Win_UsesClearTime()
        {
            var r = new StageResult(true, 102f, 100, 100, 12);
            Assert.That(r.Headline, Is.EqualTo("1分42.0秒 でクリア！"));
        }

        [Test]
        public void Headline_Lose_UsesKillCount()
        {
            var r = new StageResult(false, 55f, 62, 100, 8);
            Assert.That(r.Headline, Is.EqualTo("62 体で力尽きた"));
        }
    }
}
