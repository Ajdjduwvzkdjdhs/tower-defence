using System;
using UnityEngine;
using KakigoriTD.Core;
using KakigoriTD.Data;
using KakigoriTD.Factory;

namespace KakigoriTD.Gameplay
{
    /// <summary>
    /// 1 谷（放射状4方向のうち1本）の制御。<see cref="EnemyMatrix{T}"/> で敵の行列を保持し、
    /// 最奥（麓側）に湧かせ、雪崩で先頭行（山頂側）を消す（spec「撃破解決＝深さ制御」）。
    ///
    /// 純粋な行ロジックは Core の <see cref="EnemyMatrix{T}"/>（テスト済み）。本 MonoBehaviour は
    /// その行を放射状のワールド座標に対応づける薄いアダプタ（ADR-8 / ADR-11）。敵はプールで使い回す（ADR-4）。
    ///
    /// 配置: 砦中心から外向き <see cref="OutwardDirection"/> に行が並ぶ。先頭行(depth0)が砦に最も近く、
    /// 奥(depth大)ほど麓側。各敵の座標は湧き時に固定し、先頭行消去では動かさない
    /// （雪崩の到達点＝撃破フロントが手前→奥へ進む。survivor は静止）。
    /// </summary>
    public class ChannelController : MonoBehaviour
    {
        [Header("配置（砦中心からの外向き）")]
        [SerializeField] private float _frontGap = 2.0f;   // 先頭行(depth0)の砦中心からの距離
        [SerializeField] private float _rowSpacing = 1.1f;  // 行間（奥行き方向）
        [SerializeField] private float _slotSpacing = 0.7f; // 行内の体の間隔（横方向）
        [SerializeField] private int _rowWidth = 3;         // 1行の最大体数（rowWidth）

        private EnemyMatrix<Enemy> _matrix;
        private ObjectPool<Enemy> _pool;
        private ProjectileFactory _projectileFactory;
        private Fort _fort;
        private Vector3 _center;
        private Vector3 _outwardDir;
        private Vector3 _perpDir;

        // 安定座標のための深さ管理（EnemyMatrix の list index は先頭消去でずれるため、別途持つ）。
        private int _deepestDepth = -1;   // 現在の最奥行の深さ（最初の行で 0）
        private int _backRowRemaining = 0; // 現在の最奥行の空きスロット数

        /// <summary>谷ID（0:N / 1:W / 2:S / 3:E）。</summary>
        public int ChannelId { get; private set; }

        /// <summary>砦中心から麓へ向かう単位ベクトル。</summary>
        public Vector3 OutwardDirection => _outwardDir;

        /// <summary>現在の行数。</summary>
        public int RowCount => _matrix?.RowCount ?? 0;

        /// <summary>この谷の総敵数。</summary>
        public int EnemyCount => _matrix?.Count ?? 0;

        /// <summary>この谷に敵が居ないか。</summary>
        public bool IsEmpty => _matrix == null || _matrix.IsEmpty;

        /// <summary>
        /// Composition Root（or デモ）から依存を注入して谷を構成する。
        /// </summary>
        /// <param name="channelId">0:N / 1:W / 2:S / 3:E。外向き方向を決める。</param>
        /// <param name="center">砦中心（行配置の原点）。</param>
        /// <param name="projectileFactory">敵が弾を撃つための Factory。</param>
        /// <param name="fort">敵の攻撃対象。</param>
        /// <param name="createEnemy">敵インスタンスの生成デリゲート（プール用・プレハブ非依存、ADR-4/11）。</param>
        public void Configure(int channelId, Vector3 center, ProjectileFactory projectileFactory, Fort fort, Func<Enemy> createEnemy)
        {
            ChannelId = channelId;
            _center = center;
            _projectileFactory = projectileFactory;
            _fort = fort;
            _outwardDir = DirectionFor(channelId);
            _perpDir = new Vector3(_outwardDir.y, -_outwardDir.x, 0f); // 外向きを画面内で90°回した横方向
            _matrix = new EnemyMatrix<Enemy>(_rowWidth);
            _pool = new ObjectPool<Enemy>(createEnemy);
            _deepestDepth = -1;
            _backRowRemaining = 0;
        }

        /// <summary>0:N(+y) / 1:W(-x) / 2:S(-y) / 3:E(+x)。</summary>
        public static Vector3 DirectionFor(int channelId)
        {
            switch (channelId)
            {
                case 0: return Vector3.up;
                case 1: return Vector3.left;
                case 2: return Vector3.down;
                case 3: return Vector3.right;
                default: return Vector3.up;
            }
        }

        /// <summary>
        /// 敵を最奥（麓側）に1体湧かせる。プールから取得し、Init・配置・行列追加を行う。
        /// </summary>
        public Enemy SpawnEnemy(EnemyTypeSO type)
        {
            // 最奥行が満杯/未作成なら新しい行を 1 段奥に作る（EnemyMatrix.AddToBack と同じ条件で同期）。
            if (_matrix.IsEmpty) { _backRowRemaining = 0; }
            if (_backRowRemaining <= 0)
            {
                _deepestDepth++;
                _backRowRemaining = _rowWidth;
            }
            int depth = _deepestDepth;
            int slot = _rowWidth - _backRowRemaining;
            _backRowRemaining--;

            var enemy = _pool.Get();
            enemy.Init(type, _projectileFactory, _fort);
            enemy.transform.position = PositionFor(depth, slot);
            _matrix.AddToBack(enemy);
            return enemy;
        }

        /// <summary>
        /// 雪崩 1 ヒット = 先頭行（山頂側）を消す。消えた敵は撃破（発射停止）してプールへ返す。
        /// </summary>
        /// <returns>この一撃で消えた敵数。</returns>
        public int ApplyAvalanche()
        {
            if (_matrix.IsEmpty) return 0;
            var killed = _matrix.RemoveFrontRow();
            int n = killed.Count;
            for (int i = 0; i < n; i++)
            {
                var e = killed[i];
                if (e == null) continue;
                e.Kill();
                _pool.Release(e);
            }
            if (_matrix.IsEmpty) _backRowRemaining = 0; // 空になったら次の湧きは新しい行から
            return n;
        }

        /// <summary>Wave 終了時などに残敵を全消去してプールへ返す。</summary>
        public void ClearAll()
        {
            if (_matrix == null) return;
            var all = _matrix.RemoveAll();
            for (int i = 0; i < all.Count; i++)
            {
                var e = all[i];
                if (e == null) continue;
                e.Kill();
                _pool.Release(e);
            }
            _backRowRemaining = 0;
        }

        // 行(depth)・スロット(slot) → ワールド座標。手前(depth0)が砦寄り、奥ほど麓側。
        private Vector3 PositionFor(int depth, int slot)
        {
            float along = _frontGap + depth * _rowSpacing;
            float across = (slot - (_rowWidth - 1) * 0.5f) * _slotSpacing;
            return _center + _outwardDir * along + _perpDir * across;
        }
    }
}
