using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;
using KakigoriTD.UI;

namespace KakigoriTD.Scenes
{
    /// <summary>
    /// ゲームオーバー（砦陥落）時の画面。<see cref="GameResultHolder"/> の結果から
    /// **到達撃破数**を表示する（クリアタイムはクリアした人にしか存在しないため・ADR-30）。
    /// <c>Enter</c> でタイトルへ戻る。表示は IMGUI（ADR-13）。
    /// </summary>
    public class GameOverScene : MonoBehaviour
    {
        void Update()
        {
            if (Keyboard.current != null &&
                Keyboard.current.enterKey.wasPressedThisFrame)
            {
                GameResultHolder.Clear();
                SceneManager.LoadScene("TitleScene");
            }
        }

        // ロゴは画面中央に置かれているので、数値は下部の半透明帯に出して重ねない。
        private void OnGUI()
        {
            var result = GameResultHolder.Last;
            if (result == null) return;

            var r = result.Value;
            float bandH = 92f;
            var band = new Rect(0f, Screen.height - bandH, Screen.width, bandH);
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(band, Texture2D.whiteTexture);
            GUI.color = Color.white;

            var prevAlign = GUI.skin.label.alignment;
            var prevSize = GUI.skin.label.fontSize;
            GUI.skin.label.alignment = TextAnchor.MiddleCenter;

            GUI.contentColor = new Color(1f, 0.55f, 0.5f);
            GUI.skin.label.fontSize = 34;
            GUI.Label(new Rect(band.x, band.y + 2f, band.width, 44f), $"{r.KillCount} / {r.TargetKillCount}");

            GUI.contentColor = Color.white;
            GUI.skin.label.fontSize = 16;
            GUI.Label(new Rect(band.x, band.y + 44f, band.width, 22f), "到達撃破数");
            GUI.Label(new Rect(band.x, band.y + 66f, band.width, 22f), $"最大コンボ {r.MaxCombo}");

            GUI.skin.label.alignment = prevAlign;
            GUI.skin.label.fontSize = prevSize;
            GUI.contentColor = Color.white;
        }
    }
}
