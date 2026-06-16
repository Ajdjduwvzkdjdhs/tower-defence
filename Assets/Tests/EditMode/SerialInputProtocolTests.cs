using KakigoriTD.Input;
using NUnit.Framework;

namespace KakigoriTD.Tests.EditMode
{
    /// <summary>
    /// EditMode tests for <see cref="SerialInputProtocol"/> (pure serial-line → shave state).
    /// Covers Phase 1.5 acceptance: parse 1/0, ignore malformed lines, and the watchdog
    /// fail-safe that drops IsShaving to false after a no-message timeout.
    /// </summary>
    public class SerialInputProtocolTests
    {
        [Test]
        public void BeforeAnyMessage_IsShavingFalse_AndStale()
        {
            var p = new SerialInputProtocol(0.5f);

            Assert.That(p.IsShaving, Is.False);
            Assert.That(p.IsStale, Is.True);
        }

        [Test]
        public void IngestOne_StartsShaving()
        {
            var p = new SerialInputProtocol(0.5f);

            p.Ingest("1");

            Assert.That(p.IsShaving, Is.True);
            Assert.That(p.IsStale, Is.False);
        }

        [Test]
        public void IngestZero_StopsShaving()
        {
            var p = new SerialInputProtocol(0.5f);
            p.Ingest("1");

            p.Ingest("0");

            Assert.That(p.IsShaving, Is.False);
        }

        [Test]
        public void TrimsWhitespaceAndCarriageReturn()
        {
            var p = new SerialInputProtocol(0.5f);

            p.Ingest(" 1\r");

            Assert.That(p.IsShaving, Is.True);
        }

        [Test]
        public void MalformedLine_IsIgnored_StateUnchanged()
        {
            var p = new SerialInputProtocol(0.5f);
            p.Ingest("1");

            p.Ingest("xyz");

            Assert.That(p.IsShaving, Is.True); // 不正行は削り状態を変えない
        }

        [Test]
        public void NullLine_IsIgnored()
        {
            var p = new SerialInputProtocol(0.5f);

            Assert.DoesNotThrow(() => p.Ingest(null));
            Assert.That(p.IsShaving, Is.False);
        }

        [Test]
        public void Watchdog_DropsShavingAfterTimeout()
        {
            var p = new SerialInputProtocol(0.5f);
            p.Ingest("1");

            p.Tick(0.6f); // 0.6 > 0.5 -> stale

            Assert.That(p.IsStale, Is.True);
            Assert.That(p.IsShaving, Is.False);
        }

        [Test]
        public void Watchdog_StaysAliveWithinTimeout()
        {
            var p = new SerialInputProtocol(0.5f);
            p.Ingest("1");

            p.Tick(0.4f); // 0.4 < 0.5

            Assert.That(p.IsShaving, Is.True);
        }

        [Test]
        public void Watchdog_ReArmsOnNewMessage()
        {
            var p = new SerialInputProtocol(0.5f);
            p.Ingest("1");
            p.Tick(0.6f);               // stale -> false
            Assert.That(p.IsShaving, Is.False);

            p.Ingest("1");              // new message resets watchdog

            Assert.That(p.IsShaving, Is.True);
        }

        [Test]
        public void MalformedLine_ResetsWatchdog_KeepingPriorShave()
        {
            var p = new SerialInputProtocol(0.5f);
            p.Ingest("1");
            p.Tick(0.4f);

            p.Ingest("noise"); // 受信＝生存。watchdog をリセットしつつ削り状態は保持
            p.Tick(0.4f);      // 合計 0.8 だが、ノイズ受信でリセット済みなので 0.4 のみ

            Assert.That(p.IsShaving, Is.True);
        }

        [Test]
        public void WatchdogDisabled_WhenTimeoutZero()
        {
            var p = new SerialInputProtocol(0f); // watchdog 無効
            p.Ingest("1");

            p.Tick(100f);

            Assert.That(p.IsStale, Is.False);
            Assert.That(p.IsShaving, Is.True);
        }
    }
}
