namespace KakigoriTD.Core
{
    /// <summary>
    /// リザルト状態。Enter で結果を表示し、再挑戦入力でタイトルへ戻る。純ロジック（ADR-8）。
    /// スコアは <see cref="StageResult"/>（クリア＝クリアタイム / 失敗＝到達撃破数・ADR-30）。
    /// </summary>
    public class ResultState : IGameState
    {
        private readonly GameStateMachine _machine;
        private readonly IGameContext _ctx;

        public ResultState(GameStateMachine machine, IGameContext ctx, StageResult result)
        {
            _machine = machine;
            _ctx = ctx;
            Result = result;
        }

        /// <summary>確定した結果。</summary>
        public StageResult Result { get; }

        /// <summary>勝敗（true=クリア）。</summary>
        public bool IsWin => Result.IsWin;

        public void Enter() => _ctx.ShowResult(Result);

        public void Tick(float deltaTime)
        {
            if (_ctx.IsRetryRequested)
                _machine.Change(new TitleState(_machine, _ctx));
        }

        public void Exit() { }
    }
}
