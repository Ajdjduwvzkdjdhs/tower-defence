namespace KakigoriTD.Core
{
    /// <summary>
    /// 必殺技（ビーム砲）チャージ用の撃破数累積。純ロジック（ADR-8）— UnityEngine 非依存。
    /// <see cref="Add"/> で加算、発動時に <see cref="Reset"/> して次のチャージへ。
    /// 具象は <c>SpecialAttackController</c> が保持し、<see cref="EnemyField.OnEnemyKilled"/> 購読で増やす。
    /// </summary>
    public class KillTally
    {
        private int _total;

        /// <summary>前回リセットからの累積撃破数。</summary>
        public int Total => _total;

        /// <summary>撃破数を加算する（1体撃破ごとに呼ぶ）。</summary>
        public void Add(int count)
        {
            if (count > 0) _total += count;
        }

        /// <summary>累積をリセットする（必殺技発動時）。</summary>
        public void Reset() => _total = 0;
    }
}
