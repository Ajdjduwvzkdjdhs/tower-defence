using KakigoriTD.Input;
using NUnit.Framework;

namespace KakigoriTD.Tests.EditMode
{
    /// <summary>
    /// EditMode tests for <see cref="CompositeInputSource"/> (keyboard + serial merge, ADR-15).
    /// Covers: channel/confirm come from keyboard, shave is the logical OR, and null serial is tolerated.
    /// </summary>
    public class CompositeInputSourceTests
    {
        // Mutable stub IInputSource for driving the merge under test.
        private sealed class StubInput : IInputSource
        {
            public int ActiveChannel { get; set; } = -1;
            public bool IsShaving { get; set; }
            public bool ConfirmPressed { get; set; }
        }

        [Test]
        public void ActiveChannel_ComesFromKeyboard()
        {
            var kb = new StubInput { ActiveChannel = 2 };
            var serial = new StubInput { ActiveChannel = -1 };
            var c = new CompositeInputSource(kb, serial);

            Assert.That(c.ActiveChannel, Is.EqualTo(2));
        }

        [Test]
        public void ActiveChannel_FallsBackToSerial_WhenKeyboardUnset()
        {
            var kb = new StubInput { ActiveChannel = -1 };
            var serial = new StubInput { ActiveChannel = 3 };
            var c = new CompositeInputSource(kb, serial);

            Assert.That(c.ActiveChannel, Is.EqualTo(3));
        }

        [Test]
        public void IsShaving_IsLogicalOr()
        {
            var kb = new StubInput();
            var serial = new StubInput();
            var c = new CompositeInputSource(kb, serial);

            Assert.That(c.IsShaving, Is.False);

            kb.IsShaving = true;
            Assert.That(c.IsShaving, Is.True); // キーボード Space のみ

            kb.IsShaving = false; serial.IsShaving = true;
            Assert.That(c.IsShaving, Is.True); // ハンドル回転のみ

            kb.IsShaving = true; serial.IsShaving = true;
            Assert.That(c.IsShaving, Is.True); // 両方
        }

        [Test]
        public void ConfirmPressed_IsLogicalOr()
        {
            var kb = new StubInput();
            var serial = new StubInput();
            var c = new CompositeInputSource(kb, serial);

            Assert.That(c.ConfirmPressed, Is.False);

            kb.ConfirmPressed = true;
            Assert.That(c.ConfirmPressed, Is.True);
        }

        [Test]
        public void NullSerial_UsesKeyboardOnly()
        {
            var kb = new StubInput { ActiveChannel = 1, IsShaving = true, ConfirmPressed = true };
            var c = new CompositeInputSource(kb, null);

            Assert.That(c.ActiveChannel, Is.EqualTo(1));
            Assert.That(c.IsShaving, Is.True);
            Assert.That(c.ConfirmPressed, Is.True);
        }
    }
}
