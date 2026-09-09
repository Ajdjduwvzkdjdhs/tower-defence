using UnityEngine;
using UnityEngine.InputSystem;
using KakigoriTD.Data;
using KakigoriTD.Input;

namespace KakigoriTD.Gameplay
{
    /// <summary>
    /// 手動キャリブレーション（山の中心の平行移動オフセット <c>field_offset</c>・phase-preview-feedback.md §3.9）。
    /// <see cref="LidarPreviewConfigSO.FieldOffsetKey"/>（既定 <c>C</c>）を長押し中だけ照準オーバーレイを出し、
    /// 矢印キーで <see cref="LidarPositionInputSource.FieldOffsetMm"/> を <see cref="LidarPreviewConfigSO.FieldOffsetStepMm"/>
    /// （既定 10mm）ずつ nudge する。キーを離したら PlayerPrefs へ保存。角度・回転・反転は触らない。
    /// LiDAR 実ファイル経路（<see cref="LidarPositionInputSource"/>）のときだけ配線される。
    /// </summary>
    public class LidarCalibration : MonoBehaviour
    {
        private LidarPositionInputSource _src;
        private LidarPreviewConfigSO _config;
        private Camera _cam;
        private bool _active;

        public void Configure(LidarPositionInputSource src, LidarPreviewConfigSO config, Camera cam)
        {
            _src = src;
            _config = config;
            _cam = cam != null ? cam : Camera.main;
        }

        private void Update()
        {
            if (_src == null || _config == null) return;
            var kb = Keyboard.current;
            if (kb == null) return;

            bool held = kb[_config.FieldOffsetKey].isPressed;

            if (held)
            {
                _active = true;
                float step = _config.FieldOffsetStepMm;
                Vector2 off = _src.FieldOffsetMm;
                if (kb.rightArrowKey.wasPressedThisFrame) off.x += step;
                if (kb.leftArrowKey.wasPressedThisFrame) off.x -= step;
                if (kb.upArrowKey.wasPressedThisFrame) off.y += step;
                if (kb.downArrowKey.wasPressedThisFrame) off.y -= step;
                if (kb.backspaceKey.wasPressedThisFrame) { _src.ResetFieldOffset(); return; }
                _src.FieldOffsetMm = off;
            }
            else if (_active)
            {
                _active = false;
                _src.SaveFieldOffset();
            }
        }

        private void OnGUI()
        {
            if (!_active) return;

            var box = new Rect(12, 12, 460, 74);
            GUI.Box(box, GUIContent.none);
            GUI.Label(new Rect(box.x + 8, box.y + 6, box.width - 16, 20),
                $"[field_offset] {_src.FieldOffsetMm.x:0.#}, {_src.FieldOffsetMm.y:0.#} mm");
            GUI.Label(new Rect(box.x + 8, box.y + 26, box.width - 16, 20),
                $"矢印 ±{_config.FieldOffsetStepMm:0.#}mm / Backspace=reset / 離すと保存");
            GUI.Label(new Rect(box.x + 8, box.y + 46, box.width - 16, 20),
                _src.IsLost ? "LiDAR: LOST" : (_src.HasPoint ? $"raw {_src.RawPointMm.x:0}, {_src.RawPointMm.y:0} mm" : "検知なし"));

            if (_cam == null) return;

            // ワールド原点（山の中心）に十字。
            DrawCross(_cam.WorldToScreenPoint(Vector3.zero), 14f, "山中心");
            // 補正後の検知点（雪玉が出る位置）。
            if (_src.HasPoint)
                DrawCross(_cam.WorldToScreenPoint(_src.WorldPoint), 8f, "検知点");
        }

        private static void DrawCross(Vector3 screen, float r, string label)
        {
            if (screen.z <= 0f) return;
            float x = screen.x;
            float y = Screen.height - screen.y; // GUI は左上原点
            GUI.Label(new Rect(x - r, y - r - 2f, r * 2f, r * 2f), "+");
            GUI.Label(new Rect(x + r, y - 8f, 80f, 18f), label);
        }
    }
}
