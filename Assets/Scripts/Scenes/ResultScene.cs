using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;
using KakigoriTD.Core;
using KakigoriTD.UI;

namespace KakigoriTD.Scenes
{
    /// <summary>
    /// クリア時のリザルト。<see cref="GameResultHolder"/> に置かれた直前のプレイ結果
    /// （クリアタイム＝スコア・ADR-30）と、記録後のランキング（ADR-34）を表示し、<c>Enter</c> でタイトルへ戻る。
    /// 表示は IMGUI（ADR-13 の UI 方針に合わせる）。結果が無い場合（このシーンを直接再生した等）は
    /// スコア欄を出さず、遷移だけ動く。
    /// </summary>
    public class ResultScene : MonoBehaviour
    {
        [Tooltip("ランキング表に出す件数（保存件数とは別。GameManager の Ranking Capacity が保存上限）。")]
        [SerializeField] private int _rankingDisplayCount = 10;
        [Tooltip("D キーをこの秒数押し続けるとランキングのローカル DB ファイルを消す（ADR-34・展示のリセット用）。")]
        [SerializeField] private float _rankingResetHoldSec = 5f;

        private static readonly Color HighlightColor = new Color(1f, 0.9f, 0.3f);

        private RankingResetHotkey _rankingReset;

        void Awake()
        {
            _rankingReset = RankingResetHotkey.CreateDefault(_rankingResetHoldSec);
        }

        void Update()
        {
            _rankingReset.Update(Time.deltaTime);
            if (Keyboard.current != null &&
                Keyboard.current.enterKey.wasPressedThisFrame)
            {
                GameResultHolder.Clear();
                SceneManager.LoadScene("TitleScene");
            }
        }

        // ロゴは画面中央に置かれているので、数値は下部の半透明帯に出して重ねない。
        // ランキング表は右上の縦長パネル（ロゴの横）。
        private void OnGUI()
        {
            _rankingReset.OnGUI();

            var result = GameResultHolder.Last;
            if (result == null) return;

            var r = result.Value;
            int rank = GameResultHolder.LastRank;

            var prevAlign = GUI.skin.label.alignment;
            var prevSize = GUI.skin.label.fontSize;

            DrawBottomBand(r, rank);
            DrawRankingPanel(GameResultHolder.Ranking, rank);

            GUI.skin.label.alignment = prevAlign;
            GUI.skin.label.fontSize = prevSize;
            GUI.contentColor = Color.white;
        }

        private static void DrawBottomBand(StageResult r, int rank)
        {
            float bandH = 92f;
            var band = new Rect(0f, Screen.height - bandH, Screen.width, bandH);
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(band, Texture2D.whiteTexture);
            GUI.color = Color.white;

            GUI.skin.label.alignment = TextAnchor.MiddleCenter;

            GUI.contentColor = HighlightColor;
            GUI.skin.label.fontSize = 34;
            string headline = StageResult.FormatTime(r.ClearTimeSec, GameResultHolder.ShowClearTimeTenths);
            if (rank != ScoreRanking.Unranked) headline += $"   第{rank}位";
            GUI.Label(new Rect(band.x, band.y + 2f, band.width, 44f), headline);

            GUI.contentColor = Color.white;
            GUI.skin.label.fontSize = 16;
            GUI.Label(new Rect(band.x, band.y + 44f, band.width, 22f), "クリアタイム");
            GUI.Label(new Rect(band.x, band.y + 66f, band.width, 22f), $"撃破 {r.KillCount}   最大コンボ {r.MaxCombo}");
        }

        private void DrawRankingPanel(IReadOnlyList<ScoreEntry> ranking, int rank)
        {
            if (ranking == null || ranking.Count == 0) return;

            int rows = Mathf.Min(_rankingDisplayCount, ranking.Count);
            const float rowH = 26f;
            const float headerH = 36f;
            const float pad = 10f;
            float panelW = Mathf.Min(340f, Screen.width * 0.34f);
            float panelH = headerH + rows * rowH + pad * 2f;
            var panel = new Rect(Screen.width - panelW - 16f, 16f, panelW, panelH);

            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(panel, Texture2D.whiteTexture);
            GUI.color = Color.white;

            GUI.skin.label.alignment = TextAnchor.MiddleLeft;
            GUI.skin.label.fontSize = 20;
            GUI.contentColor = new Color(1f, 0.95f, 0.75f);
            GUI.Label(new Rect(panel.x + pad, panel.y + pad, panel.width - pad * 2f, headerH), "ランキング");

            GUI.skin.label.fontSize = 16;
            for (int i = 0; i < rows; i++)
            {
                var e = ranking[i];
                bool isMine = (i + 1) == rank;
                float y = panel.y + pad + headerH + i * rowH;
                var row = new Rect(panel.x + pad, y, panel.width - pad * 2f, rowH);

                if (isMine)
                {
                    GUI.color = new Color(1f, 0.9f, 0.3f, 0.25f);
                    GUI.DrawTexture(row, Texture2D.whiteTexture);
                    GUI.color = Color.white;
                }
                GUI.contentColor = isMine ? HighlightColor : Color.white;

                GUI.skin.label.alignment = TextAnchor.MiddleLeft;
                GUI.Label(new Rect(row.x, row.y, 44f, rowH), $"{i + 1}.");
                GUI.Label(new Rect(row.x + 44f, row.y, 110f, rowH), StageResult.FormatTime(e.ClearTimeSec, GameResultHolder.ShowClearTimeTenths));
                GUI.skin.label.alignment = TextAnchor.MiddleRight;
                GUI.Label(new Rect(row.x + 154f, row.y, row.width - 154f, rowH), $"コンボ {e.MaxCombo}");
            }
            GUI.contentColor = Color.white;
        }
    }
}
