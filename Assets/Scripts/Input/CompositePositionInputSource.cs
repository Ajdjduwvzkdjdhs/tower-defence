using UnityEngine;

namespace KakigoriTD.Input
{
    /// <summary>
    /// LiDAR 位置入力とマウス疑似入力（<see cref="IPositionInputSource"/>）を OR 合成する
    /// （ユーザー指示：<c>InputMode</c> による切替を廃止し、LiDAR とマウスを常時併用する）。
    ///
    /// LiDAR（<c>primary</c>）に検知点があるときはそちらを優先し、無いときだけマウス疑似入力
    /// （<c>fallback</c>）にフォールバックする。<see cref="LidarPositionProtocol"/> の watchdog が
    /// ロスト時に <c>HasPoint</c> を false へ落とす（フェイルセーフ）ため、フォールバック判定は
    /// <see cref="HasPoint"/> だけで足りる。<see cref="IsLost"/> は診断用に primary 側の生死のみを返す。
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
        /// <summary>LiDAR（primary）側のロスト状態のみを表す診断用フラグ。</summary>
        public bool IsLost => _primary != null && _primary.IsLost;
    }
}
