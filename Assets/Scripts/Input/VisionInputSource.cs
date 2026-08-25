using System;
using System.IO;
using UnityEngine;
using KakigoriTD.Data;

namespace KakigoriTD.Input
{
    /// <summary>
    /// 位置検知ファイル（Python が書く <c>seq,channel</c> / LiDAR 版は <c>seq,channel,shaving</c>）を
    /// ポーリングして <see cref="IInputSource.ActiveChannel"/>・<see cref="IsShaving"/> に変換する
    /// 薄い MonoBehaviour アダプタ（ADR-6/8/24）。谷選択と削りを担当し、決定は担当しない
    /// （<see cref="ConfirmPressed"/>=false）。<see cref="CompositeInputSource"/> 側で
    /// 谷選択は <see cref="InputMode"/> による切替、削りはモード非依存の論理和として合成される。
    ///
    /// パースと watchdog・ライブ/ラッチ・デバウンスは純ロジック <see cref="VisionInputProtocol"/> に委譲し
    /// （<see cref="SerialInputSource"/>↔<see cref="SerialInputProtocol"/> と対称）、本クラスは IO（間隔ポーリングの
    /// ファイル読み）とパス解決だけを持つ。物理・シーン参照なし。
    /// </summary>
    public class VisionInputSource : MonoBehaviour, IInputSource
    {
        private VisionInputProtocol _protocol;
        private string _path;
        private float _pollInterval;
        private float _pollTimer;

        /// <summary>
        /// カメラの左右反転（ミラー）。true で W(1)↔E(3) を入れ替える（N/S/-1 は不変）。
        /// 画像を水平ミラーすることは谷判定上この入れ替えと等価なので、Python を触らず
        /// Unity 側（GameManager Inspector）でライブ切替できる。出力段の純粋な remap のため
        /// <see cref="VisionInputProtocol"/>（ラッチ/デバウンス/watchdog）には影響しない。
        /// </summary>
        public bool FlipHorizontal { get; set; }

        /// <summary>
        /// カメラの上下反転（ミラー）。true で N(0)↔S(2) を入れ替える（W/E/-1 は不変）。
        /// <see cref="FlipHorizontal"/> と同じく、画像を垂直ミラーすることと谷判定上等価な
        /// 出力段の純粋な remap で、両者は独立に組み合わせられる（両 ON = 180°回転）。
        /// </summary>
        public bool FlipVertical { get; set; }

        /// <inheritdoc/>
        public int ActiveChannel => MaybeFlip(_protocol?.ActiveChannel ?? -1);

        // 反転時に W(1)↔E(3)（左右）・N(0)↔S(2)（上下）を入れ替える involution。-1 は常に不変。
        private int MaybeFlip(int ch)
        {
            if (FlipHorizontal)
            {
                if (ch == 1) ch = 3;
                else if (ch == 3) ch = 1;
            }
            if (FlipVertical)
            {
                if (ch == 0) ch = 2;
                else if (ch == 2) ch = 0;
            }
            return ch;
        }

        /// <summary>
        /// 削り中か。LiDAR 版（<c>tools/lidar-tracker/valley_tracker.py</c>）が 3 列目に書く
        /// <c>shaving</c> をそのまま素通しする。取りこぼしを埋めるホールドは Python 側が
        /// 当てた後なので、ここでは均さない。旧 2 列契約のファイル（position-tracker）では
        /// 常に false になり、削りは keyboard / serial が担う従来どおりの構成で動く。
        /// </summary>
        public bool IsShaving => _protocol?.IsShaving ?? false;

        /// <inheritdoc/>
        public bool ConfirmPressed => false;

        /// <summary>ロスト（Python 停止/ハング）中か。UI 警告（状態Y）用。</summary>
        public bool IsLost => _protocol?.IsLost ?? true;

        /// <summary>解決済みの共有ファイルの絶対パス（デバッグ表示用）。</summary>
        public string ResolvedPath => _path;

        /// <summary>Composition Root から設定を注入し、パスを解決して protocol を用意する。</summary>
        public void Configure(VisionConfigSO config)
        {
            _pollInterval = Mathf.Max(0f, config.PollIntervalSec);
            _path = ResolvePath(config);
            _protocol = new VisionInputProtocol(config.LostTimeoutSec, config.Latched, config.DebounceSec);
            Debug.Log($"[Vision] position file = {_path}");
        }

        private void Update()
        {
            if (_protocol == null) return;

            // 間隔ポーリング（毎フレーム読みは不要）。読めた最終行を protocol へ流す。
            _pollTimer += Time.deltaTime;
            if (_pollTimer >= _pollInterval)
            {
                _pollTimer = 0f;
                string line = TryReadLastLine();
                if (line != null) _protocol.Ingest(line);
            }

            // watchdog・ラッチ・デバウンスは毎フレーム進める（ロスト検知は時間ベース）。
            _protocol.Tick(Time.deltaTime);
        }

        // 共有ファイルの最終非空行を返す。未生成・ロック等は null（今回のポーリングをスキップ）。
        // Python は temp→os.replace の原子的 rename で書くため、読めた行は常に完全（partial なし）。
        private string TryReadLastLine()
        {
            try
            {
                if (string.IsNullOrEmpty(_path) || !File.Exists(_path)) return null;
                string text = File.ReadAllText(_path);
                if (string.IsNullOrEmpty(text)) return null;
                string[] lines = text.Split('\n');
                for (int i = lines.Length - 1; i >= 0; i--)
                {
                    string s = lines[i].Trim();
                    if (s.Length > 0) return s;
                }
                return null;
            }
            catch (IOException) { return null; }        // 書き込みと競合した瞬間など
            catch (UnauthorizedAccessException) { return null; }
        }

        // パス解決順：env KAKIGORI_POSITION_FILE → config.json の positionFile →
        // Editor はプロジェクトルート相対 / ビルドは persistentDataPath（docs/phase5.md §8）。
        private static string ResolvePath(VisionConfigSO config)
        {
            string env = Environment.GetEnvironmentVariable("KAKIGORI_POSITION_FILE");
            if (!string.IsNullOrEmpty(env)) return env;

            string fromConfig = TryReadConfigJson(config.ConfigFileName);
            if (!string.IsNullOrEmpty(fromConfig)) return fromConfig;

            if (Application.isEditor)
            {
                string root = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
                return Path.GetFullPath(Path.Combine(root, config.RelativePath));
            }
            return Path.Combine(Application.persistentDataPath, config.PersistentFileName);
        }

        // config.json を実行ファイル隣接・作業ディレクトリ・persistentDataPath から探し positionFile を返す。
        private static string TryReadConfigJson(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return null;
            foreach (string dir in ConfigSearchDirs())
            {
                try
                {
                    string p = Path.Combine(dir, fileName);
                    if (!File.Exists(p)) continue;
                    var cfg = JsonUtility.FromJson<PositionConfig>(File.ReadAllText(p));
                    if (cfg != null && !string.IsNullOrEmpty(cfg.positionFile)) return cfg.positionFile;
                }
                catch (Exception) { /* 壊れた config は無視して次の候補へ */ }
            }
            return null;
        }

        private static System.Collections.Generic.IEnumerable<string> ConfigSearchDirs()
        {
            // ビルド：<App>.app の隣（Data の 2 つ上）→ 作業ディレクトリ → persistentDataPath。
            var parent = Directory.GetParent(Application.dataPath);
            if (parent?.Parent != null) yield return parent.Parent.FullName;
            if (parent != null) yield return parent.FullName;
            yield return Directory.GetCurrentDirectory();
            yield return Application.persistentDataPath;
        }

        [Serializable]
        private class PositionConfig
        {
            public string positionFile;
        }
    }
}
