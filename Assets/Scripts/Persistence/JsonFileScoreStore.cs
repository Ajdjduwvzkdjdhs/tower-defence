using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using KakigoriTD.Core;

namespace KakigoriTD.Persistence
{
    /// <summary>
    /// ランキングをローカルの JSON ファイルに保存する <see cref="IScoreStore"/>（ADR-34）。
    /// 既定パスは <c>persistentDataPath/ranking.json</c>（<see cref="ResolvePath"/>）。
    ///
    /// ・書き込みは一時ファイル → 置換（電源断・強制終了で半端な JSON が残らないように）
    /// ・読み込みで壊れていたら <c>.corrupt-&lt;時刻&gt;</c> に退避して空から始める（記録を黙って上書きしない）
    /// ・Core は UnityEngine 非依存なので、<see cref="JsonUtility"/> 用の DTO はこのクラスに閉じる
    /// </summary>
    public sealed class JsonFileScoreStore : IScoreStore
    {
        /// <summary>保存先を差し替える環境変数（展示 PC で記録ファイルを USB 等に置きたいとき）。</summary>
        public const string PathEnvVar = "KAKIGORI_RANKING_FILE";

        /// <summary>既定のファイル名（<c>persistentDataPath</c> 直下）。</summary>
        public const string DefaultFileName = "ranking.json";

        private const int FormatVersion = 1;

        public JsonFileScoreStore(string path)
        {
            if (string.IsNullOrEmpty(path)) throw new ArgumentException("path is empty", nameof(path));
            Path = path;
        }

        /// <summary>実際に読み書きするファイルのフルパス。</summary>
        public string Path { get; }

        /// <summary>
        /// 保存先の解決: 環境変数 <see cref="PathEnvVar"/> → <paramref name="persistentDataPath"/>/<paramref name="fileName"/>。
        /// </summary>
        public static string ResolvePath(string persistentDataPath, string fileName)
        {
            var env = Environment.GetEnvironmentVariable(PathEnvVar);
            if (!string.IsNullOrEmpty(env)) return env;
            return System.IO.Path.Combine(persistentDataPath, fileName);
        }

        public IReadOnlyList<ScoreEntry> Load()
        {
            if (!File.Exists(Path)) return Array.Empty<ScoreEntry>();

            string json;
            try
            {
                json = File.ReadAllText(Path);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Ranking] 読み込み失敗。空のランキングで開始: {Path}\n{e.Message}");
                return Array.Empty<ScoreEntry>();
            }

            var table = Parse(json);
            if (table == null)
            {
                QuarantineCorruptFile();
                return Array.Empty<ScoreEntry>();
            }
            return table.ToEntries();
        }

        public void Save(IReadOnlyList<ScoreEntry> entries)
        {
            var json = JsonUtility.ToJson(ScoreTable.From(entries), prettyPrint: true);
            var dir = System.IO.Path.GetDirectoryName(Path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            // 一時ファイルに書き切ってから置換（原子的更新）。
            var tmp = Path + ".tmp";
            File.WriteAllText(tmp, json);
            if (File.Exists(Path)) File.Replace(tmp, Path, null);
            else File.Move(tmp, Path);
        }

        public void Clear()
        {
            if (File.Exists(Path)) File.Delete(Path);
            var tmp = Path + ".tmp";
            if (File.Exists(tmp)) File.Delete(tmp);
        }

        // JsonUtility は壊れた JSON で例外を投げる／null を返す。どちらも「壊れている」に倒す。
        private static ScoreTable Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try
            {
                var table = JsonUtility.FromJson<ScoreTable>(json);
                return table != null && table.IsValid ? table : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private void QuarantineCorruptFile()
        {
            var backup = $"{Path}.corrupt-{DateTime.UtcNow:yyyyMMdd-HHmmss}";
            try
            {
                File.Move(Path, backup);
                Debug.LogWarning($"[Ranking] ファイルが壊れていたため退避して空から開始: {backup}");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Ranking] 壊れたファイルの退避に失敗（上書きされます）: {Path}\n{e.Message}");
            }
        }

        // ---- JsonUtility 用 DTO（public フィールド禁止の鉄則に合わせ [SerializeField] private）----

        [Serializable]
        private sealed class ScoreTable
        {
            [SerializeField] private int version = FormatVersion;
            [SerializeField] private List<ScoreRow> entries = new List<ScoreRow>();

            public bool IsValid => version >= 1 && entries != null;

            public static ScoreTable From(IReadOnlyList<ScoreEntry> src)
            {
                var t = new ScoreTable();
                for (int i = 0; i < src.Count; i++) t.entries.Add(ScoreRow.From(src[i]));
                return t;
            }

            public IReadOnlyList<ScoreEntry> ToEntries()
            {
                var list = new List<ScoreEntry>(entries.Count);
                foreach (var row in entries) if (row != null) list.Add(row.ToEntry());
                return list;
            }
        }

        [Serializable]
        private sealed class ScoreRow
        {
            [SerializeField] private float clearTimeSec;
            [SerializeField] private int killCount;
            [SerializeField] private int maxCombo;
            [SerializeField] private long recordedAtUnixSec;

            public static ScoreRow From(ScoreEntry e) => new ScoreRow
            {
                clearTimeSec = e.ClearTimeSec,
                killCount = e.KillCount,
                maxCombo = e.MaxCombo,
                recordedAtUnixSec = e.RecordedAtUnixSec,
            };

            public ScoreEntry ToEntry() => new ScoreEntry(clearTimeSec, killCount, maxCombo, recordedAtUnixSec);
        }
    }
}
