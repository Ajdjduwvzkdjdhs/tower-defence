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
    /// 撃破は効果の滑走中（外向き移動しながら）に毎フレーム現在位置で半径判定し、
    /// 1 個につき 1 回のみ（旧 Snowball の「1 雪玉 1 撃」踏襲）。
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
            }
        }

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

            // 外向き移動（旧雪玉の滑走相当）。撃破は移動しながら 1 回のみサンプリングする。
            Vector3 pos = transform.position;
            pos += _outwardDir * _travelSpeed * dt;
            if (_heightAt != null) pos.y = _heightAt(pos.x, pos.z);
            transform.position = pos;
            _onPassed?.Invoke(transform.position); // 通過痕跡は毎フレーム記録（tryKillNear と違い単発ガード無し）

            if (!_reachedFront && _tryKillNear != null && _tryKillNear(transform.position))
            {
                _reachedFront = true;
            }

            bool finished = _elapsed >= _duration || _elapsed >= _maxLifetimeSec;
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

        // プール返却（SetActive(false)）のたびに呼ばれる。残留粒子を残さない。
        private void OnDisable()
        {
            if (_ps != null) _ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
    }
}
