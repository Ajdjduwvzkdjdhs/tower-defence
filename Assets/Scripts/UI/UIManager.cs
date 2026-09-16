using UnityEngine;
using KakigoriTD.Core;
using KakigoriTD.Gameplay;

namespace KakigoriTD.UI
{
    /// <summary>
    /// プロトタイプ UI（U1）。氷バー・砦HPバー・Wave タイマー（ステータス文）・アクティブ谷ハイライト・
    /// リザルトを表示する。砦HP は <see cref="Fort.OnHpChanged"/> を購読（ADR-3、<c>OnDestroy</c> で解除）。
    /// 氷・照準谷は連続値のため毎フレーム参照する。
    ///
    /// 描画は IMGUI（OnGUI）— Phase1 はプレースホルダ図形のみで、uGUI 化は後続の見栄え調整に委ねる
    /// （**ADR-13**）。配線は <c>GameManager</c>（Composition Root）が <see cref="Configure"/> で行う。
    /// </summary>
    public class UIManager : MonoBehaviour
    {
        private Fort _fort;
        private IceStock _ice;

        private int _hp, _maxHp;
        private string _status = "";
        private string _banner = "";
        private string _trackerWarning = ""; // 位置検知ロスト（状態Y）警告。空なら非表示（Phase 5）
        private bool _resultShown;
        private bool _resultWin;
        private int _resultScore;
        private int _resultPenalty;

        private int _specialKills, _specialThreshold;
        private bool _specialCharged;

        /// <summary>砦・氷を注入し、砦HP イベントを購読する。</summary>
        public void Configure(Fort fort, IceStock ice)
        {
            _fort = fort;
            _ice = ice;
            _hp = fort.CurrentHp;
            _maxHp = fort.MaxHp;
            fort.OnHpChanged += OnHpChanged;
        }

        private void OnHpChanged(int current, int max)
        {
            _hp = current;
            _maxHp = max;
        }

        private void OnDestroy()
        {
            if (_fort != null) _fort.OnHpChanged -= OnHpChanged; // 購読解除（ADR-3）
        }

        /// <summary>上部ステータス（Title / Wave n 残り秒 / Lull など）。GameManager が毎フレーム設定。</summary>
        public void SetStatus(string status) => _status = status;

        /// <summary>画面中央の遷移バナーを設定する（WaveIntro / WaveLull で使用）。</summary>
        public void SetBanner(string text) => _banner = text;

        /// <summary>中央バナーを消す（戦闘中・タイトル・リザルト）。</summary>
        public void ClearBanner() => _banner = "";

        /// <summary>位置検知ロスト警告（状態Y）。空文字で非表示。GameManager が毎フレーム設定（Phase 5）。</summary>
        public void SetTrackerWarning(string text) => _trackerWarning = text;

        /// <summary>リザルトを表示する。penalty は減点合計（0 なら非表示）。</summary>
        public void ShowResult(bool isWin, int score, int penalty = 0)
        {
            _resultShown = true;
            _resultWin = isWin;
            _resultScore = score;
            _resultPenalty = penalty;
        }

        /// <summary>リザルト表示を消す（再挑戦時）。</summary>
        public void ClearResult() => _resultShown = false;

        /// <summary>必殺技「ビーム砲」の撃破ゲージ（右下）。GameManager が毎フレーム設定。</summary>
        public void SetSpecialGauge(int kills, int threshold, bool charged)
        {
            _specialKills = kills;
            _specialThreshold = threshold;
            _specialCharged = charged;
        }

        private void OnGUI()
        {
            GUI.contentColor = Color.white;
            GUI.Label(new Rect(16, 10, 1200, 24), _status);

            // 位置検知ロスト警告（状態Y）。正常な「谷なし」とは区別し、ロスト時のみ赤で表示（Phase 5）。
            if (!string.IsNullOrEmpty(_trackerWarning))
            {
                GUI.contentColor = new Color(1f, 0.35f, 0.3f);
                GUI.Label(new Rect(16, 128, 600, 24), "⚠ " + _trackerWarning);
                GUI.contentColor = Color.white;
            }

            // 氷バー
            float ice01 = _ice != null ? _ice.Normalized : 0f;
            Bar(new Rect(16, 40, 320, 22), ice01, new Color(0.45f, 0.8f, 1f));
            GUI.Label(new Rect(344, 40, 260, 22), $"氷 {(_ice != null ? _ice.Current : 0f):F1}/{(_ice != null ? _ice.Total : 0f):F0}");

            // 砦HPバー
            float hp01 = _maxHp > 0 ? (float)_hp / _maxHp : 0f;
            Bar(new Rect(16, 68, 320, 22), hp01, new Color(0.4f, 0.95f, 0.5f));
            GUI.Label(new Rect(344, 68, 260, 22), $"砦HP {_hp}/{_maxHp}");

            // 必殺技「ビーム砲」ゲージ（右下）。閾値0（未設定）なら非表示。
            if (_specialThreshold > 0)
            {
                const float w = 260f, h = 48f, margin = 16f;
                var box = new Rect(Screen.width - w - margin, Screen.height - h - margin, w, h);
                GUI.color = new Color(0f, 0f, 0f, 0.5f);
                GUI.DrawTexture(box, Texture2D.whiteTexture);
                GUI.color = Color.white;

                float gauge01 = _specialCharged ? 1f : (_specialThreshold > 0 ? (float)_specialKills / _specialThreshold : 0f);
                var barRect = new Rect(box.x + 10, box.y + 10, box.width - 20, 16);
                Bar(barRect, gauge01, _specialCharged ? new Color(0.75f, 0.35f, 1f) : new Color(0.55f, 0.25f, 0.8f));

                var labelRect = new Rect(box.x + 10, box.y + 27, box.width - 20, 18);
                GUI.contentColor = _specialCharged ? new Color(0.85f, 0.55f, 1f) : Color.white;
                GUI.Label(labelRect, _specialCharged
                    ? "必殺技 発動中！"
                    : $"必殺技ゲージ  {_specialKills}/{_specialThreshold}");
                GUI.contentColor = Color.white;
            }

            // 中央バナー（WaveIntro / WaveLull。リザルト表示中は非表示）
            if (!string.IsNullOrEmpty(_banner) && !_resultShown)
            {
                float bw = Screen.width * 0.7f;
                float bh = 72f;
                var bannerBox = new Rect((Screen.width - bw) * 0.5f, Screen.height * 0.5f - bh * 0.5f, bw, bh);
                GUI.color = new Color(0f, 0f, 0f, 0.6f);
                GUI.DrawTexture(bannerBox, Texture2D.whiteTexture);
                GUI.color = Color.white;
                var prevAlign = GUI.skin.label.alignment;
                var prevSize = GUI.skin.label.fontSize;
                GUI.skin.label.alignment = TextAnchor.MiddleCenter;
                GUI.skin.label.fontSize = 28;
                GUI.contentColor = new Color(1f, 0.95f, 0.75f);
                GUI.Label(bannerBox, _banner);
                GUI.contentColor = Color.white;
                GUI.skin.label.alignment = prevAlign;
                GUI.skin.label.fontSize = prevSize;
            }

            // リザルト
            if (_resultShown)
            {
                var box = new Rect(Screen.width * 0.5f - 220, Screen.height * 0.5f - 70, 440, 140);
                GUI.color = new Color(0f, 0f, 0f, 0.6f);
                GUI.DrawTexture(box, Texture2D.whiteTexture);
                GUI.color = Color.white;
                var prev = GUI.skin.label.alignment;
                GUI.skin.label.alignment = TextAnchor.MiddleCenter;
                GUI.contentColor = _resultWin ? new Color(1f, 0.9f, 0.3f) : new Color(1f, 0.5f, 0.5f);
                GUI.Label(new Rect(box.x, box.y + 18, box.width, 40), _resultWin ? "YOU WIN!" : "FORT DESTROYED");
                GUI.contentColor = Color.white;
                if (_resultPenalty > 0)
                {
                    int fortHp = _resultScore + _resultPenalty;
                    GUI.Label(new Rect(box.x, box.y + 58, box.width, 24), $"砦HP {fortHp}  −  ペナルティ {_resultPenalty}  =  スコア {_resultScore}");
                }
                else
                {
                    GUI.Label(new Rect(box.x, box.y + 58, box.width, 24), $"スコア（残り砦HP）: {_resultScore}");
                }
                GUI.Label(new Rect(box.x, box.y + 92, box.width, 28), "[Enter] で再挑戦");
                GUI.skin.label.alignment = prev;
            }
        }

        // 背景（薄暗い）＋ 充填（color）の横バー。
        private static void Bar(Rect rect, float fill01, Color color)
        {
            if (fill01 < 0f) fill01 = 0f; else if (fill01 > 1f) fill01 = 1f;
            GUI.color = new Color(1f, 1f, 1f, 0.15f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = color;
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width * fill01, rect.height), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }
    }
}
