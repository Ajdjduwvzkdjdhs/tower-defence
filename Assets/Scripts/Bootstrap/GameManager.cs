using UnityEngine;
using KakigoriTD.Audio;
using KakigoriTD.Core;
using KakigoriTD.Data;
using KakigoriTD.Factory;
using KakigoriTD.Gameplay;
using KakigoriTD.Input;
using KakigoriTD.UI;
using UnityEngine.SceneManagement;
using System.Collections;


namespace KakigoriTD.Bootstrap
{
    /// <summary>
    /// 唯一の Composition Root / Singleton（ADR-5）。全システムを生成・配線し、
    /// <see cref="GameStateMachine"/> を駆動する。State 群が作用する窓口 <see cref="IGameContext"/> を実装する。
    ///
    /// Phase1 は美術アセット未定のため、見た目はコード生成のプリミティブ＋URP/Unlit のプレースホルダ。
    /// 入力は <see cref="IInputSource"/> 経由（ADR-6）、調整値は SerializeField / SO（ADR-1）。
    /// </summary>
    public class GameManager : MonoBehaviour, IGameContext
    {
        public static GameManager Instance { get; private set; }

        [Header("ウェーブ（順に Wave1/2/3 の WaveConfigSO を割り当て）")]
        [SerializeField] private WaveConfigSO[] _waveConfigs;

        [Header("LiDAR 位置入力・雪玉放出・敵スポーンの調整値（未割り当てなら既定値インスタンス）")]
        [SerializeField] private LidarPreviewConfigSO _lidarPreviewConfig;

        [Header("3D 表現（Phase 4 / ADR-21。未割り当てなら高さ0＝平面フォールバック）")]
        [SerializeField] private Terrain _terrain; // 雪山。斜面高さのフォールバック

        [Tooltip("見えている斜面の Collider（山メッシュ）。設定すると高さはこちらを優先し、"
               + "未設定・レイが外れた場合だけ Terrain に落ちる。")]
        [SerializeField] private Collider _groundSurface;

        [Header("サウンド（Phase 6 / ADR-26。未割り当てなら全音無音でエラーなし）")]
        [SerializeField] private AudioConfigSO _audioConfig;

        [Header("調整値（spec §主要パラメータ）")]
        [SerializeField] private int _fortHpMax = 1000;
        [Tooltip("砦を山頂の斜面高さから上下にずらすオフセット。負の値で少し埋める。")]
        [SerializeField] private float _fortHeightOffset = 0f;
        [SerializeField] private float _iceTotal = 50f;
        [SerializeField] private float _waveLull = 5f;
        [SerializeField] private float _waveIntroDuration = 1.2f;
        [SerializeField] private int _allyHitPenalty = 50; // Phase2: 味方巻き込みペナルティ（spec §19）
        [SerializeField] private GameObject _tgsAllyPrefab; // Prefab Variant「TGS_Ally」をInspectorでアサイン
        [SerializeField] private GameObject _snowEffectPrefab; // 雪崩の雪玉見た目（ParticleSystem付きPrefab）
        [Header("必殺技「ビーム砲」（未割り当てなら既定値インスタンス／プレハブ無しでもロジックのみ動作）")]
        [SerializeField] private SpecialAttackConfigSO _specialAttackConfig;
        [SerializeField] private GameObject _beamCannonPrefab;
        [SerializeField] private GameObject _specialAttackShockwavePrefab;
        [Header("展示用設定")]
        [SerializeField] private float _resultLimit = 3f;// WINしてからリザルト画面に移動する時間
        private Fort _fort;
        private SpecialAttackController _specialAttack;
        private PenaltyTally _penalty; // Phase2(Unit P8)で OnAllyCaught 購読と合わせて配線
        private ProjectileFactory _projectileFactory;
        private EnemyField _field;
        private IceStock _ice;
        private IInputSource _input;   // 決定キー（Title/Result の Enter）専用
        private LidarPositionInputSource _lidarPosSource; // ロスト警告用に保持（LiDAR/マウスOR合成でも常時生成）
        private LidarAvalancheVfx _lidarVfx;
        private WaveManager _waveManager;
        private UIManager _ui;
        private GameStateMachine _machine;
        private AudioService _audio;             // BGM/SE アダプタ（Phase 6 / ADR-26）
        private int _prevFortHp;                 // Hit 音の判定用（減少 かつ >0 のときだけ鳴らす）

        private int _currentWaveIndex;
        private float _waveStartTime;

        private Material _fortMat, _snowPuffMat;
        private Material _dissolveMat, _emberMat; // 撃破時の蒸発演出（ADR-20 の cosmetic 系）

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;

            _fortMat = MakeUnlit(new Color(0.92f, 0.95f, 1.0f));
            _snowPuffMat = MakeUnlitAdditive(Color.white);
            _dissolveMat = MakeDissolve(new Color(0.75f, 1.90f, 2.60f, 1f)); // 発光帯：白〜水色
            _emberMat = MakeEmber(new Color(0.55f, 0.85f, 1.00f, 1f));       // 立ち上る粒

            // 山は Terrain（シーン配置・ADR-21）。手続き Quad は廃止。
            BuildFort();
            _projectileFactory = new ProjectileFactory(CreateProjectile, prewarm: 8);
            _penalty = new PenaltyTally();

            _ice = new IceStock(_iceTotal);

            // 決定キー（Title/Result の Enter）専用。谷選択・削りは位置入力＋LidarAvalancheVfx が担う。
            _input = NewChild("InputSource").AddComponent<KeyboardInputSource>();

            BuildField();
            BuildSpecialAttack();

            _waveManager = NewChild("WaveManager").AddComponent<WaveManager>();
            _waveManager.Configure(_field);

            _ui = NewChild("UIManager").AddComponent<UIManager>();
            _ui.Configure(_fort, _ice);

            // オーディオは購読対象（Fort/Factory/EnemyField）が揃った後に配線（ADR-26）。
            BuildAudio();

            // LiDAR 連続座標（LidarPosition）／マウス疑似（SimPointer）の位置入力＋雪玉放出。
            BuildLidarPath();

            _machine = new GameStateMachine();
            _machine.Change(new TitleState(_machine, this));
        }

        private void Update()
        {
            _machine.Tick(Time.deltaTime);
            //デバッグ用
            //Enterキーが押された際にいかなる場合にも強制的にシーンジャンプ
            bool IsClearKey = UnityEngine.InputSystem.Keyboard.current.leftShiftKey.wasPressedThisFrame;
            bool IsGameOverKey = UnityEngine.InputSystem.Keyboard.current.rightShiftKey.wasPressedThisFrame;

            if (IsClearKey) UnityEngine.SceneManagement.SceneManager.LoadScene("ResultScene");
            else if (IsGameOverKey) UnityEngine.SceneManagement.SceneManager.LoadScene("GameOverScene");

            UpdateStatusUI();
            UpdateTrackerWarning();
        }

        // LiDAR の seq が停滞＝ロスト（Python 停止/ハング）のとき、状態Y の警告を UI に出す。
        // LiDAR/マウスは常時 OR 合成のため、ロスト中でもマウス疑似入力へ自動フォールバックする
        // （警告はあくまで診断用で、操作不能にはならない）。
        private void UpdateTrackerWarning()
        {
            bool lidarWarn = _lidarPosSource != null && _lidarPosSource.IsLost;
            _ui.SetTrackerWarning(lidarWarn ? "LiDAR 未接続（valley_tracker.py を確認）" : "");
        }

        // ---- IGameContext ----------------------------------------------------

        public int WaveCount => _waveConfigs != null ? _waveConfigs.Length : 0;
        public float GetWaveDuration(int waveIndex) =>
            (_waveConfigs != null && waveIndex >= 0 && waveIndex < _waveConfigs.Length) ? _waveConfigs[waveIndex].Duration : 0f;
        public float WaveLullDuration => _waveLull;
        public float WaveIntroDuration => _waveIntroDuration;
        public bool IsFortDestroyed => _fort.IsDestroyed;
        public int FortHp => _fort.CurrentHp;
        public int PenaltyTotal => _penalty?.Total ?? 0; // Phase2(Unit P8)で _penalty を生成・配線
        public bool IsStartRequested => _input.ConfirmPressed;
        public bool IsRetryRequested => _input.ConfirmPressed;

        public void BeginWave(int waveIndex)
        {
            // wave 0 開始 = 新規プレイ（初回 / リトライ）。砦HP・氷・ペナルティ・残敵をリセットする。
            if (waveIndex == 0)
            {
                _fort.Initialize(_fortHpMax);
                _ice.Refill();
                _penalty.Reset();
                _field.ClearAll();
                _ui.ClearResult();
            }
            _currentWaveIndex = waveIndex;
            _waveStartTime = Time.time;
            _waveManager.StartWave(_waveConfigs[waveIndex]);
        }

        public void ClearRemainingEnemies()
        {
            _waveManager.StopWave();
            _field.ClearAll();
        }

        public void ShowResult(bool isWin, int score)
        {
            _ui.ShowResult(isWin, score, _penalty?.Total ?? 0);
            // 勝敗ジングルは BGM に重ねる（BGM は止めない・Q4-A / ADR-26）。
            if (isWin)
            {
                _audio?.PlayClear();
                // WINの際には3秒後にシーン遷移
                StartCoroutine(ResultSceneTransition(_resultLimit));
            }
            else
            {
                _audio?.PlayGameOver();
                StartCoroutine(FadeAndLoadScene());
            }
            ;
        }

        private IEnumerator FadeAndLoadScene()
        {

            yield return new WaitForSeconds(1.0f);
            // シーン切り替え
            SceneManager.LoadScene("GameOverScene");
            yield break;
        }
        // リザルトシーンに転送する用です。展示に向けて急遽作成。
        private System.Collections.IEnumerator ResultSceneTransition(float delaySeconds)
        {
            yield return new WaitForSeconds(delaySeconds);

            // シーン遷移処理
            UnityEngine.SceneManagement.SceneManager.LoadScene("ResultScene");
        }

        // ---- build helpers ---------------------------------------------------

        // 360度化した単一の敵マネージャ（phase-preview-feedback.md / ADR-29 の 360度拡張）。
        private void BuildField()
        {
            // 調整値 SO 未割り当てなら既定値の一時インスタンスで動かす（EnemyField と LidarPath で共有）。
            if (_lidarPreviewConfig == null)
                _lidarPreviewConfig = ScriptableObject.CreateInstance<LidarPreviewConfigSO>();

            _field = NewChild("EnemyField").AddComponent<EnemyField>();
            _field.Configure(_projectileFactory, _fort,
                CreateEnemy, CreateAlly, CreateDisguised, CreateSnowPuff, CreateDissolveBurst,
                HeightAt, _lidarPreviewConfig);
            _field.OnAllyCaught += OnAllyCaughtHandler;
        }

        // 必殺技「ビーム砲」。敵を一定数倒すとチャージ完了→自動発動（SpecialAttackController に一任）。
        private void BuildSpecialAttack()
        {
            if (_specialAttackConfig == null)
                _specialAttackConfig = ScriptableObject.CreateInstance<SpecialAttackConfigSO>();

            _specialAttack = NewChild("SpecialAttack").AddComponent<SpecialAttackController>();
            _specialAttack.Configure(_field, _fort.transform, _specialAttackConfig,
                _beamCannonPrefab, _specialAttackShockwavePrefab);
        }

        // LiDAR の連続座標とマウス疑似入力を常時 OR 合成し、検知点 P を中心に放射状の雪玉ストリームを
        // 放出、一般化位置到達で撃破する（phase-preview-feedback.md）。LiDAR に検知点があればそちらを
        // 優先し、無いとき（未接続・検知範囲外）だけマウス疑似入力にフォールバックする
        // （ユーザー指示：InputMode による切替を廃止）。
        private void BuildLidarPath()
        {
            var cfg = _lidarPreviewConfig; // BuildField で解決済み

            _lidarPosSource = NewChild("LidarPositionInput").AddComponent<LidarPositionInputSource>();
            _lidarPosSource.Configure(cfg, HeightAt);
            var calib = NewChild("LidarCalibration").AddComponent<LidarCalibration>();
            calib.Configure(_lidarPosSource, cfg, Camera.main);

            var sim = NewChild("SimPositionInput").AddComponent<SimPositionInputSource>();
            sim.Configure(cfg, HeightAt, Camera.main);

            IPositionInputSource posInput = new CompositePositionInputSource(_lidarPosSource, sim);

            _lidarVfx = NewChild("LidarAvalancheVfx").AddComponent<LidarAvalancheVfx>();
            _lidarVfx.Configure(posInput, _field, HeightAt, CreateAvalancheEffect, cfg,
                cfg.ConsumeIce ? _ice : null);
            _lidarVfx.OnAvalancheChanged += OnAvalancheChangedForAudio; // 雪崩ループ音（エッジ）
        }

        // (x,z)→world Y の斜面サンプラ（ADR-21）。コライダー不使用＝物理なしを維持。
        // Terrain 未割り当て時は 0（平面フォールバック）。
        /// <summary>斜面の高さを取るレイの発射高度。山メッシュの最上部より十分高くしておく。</summary>
        private const float GroundRayHeight = 100f;

        private float HeightAt(float x, float z)
        {
            // 見た目の斜面を正とする。Terrain と山メッシュは形が違い、Terrain 基準だと
            // 敵や雪玉がメッシュに埋まって見えなくなるため（半径5で最大約3.9のずれ）。
            // Physics.Raycast ではなく Collider.Raycast を使い、敵や砦のコライダーを拾わないようにする。
            if (_groundSurface != null)
            {
                var ray = new Ray(new Vector3(x, GroundRayHeight, z), Vector3.down);
                if (_groundSurface.Raycast(ray, out var hit, GroundRayHeight * 2f)) return hit.point.y;
            }

            // 山メッシュの外側（24x24 しかない）はここに落ちる
            if (_terrain == null) return 0f;
            return _terrain.transform.position.y + _terrain.SampleHeight(new Vector3(x, 0f, z));
        }

        private void BuildFort()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Fort";
            go.transform.SetParent(transform);
            go.transform.localScale = new Vector3(1.3f, 1.3f, 1f);
            go.transform.position = new Vector3(0f, HeightAt(0f, 0f) + _fortHeightOffset, 0f); // 山頂に据える（ADR-21）
            DestroyCollider(go);
            go.GetComponent<MeshRenderer>().sharedMaterial = _fortMat;
            _fort = go.AddComponent<Fort>();
            _fort.Initialize(_fortHpMax);
        }

        private void OnAllyCaughtHandler()
        {
            _penalty.Add(_allyHitPenalty);
            _audio?.PlayAllyCaught(); // 3d-t に該当音なし＝既定 null で無音（ADR-26）
        }

        // サウンド配線（Phase 6 / ADR-26）。薄い AudioService を子生成し、BGM 開始＋additive イベント購読。
        // 追加は cosmetic な購読のみで既存ロジックの分岐・数値・順序は不変。_audioConfig 未割り当てでも無音で安全。
        private void BuildAudio()
        {
            _audio = NewChild("AudioService").AddComponent<AudioService>();
            _audio.Configure(_audioConfig);
            _audio.PlayBgm(); // 開始時に1回・以後止めない（Q4-A）

            _prevFortHp = _fort.CurrentHp;

            _projectileFactory.OnCreated += OnProjectileCreatedForAudio;     // EnemyAttack（毎発）
            _field.OnEnemyKilled += OnEnemyKilledForAudio;                   // Monster（撃破1体ごと）
            _fort.OnHpChanged += OnFortHpChangedForAudio;                    // Hit（減少 かつ >0）
            // 雪崩ループ音（OnAvalancheChanged）は BuildLidarPath で _lidarVfx に配線する。
        }

        private void OnProjectileCreatedForAudio() => _audio.PlayEnemyAttack();
        private void OnEnemyKilledForAudio() => _audio.PlayEnemyKilled();
        private void OnAvalancheChangedForAudio(bool on) => _audio.SetAvalanche(on);

        // 砦被弾＝HP が「減少 かつ 新HP>0」のときのみ Hit（Initialize の満タン通知・0到達=GameOver を除外）。
        private void OnFortHpChangedForAudio(int cur, int max)
        {
            if (cur < _prevFortHp && cur > 0) _audio.PlayHit();
            _prevFortHp = cur;
        }

        // prefab は EnemyTypeSO.Prefab（SOごと）で渡される。SO 未設定（null）の弾き出しは
        // EnemyField.SpawnEnemy が先行して行うため、ここでは無条件で Instantiate する。
        private Enemy CreateEnemy(EnemyTypeSO type)
        {
            var go = GameObject.Instantiate(type.Prefab);
            go.name = "Enemy";
            DestroyCollider(go);
            return go.AddComponent<Enemy>();
        }

        private AllyNPC CreateAlly()
        {
            var go = GameObject.Instantiate(_tgsAllyPrefab);
            go.name = "Ally";
            DestroyCollider(go);
            return go.AddComponent<AllyNPC>();
        }

        // 変装敵: 味方(TGS_Ally)と同じ prefab で見た目を揃えて擬態する。tell マーカーは
        // DisguisedEnemy.OnInitialized で追加する（同色でも判別できるように）。
        private DisguisedEnemy CreateDisguised()
        {
            var go = GameObject.Instantiate(_tgsAllyPrefab);
            go.name = "DisguisedEnemy";
            DestroyCollider(go);
            return go.AddComponent<DisguisedEnemy>();
        }

        private SnowPuff CreateSnowPuff()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "SnowPuff"; go.transform.localScale = Vector3.one * 0.5f;
            DestroyCollider(go);
            go.GetComponent<MeshRenderer>().sharedMaterial = _snowPuffMat;
            return go.AddComponent<SnowPuff>();
        }

        // 撃破時の身代わり。メッシュもパーツ構成も Play 時に敵から複製するので、ここでは空の器だけ用意する
        // （敵が Cube でも多パーツ FBX でも本メソッドは変更不要）。
        private DissolveBurst CreateDissolveBurst()
        {
            var go = new GameObject("DissolveBurst");
            go.transform.SetParent(transform);
            var burst = go.AddComponent<DissolveBurst>();
            burst.Configure(_dissolveMat, _emberMat);
            return burst;
        }

        private AvalancheSnowEffect CreateAvalancheEffect()
        {
            var go = GameObject.Instantiate(_snowEffectPrefab);
            go.name = "AvalancheSnowEffect";

            // 2) ParticleSystemコンポーネントを取得
            //    子にPSがある場合も拾えるようにGetComponentInChildrenを使う
            var ps = go.GetComponent<ParticleSystem>();
            if (ps == null)
            {
                // 子オブジェクトにParticleSystemを探す
                ps = go.GetComponentInChildren<ParticleSystem>();
            }

            // 3) 衝突モジュールを有効化し、ワールドモードに設定
            var col = ps.collision;
            col.enabled = true;                                 // 衝突モジュールを有効化
            col.type = ParticleSystemCollisionType.World;       // 衝突タイプをWorldに
            DestroyCollider(go);
            return go.AddComponent<AvalancheSnowEffect>();
        }

        private Projectile CreateProjectile()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "Projectile"; go.transform.localScale = Vector3.one * 0.35f;
            DestroyCollider(go);
            go.GetComponent<MeshRenderer>().enabled = false; // 見た目を非表示（ロジックのみ）
            return go.AddComponent<Projectile>();
        }

        private void UpdateStatusUI()
        {
            if (_specialAttack != null)
                _ui.SetSpecialGauge(_specialAttack.KillsSinceCharge, _specialAttack.Threshold, _specialAttack.IsCharged);

            var cur = _machine.Current;
            if (cur is TitleState)
            {
                _ui.ClearResult();
                _ui.ClearBanner();
                _ui.SetStatus("かき氷タワーディフェンス — [Enter] で開始");
            }
            else if (cur is WaveIntroState intro)
            {
                _ui.SetBanner($"Wave {intro.WaveIndex + 1}  ―  {GetWaveDisplayName(intro.WaveIndex)}");
                _ui.SetStatus("");
            }
            else if (cur is WavePlayingState)
            {
                _ui.ClearBanner();
                float remaining = Mathf.Max(0f, GetWaveDuration(_currentWaveIndex) - (Time.time - _waveStartTime));
                _ui.SetStatus($"Wave {_currentWaveIndex + 1}/{WaveCount}   残り {remaining:F0}s   — W/A/S/D 谷選択 / Space 削り");
            }
            else if (cur is WaveLullState lull)
            {
                _ui.SetBanner($"Wave {lull.WaveIndex + 1}  クリア");
                _ui.SetStatus("");
            }
            else if (cur is ResultState)
            {
                _ui.ClearBanner();
                _ui.SetStatus("");
            }
        }

        private string GetWaveDisplayName(int i)
        {
            if (_waveConfigs == null || i < 0 || i >= _waveConfigs.Length) return "";
            var name = _waveConfigs[i].DisplayName;
            return string.IsNullOrEmpty(name) ? "" : name;
        }

        private GameObject NewChild(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform);
            return go;
        }

        private static void DestroyCollider(GameObject go)
        {
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);
        }

        private static Material MakeUnlit(Color c)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            m.SetColor("_BaseColor", c);
            return m;
        }

        // 加算合成の Unlit（照準レーン用）。暗い背景に「光る帯」として乗り、濁らず明るく見える。
        // 色 RGB がそのまま加算量（アルファは無視）。
        private static Material MakeUnlitAdditive(Color c)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
            m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            m.SetColor("_BaseColor", c);
            return m;
        }

        // 蒸発シェーダー（Shader Graphs/Dissolve）。_DissolveAmount を 0→1 にすると
        // ノイズしきい値で Alpha Clip され、境界に edge 色の発光帯が出る。
        private static Material MakeDissolve(Color edge)
        {
            var shader = Shader.Find("Shader Graphs/Dissolve");
            if (shader == null)
            {
                Debug.LogWarning("Shader Graphs/Dissolve が見つかりません。蒸発演出は Unlit で代替します。");
                return MakeUnlit(edge);
            }
            var m = new Material(shader);
            m.SetColor("_EdgeColor", edge);
            m.SetFloat("_NoiseScale", 8f); // ワールド座標基準なので UV 時代より小さい値
            m.SetFloat("_EdgeWidth", 0.07f);
            m.SetFloat("_Smoothness", 0.2f);
            m.SetFloat("_DissolveAmount", 0f);
            m.SetFloat("_AlphaClip", 1f);
            m.EnableKeyword("_ALPHATEST_ON");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
            return m;
        }

        // 立ち上る粒。加算合成の Unlit に、コード生成した放射状グラデーションを貼る
        // （素材アセットに依存しない＝Phase1 の方針どおり）。
        private static Material MakeEmber(Color c)
        {
            var m = MakeUnlitAdditive(c);
            m.SetTexture("_BaseMap", DotTexture());

            // MakeUnlitAdditive は _SrcBlend=One のためアルファが無視され、ドットが四角く出てしまう。
            // 粒子は減衰とフェードをアルファで表現するので SrcAlpha に差し替える（加算合成のまま）。
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            return m;
        }

        private static Texture2D _dotTex;

        private static Texture2D DotTexture()
        {
            if (_dotTex != null) return _dotTex;

            const int N = 32;
            _dotTex = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < N; y++)
            {
                for (int x = 0; x < N; x++)
                {
                    float dx = (x + 0.5f) / N * 2f - 1f;
                    float dy = (y + 0.5f) / N * 2f - 1f;
                    float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                    a = a * a * (3f - 2f * a);  // smoothstep
                    _dotTex.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
                }
            }
            _dotTex.Apply();
            return _dotTex;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;

            // 購読解除（ADR-3）。EnemyField / Factory / Fort / LidarVfx。
            if (_field != null)
            {
                _field.OnAllyCaught -= OnAllyCaughtHandler;
                _field.OnEnemyKilled -= OnEnemyKilledForAudio;
            }
            if (_projectileFactory != null) _projectileFactory.OnCreated -= OnProjectileCreatedForAudio;
            if (_lidarVfx != null) _lidarVfx.OnAvalancheChanged -= OnAvalancheChangedForAudio;
            if (_fort != null) _fort.OnHpChanged -= OnFortHpChangedForAudio;
        }
    }
}