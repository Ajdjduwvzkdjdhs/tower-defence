using System;
using System.Collections.Generic;
using NUnit.Framework;
using KakigoriTD.Core;

namespace KakigoriTD.Tests.EditMode
{
    public class GameStateMachineTests
    {
        /// <summary>Enter/Tick/Exit の呼び出しを記録するスタブ状態。</summary>
        private class FakeState : IGameState
        {
            public int EnterCount;
            public int ExitCount;
            public int TickCount;
            public float LastDelta = -1f;
            public Action OnEnter;
            public Action OnExit;

            public void Enter() { EnterCount++; OnEnter?.Invoke(); }
            public void Tick(float dt) { TickCount++; LastDelta = dt; }
            public void Exit() { ExitCount++; OnExit?.Invoke(); }
        }

        [Test]
        public void Change_SetsCurrentToNext()
        {
            var fsm = new GameStateMachine();
            var s = new FakeState();
            fsm.Change(s);
            Assert.That(fsm.Current, Is.SameAs(s));
        }

        [Test]
        public void Change_EntersNextState()
        {
            var fsm = new GameStateMachine();
            var s = new FakeState();
            fsm.Change(s);
            Assert.That(s.EnterCount, Is.EqualTo(1));
        }

        [Test]
        public void Change_FromNull_DoesNotThrow_AndEntersNext()
        {
            var fsm = new GameStateMachine();
            var s = new FakeState();
            Assert.DoesNotThrow(() => fsm.Change(s));
            Assert.That(s.EnterCount, Is.EqualTo(1));
        }

        [Test]
        public void Change_ExitsPreviousStateBeforeEnteringNext()
        {
            var fsm = new GameStateMachine();
            var log = new List<string>();
            var a = new FakeState { OnExit = () => log.Add("exit:a") };
            var b = new FakeState { OnEnter = () => log.Add("enter:b") };
            fsm.Change(a);
            fsm.Change(b);
            Assert.That(a.ExitCount, Is.EqualTo(1));
            Assert.That(b.EnterCount, Is.EqualTo(1));
            Assert.That(log, Is.EqualTo(new[] { "exit:a", "enter:b" }), "Exit が Enter より先");
        }

        [Test]
        public void Tick_RoutesToCurrentState()
        {
            var fsm = new GameStateMachine();
            var s = new FakeState();
            fsm.Change(s);
            fsm.Tick(0.25f);
            Assert.That(s.TickCount, Is.EqualTo(1));
            Assert.That(s.LastDelta, Is.EqualTo(0.25f).Within(1e-6f));
        }

        [Test]
        public void Tick_WithNoState_DoesNotThrow()
        {
            var fsm = new GameStateMachine();
            Assert.DoesNotThrow(() => fsm.Tick(0.1f));
        }

        [Test]
        public void Change_ToNull_ExitsPrevious_AndCurrentIsNull()
        {
            var fsm = new GameStateMachine();
            var a = new FakeState();
            fsm.Change(a);
            fsm.Change(null);
            Assert.That(a.ExitCount, Is.EqualTo(1));
            Assert.That(fsm.Current, Is.Null);
        }
    }
}
