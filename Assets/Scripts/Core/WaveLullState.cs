namespace KakigoriTD.Core
{
    /// <summary>
    /// ウェーブ間の小休止状態（<c>WaveLullDuration</c> 秒）。経過後、次ウェーブの導入へ遷移。
    /// 休止中も飛来中の弾で砦HP0 になりうるため敗北判定を継続する。純ロジック（ADR-8）。
    /// </summary>
    public class WaveLullState : IGameState
    {
        private readonly GameStateMachine _machine;
        private readonly IGameContext _ctx;
        private readonly int _waveIndex;
        private float _elapsed;

        public WaveLullState(GameStateMachine machine, IGameContext ctx, int waveIndex)
        {
            _machine = machine;
            _ctx = ctx;
            _waveIndex = waveIndex;
        }

        /// <summary>クリアした波のインデックス（0 始まり）。GameManager がバナー文言に使う。</summary>
        public int WaveIndex => _waveIndex;

        public void Enter() => _elapsed = 0f;

        public void Tick(float deltaTime)
        {
            if (_ctx.IsFortDestroyed)
            {
                _machine.Change(new ResultState(_machine, _ctx, isWin: false, Scoring.Compute(_ctx.FortHp)));
                return;
            }

            _elapsed += deltaTime;
            if (_elapsed >= _ctx.WaveLullDuration)
                _machine.Change(new WaveIntroState(_machine, _ctx, _waveIndex + 1));
        }

        public void Exit() { }
    }
}
