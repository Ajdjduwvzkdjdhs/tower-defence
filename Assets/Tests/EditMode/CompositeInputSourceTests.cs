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

        // ---- InputMode 分岐（ADR-24 / Phase 5）--------------------------------

        [Test]
        public void VisionMode_ActiveChannel_ComesFromVision_IgnoringKeyboard()
        {
            var kb = new StubInput { ActiveChannel = 1 };   // キーボードは押されていても
            var serial = new StubInput { ActiveChannel = -1 };
            var vision = new StubInput { ActiveChannel = 3 };
            var c = new CompositeInputSource(kb, serial, vision, InputMode.Vision);

            Assert.That(c.ActiveChannel, Is.EqualTo(3)); // Vision モードは位置検知のみ
        }

        [Test]
        public void KeyboardMode_ActiveChannel_IgnoresVision()
        {
            var kb = new StubInput { ActiveChannel = 2 };
            var serial = new StubInput { ActiveChannel = -1 };
            var vision = new StubInput { ActiveChannel = 0 };
            var c = new CompositeInputSource(kb, serial, vision, InputMode.Keyboard);

            Assert.That(c.ActiveChannel, Is.EqualTo(2)); // Keyboard モードは位置検知を見ない
        }

        [Test]
        public void VisionMode_ShaveStillOrsKeyboardAndSerial()
        {
            var kb = new StubInput();
            var serial = new StubInput();
            var vision = new StubInput { ActiveChannel = 0 }; // 削りは担当しない
            var c = new CompositeInputSource(kb, serial, vision, InputMode.Vision);

            Assert.That(c.IsShaving, Is.False);
            serial.IsShaving = true;                          // M5Stick 回転
            Assert.That(c.IsShaving, Is.True);                // Vision モードでも削りは OR
            serial.IsShaving = false; kb.IsShaving = true;    // キーボード Space
            Assert.That(c.IsShaving, Is.True);
        }

        [Test]
        public void VisionMode_NullVision_ChannelMinusOne()
        {
            var kb = new StubInput { ActiveChannel = 2 };
            var c = new CompositeInputSource(kb, null, null, InputMode.Vision);

            Assert.That(c.ActiveChannel, Is.EqualTo(-1)); // 位置検知未接続なら谷なし
        }
    }
}
