using UnityEngine;

namespace KakigoriTD.Data
{
    /// <summary>
    /// 敵の種別パラメータ（移動速度・射撃間隔・使用する弾・見た目）を保持する ScriptableObject。
    /// 調整値は Inspector で編集する（ADR-1：マジックナンバー直書き禁止）。
    /// </summary>
    [CreateAssetMenu(menuName = "KakigoriTD/EnemyType")]
    public class EnemyTypeSO : ScriptableObject
    {
        // 初期値は仕様書 §19 パラメータ・テーブルに準拠。
        [SerializeField] private float _moveSpeed = 1f;
        [SerializeField] private float _fireInterval = 2f;
        [SerializeField] private ProjectileTypeSO _projectile;

        // Phase1 では未使用（define-only）。
        // _isAlly は Phase2（味方NPC）、_isDisguised は Phase3（変装）で挙動が付く。
        [SerializeField] private bool _isAlly = false;
        [SerializeField] private bool _isDisguised = false;

        // 赤＝敵 / 青＝味方・変装（見た目の色分け）。
        [SerializeField] private Color _tint = Color.red;

        /// <summary>移動速度。突撃隊は速い。</summary>
        public float MoveSpeed => _moveSpeed;

        /// <summary>弾を発射する間隔（秒）。</summary>
        public float FireInterval => _fireInterval;

        /// <summary>この敵が発射する弾の種別。</summary>
        public ProjectileTypeSO Projectile => _projectile;

        /// <summary>味方NPCかどうか（Phase2 用。Phase1 では未使用）。</summary>
        public bool IsAlly => _isAlly;

        /// <summary>変装中かどうか（Phase3 用。Phase1 では未使用）。</summary>
        public bool IsDisguised => _isDisguised;

        /// <summary>見た目の色（赤＝敵 / 青＝味方・変装）。</summary>
        public Color Tint => _tint;
    }
}
