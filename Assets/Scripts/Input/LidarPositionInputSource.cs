using System;
using System.IO;
using UnityEngine;
using KakigoriTD.Data;

namespace KakigoriTD.Input
{
    /// <summary>
    /// LiDAR 位置検知ファイル（<c>seq,x,y,shaving</c>）を間隔ポーリングして <see cref="IPositionInputSource"/> に
    /// 変換する薄い MonoBehaviour アダプタ（phase-preview-feedback.md §3.2）。パースと watchdog は純ロジック
    /// <see cref="LidarPositionProtocol"/> に委譲し、本クラスは IO（ファイル読み）とパス解決、
    /// そして <see cref="LidarPointMapper"/> による mm → ワールド XZ 写像だけを持つ。
    ///
    /// キャリブレーション（山中心の平行移動オフセット <c>field_offset</c>）は <see cref="FieldOffsetMm"/> に
    /// 保持し、PlayerPrefs で永続化する。<c>LidarCalibration</c> が長押し中に nudge する。角度・回転・反転は
    /// 設置時に <see cref="LidarPreviewConfigSO"/> で確定して以後固定。
    /// </summary>
    public class LidarPositionInputSource : MonoBehaviour, IPositionInputSource
    {
        private const string EnvVar = "KAKIGORI_POSITION_FILE";
        private const string PrefX = "kakigori.lidar.field_offset.x";
        private const string PrefY = "kakigori.lidar.field_offset.y";

        private LidarPositionProtocol _protocol;
        private LidarPreviewConfigSO _config;
        private Func<float, float, float> _heightAt;
        private string _path;
        private float _pollTimer;

        private Vector2 _fieldOffsetMm;
        private Vector3 _worldPoint;
        private float _dMm;

        /// <inheritdoc/>
        public bool HasPoint => _protocol != null && _protocol.HasPoint;
        /// <inheritdoc/>
        public Vector3 WorldPoint => _worldPoint;
        /// <inheritdoc/>
        public float DistanceMm => _dMm;
        /// <inheritdoc/>
        public bool IsShaving => _protocol != null && _protocol.IsShaving;
        /// <inheritdoc/>
        public bool IsLost => _protocol == null || _protocol.IsLost;

        /// <summary>キャリブの山中心オフセット[mm]（config の center_offset_mm に加算される）。</summary>
        public Vector2 FieldOffsetMm { get => _fieldOffsetMm; set => _fieldOffsetMm = value; }

        /// <summary>直近に受信した生の検知点[mm]（LiDAR 原点フレーム）。キャリブ照準の「補正前」マーカー用。</summary>
        public Vector2 RawPointMm
            => _protocol != null ? new Vector2(_protocol.RawPointMm.x, _protocol.RawPointMm.y) : Vector2.zero;

        /// <summary>解決済みの共有ファイルの絶対パス（デバッグ表示用）。</summary>
        public string ResolvedPath => _path;

        public void Configure(LidarPreviewConfigSO config, Func<float, float, float> heightAt)
        {
            _config = config;
            _heightAt = heightAt;
            _protocol = new LidarPositionProtocol(config.LostTimeoutSec);
            _path = ResolvePath(config);
            _fieldOffsetMm = new Vector2(PlayerPrefs.GetFloat(PrefX, 0f), PlayerPrefs.GetFloat(PrefY, 0f));
            Debug.Log($"[LidarPosition] file = {_path} / field_offset = {_fieldOffsetMm} mm");
        }

        /// <summary>現在の field_offset を PlayerPrefs へ保存する（キャリブのキーを離したとき）。</summary>
        public void SaveFieldOffset()
        {
            PlayerPrefs.SetFloat(PrefX, _fieldOffsetMm.x);
            PlayerPrefs.SetFloat(PrefY, _fieldOffsetMm.y);
            PlayerPrefs.Save();
        }

        /// <summary>field_offset を 0 に戻して保存する。</summary>
        public void ResetFieldOffset()
        {
            _fieldOffsetMm = Vector2.zero;
            SaveFieldOffset();
        }

        private void Update()
        {
            if (_protocol == null || _config == null) return;

            _pollTimer += Time.deltaTime;
            if (_pollTimer >= Mathf.Max(0f, _config.PollIntervalSec))
            {
                _pollTimer = 0f;
                string line = TryReadLastLine();
                if (line != null) _protocol.Ingest(line);
            }
            _protocol.Tick(Time.deltaTime);

            if (_protocol.HasPoint)
            {
                var raw = _protocol.RawPointMm;
                var r = LidarPointMapper.Map(new Vector2(raw.x, raw.y),
                    _config.CenterOffsetMm + _fieldOffsetMm,
                    _config.WorldRotationDeg, _config.FlipX, _config.FlipY,
                    _config.ScaleUnitsPerMm, _config.RadiusMm);
                _dMm = r.DistanceMm;
                float y = _heightAt != null ? _heightAt(r.WorldOffsetXZ.x, r.WorldOffsetXZ.y) : 0f;
                _worldPoint = new Vector3(r.WorldOffsetXZ.x, y, r.WorldOffsetXZ.y);
            }
        }

        // 共有ファイルの最終非空行。未生成・ロック等は null（Python は temp→os.replace の原子的 rename）。
        private string TryReadLastLine()
        {
            try
            {
                if (string.IsNullOrEmpty(_path) || !File.Exists(_path)) return null;
                string text = File.ReadAllText(_path);
                if (string.IsNullOrEmpty(text)) return null;
                string[] lines = text.Split('\n');
                for (int i = lines.Length - 1; i >= 0; i--)
                {
                    string s = lines[i].Trim();
                    if (s.Length > 0) return s;
                }
                return null;
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }

        // env KAKIGORI_POSITION_FILE → Editor はプロジェクトルート相対 → ビルドは persistentDataPath。
        private static string ResolvePath(LidarPreviewConfigSO config)
        {
            string env = Environment.GetEnvironmentVariable(EnvVar);
            if (!string.IsNullOrEmpty(env)) return env;

            if (Application.isEditor)
            {
                string root = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
                return Path.GetFullPath(Path.Combine(root, config.PositionFileRelative));
            }
            return Path.Combine(Application.persistentDataPath, config.PositionFilePersistent);
        }
    }
}
