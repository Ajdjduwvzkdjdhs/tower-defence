using UnityEngine;

namespace KakigoriTD.Input
{
    /// <summary>
    /// LiDAR 位置入力とマウス疑似入力（<see cref="IPositionInputSource"/>）を OR 合成する
    /// （ユーザー指示：<c>InputMode</c> による切替を廃止し、LiDAR とマウスを常時併用する）。
    ///
    /// LiDAR（<c>primary</c>）に検知点があるときはそちらを優先し、無いとき（未接続・検知範囲外）だけ
    /// マウス疑似入力（<c>fallback</c>）にフォールバックする。<see cref="IsLost"/> を含む全プロパティを
    /// 「現在有効な方（<see cref="Active"/>）」から取る。LiDAR 未接続時は
    /// <see cref="LidarPositionProtocol"/> の watchdog により <c>primary.IsLost</c> が常時 true になるが、
    /// このとき <see cref="Active"/> はマウス側（常に <c>IsLost=false</c>）に切り替わっているため、
    /// フォールバック中に「ロスト＝操作不可」扱いになって詰まることはない。
    /// </summary>
    public class CompositePositionInputSource : IPositionInputSource
    {
        private readonly IPositionInputSource _primary;  // LiDAR
        private readonly IPositionInputSource _fallback; // マウス疑似

        public CompositePositionInputSource(IPositionInputSource primary, IPositionInputSource fallback)
        {
            _primary = primary;
            _fallback = fallback;
        }

        private IPositionInputSource Active
            => (_primary != null && _primary.HasPoint) ? _primary : _fallback;

        /// <inheritdoc/>
        public bool HasPoint => Active != null && Active.HasPoint;
        /// <inheritdoc/>
        public Vector3 WorldPoint => Active != null ? Active.WorldPoint : Vector3.zero;
        /// <inheritdoc/>
        public float DistanceMm => Active != null ? Active.DistanceMm : 0f;
        /// <inheritdoc/>
        public bool IsShaving => Active != null && Active.IsShaving;
        /// <inheritdoc/>
        public bool IsLost => Active != null && Active.IsLost;
    }
}
