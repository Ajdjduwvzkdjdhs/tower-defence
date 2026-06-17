namespace KakigoriTD.Core
{
    public enum SpawnKind { Enemy, Ally, Disguised }

    /// <summary>
    /// 湧き種別（敵／味方／変装）の純ロジック（ADR-8 / Phase3）。
    /// 優先順位: 変装 → 味方 → 敵。種別が無効（chance=0 or hasXxx=false）ならスキップ。
    /// roll は [0,1) の外部注入で決定的にテスト可能にする。
    /// </summary>
    public static class SpawnSelector
    {
        /// <summary>
        /// 湧き種別を選択する。
        /// </summary>
        /// <param name="allyChance">味方出現確率 [0,1]。</param>
        /// <param name="disguiseChance">変装出現確率 [0,1]。</param>
        /// <param name="hasAlly">このウェーブに味方種別が設定されているか。</param>
        /// <param name="hasDisguise">このウェーブに変装種別が設定されているか。</param>
        /// <param name="roll">乱数値 [0,1)。外部から注入（Random.value を渡す）。</param>
        public static SpawnKind Select(float allyChance, float disguiseChance,
                                       bool hasAlly, bool hasDisguise, float roll)
        {
            if (hasDisguise && roll < disguiseChance)
                return SpawnKind.Disguised;

            // 変装の抽選を外れた後、残りの確率帯で味方を抽選する。
            // 単純に allyChance を再判定する（独立抽選）。
            if (hasAlly && roll < allyChance)
                return SpawnKind.Ally;

            return SpawnKind.Enemy;
        }
    }
}
