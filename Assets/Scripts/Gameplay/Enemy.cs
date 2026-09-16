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

        // ADR-20: 繰り上がり前進を lerp 化。SetTarget で目標位置を設定し Update で MoveTowards。
        private Vector3 _targetPos;
        private float _advanceSpeed;

        // ADR-23: 発射は所定スロット到達後のみ（入場・繰り上がりの移動中は撃たない）。
        private const float ArriveEpsilonSqr = 0.0004f; // (0.02)^2

        /// <summary>目標位置（スロット）に到達して静止しているか。移動中は false（＝発射しない）。</summary>
        protected bool IsInPosition => (transform.position - _targetPos).sqrMagnitude <= ArriveEpsilonSqr;

        // per-instance tint: sharedMaterial を汚さずプール再利用で色を切り替える（ADR-16）。
        private Renderer _renderer;
        private MaterialPropertyBlock _mpb;

        // 攻撃モーション用（Animator は種別によって無い/Attack トリガーを持たない場合もあるため null 許容）。
        private const string AttackTrigger = "Attack";
        private const string WalkBool = "Walk";
        private Animator _animator;
        private bool _hasAttackTrigger;
        private bool _hasWalkBool;

        /// <summary>生存しているか（撃破されると false）。</summary>
        public bool IsAlive => _alive;

        /// <summary>これまでに発射した弾数（検証用の可観測性）。</summary>
        public int ShotsFired { get; private set; }

        private void Awake()
        {
            _renderer = GetComponent<Renderer>();
            _mpb = new MaterialPropertyBlock();

            _animator = GetComponent<Animator>();
            _hasAttackTrigger = false;
            _hasWalkBool = false;
            if (_animator != null)
            {
                foreach (var p in _animator.parameters)
                {
                    if (p.type == AnimatorControllerParameterType.Trigger && p.name == AttackTrigger)
                        _hasAttackTrigger = true;
                    else if (p.type == AnimatorControllerParameterType.Bool && p.name == WalkBool)
                        _hasWalkBool = true;
                }
            }
        }

        /// <summary>
        /// 敵を初期化して稼働させる。プールから取得するたびに呼ぶ。
        /// 最初の発射は <see cref="EnemyTypeSO.InitialFireDelay"/> 秒後（既定0＝即時）、
        /// 以後は <see cref="EnemyTypeSO.FireInterval"/> 秒ごと。
        /// </summary>
        public void Init(EnemyTypeSO type, ProjectileFactory factory, Fort target)
        {
            _type = type;
            _factory = factory;
            _target = target;
            _fireTimer = type != null ? type.InitialFireDelay : 0f;
            ShotsFired = 0;
            _alive = true;
            _targetPos = transform.position;
            SetAnimationPlaying(true); // プール再利用時に前回 StopAnimation 済みでも歩行再生を復帰させる
            if (_renderer != null)
            {
                _mpb.SetColor("_BaseColor", type != null ? type.Tint : Color.red);
                _renderer.SetPropertyBlock(_mpb);
            }
            OnInitialized();
        }

        /// <summary>目標位置を設定する。snap=true のとき即座に移動（湧き・初期配置用）、false のとき lerp（ADR-20）。</summary>
        public void SetTarget(Vector3 pos, bool snap, float speed = 3.5f)
        {
            _targetPos = pos;
            _advanceSpeed = speed;
            if (snap) transform.position = pos;
        }

        /// <summary>
        /// Init 完了後に呼ばれる仮想フック。派生クラス（例: DisguisedEnemy の tell マーカー生成）で使う。
        /// </summary>
        protected virtual void OnInitialized() { }

        protected virtual void Update()
        {
            if (!_alive) return;
            MoveTowardTarget();

            // ADR-23: 所定スロットに到達して静止するまで発射しない（入場・繰り上がり中はタイマ凍結）。
            // 初弾は到達後に InitialFireDelay から計時される。
            if (!IsInPosition) return;

            _fireTimer -= Time.deltaTime;
            if (_fireTimer <= 0f)
            {
                Fire();
                // 取りこぼし分を持ち越す（フレーム落ち時もレートを保つ）。
                _fireTimer += _type.FireInterval;
            }
        }

        /// <summary>
        /// Animator の再生/停止を切り替える。攻撃を持たない派生（<see cref="AllyNPC"/>）が
        /// 目的地に到着した後、歩行アニメーションをその場で回し続けないようにするために使う。
        /// </summary>
        protected void SetAnimationPlaying(bool playing)
        {
            if (_animator != null) _animator.enabled = playing;
        }

        protected void MoveTowardTarget()
        {
            if (_advanceSpeed > 0f)
                transform.position = Vector3.MoveTowards(transform.position, _targetPos, _advanceSpeed * Time.deltaTime);

            // 実際に動いている間だけ Walk=true。停止したら false にして、攻撃モーションの
            // 再生中に歩行へ自動で戻って割り込む（ExitTime だけに頼ると起きる）ことを防ぐ（ADR-23）。
            if (_hasWalkBool)
                _animator.SetBool(WalkBool, !IsInPosition);
        }

        // 停止して砦を狙える状態になったら呼ばれる。攻撃モーションを再生し、
        // その前振り（EnemyTypeSO.AttackWindup）が終わってから実際に弾を出す（ADR-23の「止まってから攻撃」に準拠）。
        private void Fire()
        {
            if (_factory == null || _type == null || _type.Projectile == null || _target == null) return;

            if (_hasAttackTrigger) _animator.SetTrigger(AttackTrigger);

            float windup = _type.AttackWindup;
            if (windup > 0f) StartCoroutine(FireAfterWindup(windup));
            else SpawnProjectile();
        }

        private System.Collections.IEnumerator FireAfterWindup(float delay)
        {
            yield return new WaitForSeconds(delay);
            if (_alive) SpawnProjectile(); // Kill() で非アクティブ化されるとこのコルーチン自体も止まるが念のため二重に防御
        }

        private void SpawnProjectile()
        {
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
