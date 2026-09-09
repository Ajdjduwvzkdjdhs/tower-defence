using UnityEngine;
using UnityEngine.InputSystem;

namespace KakigoriTD.Data
{
    /// <summary>
    /// LiDAR 位置検知（連続座標）入力・雪玉放出の調整値（ADR-1 / docs/phase-preview-feedback.md §3.8）。
    ///
    /// Python は関心円（<c>tools/lidar-tracker/preview.toml</c>）でフィルタした代表点の LiDAR 原点フレーム
    /// 座標[mm]を書く。本 SO は Unity 側の写像（中心オフセット・回転・反転・スケール）と、
    /// 検知点の中心距離 d に連動する撃破クールダウン／角度拡散、キャリブレーションの刻みを持つ。
    ///
    /// 実体アセットは <c>Assets/ScriptableObjects/</c> に置く。GameManager 未割り当て時は
    /// 既定値の一時インスタンスで動く（現場で asset を作って調整）。
    /// </summary>
    [CreateAssetMenu(menuName = "KakigoriTD/LidarPreviewConfig")]
    public class LidarPreviewConfigSO : ScriptableObject
    {
        [Header("ファイル読み・ロスト検知")]
        [SerializeField] private float _pollIntervalSec = 0.05f;
        [SerializeField] private float _lostTimeoutSec = 0.3f;
        [Tooltip("Editor：プロジェクトルート相対の共有ファイル。Python preview.toml の position_file と対。")]
        [SerializeField] private string _positionFileRelative = "tools/lidar-tracker/out/position.txt";
        [Tooltip("ビルド：persistentDataPath 下のファイル名。")]
        [SerializeField] private string _positionFilePersistent = "position.txt";

        [Header("座標写像（mm → ゲームワールド XZ）")]
        [Tooltip("mm → ゲーム単位。円の縁がゲーム r=10（雪崩終端）想定の暫定値 10/300。現場調整。")]
        [SerializeField] private float _scaleUnitsPerMm = 0.03333f;
        [Tooltip("設置時に一度だけ確定。以後固定。dx→X, dy→Z を回す角度[deg]。")]
        [SerializeField] private float _worldRotationDeg = 0f;
        [Tooltip("設置時確定（プロジェクタ鏡像 / モニタ直で切替）。回転後に x を反転。")]
        [SerializeField] private bool _flipX = false;
        [Tooltip("同上。回転後に y(→Z) を反転。")]
        [SerializeField] private bool _flipY = false;
        [Tooltip("山の中心（LiDAR の 270° 方向へ）。Python preview.toml の center_offset_mm と一致させる。")]
        [SerializeField] private Vector2 _centerOffsetMm = new Vector2(0f, 300f);
        [Tooltip("関心円の半径[mm]。d のクランプ上限。Python の radius_mm と一致させる。")]
        [SerializeField] private float _radiusMm = 300f;

        [Header("雪玉ストリーム")]
        [SerializeField] private float _spawnRate = 24f;
        [SerializeField] private float _endRadius = 10f;
        [Tooltip("氷消費の有効/無効。既定 false＝氷は無限（システム側リソース管理を使わない方針）。")]
        [SerializeField] private bool _consumeIce = false;

        [Header("敵スポーン（360度）")]
        [Tooltip("入場開始の絶対半径（山中心から）。外周のこの位置に湧いて内側へ歩く。")]
        [SerializeField] private float _entranceRadius = 11f;
        [Tooltip("静止半径。ここに到達して静止し、発射を始める。同じ半径なので密集すると自然に重なる。")]
        [SerializeField] private float _restRadius = 8f;
        [Tooltip("入場の歩行速度。")]
        [SerializeField] private float _entranceSpeed = 6f;
        [Tooltip("同時存在の上限。")]
        [SerializeField] private int _maxEnemies = 48;

        [Header("撃破（一般化した位置到達）")]
        [Tooltip("雪玉→敵 撃破半径（XZ・ゲーム単位）。")]
        [SerializeField] private float _killRadiusUnits = 0.6f;
        [Tooltip("前列判定の重なり半径。この距離内により古い生存敵がいる敵は「まだ前列でない」として撃破対象から外す。")]
        [SerializeField] private float _overlapRadiusUnits = 0.7f;
        [Tooltip("d=0（中心）での撃破クールダウン秒。中心＝広く弱い。")]
        [SerializeField] private float _cooldownCenterSec = 0.6f;
        [Tooltip("d=radiusMm（外周）での撃破クールダウン秒。外周＝狭く強い。")]
        [SerializeField] private float _cooldownEdgeSec = 0f;

        [Header("拡散（放射方向に対する角度ばらつき）")]
        [Tooltip("d=0（中心）での ±角度[deg]。広い。")]
        [SerializeField] private float _spreadCenterDeg = 25f;
        [Tooltip("d=radiusMm（外周）での ±角度[deg]。狭い。")]
        [SerializeField] private float _spreadEdgeDeg = 3f;

        [Header("キャリブレーション（field_offset）")]
        [Tooltip("長押し中に照準表示＋矢印キーで山中心オフセットを nudge。")]
        [SerializeField] private Key _fieldOffsetKey = Key.C;
        [Tooltip("矢印キー 1 押しの移動量[mm]。")]
        [SerializeField] private float _fieldOffsetStepMm = 10f;

        public float PollIntervalSec => _pollIntervalSec;
        public float LostTimeoutSec => _lostTimeoutSec;
        public string PositionFileRelative => _positionFileRelative;
        public string PositionFilePersistent => _positionFilePersistent;
        public float ScaleUnitsPerMm => _scaleUnitsPerMm;
        public float WorldRotationDeg => _worldRotationDeg;
        public bool FlipX => _flipX;
        public bool FlipY => _flipY;
        public Vector2 CenterOffsetMm => _centerOffsetMm;
        public float RadiusMm => _radiusMm;
        public float SpawnRate => _spawnRate;
        public float EndRadius => _endRadius;
        public bool ConsumeIce => _consumeIce;
        public float EntranceRadius => _entranceRadius;
        public float RestRadius => _restRadius;
        public float EntranceSpeed => _entranceSpeed;
        public int MaxEnemies => _maxEnemies;
        public float KillRadiusUnits => _killRadiusUnits;
        public float OverlapRadiusUnits => _overlapRadiusUnits;
        public float CooldownCenterSec => _cooldownCenterSec;
        public float CooldownEdgeSec => _cooldownEdgeSec;
        public float SpreadCenterDeg => _spreadCenterDeg;
        public float SpreadEdgeDeg => _spreadEdgeDeg;
        public Key FieldOffsetKey => _fieldOffsetKey;
        public float FieldOffsetStepMm => _fieldOffsetStepMm;

        /// <summary>検知点の中心距離 d[mm]（0..RadiusMm にクランプ済み）を 0..1 の正規化係数へ。</summary>
        public float NormalizedDistance(float dMm)
        {
            if (_radiusMm <= 0f) return 0f;
            return Mathf.Clamp01(dMm / _radiusMm);
        }

        /// <summary>撃破クールダウン秒を d[mm] から線形補間（中心 0.6 → 外周 0.0）。</summary>
        public float CooldownSecFor(float dMm)
            => Mathf.Lerp(_cooldownCenterSec, _cooldownEdgeSec, NormalizedDistance(dMm));

        /// <summary>拡散の ±角度[deg] を d[mm] から線形補間（中心 25 → 外周 3）。</summary>
        public float SpreadDegFor(float dMm)
            => Mathf.Lerp(_spreadCenterDeg, _spreadEdgeDeg, NormalizedDistance(dMm));
    }
}
