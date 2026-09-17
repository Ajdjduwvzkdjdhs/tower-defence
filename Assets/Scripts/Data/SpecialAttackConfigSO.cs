using UnityEngine;

namespace KakigoriTD.Data
{
    /// <summary>
    /// 必殺技「ビーム砲」の**演出**の調整値。発動条件（ゲージの溜まり方）は <see cref="StageConfigSO"/> 側にあり、
    /// ここは発動してからの見た目だけを持つ：山頂にビーム砲モデルが出現し、紫の光線が敵の輪を一周、
    /// 少し遅れて衝撃波が走って盤面の敵をまとめて撃破する（<c>SpecialAttackController</c> が駆動）。
    /// 調整値は Inspector で編集する（ADR-1：マジックナンバー直書き禁止）。
    /// </summary>
    [CreateAssetMenu(menuName = "KakigoriTD/SpecialAttackConfig")]
    public class SpecialAttackConfigSO : ScriptableObject
    {
        [Header("ビーム演出（紫の光線が敵の輪を一周する）")]
        [SerializeField] private float _beamSweepDuration = 0.85f;
        [SerializeField] private Color _beamColor = new Color(0.85f, 0.15f, 0.9f, 0.9f);
        [Tooltip("入場開始の半径（EnemyField.EntranceRadius）にこの値を足したものを光線・衝撃波の半径とする。"
               + "衝撃波は盤面の敵を全員（入場中も含む）倒すので、見た目もそこまで届かせる。")]
        [SerializeField] private float _ringRadiusPadding = 1.0f;
        [Tooltip("リングの帯の太さ（内径〜外径の幅）。")]
        [SerializeField] private float _beamBandWidth = 0.8f;

        [Header("衝撃波演出（ビーム一周後、少し遅れて発生し敵を撃破する）")]
        [SerializeField] private float _shockwaveDelay = 0.35f;

        [Header("配置（山頂・Fort からのオフセット）")]
        [SerializeField] private Vector3 _cannonOffset = new Vector3(0f, 1.5f, -2.5f);

        /// <summary>ビームが敵の静止リングを一周するのにかかる秒数。</summary>
        public float BeamSweepDuration => _beamSweepDuration;

        /// <summary>ビームの色（紫）。</summary>
        public Color BeamColor => _beamColor;

        /// <summary>EnemyField.EntranceRadius に足す光線・衝撃波の半径のマージン。</summary>
        public float RingRadiusPadding => _ringRadiusPadding;

        /// <summary>ビームのリング帯の太さ。</summary>
        public float BeamBandWidth => _beamBandWidth;

        /// <summary>ビーム一周後、衝撃波が発生するまでの遅延秒数。</summary>
        public float ShockwaveDelay => _shockwaveDelay;

        /// <summary>ビーム砲モデルを置く、Fort からのオフセット。</summary>
        public Vector3 CannonOffset => _cannonOffset;
    }
}
