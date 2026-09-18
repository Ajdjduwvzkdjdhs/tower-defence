using System.Collections.Generic;
using UnityEngine;

namespace KakigoriTD.Gameplay
{
    /// <summary>
    /// 雪崩の粒 1 個が「どの敵に当たったか」を選ぶ純ロジック（ADR-32・ParticleContact モード）。
    /// UnityEngine の数学型のみ使用（シーン非依存）で EditMode 検証できる。<see cref="EnemyKillSelection"/> と同じ流儀。
    ///
    /// 規則:
    ///  1. XZ 距離が <c>radius</c>（敵の半身 ＋ 粒の半径）以内の生存敵を候補にする。
    ///  2. さらに粒の Y が敵の Y ± <c>heightTolerance</c> の中にあること（地形の下に落ちた粒・高く跳ねた粒は当たらない）。
    ///  3. 候補のうち XZ で最も近い 1 体。該当なしなら -1。
    /// 前列ルールは持たない：粒は当たった敵に吸収されて消えるので、前列が生きている間は後列に届かない（自然に前から倒れる）。
    /// </summary>
    public static class AvalancheContact
    {
        /// <param name="positions">敵のワールド位置。</param>
        /// <param name="alive">同順の生存フラグ。</param>
        /// <param name="particle">粒のワールド位置。</param>
        /// <param name="radius">接触半径（XZ）。</param>
        /// <param name="heightTolerance">接触とみなす高さの許容幅（±）。負なら高さを見ない。</param>
        /// <returns>当たった敵の添字。該当なしなら -1。</returns>
        public static int FindHit(IReadOnlyList<Vector3> positions, IReadOnlyList<bool> alive,
            Vector3 particle, float radius, float heightTolerance)
        {
            int n = positions.Count;
            float r2 = radius * radius;

            int best = -1;
            float bestSq = float.PositiveInfinity;

            for (int i = 0; i < n; i++)
            {
                if (i >= alive.Count || !alive[i]) continue;

                Vector3 p = positions[i];
                float dx = p.x - particle.x;
                float dz = p.z - particle.z;
                float sq = dx * dx + dz * dz;
                if (sq > r2) continue;

                if (heightTolerance >= 0f)
                {
                    float dy = particle.y - p.y;
                    if (dy > heightTolerance || dy < -heightTolerance) continue;
                }

                if (sq < bestSq) { bestSq = sq; best = i; }
            }

            return best;
        }
    }
}
