using System.Collections.Generic;
using UnityEngine;

namespace KakigoriTD.Data
{
    /// <summary>
    /// 単一ステージ（Wave 制廃止・ADR-30）の設定。旧 <c>WaveConfigSO</c>×3 の置き換え。
    /// 調整値は Inspector で編集する（ADR-1：マジックナンバー直書き禁止）。
    ///
    /// **難度調整の主ノブは <see cref="TargetKillCount"/>**。湧きレートは固定値で、
    /// 進行に応じてランプするのは爆発兵率・盾兵率の 2 つだけ（撃破数の進捗 0..1 でカーブを評価）。
    /// 盤面の混雑度は「湧き速度 − プレイヤーの撃破速度」の自然な平衡に任せる。
    ///
    /// <c>AnimationCurve</c> は UnityEngine 型なので Core には持ち込まず、
    /// <c>SpawnDirector</c> がここで評価した float を <c>SpawnSelector</c> に渡す。
    /// </summary>
    [CreateAssetMenu(menuName = "KakigoriTD/StageConfig")]
    public class StageConfigSO : ScriptableObject
    {
        [Header("終了条件")]
        [Tooltip("クリアに必要な撃破数。ステージの長さと難度を決める主ノブ。")]
        [SerializeField] private int _targetKillCount = 100;

        [Header("湧き")]
        [Tooltip("1秒あたりの敵出現数(体/秒)。展示現場で直感的に調整できるよう秒間スポーン数で持つ"
               + "(内部では間隔=1/レートへ変換)。進行によるランプはかけない固定値。")]
        [SerializeField] private float _spawnRatePerSecond = 0.667f;
        [Tooltip("通常敵の種別プール。ここからランダムに 1 種を選ぶ。")]
        [SerializeField] private EnemyTypeSO[] _enemyPool;

        [Header("爆発兵（範囲殲滅の専売）")]
        [SerializeField] private EnemyTypeSO _bomberType;
        [Tooltip("横軸＝撃破数の進捗 0..1、縦軸＝出現確率。終盤ほど濃くする。")]
        [SerializeField] private AnimationCurve _bomberChanceCurve = AnimationCurve.Linear(0f, 0.10f, 1f, 0.25f);
        [Tooltip("爆発半径（ゲーム単位・XZ）。連鎖の唯一の制御パラメータ。")]
        [SerializeField] private float _explosionRadius = 3.0f;

        [Header("盾兵（雪玉では倒しにくい）")]
        [SerializeField] private EnemyTypeSO _shieldType;
        [Tooltip("横軸＝撃破数の進捗 0..1、縦軸＝出現確率。")]
        [SerializeField] private AnimationCurve _shieldChanceCurve = AnimationCurve.Linear(0f, 0.05f, 1f, 0.20f);
        [Tooltip("雪玉で撃破するのに必要なヒット数。爆発なら耐久を無視して一撃。")]
        [SerializeField] private int _shieldHits = 5;

        [Header("コンボ")]
        [Tooltip("この秒数以内に次を倒せばコンボ継続。爆発の連鎖撃破も 1 体ずつ加算される。")]
        [SerializeField] private float _comboWindowSec = 2.0f;

        [Header("必殺ビーム砲（ゲージMAXで自動発動）")]
        [Tooltip("MAX となる充填量。1ゲーム中に約2回MAXになる総量から逆算する。")]
        [SerializeField] private float _gaugeMax = 100f;
        [Tooltip("撃破 1 体あたりの基礎充填量（撃破数がメイン）。")]
        [SerializeField] private float _gaugePerKill = 1.0f;
        [Tooltip("コンボ 1 あたりの上乗せ係数（コンボはボーナス）。0 ならコンボ無関係。")]
        [SerializeField] private float _gaugeComboBonus = 0.1f;

        /// <summary>クリアに必要な撃破数。</summary>
        public int TargetKillCount => _targetKillCount;

        /// <summary>1秒あたりの敵出現数（体/秒）。</summary>
        public float SpawnRatePerSecond => _spawnRatePerSecond;

        /// <summary>通常敵の種別プール（読み取り専用）。</summary>
        public IReadOnlyList<EnemyTypeSO> EnemyPool => _enemyPool;

        /// <summary>爆発兵の種別（null なら湧かない）。</summary>
        public EnemyTypeSO BomberType => _bomberType;

        /// <summary>盾兵の種別（null なら湧かない）。</summary>
        public EnemyTypeSO ShieldType => _shieldType;

        /// <summary>爆発半径（ゲーム単位・XZ）。</summary>
        public float ExplosionRadius => _explosionRadius;

        /// <summary>盾兵の雪玉耐久（ヒット数）。</summary>
        public int ShieldHits => _shieldHits;

        /// <summary>コンボ継続の猶予秒。</summary>
        public float ComboWindowSec => _comboWindowSec;

        /// <summary>ゲージ MAX の充填量。</summary>
        public float GaugeMax => _gaugeMax;

        /// <summary>撃破 1 体あたりの基礎充填量。</summary>
        public float GaugePerKill => _gaugePerKill;

        /// <summary>コンボ 1 あたりのゲージ上乗せ係数。</summary>
        public float GaugeComboBonus => _gaugeComboBonus;

        /// <summary>進捗 0..1 における爆発兵の出現確率（0..1 にクランプ）。</summary>
        public float BomberChanceAt(float progress01)
            => _bomberType == null ? 0f : Mathf.Clamp01(_bomberChanceCurve.Evaluate(Mathf.Clamp01(progress01)));

        /// <summary>進捗 0..1 における盾兵の出現確率（0..1 にクランプ）。</summary>
        public float ShieldChanceAt(float progress01)
            => _shieldType == null ? 0f : Mathf.Clamp01(_shieldChanceCurve.Evaluate(Mathf.Clamp01(progress01)));
    }
}
