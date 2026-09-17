namespace KakigoriTD.Core
{
    /// <summary>
    /// 単一ステージの戦闘状態（ADR-30）。旧 <c>WaveIntroState</c> / <c>WavePlayingState</c> /
    /// <c>WaveLullState</c> を 1 つに置き換える。純ロジック（ADR-8）。
    ///
    /// Enter で戦闘を開始し、以後は 2 つの終了条件だけを見る:
    ///  ・砦HP 0 → 残敵消去 → Result(Lose・スコアは到達撃破数)
    ///  ・目標撃破数に到達 → 残敵消去 → Result(Win・スコアはクリアタイム)
    ///
    /// 敗北判定を先に見るのは旧 <c>WavePlayingState</c> の流儀を踏襲（飛来中の弾で砦が先に落ちる場合、
    /// 同フレームのクリアより敗北を優先する）。
    /// </summary>
    public class BattleState : IGameState
    {
        private readonly GameStateMachine _machine;
        private readonly IGameContext _ctx;

        public BattleState(GameStateMachine machine, IGameContext ctx)
        {
            _machine = machine;
            _ctx = ctx;
        }

        public void Enter() => _ctx.BeginBattle();

        public void Tick(float deltaTime)
        {
            if (_ctx.IsFortDestroyed)
            {
                Finish(isWin: false);
                return;
            }

            if (_ctx.KillCount >= _ctx.TargetKillCount)
                Finish(isWin: true);
        }

        public void Exit() { }

        private void Finish(bool isWin)
        {
            _ctx.ClearRemainingEnemies();
            var result = new StageResult(
                isWin,
                _ctx.ElapsedSec,
                _ctx.KillCount,
                _ctx.TargetKillCount,
                _ctx.MaxCombo);
            _machine.Change(new ResultState(_machine, _ctx, result));
        }
    }
}
