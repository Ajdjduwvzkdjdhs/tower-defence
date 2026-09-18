using UnityEngine;

namespace KakigoriTD.Gameplay
{
    /// <summary>
    /// 雪崩演出の新実装。雪玉（<c>Snowball</c> 時代）の転がしを廃し、
    /// snowEffectPrefab（ParticleSystem 付き）を検知位置の真上に 1 個生成して最初から再生する。
    ///
    /// 粒子速度の XZ 成分を「山中心 → 検知点」の外向きベクトルに整えるため、
    /// 外向き方向へ transform の向きを設定する。Prefab はローカル +Y 軸からコーンで
    /// 出力する構成（Shape: Cone・初期ローテーション 90° で +Y が水平向き）なので、
    /// この向き設定だけで粒子が XZ の外向きに流れ、Y 成分は Prefab 既定のまま。
    ///
    /// 撃破は当たり判定の方式（<see cref="Data.AvalancheHitMode"/>・ADR-32）で 2 通り:
    ///  - <b>Legacy</b>（<see cref="Spawn"/>）：効果の滑走中（外向き移動しながら）に毎フレーム現在位置で半径判定し、
    ///    1 個につき 1 回のみ（旧 Snowball の「1 雪玉 1 撃」踏襲）。
    ///  - <b>ParticleContact</b>（<see cref="SpawnContact"/>）：吹き出し口は検知点に固定。毎フレーム粒の実位置を走査し、
    ///    敵に触れた粒は吸収（寿命 0）＋敵に 1 ダメージ。通過痕跡は「最も遠くまで届いた粒」の位置で記録する。
    ///
    /// 再生完了（エミッション期間 ＋ 最大生存期間）でコールバックが呼ばれプール返却される。
    /// <see cref="Factory.ObjectPool{T}"/> で使い回す（ADR-4）。
    /// </summary>
    public class AvalancheSnowEffect : MonoBehaviour
    {
        [Header("雪崩の進み（旧雪玉の滑走に相当）")]
        [SerializeField] private float _travelSpeed = 4.0f;  // 外向き進み速度（撃破サンプリング用）
        [SerializeField] private float _maxLifetimeSec = 3.2f; // 保険：パーティクル完了前の早期返却上限

        private ParticleSystem _ps;
        private float _duration = 2f;

        // ParticleContact モード用
        private ParticleSystem.Particle[] _buf;
        private bool _worldSpace = true;
        private float _particleRadius;
        private bool _contactMode;
        private System.Func<Vector3, float, bool> _tryAbsorb;
        private bool _emissionStopped;

        private System.Func<Vector3, bool> _tryKillNear;
        private System.Action<Vector3> _onPassed;
        private System.Action<AvalancheSnowEffect> _onDone;
        private Vector3 _outwardDir = Vector3.forward;
        private float _elapsed;
        private float _endRadius;
        private System.Func<float, float, float> _heightAt;
        private bool _reachedFront;

        private void Awake()
        {
            _ps = GetComponent<ParticleSystem>();
            if (_ps != null)
            {
                var main = _ps.main;
                _duration = main.duration + Mathf.Max(main.startLifetime.constantMax, main.startLifetime.constantMin);
                _buf = new ParticleSystem.Particle[Mathf.Max(1, main.maxParticles)];
                _worldSpace = main.simulationSpace == ParticleSystemSimulationSpace.World;
                // 粒の見た目の半径（startSize の最大 × transform スケール ÷ 2）。scalingMode=Local/Hierarchy で
                // 粒サイズにスケールが掛かる前提（ルート直下で使うので lossyScale.x で代表）。
                float scale = transform.lossyScale.x > 1e-4f ? transform.lossyScale.x : 1f;
                _particleRadius = Mathf.Max(main.startSize.constantMax, main.startSize.constantMin) * scale * 0.5f;
            }
        }

        /// <summary>粒 1 個の見た目の半径（ParticleContact の接触半径に加算する）。</summary>
        public float ParticleRadius => _particleRadius;

        /// <summary>
        /// 検知点 <paramref name="worldPoint"/> の真上に配置し、外向き（XZ の山中心→検知点ベクトル）
        /// へ向きを設定して最初から再生する。
        /// </summary>
        /// <param name="worldPoint">雪崩の発生位置（検知点）。山中心は XZ 原点。</param>
        /// <param name="heightAt">(x,z)→world Y の斜面サンプラ。</param>
        /// <param name="endRadius">麓の距離。外向き移動がここまで進むと早期完了。</param>
        /// <param name="tryKillNear">現在位置の半径内で 1 体撃破できたら true。</param>
        /// <param name="onPassed">移動中の現在位置で毎フレーム呼ばれる（通過痕跡の記録用）。</param>
        /// <param name="onDone">再生完了時に呼びプール返却するコールバック。</param>
        public void Spawn(Vector3 worldPoint,
            System.Func<float, float, float> heightAt, float endRadius,
            System.Func<Vector3, bool> tryKillNear,
            System.Action<Vector3> onPassed,
            System.Action<AvalancheSnowEffect> onDone)
        {
            _heightAt = heightAt;
            _tryKillNear = tryKillNear;
            _onPassed = onPassed;
            _onDone = onDone;
            _endRadius = endRadius;
            _elapsed = 0f;
            _reachedFront = false;
            _contactMode = false;
            _tryAbsorb = null;
            _emissionStopped = false;

            PlaceAndPlay(worldPoint);
        }

        /// <summary>
        /// ParticleContact モード（ADR-32）。検知点 <paramref name="worldPoint"/> に吹き出し口を固定して再生し、
        /// 毎フレーム粒の実位置で <paramref name="tryAbsorb"/>（粒の位置, 粒の半径）を呼ぶ。true が返った粒は吸収されて消える。
        /// </summary>
        /// <param name="onPassed">「最も遠くまで届いた粒」の位置で毎フレーム呼ばれる（通過痕跡の記録用）。</param>
        public void SpawnContact(Vector3 worldPoint,
            System.Func<float, float, float> heightAt,
            System.Func<Vector3, float, bool> tryAbsorb,
            System.Action<Vector3> onPassed,
            System.Action<AvalancheSnowEffect> onDone)
        {
            _heightAt = heightAt;
            _tryKillNear = null;
            _tryAbsorb = tryAbsorb;
            _onPassed = onPassed;
            _onDone = onDone;
            _endRadius = 0f;
            _elapsed = 0f;
            _reachedFront = false;
            _contactMode = true;
            _emissionStopped = false;

            PlaceAndPlay(worldPoint);
        }

        /// <summary>
        /// 吹き出しを止める（ParticleContact：削るのをやめた瞬間に LidarAvalancheVfx が活性中の全雪崩へ呼ぶ）。
        /// 飛んでいる粒は寿命（Prefab 0.9 s）で自然に消え、粒が無くなったらプールへ返る。
        /// </summary>
        public void StopEmitting()
        {
            if (_emissionStopped) return;
            _emissionStopped = true;
            if (_ps != null) _ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        // 検知点の真上に置き、外向きへ向きを設定して最初から再生する（両モード共通）。
        private void PlaceAndPlay(Vector3 worldPoint)
        {
            var flat = new Vector3(worldPoint.x, 0f, worldPoint.z);
            _outwardDir = flat.sqrMagnitude > 1e-6f ? flat.normalized : _outwardDir;

            // Cone shape はローカル +Y 軸に沿って吐出する。+Y を外向きへ載せるには
            // +Y→+Z の +90°(X) を LookRotation に合成する（Prefab 本体も同じ Bake を持つ）。
            transform.rotation =
                Quaternion.LookRotation(_outwardDir, Vector3.up) * Quaternion.AngleAxis(90f, Vector3.right);
            float y = _heightAt != null ? _heightAt(worldPoint.x, worldPoint.z) : worldPoint.y;
            transform.position = new Vector3(worldPoint.x, y, worldPoint.z);

            if (_ps != null)
            {
                _ps.Clear(true);
                _ps.Play(false);
            }
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            _elapsed += dt;

            if (_contactMode)
            {
                ScanParticlesForContact();
            }
            else
            {
                // Legacy：外向き移動（旧雪玉の滑走相当）。撃破は移動しながら 1 回のみサンプリングする。
                Vector3 pos = transform.position;
                pos += _outwardDir * _travelSpeed * dt;
                if (_heightAt != null) pos.y = _heightAt(pos.x, pos.z);
                transform.position = pos;
                _onPassed?.Invoke(transform.position); // 通過痕跡は毎フレーム記録（tryKillNear と違い単発ガード無し）

                if (!_reachedFront && _tryKillNear != null && _tryKillNear(transform.position))
                {
                    _reachedFront = true;
                }
            }

            bool finished = _elapsed >= _duration || _elapsed >= _maxLifetimeSec;
            if (!finished && _emissionStopped && _ps != null && _ps.particleCount == 0) finished = true; // 止めた後は粒が尽きたら即返却
            if (!finished && _endRadius > 0f)
            {
                Vector3 flat = new Vector3(transform.position.x, 0f, transform.position.z);
                finished = flat.magnitude >= _endRadius;
            }

            if (finished)
            {
                if (_ps != null) _ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var cb = _onDone;
                _onDone = null;
                cb?.Invoke(this); // ObjectPool.Release → SetActive(false) のみ（Destroy しない）
            }
        }

        // ParticleContact：全粒を走査し、敵に触れた粒を吸収（寿命 0）。最も遠くまで届いた粒の位置を通過痕跡として通知する。
        private void ScanParticlesForContact()
        {
            if (_ps == null || _buf == null) return;
            int n = _ps.GetParticles(_buf);
            if (n == 0) return;

            bool modified = false;
            float farSq = -1f;
            Vector3 far = Vector3.zero;

            for (int i = 0; i < n; i++)
            {
                if (_buf[i].remainingLifetime <= 0f) continue;
                Vector3 p = _buf[i].position;
                if (!_worldSpace) p = transform.TransformPoint(p);

                float rSq = p.x * p.x + p.z * p.z;
                if (rSq > farSq) { farSq = rSq; far = p; }

                if (_tryAbsorb != null && _tryAbsorb(p, _particleRadius))
                {
                    _buf[i].remainingLifetime = 0f;
                    modified = true;
                }
            }

            if (modified) _ps.SetParticles(_buf, n);
            if (farSq >= 0f) _onPassed?.Invoke(far);
        }

        // プール返却（SetActive(false)）のたびに呼ばれる。残留粒子を残さない。
        private void OnDisable()
        {
            if (_ps != null) _ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
    }
}
