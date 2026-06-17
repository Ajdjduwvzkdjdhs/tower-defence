namespace KakigoriTD.Core
{
    /// <summary>
    /// ウェーブ導入状態（「Wave n」バナー）。<c>WaveIntroDuration</c> 経過で当該ウェーブの
    /// 戦闘状態へ遷移する。0 秒なら最初の Tick で即遷移。純ロジック（ADR-8）。
    /// </summary>
    public class WaveIntroState : IGameState
    {
        private readonly GameStateMachine _machine;
        private readonly IGameContext _ctx;
        private readonly int _waveIndex;
        private float _elapsed;

        public WaveIntroState(GameStateMachine machine, IGameContext ctx, int waveIndex)
        {
            _machine = machine;
            _ctx = ctx;
            _waveIndex = waveIndex;
        }

        /// <summary>これから始める波のインデックス（0 始まり）。GameManager がバナー文言に使う。</summary>
        public int WaveIndex => _waveIndex;

        public void Enter() => _elapsed = 0f;

        public void Tick(float deltaTime)
        {
            _elapsed += deltaTime;
            if (_elapsed >= _ctx.WaveIntroDuration)
                _machine.Change(new WavePlayingState(_machine, _ctx, _waveIndex));
        }

        public void Exit() { }
    }
}
