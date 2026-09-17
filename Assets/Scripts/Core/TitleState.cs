namespace KakigoriTD.Core
{
    /// <summary>
    /// タイトル状態。開始入力でカウントダウンへ遷移する（ADR-30 で Wave 導入を廃止）。純ロジック（ADR-8）。
    /// </summary>
    public class TitleState : IGameState
    {
        private readonly GameStateMachine _machine;
        private readonly IGameContext _ctx;

        public TitleState(GameStateMachine machine, IGameContext ctx)
        {
            _machine = machine;
            _ctx = ctx;
        }

        public void Enter() { }

        public void Tick(float deltaTime)
        {
            if (_ctx.IsStartRequested)
                _machine.Change(new CountdownState(_machine, _ctx));
        }

        public void Exit() { }
    }
}
