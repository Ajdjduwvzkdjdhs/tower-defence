using System.Collections.Generic;
using UnityEngine;
using KakigoriTD.Core;
using KakigoriTD.Gameplay;

namespace KakigoriTD.UI
{
    /// <summary>
    /// プロトタイプ UI（U1）。砦HPバー・撃破数・必殺ゲージ・経過タイム・リザルトを表示する
    /// （ADR-30 で氷バーと Wave タイマーを廃止）。砦HP は <see cref="Fort.OnHpChanged"/> を購読
    /// （ADR-3、<c>OnDestroy</c> で解除）。撃破数・ゲージは連続値のため GameManager が毎フレーム渡す。
    /// コンボは画面固定表示をやめ、撃破位置に一瞬出るポップアップに変更した（<see cref="ShowComboPopup"/>）
    /// ——実プレイ中はプレイヤーが画面端を見ないため。
    ///
    /// 描画は IMGUI（OnGUI）— Phase1 はプレースホルダ図形のみで、uGUI 化は後続の見栄え調整に委ねる
    /// （**ADR-13**）。配線は <c>GameManager</c>（Composition Root）が <see cref="Configure"/> で行う。
    /// </summary>
    public class UIManager : MonoBehaviour
    {
        [Tooltip("コンボポップアップが表示され続ける秒数。")]
        [SerializeField] private float _comboPopupLifetimeSec = 0.8f;
        [Tooltip("コンボポップアップが寿命までに上へ浮く距離（スクリーンピクセル）。")]
        [SerializeField] private float _comboPopupRisePx = 50f;
        [Tooltip("コンボポップアップの基準フォントサイズ（通常撃破・撃破位置に表示）。")]
        [SerializeField] private int _comboPopupFontSize = 44;
        [Tooltip("節目ポップアップのフォントサイズ（画面中央に表示）。")]
        [SerializeField] private int _comboMilestoneFontSize = 90;
        [Tooltip("出現直後に拡大→収束させるポップ演出の秒数。")]
        [SerializeField] private float _comboPopupPopDurationSec = 0.12f;

        private Fort _fort;

        private int _hp, _maxHp;

        // 戦闘中の可視化（GameManager が毎フレーム SetBattleStats で更新）。
        private int _kills, _targetKills;
        private float _gauge01, _elapsedSec;
        private string _status = "";
        private string _banner = "";
        private string _trackerWarning = ""; // 位置検知ロスト（状態Y）警告。空なら非表示（Phase 5）
        private bool _resultShown;
        private StageResult _result;
        private int _resultRank = ScoreRanking.Unranked; // ADR-34: 今回の順位（クリア時のみ）
        private bool _showClearTimeTenths = true;         // ADR-36: クリアタイムを 0.1 秒単位で出すか（GameManager から）
        // 開始前カウントダウン（"3"/"2"/"1"/"スタート"）。プロジェクター投影を想定し、
        // 画面全体を暗転させたうえで超大型・黒縁取り文字を出す（CountdownState が都度 SetCountdown で更新）。
        private string _countdownText = "";
        private bool _countdownIsFinal;
        private float _countdownSetAt = -999f;
        private const float CountdownPopInSec = 0.15f; // 出現直後にひと回り大きく→縮むポップイン演出の時間
        private const float CountdownStepSec = 1.0f;   // CountdownState.StepSec と同じ長さ（1ステップ=1秒）
        private static Texture2D _sonarSweepTex; // ソナー掃引の実行時生成テクスチャ（キャッシュ・使い回し）

        // コンボポップアップ（敵の撃破位置に一瞬表示。GameManager.OnEnemyKilled が都度追加）。
        // _comboMilestoneInterval コンボごとの節目だけは撃破位置ではなく画面中央に大きく表示する（IsMilestone）。
        private struct ComboPopup { public Vector3 WorldPos; public int Combo; public float StartTime; public bool IsMilestone; }
        private readonly List<ComboPopup> _comboPopups = new List<ComboPopup>();

        // UIManager は GameManager が実行時に AddComponent で生成する（ADR-5）ため、
        // このコンポーネント自体に [SerializeField] を置いても Play せずに調整できない。
        // Play せずに現場調整できるよう、値は GameManager 側の SerializeField から Configure で受け取る。
        private int _comboMilestoneInterval = 10;

        /// <summary>砦を注入し、砦HP イベントを購読する。</summary>
        public void Configure(Fort fort, int comboMilestoneInterval)
        {
            _fort = fort;
            _hp = fort.CurrentHp;
            _maxHp = fort.MaxHp;
            fort.OnHpChanged += OnHpChanged;
            _comboMilestoneInterval = Mathf.Max(1, comboMilestoneInterval);
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
        public void SetBattleStats(int kills, int targetKills, float gauge01, float elapsedSec)
        {
            _kills = kills;
            _targetKills = targetKills;
            _gauge01 = gauge01;
            _elapsedSec = elapsedSec;
        }

        /// <summary>
        /// 敵を撃破した位置にコンボ数を一瞬表示する。GameManager.OnEnemyKilled がコンボ 2 以上のときだけ呼ぶ。
        /// <see cref="_comboMilestoneInterval"/> コンボごとの節目は撃破位置ではなく画面中央に大きく表示する。
        /// </summary>
        public void ShowComboPopup(Vector3 worldPos, int combo)
        {
            _comboPopups.Add(new ComboPopup
            {
                WorldPos = worldPos,
                Combo = combo,
                StartTime = Time.time,
                IsMilestone = combo % _comboMilestoneInterval == 0,
            });
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
        /// <param name="rank">ランキング順位（1 始まり）。ランク外・失敗は <see cref="ScoreRanking.Unranked"/>。</param>
        public void ShowResult(StageResult result, int rank = ScoreRanking.Unranked)
        {
            _resultShown = true;
            _result = result;
            _resultRank = rank;
        }

        /// <summary>クリアタイムを 0.1 秒単位で出すか（GameManager の Inspector 値・ADR-36）。</summary>
        public void SetClearTimeTenths(bool show) => _showClearTimeTenths = show;

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

            // コンボポップアップ（撃破位置に一瞬表示。画面固定の常設表示は廃止）
            for (int i = _comboPopups.Count - 1; i >= 0; i--)
            {
                float age = Time.time - _comboPopups[i].StartTime;
                if (age >= _comboPopupLifetimeSec) { _comboPopups.RemoveAt(i); continue; }
                DrawComboPopup(_comboPopups[i], age);
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
                // クリア時は順位を見出しに添える（「1分42秒 でクリア！ — 第3位」）。
                string headline = _result.IsWin && _resultRank != ScoreRanking.Unranked
                    ? $"{_result.HeadlineFor(_showClearTimeTenths)}  — 第{_resultRank}位"
                    : _result.HeadlineFor(_showClearTimeTenths);
                GUI.Label(new Rect(box.x, box.y + 58, box.width, 24), headline);
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

        // 縁取り用のオフセット（周囲8方向）。IMGUIには太字の縁取りが無いので黒を少しずつずらして重ね描きする。
        private static readonly Vector2[] ComboPopupOutlineOffsets =
        {
            new Vector2(-2, -2), new Vector2(0, -2), new Vector2(2, -2),
            new Vector2(-2, 0),                       new Vector2(2, 0),
            new Vector2(-2, 2),  new Vector2(0, 2),   new Vector2(2, 2),
        };

        // コンボポップアップ 1 件を描画する。節目（10コンボ毎）は画面中央に大きく、それ以外は撃破位置に小さく表示する。
        private void DrawComboPopup(ComboPopup popup, float age)
        {
            if (popup.IsMilestone) DrawMilestonePopup(popup, age);
            else DrawKillPopup(popup, age);
        }

        // 通常撃破（節目以外）: ワールド座標→スクリーン座標に変換し、撃破位置に小さく出す。
        // 出現直後は拡大→収束するポップ演出、以後は上に浮かせつつフェードアウトする。
        private void DrawKillPopup(ComboPopup popup, float age)
        {
            var cam = Camera.main;
            if (cam == null) return;

            Vector3 screenPos = cam.WorldToScreenPoint(popup.WorldPos);
            if (screenPos.z <= 0f) return; // カメラの後ろ（描画しない）

            float t = age / _comboPopupLifetimeSec;
            float guiY = Screen.height - screenPos.y - t * _comboPopupRisePx; // GUI 座標は上下反転＋上昇演出
            float pop = age < _comboPopupPopDurationSec
                ? Mathf.Lerp(1.6f, 1f, age / _comboPopupPopDurationSec)
                : 1f;

            // _comboMilestoneInterval コンボごとに階層が上がり、フォントも色も豪華になっていく（節目の中央大表示とは別に、通常表示側でも変化させる）。
            int tier = Mathf.Min(popup.Combo / _comboMilestoneInterval, ComboPopupMaxTier);
            int tieredFontSize = _comboPopupFontSize + tier * 6;

            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.RoundToInt(tieredFontSize * pop),
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                wordWrap = false,
                clipping = TextClipping.Overflow, // ポップ演出でフォントが箱より大きくなる瞬間があるため、切り捨てない
            };

            string text = $"{popup.Combo} COMBO";
            float alpha = 1f - t;
            const float w = 220f, h = 90f, margin = 8f; // h はポップ最大時のフォント(約70px)より余裕を持たせる
            float x = Mathf.Clamp(screenPos.x - w * 0.5f, margin, Screen.width - w - margin);
            float y = Mathf.Clamp(guiY - h * 0.5f, margin, Screen.height - h - margin);
            var rect = new Rect(x, y, w, h);

            DrawOutlinedLabel(rect, text, style, ComboTierColor(tier), alpha);
        }

        private const int ComboPopupMaxTier = 5; // 50コンボ以降は虹色に固定（それ以上は上げない）

        // 階層ごとの色。最終階層（50コンボ以降）は時間で色相を回して虹色にする（最高潮の演出）。
        private static Color ComboTierColor(int tier)
        {
            switch (tier)
            {
                case 0: return new Color(1f, 0.85f, 0.15f);  // 黄色（2〜9コンボ）
                case 1: return new Color(1f, 0.55f, 0.1f);   // オレンジ（10〜19）
                case 2: return new Color(1f, 0.25f, 0.25f);  // 赤（20〜29）
                case 3: return new Color(0.85f, 0.3f, 1f);   // 紫（30〜39）
                case 4: return new Color(0.3f, 0.8f, 1f);    // 水色（40〜49）
                default: return Color.HSVToRGB(Mathf.Repeat(Time.time * 0.6f, 1f), 0.85f, 1f); // 50コンボ以降：虹色
            }
        }

        // 節目（10コンボ毎）: 撃破位置ではなく画面中央に大きく、虹色で出す。
        private void DrawMilestonePopup(ComboPopup popup, float age)
        {
            float t = age / _comboPopupLifetimeSec;
            float pop = age < _comboPopupPopDurationSec
                ? Mathf.Lerp(1.8f, 1f, age / _comboPopupPopDurationSec)
                : 1f;

            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.RoundToInt(_comboMilestoneFontSize * pop),
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                wordWrap = false,
                clipping = TextClipping.Overflow,
            };

            string text = $"{popup.Combo} COMBO!!";
            float alpha = 1f - t;
            const float w = 800f, h = 160f;
            var rect = new Rect(Screen.width * 0.5f - w * 0.5f, Screen.height * 0.5f - h * 0.5f, w, h);

            Color c = Color.HSVToRGB(Mathf.Repeat(Time.time * 0.6f, 1f), 0.85f, 1f); // 節目はすべて虹色（時間で色相回転）
            DrawOutlinedLabel(rect, text, style, c, alpha);
        }

        // 黒縁取り（周囲8方向に重ね描き）＋本体色でラベルを1つ描画する共通処理。
        private static void DrawOutlinedLabel(Rect rect, string text, GUIStyle style, Color fillColor, float alpha)
        {
            GUI.contentColor = new Color(0f, 0f, 0f, alpha * 0.85f);
            for (int i = 0; i < ComboPopupOutlineOffsets.Length; i++)
            {
                var r = rect;
                r.x += ComboPopupOutlineOffsets[i].x;
                r.y += ComboPopupOutlineOffsets[i].y;
                GUI.Label(r, text, style);
            }

            GUI.contentColor = new Color(fillColor.r, fillColor.g, fillColor.b, alpha);
            GUI.Label(rect, text, style);
            GUI.contentColor = Color.white;
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
