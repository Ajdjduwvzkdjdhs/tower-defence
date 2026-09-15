using System.Collections.Generic;
using UnityEngine;

namespace KakigoriTD.Gameplay
{
    /// <summary>
    /// 360度スポーンの敵配置を「列（方位）×段（半径）」のスロットに離散化する純ロジック。
    /// UnityEngine の数学型のみ使用（シーン非依存）で EditMode 検証できる（<see cref="EnemyKillSelection"/> と同じ流儀）。
    ///
    /// 以前は全員が同じ静止半径に立っていたため、同じ方位に湧いた敵が完全に重なって
    /// 1 体にしか見えなかった。列ごとに段（半径）をずらし、後から来た敵を外側へ並べることで
    /// 「前の敵の背後に並んでいる」ことが見て分かるようにする。
    ///
    ///  - 列 <c>column</c>: 0..stepCount-1。方位 = column * 360/stepCount[deg]。
    ///  - 段 <c>slot</c>: 0 が最前列（<c>restRadius</c>）。以降 <c>spacing</c> ずつ外側。
    ///  - 空きスロットは「既存の生存敵の目標位置」から幾何的に判定するので、撃破・退場で
    ///    空いた段はそのまま再利用できる（別途の占有テーブルを持たない）。
    /// </summary>
    public static class EnemyQueueLayout
    {
        /// <summary>spacing の下限（0 割り・完全重なりの防止）。</summary>
        public const float MinSpacing = 0.01f;

        /// <summary>列番号を 0..stepCount-1 に丸める（負値・循環に対応）。</summary>
        public static int NormalizeColumn(int column, int stepCount)
        {
            if (stepCount <= 0) return 0;
            int c = column % stepCount;
            return c < 0 ? c + stepCount : c;
        }

        /// <summary>列の方位[rad]（0 = +Z、時計回りに +X 方向へ）。</summary>
        public static float Azimuth(int column, int stepCount)
        {
            if (stepCount <= 0) return 0f;
            return NormalizeColumn(column, stepCount) * (Mathf.PI * 2f / stepCount);
        }

        /// <summary>列の外向き XZ 単位ベクトル。</summary>
        public static Vector3 Direction(int column, int stepCount)
        {
            float az = Azimuth(column, stepCount);
            return new Vector3(Mathf.Sin(az), 0f, Mathf.Cos(az));
        }

        /// <summary>段の半径（slot 0 = restRadius、以降 spacing ずつ外側）。</summary>
        public static float SlotRadius(int slot, float restRadius, float spacing)
            => restRadius + Mathf.Max(0, slot) * Mathf.Max(MinSpacing, spacing);

        /// <summary>スロットのワールド XZ 位置（y=0。高さは呼び出し側が地形から与える）。</summary>
        public static Vector3 SlotPosition(int column, int slot, int stepCount, float restRadius, float spacing)
            => Direction(column, stepCount) * SlotRadius(slot, restRadius, spacing);

        /// <summary>XZ 位置から最も近い列番号を求める（<see cref="Direction"/> の逆写像）。</summary>
        public static int ColumnOf(Vector3 pos, int stepCount)
        {
            if (stepCount <= 0) return 0;
            float az = Mathf.Atan2(pos.x, pos.z); // Direction の (sin, cos) 規約と対
            int c = Mathf.RoundToInt(az / (Mathf.PI * 2f / stepCount));
            return NormalizeColumn(c, stepCount);
        }

        /// <summary>
        /// 空きスロットを 1 つ確保する。<paramref name="preferredColumn"/> の最前列から外側へ探し、
        /// その列が満段なら ±1, ±2 … と隣の列へ逃がす（＝重ねずに並べる）。
        /// 全列満段なら false（呼び出し側はスポーンを見送る）。
        /// </summary>
        /// <param name="occupiedTargets">既存の生存敵の目標位置（XZ のみ使用）。</param>
        public static bool TryAllocate(IReadOnlyList<Vector3> occupiedTargets, int stepCount, int preferredColumn,
            float restRadius, float spacing, int maxDepth, out int column, out int slot)
        {
            column = NormalizeColumn(preferredColumn, stepCount);
            slot = 0;
            if (stepCount <= 0 || maxDepth <= 0) return false;

            int half = stepCount / 2;
            for (int offset = 0; offset <= half; offset++)
            {
                for (int sign = 1; sign >= -1; sign -= 2)
                {
                    int c = NormalizeColumn(preferredColumn + offset * sign, stepCount);
                    if (TryFreeSlotIn(occupiedTargets, c, stepCount, restRadius, spacing, maxDepth, out int s))
                    {
                        column = c;
                        slot = s;
                        return true;
                    }
                    if (offset == 0) break; // ±0 は同じ列なので 1 回だけ
                }
            }
            return false;
        }

        /// <summary>
        /// 先頭から <paramref name="depth"/> 段が**すべて空いている列**を 1 つ確保する（ペア湧き用・ADR-30）。
        /// 爆発兵（前）＋盾兵（後ろ）のように「同じ列に前後で並べたい」ときに使う。
        /// <paramref name="preferredColumn"/> から ±1, ±2 … と探し、無ければ false。
        /// 呼び出し側は slot 0 .. depth-1 をそのまま使える。
        /// </summary>
        public static bool TryAllocateColumnWithFreeDepth(IReadOnlyList<Vector3> occupiedTargets, int stepCount,
            int preferredColumn, float restRadius, float spacing, int maxDepth, int depth, out int column)
        {
            column = NormalizeColumn(preferredColumn, stepCount);
            if (stepCount <= 0 || depth <= 0 || depth > maxDepth) return false;

            float threshold = Mathf.Max(MinSpacing, spacing) * 0.5f;
            float th2 = threshold * threshold;

            int half = stepCount / 2;
            for (int offset = 0; offset <= half; offset++)
            {
                for (int sign = 1; sign >= -1; sign -= 2)
                {
                    int c = NormalizeColumn(preferredColumn + offset * sign, stepCount);
                    bool allFree = true;
                    for (int s = 0; s < depth && allFree; s++)
                        allFree = !IsOccupied(occupiedTargets, SlotPosition(c, s, stepCount, restRadius, spacing), th2);
                    if (allFree) { column = c; return true; }
                    if (offset == 0) break;
                }
            }
            return false;
        }

        private static bool TryFreeSlotIn(IReadOnlyList<Vector3> occupiedTargets, int column, int stepCount,
            float restRadius, float spacing, int maxDepth, out int slot)
        {
            // 判定閾値は段間隔の半分。隣の段（spacing）や隣の列（>= 2πr/stepCount）には反応しない。
            float threshold = Mathf.Max(MinSpacing, spacing) * 0.5f;
            float th2 = threshold * threshold;
            for (int s = 0; s < maxDepth; s++)
            {
                var p = SlotPosition(column, s, stepCount, restRadius, spacing);
                if (!IsOccupied(occupiedTargets, p, th2)) { slot = s; return true; }
            }
            slot = 0;
            return false;
        }

        private static bool IsOccupied(IReadOnlyList<Vector3> occupiedTargets, Vector3 p, float thresholdSqr)
        {
            if (occupiedTargets == null) return false;
            for (int i = 0; i < occupiedTargets.Count; i++)
            {
                float dx = occupiedTargets[i].x - p.x;
                float dz = occupiedTargets[i].z - p.z;
                if (dx * dx + dz * dz <= thresholdSqr) return true;
            }
            return false;
        }
    }
}
