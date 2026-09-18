using UnityEngine;
using UnityEngine.InputSystem;
using KakigoriTD.Core;
using KakigoriTD.Persistence;
using KakigoriTD.UI;

namespace KakigoriTD.Scenes
{
    /// <summary>
    /// <c>D</c> キーの長押し（既定 5 秒）でランキングのローカル DB ファイルを消す（ADR-34・展示のリセット用）。
    /// Title / Result の既存コンポーネントが生成して <see cref="Update"/> / <see cref="OnGUI"/> を毎フレーム呼ぶ
    /// （plain class。シーンに部品を足さず、戦闘中の <c>TH</c> では動かさない）。
    /// 判定は Core の <see cref="HoldToConfirm"/>、削除は <see cref="IScoreStore.Clear"/>。
    /// </summary>
    public sealed class RankingResetHotkey
    {
        private const float DoneMessageSec = 2.5f;

        private readonly HoldToConfirm _hold;
        private readonly IScoreStore _store;
        private float _doneShownAt = -1f;

        public RankingResetHotkey(float holdSec, IScoreStore store)
        {
            _hold = new HoldToConfirm(holdSec);
            _store = store;
        }

        /// <summary>既定の保存先（GameManager と同じ解決順）を使う。</summary>
        public static RankingResetHotkey CreateDefault(float holdSec)
        {
            var path = JsonFileScoreStore.ResolvePath(Application.persistentDataPath, JsonFileScoreStore.DefaultFileName);
            return new RankingResetHotkey(holdSec, new JsonFileScoreStore(path));
        }

        public void Update(float deltaSec)
        {
            bool held = Keyboard.current != null && Keyboard.current.dKey.isPressed;
            bool fired = _hold.Tick(deltaSec, held);
            // 発火後も押し続けている間は時刻を更新し続け、離してから DoneMessageSec だけ完了表示を残す。
            if (_hold.HasFired) _doneShownAt = Time.time;
            if (!fired) return;

            try
            {
                _store.Clear();
                // 表示中のランキングも捨てる（Result 画面で消したら表もその場で消える）。
                GameResultHolder.Ranking = null;
                GameResultHolder.LastRank = ScoreRanking.Unranked;
                Debug.Log("[Ranking] D 長押しでランキングを消去");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[Ranking] 消去に失敗: {e.Message}");
            }
        }

        // 左上に小さく：長押し中は進捗バー、消去後は（押し続けている間＋離してから数秒）完了メッセージ。普段は何も出さない。
        public void OnGUI()
        {
            bool showDone = _doneShownAt >= 0f && Time.time - _doneShownAt < DoneMessageSec;
            if (!_hold.IsHolding && !showDone) return;

            var box = new Rect(16f, 16f, 300f, 46f);
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(box, Texture2D.whiteTexture);
            GUI.color = Color.white;

            var prevSize = GUI.skin.label.fontSize;
            var prevAlign = GUI.skin.label.alignment;
            GUI.skin.label.fontSize = 14;
            GUI.skin.label.alignment = TextAnchor.MiddleLeft;

            if (!showDone)
            {
                GUI.contentColor = new Color(1f, 0.55f, 0.5f);
                GUI.Label(new Rect(box.x + 10f, box.y + 2f, box.width - 20f, 22f),
                    $"D 長押しでランキング消去… {Mathf.CeilToInt(_hold.HoldSec * (1f - _hold.Progress01))} 秒");
                var bar = new Rect(box.x + 10f, box.y + 28f, box.width - 20f, 10f);
                GUI.color = new Color(1f, 1f, 1f, 0.25f);
                GUI.DrawTexture(bar, Texture2D.whiteTexture);
                GUI.color = new Color(1f, 0.45f, 0.4f);
                GUI.DrawTexture(new Rect(bar.x, bar.y, bar.width * _hold.Progress01, bar.height), Texture2D.whiteTexture);
                GUI.color = Color.white;
            }
            else
            {
                GUI.contentColor = new Color(1f, 0.9f, 0.3f);
                GUI.Label(new Rect(box.x + 10f, box.y, box.width - 20f, box.height), "ランキングを消去しました");
            }

            GUI.contentColor = Color.white;
            GUI.skin.label.fontSize = prevSize;
            GUI.skin.label.alignment = prevAlign;
        }
    }
}
