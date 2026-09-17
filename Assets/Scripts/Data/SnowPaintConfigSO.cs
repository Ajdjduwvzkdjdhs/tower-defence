using UnityEngine;

namespace KakigoriTD.Data
{
    /// <summary>
    /// 「雪崩が通った扇形を一定時間白く塗る」演出の調整値（ADR-1）。
    /// 山メッシュに重ねたオーバーレイ（<c>Custom/SnowPaintOverlay</c>）と
    /// <see cref="KakigoriTD.Gameplay.SnowPaintTrail"/> が参照する。
    ///
    /// 山中心（XZ原点）からの方位角で 360/SectorDeg 個の扇形に分割し、雪崩が通った
    /// 扇形をまるごと白く塗る（敵の360度スポーン角度刻みと揃える想定）。
    ///
    /// GameManager 未割り当て時は既定値の一時インスタンスで動く。
    /// </summary>
    [CreateAssetMenu(menuName = "KakigoriTD/SnowPaintConfig")]
    public class SnowPaintConfigSO : ScriptableObject
    {
        [Tooltip("塗る色（アルファ＝最大不透明度。透明感を出すため低め推奨）。")]
        [SerializeField] private Color _paintColor = new Color(1f, 1f, 1f, 0.55f);
        [Tooltip("塗られてから消えるまでの秒数（＝「一定時間」の実体）。")]
        [SerializeField] private float _paintFadeSec = 2f;
        [Tooltip("扇形1個分の角度[deg]。360をこの値で割った数の扇形に分割する（既定15度=24分割）。")]
        [SerializeField] private float _sectorDeg = 15f;
        [Tooltip("山の裾までの目安半径[ワールド単位]。これを超えた範囲は塗らない（円形にクリップ）。"
               + "LidarPreviewConfigSO.EndRadius と揃えるとよい。")]
        [SerializeField] private float _maxRadius = 10f;

        public Color PaintColor => _paintColor;
        public float PaintFadeSec => _paintFadeSec;
        public float SectorDeg => _sectorDeg;
        public float MaxRadius => _maxRadius;
    }
}
