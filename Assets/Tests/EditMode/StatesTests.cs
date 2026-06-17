using System.Collections.Generic;
using NUnit.Framework;
using KakigoriTD.Core;

namespace KakigoriTD.Tests.EditMode
{
    public class StatesTests
    {
        /// <summary>State 群の遷移を検証するためのスタブ context。</summary>
        private class FakeContext : IGameContext
        {
            public int WaveCount { get; set; } = 3;
            public float WaveDuration { get; set; } = 30f;
            public float WaveLullDuration { get; set; } = 5f;
            public float WaveIntroDuration { get; set; } = 0f;
            public bool IsFortDestroyed { get; set; }
            public int FortHp { get; set; } = 1000;
            public bool IsStartRequested { get; set; }
            public bool IsRetryRequested { get; set; }

            public int PenaltyTotal { get; set; }

            public readonly List<int> BegunWaves = new List<int>();
            public int ClearCount;
            public bool? ResultIsWin;
            public int ResultScore = -1;

            public float GetWaveDuration(int waveIndex) => WaveDuration;
            public void BeginWave(int waveIndex) => BegunWaves.Add(waveIndex);
            public void ClearRemainingEnemies() => ClearCount++;
            public void ShowResult(bool isWin, int score) { ResultIsWin = isWin; ResultScore = score; }
        }

        // ---- TitleState ----

        [Test]
        public void Title_WhenStartRequested_ChangesToWaveIntro()
        {
            var ctx = new FakeContext { IsStartRequested = true };
            var fsm = new GameStateMachine();
            var title = new TitleState(fsm, ctx);
            fsm.Change(title);
            fsm.Tick(0.016f);
            Assert.That(fsm.Current, Is.TypeOf<WaveIntroState>());
        }

        [Test]
        public void Title_WhenNotStarted_StaysTitle()
        {
            var ctx = new FakeContext { IsStartRequested = false };
            var fsm = new GameStateMachine();
            var title = new TitleState(fsm, ctx);
            fsm.Change(title);
            fsm.Tick(0.016f);
            Assert.That(fsm.Current, Is.SameAs(title));
        }

        // ---- WaveIntroState ----

        [Test]
        public void WaveIntro_AfterIntroDuration_ChangesToWavePlaying()
        {
            var ctx = new FakeContext { WaveIntroDuration = 0f };
            var fsm = new GameStateMachine();
            fsm.Change(new WaveIntroState(fsm, ctx, 0));
            fsm.Tick(0.016f);
            Assert.That(fsm.Current, Is.TypeOf<WavePlayingState>());
        }

        [Test]
        public void WaveIntro_BeforeIntroDuration_StaysInIntro()
        {
            var ctx = new FakeContext { WaveIntroDuration = 1f };
            var fsm = new GameStateMachine();
            var intro = new WaveIntroState(fsm, ctx, 0);
            fsm.Change(intro);
            fsm.Tick(0.2f);
            Assert.That(fsm.Current, Is.SameAs(intro));
        }

        // ---- WavePlayingState ----

        [Test]
        public void WavePlaying_OnEnter_BeginsWaveForThatIndex()
        {
            var ctx = new FakeContext();
            var fsm = new GameStateMachine();
            fsm.Change(new WavePlayingState(fsm, ctx, 2)); // Enter() -> BeginWave(2)
            Assert.That(ctx.BegunWaves, Is.EqualTo(new[] { 2 }));
        }

        [Test]
        public void WavePlaying_WhenFortDestroyed_ClearsEnemiesAndGoesToResultLose()
        {
            var ctx = new FakeContext { IsFortDestroyed = true, FortHp = 0 };
            var fsm = new GameStateMachine();
            fsm.Change(new WavePlayingState(fsm, ctx, 0));
            fsm.Tick(0.016f);
            Assert.That(fsm.Current, Is.TypeOf<ResultState>());
            Assert.That(((ResultState)fsm.Current).IsWin, Is.False);
            Assert.That(ctx.ClearCount, Is.EqualTo(1));
        }

        [Test]
        public void WavePlaying_FortDestroyed_TakesPriorityOverTimeUp()
        {
            // duration exceeded AND fort destroyed in same tick -> Lose, not Win.
            var ctx = new FakeContext { WaveDuration = 0.1f, IsFortDestroyed = true, FortHp = 0, WaveCount = 1 };
            var fsm = new GameStateMachine();
            fsm.Change(new WavePlayingState(fsm, ctx, 0));
            fsm.Tick(1f);
            Assert.That(((ResultState)fsm.Current).IsWin, Is.False);
        }

        [Test]
        public void WavePlaying_TimeUp_NonFinalWave_ClearsEnemiesAndGoesToWaveLull()
        {
            var ctx = new FakeContext { WaveDuration = 1f, WaveCount = 3 };
            var fsm = new GameStateMachine();
            fsm.Change(new WavePlayingState(fsm, ctx, 0));
            fsm.Tick(1.5f);
            Assert.That(fsm.Current, Is.TypeOf<WaveLullState>());
            Assert.That(ctx.ClearCount, Is.EqualTo(1));
        }

        [Test]
        public void WavePlaying_TimeUp_FinalWave_GoesToResultWin()
        {
            var ctx = new FakeContext { WaveDuration = 1f, WaveCount = 3, FortHp = 640 };
            var fsm = new GameStateMachine();
            fsm.Change(new WavePlayingState(fsm, ctx, 2)); // last wave (0-based)
            fsm.Tick(1.5f);
            Assert.That(fsm.Current, Is.TypeOf<ResultState>());
            Assert.That(((ResultState)fsm.Current).IsWin, Is.True);
        }

        [Test]
        public void WavePlaying_BeforeTimeUp_StaysPlaying()
        {
            var ctx = new FakeContext { WaveDuration = 5f };
            var fsm = new GameStateMachine();
            var playing = new WavePlayingState(fsm, ctx, 0);
            fsm.Change(playing);
            fsm.Tick(1f);
            Assert.That(fsm.Current, Is.SameAs(playing));
        }

        // ---- WaveLullState ----

        [Test]
        public void WaveLull_AfterLullDuration_GoesToNextWaveIntro()
        {
            var ctx = new FakeContext { WaveLullDuration = 5f, WaveIntroDuration = 999f };
            var fsm = new GameStateMachine();
            fsm.Change(new WaveLullState(fsm, ctx, 0));
            fsm.Tick(5f);
            Assert.That(fsm.Current, Is.TypeOf<WaveIntroState>());
        }

        [Test]
        public void WaveLull_WhenFortDestroyed_GoesToResultLose()
        {
            // 飛来中の弾で休止中に砦が落ちるケース。
            var ctx = new FakeContext { IsFortDestroyed = true, FortHp = 0 };
            var fsm = new GameStateMachine();
            fsm.Change(new WaveLullState(fsm, ctx, 0));
            fsm.Tick(0.016f);
            Assert.That(fsm.Current, Is.TypeOf<ResultState>());
            Assert.That(((ResultState)fsm.Current).IsWin, Is.False);
        }

        // ---- ResultState ----

        [Test]
        public void Result_OnEnter_ShowsResultWithScore()
        {
            var ctx = new FakeContext();
            var fsm = new GameStateMachine();
            fsm.Change(new ResultState(fsm, ctx, isWin: true, score: 555));
            Assert.That(ctx.ResultIsWin, Is.True);
            Assert.That(ctx.ResultScore, Is.EqualTo(555));
        }

        [Test]
        public void Result_WinScore_EqualsRemainingFortHp()
        {
            // 勝利時のスコアは残砦HP（WavePlaying 経由で確認）。
            var ctx = new FakeContext { WaveDuration = 1f, WaveCount = 1, FortHp = 873 };
            var fsm = new GameStateMachine();
            fsm.Change(new WavePlayingState(fsm, ctx, 0));
            fsm.Tick(1f);
            Assert.That(ctx.ResultScore, Is.EqualTo(873));
        }

        [Test]
        public void Result_WhenRetryRequested_GoesToTitle()
        {
            var ctx = new FakeContext { IsRetryRequested = true };
            var fsm = new GameStateMachine();
            fsm.Change(new ResultState(fsm, ctx, isWin: false, score: 0));
            fsm.Tick(0.016f);
            Assert.That(fsm.Current, Is.TypeOf<TitleState>());
        }

        // ---- Phase2: PenaltyTotal がスコアに反映される ----

        [Test]
        public void WavePlaying_WinScore_WithPenalty_SubtractsPenalty()
        {
            var ctx = new FakeContext { WaveDuration = 1f, WaveCount = 1, FortHp = 640, PenaltyTotal = 100 };
            var fsm = new GameStateMachine();
            fsm.Change(new WavePlayingState(fsm, ctx, 0));
            fsm.Tick(1.5f);
            Assert.That(ctx.ResultScore, Is.EqualTo(540), "640 − 100 = 540");
        }

        [Test]
        public void WavePlaying_LoseScore_WithPenalty_ClampsToZero()
        {
            var ctx = new FakeContext { IsFortDestroyed = true, FortHp = 0, PenaltyTotal = 150 };
            var fsm = new GameStateMachine();
            fsm.Change(new WavePlayingState(fsm, ctx, 0));
            fsm.Tick(0.016f);
            Assert.That(ctx.ResultScore, Is.EqualTo(0), "HP0 − penalty は 0 に丸める");
        }

        [Test]
        public void WavePlaying_NoPenalty_BehaviorUnchangedFromPhase1()
        {
            // PenaltyTotal=0（既定）なら Phase1 と同じスコアになる（回帰確認）。
            var ctx = new FakeContext { WaveDuration = 1f, WaveCount = 1, FortHp = 873, PenaltyTotal = 0 };
            var fsm = new GameStateMachine();
            fsm.Change(new WavePlayingState(fsm, ctx, 0));
            fsm.Tick(1f);
            Assert.That(ctx.ResultScore, Is.EqualTo(873));
        }

        // ---- 通し（統合）----

        [Test]
        public void FullFlow_TitleToThreeWaves_EndsInResultWin()
        {
            var ctx = new FakeContext
            {
                WaveCount = 3,
                WaveDuration = 1f,
                WaveLullDuration = 1f,
                WaveIntroDuration = 0f,
                IsStartRequested = true,
                FortHp = 1000
            };
            var fsm = new GameStateMachine();
            fsm.Change(new TitleState(fsm, ctx));

            // 十分な回数 Tick して通し進行させる。
            for (int i = 0; i < 50; i++)
                fsm.Tick(1f);

            Assert.That(fsm.Current, Is.TypeOf<ResultState>());
            Assert.That(((ResultState)fsm.Current).IsWin, Is.True);
            Assert.That(ctx.BegunWaves, Is.EqualTo(new[] { 0, 1, 2 }), "3 ウェーブが順に開始される");
            Assert.That(ctx.ClearCount, Is.EqualTo(3), "各ウェーブ終了で残敵消去");
        }
    }
}
