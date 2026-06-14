namespace KakigoriTD.Core
{
    /// <summary>
    /// 有限の氷ストック（削れる秒数）。1プレイ固定・補充なし・溶けない（圧力 i）。
    /// 純ロジック（ADR-8）— UnityEngine 非依存。削り中に毎フレーム消費される。
    /// </summary>
    public class IceStock
    {
        private float _current;

        /// <param name="total">総氷量（削れる秒数）。負値は 0 に丸める。</param>
        public IceStock(float total)
        {
            Total = total < 0f ? 0f : total;
            _current = Total;
        }

        /// <summary>初期総量。</summary>
        public float Total { get; }

        /// <summary>残量（削れる秒数）。</summary>
        public float Current => _current;

        /// <summary>枯渇しているか（これ以上削れない）。</summary>
        public bool IsEmpty => _current <= 0f;

        /// <summary>残量の正規化値 0..1（UI バー用）。Total が 0 なら 0。</summary>
        public float Normalized => Total <= 0f ? 0f : _current / Total;

        /// <summary>
        /// 氷を <paramref name="amount"/> だけ消費しようと試みる。
        /// 残量があれば消費して true（＝この瞬間は削れる）。0 未満には下がらない。
        /// 枯渇していれば false（＝削れない）で残量は変化しない。
        /// </summary>
        public bool TryDrain(float amount)
        {
            if (IsEmpty) return false;
            if (amount < 0f) amount = 0f;
            _current -= amount;
            if (_current < 0f) _current = 0f;
            return true;
        }
    }
}
