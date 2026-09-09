using System.Collections.Generic;
using UnityEngine;

namespace KakigoriTD.Gameplay
{
    /// <summary>
    /// 雪玉の一般化位置到達で「どの敵を撃破するか」を選ぶ純ロジック（phase-preview-feedback.md / 360度化）。
    /// UnityEngine の数学型のみ使用（シーン非依存）で EditMode 検証できる。
    ///
    /// 規則:
    ///  1. worldPos の XZ から <c>killRadius</c> 内の生存敵を候補にする。
    ///  2. 候補のうち、<c>overlapRadius</c> 内に**より古い生存敵**がいる敵は「まだ前列でない」として除外
    ///     （先にいた方が最前列＝先に剥がれる。深さ制御）。除外は候補以外の古い敵も対象。
    ///  3. 残った敵のうち worldPos に最も近い 1 体を選ぶ。全滅なら -1。
    /// </summary>
    public static class EnemyKillSelection
    {
        /// <param name="positions">敵のワールド位置。添字 0 = 最古（古い順）。</param>
        /// <param name="alive">同順の生存フラグ。</param>
        /// <returns>撃破する敵の添字。該当なしなら -1。</returns>
        public static int SelectFront(IReadOnlyList<Vector3> positions, IReadOnlyList<bool> alive,
            Vector3 worldPos, float killRadius, float overlapRadius)
        {
            int n = positions.Count;
            float kr2 = killRadius * killRadius;
            float or2 = overlapRadius * overlapRadius;

            int best = -1;
            float bestSq = float.PositiveInfinity;

            for (int i = 0; i < n; i++)
            {
                if (i >= alive.Count || !alive[i]) continue;

                float dxk = positions[i].x - worldPos.x;
                float dzk = positions[i].z - worldPos.z;
                float sq = dxk * dxk + dzk * dzk;
                if (sq > kr2) continue;

                // i より古い（添字が小さい）生存敵が overlap 内にいたら、i はまだ前列ではない。
                bool blockedByOlder = false;
                for (int j = 0; j < i; j++)
                {
                    if (j >= alive.Count || !alive[j]) continue;
                    float ox = positions[j].x - positions[i].x;
                    float oz = positions[j].z - positions[i].z;
                    if (ox * ox + oz * oz <= or2) { blockedByOlder = true; break; }
                }
                if (blockedByOlder) continue;

                if (sq < bestSq) { bestSq = sq; best = i; }
            }

            return best;
        }
    }
}
