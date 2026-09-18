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

        [Header("ステージ設定（未割り当てなら既定値インスタンス）")]
        [SerializeField] private StageConfigSO _stageConfig;

        [Header("LiDAR 位置入力・雪玉放出・敵スポーンの調整値（未割り当てなら既定値インスタンス）")]
        [SerializeField] private LidarPreviewConfigSO _lidarPreviewConfig;

        [Tooltip("雪崩が通った箇所を一定時間白く塗る演出の調整値。未割り当てなら既定値インスタンス。")]
        [SerializeField] private SnowPaintConfigSO _snowPaintConfig;

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
        [SerializeField] private GameObject _enemyPrefab;
        [Tooltip("爆発兵のモデル。Assets/Prefabs/EnemyBombTrooper.prefab。")]
        [SerializeField] private GameObject _bomberPrefab;
        [Tooltip("盾兵のモデル。Assets/Prefabs/EnemyShieldTrooper.prefab。")]
        [SerializeField] private GameObject _shieldPrefab;
        [Header("演出プレハブ")]
        [Tooltip("雪崩の見た目（ParticleSystem 付き）。Assets/Prefabs/AvalancheSnow 系。未割り当てだと雪崩が出ない。")]
        [SerializeField] private GameObject _snowEffectPrefab;
        [Header("必殺技「ビーム砲」の演出（発動条件は StageConfigSO のゲージ。未割り当てなら既定値／見た目無しでロジックのみ動作）")]
        [SerializeField] private SpecialAttackConfigSO _specialAttackConfig;
        [Tooltip("山頂に出現するビーム砲のモデル。Assets/Prefabs/BeamCannon.prefab。")]
        [SerializeField] private GameObject _beamCannonPrefab;
        [Tooltip("衝撃波（ExplosionTelegraph の色違い）。Assets/Prefabs/SpecialAttackShockwave.prefab。")]
        [SerializeField] private GameObject _specialAttackShockwavePrefab;
        [Header("展示用設定")]
        [Tooltip("クリアしてから ResultScene へ移るまでの秒数（観客にリザルトを見せる間）。")]
        [SerializeField] private float _resultLimit = 3f;
        [Tooltip("砦 HP 0 になってから GameOverScene へ移るまでの秒数。")]
        [SerializeField] private float _gameOverLimit = 1f;
        [Tooltip("何コンボごとに「節目」（画面中央の虹色表示＋通常ポップアップの色/サイズ変化）とするか。"
               + "現場での撃破ペースが読めないため、Play せずここで調整できるようにしてある。")]
        [SerializeField] private int _comboMilestoneInterval = 10;
        private Fort _fort;
        private ProjectileFactory _projectileFactory;
        private EnemyField _field;

        // ADR-30: 単一ステージの集計（撃破数の進捗・コンボ・必殺ゲージ）。
        private KillProgress _progress;
        private ComboTracker _combo;
        private SpecialGauge _gauge;
        private SpecialAttackController _beam;
        private IInputSource _input;   // 決定キー（Title/Result の Enter）専用
        private LidarPositionInputSource _lidarPosSource; // ロスト警告用に保持（LiDAR/マウスOR合成でも常時生成）
        private LidarAvalancheVfx _lidarVfx;
        private SnowPaintTrail _snowPaintTrail;
        private SpawnDirector _spawnDirector;
        private UIManager _ui;
        private GameStateMachine _machine;
        private AudioService _audio;             // BGM/SE アダプタ（Phase 6 / ADR-26）
        private int _prevFortHp;                 // Hit 音の判定用（減少 かつ >0 のときだけ鳴らす）

        private float _battleStartTime;
        private float _battleEndTime;   // 決着した瞬間の時刻（リザルト中もタイムを固定して見せる）
        private bool _battleRunning;

        private Material _fortMat, _enemyMat, _snowPuffMat;
        private Material _dissolveMat, _emberMat; // 撃破時の蒸発演出（ADR-20 の cosmetic 系）

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;

            _fortMat = MakeUnlit(new Color(0.92f, 0.95f, 1.0f));
            _enemyMat = MakeUnlit(new Color(0.90f, 0.15f, 0.15f));
            _snowPuffMat = MakeUnlitAdditive(Color.white);
            _dissolveMat = MakeDissolve(new Color(0.75f, 1.90f, 2.60f, 1f)); // 発光帯：白〜水色
            _emberMat = MakeEmber(new Color(0.55f, 0.85f, 1.00f, 1f));       // 立ち上る粒

            // 山は Terrain（シーン配置・ADR-21）。手続き Quad は廃止。
            BuildFort();
            _projectileFactory = new ProjectileFactory(CreateProjectile, prewarm: 8);

            // ステージ設定は未割り当てでも既定値インスタンスで動かす（現場で asset を作って調整）。
            if (_stageConfig == null) _stageConfig = ScriptableObject.CreateInstance<StageConfigSO>();

            _progress = new KillProgress(_stageConfig.TargetKillCount);
            _combo = new ComboTracker(_stageConfig.ComboWindowSec);
            _gauge = new SpecialGauge(_stageConfig.GaugeMax, _stageConfig.GaugePerKill, _stageConfig.GaugeComboBonus);

            // 決定キー（Title/Result の Enter）専用。削りは位置入力＋LidarAvalancheVfx が担う。
            _input = NewChild("InputSource").AddComponent<KeyboardInputSource>();

            BuildField();

            BuildSpecialAttack();

            _spawnDirector = NewChild("SpawnDirector").AddComponent<SpawnDirector>();
            _spawnDirector.Configure(_field, _progress);

            _ui = NewChild("UIManager").AddComponent<UIManager>();
            _ui.Configure(_fort, _comboMilestoneInterval);

            // オーディオは購読対象（Fort/Factory/EnemyField）が揃った後に配線（ADR-26）。
            BuildAudio();

            // LiDAR 連続座標（LidarPosition）／マウス疑似（SimPointer）の位置入力＋雪玉放出。
            BuildLidarPath();

            // 雪崩が通った箇所を一定時間白く塗る演出（見た目のみ。_lidarVfx が生成済みの後で配線）。
            BuildSnowPaintOverlay();

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

            UpdateBattleSystems();
            UpdateStatusUI();
            UpdateTrackerWarning();
        }

        // コンボの時間切れ判定と、ゲージ MAX での必殺ビーム自動発動（ADR-30）。
        // 展示にはプレイヤーが押せるボタンが無いため、発動は入力ではなくゲージが担う。
        private void UpdateBattleSystems()
        {
            if (!_battleRunning) return;

            _combo.Tick(Time.time);

            if (_gauge.IsFull && !_beam.IsPlaying)
            {
                _gauge.Consume();
                _beam.Fire();
            }
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

        public int TargetKillCount => _progress.Target;
        public int KillCount => _progress.Count;
        public int MaxCombo => _combo.MaxCount;
        public float ElapsedSec => _battleRunning ? Time.time - _battleStartTime : _battleEndTime - _battleStartTime;
        public bool IsFortDestroyed => _fort.IsDestroyed;
        public bool IsStartRequested => _input.ConfirmPressed;
        public bool IsRetryRequested => _input.ConfirmPressed;
        /// <summary>カウントダウンの1ステップ（"3"/"2"/"1"/"スタート"）を表示し、対応するSEを鳴らす。
        /// 3.2.1 は共通SE、スタートのみ専用SE（isFinal で判別）。</summary>
        public void ShowCountdownStep(string text, bool isFinal)
        {
            _ui.SetCountdown(text, isFinal);
            if (isFinal) _audio?.PlayCountdownGo();
            else _audio?.PlayCountdownBeep();
        }

        /// <summary>カウントダウン表示を消す（CountdownState.Exit で呼ばれる）。</summary>
        public void ClearCountdown() => _ui.ClearCountdown();

        /// <summary>戦闘開始（初回 / リトライ）。砦HP・進捗・コンボ・ゲージ・残敵をリセットする。</summary>
        public void BeginBattle()
        {
            _fort.Initialize(_fortHpMax);
            _progress.Reset();
            _combo.Reset();
            _gauge.Reset();
            _field.ClearAll();
            _field.ConfigureStage(_stageConfig);
            _ui.ClearResult();

            _battleStartTime = Time.time;
            _battleEndTime = _battleStartTime;
            _battleRunning = true;
            _spawnDirector.StartBattle(_stageConfig);
        }

        public void ClearRemainingEnemies()
        {
            _battleEndTime = Time.time;
            _battleRunning = false;
            _spawnDirector.Stop();
            _field.ClearAll();
        }

        public void ShowResult(StageResult result)
        {
            _ui.ShowResult(result);
            GameResultHolder.Last = result; // 次のシーン（Result / GameOver）へ受け渡す

            // 勝敗ジングルは BGM に重ねる（BGM は止めない・Q4-A / ADR-26）。
            if (result.IsWin) _audio?.PlayClear();
            else _audio?.PlayGameOver();

            // クリア→ResultScene / 失敗→GameOverScene（ADR-30）。
            if (result.IsWin) StartCoroutine(ResultSceneTransition(_resultLimit, "ResultScene"));
            else StartCoroutine(ResultSceneTransition(_gameOverLimit, "GameOverScene"));
        }

        // 決着からリザルト系シーンへ移るまでの間（展示ではこの数秒で観客に結果を見せる）。
        private System.Collections.IEnumerator ResultSceneTransition(float delaySeconds, string sceneName)
        {
            yield return new WaitForSeconds(delaySeconds);
            UnityEngine.SceneManagement.SceneManager.LoadScene(sceneName);
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
                CreateEnemy, CreateBomber, CreateShield,
                CreateSnowPuff, CreateDissolveBurst,
                HeightAt, _lidarPreviewConfig);
            _field.ConfigureStage(_stageConfig);
        }

        // 必殺技「ビーム砲」の演出進行役。発動は UpdateBattleSystems のゲージ判定（ADR-30）で、
        // ここは「砲が出る → 光線一周 → 衝撃波で全員撃破 → 砲が消える」の見た目を組み立てるだけ。
        private void BuildSpecialAttack()
        {
            if (_specialAttackConfig == null)
                _specialAttackConfig = ScriptableObject.CreateInstance<SpecialAttackConfigSO>();

            _beam = NewChild("SpecialAttack").AddComponent<SpecialAttackController>();
            _beam.Configure(_field, _fort.transform, _specialAttackConfig,
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
            _lidarVfx.Configure(posInput, _field, HeightAt, CreateAvalancheEffect, cfg);
            _lidarVfx.OnAvalancheChanged += OnAvalancheChangedForAudio; // 雪崩ループ音（エッジ）
        }

        // 山メッシュにほぼ重ねたオーバーレイへ、雪崩の通過痕跡（軌跡点）を描く（見た目のみ・Core非改変）。
        // 山本体のマテリアル／シェーダーには一切触れない。_groundSurface に MeshFilter が無ければ何もしない。
        private void BuildSnowPaintOverlay()
        {
            if (_snowPaintConfig == null) _snowPaintConfig = ScriptableObject.CreateInstance<SnowPaintConfigSO>();

            var groundMesh = _groundSurface != null ? _groundSurface.GetComponent<MeshFilter>() : null;
            if (groundMesh == null || groundMesh.sharedMesh == null)
            {
                Debug.LogWarning("SnowPaintOverlay: _groundSurface に MeshFilter が無いため雪崩の白塗り演出をスキップします。");
                return;
            }

            var go = NewChild("SnowPaintOverlay");
            go.transform.SetPositionAndRotation(_groundSurface.transform.position, _groundSurface.transform.rotation);
            go.transform.localScale = _groundSurface.transform.lossyScale;

            go.AddComponent<MeshFilter>().sharedMesh = groundMesh.sharedMesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = MakeSnowPaintOverlayMaterial(_snowPaintConfig);
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;

            _snowPaintTrail = go.AddComponent<SnowPaintTrail>();
            _snowPaintTrail.Configure(mr, _snowPaintConfig);
            _lidarVfx.OnAvalanchePassed += _snowPaintTrail.AddPoint;
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

        // サウンド配線（Phase 6 / ADR-26）。薄い AudioService を子生成し、BGM 開始＋additive イベント購読。
        // 追加は cosmetic な購読のみで既存ロジックの分岐・数値・順序は不変。_audioConfig 未割り当てでも無音で安全。
        private void BuildAudio()
        {
            _audio = NewChild("AudioService").AddComponent<AudioService>();
            _audio.Configure(_audioConfig);
            _audio.PlayBgm(); // 開始時に1回・以後止めない（Q4-A）

            _prevFortHp = _fort.CurrentHp;

            _projectileFactory.OnCreated += OnProjectileCreatedForAudio;     // EnemyAttack（毎発）
            _field.OnEnemyKilled += OnEnemyKilled;                           // 撃破の集計＋Monster 音
            _fort.OnHpChanged += OnFortHpChangedForAudio;                    // Hit（減少 かつ >0）
            // 雪崩ループ音（OnAvalancheChanged）は BuildLidarPath で _lidarVfx に配線する。
        }

        private void OnProjectileCreatedForAudio() => _audio.PlayEnemyAttack();
        private void OnAvalancheChangedForAudio(bool on) => _audio.SetAvalanche(on);

        /// <summary>
        /// 敵を 1 体撃破したときの集計（ADR-30）。出どころで扱いを分ける:
        ///  ・雪玉／爆発 … 撃破数・コンボ・ゲージすべてに算入（連鎖 20 体ならコンボも +20）
        ///  ・ビーム … 撃破数のみ算入。ゲージには入れず（自己充填ループの防止）、コンボは中立
        /// </summary>
        private void OnEnemyKilled(KillSource source, Vector3 position)
        {
            _audio?.PlayEnemyKilled();
            _progress.Add();

            if (source == KillSource.Beam) return;

            _combo.AddKill(Time.time);
            _gauge.AddKill(_combo.Count);

            // コンボ表示は撃破位置へのポップアップのみ（画面固定の常設表示は廃止）。
            if (_combo.Count >= 2) _ui.ShowComboPopup(position, _combo.Count);
        }

        // 砦被弾＝HP が「減少 かつ 新HP>0」のときのみ Hit（Initialize の満タン通知・0到達=GameOver を除外）。
        private void OnFortHpChangedForAudio(int cur, int max)
        {
            if (cur < _prevFortHp && cur > 0) _audio.PlayHit();
            _prevFortHp = cur;
        }

        // private Enemy CreateEnemy()
        // {
        //     var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        //     go.name = "Enemy"; go.transform.localScale = Vector3.one * 0.5f;
        //     DestroyCollider(go);
        //     go.GetComponent<MeshRenderer>().sharedMaterial = _enemyMat;
        //     return go.AddComponent<Enemy>();
        // }
        private Enemy CreateEnemy()
        {
            var go = GameObject.Instantiate(_enemyPrefab);
            go.name = "Enemy";
            DestroyCollider(go);
            return go.AddComponent<Enemy>();
        }

        // 爆発兵（ADR-30）。専用モデル EnemyBombTrooper.prefab。未割り当てなら通常敵の見た目で代替する。
        private BombTrooper CreateBomber()
        {
            var go = GameObject.Instantiate(_bomberPrefab != null ? _bomberPrefab : _enemyPrefab);
            go.name = "BombTrooper";
            DestroyCollider(go);
            return go.AddComponent<BombTrooper>();
        }

        // 盾兵（ADR-30）。専用モデル EnemyShieldTrooper.prefab。未割り当てなら通常敵の見た目で代替する。
        private ShieldTrooper CreateShield()
        {
            var go = GameObject.Instantiate(_shieldPrefab != null ? _shieldPrefab : _enemyPrefab);
            go.name = "ShieldTrooper";
            DestroyCollider(go);
            return go.AddComponent<ShieldTrooper>();
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

        // 雪崩の見た目（ParticleSystem 付きプレハブ）。粒子を地形に当てるため衝突モジュールを World にする。
        private AvalancheSnowEffect CreateAvalancheEffect()
        {
            var go = GameObject.Instantiate(_snowEffectPrefab);
            go.name = "AvalancheSnowEffect";

            // 子に ParticleSystem がある構成も拾う。
            var ps = go.GetComponent<ParticleSystem>();
            if (ps == null) ps = go.GetComponentInChildren<ParticleSystem>();

            if (ps != null)
            {
                var col = ps.collision;
                col.enabled = true;
                col.type = ParticleSystemCollisionType.World;
            }
            DestroyCollider(go);
            return go.AddComponent<AvalancheSnowEffect>();
        }

        private Projectile CreateProjectile()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "Projectile"; go.transform.localScale = Vector3.one * 0.35f;
            DestroyCollider(go);
            // 敵に攻撃モーション（ADR-23）が付いたので弾は見せない（ロジックのみ）。被弾は砦 HP バーと Hit 音で伝える。
            go.GetComponent<MeshRenderer>().enabled = false;
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
                _ui.SetBattleStats(0, _progress.Target, 0f, 0f);
            }
            else if (cur is CountdownState)
            {
                // バナー相当の表示は ShowCountdownStep（IGameContext経由）で都度セット済みなのでここでは触らない。
                _ui.SetStatus("");
            }
            else if (cur is BattleState)
            {
                _ui.ClearBanner();
                _ui.SetStatus($"経過 {StageResult.FormatTime(ElapsedSec)}");
                _ui.SetBattleStats(_progress.Count, _progress.Target,
                    _gauge.Normalized, ElapsedSec);
            }
            else if (cur is ResultState)
            {
                _ui.ClearBanner();
                _ui.SetStatus("");
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

        // 雪崩の通過痕跡オーバーレイ用マテリアル（Custom/SnowPaintOverlay）。
        private static Material MakeSnowPaintOverlayMaterial(SnowPaintConfigSO cfg)
        {
            var shader = Shader.Find("Custom/SnowPaintOverlay");
            if (shader == null)
            {
                Debug.LogWarning("Custom/SnowPaintOverlay が見つかりません。雪崩の白塗り演出は Unlit で代替します。");
                return MakeUnlit(cfg.PaintColor);
            }
            var m = new Material(shader);
            m.SetColor("_PaintColor", cfg.PaintColor);
            m.SetFloat("_PaintFadeSec", cfg.PaintFadeSec);
            m.SetFloat("_SectorDeg", cfg.SectorDeg);
            m.SetFloat("_MaxRadius", cfg.MaxRadius);
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
            if (_field != null) _field.OnEnemyKilled -= OnEnemyKilled;
            if (_projectileFactory != null) _projectileFactory.OnCreated -= OnProjectileCreatedForAudio;
            if (_lidarVfx != null) _lidarVfx.OnAvalancheChanged -= OnAvalancheChangedForAudio;
            if (_lidarVfx != null && _snowPaintTrail != null) _lidarVfx.OnAvalanchePassed -= _snowPaintTrail.AddPoint;
            if (_fort != null) _fort.OnHpChanged -= OnFortHpChangedForAudio;
        }
    }
}
