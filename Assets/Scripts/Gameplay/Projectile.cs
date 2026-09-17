using System;
using UnityEngine;
using KakigoriTD.Data;

namespace KakigoriTD.Gameplay
{
    /// <summary>
    /// 敵が放つ遠隔攻撃の弾。麓（外周）から山頂（砦）へ、山を越える放物線を描いて飛翔し、
    /// 着弾で砦に <see cref="ProjectileTypeSO.Damage"/> を与えてプールへ返却される（ADR-4 / ADR-7）。
    /// 山を貫通して見えないよう、<see cref="ProjectileTypeSO.ArcHeight"/> を頂点とする山なり軌道にする（見た目のみ）。
    ///
    /// ホットパスで生成・破棄しないため <see cref="ProjectileFactory"/> 経由でプールから取得・返却する。
    /// 着弾判定はコライダーを使わず飛行の進捗割合で行う（phase1-plan §5: 距離/到達割合ベース）。
    /// プール再利用のため、状態は毎回 <see cref="Init"/> で初期化する。
    /// </summary>
    public class Projectile : MonoBehaviour
    {
        private Vector3 _from;
        private Vector3 _target;
        private float _arcHeight;
        private float _duration;
        private float _elapsed;
        private int _damage;
        private Fort _targetFort;
        private Action<Projectile> _onArrive;

        // 飛行中のみ true。着弾後はプール返却までの間に Update が二重に走っても再着弾しないためのガード。
        private bool _inFlight;

        /// <summary>
        /// 弾を初期化して飛行を開始する。プールから取得するたびに呼ぶ。
        /// </summary>
        /// <param name="type">弾種（速度・ダメージ・弧の高さ）。</param>
        /// <param name="from">発射位置（麓）。</param>
        /// <param name="to">着弾位置（砦）。</param>
        /// <param name="targetFort">着弾時にダメージを与える砦（null 可）。</param>
        /// <param name="onArrive">着弾時に呼ばれる返却コールバック（Factory がプールへ戻す）。</param>
        public void Init(ProjectileTypeSO type, Vector3 from, Vector3 to, Fort targetFort, Action<Projectile> onArrive)
        {
            _from = from;
            _target = to;
            _arcHeight = type.ArcHeight;
            // Speed の意味（直線距離あたりの速さ）を維持したまま飛行時間へ換算する。
            float distance = Vector3.Distance(from, to);
            _duration = type.Speed > 0f ? distance / type.Speed : 0f;
            _elapsed = 0f;
            _damage = type.Damage;
            _targetFort = targetFort;
            _onArrive = onArrive;
            transform.position = from;
            _inFlight = true;
        }

        private void Update()
        {
            if (!_inFlight) return;

            _elapsed += Time.deltaTime;
            float t = _duration > 0f ? Mathf.Clamp01(_elapsed / _duration) : 1f;

            Vector3 basePos = Vector3.Lerp(_from, _target, t);
            float arc = _arcHeight * Mathf.Sin(t * Mathf.PI); // t=0/1で0、中間で最大＝山を越える弧
            transform.position = basePos + Vector3.up * arc;

            if (t >= 1f)
            {
                _inFlight = false;
                _targetFort?.TakeDamage(_damage);
                _onArrive?.Invoke(this); // プールへ返却（SetActive(false)）
            }
        }
    }
}
