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

        [Header("調整値（spec §主要パラメータ）")]
        [SerializeField] private int _fortHpMax = 1000;
        [SerializeField] private float _iceTotal = 50f;
        [SerializeField] private float _waveLull = 5f;
        [SerializeField] private float _waveIntroDuration = 1.2f;

        private Fort _fort;
        private ProjectileFactory _projectileFactory;
        private ChannelController[] _channels;
        private IceStock _ice;
        private KeyboardInputSource _input;
        private AvalancheController _avalanche;
        private WaveManager _waveManager;
        private UIManager _ui;
        private GameStateMachine _machine;

        private int _currentWaveIndex;
        private float _waveStartTime;

        private Material _fortMat, _enemyMat, _projMat;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;

            _fortMat = MakeUnlit(new Color(0.85f, 0.90f, 0.97f));
            _enemyMat = MakeUnlit(new Color(0.90f, 0.15f, 0.15f));
            _projMat = MakeUnlit(new Color(1.0f, 0.85f, 0.10f));

            BuildFort();
            _projectileFactory = new ProjectileFactory(CreateProjectile, prewarm: 8);
            BuildChannels();

            _ice = new IceStock(_iceTotal);
            _input = NewChild("InputSource").AddComponent<KeyboardInputSource>();

            _avalanche = NewChild("AvalancheController").AddComponent<AvalancheController>();
            _avalanche.Configure(_input, _ice, _channels);

            _waveManager = NewChild("WaveManager").AddComponent<WaveManager>();
            _waveManager.Configure(_channels);

            _ui = NewChild("UIManager").AddComponent<UIManager>();
            _ui.Configure(_fort, _ice, _avalanche);

            _machine = new GameStateMachine();
            _machine.Change(new TitleState(_machine, this));
        }

        private void Update()
        {
            _machine.Tick(Time.deltaTime);
            UpdateStatusUI();
        }

        // ---- IGameContext ----------------------------------------------------

        public int WaveCount => _waveConfigs != null ? _waveConfigs.Length : 0;
        public float GetWaveDuration(int waveIndex) =>
            (_waveConfigs != null && waveIndex >= 0 && waveIndex < _waveConfigs.Length) ? _waveConfigs[waveIndex].Duration : 0f;
        public float WaveLullDuration => _waveLull;
        public float WaveIntroDuration => _waveIntroDuration;
        public bool IsFortDestroyed => _fort.IsDestroyed;
        public int FortHp => _fort.CurrentHp;
        public bool IsStartRequested => _input.ConfirmPressed;
        public bool IsRetryRequested => _input.ConfirmPressed;

        public void BeginWave(int waveIndex)
        {
            // wave 0 開始 = 新規プレイ（初回 / リトライ）。砦HP・氷・残敵をリセットする。
            if (waveIndex == 0)
            {
                _fort.Initialize(_fortHpMax);
                _ice.Refill();
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

        public void ShowResult(bool isWin, int score) => _ui.ShowResult(isWin, score);

        // ---- build helpers ---------------------------------------------------

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
                ch.Configure(id, Vector3.zero, _projectileFactory, _fort, CreateEnemy);
                _channels[id] = ch;
            }
        }

        private Enemy CreateEnemy()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Enemy"; go.transform.localScale = Vector3.one * 0.5f;
            DestroyCollider(go);
            go.GetComponent<MeshRenderer>().sharedMaterial = _enemyMat;
            return go.AddComponent<Enemy>();
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
                _ui.ClearResult(); // リザルト表示を消してタイトルへ
                _ui.SetStatus("かき氷タワーディフェンス — [Enter] で開始");
            }
            else if (cur is WaveIntroState)
            {
                _ui.SetStatus("WAVE 開始…");
            }
            else if (cur is WavePlayingState)
            {
                float remaining = Mathf.Max(0f, GetWaveDuration(_currentWaveIndex) - (Time.time - _waveStartTime));
                _ui.SetStatus($"Wave {_currentWaveIndex + 1}/{WaveCount}   残り {remaining:F0}s   — W/A/S/D 谷選択 / Space 削り");
            }
            else if (cur is WaveLullState)
            {
                _ui.SetStatus($"Wave {_currentWaveIndex + 1} クリア — 次の波へ");
            }
            else if (cur is ResultState)
            {
                _ui.SetStatus(""); // リザルトはオーバーレイ（ShowResult）で表示。背後の Wave 文を消す
            }
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

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }
    }
}
