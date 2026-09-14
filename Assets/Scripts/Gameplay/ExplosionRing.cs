using System;
using UnityEngine;

namespace KakigoriTD.Gameplay
{
    /// <summary>
    /// 爆発のプレースホルダ演出（ADR-30）。敵の足元から赤い円が爆発半径まで広がってフェードする。
    /// <see cref="SnowPuff"/> と同じ「Play(pos, onDone) で自走し、終わったらプール返却を呼ぶ」形。
    ///
    /// **正式な爆発 VFX は別担当が作るため、ここは差し替え前提の簡易版**。
    /// 差し替え時に触るのは本ファイルと <c>GameManager.CreateExplosionRing()</c> の 2 箇所だけで済むよう、
    /// <c>EnemyField</c> からの呼び出し口は <see cref="Play"/> 1 つに閉じている。
    /// </summary>
    public class ExplosionRing : MonoBehaviour
    {
        private const float Duration = 0.35f;
        private const float GroundOffset = 0.05f; // 斜面に埋まらないよう少し浮かせる

        private Action<ExplosionRing> _onDone;
        private float _elapsed;
        private float _radius = 3f;
        private Renderer _renderer;
        private MaterialPropertyBlock _mpb;

        private void Awake()
        {
            _renderer = GetComponent<Renderer>();
            _mpb = new MaterialPropertyBlock();
        }

        /// <summary>指定位置で半径 radius まで広がる円を再生する。完了時に onDone（プール返却）を呼ぶ。</summary>
        public void Play(Vector3 pos, float radius, Action<ExplosionRing> onDone)
        {
            transform.position = pos + Vector3.up * GroundOffset;
            _radius = radius <= 0f ? 0.01f : radius;
            _onDone = onDone;
            _elapsed = 0f;
            ApplyVisual(0f);
        }

        private void Update()
        {
            _elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(_elapsed / Duration);
            ApplyVisual(t);

            if (t >= 1f)
            {
                var cb = _onDone;
                _onDone = null;
                cb?.Invoke(this);
            }
        }

        // 円盤（薄い Cylinder）の直径を 0→2r まで広げ、赤の輝度を落としながら消す。
        private void ApplyVisual(float t)
        {
            float d = Mathf.Lerp(0f, _radius * 2f, t);
            transform.localScale = new Vector3(d, 0.01f, d); // Cylinder は高さ 2 units なので薄く潰す

            if (_renderer == null || _mpb == null) return;
            float a = 1f - t;
            _mpb.SetColor("_BaseColor", new Color(1f * a, 0.18f * a, 0.10f * a, 1f)); // 加算マテリアル前提
            _renderer.SetPropertyBlock(_mpb);
        }
    }
}
