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

        /// <summary>結果の見出し文（クリア＝タイム / 失敗＝到達撃破数）。</summary>
        public string Headline => IsWin
            ? $"{FormatTime(ClearTimeSec)} でクリア！"
            : $"{KillCount} 体で力尽きた";

        /// <summary>経過秒を「1分42秒」形式にする。</summary>
        public static string FormatTime(float sec)
        {
            if (sec < 0f) sec = 0f;
            int total = (int)sec;
            int m = total / 60;
            int s = total % 60;
            return m > 0 ? $"{m}分{s:00}秒" : $"{s}秒";
        }
    }
}
