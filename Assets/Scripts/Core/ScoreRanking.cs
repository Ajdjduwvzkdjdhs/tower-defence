using System.Collections.Generic;

namespace KakigoriTD.Core
{
    /// <summary>
    /// クリアタイムのランキング（ADR-34）。純ロジック（ADR-8）— 保存先は <see cref="IScoreStore"/> が担う。
    ///
    /// ・順位はクリアタイム昇順。同タイムは先に記録した方が上（後から並んでも追い抜けない）
    /// ・保持件数は <see cref="Capacity"/> まで。溢れた記録は末尾から落ち、落ちた記録は「ランク外」
    /// ・失敗したプレイは記録しない（<see cref="TryRecord"/> が 0 を返す）
    /// </summary>
    public sealed class ScoreRanking
    {
        /// <summary><see cref="Insert"/> / <see cref="TryRecord"/> が「ランク外・未記録」のときに返す順位。</summary>
        public const int Unranked = 0;

        private readonly List<ScoreEntry> _entries = new List<ScoreEntry>();

        public ScoreRanking(int capacity)
        {
            Capacity = capacity < 1 ? 1 : capacity;
        }

        /// <summary>保持する最大件数（1 以上）。</summary>
        public int Capacity { get; }

        /// <summary>上位から順に並んだ記録。</summary>
        public IReadOnlyList<ScoreEntry> Entries => _entries;

        public int Count => _entries.Count;

        /// <summary>
        /// 保存済みの記録から組み立てる。入力の並びは問わない（ここで整列する）。
        /// 同タイムは <see cref="ScoreEntry.RecordedAtUnixSec"/> が小さい方を上にする。
        /// </summary>
        public static ScoreRanking FromEntries(IEnumerable<ScoreEntry> entries, int capacity)
        {
            var ranking = new ScoreRanking(capacity);
            if (entries == null) return ranking;

            var sorted = new List<ScoreEntry>(entries);
            // List.Sort は不安定なので、記録時刻まで比較キーに含めて決定的にする。
            sorted.Sort((a, b) =>
            {
                int byTime = a.ClearTimeSec.CompareTo(b.ClearTimeSec);
                return byTime != 0 ? byTime : a.RecordedAtUnixSec.CompareTo(b.RecordedAtUnixSec);
            });
            foreach (var e in sorted)
            {
                if (ranking._entries.Count >= ranking.Capacity) break;
                ranking._entries.Add(e);
            }
            return ranking;
        }

        /// <summary>
        /// 記録を挿入し、1 始まりの順位を返す。容量に入らなければ <see cref="Unranked"/>（保持しない）。
        /// </summary>
        public int Insert(ScoreEntry entry)
        {
            int index = 0;
            while (index < _entries.Count && _entries[index].ClearTimeSec <= entry.ClearTimeSec) index++;
            if (index >= Capacity) return Unranked;

            _entries.Insert(index, entry);
            if (_entries.Count > Capacity) _entries.RemoveAt(_entries.Count - 1);
            return index + 1;
        }

        /// <summary>
        /// プレイ結果を記録する。クリアしていなければ何もせず <see cref="Unranked"/>。
        /// </summary>
        public int TryRecord(StageResult result, long recordedAtUnixSec)
        {
            var entry = ScoreEntry.FromResult(result, recordedAtUnixSec);
            return entry.HasValue ? Insert(entry.Value) : Unranked;
        }

        /// <summary>上位 <paramref name="count"/> 件を返す（表示用）。</summary>
        public List<ScoreEntry> Top(int count)
        {
            if (count < 0) count = 0;
            if (count > _entries.Count) count = _entries.Count;
            return _entries.GetRange(0, count);
        }
    }
}
