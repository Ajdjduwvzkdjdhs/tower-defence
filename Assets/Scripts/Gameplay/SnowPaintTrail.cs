using UnityEngine;
using KakigoriTD.Data;

namespace KakigoriTD.Gameplay
{
    /// <summary>
    /// <see cref="SnowPaintTrailBuffer"/>（純粋ロジック）の内容を、山オーバーレイの
    /// <see cref="MaterialPropertyBlock"/> へ変換するだけの薄い入出力アダプタ。
    /// <see cref="LidarAvalancheVfx.OnAvalanchePassed"/> の購読先として
    /// <see cref="AddPoint"/> をそのまま渡す想定（GameManager が配線する）。
    /// </summary>
    public class SnowPaintTrail : MonoBehaviour
    {
        private const int SectorMax = 24; // Custom/SnowPaintOverlay の SECTOR_MAX と一致させる

        private static readonly int IdSectorTimes = Shader.PropertyToID("_SectorTimes");
        private static readonly int IdPaintNow = Shader.PropertyToID("_PaintNow");

        private MeshRenderer _renderer;
        private SnowPaintTrailBuffer _buffer;
        private MaterialPropertyBlock _block;
        private Vector4[] _upload; // xy = (最終塗布時刻, 起点半径)。float4[]（SetVectorArray）で渡す

        public void Configure(MeshRenderer overlayRenderer, SnowPaintConfigSO config)
        {
            _renderer = overlayRenderer;
            _buffer = new SnowPaintTrailBuffer(SectorMax, config.SectorDeg, config.PaintFadeSec);
            _block = new MaterialPropertyBlock();
            _upload = new Vector4[SectorMax];
        }

        public void AddPoint(Vector3 worldPos)
        {
            _buffer?.MarkSector(worldPos, Time.time);
        }

        private void LateUpdate()
        {
            if (_buffer == null || _renderer == null) return;

            _buffer.BuildUploadArray(_upload);
            _renderer.GetPropertyBlock(_block);
            _block.SetVectorArray(IdSectorTimes, _upload);
            _block.SetFloat(IdPaintNow, Time.time);
            _renderer.SetPropertyBlock(_block);
        }
    }
}
