// =============================================================================
//  TEMPORARY DEMO — 使い捨て。マイルストーンE の GameManager（Composition Root）が
//  完成したら、この Demo フォルダごと削除すること。
//  目的: Unit 9-11（Fort / Projectile / ProjectileFactory / Enemy）を Play するだけで
//        目視確認できるようにする最小ハーネス。production のロジックには一切手を入れない。
// =============================================================================
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using KakigoriTD.Data;
using KakigoriTD.Factory;
using KakigoriTD.Gameplay;

namespace KakigoriTD.Demo
{
    /// <summary>
    /// 【使い捨てデモ】Play 時に砦・敵×4・弾プールを生成し、Unit 9-11 の挙動を可視化する。
    /// GameManager 実装時に削除する（本物の Composition Root に置き換わる）。
    /// </summary>
    public class DemoHarness : MonoBehaviour
    {
        // 唯一の外部参照。BasicEnemy.asset を Inspector で割り当てる（弾種は SO 側で配線済み）。
        [SerializeField] private EnemyTypeSO _enemyType;
        [SerializeField] private int _fortHp = 1000;

        // 放射状4谷の麓（N/W/S/E）。カメラは ortho size5（横±8.89 / 縦±5）。
        private static readonly Vector3[] MuzzlePositions =
        {
            new Vector3(0f, 4.2f, 0f),   // N
            new Vector3(-7f, 0f, 0f),    // W
            new Vector3(0f, -4.2f, 0f),  // S
            new Vector3(7f, 0f, 0f),     // E
        };

        private Fort _fort;
        private ProjectileFactory _factory;
        private readonly List<Enemy> _enemies = new();
        private Material _fortMat, _enemyMat, _projMat;
        private string _status = "running";

        private void Start()
        {
            // 2D ortho ＋ 2D ライト下でも確実に見えるよう、Unlit の明色マテリアルを実行時生成。
            _fortMat = MakeUnlit(new Color(0.85f, 0.90f, 0.97f));
            _enemyMat = MakeUnlit(new Color(0.90f, 0.15f, 0.15f));
            _projMat = MakeUnlit(new Color(1.00f, 0.85f, 0.10f));

            // 砦（山頂・中央）
            var fortGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            fortGo.name = "Demo_Fort";
            fortGo.transform.localScale = new Vector3(1.6f, 1.6f, 1f);
            DestroyCollider(fortGo);
            fortGo.GetComponent<MeshRenderer>().sharedMaterial = _fortMat;
            _fort = fortGo.AddComponent<Fort>();
            _fort.Initialize(_fortHp);
            _fort.OnDestroyed += () => _status = "FORT DESTROYED (Lose)";

            // 弾プール（生成方法を注入＝ADR-11）。prewarm で初回ヒッチ回避。
            _factory = new ProjectileFactory(CreateProjectile, prewarm: 8);

            SpawnAllEnemies();
        }

        private void Update()
        {
            var kb = Keyboard.current;
            if (kb == null) return;

            if (kb.kKey.wasPressedThisFrame)
            {
                foreach (var e in _enemies) e.Kill();
                _status = "enemies killed [K] -> firing stops (in-flight projectiles still land)";
            }
            if (kb.rKey.wasPressedThisFrame)
            {
                _fort.Initialize(_fortHp);
                foreach (var e in _enemies) if (e != null) Destroy(e.gameObject);
                _enemies.Clear();
                SpawnAllEnemies();
                _status = "reset [R]";
            }
        }

        private void SpawnAllEnemies()
        {
            foreach (var pos in MuzzlePositions)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = "Demo_Enemy";
                go.transform.position = pos;
                go.transform.localScale = Vector3.one * 0.8f;
                DestroyCollider(go);
                go.GetComponent<MeshRenderer>().sharedMaterial = _enemyMat;
                var enemy = go.AddComponent<Enemy>();
                enemy.Init(_enemyType, _factory, _fort);
                _enemies.Add(enemy);
            }
        }

        private Projectile CreateProjectile()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "Demo_Projectile";
            go.transform.localScale = Vector3.one * 0.45f;
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
            int alive = 0;
            int shots = 0;
            foreach (var e in _enemies) { if (e != null && e.IsAlive) alive++; if (e != null) shots += e.ShotsFired; }

            GUI.contentColor = Color.white;
            GUI.Label(new Rect(12, 10, 900, 120),
                "=== DEMO (Unit 9-11 / 使い捨て) ===\n" +
                $"Fort HP: {_fort.CurrentHp}/{_fort.MaxHp}   敵: {alive}/{_enemies.Count} 生存   発射累計: {shots}\n" +
                $"敵は fireInterval={(_enemyType != null ? _enemyType.FireInterval : 0f)}s ごとに発射 → プール弾が砦へ → TakeDamage\n" +
                "[K] 全敵を撃破（発射停止）   [R] リセット\n" +
                $"status: {_status}");
        }
    }
}
