namespace KakigoriTD.Data
{
    /// <summary>
    /// 敵の湧き方式（ADR-36）。<see cref="StageConfigSO.SpawnMode"/> で Inspector から切り替える。
    /// </summary>
    public enum SpawnMode
    {
        /// <summary>
        /// ADR-30 の挙動そのまま。<c>SpawnRatePerSecond</c>（体/秒）の一定間隔で湧く。
        /// 「湧いた分しか倒せない」ため、クリアタイムの下限が 目標撃破数 ÷ 湧きレート に固定される
        /// （その速さで倒せる人は全員ほぼ同タイムになる）。比較・切り戻し用。
        /// </summary>
        FixedRate = 0,

        /// <summary>
        /// 盤面の敵数を目標人数（<c>FieldPopulationStart/End</c> を進捗で補間）に保つ。倒した分だけ
        /// <c>RefillRatePerSecond</c> を上限に即座に補充するので、倒す速さがそのままクリアタイムに反映される。
        /// 盤面の混雑度が腕前に依らず一定なので、ビーム一掃・爆発連鎖の稼ぎも全員同じ条件になる。
        /// </summary>
        Population = 1,
    }
}
