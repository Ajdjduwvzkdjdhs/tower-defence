using UnityEngine;

namespace KakigoriTD.Gameplay
{
    /// <summary>1 谷ぶんの「最近傍の生存敵を探す／1 体撃破する」窓口。<see cref="ChannelController"/> が実装。</summary>
    public interface IEnemyChannel
    {
        /// <summary>worldPos の XZ から maxRadius 内の最も近い生存敵（撃破はしない）。無ければ null。</summary>
        Enemy FindNearestLive(Vector3 worldPos, float maxRadius);

        /// <summary>敵 e を 1 体だけ撃破する（演出＋除去＋繰り上げ＋イベント発火）。</summary>
        void KillOne(Enemy e);
    }

    /// <summary>
    /// 4 谷を横断して「worldPos の半径内で最も近い生存敵を 1 体撃破する」を提供する
    /// （phase-preview-feedback.md §3.4 / §3.6）。谷選択（channel）を持たない LiDAR 位置入力経路の
    /// 撃破解決口。撃破の**グローバルなクールダウン**（検知点の中心距離 d に連動する間隔）もここで管理する。
    ///
    /// MonoBehaviour ではなく、Composition Root（GameManager）が 4 谷を渡して生成する。
    /// </summary>
    public class EnemyRegistry
    {
        private readonly IEnemyChannel[] _channels;
        private float _lastKillTime = float.NegativeInfinity;

        public EnemyRegistry(IEnemyChannel[] channels)
        {
            _channels = channels;
        }

        /// <summary>直近の撃破時刻。デバッグ表示用。</summary>
        public float LastKillTime => _lastKillTime;

        /// <summary>
        /// <paramref name="worldPos"/> の XZ から <paramref name="radius"/> 内で最も近い生存敵を 1 体撃破する。
        /// 前回撃破から <paramref name="cooldownSec"/> 未満なら何もせず false（レート制御）。撃破したら
        /// <paramref name="now"/> を記録して true。時刻は引数注入で EditMode から検証できる。
        /// </summary>
        public bool TryKillNearest(Vector3 worldPos, float radius, float cooldownSec, float now)
        {
            if (now - _lastKillTime < cooldownSec) return false;

            Enemy best = null;
            IEnemyChannel owner = null;
            float bestSq = float.PositiveInfinity;
            for (int i = 0; i < _channels.Length; i++)
            {
                var c = _channels[i];
                if (c == null) continue;
                var e = c.FindNearestLive(worldPos, radius);
                if (e == null) continue;
                float dx = e.transform.position.x - worldPos.x;
                float dz = e.transform.position.z - worldPos.z;
                float sq = dx * dx + dz * dz;
                if (sq < bestSq) { bestSq = sq; best = e; owner = c; }
            }

            if (best == null) return false;
            owner.KillOne(best);
            _lastKillTime = now;
            return true;
        }
    }
}
