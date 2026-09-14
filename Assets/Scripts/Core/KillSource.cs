namespace KakigoriTD.Core
{
    /// <summary>
    /// 撃破の出どころ。集計ルール（撃破数・コンボ・ゲージ）の分岐キー（ADR-30）。
    ///
    /// ・<see cref="Snowball"/> … 雪玉の直接撃破。全てに算入する。
    /// ・<see cref="Explosion"/> … 爆発兵の爆発・誘爆による撃破。連鎖20体なら20回発火＝コンボも+20。
    /// ・<see cref="Beam"/> … 必殺ビーム砲の一掃。撃破数には算入するが、
    ///   **ゲージには算入しない**（ビーム→大量撃破→ゲージ即MAX の自己充填ループを断つ）。
    ///   コンボは加算も中断もしない（中立）。
    /// </summary>
    public enum KillSource
    {
        Snowball,
        Explosion,
        Beam,
    }
}
