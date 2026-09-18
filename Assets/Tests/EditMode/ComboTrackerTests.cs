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

        // ---- ShortenGrace（ビーム撃破：加算せず猶予だけ縮める・ADR-35）------------------------

        [Test]
        public void ShortenGrace_DoesNotChangeCount()
        {
            var c = new ComboTracker(2f);
            c.AddKill(0f);
            c.AddKill(0.5f);
            c.ShortenGrace(0.6f, 0.5f);
            Assert.That(c.Count, Is.EqualTo(2), "ビーム撃破はコンボに加算しない");
            Assert.That(c.MaxCount, Is.EqualTo(2));
        }

        [Test]
        public void ShortenGrace_ExpiresEarlierThanWindow()
        {
            var c = new ComboTracker(2f);
            c.AddKill(0f);           // 本来は 2.0 まで継続
            c.ShortenGrace(0.5f, 0.5f); // → 1.0 で切れる

            Assert.That(c.Tick(1.0f), Is.False, "縮めた猶予ちょうどは継続（境界は > で切れる）");
            Assert.That(c.Count, Is.EqualTo(1));
            Assert.That(c.Tick(1.1f), Is.True, "縮めた猶予を超えた最初の Tick で切れる");
            Assert.That(c.Count, Is.EqualTo(0));
        }

        [Test]
        public void ShortenGrace_NeverExtends()
        {
            var c = new ComboTracker(2f);
            c.AddKill(0f);              // 2.0 まで
            c.ShortenGrace(0.5f, 5f);   // 5.5 に「延長」は起きない
            Assert.That(c.Tick(2.5f), Is.True, "元の猶予 2.0 のまま切れる");
        }

        [Test]
        public void ShortenGrace_RepeatedCalls_KeepEarliest()
        {
            // ビームは 1 体ずつ通知されるので同一フレームに何度も呼ばれる。最初に縮めた値が保たれる。
            var c = new ComboTracker(2f);
            c.AddKill(0f);
            c.ShortenGrace(0.5f, 0.5f); // 1.0
            c.ShortenGrace(0.5f, 0.5f);
            c.ShortenGrace(0.5f, 0.5f);
            Assert.That(c.Tick(1.1f), Is.True);
        }

        [Test]
        public void KillWithinShortenedGrace_ContinuesAndRestoresWindow()
        {
            var c = new ComboTracker(2f);
            c.AddKill(0f);
            c.ShortenGrace(0.5f, 0.5f); // 1.0 で切れる予定
            c.AddKill(0.9f);            // 猶予内に雪崩で撃破 → 継続、猶予は 0.9+2.0=2.9 に戻る
            Assert.That(c.Count, Is.EqualTo(2));
            Assert.That(c.Tick(2.5f), Is.False, "猶予が通常の窓に戻っている");
            Assert.That(c.Tick(3.0f), Is.True);
        }

        [Test]
        public void KillAfterShortenedGrace_RestartsAtOne()
        {
            var c = new ComboTracker(2f);
            c.AddKill(0f);
            c.AddKill(0.2f);
            c.ShortenGrace(0.5f, 0.5f); // 1.0 で切れる
            c.AddKill(1.5f);            // 本来の窓（2.2）内だが、縮めた猶予は過ぎている
            Assert.That(c.Count, Is.EqualTo(1));
            Assert.That(c.MaxCount, Is.EqualTo(2));
        }

        [Test]
        public void ShortenGrace_NegativeGrace_ClampsToZero()
        {
            var c = new ComboTracker(2f);
            c.AddKill(0f);
            c.ShortenGrace(0.5f, -1f); // 0.5 で切れる（負は 0 に丸める＝即時）
            Assert.That(c.Tick(0.5f), Is.False, "ちょうどは継続");
            Assert.That(c.Tick(0.51f), Is.True);
        }

        [Test]
        public void ShortenGrace_WhenNoCombo_IsNoOp()
        {
            var c = new ComboTracker(2f);
            c.ShortenGrace(0f, 0.5f);
            Assert.That(c.Count, Is.EqualTo(0));
            Assert.That(c.Tick(1f), Is.False, "0 のままで再通知しない");
            c.AddKill(0.1f);
            Assert.That(c.Count, Is.EqualTo(1), "次の撃破は普通に 1 から");
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
