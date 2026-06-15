using UnityEngine;

namespace KakigoriTD.Data
{
    /// <summary>
    /// 弾の種別パラメータ（速度・ダメージ）を保持する ScriptableObject。
    /// 調整値は Inspector で編集する（ADR-1：マジックナンバー直書き禁止）。
    /// </summary>
    [CreateAssetMenu(menuName = "KakigoriTD/ProjectileType")]
    public class ProjectileTypeSO : ScriptableObject
    {
        // 初期値は仕様書 §19 パラメータ・テーブルに準拠（麓→山頂の飛翔）。
        [SerializeField] private float _speed = 6f;
        [SerializeField] private int _damage = 5;

        /// <summary>弾の飛翔速度。</summary>
        public float Speed => _speed;

        /// <summary>着弾時に砦へ与えるダメージ。</summary>
        public int Damage => _damage;
    }
}
