using UnityEngine;

namespace KakigoriTD.Input
{
    /// <summary>
    /// LiDAR 原点フレームの検知点[mm] を、山中心（ワールド原点）からのゲームワールド XZ オフセットへ写す
    /// 純関数（docs/phase-preview-feedback.md §3.2）。UnityEngine の数学型のみ使用（シーン非依存）で
    /// EditMode 検証できる。
    ///
    /// 手順:
    ///  1. <c>local = rawMm − centerMm</c>（centerMm ＝ 円中心オフセット ＋ キャリブ field_offset）
    ///  2. <c>dMm = clamp(|local|, 0, radiusMm)</c>（許容帯の点は radiusMm にクランプ）
    ///  3. local を <c>worldRotationDeg</c> 回転 → flipX で x 反転 → flipY で y 反転
    ///     （設置時に一度だけ config で確定。プロジェクタ鏡像 / モニタ直の差を吸収）
    ///  4. <c>scaleUnitsPerMm</c> を掛けて (X, Z) オフセット（既定 x→X, y→Z）
    /// </summary>
    public static class LidarPointMapper
    {
        public struct Result
        {
            /// <summary>山中心（原点）からのゲームワールド XZ オフセット（Y は呼び出し側が HeightAt で載せる）。</summary>
            public Vector2 WorldOffsetXZ;
            /// <summary>検知点の中心距離[mm]（0..radiusMm にクランプ済み）。cooldown/spread の d。</summary>
            public float DistanceMm;
        }

        public static Result Map(Vector2 rawMm, Vector2 centerMm, float worldRotationDeg,
            bool flipX, bool flipY, float scaleUnitsPerMm, float radiusMm)
        {
            Vector2 local = rawMm - centerMm;

            float rMax = Mathf.Max(0f, radiusMm);
            float dMm = Mathf.Clamp(local.magnitude, 0f, rMax);

            float rad = worldRotationDeg * Mathf.Deg2Rad;
            float c = Mathf.Cos(rad);
            float s = Mathf.Sin(rad);
            Vector2 r = new Vector2(local.x * c - local.y * s, local.x * s + local.y * c);
            if (flipX) r.x = -r.x;
            if (flipY) r.y = -r.y;

            return new Result
            {
                WorldOffsetXZ = r * scaleUnitsPerMm,
                DistanceMm = dMm,
            };
        }
    }
}
