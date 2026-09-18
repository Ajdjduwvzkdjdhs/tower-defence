using System.Collections.Generic;
using UnityEngine;

namespace KakigoriTD.Data
{
    /// <summary>
    /// 単一ステージ（Wave 制廃止・ADR-30）の設定。旧 <c>WaveConfigSO</c>×3 の置き換え。
    /// 調整値は Inspector で編集する（ADR-1：マジックナンバー直書き禁止）。
    ///
    /// **難度調整の主ノブは <see cref="TargetKillCount"/>**。
    /// 湧きは既定で「盤面の敵数を目標人数に保つ」<see cref="Data.SpawnMode.Population"/>（ADR-36）：
    /// 倒した分だけ補充するので、倒す速さがそのままクリアタイムになる。人数は開始→終了を進捗で線形補間。
    /// 旧方式 <see cref="Data.SpawnMode.FixedRate"/>（一定間隔・ADR-30）は比較・切り戻し用に残す。
    /// 進行に応じてランプするのは 盤面人数・爆発兵率・盾兵率（撃破数の進捗 0..1 で評価）。
    ///
    /// 出現率は「進捗 0 のとき」「進捗 1 のとき」の 2 値を線形補間する（現場で数字を直接触れるようにするため、
    /// カーブではなく 2 値で持つ）。<c>SpawnDirector</c> がここで評価した float を <c>SpawnSelector</c> に渡す。
    /// </summary>
    [CreateAssetMenu(menuName = "KakigoriTD/StageConfig")]
    public class StageConfigSO : ScriptableObject
    {
        [Header("終了条件")]
        [Tooltip("クリアに必要な撃破数。ステージの長さと難度を決める主ノブ。")]
        [SerializeField] private int _targetKillCount = 100;

        [Header("湧き（ADR-36）")]
        [Tooltip("湧き方式。Population（既定）＝盤面の敵数を目標人数に保ち、倒した分だけ補充する（腕前がクリアタイムに出る）。"
               + "FixedRate＝一定間隔で湧く旧方式（ADR-30。クリアタイムの下限が 目標撃破数÷湧きレート に固定される）。")]
        [SerializeField] private SpawnMode _spawnMode = SpawnMode.Population;
        [Tooltip("[Population] 開始時（撃破 0 体）に盤面へ維持する敵数。多いほど砦への圧が強く、雪崩 1 回の巻き込みも増える。")]
        [SerializeField] private int _fieldPopulationStart = 8;
        [Tooltip("[Population] 終了時（目標撃破数に到達）に維持する敵数。開始→終了を進捗で線形補間。")]
        [SerializeField] private int _fieldPopulationEnd = 16;
        [Tooltip("[Population] 補充の上限レート(体/秒)。目標人数を下回っている間、この速さまでで補充する。"
               + "腕前の上限より十分速くしておく（遅いと再び湧き律速になる）。")]
        [SerializeField] private float _refillRatePerSecond = 6f;
        [Tooltip("[FixedRate] 1秒あたりの敵出現数(体/秒)。展示現場で直感的に調整できるよう秒間スポーン数で持つ"
               + "(内部では間隔=1/レートへ変換)。進行によるランプはかけない固定値。")]
        [SerializeField] private float _spawnRatePerSecond = 0.667f;
        [Tooltip("通常敵の種別プール。ここからランダムに 1 種を選ぶ。")]
        [SerializeField] private EnemyTypeSO[] _enemyPool;

        [Header("爆発兵（範囲殲滅の専売）")]
        [SerializeField] private EnemyTypeSO _bomberType;
        [Tooltip("開始時（撃破 0 体）の出現確率 0..1。1 体湧くごとにこの確率で爆発兵になる。")]
        [Range(0f, 1f)] [SerializeField] private float _bomberChanceStart = 0.10f;
        [Tooltip("終了時（目標撃破数に到達）の出現確率 0..1。開始→終了を進捗で線形補間。")]
        [Range(0f, 1f)] [SerializeField] private float _bomberChanceEnd = 0.25f;
        [Tooltip("爆発半径（ゲーム単位・XZ）。連鎖の唯一の制御パラメータ。"
               + "敵は 15° 刻み 24 列・静止半径 8 に並ぶので隣の列は約 2.1、2 列先は約 4.2 離れる。"
               + "3.0 だと隣の列にしか届かず巻き込みがほぼ起きない。")]
        [SerializeField] private float _explosionRadius = 4.5f;

        [Header("盾兵（雪玉では倒しにくい。必ず爆発兵の後ろにペアで湧く）")]
        [SerializeField] private EnemyTypeSO _shieldType;
        [Tooltip("開始時（撃破 0 体）の出現確率 0..1。この確率で「爆発兵＋盾兵」のペアが湧く。")]
        [Range(0f, 1f)] [SerializeField] private float _shieldChanceStart = 0.05f;
        [Tooltip("終了時（目標撃破数に到達）の出現確率 0..1。開始→終了を進捗で線形補間。")]
        [Range(0f, 1f)] [SerializeField] private float _shieldChanceEnd = 0.20f;
        [Tooltip("雪玉で撃破するのに必要なヒット数。爆発なら耐久を無視して一撃。")]
        [SerializeField] private int _shieldHits = 20;

        [Header("コンボ")]
        [Tooltip("この秒数以内に次を倒せばコンボ継続。爆発の連鎖撃破も 1 体ずつ加算される。")]
        [SerializeField] private float _comboWindowSec = 1.5f;
        [Tooltip("ビーム砲が敵を倒した瞬間から、進行中コンボが切れるまでの猶予秒（ADR-35）。"
               + "ビーム撃破はコンボに加算しない。Combo Window Sec より短くして、一掃後にコンボだけが長く生き残らないようにする。"
               + "この猶予内に雪崩で倒せばコンボは継続し、猶予は Combo Window Sec に戻る。")]
        [SerializeField] private float _beamComboGraceSec = 0.5f;

        [Header("必殺ビーム砲（ゲージMAXで自動発動）")]
        [Tooltip("MAX となる充填量。大きいほどビーム砲の発動頻度が下がる（目安：1ゲーム中に約1回MAXになる総量から逆算）。")]
        [SerializeField] private float _gaugeMax = 60f;
        [Tooltip("撃破 1 体あたりの基礎充填量（撃破数がメイン）。")]
        [SerializeField] private float _gaugePerKill = 1.0f;
        [Tooltip("コンボ 1 あたりの上乗せ係数（コンボはボーナス）。0 ならコンボ無関係。")]
        [SerializeField] private float _gaugeComboBonus = 0.1f;

        /// <summary>クリアに必要な撃破数。</summary>
        public int TargetKillCount => _targetKillCount;

        /// <summary>湧き方式（ADR-36）。</summary>
        public SpawnMode SpawnMode => _spawnMode;

        /// <summary>[Population] 補充の上限レート（体/秒）。</summary>
        public float RefillRatePerSecond => _refillRatePerSecond;

        /// <summary>[FixedRate] 1秒あたりの敵出現数（体/秒）。</summary>
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

        /// <summary>ビーム撃破の瞬間から進行中コンボが切れるまでの猶予秒（ADR-35）。</summary>
        public float BeamComboGraceSec => _beamComboGraceSec;

        /// <summary>ゲージ MAX の充填量。</summary>
        public float GaugeMax => _gaugeMax;

        /// <summary>撃破 1 体あたりの基礎充填量。</summary>
        public float GaugePerKill => _gaugePerKill;

        /// <summary>コンボ 1 あたりのゲージ上乗せ係数。</summary>
        public float GaugeComboBonus => _gaugeComboBonus;

        /// <summary>[Population] 進捗 0..1 において盤面へ維持する敵数（開始→終了の線形補間・四捨五入・0 以上）。</summary>
        public int FieldPopulationAt(float progress01)
            => Mathf.Max(0, Mathf.RoundToInt(Mathf.Lerp(_fieldPopulationStart, _fieldPopulationEnd, Mathf.Clamp01(progress01))));

        /// <summary>進捗 0..1 における爆発兵の出現確率（開始→終了の線形補間・0..1 にクランプ）。</summary>
        public float BomberChanceAt(float progress01)
            => _bomberType == null ? 0f : Mathf.Clamp01(Mathf.Lerp(_bomberChanceStart, _bomberChanceEnd, Mathf.Clamp01(progress01)));

        /// <summary>進捗 0..1 における盾兵の出現確率（開始→終了の線形補間・0..1 にクランプ）。</summary>
        public float ShieldChanceAt(float progress01)
            => _shieldType == null ? 0f : Mathf.Clamp01(Mathf.Lerp(_shieldChanceStart, _shieldChanceEnd, Mathf.Clamp01(progress01)));
    }
}
