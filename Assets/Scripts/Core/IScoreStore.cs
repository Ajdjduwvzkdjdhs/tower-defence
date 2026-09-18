using System.Collections.Generic;

namespace KakigoriTD.Core
{
    /// <summary>
    /// ランキングの永続化先（ADR-34）。実装はローカルの DB ファイル
    /// （<c>Persistence/JsonFileScoreStore</c>）。Core は保存形式を知らない。
    /// </summary>
    public interface IScoreStore
    {
        /// <summary>保存済みの記録を読む。無ければ空。壊れていても例外にせず空（実装側で警告）。</summary>
        IReadOnlyList<ScoreEntry> Load();

        /// <summary>記録一式を保存する（全置換）。</summary>
        void Save(IReadOnlyList<ScoreEntry> entries);

        /// <summary>保存済みの記録をすべて消す（展示のリセット用）。無ければ何もしない。</summary>
        void Clear();
    }
}
