using System;
using UnityEngine;
using KakigoriTD.Core;
using KakigoriTD.Data;
using KakigoriTD.Factory;
using KakigoriTD.Input;

namespace KakigoriTD.Gameplay
{
    /// <summary>
    /// LiDAR 位置入力（連続座標）版の雪崩。谷を持たず、検知点 P（<see cref="IPositionInputSource.WorldPoint"/>）
    /// から**山中心を通る放射方向へ**雪玉ストリームを放出し、雪玉が半径内の敵を 1 体撃破する
    /// （phase-preview-feedback.md §3.4）。旧 AvalancheVfx（谷ストリーム＋ADR-27 先頭行到達）の
    /// 置き換え。物理不使用・雪玉はプール（ADR-4/21/25/27 の方針は踏襲）。
    ///
    /// d（＝検知点の中心距離[mm]）に連動して:
    ///  - **撃破クールダウン** <see cref="LidarPreviewConfigSO.CooldownSecFor"/>（中心 0.6s → 外周 0.0s）
    ///  - **拡散の角度ばらつき** <see cref="LidarPreviewConfigSO.SpreadDegFor"/>（中心 ±25° → 外周 ±3°）
    /// 中心付近＝広く弱い / 外周＝狭く強い。
    ///
    /// 氷消費は <see cref="LidarPreviewConfigSO.ConsumeIce"/>（既定 false＝無限）で無効。true のとき
    /// <see cref="IceStock"/> でゲートする分岐だけ残す（保険）。
    /// </summary>
    public class LidarAvalancheVfx : MonoBehaviour
    {
        [Tooltip("consumeIce=true のときの氷消費レート[/s]（既定は無限なので未使用）。")]
        [SerializeField] private float _iceDrainPerSec = 1.0f;

        private IPositionInputSource _input;
        private EnemyField _field;
        private Func<float, float, float> _heightAt;
        private ObjectPool<Snowball> _pool;
        private LidarPreviewConfigSO _config;
        private IceStock _ice;

        private float _spawnAccum;
        private Vector3 _lastDir = Vector3.forward;
        private float _lastDMm;
        private bool _wasEmitting;

        /// <summary>放出（＝雪崩）の on/off が変化したフレームに発火（音の雪崩ループ用・エッジ）。</summary>
        public event Action<bool> OnAvalancheChanged;

        /// <summary>このフレームに放出しているか（デバッグ表示用）。</summary>
        public bool IsEmitting => _wasEmitting;

        public void Configure(IPositionInputSource input, EnemyField field,
            Func<float, float, float> heightAt, Func<Snowball> createSnowball,
            LidarPreviewConfigSO config, IceStock ice = null)
        {
            _input = input;
            _field = field;
            _heightAt = heightAt;
            _config = config;
            _ice = ice;
            _pool = new ObjectPool<Snowball>(createSnowball, prewarm: 24);
            _spawnAccum = 0f;
            _wasEmitting = false;
        }

        private void Update()
        {
            bool emitting = false;

            if (_input != null && _field != null && _config != null && _pool != null
                && _input.HasPoint && _input.IsShaving && !_input.IsLost)
            {
                bool iceOk = !_config.ConsumeIce || _ice == null
                             || (!_ice.IsEmpty && _ice.TryDrain(_iceDrainPerSec * Time.deltaTime));
                if (iceOk)
                {
                    emitting = true;
                    Emit();
                }
            }

            if (!emitting) _spawnAccum = 0f; // 溜まった分が次に噴き出さないよう 0 に戻す

            if (emitting != _wasEmitting)
            {
                _wasEmitting = emitting;
                OnAvalancheChanged?.Invoke(emitting);
            }
        }

        private void Emit()
        {
            Vector3 p = _input.WorldPoint;
            _lastDMm = _input.DistanceMm;

            Vector3 flat = new Vector3(p.x, 0f, p.z);
            float startRadius = flat.magnitude;
            Vector3 radialDir = startRadius > 1e-3f ? flat / startRadius : _lastDir; // 中心ちょうどは前フレームの向き
            _lastDir = radialDir;

            float spreadDeg = _config.SpreadDegFor(_lastDMm);

            _spawnAccum += _config.SpawnRate * Time.deltaTime;
            while (_spawnAccum >= 1f)
            {
                _spawnAccum -= 1f;
                float a = UnityEngine.Random.Range(-spreadDeg, spreadDeg);
                Vector3 dir = Quaternion.AngleAxis(a, Vector3.up) * radialDir; // 拡散＝方位角を回した放射方向
                _pool.Get().Launch(dir, startRadius, 0f, _config.EndRadius, _heightAt, _pool.Release, TryKill);
            }
        }

        // 雪玉が毎フレーム自分の現在位置で呼ぶ。cooldown は「今フレームの検知点の d」（切れていたら直近の d）。
        private bool TryKill(Vector3 worldPos)
        {
            float d = (_input != null && _input.HasPoint) ? _input.DistanceMm : _lastDMm;
            return _field.TryKillNearest(worldPos, _config.KillRadiusUnits, _config.CooldownSecFor(d), Time.time);
        }
    }
}
