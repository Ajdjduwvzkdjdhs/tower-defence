using UnityEngine;

namespace KakigoriTD.Gameplay
{
    /// <summary>
    /// 爆発兵（ADR-30）。<see cref="Enemy"/> を継承し、同じプールの敵リストに多態で保持される。
    /// 撃破されると <see cref="EnemyField"/> が爆発半径内の敵を巻き込み、
    /// 範囲内の爆発兵はさらに誘爆する（連鎖は無制限。制御は出現率と爆発半径のみ）。
    ///
    /// 本クラス自体は「爆発兵である」という型の目印と半径の保持だけを担い、
    /// 連鎖の探索は <c>KakigoriTD.Core.ExplosionChain</c>（純ロジック）が行う。
    /// 発射・入場・撃破の挙動は基底のまま。
    /// </summary>
    public class BombTrooper : Enemy
    {
        private float _explosionRadius = 3f;

        /// <summary>爆発半径（ゲーム単位・XZ）。<c>StageConfigSO.ExplosionRadius</c> を EnemyField が渡す。</summary>
        public float ExplosionRadius => _explosionRadius;

        /// <summary>スポーン時に爆発半径を与える（プール再利用のたびに呼ばれる）。</summary>
        public void SetExplosionRadius(float radius)
        {
            _explosionRadius = radius < 0f ? 0f : radius;
        }
    }
}
