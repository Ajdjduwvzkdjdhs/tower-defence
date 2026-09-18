namespace KakigoriTD.Core
{
    /// <summary>
    /// ランキングに保存する 1 件の記録（ADR-34）。クリアしたプレイだけが対象
    /// （失敗はランキング対象外・ADR-30）。純ロジック（ADR-8）— UnityEngine 非依存。
    /// </summary>
    public readonly struct ScoreEntry
    {
        public ScoreEntry(float clearTimeSec, int killCount, int maxCombo, long recordedAtUnixSec)
        {
            ClearTimeSec = clearTimeSec < 0f ? 0f : clearTimeSec;
            KillCount = killCount;
            MaxCombo = maxCombo;
            RecordedAtUnixSec = recordedAtUnixSec;
        }

        /// <summary>クリアタイム（秒）。短いほど上位。</summary>
        public float ClearTimeSec { get; }

        /// <summary>クリア時の撃破数（表示用）。</summary>
        public int KillCount { get; }

        /// <summary>そのプレイの最大コンボ（表示用・順位には使わない）。</summary>
        public int MaxCombo { get; }

        /// <summary>記録した時刻（Unix 秒・UTC）。同タイムの先着判定と後日の突き合わせ用。</summary>
        public long RecordedAtUnixSec { get; }

        /// <summary>プレイ結果から記録を作る。クリアしていなければ null。</summary>
        public static ScoreEntry? FromResult(StageResult result, long recordedAtUnixSec)
        {
            if (!result.IsWin) return null;
            return new ScoreEntry(result.ClearTimeSec, result.KillCount, result.MaxCombo, recordedAtUnixSec);
        }
    }
}
