// =============================================================================
//  TEMPORARY DEMO — 使い捨て。マイルストーンE の GameManager（Composition Root）が
//  完成したら、この Demo フォルダごと削除すること。
//  目的: Unit 8-13（Input / Fort / Projectile / Enemy / ChannelController /
//        AvalancheController / IceStock）を Play するだけで対話的に触れるようにする最小ハーネス。
//        production のロジックには一切手を入れない（依存を配線するだけ）。
//
//  操作: W/A/S/D = 谷を選択（N/W/S/E・同時1本ラッチ） / Space 長押し = 削る（雪崩）
//        R = リセット。 敵は fireInterval ごとに砦へ発射、砦HP が減る。
// =============================================================================
using UnityEngine;
using UnityEngine.InputSystem;
using KakigoriTD.Core;
using KakigoriTD.Data;
using KakigoriTD.Factory;
using KakigoriTD.Gameplay;
using KakigoriTD.Input;

namespace KakigoriTD.Demo
{
    /// <summary>
    /// 【使い捨てデモ】Play 時に砦・放射状4谷・敵・弾プール・入力・氷・雪崩制御を配線し、
    /// 「削る→雪崩→先頭行消去」の手触りを対話的に確認できるようにする。
    /// GameManager 実装時に削除する（本物の Composition Root に置き換わる）。
    /// </summary>
    public class DemoHarness : MonoBehaviour
    {
        [SerializeField] private EnemyTypeSO _enemyType; // BasicEnemy を割り当て（弾種は SO 側で配線済み）
        [SerializeField] private int _fortHp = 1000;
        [SerializeField] private float _iceTotal = 50f;
        [SerializeField] private int _rowsPerChannel = 3;

        private static readonly string[] ChannelNames = { "N", "W", "S", "E" };

        private Fort _fort;
        private ProjectileFactory _factory;
        private IceStock _ice;
        private KeyboardInputSource _input;
        private AvalancheController _avalanche;
        private ChannelController[] _channels;
        private Material _fortMat, _enemyMat, _projMat;
        private string _status = "W/A/S/D で谷を選び、Space 長押しで削る";

        private void Start()
        {
            _fortMat = MakeUnlit(new Color(0.85f, 0.90f, 0.97f));
            _enemyMat = MakeUnlit(new Color(0.90f, 0.15f, 0.15f));
            _projMat = MakeUnlit(new Color(1.0f, 0.85f, 0.10f));

            // 砦（中心・山頂）
            var fortGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            fortGo.name = "Demo_Fort";
            fortGo.transform.localScale = new Vector3(1.3f, 1.3f, 1f);
            DestroyCollider(fortGo);
            fortGo.GetComponent<MeshRenderer>().sharedMaterial = _fortMat;
            _fort = fortGo.AddComponent<Fort>();
            _fort.Initialize(_fortHp);
            _fort.OnDestroyed += () => _status = "FORT DESTROYED — [R] でリセット";

            _factory = new ProjectileFactory(CreateProjectile, prewarm: 8);

            // 放射状4谷 + 入力 + 氷 + 雪崩制御
            _channels = new ChannelController[4];
            for (int id = 0; id < 4; id++)
            {
                var cgo = new GameObject("Demo_Channel_" + ChannelNames[id]);
                var ch = cgo.AddComponent<ChannelController>();
                ch.Configure(id, Vector3.zero, _factory, _fort, CreateEnemy);
                _channels[id] = ch;
            }
            SpawnFormation();

            _input = new GameObject("Demo_Input").AddComponent<KeyboardInputSource>();
            _ice = new IceStock(_iceTotal);
            _avalanche = new GameObject("Demo_Avalanche").AddComponent<AvalancheController>();
            _avalanche.Configure(_input, _ice, _channels);
        }

        private void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.rKey.wasPressedThisFrame)
            {
                _fort.Initialize(_fortHp);
                foreach (var ch in _channels) ch.ClearAll();
                SpawnFormation();
                _ice = new IceStock(_iceTotal);
                _avalanche.Configure(_input, _ice, _channels); // 氷をリフレッシュ
                _status = "reset [R] — W/A/S/D で谷を選び Space 長押しで削る";
            }
        }

        private void SpawnFormation()
        {
            foreach (var ch in _channels)
                for (int n = 0; n < _rowsPerChannel * 3; n++) ch.SpawnEnemy(_enemyType);
        }

        private Enemy CreateEnemy()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Demo_Enemy"; go.transform.localScale = Vector3.one * 0.5f;
            DestroyCollider(go);
            go.GetComponent<MeshRenderer>().sharedMaterial = _enemyMat;
            return go.AddComponent<Enemy>();
        }

        private Projectile CreateProjectile()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "Demo_Projectile"; go.transform.localScale = Vector3.one * 0.35f;
            DestroyCollider(go);
            go.GetComponent<MeshRenderer>().sharedMaterial = _projMat;
            return go.AddComponent<Projectile>();
        }

        private static void DestroyCollider(GameObject go)
        {
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);
        }

        private Material MakeUnlit(Color c)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            m.SetColor("_BaseColor", c);
            return m;
        }

        private void OnGUI()
        {
            int ch = _input != null ? _input.ActiveChannel : -1;
            string active = ch >= 0 && ch < 4 ? ChannelNames[ch] : "(なし)";
            bool shaving = _avalanche != null && _avalanche.IsAvalanching;

            string counts = "";
            if (_channels != null)
                for (int i = 0; i < _channels.Length; i++)
                    counts += $"{ChannelNames[i]}:{_channels[i].EnemyCount}  ";

            GUI.contentColor = Color.white;
            GUI.Label(new Rect(12, 10, 1100, 160),
                "=== DEMO (Unit 8-13 / 使い捨て) ===\n" +
                $"砦HP: {_fort.CurrentHp}/{_fort.MaxHp}   氷: {_ice.Current:F1}/{_ice.Total:F0}   照準谷: {active}   {(shaving ? "▼ 削り中(雪崩)" : "")}\n" +
                $"敵: {counts}\n" +
                "[W/A/S/D] 谷を選択（N/W/S/E）   [Space] 長押しで削る（先頭行から消える）   [R] リセット\n" +
                $"{_status}");
        }
    }
}
