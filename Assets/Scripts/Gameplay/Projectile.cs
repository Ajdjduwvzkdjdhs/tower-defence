using System;
using UnityEngine;
using KakigoriTD.Data;

namespace KakigoriTD.Gameplay
{
    /// <summary>
    /// 敵が放つ遠隔攻撃の弾。麓（外周）から山頂（砦）へ <see cref="ProjectileTypeSO.Speed"/> で直進し、
    /// 着弾で砦に <see cref="ProjectileTypeSO.Damage"/> を与えてプールへ返却される（ADR-4 / ADR-7）。
    ///
    /// ホットパスで生成・破棄しないため <see cref="ProjectileFactory"/> 経由でプールから取得・返却する。
    /// 着弾判定はコライダーを使わず到達距離で行う（phase1-plan §5: 距離/到達割合ベース）。
    /// プール再利用のため、状態は毎回 <see cref="Init"/> で初期化する。
    /// </summary>
    public class Projectile : MonoBehaviour
    {
        // 目標にこの距離まで近づいたら着弾とみなす。MoveTowards は overshoot しないので到達時 0 に収束する。
        private const float ArriveThreshold = 0.02f;

        private float _speed;
        private int _damage;
        private Vector3 _target;
        private Fort _targetFort;
        private Action<Projectile> _onArrive;

        // 飛行中のみ true。着弾後はプール返却までの間に Update が二重に走っても再着弾しないためのガード。
        private bool _inFlight;

        /// <summary>
        /// 弾を初期化して飛行を開始する。プールから取得するたびに呼ぶ。
        /// </summary>
        /// <param name="type">弾種（速度・ダメージ）。</param>
        /// <param name="from">発射位置（麓）。</param>
        /// <param name="to">着弾位置（砦）。</param>
        /// <param name="targetFort">着弾時にダメージを与える砦（null 可）。</param>
        /// <param name="onArrive">着弾時に呼ばれる返却コールバック（Factory がプールへ戻す）。</param>
        public void Init(ProjectileTypeSO type, Vector3 from, Vector3 to, Fort targetFort, Action<Projectile> onArrive)
        {
            _speed = type.Speed;
            _damage = type.Damage;
            _target = to;
            _targetFort = targetFort;
            _onArrive = onArrive;
            transform.position = from;
            _inFlight = true;
        }

        private void Update()
        {
            if (!_inFlight) return;

            transform.position = Vector3.MoveTowards(transform.position, _target, _speed * Time.deltaTime);

            if ((transform.position - _target).sqrMagnitude <= ArriveThreshold * ArriveThreshold)
            {
                _inFlight = false;
                _targetFort?.TakeDamage(_damage);
                _onArrive?.Invoke(this); // プールへ返却（SetActive(false)）
            }
        }
    }
}
