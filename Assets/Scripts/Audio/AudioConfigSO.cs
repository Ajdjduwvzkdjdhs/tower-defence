using UnityEngine;

namespace KakigoriTD.Audio
{
    /// <summary>
    /// BGM/SE のクリップと音量の調整値（ADR-1 / ADR-26）。マジックナンバーを直書きせず、
    /// クリップ差し替え・音量調整を Inspector で行えるようにする。音量は移植元 kakigori-3d-t を踏襲
    /// （enemyKilled=0.2 / hit=0.1 / enemyAttack=0.1・他=1.0）。
    ///
    /// <see cref="AudioService"/> が本アセットを読み、イベント購読（GameManager）から鳴らす。
    /// クリップ未割り当て（null）は無音（<see cref="AllyCaught"/> は 3d-t に該当音が無く既定 null＝無音）。
    /// </summary>
    [CreateAssetMenu(fileName = "AudioConfig", menuName = "KakigoriTD/AudioConfig")]
    public class AudioConfigSO : ScriptableObject
    {
        [Header("BGM（馬車道・ループ）")]
        [SerializeField] private AudioClip _bgm;
        [Range(0f, 1f)][SerializeField] private float _bgmVolume = 1.0f;

        [Header("勝敗ジングル（BGM に重ねる）")]
        [SerializeField] private AudioClip _clear;
        [SerializeField] private AudioClip _gameOver;

        [Header("戦闘 SE")]
        [SerializeField] private AudioClip _hit;         // 砦被弾
        [SerializeField] private AudioClip _enemyKilled; // 敵撃破（下の配列が空のときのフォールバック）
        [SerializeField] private AudioClip _enemyAttack; // 敵発射（毎発）

        [Tooltip("敵撃破音。複数入れると鳴るたびに上から順番に切り替わる。空なら上の単発クリップを使う。")]
        [SerializeField] private AudioClip[] _enemyKilledClips;

        [Header("雪崩（ループ gate・専用 source）")]
        [SerializeField] private AudioClip _avalanche;

        [Header("味方巻き込み（3d-t に該当音なし＝null 既定で無音）")]
        [SerializeField] private AudioClip _allyCaught;

        [Header("SE 音量（3d-t 踏襲）")]
        [Range(0f, 1f)][SerializeField] private float _clearVolume = 1.0f;
        [Range(0f, 1f)][SerializeField] private float _gameOverVolume = 1.0f;
        [Range(0f, 1f)][SerializeField] private float _hitVolume = 0.1f;
        [Range(0f, 1f)][SerializeField] private float _enemyKilledVolume = 0.2f;
        [Range(0f, 1f)][SerializeField] private float _enemyAttackVolume = 0.1f;
        [Range(0f, 1f)][SerializeField] private float _avalancheVolume = 1.0f;
        [Range(0f, 1f)][SerializeField] private float _allyCaughtVolume = 1.0f;

        public AudioClip Bgm => _bgm;
        public float BgmVolume => _bgmVolume;

        public AudioClip Clear => _clear;
        public float ClearVolume => _clearVolume;
        public AudioClip GameOver => _gameOver;
        public float GameOverVolume => _gameOverVolume;

        public AudioClip Hit => _hit;
        public float HitVolume => _hitVolume;
        public AudioClip EnemyKilled => _enemyKilled;
        public float EnemyKilledVolume => _enemyKilledVolume;

        /// <summary>
        /// 敵撃破音を1つ返す。<c>_enemyKilledClips</c> に複数入っていれば呼ばれるたびに順送りし、
        /// 同じ音が連続しないようにする（オープンキャンパスの「ヒット感が見えづらい」への対策）。
        /// 未設定・全要素 null なら従来どおり <c>_enemyKilled</c> を返すので、差し替え前の挙動に戻せる。
        /// </summary>
        public AudioClip NextEnemyKilled()
        {
            if (_enemyKilledClips != null && _enemyKilledClips.Length > 0)
            {
                // null 要素は飛ばす。1周して全部 null ならフォールバックへ落ちる。
                for (int i = 0; i < _enemyKilledClips.Length; i++)
                {
                    var clip = _enemyKilledClips[_enemyKilledIndex];
                    _enemyKilledIndex = (_enemyKilledIndex + 1) % _enemyKilledClips.Length;
                    if (clip != null) return clip;
                }
            }
            return _enemyKilled;
        }

        // 再生位置。ScriptableObject の実行時状態だが、次に鳴る番号が持ち越されるだけで副作用はない。
        private int _enemyKilledIndex;
        public AudioClip EnemyAttack => _enemyAttack;
        public float EnemyAttackVolume => _enemyAttackVolume;

        public AudioClip Avalanche => _avalanche;
        public float AvalancheVolume => _avalancheVolume;

        public AudioClip AllyCaught => _allyCaught;
        public float AllyCaughtVolume => _allyCaughtVolume;
    }
}
