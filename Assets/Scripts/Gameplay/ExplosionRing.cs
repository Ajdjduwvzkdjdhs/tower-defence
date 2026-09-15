using System;
using UnityEngine;

namespace KakigoriTD.Gameplay
{
    /// <summary>
    /// 爆発のプレースホルダ演出（ADR-30）。爆心から**膨らむ発光球**を爆発半径まで広げてフェードする。
    /// <see cref="SnowPuff"/> と同じ「Play(pos, onDone) で自走し、終わったらプール返却を呼ぶ」形。
    ///
    /// 当初は地面に寝かせた薄い円盤だったが、斜面に半分埋まって見えず、0.35 秒では気づけなかった。
    /// 球体なら真上（プロジェクタ）からでも斜めからでも同じ大きさに見え、範囲がそのまま伝わる。
    ///
    /// **正式な爆発 VFX は別担当が作るため、ここは差し替え前提の簡易版**。
    /// 差し替え時に触るのは本ファイルと <c>GameManager.CreateExplosionRing()</c> の 2 箇所だけで済むよう、
    /// <c>EnemyField</c> からの呼び出し口は <see cref="Play"/> 1 つに閉じている。
    /// </summary>
    public class ExplosionRing : MonoBehaviour
    {
        private const float Duration = 0.6f;
        private const float ExpandPortion = 0.35f; // 前半で一気に膨らみ、残りでフェード

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

        /// <summary>指定位置で半径 radius まで膨らむ球を再生する。完了時に onDone（プール返却）を呼ぶ。</summary>
        public void Play(Vector3 pos, float radius, Action<ExplosionRing> onDone)
        {
            transform.position = pos + Vector3.up * 0.4f; // 敵の胴の高さに爆心を置く
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

        // 直径 0 → 2r へ ease-out で膨らみ、膨らみ切ってから輝度を落とす。
        private void ApplyVisual(float t)
        {
            float grow = Mathf.Clamp01(t / ExpandPortion);
            grow = 1f - (1f - grow) * (1f - grow);            // ease-out
            float d = Mathf.Lerp(0.3f, _radius * 2f, grow);   // Sphere primitive は直径 1
            transform.localScale = Vector3.one * d;

            if (_renderer == null || _mpb == null) return;
            float fade = t < ExpandPortion ? 1f : 1f - (t - ExpandPortion) / (1f - ExpandPortion);
            float a = fade * 0.9f;
            // 加算合成前提: 中心が白熱し、外へ行くほど橙〜赤に落ちる色を時間で表す。
            var c = Color.Lerp(new Color(1f, 0.95f, 0.7f), new Color(1f, 0.25f, 0.08f), grow);
            _mpb.SetColor("_BaseColor", new Color(c.r * a, c.g * a, c.b * a, 1f));
            _renderer.SetPropertyBlock(_mpb);
        }
    }
}
