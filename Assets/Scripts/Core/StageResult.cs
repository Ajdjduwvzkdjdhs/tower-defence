namespace KakigoriTD.Core
{
    /// <summary>
    /// ステージの確定結果（ADR-30）。旧 <c>Scoring</c>（残砦HP − ペナルティ）の置き換え。
    /// 純ロジック（ADR-8）— UnityEngine 非依存。
    ///
    /// ・クリア … スコアは <see cref="ClearTimeSec"/>（短いほど上位・ランキング対象）
    /// ・失敗 … スコアは <see cref="KillCount"/>（「62体で力尽きた」）。ランキング対象外
    ///
    /// 砦HP はスコアに含めない（生存リソースに純化し、削られてもタイムを縮めれば勝てるようにする）。
    /// </summary>
    public readonly struct StageResult
    {
        public StageResult(bool isWin, float clearTimeSec, int killCount, int targetKillCount, int maxCombo)
        {
            IsWin = isWin;
            ClearTimeSec = clearTimeSec;
            KillCount = killCount;
            TargetKillCount = targetKillCount;
            MaxCombo = maxCombo;
        }

        /// <summary>クリアしたか。</summary>
        public bool IsWin { get; }

        /// <summary>開始から終了までの経過秒。クリア時はこれがスコア。</summary>
        public float ClearTimeSec { get; }

        /// <summary>終了時点の撃破数。失敗時はこれがスコア。</summary>
        public int KillCount { get; }

        /// <summary>クリアに必要だった撃破数（「62 / 100」表示用）。</summary>
        public int TargetKillCount { get; }

        /// <summary>このプレイの最大コンボ（表示のみ・順位には使わない）。</summary>
        public int MaxCombo { get; }

        /// <summary>結果の見出し文（クリア＝タイム（秒単位） / 失敗＝到達撃破数）。</summary>
        public string Headline => HeadlineFor(showTenths: false);

        /// <summary>結果の見出し文。<paramref name="showTenths"/> ならクリアタイムを 0.1 秒まで出す（ADR-36・Inspector で切替）。</summary>
        public string HeadlineFor(bool showTenths) => IsWin
            ? $"{FormatTime(ClearTimeSec, showTenths)} でクリア！"
            : $"{KillCount} 体で力尽きた";

        /// <summary>
        /// 経過秒を「1分42秒」形式にする。<paramref name="showTenths"/> なら「1分42.3秒」と 0.1 秒まで出す
        /// （ランキングは float で順位付けしているのに表示が秒単位だと同タイムに見えるため・ADR-36）。
        /// </summary>
        public static string FormatTime(float sec, bool showTenths = false)
        {
            if (sec < 0f) sec = 0f;
            int tenths = (int)(sec * 10f); // 切り捨て（59.99 → 59.9 秒。四捨五入で 60.0 秒になり分が繰り上がらないのを避ける）
            int m = tenths / 600;
            int s = tenths / 10 % 60;
            if (!showTenths) return m > 0 ? $"{m}分{s:00}秒" : $"{s}秒";
            int t = tenths % 10;
            return m > 0 ? $"{m}分{s:00}.{t}秒" : $"{s}.{t}秒";
        }
    }
}
