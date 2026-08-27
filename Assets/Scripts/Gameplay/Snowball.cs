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
    ///
    /// ADR-27 で撃破の駆動を担う: 発射時に渡された「先頭行の谷軸距離」に <see cref="_radius"/> が届いた瞬間、
    /// 1 回だけ撃破コールバック（谷の先頭行を 1 行消す）を呼ぶ。撃破の起点を別フロントの模擬から
    /// 「実際に流れている雪玉の到達」に一本化するため（見た目と撃破の同期・入力ちらつきへの頑健性）。
    /// コールバック未指定（<see cref="Launch"/> の既定 null）なら従来どおり純 cosmetic として振る舞う。
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

        // 撃破駆動（ADR-27）: この雪玉が先頭行の敵の実位置に届いたら 1 回だけ撃破コールバックを呼ぶ。
        private Func<float> _frontRowDistance; // 谷の先頭行の谷軸距離（毎フレーム再評価・敵の前進に追従）。null で純 cosmetic。
        private Action _onReachFrontRow;       // 到達時に 1 回だけ呼ぶ（= 1 雪玉 1 行）。
        private bool _reachedFront;            // 既に発火したか（多重撃破防止）。

        // 一般化位置到達（phase-preview-feedback §3.4）: 谷を持たない LiDAR 経路用。毎フレーム自分の現在位置を渡し、
        // 半径内の敵を 1 体撃破できたら true が返る。true で _reachedFront を立てて以後は純滑走（1 雪玉 1 撃）。
        private Func<Vector3, bool> _tryKillNear;

        /// <summary>
        /// 山頂寄りの一点から谷の外向きへ射出する。完了時に onDone を呼ぶ（所有者がプール返却する）。
        /// </summary>
        /// <param name="outwardDir">谷の外向き単位ベクトル（山頂→麓）。</param>
        /// <param name="startRadius">中心からの射出開始距離（＝山頂寄り）。</param>
        /// <param name="lateral">谷幅方向のオフセット（滑走中は保持）。</param>
        /// <param name="endRadius">麓の距離。ここに達したら返却する。</param>
        /// <param name="heightAt">(x,z)→world Y の斜面サンプラ。</param>
        /// <param name="frontRowDistance">先頭行の敵の谷軸距離を返す関数（毎フレーム再評価）。null で撃破せず純 cosmetic（ADR-27）。</param>
        /// <param name="onReachFrontRow">上記距離に届いた瞬間に 1 回だけ呼ぶ撃破コールバック（= 1 雪玉 1 行）。</param>
        public void Launch(Vector3 outwardDir, float startRadius, float lateral, float endRadius,
            Func<float, float, float> heightAt, Action<Snowball> onDone,
            Func<float> frontRowDistance = null, Action onReachFrontRow = null)
        {
            _outwardDir = outwardDir;
            _perpDir = new Vector3(outwardDir.z, 0f, -outwardDir.x); // XZ 平面での直交（ChannelController と同基底）
            _radius = startRadius;
            _lateral = lateral;
            _endRadius = endRadius;
            _heightAt = heightAt;
            _onDone = onDone;
            _frontRowDistance = frontRowDistance;
            _onReachFrontRow = onReachFrontRow;
            _tryKillNear = null;
            _reachedFront = false;
            _speed = _startSpeed;
            _elapsed = 0f;
            _spin = UnityEngine.Random.onUnitSphere * UnityEngine.Random.Range(180f, 540f);

            Apply();
        }

        /// <summary>
        /// 検知点から放射方向へ射出する一般化版（phase-preview-feedback §3.4）。谷を持たず、滑走中に
        /// <paramref name="tryKillNear"/> が半径内の敵を 1 体撃破できたら以後は純滑走する（1 雪玉 1 撃）。
        /// 拡散は <paramref name="dir"/>（方位角を回した放射方向）で表すため <paramref name="lateral"/> は 0 で呼ぶ。
        /// </summary>
        public void Launch(Vector3 dir, float startRadius, float lateral, float endRadius,
            Func<float, float, float> heightAt, Action<Snowball> onDone, Func<Vector3, bool> tryKillNear)
        {
            _outwardDir = dir;
            _perpDir = new Vector3(dir.z, 0f, -dir.x);
            _radius = startRadius;
            _lateral = lateral;
            _endRadius = endRadius;
            _heightAt = heightAt;
            _onDone = onDone;
            _frontRowDistance = null;
            _onReachFrontRow = null;
            _tryKillNear = tryKillNear;
            _reachedFront = false;
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

            // 撃破駆動（ADR-27）: 先頭行の敵の実位置に届いた瞬間、その谷の先頭行を 1 行だけ撃破。
            // 1 雪玉につき 1 回のみ（_reachedFront）。谷が空なら距離は +∞ で発火しない＝純 cosmetic。
            if (!_reachedFront && _frontRowDistance != null && _radius >= _frontRowDistance())
            {
                _reachedFront = true;
                _onReachFrontRow?.Invoke();
            }

            // 一般化位置到達（phase-preview-feedback §3.4・LiDAR 経路）: 半径内の敵を 1 体撃破できたら以後は純滑走。
            if (!_reachedFront && _tryKillNear != null && _tryKillNear(transform.position))
            {
                _reachedFront = true;
            }

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
