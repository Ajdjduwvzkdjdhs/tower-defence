using UnityEngine;

namespace KakigoriTD.Audio
{
    /// <summary>
    /// BGM/SE を鳴らす薄い MonoBehaviour アダプタ（ADR-8/11 / ADR-26）。純ロジックを持たず、
    /// <see cref="AudioConfigSO"/> のクリップ・音量を <see cref="AudioSource"/> に橋渡しするだけ。
    /// 3d-t の Singleton 直叩き（AudioManager.Instance.PlaySound）は採用せず、Composition Root
    /// （GameManager）が生成・<see cref="Configure"/> し、イベント購読から本サービスを呼ぶ。
    ///
    /// AudioSource は 3 系統を <see cref="Awake"/> で自動生成する:
    ///  - <c>_bgmSource</c>       : BGM（loop）。開始時に1回 Play し以後止めない（Q4-A）。
    ///  - <c>_seSource</c>        : SE（PlayOneShot）。発射/被弾/撃破/勝敗/巻き込み。
    ///  - <c>_avalancheSource</c> : 雪崩（loop・gate 専用）。IsAvalanching のエッジで on/off（Q1-A）。
    ///    PlayOneShot と source を分けることで、雪崩ループの Stop が他 SE を切らない。
    ///
    /// クリップ null は無音（鳴らさない）。EditMode テストは書かず MCP Play で検証する（ADR-11/26）。
    /// </summary>
    public class AudioService : MonoBehaviour
    {
        private AudioConfigSO _cfg;
        private AudioSource _bgmSource;
        private AudioSource _seSource;
        private AudioSource _avalancheSource;

        private void Awake()
        {
            _bgmSource = gameObject.AddComponent<AudioSource>();
            _bgmSource.playOnAwake = false;
            _bgmSource.loop = true;

            _seSource = gameObject.AddComponent<AudioSource>();
            _seSource.playOnAwake = false;
            _seSource.loop = false;

            _avalancheSource = gameObject.AddComponent<AudioSource>();
            _avalancheSource.playOnAwake = false;
            _avalancheSource.loop = true;
        }

        /// <summary>調整値（クリップ・音量）を注入する。</summary>
        public void Configure(AudioConfigSO cfg)
        {
            _cfg = cfg;
            if (_cfg != null) _avalancheSource.volume = _cfg.AvalancheVolume;
        }

        /// <summary>BGM をループ再生する（開始時に1回・以後止めない・Q4-A）。</summary>
        public void PlayBgm()
        {
            if (_cfg == null || _cfg.Bgm == null) return;
            _bgmSource.clip = _cfg.Bgm;
            _bgmSource.volume = _cfg.BgmVolume;
            _bgmSource.loop = true;
            if (!_bgmSource.isPlaying) _bgmSource.Play();
        }

        /// <summary>雪崩ループを on/off する（IsAvalanching のエッジで呼ぶ・Q1-A）。</summary>
        public void SetAvalanche(bool on)
        {
            if (on)
            {
                if (_cfg == null || _cfg.Avalanche == null) return;
                if (_avalancheSource.isPlaying) return; // 二重再生防止（エッジ以外で呼ばれても安全）
                _avalancheSource.clip = _cfg.Avalanche;
                _avalancheSource.volume = _cfg.AvalancheVolume;
                _avalancheSource.loop = true;
                _avalancheSource.Play();
            }
            else
            {
                if (_avalancheSource.isPlaying) _avalancheSource.Stop();
            }
        }

        /// <summary>SE を一度鳴らす（PlayOneShot）。クリップ null は無音。</summary>
        public void PlaySe(AudioClip clip, float volume)
        {
            if (clip == null) return;
            _seSource.PlayOneShot(clip, volume);
        }

        // ---- 意味づけした呼び出し口（GameManager のイベント購読から使う。音量は SO 由来） ----
        public void PlayClear() { if (_cfg != null) PlaySe(_cfg.Clear, _cfg.ClearVolume); }
        public void PlayGameOver() { if (_cfg != null) PlaySe(_cfg.GameOver, _cfg.GameOverVolume); }
        public void PlayHit() { if (_cfg != null) PlaySe(_cfg.Hit, _cfg.HitVolume); }
        public void PlayEnemyKilled() { if (_cfg != null) PlaySe(_cfg.NextEnemyKilled(), _cfg.EnemyKilledVolume); }
        public void PlayEnemyAttack() { if (_cfg != null) PlaySe(_cfg.EnemyAttack, _cfg.EnemyAttackVolume); }
        public void PlayAllyCaught() { if (_cfg != null) PlaySe(_cfg.AllyCaught, _cfg.AllyCaughtVolume); }
    }
}
