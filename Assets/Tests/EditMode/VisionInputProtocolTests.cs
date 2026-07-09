using KakigoriTD.Input;
using NUnit.Framework;

namespace KakigoriTD.Tests.EditMode
{
    /// <summary>
    /// EditMode tests for <see cref="VisionInputProtocol"/> (Phase 5 / ADR-24).
    /// Covers: seq,channel parse, malformed-line rejection, seq-based lost watchdog (state Y) with
    /// fail-safe -1, live(A) vs latched(B) handling of -1, Python-restart recovery, and the debounce insurance.
    /// </summary>
    public class VisionInputProtocolTests
    {
        // 既定：ロスト0.3s・ライブ(A)・デバウンス無効。
        private static VisionInputProtocol Live(float lost = 0.3f, float debounce = 0f)
            => new VisionInputProtocol(lost, latch: false, debounceSeconds: debounce);

        private static VisionInputProtocol Latched(float lost = 0.3f, float debounce = 0f)
            => new VisionInputProtocol(lost, latch: true, debounceSeconds: debounce);

        [Test]
        public void BeforeAnyMessage_IsLost_AndChannelMinusOne()
        {
            var p = Live();
            Assert.That(p.IsLost, Is.True);
            Assert.That(p.ActiveChannel, Is.EqualTo(-1));
        }

        [Test]
        public void IngestChannel_LiveFollows()
        {
            var p = Live();
            p.Ingest("1,2");
            p.Tick(0f);
            Assert.That(p.IsLost, Is.False);
            Assert.That(p.ActiveChannel, Is.EqualTo(2));
        }

        [Test]
        public void Live_MinusOne_DeselectsImmediately()
        {
            var p = Live();
            p.Ingest("1,2"); p.Tick(0f);
            p.Ingest("2,-1"); p.Tick(0f);
            Assert.That(p.ActiveChannel, Is.EqualTo(-1)); // ライブは -1 で谷なしに戻る
        }

        [Test]
        public void Latched_MinusOne_HoldsLastValley()
        {
            var p = Latched();
            p.Ingest("1,2"); p.Tick(0f);
            p.Ingest("2,-1"); p.Tick(0f);
            Assert.That(p.ActiveChannel, Is.EqualTo(2)); // ラッチは直前の谷を保持
        }

        [Test]
        public void Latched_SwitchesOnNewValley()
        {
            var p = Latched();
            p.Ingest("1,2"); p.Tick(0f);
            p.Ingest("2,-1"); p.Tick(0f);
            p.Ingest("3,0"); p.Tick(0f);
            Assert.That(p.ActiveChannel, Is.EqualTo(0)); // 別の谷に入れば切替
        }

        [Test]
        public void MalformedLine_IsIgnored()
        {
            var p = Live();
            p.Ingest("1,2"); p.Tick(0f);
            p.Ingest("garbage"); p.Tick(0f);
            p.Ingest("5"); p.Tick(0f);      // カンマなし
            p.Ingest("6,9"); p.Tick(0f);    // 範囲外 channel
            Assert.That(p.ActiveChannel, Is.EqualTo(2)); // どれも状態を変えない
        }

        [Test]
        public void NullLine_IsIgnored()
        {
            var p = Live();
            Assert.DoesNotThrow(() => p.Ingest(null));
        }

        [Test]
        public void Watchdog_LostAfterSeqStops()
        {
            var p = Live(lost: 0.3f);
            p.Ingest("10,1"); p.Tick(0f);
            Assert.That(p.ActiveChannel, Is.EqualTo(1));

            // 同じ seq を再読み（Python 停止）→ 時間だけ進む
            p.Ingest("10,1"); p.Tick(0.2f);
            Assert.That(p.IsLost, Is.False);
            p.Ingest("10,1"); p.Tick(0.2f); // 累計 0.4 > 0.3
            Assert.That(p.IsLost, Is.True);
            Assert.That(p.ActiveChannel, Is.EqualTo(-1)); // フェイルセーフ
        }

        [Test]
        public void Watchdog_StaysAliveWhileSeqAdvances()
        {
            var p = Live(lost: 0.3f);
            p.Ingest("10,1"); p.Tick(0.2f);
            p.Ingest("11,1"); p.Tick(0.2f); // seq 変化で再武装 → まだロストしない
            Assert.That(p.IsLost, Is.False);
            Assert.That(p.ActiveChannel, Is.EqualTo(1));
        }

        [Test]
        public void Watchdog_RecoversWhenSeqResumes()
        {
            var p = Live(lost: 0.3f);
            p.Ingest("10,1"); p.Tick(0.4f); // ロスト
            Assert.That(p.IsLost, Is.True);

            p.Ingest("11,3"); p.Tick(0f);   // seq 前進で復帰
            Assert.That(p.IsLost, Is.False);
            Assert.That(p.ActiveChannel, Is.EqualTo(3));
        }

        [Test]
        public void PythonRestart_SeqDropsButTreatedAsFresh()
        {
            var p = Live(lost: 0.3f);
            p.Ingest("500,2"); p.Tick(0f);
            // 再起動で seq が 1 に戻る（500 より小さいが「変化」＝新鮮）
            p.Ingest("1,0"); p.Tick(0f);
            Assert.That(p.IsLost, Is.False);
            Assert.That(p.ActiveChannel, Is.EqualTo(0));
        }

        [Test]
        public void WatchdogDisabled_WhenTimeoutZero()
        {
            var p = new VisionInputProtocol(0f, latch: false, debounceSeconds: 0f);
            p.Ingest("1,2"); p.Tick(100f);
            Assert.That(p.IsLost, Is.False);
            Assert.That(p.ActiveChannel, Is.EqualTo(2));
        }

        [Test]
        public void Debounce_DelaysAdoptionUntilStable()
        {
            var p = Live(lost: 5f, debounce: 0.1f);
            p.Ingest("1,2");
            p.Tick(0.05f);                       // 候補 2、まだ 0.05 < 0.1
            Assert.That(p.ActiveChannel, Is.EqualTo(-1));
            p.Ingest("2,2");
            p.Tick(0.1f);                        // 累計安定 0.15 >= 0.1 → 採用
            Assert.That(p.ActiveChannel, Is.EqualTo(2));
        }

        [Test]
        public void Debounce_FlickerNeverAdoptsUnstableChannel()
        {
            var p = Live(lost: 5f, debounce: 0.1f);
            p.Ingest("1,2"); p.Tick(0.05f); // 2 を候補
            p.Ingest("2,3"); p.Tick(0.05f); // 3 に変わり待ち直し
            p.Ingest("3,2"); p.Tick(0.05f); // また 2、安定しない
            Assert.That(p.ActiveChannel, Is.EqualTo(-1)); // ちらつく間は採用しない
        }

        [Test]
        public void Debounce_Zero_AdoptsImmediately()
        {
            var p = Live(lost: 5f, debounce: 0f);
            p.Ingest("1,3"); p.Tick(0f);
            Assert.That(p.ActiveChannel, Is.EqualTo(3));
        }
    }
}
