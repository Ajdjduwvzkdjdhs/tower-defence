using UnityEngine;
using KakigoriTD.Audio;
using KakigoriTD.Core;
using KakigoriTD.Data;
using KakigoriTD.Factory;
using KakigoriTD.Gameplay;
using KakigoriTD.Input;
using KakigoriTD.UI;

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

        [Header("シリアル（実機ハンドル接続。未割り当てならキーボードのみで動作）")]
        [SerializeField] private SerialConfigSO _serialConfig;

        [Header("位置検知（Vision。未割り当てならキーボードのみ。谷選択の源は _inputMode で切替）")]
        [SerializeField] private VisionConfigSO _visionConfig;
        [Tooltip("谷選択の源。展示本番=Vision（かき氷機の位置）／開発=Keyboard。")]
        [SerializeField] private InputMode _inputMode = InputMode.Keyboard;
        [Tooltip("カメラの左右反転（ミラー）。左右の谷が逆に出るときに ON。W↔E を入れ替える（再生中もライブ反映）。")]
        [SerializeField] private bool _visionFlipHorizontal;
        [Tooltip("カメラの上下反転（ミラー）。前後の谷が逆に出るときに ON。N↔S を入れ替える（再生中もライブ反映）。")]
        [SerializeField] private bool _visionFlipVertical;

        [Header("3D 表現（Phase 4 / ADR-21。未割り当てなら高さ0＝平面フォールバック）")]
        [SerializeField] private Terrain _terrain; // 雪山。斜面高さのフォールバック

        [Tooltip("見えている斜面の Collider（山メッシュ）。設定すると高さはこちらを優先し、"
               + "未設定・レイが外れた場合だけ Terrain に落ちる。")]
        [SerializeField] private Collider _groundSurface;

        [Header("サウンド（Phase 6 / ADR-26。未割り当てなら全音無音でエラーなし）")]
        [SerializeField] private AudioConfigSO _audioConfig;

        [Header("敵の見た目（未割り当てならプリミティブ Cube にフォールバック）")]
        [Tooltip("敵として使う FBX（例: TGS_兵士赤）。原点や実寸はモデル任せでよく、下の高さに正規化される。")]
        [SerializeField] private GameObject _enemyModel;
        [Tooltip("敵モデルのワールド高さ。足元が接地するよう自動でスケール・オフセットする。")]
        [SerializeField] private float _enemyModelHeight = 3.2f;

        [Header("調整値（spec §主要パラメータ）")]
        [SerializeField] private int _fortHpMax = 1000;
        [SerializeField] private float _iceTotal = 50f;
        [SerializeField] private float _waveLull = 5f;
        [SerializeField] private float _waveIntroDuration = 1.2f;
        [SerializeField] private int _allyHitPenalty = 50; // Phase2: 味方巻き込みペナルティ（spec §19）

        private Fort _fort;
        private PenaltyTally _penalty; // Phase2(Unit P8)で OnAllyCaught 購読と合わせて配線
        private ProjectileFactory _projectileFactory;
        private ChannelController[] _channels;
        private IceStock _ice;
        private IInputSource _input;
        private AvalancheController _avalanche;
        private AvalancheVfx _avalancheVfx;
        private WaveManager _waveManager;
        private UIManager _ui;
        private GameStateMachine _machine;
        private VisionInputSource _visionSource; // ロスト（Python 停止）を UI 警告に反映するため保持
        private AudioService _audio;             // BGM/SE アダプタ（Phase 6 / ADR-26）
        private int _prevFortHp;                 // Hit 音の判定用（減少 かつ >0 のときだけ鳴らす）

        private int _currentWaveIndex;
        private float _waveStartTime;

        private Material _fortMat, _enemyMat, _projMat, _aimMat, _snowPuffMat, _snowballMat;
        private Material _dissolveMat, _emberMat; // 撃破時の蒸発演出（ADR-20 の cosmetic 系）
        private GameObject _aimIndicator; // 照準中の谷を示すレーン（半透明）

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;

            _fortMat = MakeUnlit(new Color(0.92f, 0.95f, 1.0f));
            _enemyMat = MakeUnlit(new Color(0.90f, 0.15f, 0.15f));
            _projMat = MakeUnlit(new Color(1.0f, 0.85f, 0.10f));
            _snowPuffMat = MakeUnlitAdditive(Color.white);
            _snowballMat = MakeUnlit(new Color(0.97f, 0.98f, 1.0f)); // 雪玉（斜面を流れる雪崩の粒）
            _dissolveMat = MakeDissolve(new Color(0.75f, 1.90f, 2.60f, 1f)); // 発光帯：白〜水色
            _emberMat = MakeEmber(new Color(0.55f, 0.85f, 1.00f, 1f));       // 立ち上る粒

            // 山は Terrain（シーン配置・ADR-21）。手続き Quad は廃止。
            BuildFort();
            _projectileFactory = new ProjectileFactory(CreateProjectile, prewarm: 8);
            _penalty = new PenaltyTally();
            BuildChannels();

            _ice = new IceStock(_iceTotal);
            _input = BuildInput();

            _avalanche = NewChild("AvalancheController").AddComponent<AvalancheController>();
            _avalanche.Configure(_input, _ice, _channels);

            // 雪崩の見た目（斜面を流れる雪玉ストリーム）。削り状態を読むだけの純 cosmetic（ADR-21）。
            _avalancheVfx = NewChild("AvalancheVfx").AddComponent<AvalancheVfx>();
            _avalancheVfx.Configure(_avalanche, _channels, HeightAt, CreateSnowball);

            _waveManager = NewChild("WaveManager").AddComponent<WaveManager>();
            _waveManager.Configure(_channels);

            _ui = NewChild("UIManager").AddComponent<UIManager>();
            _ui.Configure(_fort, _ice, _avalanche);

            // オーディオは購読対象（Fort/Factory/Channels/Avalanche）が揃った後に配線（ADR-26）。
            BuildAudio();

            BuildAimIndicator();

            _machine = new GameStateMachine();
            _machine.Change(new TitleState(_machine, this));
        }

        private void Update()
        {
            _machine.Tick(Time.deltaTime);
            UpdateStatusUI();
            UpdateAimIndicator();
            UpdateTrackerWarning();
        }

        // Vision モードで位置検知がロスト（Python 停止/ハング）のとき、状態Y の警告を UI に出す。
        // 正常な「谷なし(-1)」とは区別し、ロスト時のみ表示する（docs/phase5.md §3-⑥）。
        private void UpdateTrackerWarning()
        {
            // Inspector の左右／上下反転トグルを再生中もライブ反映（キャリブレーション用）。
            if (_visionSource != null)
            {
                _visionSource.FlipHorizontal = _visionFlipHorizontal;
                _visionSource.FlipVertical = _visionFlipVertical;
            }

            bool warn = _inputMode == InputMode.Vision && _visionSource != null && _visionSource.IsLost;
            _ui.SetTrackerWarning(warn ? "位置トラッキング未接続（Python を確認）" : "");
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
                foreach (var ch in _channels) ch.ClearAll();
                _ui.ClearResult();
            }
            _currentWaveIndex = waveIndex;
            _waveStartTime = Time.time;
            _waveManager.StartWave(_waveConfigs[waveIndex]);
        }

        public void ClearRemainingEnemies()
        {
            _waveManager.StopWave();
            foreach (var ch in _channels) ch.ClearAll();
        }

        public void ShowResult(bool isWin, int score)
        {
            _ui.ShowResult(isWin, score, _penalty?.Total ?? 0);
            // 勝敗ジングルは BGM に重ねる（BGM は止めない・Q4-A / ADR-26）。
            if (isWin) _audio?.PlayClear(); else _audio?.PlayGameOver();
        }

        // ---- build helpers ---------------------------------------------------

        // 入力は Composite（キーボード＋シリアル＋位置検知、ADR-15/24）。谷選択＝_inputMode で
        // Keyboard/Vision を明示切替、決定＝キーボード、削り＝キーボード Space OR ハンドル回転。
        // 各 Config 未割り当てなら該当ソースは生成しない（実機なしでもキーボードで全操作可能）。
        private IInputSource BuildInput()
        {
            var keyboard = NewChild("InputSource").AddComponent<KeyboardInputSource>();

            IInputSource serial = null;
            if (_serialConfig != null)
            {
                var go = NewChild("SerialInput");
                var handler = go.AddComponent<SerialHandler>();
                var serialSource = go.AddComponent<SerialInputSource>();
                handler.Begin(_serialConfig);
                serialSource.Configure(handler);
                serial = serialSource;
            }

            IInputSource vision = null;
            if (_visionConfig != null)
            {
                _visionSource = NewChild("VisionInput").AddComponent<VisionInputSource>();
                _visionSource.Configure(_visionConfig);
                _visionSource.FlipHorizontal = _visionFlipHorizontal;
                _visionSource.FlipVertical = _visionFlipVertical;
                vision = _visionSource;
            }

            return new CompositeInputSource(keyboard, serial, vision, _inputMode);
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
            go.transform.position = new Vector3(0f, HeightAt(0f, 0f), 0f); // 山頂に据える（ADR-21）
            DestroyCollider(go);
            go.GetComponent<MeshRenderer>().sharedMaterial = _fortMat;
            _fort = go.AddComponent<Fort>();
            _fort.Initialize(_fortHpMax);
        }

        private void BuildChannels()
        {
            string[] names = { "N", "W", "S", "E" };
            _channels = new ChannelController[4];
            for (int id = 0; id < 4; id++)
            {
                var cgo = NewChild("Channel_" + names[id]);
                var ch = cgo.AddComponent<ChannelController>();
                ch.Configure(id, Vector3.zero, _projectileFactory, _fort, CreateEnemy, CreateAlly, CreateDisguised, CreateSnowPuff, CreateDissolveBurst, HeightAt);
                ch.OnAllyCaught += OnAllyCaughtHandler;
                _channels[id] = ch;
            }
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
            foreach (var ch in _channels) ch.OnEnemyKilled += OnEnemyKilledForAudio; // Monster（行単位）
            _avalanche.OnAvalancheChanged += OnAvalancheChangedForAudio;     // Avalanche loop on/off
            _fort.OnHpChanged += OnFortHpChangedForAudio;                    // Hit（減少 かつ >0）
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

        private Enemy CreateEnemy()
        {
            if (_enemyModel != null) return CreateModelActor<Enemy>("Enemy", _enemyModel);

            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Enemy"; go.transform.localScale = Vector3.one * 2f;
            DestroyCollider(go);
            go.GetComponent<MeshRenderer>().sharedMaterial = _enemyMat;
            return go.AddComponent<Enemy>();
        }

        /// <summary>
        /// FBX を「足元が原点・_enemyModelHeight の高さ」に正規化して器の子に入れる。
        ///
        /// 器（ルート）に Renderer を置かないのが要点。Enemy.Init の tint（ADR-16）は
        /// GetComponent&lt;Renderer&gt;() が null のとき何もしないので、モデル本来の配色がそのまま残る。
        /// 位置合わせは器の transform に対して行われるため、移動・撃破ロジックは Cube のときと同一。
        /// </summary>
        private T CreateModelActor<T>(string name, GameObject model) where T : Component
        {
            var root = new GameObject(name);
            root.transform.SetParent(transform);

            var view = Instantiate(model, root.transform);
            view.name = "View";
            view.transform.localPosition = Vector3.zero;
            view.transform.localRotation = Quaternion.identity;
            view.transform.localScale = Vector3.one;

            var b = ModelBounds(view);
            float scale = b.size.y > 0.0001f ? _enemyModelHeight / b.size.y : 1f;
            view.transform.localScale = Vector3.one * scale;

            // 最下点を y=0 へ。X/Z はモデルの原点を尊重する（剣などで bounds 中心がずれるため寄せない）。
            float footOffset = -(b.center.y - b.size.y * 0.5f) * scale;
            view.transform.localPosition = new Vector3(0f, footOffset, 0f);

            foreach (var c in view.GetComponentsInChildren<Collider>(true)) Destroy(c);

            return root.AddComponent<T>();
        }

        // スケール1・原点配置の状態でのローカル境界。Renderer が無ければゼロ。
        private static Bounds ModelBounds(GameObject view)
        {
            var rends = view.GetComponentsInChildren<Renderer>(true);
            if (rends.Length == 0) return new Bounds(Vector3.zero, Vector3.one);

            var b = rends[0].bounds;
            for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);

            var origin = view.transform.position;
            b.center -= origin;
            return b;
        }

        // 味方NPC: sharedMaterial は敵と共用（per-instance MPB で青に上書き）。
        private AllyNPC CreateAlly()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Ally"; go.transform.localScale = Vector3.one * 0.5f;
            DestroyCollider(go);
            go.GetComponent<MeshRenderer>().sharedMaterial = _enemyMat;
            return go.AddComponent<AllyNPC>();
        }

        // 変装敵: 青 tint は per-instance MPB で付く。tell マーカーは DisguisedEnemy.OnInitialized で生成。
        private DisguisedEnemy CreateDisguised()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "DisguisedEnemy"; go.transform.localScale = Vector3.one * 0.5f;
            DestroyCollider(go);
            go.GetComponent<MeshRenderer>().sharedMaterial = _enemyMat;
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

        private Snowball CreateSnowball()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "Snowball"; go.transform.localScale = Vector3.one * 0.22f;
            DestroyCollider(go);
            go.GetComponent<MeshRenderer>().sharedMaterial = _snowballMat;
            return go.AddComponent<Snowball>();
        }

        private Projectile CreateProjectile()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "Projectile"; go.transform.localScale = Vector3.one * 0.35f;
            DestroyCollider(go);
            go.GetComponent<MeshRenderer>().sharedMaterial = _projMat;
            return go.AddComponent<Projectile>();
        }

        private void UpdateStatusUI()
        {
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

        // 照準レーンの along 範囲（砦寄り→麓）と幅。斜面上に寝かせて描く（ADR-21・U6）。
        private const float AimNearAlong = 6.5f; // 砦寄り端（先頭行 8.0 の手前）
        private const float AimFarAlong = 10.0f; // 麓側端（最奥行 9.65 の少し外）
        private const float AimWidth = 2.0f;     // レーン幅（スロット横幅 ~2.1 を覆う）

        // 照準中の谷を示す半透明レーン。斜面(XZ＋高さ)に沿って寝かせ、麓へ伸びる帯（ADR-21）。
        private void BuildAimIndicator()
        {
            _aimMat = MakeUnlitAdditive(new Color(0.30f, 0.26f, 0.05f, 1f));
            // 斜面に寝かせると向きにより表裏が変わるため両面表示（カリング無効）。
            _aimMat.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "AimIndicator";
            go.transform.SetParent(transform);
            DestroyCollider(go);
            go.GetComponent<MeshRenderer>().sharedMaterial = _aimMat;
            _aimIndicator = go;
            _aimIndicator.SetActive(false);
        }

        private void UpdateAimIndicator()
        {
            int ch = _avalanche != null ? _avalanche.ActiveChannel : -1;
            if (ch < 0 || ch > 3)
            {
                if (_aimIndicator.activeSelf) _aimIndicator.SetActive(false);
                return;
            }

            // 外向き(XZ)に沿って砦寄り端→麓側端の 2 点を斜面高さでサンプルし、その傾きに沿って寝かせる。
            Vector3 dir = ChannelController.DirectionFor(ch); // XZ, y=0
            Vector3 nearP = dir * AimNearAlong; nearP.y = HeightAt(nearP.x, nearP.z);
            Vector3 farP = dir * AimFarAlong; farP.y = HeightAt(farP.x, farP.z);

            Vector3 laneDir = farP - nearP;
            float length = laneDir.magnitude;
            if (length < 1e-4f) { laneDir = dir; length = AimFarAlong - AimNearAlong; }
            else laneDir /= length;

            // 斜面法線（レーン方向に直交し上向き）: 帯を斜面に沿って寝かせつつ面をカメラ側へ向ける。
            Vector3 side = Vector3.Cross(Vector3.up, laneDir).normalized;
            Vector3 normal = Vector3.Cross(laneDir, side).normalized;
            // 単一の平面帯なので凸な斜面のバンプに食い込む。法線方向に少し浮かせて食い込みを抑える。
            Vector3 mid = (nearP + farP) * 0.5f + normal * 0.35f;

            // ローカル +Z=法線（面をカメラへ）、+Y=レーン方向（長さ）、+X=幅。
            _aimIndicator.transform.SetPositionAndRotation(mid, Quaternion.LookRotation(normal, laneDir));
            _aimIndicator.transform.localScale = new Vector3(AimWidth, length, 1f);
            if (!_aimIndicator.activeSelf) _aimIndicator.SetActive(true);

            // 削り中はより明るく（加算量UP）。RGB がそのまま加算される。
            bool shaving = _avalanche.IsAvalanching;
            _aimMat.SetColor("_BaseColor", shaving ? new Color(0.70f, 0.52f, 0.08f, 1f)
                                                   : new Color(0.30f, 0.26f, 0.05f, 1f));
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            // OnAllyCaught 購読解除（ADR-3）
            if (_channels != null)
                foreach (var ch in _channels)
                    if (ch != null) ch.OnAllyCaught -= OnAllyCaughtHandler;

            // サウンド購読解除（Phase 6 / ADR-26）。全新規購読を確実に外す。
            if (_projectileFactory != null) _projectileFactory.OnCreated -= OnProjectileCreatedForAudio;
            if (_channels != null)
                foreach (var ch in _channels)
                    if (ch != null) ch.OnEnemyKilled -= OnEnemyKilledForAudio;
            if (_avalanche != null) _avalanche.OnAvalancheChanged -= OnAvalancheChangedForAudio;
            if (_fort != null) _fort.OnHpChanged -= OnFortHpChangedForAudio;
        }
    }
}
