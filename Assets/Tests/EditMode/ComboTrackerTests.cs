using NUnit.Framework;
using KakigoriTD.Core;

namespace KakigoriTD.Tests.EditMode
{
    /// <summary>
    /// コンボ（一定時間内の連続撃破）の検証（ADR-30）。時刻は外部注入で決定的にする。
    /// </summary>
    public class ComboTrackerTests
    {
        [Test]
        public void FirstKill_StartsAtOne()
        {
            var c = new ComboTracker(2f);
            c.AddKill(0f);
            Assert.That(c.Count, Is.EqualTo(1));
        }

        [Test]
        public void KillsWithinWindow_Accumulate()
        {
            var c = new ComboTracker(2f);
            c.AddKill(0f);
            c.AddKill(1.5f);
            c.AddKill(3.0f);
            Assert.That(c.Count, Is.EqualTo(3));
        }

        [Test]
        public void KillAfterWindow_RestartsAtOne()
        {
            var c = new ComboTracker(2f);
            c.AddKill(0f);
            c.AddKill(2.5f); // 猶予 2.0 を超えた
            Assert.That(c.Count, Is.EqualTo(1));
        }

        [Test]
        public void ExactlyAtWindow_StillContinues()
        {
            // 境界は「超えたら切れる」（> window）。ちょうどは継続。
            var c = new ComboTracker(2f);
            c.AddKill(0f);
            c.AddKill(2.0f);
            Assert.That(c.Count, Is.EqualTo(2));
        }

        [Test]
        public void Tick_AfterWindow_DropsToZero()
        {
            var c = new ComboTracker(2f);
            c.AddKill(0f);

            Assert.That(c.Tick(1.0f), Is.False, "猶予内は切れない");
            Assert.That(c.Count, Is.EqualTo(1));

            Assert.That(c.Tick(2.5f), Is.True, "猶予を超えた最初の Tick で切れる");
            Assert.That(c.Count, Is.EqualTo(0));
            Assert.That(c.Tick(3.0f), Is.False, "既に 0 なら再通知しない");
        }

        [Test]
        public void ChainKills_SameFrame_AllCount()
        {
            // 爆発の連鎖は同一フレームで 1 体ずつ AddKill される（20体連鎖＝+20）。
            var c = new ComboTracker(2f);
            for (int i = 0; i < 20; i++) c.AddKill(5f);
            Assert.That(c.Count, Is.EqualTo(20));
        }

        [Test]
        public void MaxCount_TracksPeak_AndSurvivesBreak()
        {
            var c = new ComboTracker(2f);
            c.AddKill(0f);
            c.AddKill(0.5f);
            c.AddKill(1.0f);   // ここが最大 3
            c.AddKill(10f);    // 途切れて 1 に戻る
            Assert.That(c.Count, Is.EqualTo(1));
            Assert.That(c.MaxCount, Is.EqualTo(3));
        }

        [Test]
        public void IsActive_OnlyFromTwo()
        {
            var c = new ComboTracker(2f);
            Assert.That(c.IsActive, Is.False);
            c.AddKill(0f);
            Assert.That(c.IsActive, Is.False, "1 体目はコンボ表示しない");
            c.AddKill(0.2f);
            Assert.That(c.IsActive, Is.True);
        }

        [Test]
        public void Reset_ClearsCountAndMax()
        {
            var c = new ComboTracker(2f);
            c.AddKill(0f);
            c.AddKill(0.5f);
            c.Reset();
            Assert.That(c.Count, Is.EqualTo(0));
            Assert.That(c.MaxCount, Is.EqualTo(0));

            c.AddKill(0.6f); // リセット直後は間隔が近くても 1 から
            Assert.That(c.Count, Is.EqualTo(1));
        }
    }
}
