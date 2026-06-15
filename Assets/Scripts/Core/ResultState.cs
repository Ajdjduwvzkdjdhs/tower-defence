namespace KakigoriTD.Core
{
    /// <summary>
    /// リザルト状態。Enter で勝敗とスコアを表示し、再挑戦入力でタイトルへ戻る。
    /// 純ロジック（ADR-8）。
    /// </summary>
    public class ResultState : IGameState
    {
        private readonly GameStateMachine _machine;
        private readonly IGameContext _ctx;
        private readonly bool _isWin;
        private readonly int _score;

        public ResultState(GameStateMachine machine, IGameContext ctx, bool isWin, int score)
        {
            _machine = machine;
            _ctx = ctx;
            _isWin = isWin;
            _score = score;
        }

        /// <summary>勝敗（true=勝利）。</summary>
        public bool IsWin => _isWin;

        /// <summary>確定スコア（終了時の砦HP − ペナルティ）。</summary>
        public int Score => _score;

        public void Enter() => _ctx.ShowResult(_isWin, _score);

        public void Tick(float deltaTime)
        {
            if (_ctx.IsRetryRequested)
                _machine.Change(new TitleState(_machine, _ctx));
        }

        public void Exit() { }
    }
}
