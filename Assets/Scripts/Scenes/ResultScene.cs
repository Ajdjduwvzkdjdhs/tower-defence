using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;
using KakigoriTD.Core;
using KakigoriTD.UI;

namespace KakigoriTD.Scenes
{
    /// <summary>
    /// クリア時のリザルト。<see cref="GameResultHolder"/> に置かれた直前のプレイ結果
    /// （クリアタイム＝スコア・ADR-30）を表示し、<c>Enter</c> でタイトルへ戻る。
    /// 表示は IMGUI（ADR-13 の UI 方針に合わせる）。結果が無い場合（このシーンを直接再生した等）は
    /// スコア欄を出さず、遷移だけ動く。
    /// </summary>
    public class ResultScene : MonoBehaviour
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

            GUI.contentColor = new Color(1f, 0.9f, 0.3f);
            GUI.skin.label.fontSize = 34;
            GUI.Label(new Rect(band.x, band.y + 2f, band.width, 44f), StageResult.FormatTime(r.ClearTimeSec));

            GUI.contentColor = Color.white;
            GUI.skin.label.fontSize = 16;
            GUI.Label(new Rect(band.x, band.y + 44f, band.width, 22f), "クリアタイム");
            GUI.Label(new Rect(band.x, band.y + 66f, band.width, 22f), $"撃破 {r.KillCount}   最大コンボ {r.MaxCombo}");

            GUI.skin.label.alignment = prevAlign;
            GUI.skin.label.fontSize = prevSize;
            GUI.contentColor = Color.white;
        }
    }
}
