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
        private static readonly string[] ChannelNames = { "N", "W", "S", "E" };

        private Fort _fort;
        private IceStock _ice;
        private AvalancheController _avalanche;

        private int _hp, _maxHp;
        private string _status = "";
        private bool _resultShown;
        private bool _resultWin;
        private int _resultScore;
        private int _resultPenalty;

        /// <summary>砦・氷・雪崩制御を注入し、砦HP イベントを購読する。</summary>
        public void Configure(Fort fort, IceStock ice, AvalancheController avalanche)
        {
            _fort = fort;
            _ice = ice;
            _avalanche = avalanche;
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

        private void OnGUI()
        {
            GUI.contentColor = Color.white;
            GUI.Label(new Rect(16, 10, 1200, 24), _status);

            // 氷バー
            float ice01 = _ice != null ? _ice.Normalized : 0f;
            Bar(new Rect(16, 40, 320, 22), ice01, new Color(0.45f, 0.8f, 1f));
            GUI.Label(new Rect(344, 40, 260, 22), $"氷 {(_ice != null ? _ice.Current : 0f):F1}/{(_ice != null ? _ice.Total : 0f):F0}");

            // 砦HPバー
            float hp01 = _maxHp > 0 ? (float)_hp / _maxHp : 0f;
            Bar(new Rect(16, 68, 320, 22), hp01, new Color(0.4f, 0.95f, 0.5f));
            GUI.Label(new Rect(344, 68, 260, 22), $"砦HP {_hp}/{_maxHp}");

            // アクティブ谷ハイライト（同時1本）
            int active = _avalanche != null ? _avalanche.ActiveChannel : -1;
            for (int i = 0; i < 4; i++)
            {
                var r = new Rect(16 + i * 52, 98, 46, 26);
                GUI.color = (i == active) ? new Color(1f, 0.85f, 0.1f) : new Color(1f, 1f, 1f, 0.18f);
                GUI.DrawTexture(r, Texture2D.whiteTexture);
                GUI.color = Color.white;
                GUI.contentColor = (i == active) ? Color.black : Color.white;
                GUI.Label(new Rect(r.x + 16, r.y + 4, 24, 20), ChannelNames[i]);
                GUI.contentColor = Color.white;
            }
            bool shaving = _avalanche != null && _avalanche.IsAvalanching;
            if (shaving) GUI.Label(new Rect(232, 100, 200, 22), "▼ 削り中（雪崩）");

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
