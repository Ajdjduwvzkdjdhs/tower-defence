using System.Collections.Generic;

namespace KakigoriTD.Core
{
    /// <summary>
    /// 爆発兵の誘爆連鎖の探索（ADR-30）。純ロジック（ADR-8）— UnityEngine 非依存。
    ///
    /// 起点の爆発兵から半径内の生存敵を全滅させ、その中に爆発兵がいれば**そこを新たな起点として
    /// 再帰的に伝播**する（連鎖は無制限。制御は出現率と爆発半径という可視のパラメータのみで行う）。
    /// 探索は BFS で、同じ敵を二度爆発させない。
    ///
    /// 上位層（<c>EnemyField</c>）は XZ 座標の配列と「爆発兵か」「生存しているか」のフラグを渡し、
    /// 返ってきた撃破インデックス列に対して撃破処理と演出を流す。
    /// 座標は XZ 平面の 2 要素で扱う（本作は高さを判定に使わない）。
    /// </summary>
    public static class ExplosionChain
    {
        /// <summary>
        /// <paramref name="originIndex"/> の爆発から始まる連鎖を解決し、撃破される敵のインデックスを
        /// **爆発の波及順**で <paramref name="killed"/> に積む（起点自身は含まない）。
        /// 爆発が起きた敵のインデックスは <paramref name="detonated"/> に積む（起点を含む・演出用）。
        /// </summary>
        /// <param name="xs">各敵の X 座標。</param>
        /// <param name="zs">各敵の Z 座標。</param>
        /// <param name="isBomber">各敵が爆発兵か。</param>
        /// <param name="alive">各敵が生存しているか（false は探索対象外）。</param>
        /// <param name="originIndex">連鎖の起点（撃破された爆発兵）。</param>
        /// <param name="radius">爆発半径（ゲーム単位・XZ）。</param>
        /// <param name="killed">撃破されるインデックスの出力先（呼び出し側で Clear 済みのリスト）。</param>
        /// <param name="detonated">爆発したインデックスの出力先（演出用。null 可）。</param>
        public static void Resolve(
            IReadOnlyList<float> xs, IReadOnlyList<float> zs,
            IReadOnlyList<bool> isBomber, IReadOnlyList<bool> alive,
            int originIndex, float radius,
            List<int> killed, List<int> detonated = null)
        {
            if (xs == null || zs == null || isBomber == null || alive == null || killed == null) return;
            int n = xs.Count;
            if (originIndex < 0 || originIndex >= n) return;

            float r2 = radius * radius;

            // 処理済み（爆発済み or 撃破済み）を記録して二重処理を防ぐ。
            var handled = new bool[n];
            handled[originIndex] = true;

            var queue = new Queue<int>();
            queue.Enqueue(originIndex);
            detonated?.Add(originIndex);

            while (queue.Count > 0)
            {
                int center = queue.Dequeue();
                float cx = xs[center];
                float cz = zs[center];

                for (int i = 0; i < n; i++)
                {
                    if (handled[i] || !alive[i]) continue;

                    float dx = xs[i] - cx;
                    float dz = zs[i] - cz;
                    if (dx * dx + dz * dz > r2) continue;

                    handled[i] = true;
                    killed.Add(i); // 盾兵も耐久を無視して即死する（爆発は盾を貫く）。

                    if (isBomber[i])
                    {
                        queue.Enqueue(i); // 誘爆 → さらに伝播
                        detonated?.Add(i);
                    }
                }
            }
        }
    }
}
