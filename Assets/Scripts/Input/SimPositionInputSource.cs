using System;
using UnityEngine;
using UnityEngine.InputSystem;
using KakigoriTD.Data;

namespace KakigoriTD.Input
{
    /// <summary>
    /// 開発用の疑似位置入力（phase-preview-feedback.md §3.3）。実 LiDAR なしで
    /// <see cref="IPositionInputSource"/> を満たす。
    ///
    ///  - <b>マウス</b>：ゲーム画面で左ボタン押下中、スクリーンレイと接地面（y=0 平面）の交点を検知点とし、
    ///    押下中 <see cref="IsShaving"/>=true。
    ///  - <b>フォールバック</b>：矢印キーで検知点を XZ 移動、<c>Space</c> で <see cref="IsShaving"/>。
    ///
    /// d[mm] は <see cref="WorldPoint"/> の中心距離から scale で逆算し、radiusMm にクランプする。
    /// </summary>
    public class SimPositionInputSource : MonoBehaviour, IPositionInputSource
    {
        [SerializeField] private float _keyMoveSpeedUnits = 4f;

        private LidarPreviewConfigSO _config;
        private Func<float, float, float> _heightAt;
        private Camera _cam;

        private Vector3 _point;
        private float _dMm;
        private bool _shaving;
        private bool _has;

        public bool HasPoint => _has;
        public Vector3 WorldPoint => _point;
        public float DistanceMm => _dMm;
        public bool IsShaving => _shaving;
        public bool IsLost => false;

        public void Configure(LidarPreviewConfigSO config, Func<float, float, float> heightAt, Camera cam)
        {
            _config = config;
            _heightAt = heightAt;
            _cam = cam != null ? cam : Camera.main;
            SetPoint(Vector3.zero);
        }

        private void Update()
        {
            if (_config == null) return;
            var mouse = Mouse.current;
            var kb = Keyboard.current;

            if (mouse != null && mouse.leftButton.isPressed && _cam != null && TryMouseGround(out Vector3 gp))
            {
                SetPoint(gp);
                _shaving = true;
                _has = true;
                return;
            }

            if (kb != null)
            {
                Vector3 d = Vector3.zero;
                if (kb.upArrowKey.isPressed) d.z += 1f;
                if (kb.downArrowKey.isPressed) d.z -= 1f;
                if (kb.rightArrowKey.isPressed) d.x += 1f;
                if (kb.leftArrowKey.isPressed) d.x -= 1f;
                bool space = kb.spaceKey.isPressed;
                if (d != Vector3.zero || space)
                {
                    if (d != Vector3.zero)
                        SetPoint(_point + d.normalized * _keyMoveSpeedUnits * Time.deltaTime);
                    _shaving = space;
                    _has = true;
                    return;
                }
            }

            _shaving = false;
            _has = false; // 疑似入力にホールドは無い（放すと即停止）
        }

        private void SetPoint(Vector3 worldish)
        {
            float y = _heightAt != null ? _heightAt(worldish.x, worldish.z) : 0f;
            _point = new Vector3(worldish.x, y, worldish.z);
            float rUnits = new Vector2(_point.x, _point.z).magnitude;
            float scale = _config.ScaleUnitsPerMm > 1e-6f ? _config.ScaleUnitsPerMm : 1e-6f;
            _dMm = Mathf.Clamp(rUnits / scale, 0f, _config.RadiusMm);
        }

        private bool TryMouseGround(out Vector3 p)
        {
            p = Vector3.zero;
            Ray ray = _cam.ScreenPointToRay(Mouse.current.position.ReadValue());
            var plane = new Plane(Vector3.up, Vector3.zero);
            if (plane.Raycast(ray, out float enter)) { p = ray.GetPoint(enter); return true; }
            return false;
        }
    }
}
