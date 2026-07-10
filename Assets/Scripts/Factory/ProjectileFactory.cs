using System;
using UnityEngine;
using KakigoriTD.Data;
using KakigoriTD.Gameplay;

namespace KakigoriTD.Factory
{
    /// <summary>
    /// 弾の生成口（ADR-4 / ADR-7）。<see cref="ObjectPool{T}"/> で <see cref="Projectile"/> を使い回し、
    /// 着弾でプールへ返却する。ホットパスで <c>Instantiate</c>/<c>Destroy</c> を呼ばないための要。
    ///
    /// 実体の生成方法（プレハブの Instantiate 等）は Composition Root（GameManager）やテストが
    /// <paramref name="createInstance"/> として注入する。本クラスはプール運用と <see cref="Projectile.Init"/>
    /// 配線にのみ責務を持ち、シーンやプレハブのアセット参照には依存しない（テスト容易性）。
    ///
    /// 将来の貫通弾・拡散弾は弾種（<see cref="ProjectileTypeSO"/>）追加と本 Factory の分岐で拡張できる（ADR-4）。
    /// </summary>
    public class ProjectileFactory
    {
        private readonly ObjectPool<Projectile> _pool;

        // 着弾時にプールへ返す共通デリゲート。Projectile ごとに割り当てを作らないよう 1 個を使い回す。
        private readonly Action<Projectile> _release;

        /// <summary>
        /// 弾が生成された（＝発射された）瞬間に発火する（ADR-26）。全発射の単一チョークポイント。
        /// 発射音（EnemyAttack）を鳴らすための additive な cosmetic イベント。プール運用・ロジックは不変。
        /// </summary>
        public event Action OnCreated;

        /// <param name="createInstance">弾インスタンスを 1 つ生成するデリゲート（プレハブ Instantiate 等）。null 不可。</param>
        /// <param name="prewarm">事前生成数（初回発射時のヒッチ回避）。</param>
        public ProjectileFactory(Func<Projectile> createInstance, int prewarm = 0)
        {
            _pool = new ObjectPool<Projectile>(createInstance, prewarm);
            _release = projectile => _pool.Release(projectile);
        }

        /// <summary>
        /// プールから弾を取得し、麓 <paramref name="from"/> → 砦 <paramref name="to"/> の飛行を開始する。
        /// 着弾で <paramref name="target"/> にダメージを与え、自動的にプールへ返却される。
        /// </summary>
        public Projectile Create(ProjectileTypeSO type, Vector3 from, Vector3 to, Fort target)
        {
            var projectile = _pool.Get();
            projectile.Init(type, from, to, target, _release);
            OnCreated?.Invoke(); // 発射音（EnemyAttack・ADR-26）。cosmetic のみ。
            return projectile;
        }

        /// <summary>プールで待機中（非アクティブ）の弾数。検証用の可観測性。</summary>
        public int CountInactive => _pool.CountInactive;
    }
}
