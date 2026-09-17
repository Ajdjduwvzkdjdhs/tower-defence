using System;
using UnityEngine;

namespace KakigoriTD.Gameplay
{
    /// <summary>
    /// 敵プレハブに付けておくと、撃破時に <see cref="ExplosionTelegraph"/> を再生する。
    /// モデル・傾き・爆発演出を1つのプレハブにまとめ、<see cref="Bootstrap.GameManager"/> の
    /// 敵プレハブ差し替え1箇所だけで「爆発する敵かどうか」も一緒に切り替わるようにするための部品。
    /// このコンポーネントが無い敵（例: 通常の赤い敵兵）は <see cref="EnemyField"/> 側で何もしない。
    /// </summary>
    public class ExplosiveDeath : MonoBehaviour
    {
        [SerializeField] private ExplosionTelegraph _explosionPrefab;

        /// <summary>撃破された瞬間に呼ぶ。onExplode は爆発した瞬間に (中心位置, 半径) を受け取るコールバック。</summary>
        public void Explode(Action<Vector3, float> onExplode)
        {
            if (_explosionPrefab == null) return;
            var telegraph = Instantiate(_explosionPrefab, transform.position, Quaternion.identity);
            telegraph.PlayImmediate(onExplode);
        }

        /// <summary>
        /// 撃破された瞬間に、見た目の半径を <paramref name="radius"/> に合わせて爆発演出だけ再生する。
        /// 巻き込みの判定はロジック側（<c>EnemyField</c> の ExplosionChain・ADR-30）が済ませているため
        /// コールバックは持たない。半径の正は StageConfigSO で、プレハブ側の値は使わない。
        /// </summary>
        public void Explode(float radius)
        {
            if (_explosionPrefab == null) return;
            var telegraph = Instantiate(_explosionPrefab, transform.position, Quaternion.identity);
            telegraph.PlayImmediate(radius, null);
        }
    }
}
