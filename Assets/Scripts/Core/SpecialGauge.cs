namespace KakigoriTD.Core
{
    /// <summary>
    /// 必殺ビーム砲のゲージ。純ロジック（ADR-8）— UnityEngine 非依存。
    ///
    /// 充填量＝<c>PerKill × (1 + ComboBonus × (combo - 1))</c>。
    /// **撃破数がメイン・コンボはボーナス**という設計（ADR-30）。<see cref="Max"/> に達したら
    /// <see cref="IsFull"/> が true になり、呼び出し側が自動発動 → <see cref="Consume"/> で 0 に戻す。
    ///
    /// ビーム撃破は本クラスに通知しない（自己充填ループの防止）。
    /// </summary>
    public class SpecialGauge
    {
        /// <param name="max">MAX となる充填量。0 以下は極小値に丸める。</param>
        /// <param name="perKill">撃破 1 体あたりの基礎充填量。</param>
        /// <param name="comboBonus">コンボ 1 あたりの上乗せ係数（0 ならコンボ無関係）。</param>
        public SpecialGauge(float max, float perKill, float comboBonus)
        {
            Max = max <= 0f ? 1e-4f : max;
            PerKill = perKill < 0f ? 0f : perKill;
            ComboBonus = comboBonus < 0f ? 0f : comboBonus;
        }

        /// <summary>MAX となる充填量。</summary>
        public float Max { get; }

        /// <summary>撃破 1 体あたりの基礎充填量。</summary>
        public float PerKill { get; }

        /// <summary>コンボ 1 あたりの上乗せ係数。</summary>
        public float ComboBonus { get; }

        /// <summary>現在の充填量（0..Max）。</summary>
        public float Value { get; private set; }

        /// <summary>充填率 0..1（UI バー用）。</summary>
        public float Normalized => Value / Max;

        /// <summary>MAX に達しているか（＝ビーム自動発動の条件）。</summary>
        public bool IsFull => Value >= Max;

        /// <summary>このプレイでビームを撃った回数（リザルト・検証用）。</summary>
        public int FireCount { get; private set; }

        /// <summary>
        /// 撃破 1 体分を充填する。<paramref name="combo"/> は撃破時点のコンボ数（1 以上）。
        /// Max を超える分は切り捨てる。
        /// </summary>
        public void AddKill(int combo)
        {
            int c = combo < 1 ? 1 : combo;
            Value += PerKill * (1f + ComboBonus * (c - 1));
            if (Value > Max) Value = Max;
        }

        /// <summary>MAX を消費してビームを撃つ。MAX 未満なら何もせず false。</summary>
        public bool Consume()
        {
            if (!IsFull) return false;
            Value = 0f;
            FireCount++;
            return true;
        }

        /// <summary>プレイ開始時に初期化する。</summary>
        public void Reset()
        {
            Value = 0f;
            FireCount = 0;
        }
    }
}
