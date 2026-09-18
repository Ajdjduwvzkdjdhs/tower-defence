using NUnit.Framework;
using KakigoriTD.Core;

namespace KakigoriTD.Tests.EditMode
{
    /// <summary>
    /// State 群（Title → Battle → Result）の遷移検証（ADR-30 で Wave 制を廃止）。
    /// </summary>
    public class StatesTests
    {
        /// <summary>State 群の遷移を検証するためのスタブ context。</summary>
        private class FakeContext : IGameContext
        {
            public int TargetKillCount { get; set; } = 100;
            public int KillCount { get; set; }
            public int MaxCombo { get; set; }
            public float ElapsedSec { get; set; }
            public bool IsFortDestroyed { get; set; }
            public bool IsStartRequested { get; set; }
            public bool IsRetryRequested { get; set; }

            public int BeginCount;
            public int ClearCount;
            public StageResult? Result;

            public int ClearCountdownCount;
            public readonly System.Collections.Generic.List<(string Text, bool IsFinal)> CountdownSteps = new();

            public void BeginBattle() => BeginCount++;
            public void ClearRemainingEnemies() => ClearCount++;
            public void ShowResult(StageResult result) => Result = result;
            public void ShowCountdownStep(string text, bool isFinal) => CountdownSteps.Add((text, isFinal));
            public void ClearCountdown() => ClearCountdownCount++;
        }

        // ---- TitleState ----

        [Test]
        public void Title_WhenStartRequested_ChangesToCountdown()
        {
            var ctx = new FakeContext { IsStartRequested = true };
            var fsm = new GameStateMachine();
            fsm.Change(new TitleState(fsm, ctx));

            fsm.Tick(0.016f);

            Assert.That(fsm.Current, Is.TypeOf<CountdownState>());
        }

        [Test]
        public void Title_WithoutStartRequest_Stays()
        {
            var ctx = new FakeContext { IsStartRequested = false };
            var fsm = new GameStateMachine();
            fsm.Change(new TitleState(fsm, ctx));

            fsm.Tick(1f);

            Assert.That(fsm.Current, Is.TypeOf<TitleState>());
        }

        // ---- CountdownState ----

        [Test]
        public void Countdown_OnEnter_ShowsFirstStep()
        {
            var ctx = new FakeContext();
            var fsm = new GameStateMachine();
            fsm.Change(new CountdownState(fsm, ctx));

            Assert.That(ctx.CountdownSteps, Is.EqualTo(new[] { ("3", false) }));
        }

        [Test]
        public void Countdown_StepsThroughThreeTwoOneGo_ThenBeginsBattle()
        {
            var ctx = new FakeContext();
            var fsm = new GameStateMachine();
            fsm.Change(new CountdownState(fsm, ctx));

            fsm.Tick(1f); // "3" -> "2"
            fsm.Tick(1f); // "2" -> "1"
            fsm.Tick(1f); // "1" -> "スタート"

            Assert.That(fsm.Current, Is.TypeOf<CountdownState>());
            Assert.That(ctx.CountdownSteps, Is.EqualTo(new[]
            {
                ("3", false), ("2", false), ("1", false), ("スタート", true),
            }));

            fsm.Tick(1f); // "スタート" -> BattleState

            Assert.That(fsm.Current, Is.TypeOf<BattleState>());
            Assert.That(ctx.BeginCount, Is.EqualTo(1));
        }

        [Test]
        public void Countdown_BeforeStepBoundary_DoesNotAdvance()
        {
            var ctx = new FakeContext();
            var fsm = new GameStateMachine();
            fsm.Change(new CountdownState(fsm, ctx));

            fsm.Tick(0.5f);

            Assert.That(ctx.CountdownSteps, Is.EqualTo(new[] { ("3", false) }));
        }

        [Test]
        public void Countdown_OnExit_ClearsCountdownDisplay()
        {
            var ctx = new FakeContext();
            var fsm = new GameStateMachine();
            fsm.Change(new CountdownState(fsm, ctx));

            fsm.Change(new TitleState(fsm, ctx));

            Assert.That(ctx.ClearCountdownCount, Is.EqualTo(1));
        }

        // ---- BattleState ----

        [Test]
        public void Battle_OnEnter_BeginsBattle()
        {
            var ctx = new FakeContext();
            var fsm = new GameStateMachine();
            fsm.Change(new BattleState(fsm, ctx));

            Assert.That(ctx.BeginCount, Is.EqualTo(1));
        }

        [Test]
        public void Battle_BeforeTargetReached_Stays()
        {
            var ctx = new FakeContext { TargetKillCount = 100, KillCount = 99 };
            var fsm = new GameStateMachine();
            fsm.Change(new BattleState(fsm, ctx));

            fsm.Tick(10f);

            Assert.That(fsm.Current, Is.TypeOf<BattleState>());
            Assert.That(ctx.Result, Is.Null);
        }

        [Test]
        public void Battle_WhenTargetReached_ResultIsWinWithClearTime()
        {
            var ctx = new FakeContext { TargetKillCount = 100, KillCount = 100, ElapsedSec = 102.5f, MaxCombo = 12 };
            var fsm = new GameStateMachine();
            fsm.Change(new BattleState(fsm, ctx));

            fsm.Tick(0.016f);

            Assert.That(fsm.Current, Is.TypeOf<ResultState>());
            Assert.That(ctx.ClearCount, Is.EqualTo(1), "決着時に残敵を消す");
            Assert.That(ctx.Result.HasValue, Is.True);
            Assert.That(ctx.Result.Value.IsWin, Is.True);
            Assert.That(ctx.Result.Value.ClearTimeSec, Is.EqualTo(102.5f).Within(1e-4f));
            Assert.That(ctx.Result.Value.MaxCombo, Is.EqualTo(12));
        }

        [Test]
        public void Battle_WhenFortDestroyed_ResultIsLoseWithKillCount()
        {
            var ctx = new FakeContext { TargetKillCount = 100, KillCount = 62, IsFortDestroyed = true };
            var fsm = new GameStateMachine();
            fsm.Change(new BattleState(fsm, ctx));

            fsm.Tick(0.016f);

            Assert.That(fsm.Current, Is.TypeOf<ResultState>());
            Assert.That(ctx.Result.Value.IsWin, Is.False);
            Assert.That(ctx.Result.Value.KillCount, Is.EqualTo(62));
            Assert.That(ctx.Result.Value.TargetKillCount, Is.EqualTo(100));
        }

        [Test]
        public void Battle_FortDestroyedAndTargetReachedSameFrame_LoseWins()
        {
            // 飛来中の弾で砦が落ちるのとクリアが同フレームなら敗北を優先する（旧 WavePlayingState の流儀）。
            var ctx = new FakeContext { TargetKillCount = 10, KillCount = 10, IsFortDestroyed = true };
            var fsm = new GameStateMachine();
            fsm.Change(new BattleState(fsm, ctx));

            fsm.Tick(0.016f);

            Assert.That(ctx.Result.Value.IsWin, Is.False);
        }

        // ---- ResultState ----

        [Test]
        public void Result_OnEnter_ShowsResult()
        {
            var ctx = new FakeContext();
            var fsm = new GameStateMachine();
            var result = new StageResult(true, 42f, 100, 100, 8);

            fsm.Change(new ResultState(fsm, ctx, result));

            Assert.That(ctx.Result.HasValue, Is.True);
            Assert.That(ctx.Result.Value.ClearTimeSec, Is.EqualTo(42f).Within(1e-4f));
        }

        [Test]
        public void Result_WhenRetryRequested_ChangesToTitle()
        {
            var ctx = new FakeContext { IsRetryRequested = true };
            var fsm = new GameStateMachine();
            fsm.Change(new ResultState(fsm, ctx, new StageResult(false, 10f, 3, 100, 2)));

            fsm.Tick(0.016f);

            Assert.That(fsm.Current, Is.TypeOf<TitleState>());
        }
    }
}
