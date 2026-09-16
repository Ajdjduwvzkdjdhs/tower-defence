using UnityEngine;

namespace KakigoriTD.Data
{
    /// <summary>
    /// 必殺技「ビーム砲」の調整値。敵を <see cref="KillThreshold"/> 体撃破するとチャージ完了となり、
    /// 山頂のビーム砲モデルが出現して自動発動する。紫の光線が敵の静止リングを一周し、少し遅れて
    /// 衝撃波が走って範囲内の敵をまとめて撃破する（<c>SpecialAttackController</c> が駆動）。
    /// 調整値は Inspector で編集する（ADR-1：マジックナンバー直書き禁止）。
    /// </summary>
    [CreateAssetMenu(menuName = "KakigoriTD/SpecialAttackConfig")]
    public class SpecialAttackConfigSO : ScriptableObject
    {
        [Header("発動条件")]
        [Tooltip("この数だけ敵を倒すとチャージ完了。展示現場で直感的に調整できるよう「体数」で持つ。")]
        [SerializeField] private int _killThreshold = 10;

        [Header("ビーム演出（紫の光線が敵の静止リングを一周する）")]
        [SerializeField] private float _beamSweepDuration = 0.85f;
        [SerializeField] private Color _beamColor = new Color(0.85f, 0.15f, 0.9f, 0.9f);
        [Tooltip("敵の静止半径（EnemyField.RestRadius）にこの値を足したものをリング半径とする。")]
        [SerializeField] private float _ringRadiusPadding = 1.0f;
        [Tooltip("リングの帯の太さ（内径〜外径の幅）。")]
        [SerializeField] private float _beamBandWidth = 0.8f;

        [Header("衝撃波演出（ビーム一周後、少し遅れて発生し敵を撃破する）")]
        [SerializeField] private float _shockwaveDelay = 0.35f;

        [Header("配置（山頂・Fort からのオフセット）")]
        [SerializeField] private Vector3 _cannonOffset = new Vector3(0f, 1.5f, -2.5f);

        /// <summary>この数だけ敵を倒すとチャージ完了。</summary>
        public int KillThreshold => _killThreshold;

        /// <summary>ビームが敵の静止リングを一周するのにかかる秒数。</summary>
        public float BeamSweepDuration => _beamSweepDuration;

        /// <summary>ビームの色（紫）。</summary>
        public Color BeamColor => _beamColor;

        /// <summary>EnemyField.RestRadius に足すリング半径のマージン。</summary>
        public float RingRadiusPadding => _ringRadiusPadding;

        /// <summary>ビームのリング帯の太さ。</summary>
        public float BeamBandWidth => _beamBandWidth;

        /// <summary>ビーム一周後、衝撃波が発生するまでの遅延秒数。</summary>
        public float ShockwaveDelay => _shockwaveDelay;

        /// <summary>ビーム砲モデルを置く、Fort からのオフセット。</summary>
        public Vector3 CannonOffset => _cannonOffset;
    }
}
