namespace KakigoriTD.Core
{
    /// <summary>
    /// ウェーブ戦闘状態。Enter で当該ウェーブの戦闘を開始し、制限時間をカウントする。
    /// ・砦HP0 → 残敵消去 → Result(Lose)
    /// ・時間切れ → 残敵消去 → 最終ウェーブなら Result(Win) / それ以外は WaveLull
    /// 純ロジック（ADR-8）。スコアは <see cref="Scoring"/> で算出。
    /// </summary>
    public class WavePlayingState : IGameState
    {
        private readonly GameStateMachine _machine;
        private readonly IGameContext _ctx;
        private readonly int _waveIndex;
        private float _elapsed;

        public WavePlayingState(GameStateMachine machine, IGameContext ctx, int waveIndex)
        {
            _machine = machine;
            _ctx = ctx;
            _waveIndex = waveIndex;
        }

        public void Enter()
        {
            _elapsed = 0f;
            _ctx.BeginWave(_waveIndex);
        }

        public void Tick(float deltaTime)
        {
            // 敗北は時間切れより優先（着弾で砦HPが先に 0 になる場合）。
            if (_ctx.IsFortDestroyed)
            {
                _ctx.ClearRemainingEnemies();
                _machine.Change(new ResultState(_machine, _ctx, isWin: false, Scoring.Compute(_ctx.FortHp)));
                return;
            }

            _elapsed += deltaTime;
            if (_elapsed < _ctx.GetWaveDuration(_waveIndex)) return;

            _ctx.ClearRemainingEnemies();

            bool isFinalWave = _waveIndex >= _ctx.WaveCount - 1;
            if (isFinalWave)
                _machine.Change(new ResultState(_machine, _ctx, isWin: true, Scoring.Compute(_ctx.FortHp)));
            else
                _machine.Change(new WaveLullState(_machine, _ctx, _waveIndex));
        }

        public void Exit() { }
    }
}
