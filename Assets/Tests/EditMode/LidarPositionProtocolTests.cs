using KakigoriTD.Input;
using NUnit.Framework;

namespace KakigoriTD.Tests.EditMode
{
    /// <summary>
    /// EditMode tests for <see cref="LidarPositionProtocol"/> (Phase preview-feedback / docs §3.1).
    /// Covers: seq,x,y,shaving parse, "no detection" (empty x,y), malformed-line rejection,
    /// seq-based lost watchdog with fail-safe (no point / shaving false), Python-restart recovery,
    /// same-seq re-read is a no-op, watchdog disabled when timeout &lt;= 0.
    /// </summary>
    public class LidarPositionProtocolTests
    {
        private static LidarPositionProtocol New(float lost = 0.3f) => new LidarPositionProtocol(lost);

        [Test]
        public void BeforeAnyMessage_IsLost_NoPoint_NoShaving()
        {
            var p = New();
            Assert.That(p.IsLost, Is.True);
            Assert.That(p.HasPoint, Is.False);
            Assert.That(p.IsShaving, Is.False);
        }

        [Test]
        public void IngestPoint_HasPoint_AndCoords()
        {
            var p = New(lost: 5f);
            p.Ingest("1,12.5,-300,1"); p.Tick(0f);
            Assert.That(p.IsLost, Is.False);
            Assert.That(p.HasPoint, Is.True);
            Assert.That(p.IsShaving, Is.True);
            Assert.That(p.RawPointMm.x, Is.EqualTo(12.5f).Within(1e-4f));
            Assert.That(p.RawPointMm.y, Is.EqualTo(-300f).Within(1e-4f));
        }

        [Test]
        public void EmptyXY_IsNoDetection()
        {
            var p = New(lost: 5f);
            p.Ingest("1,10,20,1"); p.Tick(0f);
            Assert.That(p.HasPoint, Is.True);
            p.Ingest("2,,,0"); p.Tick(0f);
            Assert.That(p.HasPoint, Is.False);
            Assert.That(p.IsShaving, Is.False);
        }

        [Test]
        public void ShavingHold_KeepsLastPointWithShavingOne()
        {
            // Python 側が shaving ホールド中は直近 x,y を shaving=1 で継続送信する契約。
            var p = New(lost: 5f);
            p.Ingest("1,40,-260,1"); p.Tick(0f);
            p.Ingest("2,40,-260,1"); p.Tick(0f); // 今回転は氷なしだが直近点を継続
            Assert.That(p.HasPoint, Is.True);
            Assert.That(p.IsShaving, Is.True);
            Assert.That(p.RawPointMm.x, Is.EqualTo(40f).Within(1e-4f));
        }

        [Test]
        public void MalformedLine_IsIgnored()
        {
            var p = New(lost: 5f);
            p.Ingest("1,10,20,1"); p.Tick(0f);
            p.Ingest("garbage"); p.Tick(0f);
            p.Ingest("2,10,20"); p.Tick(0f);      // フィールド 3 個
            p.Ingest("3,10,20,1,extra"); p.Tick(0f); // フィールド 5 個
            p.Ingest("4,10,,1"); p.Tick(0f);      // 片方だけ空欄
            p.Ingest("5,x,20,1"); p.Tick(0f);     // 数値でない
            p.Ingest("6,10,20,7"); p.Tick(0f);    // shaving 範囲外
            Assert.That(p.HasPoint, Is.True);
            Assert.That(p.RawPointMm.x, Is.EqualTo(10f).Within(1e-4f)); // どれも状態を変えない
            Assert.That(p.RawPointMm.y, Is.EqualTo(20f).Within(1e-4f));
            Assert.That(p.IsShaving, Is.True);
        }

        [Test]
        public void NullLine_IsIgnored()
        {
            var p = New();
            Assert.DoesNotThrow(() => p.Ingest(null));
        }

        [Test]
        public void NaNOrInfinity_RejectsLine()
        {
            var p = New(lost: 5f);
            p.Ingest("1,10,20,1"); p.Tick(0f);
            p.Ingest("2,NaN,20,1"); p.Tick(0f);
            p.Ingest("3,10,Infinity,1"); p.Tick(0f);
            Assert.That(p.RawPointMm.x, Is.EqualTo(10f).Within(1e-4f));
        }

        [Test]
        public void Watchdog_LostAfterSeqStops()
        {
            var p = New(lost: 0.3f);
            p.Ingest("10,5,5,1"); p.Tick(0f);
            Assert.That(p.HasPoint, Is.True);

            p.Ingest("10,5,5,1"); p.Tick(0.2f); // 同じ seq を再読み（Python 停止）
            Assert.That(p.IsLost, Is.False);
            p.Ingest("10,5,5,1"); p.Tick(0.2f); // 累計 0.4 > 0.3
            Assert.That(p.IsLost, Is.True);
            Assert.That(p.HasPoint, Is.False);  // フェイルセーフ
            Assert.That(p.IsShaving, Is.False);
        }

        [Test]
        public void Watchdog_StaysAliveWhileSeqAdvances()
        {
            var p = New(lost: 0.3f);
            p.Ingest("10,5,5,1"); p.Tick(0.2f);
            p.Ingest("11,6,6,1"); p.Tick(0.2f); // seq 変化で再武装
            Assert.That(p.IsLost, Is.False);
            Assert.That(p.RawPointMm.x, Is.EqualTo(6f).Within(1e-4f));
        }

        [Test]
        public void Watchdog_RecoversWhenSeqResumes()
        {
            var p = New(lost: 0.3f);
            p.Ingest("10,5,5,1"); p.Tick(0.4f); // ロスト
            Assert.That(p.IsLost, Is.True);

            p.Ingest("11,7,8,0"); p.Tick(0f);   // seq 前進で復帰
            Assert.That(p.IsLost, Is.False);
            Assert.That(p.HasPoint, Is.True);
            Assert.That(p.RawPointMm.y, Is.EqualTo(8f).Within(1e-4f));
            Assert.That(p.IsShaving, Is.False);
        }

        [Test]
        public void PythonRestart_SeqDropsButTreatedAsFresh()
        {
            var p = New(lost: 0.3f);
            p.Ingest("500,1,1,1"); p.Tick(0f);
            p.Ingest("1,2,3,0"); p.Tick(0f);    // 再起動で seq が戻る（変化＝新鮮）
            Assert.That(p.IsLost, Is.False);
            Assert.That(p.RawPointMm.x, Is.EqualTo(2f).Within(1e-4f));
        }

        [Test]
        public void SameSeq_IsIgnored()
        {
            var p = New(lost: 5f);
            p.Ingest("1,10,20,1"); p.Tick(0f);
            p.Ingest("1,99,99,0"); p.Tick(0f);  // seq 同一 → 反映しない
            Assert.That(p.RawPointMm.x, Is.EqualTo(10f).Within(1e-4f));
            Assert.That(p.IsShaving, Is.True);
        }

        [Test]
        public void WatchdogDisabled_WhenTimeoutZero()
        {
            var p = new LidarPositionProtocol(0f);
            p.Ingest("1,10,20,1"); p.Tick(100f);
            Assert.That(p.IsLost, Is.False);
            Assert.That(p.HasPoint, Is.True);
        }
    }
}
