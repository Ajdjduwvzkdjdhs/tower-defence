using UnityEngine;

namespace KakigoriTD.Data
{
    /// <summary>
    /// 位置検知（Vision）入力の調整値を保持する ScriptableObject（ADR-1 / ADR-24 / docs/phase5.md §4）。
    /// 共有ファイルのパス解決素材（相対デフォルト・config.json 名・persistent フォールバック名）と、
    /// ポーリング間隔・ロスト検知しきい値・ライブ/ラッチ・デバウンス秒を集約する。
    ///
    /// パス解決順（<see cref="KakigoriTD.Input.VisionInputSource"/> が実施）:
    ///  1. 環境変数 <c>KAKIGORI_POSITION_FILE</c>（通常未設定・手動運用の上書き保険）
    ///  2. <c>config.json</c> の <c>positionFile</c>（配布物同梱の単一真実）
    ///  3. Editor＝プロジェクトルート相対 <see cref="RelativePath"/> ／ ビルド＝persistentDataPath/<see cref="PersistentFileName"/>
    ///
    /// 実体アセットは <c>Assets/ScriptableObjects/</c> に置き、環境ごとに Inspector で設定する。
    /// </summary>
    [CreateAssetMenu(menuName = "KakigoriTD/VisionConfig")]
    public class VisionConfigSO : ScriptableObject
    {
        [Header("パス解決")]
        [Tooltip("Editor 開発の既定。プロジェクトルート(Assets の親)からの相対パス。")]
        [SerializeField] private string _relativePath = "tools/position-tracker/out/position.txt";
        [Tooltip("配布時に探す設定ファイル名。実行ファイル隣接／作業ディレクトリを探索。")]
        [SerializeField] private string _configFileName = "config.json";
        [Tooltip("ビルドで config.json も env も無いときの persistentDataPath 下のファイル名。")]
        [SerializeField] private string _persistentFileName = "position.txt";

        [Header("ポーリングと fail-safe")]
        [Tooltip("ファイルを読む間隔（秒）。50〜100ms 目安。毎フレーム読みは不要。")]
        [SerializeField] private float _pollIntervalSec = 0.05f;
        [Tooltip("seq がこの秒数を超えて不変ならロスト（Python 停止/ハング）と判定し -1 に倒す。0 以下で無効。")]
        [SerializeField] private float _lostTimeoutSec = 0.3f;

        [Header("チャンネルの意味・ちらつき対策")]
        [Tooltip("ON=ラッチ(B：-1 は直前の谷を保持) / OFF=ライブ(A：現在位置に追従・中心と未検出で -1)。既定は OFF。")]
        [SerializeField] private bool _latched = false;
        [Tooltip("境界ちらつき対策のデバウンス秒（保険）。0＝無効。まず 0 で試し、必要なら当日回す。")]
        [SerializeField] private float _debounceSec = 0f;

        public string RelativePath => _relativePath;
        public string ConfigFileName => _configFileName;
        public string PersistentFileName => _persistentFileName;
        public float PollIntervalSec => _pollIntervalSec;
        public float LostTimeoutSec => _lostTimeoutSec;

        /// <summary>ON＝ラッチ(B) / OFF＝ライブ(A)。</summary>
        public bool Latched => _latched;

        /// <summary>デバウンス秒（0＝無効）。</summary>
        public float DebounceSec => _debounceSec;
    }
}
