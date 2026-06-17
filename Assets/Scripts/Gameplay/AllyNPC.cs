using System;
using UnityEngine;
using KakigoriTD.Data;

namespace KakigoriTD.Gameplay
{
    /// <summary>
    /// 味方NPC。<see cref="Enemy"/> を継承し同一 <c>EnemyMatrix&lt;Enemy&gt;</c> に多態で保持する（ADR-16）。
    /// 無発射（factory=null → <c>Fire</c> のガードで自動成立）、<see cref="EnemyTypeSO.AllyLifetime"/> 秒で
    /// 自走退場し <see cref="OnExpired"/> を発火する。所有者（<see cref="ChannelController"/>）が
    /// プール返却・行除去を行う。
    /// </summary>
    public class AllyNPC : Enemy
    {
        /// <summary>自走退場時に発火（所有者がプール返却・行除去に使う）。</summary>
        public event Action<AllyNPC> OnExpired;

        private float _lifetimeTimer;

        /// <summary>
        /// 味方として初期化する。factory=null/target=null を渡して無発射を保証し、
        /// lifetime を <see cref="EnemyTypeSO.AllyLifetime"/> にセットする。
        /// </summary>
        public void InitAlly(EnemyTypeSO type)
        {
            base.Init(type, null, null);
            _lifetimeTimer = type != null ? type.AllyLifetime : 6f;
        }

        protected override void Update()
        {
            if (!IsAlive) return;
            MoveTowardTarget(); // base.Update は呼ばないため明示的に呼ぶ（ADR-20）
            _lifetimeTimer -= Time.deltaTime;
            if (_lifetimeTimer <= 0f)
            {
                Kill();
                OnExpired?.Invoke(this);
            }
        }
    }
}
