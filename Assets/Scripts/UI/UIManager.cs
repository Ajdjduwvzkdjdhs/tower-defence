using UnityEngine;
using KakigoriTD.Core;
using KakigoriTD.Gameplay;

namespace KakigoriTD.UI
{
    /// <summary>
    /// プロトタイプ UI（U1）。砦HPバー・撃破数・コンボ・必殺ゲージ・経過タイム・リザルトを表示する
    /// （ADR-30 で氷バーと Wave タイマーを廃止）。砦HP は <see cref="Fort.OnHpChanged"/> を購読
    /// （ADR-3、<c>OnDestroy</c> で解除）。撃破数・コンボ・ゲージは連続値のため GameManager が毎フレーム渡す。
    ///
    /// 描画は IMGUI（OnGUI）— Phase1 はプレースホルダ図形のみで、uGUI 化は後続の見栄え調整に委ねる
    /// （**ADR-13**）。配線は <c>GameManager</c>（Composition Root）が <see cref="Configure"/> で行う。
    /// </summary>
    public class UIManager : MonoBehaviour
    {
        private Fort _fort;

        private int _hp, _maxHp;

        // 戦闘中の可視化（GameManager が毎フレーム SetBattleStats で更新）。
        private int _kills, _targetKills, _combo;
        private float _gauge01, _elapsedSec;
        private string _status = "";
        private string _banner = "";
        private string _trackerWarning = ""; // 位置検知ロスト（状態Y）警告。空なら非表示（Phase 5）
        private bool _resultShown;
        private StageResult _result;
        // 開始前カウントダウン（"3"/"2"/"1"/"スタート"）。プロジェクター投影を想定し、
        // 画面全体を暗転させたうえで超大型・黒縁取り文字を出す（CountdownState が都度 SetCountdown で更新）。
        private string _countdownText = "";
        private bool _countdownIsFinal;
        private float _countdownSetAt = -999f;
        private const float CountdownPopInSec = 0.15f; // 出現直後にひと回り大きく→縮むポップイン演出の時間
        private const float CountdownStepSec = 1.0f;   // CountdownState.StepSec と同じ長さ（1ステップ=1秒）
        private static Texture2D _sonarSweepTex; // ソナー掃引の実行時生成テクスチャ（キャッシュ・使い回し）

        /// <summary>砦を注入し、砦HP イベントを購読する。</summary>
        public void Configure(Fort fort)
        {
            _fort = fort;
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


        /// <summary>
        /// カウントダウンの1ステップを表示する（"3"/"2"/"1"/"スタート"）。呼ばれるたびにポップイン
        /// アニメーションを頭から再生する。<paramref name="isFinal"/> は「スタート」のみ true
        /// （色・サイズを変えて強調する）。
        /// </summary>
        public void SetCountdown(string text, bool isFinal)
        {
            _countdownText = text;
            _countdownIsFinal = isFinal;
            _countdownSetAt = Time.time;
        }

        /// <summary>カウントダウン表示を消す（Battle開始時）。</summary>
        public void ClearCountdown() => _countdownText = "";
        /// <summary>
        /// 戦闘中の集計を更新する（ADR-30）。GameManager が毎フレーム呼ぶ。
        /// </summary>
        public void SetBattleStats(int kills, int targetKills, int combo, float gauge01, float elapsedSec)
        {
            _kills = kills;
            _targetKills = targetKills;
            _combo = combo;
            _gauge01 = gauge01;
            _elapsedSec = elapsedSec;
        }

        /// <summary>上部ステータス（Title / 経過タイムなど）。GameManager が毎フレーム設定。</summary>
        public void SetStatus(string status) => _status = status;

        /// <summary>画面中央の遷移バナーを設定する（WaveIntro / WaveLull で使用）。</summary>
        public void SetBanner(string text) => _banner = text;

        /// <summary>中央バナーを消す（戦闘中・タイトル・リザルト）。</summary>
        public void ClearBanner() => _banner = "";

        /// <summary>位置検知ロスト警告（状態Y）。空文字で非表示。GameManager が毎フレーム設定（Phase 5）。</summary>
        public void SetTrackerWarning(string text) => _trackerWarning = text;

        /// <summary>リザルトを表示する（クリア＝タイム / 失敗＝到達撃破数・ADR-30）。</summary>
        public void ShowResult(StageResult result)
        {
            _resultShown = true;
            _result = result;
        }

        /// <summary>リザルト表示を消す（再挑戦時）。</summary>
        public void ClearResult() => _resultShown = false;

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

            // 砦HPバー（生存リソース。スコアには含まない）
            float hp01 = _maxHp > 0 ? (float)_hp / _maxHp : 0f;
            Bar(new Rect(16, 40, 320, 22), hp01, new Color(0.4f, 0.95f, 0.5f));
            GUI.Label(new Rect(344, 40, 260, 22), $"砦HP {_hp}/{_maxHp}");

            // 必殺ゲージ（MAX で自動発動するので「満ちたら何か起きる」ことだけ伝える）
            bool gaugeFull = _gauge01 >= 1f;
            Bar(new Rect(16, 68, 320, 22), _gauge01,
                gaugeFull ? new Color(1f, 0.95f, 0.35f) : new Color(0.75f, 0.55f, 1f));
            GUI.Label(new Rect(344, 68, 260, 22), gaugeFull ? "必殺 発動!" : "必殺ゲージ");

            // 撃破数（クリア条件）
            GUI.contentColor = new Color(1f, 0.95f, 0.75f);
            var killStyle = new GUIStyle(GUI.skin.label) { fontSize = 26 };
            GUI.Label(new Rect(16, 96, 400, 36), $"撃破 {_kills} / {_targetKills}", killStyle);
            GUI.contentColor = Color.white;

            // コンボ（2 以上のときだけ大きく出す）
            if (_combo >= 2)
            {
                var comboStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 34,
                    alignment = TextAnchor.UpperRight,
                };
                GUI.contentColor = new Color(1f, 0.75f, 0.25f);
                GUI.Label(new Rect(Screen.width - 320, 40, 300, 48), $"{_combo} COMBO", comboStyle);
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
                GUI.contentColor = _result.IsWin ? new Color(1f, 0.9f, 0.3f) : new Color(1f, 0.5f, 0.5f);
                GUI.Label(new Rect(box.x, box.y + 18, box.width, 40), _result.IsWin ? "CLEAR!" : "FORT DESTROYED");
                GUI.contentColor = Color.white;
                GUI.Label(new Rect(box.x, box.y + 58, box.width, 24), _result.Headline);
                GUI.Label(new Rect(box.x, box.y + 86, box.width, 24),
                    $"撃破 {_result.KillCount} / {_result.TargetKillCount}   最大コンボ {_result.MaxCombo}");
                GUI.Label(new Rect(box.x, box.y + 112, box.width, 28), "[Enter] で再挑戦");
                GUI.skin.label.alignment = prev;
            }
            DrawCountdown();
        }

        private void DrawCountdown()
        {
            if (string.IsNullOrEmpty(_countdownText)) return;

            // 画面全体を軽く暗転。数字/文字そのものではなく「今が特別な瞬間」であることを伝える。
            GUI.color = new Color(0f, 0f, 0f, 0.35f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = Color.white;

            float t = Mathf.Clamp01((Time.time - _countdownSetAt) / CountdownPopInSec);
            float pop = 1f + 0.4f * (1f - t); // 出現時1.4倍 → 0.15秒で1.0倍まで縮む

            // 数字は元の 240 の 0.7 倍、「スタート」は SE を主役にして控えめに 0.3 倍。
            int baseSize = _countdownIsFinal ? Mathf.RoundToInt(190 * 0.3f) : Mathf.RoundToInt(240 * 0.7f);
            int fontSize = Mathf.RoundToInt(baseSize * pop);

            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize = fontSize,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
            };

            float h = fontSize * 1.6f;
            var rect = new Rect(0f, Screen.height * 0.5f - h * 0.5f, Screen.width, h);

            // 数字("3"/"2"/"1")と「スタート」で色が違って見えると不安定な印象になるため、
            // ステップによらず同じ色で統一する。
            Color fill = new Color(1f, 0.85f, 0.15f);

            if (!_countdownIsFinal) DrawSonarSweep(rect);

            // 縁取り（黒）: 4方向にオフセットして描画するIMGUI定番の疑似アウトライン。
            style.normal.textColor = Color.black;
            const float o = 4f;
            GUI.Label(new Rect(rect.x - o, rect.y, rect.width, rect.height), _countdownText, style);
            GUI.Label(new Rect(rect.x + o, rect.y, rect.width, rect.height), _countdownText, style);
            GUI.Label(new Rect(rect.x, rect.y - o, rect.width, rect.height), _countdownText, style);
            GUI.Label(new Rect(rect.x, rect.y + o, rect.width, rect.height), _countdownText, style);

            // 塗り（本体）。
            style.normal.textColor = fill;
            GUI.Label(rect, _countdownText, style);
        }

        private const float SonarTrailDeg = 130f; // 掃引の尾の長さ（度）。先端が最も明るく、ここまでかけて減衰する。

        /// <summary>
        /// 数字カウントダウン（"3"/"2"/"1"）専用の演出。潜水艦ソナーのように、先端が明るく尾を引いて
        /// 減衰する扇形の濃淡が1ステップ（1秒）かけて時計回りに1周する。「スタート」では出さない。
        /// </summary>
        private void DrawSonarSweep(Rect numberRect)
        {
            float cx = Screen.width * 0.5f;
            float cy = numberRect.y + numberRect.height * 0.5f;
            float diameter = numberRect.height * 1.15f;

            float sweepT = Mathf.Clamp01((Time.time - _countdownSetAt) / CountdownStepSec);

            var prevColor = GUI.color;
            var prevMatrix = GUI.matrix;

            GUI.color = new Color(0.5f, 1f, 0.65f, 0.9f);
            GUIUtility.RotateAroundPivot(sweepT * 360f, new Vector2(cx, cy));
            GUI.DrawTexture(new Rect(cx - diameter * 0.5f, cy - diameter * 0.5f, diameter, diameter), SonarSweepTexture());

            GUI.matrix = prevMatrix;
            GUI.color = prevColor;
        }

        /// <summary>
        /// 「先端が明るく尾を引いて減衰する扇形」の濃淡テクスチャを一度だけ生成して使い回す
        /// （毎フレーム生成しない）。境界は smoothstep で滑らかにし、拡大表示してもジャギーが出ないようにする。
        /// </summary>
        private static Texture2D SonarSweepTexture()
        {
            if (_sonarSweepTex != null) return _sonarSweepTex;

            const int size = 256;
            const float half = size * 0.5f;
            const float outerR = half - 2f;
            float trailRad = SonarTrailDeg * Mathf.Deg2Rad;

            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x - half + 0.5f;
                    float dy = y - half + 0.5f;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);

                    // 真上(12時)を0度、時計回りに増える角度。
                    float angle = Mathf.Atan2(dx, -dy);
                    if (angle < 0f) angle += 2f * Mathf.PI;

                    // 尾は指数0.8でゆるやかに減衰させ、くっきりした扇形ではなく「じわっと」した濃淡にする。
                    float angleFade = angle <= trailRad ? Mathf.Pow(1f - angle / trailRad, 0.8f) : 0f;
                    // 掃引が当たっていない部分もソナー画面らしくうっすら常時見せる（ベース濃度）。
                    const float baseWash = 0.12f;
                    float shade = Mathf.Max(baseWash, angleFade);
                    // 外周を2pxかけて滑らかにフェードさせ、拡大表示時のジャギーを防ぐ。
                    float edgeFade = Mathf.Clamp01((outerR - dist) / 2f);

                    byte a = (byte)Mathf.RoundToInt(shade * edgeFade * 255f);
                    pixels[y * size + x] = new Color32(255, 255, 255, a);
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply();
            _sonarSweepTex = tex;
            return tex;
        }

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
