using System;
using UnityEngine;

namespace KakigoriTD.Gameplay
{
    /// <summary>
    /// 雪崩を構成する 1 粒の雪玉。<see cref="Launch"/> で山頂寄りから射出し、谷の外向き方向へ
    /// 加速しながら滑り落ち、斜面（<c>heightAt</c> サンプラ）に乗り続けて麓（<c>endRadius</c>）に
    /// 達したら自動でプール返却コールバックを呼ぶ（ADR-20 / ADR-21）。
    ///
    /// コライダー不使用＝物理なしを維持し、Rigidbody ではなくスクリプト滑走で表現する（ADR-21）。
    /// <see cref="Factory.ObjectPool{T}"/> で使い回し、ホットパスで Instantiate しない（ADR-4）。
    /// 見た目は <see cref="SnowPuff"/> に倣った軽量な cosmetic（純ロジックなし）。
    /// </summary>
    public class Snowball : MonoBehaviour
    {
        [Header("滑走（斜面を下る）")]
        [SerializeField] private float _startSpeed = 2.5f;   // 射出直後の外向き速度
        [SerializeField] private float _accel = 6.0f;        // 下り加速（重力の代わり）
        [SerializeField] private float _maxSpeed = 8.0f;     // 終端速度
        [SerializeField] private float _maxLifetime = 2.5f;  // 保険：麓に届かなくても返却する上限秒

        private Action<Snowball> _onDone;
        private Vector3 _outwardDir;
        private Vector3 _perpDir;
        private float _radius;     // 中心（山頂）からの外向き距離
        private float _lateral;    // 谷幅方向のオフセット（横のばらつき）
        private float _endRadius;  // 麓（滑走終了）
        private float _speed;
        private float _elapsed;
        private Func<float, float, float> _heightAt;
        private Vector3 _spin;     // 見た目の転がり

        /// <summary>
        /// 山頂寄りの一点から谷の外向きへ射出する。完了時に onDone を呼ぶ（所有者がプール返却する）。
        /// </summary>
        /// <param name="outwardDir">谷の外向き単位ベクトル（山頂→麓）。</param>
        /// <param name="startRadius">中心からの射出開始距離（＝山頂寄り）。</param>
        /// <param name="lateral">谷幅方向のオフセット（滑走中は保持）。</param>
        /// <param name="endRadius">麓の距離。ここに達したら返却する。</param>
        /// <param name="heightAt">(x,z)→world Y の斜面サンプラ。</param>
        public void Launch(Vector3 outwardDir, float startRadius, float lateral, float endRadius,
            Func<float, float, float> heightAt, Action<Snowball> onDone)
        {
            _outwardDir = outwardDir;
            _perpDir = new Vector3(outwardDir.z, 0f, -outwardDir.x); // XZ 平面での直交（ChannelController と同基底）
            _radius = startRadius;
            _lateral = lateral;
            _endRadius = endRadius;
            _heightAt = heightAt;
            _onDone = onDone;
            _speed = _startSpeed;
            _elapsed = 0f;
            _spin = UnityEngine.Random.onUnitSphere * UnityEngine.Random.Range(180f, 540f);

            Apply();
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            _elapsed += dt;

            _speed = Mathf.Min(_speed + _accel * dt, _maxSpeed);
            _radius += _speed * dt;
            transform.Rotate(_spin * dt, Space.Self);
            Apply();

            if (_radius >= _endRadius || _elapsed >= _maxLifetime)
            {
                var cb = _onDone;
                _onDone = null;
                cb?.Invoke(this); // ObjectPool.Release → SetActive(false) で Update 停止
            }
        }

        // 現在の radius/lateral を斜面上のワールド座標へ写す（y は斜面に乗せる・ADR-21）。
        private void Apply()
        {
            Vector3 p = _outwardDir * _radius + _perpDir * _lateral;
            p.y = _heightAt != null ? _heightAt(p.x, p.z) : 0f;
            transform.position = p;
        }
    }
}
