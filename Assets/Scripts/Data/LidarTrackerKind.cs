namespace KakigoriTD.Data
{
    /// <summary>
    /// Unity が読む LiDAR 落氷検知の共有ファイルを、どのトラッカー（<c>tools/</c> 配下）の出力にするかの選択
    /// （Issue #83 / ADR-33）。<see cref="LidarPreviewConfigSO"/> の Inspector で選ぶと
    /// <see cref="LidarPreviewConfigSO.PositionFileRelative"/> がそのトラッカーの既定出力先へ切り替わる。
    /// 出力の契約（<c>seq,x,y,shaving</c>）は全トラッカーで同じなので、違うのはパスだけ。
    /// </summary>
    public enum LidarTrackerKind
    {
        /// <summary><c>tools/lidar-tracker/</c>（YDLIDAR T-mini Plus 単体）。既存アセットの既定（値 0）。</summary>
        LidarTracker = 0,

        /// <summary><c>tools/lidar-tracker3/</c>（Slamtec RPLIDAR S1 単体）。</summary>
        LidarTracker3 = 1,

        /// <summary><c>tools/lidar-tracker1+3/</c>（T-mini Plus + RPLIDAR S1 ペアの交点法を束ねた版）。展示本番。</summary>
        LidarTracker1Plus3 = 2,

        /// <summary>プリセットを使わず <c>Position File Relative</c> に書いたパスをそのまま使う。</summary>
        Custom = 3,
    }
}
