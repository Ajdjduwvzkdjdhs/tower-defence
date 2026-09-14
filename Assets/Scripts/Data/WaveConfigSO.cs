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
        [Tooltip("1秒あたりの敵出現数(体/秒)。展示現場で直感的に調整できるよう秒間スポーン数で持つ(内部では間隔=1/レートへ変換)。")]
        [SerializeField] private float _spawnRatePerSecond = 0.667f;
        [SerializeField] private EnemyTypeSO[] _enemyPool;

        // Phase1 では未使用（define-only）。
        // _allySpawnChance は Phase2（味方NPC 出現）、_disguiseChance は Phase3（変装）で使う。
        [SerializeField] private float _allySpawnChance = 0f;
        [SerializeField] private float _disguiseChance = 0f;

        // Phase2: このウェーブで湧く味方NPC の種別（null の場合は味方なし）。
        [SerializeField] private EnemyTypeSO _allyType;

        // Phase3: このウェーブで湧く変装敵の種別（null の場合は変装なし）。
        [SerializeField] private EnemyTypeSO _disguiseType;

        // Extra1-C: 中央バナーに表示する波名（例: 「快感波」「本隊侵攻」）。
        [SerializeField] private string _displayName;

        /// <summary>ウェーブ毎の制限時間（秒）。</summary>
        public float Duration => _duration;

        /// <summary>1秒あたりの敵出現数（体/秒）。</summary>
        public float SpawnRatePerSecond => _spawnRatePerSecond;

        /// <summary>このウェーブに出現する敵種のプール（読み取り専用）。</summary>
        public IReadOnlyList<EnemyTypeSO> EnemyPool => _enemyPool;

        /// <summary>味方NPC の出現確率（Phase2 用）。※ 味方システムは <c>WaveManager</c> 側で無効化済みのため現在は未参照。</summary>
        public float AllySpawnChance => _allySpawnChance;

        /// <summary>変装した敵の出現確率（Phase3 用。Phase1 では未使用）。</summary>
        public float DisguiseChance => _disguiseChance;

        /// <summary>このウェーブで湧く味方NPC の種別（null なら味方なし）。※ 味方システムは <c>WaveManager</c> 側で無効化済みのため現在は未参照。</summary>
        public EnemyTypeSO AllyType => _allyType;

        /// <summary>このウェーブで湧く変装敵の種別（null なら変装なし）。</summary>
        public EnemyTypeSO DisguiseType => _disguiseType;

        /// <summary>中央バナーに表示する波名（Extra1-C）。</summary>
        public string DisplayName => _displayName;
    }
}
