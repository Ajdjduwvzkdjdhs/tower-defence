namespace KakigoriTD.Core
{
    /// <summary>湧かせる敵の種別（ADR-30）。</summary>
    public enum SpawnKind { Normal, Bomber, Shield }

    /// <summary>
    /// 湧き種別（通常／爆発兵／盾兵）の抽選。純ロジック（ADR-8）。
    /// 優先順位は 爆発兵 → 盾兵 → 通常。確率 0 の種別はスキップされる。
    /// roll は [0,1) の外部注入で決定的にテスト可能にする。
    ///
    /// 出現率は進捗（撃破数 / 目標撃破数）に応じたカーブから <c>SpawnDirector</c> が評価して渡す。
    /// カーブ（<c>AnimationCurve</c>）は UnityEngine 型のため Core には持ち込まない。
    /// </summary>
    public static class SpawnSelector
    {
        /// <param name="bomberChance">爆発兵の出現確率 [0,1]。</param>
        /// <param name="shieldChance">盾兵の出現確率 [0,1]。</param>
        /// <param name="roll">乱数値 [0,1)。外部から注入（<c>Random.value</c> を渡す）。</param>
        public static SpawnKind Select(float bomberChance, float shieldChance, float roll)
        {
            if (roll < bomberChance) return SpawnKind.Bomber;

            // 爆発兵の帯を外した残りで盾兵を抽選する（帯が重ならないよう連続した区間で判定）。
            if (roll < bomberChance + shieldChance) return SpawnKind.Shield;

            return SpawnKind.Normal;
        }
    }
}
