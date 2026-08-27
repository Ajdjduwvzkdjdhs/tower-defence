using UnityEngine;

namespace KakigoriTD.Input
{
    /// <summary>
    /// LiDAR 位置検知（連続座標）入力の抽象（phase-preview-feedback.md §3.2）。谷選択（channel）を持たない。
    /// 実ファイル経路 = <c>LidarPositionInputSource</c>、開発用（マウス/キー疑似）= <c>SimPositionInputSource</c>。
    /// </summary>
    public interface IPositionInputSource
    {
        /// <summary>検知点があるか（円内で氷を検知、または shaving ホールド中）。</summary>
        bool HasPoint { get; }

        /// <summary>検知点のワールド位置（山中心＝原点基準、Y は斜面に乗せ済み）。<see cref="HasPoint"/> false 時は不定。</summary>
        Vector3 WorldPoint { get; }

        /// <summary>検知点の中心距離[mm]（0..radiusMm にクランプ済み）＝ cooldown / spread の d。</summary>
        float DistanceMm { get; }

        /// <summary>削り中か。</summary>
        bool IsShaving { get; }

        /// <summary>ロスト（Python 停止/ハング）。実ファイル経路のみ意味を持つ（疑似入力は常に false）。</summary>
        bool IsLost { get; }
    }
}
