using UnityEngine;
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
        private WaveManager _waveManager;
        private UIManager _ui;
        private GameStateMachine _machine;

        private int _currentWaveIndex;
        private float _waveStartTime;

        private Material _fortMat, _enemyMat, _projMat, _aimMat, _snowPuffMat;
        private GameObject _aimIndicator; // 照準中の谷を示すレーン（半透明）

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;

            _fortMat = MakeUnlit(new Color(0.92f, 0.95f, 1.0f));
            _enemyMat = MakeUnlit(new Color(0.90f, 0.15f, 0.15f));
            _projMat = MakeUnlit(new Color(1.0f, 0.85f, 0.10f));
            _snowPuffMat = MakeUnlitAdditive(Color.white);

            BuildMountain(); // 雪山の俯瞰背景（頂上=中央 → 麓=外周）。敵が麓に居ることを伝える
            BuildFort();
            _projectileFactory = new ProjectileFactory(CreateProjectile, prewarm: 8);
            _penalty = new PenaltyTally();
            BuildChannels();

            _ice = new IceStock(_iceTotal);
            _input = BuildInput();

            _avalanche = NewChild("AvalancheController").AddComponent<AvalancheController>();
            _avalanche.Configure(_input, _ice, _channels);

            _waveManager = NewChild("WaveManager").AddComponent<WaveManager>();
            _waveManager.Configure(_channels);

            _ui = NewChild("UIManager").AddComponent<UIManager>();
            _ui.Configure(_fort, _ice, _avalanche);

            BuildAimIndicator();

            _machine = new GameStateMachine();
            _machine.Change(new TitleState(_machine, this));
        }

        private void Update()
        {
            _machine.Tick(Time.deltaTime);
            UpdateStatusUI();
            UpdateAimIndicator();
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

        public void ShowResult(bool isWin, int score) => _ui.ShowResult(isWin, score, _penalty?.Total ?? 0);

        // ---- build helpers ---------------------------------------------------

        // 入力は Composite（キーボード＋シリアル、ADR-15）。谷選択/決定＝キーボード、
        // 削り＝キーボード Space OR ハンドル回転。SerialConfigSO 未割り当てならシリアルは生成せず
        // キーボードのみ（実機なしでもそのまま遊べる）。
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

            return new CompositeInputSource(keyboard, serial);
        }

        private void BuildFort()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Fort";
            go.transform.SetParent(transform);
            go.transform.localScale = new Vector3(1.3f, 1.3f, 1f);
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
                ch.Configure(id, Vector3.zero, _projectileFactory, _fort, CreateEnemy, CreateAlly, CreateDisguised, CreateSnowPuff);
                ch.OnAllyCaught += OnAllyCaughtHandler;
                _channels[id] = ch;
            }
        }

        private void OnAllyCaughtHandler() => _penalty.Add(_allyHitPenalty);

        private Enemy CreateEnemy()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Enemy"; go.transform.localScale = Vector3.one * 0.5f;
            DestroyCollider(go);
            go.GetComponent<MeshRenderer>().sharedMaterial = _enemyMat;
            return go.AddComponent<Enemy>();
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

        // 山を俯瞰した背景。中央=頂上（砦）から外へ広がる「緑の同心円（弓道の的状）」、
        // 一番外側の円より外は茶色い地面（＝麓）。敵はこの一番外側の円の外＝地面に湧く。
        // 全オブジェクトより奥(z)に置く大きな板ポリゴンに、手続き生成したテクスチャを貼る。
        private void BuildMountain()
        {
            var tex = GenerateMountainTexture(512);
            var mat = MakeUnlit(Color.white);
            mat.SetTexture("_BaseMap", tex);

            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "Mountain";
            go.transform.SetParent(transform);
            go.transform.position = new Vector3(0f, 0f, 3f); // 最奥
            go.transform.localScale = new Vector3(20f, 12f, 1f); // 画面全体を覆う
            DestroyCollider(go);
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }

        private static Texture2D GenerateMountainTexture(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            // 緑の山＝中央(頂上)から外へ向かう同心円バンド。一番外側の円(mountainR)より外は茶色い地面。
            Color summit   = new Color(0.60f, 0.82f, 0.46f); // 頂上（明るい緑）
            Color midSlope = new Color(0.38f, 0.64f, 0.32f); // 中腹
            Color lowSlope = new Color(0.24f, 0.48f, 0.24f); // 麓側の山肌（濃い緑）
            Color ground   = new Color(0.42f, 0.30f, 0.18f); // 地面（茶色）＝麓。敵はここに湧く
            Color ringLine = new Color(0.15f, 0.32f, 0.17f); // 円の輪郭線（濃い緑）
            const float worldW = 20f, worldH = 12f;
            const float mountainR = 2.2f;  // 一番外側の円＝山の縁（これより外は地面）
            const float edgeBlend = 0.16f; // 山→地面のなじませ幅
            // 同心円のバンド境界（内側→外側）。ここに輪郭線を引いて「的」状に見せる。
            float[] bands = { 0.85f, 1.55f, mountainR };

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float wx = ((x + 0.5f) / size - 0.5f) * worldW;
                    float wy = ((y + 0.5f) / size - 0.5f) * worldH;
                    float r = Mathf.Sqrt(wx * wx + wy * wy);

                    Color c;
                    if (r >= mountainR + edgeBlend)
                    {
                        c = ground; // 一番外側の円の外＝麓の地面
                    }
                    else
                    {
                        // バンドで山肌の緑を決める（同心円＝的の見た目）。
                        Color mountain = r < bands[0] ? summit : (r < bands[1] ? midSlope : lowSlope);
                        // 縁は地面へなじませる。
                        if (r > mountainR - edgeBlend)
                        {
                            float t = Mathf.Clamp01((r - (mountainR - edgeBlend)) / (2f * edgeBlend));
                            mountain = Color.Lerp(mountain, ground, t);
                        }
                        c = mountain;
                        // バンド境界に円の輪郭線。
                        for (int i = 0; i < bands.Length; i++)
                        {
                            if (Mathf.Abs(r - bands[i]) < 0.035f) { c = Color.Lerp(c, ringLine, 0.6f); break; }
                        }
                    }

                    c.a = 1f;
                    tex.SetPixel(x, y, c);
                }
            }
            tex.Apply();
            return tex;
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

        // 照準中の谷を示す半透明レーン（砦付近→麓へ伸びる帯）。敵より奥(z)に置き敵を隠さない。
        private void BuildAimIndicator()
        {
            _aimMat = MakeUnlitAdditive(new Color(0.30f, 0.26f, 0.05f, 1f));
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "AimIndicator";
            go.transform.SetParent(transform);
            // ローカル +Y を「外向き」に合わせる前提で、幅2.2 × 長さ4.4 の帯。
            go.transform.localScale = new Vector3(2.2f, 4.4f, 1f);
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

            Vector3 dir = ChannelController.DirectionFor(ch);
            // 砦付近(≈0.8)から麓(≈5.0)までを覆う帯。z=0.6 で敵(z=0)より奥に置く。
            // Z軸回転のみ（XY平面・法線は -Z のままカメラを向く。FromToRotation だと S/E/W で
            // 法線が反転して裏面カリングされ見えなくなるため）。ローカル +Y を dir に向ける。
            float angleDeg = Mathf.Atan2(-dir.x, dir.y) * Mathf.Rad2Deg;
            _aimIndicator.transform.SetPositionAndRotation(
                dir * 2.9f + new Vector3(0f, 0f, 0.6f),
                Quaternion.Euler(0f, 0f, angleDeg));
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
        }
    }
}
