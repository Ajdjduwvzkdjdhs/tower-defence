using UnityEngine;
using KakigoriTD.Data;
using KakigoriTD.Factory;

namespace KakigoriTD.Gameplay
{
    /// <summary>
    /// 麓に湧く敵。<see cref="EnemyTypeSO.FireInterval"/> 秒ごとに <see cref="ProjectileFactory"/> 経由で
    /// 弾を生成し、砦へ発射する（ADR-7）。撃破される（<see cref="Kill"/>）と以後の発射が止まる。
    /// 飛来中の弾は <see cref="Projectile"/> 自身が着弾まで処理するため、敵の死とは独立して着弾する。
    ///
    /// 敵自身も <see cref="ObjectPool{T}"/> で使い回す前提（ChannelController が後続マイルストーンで配線）。
    /// そのため状態は毎回 <see cref="Init"/> でリセットする。MonoBehaviour は薄いアダプタに徹する（ADR-8）。
    /// </summary>
    public class Enemy : MonoBehaviour
    {
        private EnemyTypeSO _type;
        private ProjectileFactory _factory;
        private Fort _target;
        private float _fireTimer;

        // 生存中のみ発射する。撃破後は false（飛来中の弾には影響しない）。
        private bool _alive;

        // per-instance tint: sharedMaterial を汚さずプール再利用で色を切り替える（ADR-16）。
        private Renderer _renderer;
        private MaterialPropertyBlock _mpb;

        /// <summary>生存しているか（撃破されると false）。</summary>
        public bool IsAlive => _alive;

        /// <summary>これまでに発射した弾数（検証用の可観測性）。</summary>
        public int ShotsFired { get; private set; }

        private void Awake()
        {
            _renderer = GetComponent<Renderer>();
            _mpb = new MaterialPropertyBlock();
        }

        /// <summary>
        /// 敵を初期化して稼働させる。プールから取得するたびに呼ぶ。
        /// 最初の発射は <see cref="EnemyTypeSO.FireInterval"/> 秒後（spec「fireInterval 秒ごとに発射」）。
        /// </summary>
        public void Init(EnemyTypeSO type, ProjectileFactory factory, Fort target)
        {
            _type = type;
            _factory = factory;
            _target = target;
            _fireTimer = type != null ? type.FireInterval : 0f;
            ShotsFired = 0;
            _alive = true;
            if (_renderer != null)
            {
                _mpb.SetColor("_BaseColor", type != null ? type.Tint : Color.red);
                _renderer.SetPropertyBlock(_mpb);
            }
            OnInitialized();
        }

        /// <summary>
        /// Init 完了後に呼ばれる仮想フック。派生クラス（例: DisguisedEnemy の tell マーカー生成）で使う。
        /// </summary>
        protected virtual void OnInitialized() { }

        protected virtual void Update()
        {
            if (!_alive) return;

            _fireTimer -= Time.deltaTime;
            if (_fireTimer <= 0f)
            {
                Fire();
                // 取りこぼし分を持ち越す（フレーム落ち時もレートを保つ）。
                _fireTimer += _type.FireInterval;
            }
        }

        private void Fire()
        {
            if (_factory == null || _type == null || _type.Projectile == null || _target == null) return;

            _factory.Create(_type.Projectile, transform.position, _target.transform.position, _target);
            ShotsFired++;
        }

        /// <summary>
        /// 撃破する。以後の発射を止める（飛来中の弾は各 <see cref="Projectile"/> が着弾まで処理する）。
        /// 実際のプール返却・非表示は所有者（ChannelController）が行う。
        /// </summary>
        public void Kill()
        {
            _alive = false;
        }
    }
}
