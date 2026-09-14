namespace KakigoriTD.Core
{
    /// <summary>
    /// 湧きタイミング（純ロジック、ADR-8）。UnityEngine 非依存で EditMode 検証する。
    /// <c>spawnInterval</c> 秒ごとに 1 体湧く（最初の湧きは t=spawnInterval）。
    /// <c>SpawnDirector</c>（MonoBehaviour）が戦闘中だけ <see cref="Tick"/> を呼ぶ。
    /// </summary>
    public class SpawnClock
    {
        private readonly float _interval;
        private float _elapsed;
        private int _emitted;

        /// <param name="spawnInterval">湧き間隔（秒）。極小値に下限を設けて 0 除算を防ぐ。</param>
        public SpawnClock(float spawnInterval)
        {
            _interval = spawnInterval < 1e-4f ? 1e-4f : spawnInterval;
        }

        /// <summary>このフレームで湧かせる体数（0 以上）を返す。フレーム落ち時は累計で追いつく。</summary>
        public int Tick(float dt)
        {
            if (dt > 0f) _elapsed += dt;
            int total = (int)(_elapsed / _interval); // 最初の湧きは t=interval
            int n = total - _emitted;
            _emitted = total;
            return n < 0 ? 0 : n;
        }

        /// <summary>進行をリセット（戦闘の開始/停止時）。</summary>
        public void Reset()
        {
            _elapsed = 0f;
            _emitted = 0;
        }
    }
}
