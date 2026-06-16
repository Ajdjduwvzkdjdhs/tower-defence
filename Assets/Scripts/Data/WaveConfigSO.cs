using System.Collections.Generic;
using UnityEngine;

namespace KakigoriTD.Data
{
    /// <summary>
    /// 1 ウェーブ分の設定（制限時間・湧き間隔・出現する敵種）を保持する ScriptableObject。
    /// 調整値は Inspector で編集する（ADR-1：マジックナンバー直書き禁止）。
    /// </summary>
    [CreateAssetMenu(menuName = "KakigoriTD/WaveConfig")]
    public class WaveConfigSO : ScriptableObject
    {
        // 初期値は仕様書 §19 パラメータ・テーブルに準拠（Wave1 想定）。
        [SerializeField] private float _duration = 30f;
        [SerializeField] private float _spawnInterval = 1.5f;
        [SerializeField] private EnemyTypeSO[] _enemyPool;

        // Phase1 では未使用（define-only）。
        // _allySpawnChance は Phase2（味方NPC 出現）、_disguiseChance は Phase3（変装）で使う。
        [SerializeField] private float _allySpawnChance = 0f;
        [SerializeField] private float _disguiseChance = 0f;

        // Phase2: このウェーブで湧く味方NPC の種別（null の場合は味方なし）。
        [SerializeField] private EnemyTypeSO _allyType;

        /// <summary>ウェーブ毎の制限時間（秒）。</summary>
        public float Duration => _duration;

        /// <summary>谷ごとの敵の湧き間隔（秒）。</summary>
        public float SpawnInterval => _spawnInterval;

        /// <summary>このウェーブに出現する敵種のプール（読み取り専用）。</summary>
        public IReadOnlyList<EnemyTypeSO> EnemyPool => _enemyPool;

        /// <summary>味方NPC の出現確率（Phase2 用。Phase1 では未使用）。</summary>
        public float AllySpawnChance => _allySpawnChance;

        /// <summary>変装した敵の出現確率（Phase3 用。Phase1 では未使用）。</summary>
        public float DisguiseChance => _disguiseChance;

        /// <summary>このウェーブで湧く味方NPC の種別（null なら味方なし）。</summary>
        public EnemyTypeSO AllyType => _allyType;
    }
}
