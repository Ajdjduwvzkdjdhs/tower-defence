using UnityEngine;

namespace KakigoriTD.Gameplay
{
    /// <summary>
    /// 盾兵（ADR-30）。雪玉では倒しにくく、<see cref="MaxHits"/> 発当てて初めて撃破される。
    /// 爆発（爆発兵の連鎖）は耐久を無視して一撃で倒す＝「爆弾を使えば早い」を学習させる。
    ///
    /// 完全無敵にしないのは、<c>EnemyKillSelection.SelectFront</c> が「重なり半径内により古い生存敵が
    /// いる敵は前列でない」として除外するため、不死身の盾が居座るとその背後が永久に撃破不能になるから。
    /// 耐久制なら必ず退くので詰みが起きない。
    ///
    /// ヒットのたびに per-instance MPB で色を暗くし、残り耐久を視覚的に伝える（sharedMaterial は汚さない）。
    /// 元の色はマテリアルから読み取って保存し、それを暗くする（モデル本来の色を白く飛ばさないため）。
    /// </summary>
    public class ShieldTrooper : Enemy
    {
        private int _maxHits = 5;
        private int _hitsLeft;
        private Renderer[] _renderers;
        private Color[] _baseColors;     // 元の色（プール再利用で失わないよう最初の 1 回だけ読む）
        private MaterialPropertyBlock _hitMpb;

        /// <summary>雪玉で撃破するのに必要なヒット数。</summary>
        public int MaxHits => _maxHits;

        /// <summary>残り耐久（0 で撃破）。</summary>
        public int HitsLeft => _hitsLeft;

        /// <summary>スポーン時に耐久を与える（プール再利用のたびに呼ばれる）。EnemyField が Init 前に呼ぶ。</summary>
        public void SetMaxHits(int hits)
        {
            _maxHits = hits < 1 ? 1 : hits;
        }

        // Init 完了フック（DisguisedEnemy の tell マーカー生成に使われていた口をそのまま流用）。
        protected override void OnInitialized()
        {
            _hitsLeft = _maxHits;
            if (_renderers == null) CacheRenderers();
            ApplyDamageTint();
        }

        /// <summary>
        /// 雪玉が 1 発当たった。耐久を 1 減らし、0 になったら true（＝撃破）を返す。
        /// 残っていれば false（雪玉は消費されるが敵は倒れない）。
        /// </summary>
        public bool TakeSnowballHit()
        {
            if (!IsAlive) return false;
            _hitsLeft--;
            if (_hitsLeft > 0)
            {
                ApplyDamageTint();
                return false;
            }
            _hitsLeft = 0;
            return true;
        }

        // Renderer と元の色を 1 度だけ拾う。色はマテリアルの _BaseColor（無ければ白）。
        private void CacheRenderers()
        {
            _renderers = GetComponentsInChildren<Renderer>(true);
            _baseColors = new Color[_renderers.Length];
            _hitMpb = new MaterialPropertyBlock();

            for (int i = 0; i < _renderers.Length; i++)
            {
                var mat = _renderers[i] != null ? _renderers[i].sharedMaterial : null;
                _baseColors[i] = (mat != null && mat.HasProperty("_BaseColor")) ? mat.GetColor("_BaseColor") : Color.white;
            }
        }

        // 残り耐久に応じて暗くする（満タン＝元の色、瀕死＝暗い）。正式な表現は後日差し替え。
        private void ApplyDamageTint()
        {
            if (_renderers == null || _hitMpb == null) return;

            float t = _maxHits > 0 ? (float)_hitsLeft / _maxHits : 1f;
            float k = Mathf.Lerp(0.35f, 1f, t);

            for (int i = 0; i < _renderers.Length; i++)
            {
                if (_renderers[i] == null) continue;
                var c = _baseColors[i];
                _hitMpb.SetColor("_BaseColor", new Color(c.r * k, c.g * k, c.b * k, c.a));
                _renderers[i].SetPropertyBlock(_hitMpb);
            }
        }
    }
}
