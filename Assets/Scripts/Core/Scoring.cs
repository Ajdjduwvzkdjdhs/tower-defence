namespace KakigoriTD.Core
{
    /// <summary>
    /// 終了時スコア算出。純ロジック（ADR-8）— UnityEngine 非依存。
    /// Phase1 はスコア＝残砦HP（penalty=0）。
    /// Phase2 以降は味方巻き込みペナルティを <paramref name="penalty"/> で差し引く拡張点。
    /// </summary>
    public static class Scoring
    {
        /// <summary>
        /// スコア＝残砦HP − ペナルティ。0 未満は 0 に丸める。
        /// </summary>
        /// <param name="fortHp">終了時の砦HP（<c>Fort.CurrentHp</c>）。</param>
        /// <param name="penalty">味方巻き込み等の減点合計（Phase1 は 0）。</param>
        public static int Compute(int fortHp, int penalty = 0)
        {
            int score = fortHp - penalty;
            return score < 0 ? 0 : score;
        }
    }
}
