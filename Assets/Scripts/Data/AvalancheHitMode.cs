namespace KakigoriTD.Data
{
    /// <summary>
    /// 雪崩の当たり判定の方式（ADR-32）。<see cref="LidarPreviewConfigSO.HitMode"/> で Inspector から切り替える。
    /// </summary>
    public enum AvalancheHitMode
    {
        /// <summary>
        /// 分岐点（main・2026-09-18）の挙動そのまま。雪崩 1 個につき見えない判定点が落氷点から外へ 4 u/s で歩き、
        /// 撃破半径内の前列の敵を 1 体だけ倒す（前列ルール・d 連動クールダウン・盾兵は StageConfigSO.ShieldHits 発）。
        /// </summary>
        Legacy = 0,

        /// <summary>
        /// 雪の粒そのものが判定。粒が敵に触れたら粒は吸収されて消え、敵に 1 ダメージ
        /// （耐久は EnemyTypeSO.ParticleHitPoints）。前列が生きている間は雪がそこで止まるので自然に前列から倒れる。
        /// クールダウン・1 雪崩 1 撃の制限はない。静止線より外（敵の上）に落としても雪崩は起きない。
        /// </summary>
        ParticleContact = 1,
    }
}
