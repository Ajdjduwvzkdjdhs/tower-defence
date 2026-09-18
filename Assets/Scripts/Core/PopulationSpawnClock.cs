namespace KakigoriTD.Core
{
    /// <summary>
    /// 「盤面の敵数を目標人数に保つ」湧きタイミング（純ロジック、ADR-8 / ADR-36）。UnityEngine 非依存で EditMode 検証する。
    ///
    /// <see cref="SpawnClock"/>（一定間隔で 1 体）は「湧いた分しか倒せない」ため、クリアタイムの下限が
    /// <c>目標撃破数 ÷ 湧きレート</c> に固定され、その速さで倒せる人は全員ほぼ同タイムになっていた。
    /// 本クロックは**倒した分だけ即座に補充する**ので、倒す速さがそのままクリアタイムに反映される。
    ///
    ///  - 盤面の生存数が <c>desiredCount</c> を下回っている間だけ、<c>refillRatePerSecond</c>（体/秒）の上限で補充する。
    ///  - 補充の予算は不足分を超えて貯めない（満員が長く続いた直後に一気に吐き出さない）。
    ///  - 目標人数は呼び出し側（<c>SpawnDirector</c>）が進捗に応じて与える。
    /// </summary>
    public class PopulationSpawnClock
    {
        private readonly float _refillRate;
        private float _budget;

        /// <param name="refillRatePerSecond">補充の上限レート（体/秒）。極小値に下限を設けて停止を防ぐ。</param>
        public PopulationSpawnClock(float refillRatePerSecond)
        {
            _refillRate = refillRatePerSecond < 1e-4f ? 1e-4f : refillRatePerSecond;
        }

        /// <summary>補充の上限レート（体/秒）。</summary>
        public float RefillRate => _refillRate;

        /// <summary>
        /// このフレームで湧かせる体数（0 以上）を返す。
        /// 不足分（<paramref name="desiredCount"/> − <paramref name="aliveCount"/>）が無ければ 0。
        /// </summary>
        /// <param name="dt">経過秒。</param>
        /// <param name="aliveCount">盤面の生存敵数。</param>
        /// <param name="desiredCount">維持したい敵数。</param>
        public int Tick(float dt, int aliveCount, int desiredCount)
        {
            int deficit = desiredCount - aliveCount;
            if (deficit <= 0)
            {
                _budget = 0f;
                return 0;
            }

            if (dt > 0f) _budget += _refillRate * dt;
            if (_budget > deficit) _budget = deficit;

            int n = (int)_budget;
            _budget -= n;
            return n;
        }

        /// <summary>進行をリセット（戦闘の開始/停止時）。</summary>
        public void Reset() => _budget = 0f;
    }
}
