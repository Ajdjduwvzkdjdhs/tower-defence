using System;
using UnityEngine;

namespace KakigoriTD.Gameplay
{
    /// <summary>
    /// 撃破位置に一瞬現れる雪しぶきエフェクト。<see cref="Play"/> で起動し、
    /// 拡大＋輝度フェードアウト後に自動でプール返却コールバックを呼ぶ（ADR-20）。
    /// <see cref="ObjectPool{T}"/> で使い回し、ホットパスで Instantiate しない（ADR-4）。
    /// </summary>
    public class SnowPuff : MonoBehaviour
    {
        private Action<SnowPuff> _onDone;
        private float _elapsed;
        private const float Duration = 0.25f;
        private Renderer _renderer;
        private MaterialPropertyBlock _mpb;

        private void Awake()
        {
            _renderer = GetComponent<Renderer>();
            _mpb = new MaterialPropertyBlock();
        }

        /// <summary>指定位置でエフェクトを起動する。完了時に onDone を呼ぶ（所有者がプール返却する）。</summary>
        public void Play(Vector3 pos, Action<SnowPuff> onDone)
        {
            transform.position = pos;
            _onDone = onDone;
            _elapsed = 0f;
        }

        private void Update()
        {
            _elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(_elapsed / Duration);

            // スケールを拡大（0.15 → 0.6）
            transform.localScale = Vector3.one * Mathf.Lerp(0.15f, 0.6f, t);

            // 加算マテリアルの輝度を 1→0 でフェードアウト
            float brightness = 1f - t;
            _mpb.SetColor("_BaseColor", new Color(brightness, brightness, brightness, 1f));
            _renderer.SetPropertyBlock(_mpb);

            if (t >= 1f)
            {
                var cb = _onDone;
                _onDone = null;
                cb?.Invoke(this); // ObjectPool.Release → SetActive(false) で Update 停止
            }
        }
    }
}
