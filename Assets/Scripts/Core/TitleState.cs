namespace KakigoriTD.Core
{
    /// <summary>
    /// タイトル状態。開始入力で Wave1（index 0）の導入へ遷移する。純ロジック（ADR-8）。
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
                _machine.Change(new WaveIntroState(_machine, _ctx, 0));
        }

        public void Exit() { }
    }
}
