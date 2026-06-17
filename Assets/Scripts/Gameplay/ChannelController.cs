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
    /// Phase2: 味方NPC 専用プール + 専用行（AddRow）対応。雪崩が味方行に達すると
    /// <see cref="OnAllyCaught"/> を発火し、自走退場は無ペナルティで行除去（ADR-16）。
    /// </summary>
    public class ChannelController : MonoBehaviour
    {
        [Header("配置（砦中心からの外向き）")]
        // 先頭行(index0)は山の一番外側の円(r≈2.2)より外＝麓の茶色い地面に置く。
        // 最奥行(index3)でも画面内: 2.7 + 3*0.55 = 4.35（敵半幅0.25 → y±4.6 < カメラ視野±5）。
        [SerializeField] private float _frontGap = 2.7f;    // 先頭行(index0)の砦中心からの距離（山の外）
        [SerializeField] private float _rowSpacing = 0.55f; // 行間（奥行き方向）
        [SerializeField] private float _slotSpacing = 0.7f;  // 行内の体の間隔（横方向）
        [SerializeField] private int _rowWidth = 3;          // 1行の最大体数（rowWidth）
        [SerializeField] private int _maxRows = 4;           // 画面内に収まる最大行数

        private EnemyMatrix<Enemy> _matrix;
        private ObjectPool<Enemy> _pool;
        private ObjectPool<AllyNPC> _allyPool;
        private ObjectPool<DisguisedEnemy> _disguisedPool;
        private ProjectileFactory _projectileFactory;
        private Fort _fort;
        private Vector3 _center;
        private Vector3 _outwardDir;
        private Vector3 _perpDir;

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

        /// <summary>これ以上湧かせられないか（画面内行数の上限に達した）。</summary>
        public bool IsFull => _matrix != null && _matrix.RowCount >= _maxRows && IsBackRowFull();

        /// <summary>雪崩が味方行に達したとき（巻き込み＝減点トリガー）。</summary>
        public event Action OnAllyCaught;

        /// <summary>
        /// Composition Root から依存を注入して谷を構成する。
        /// </summary>
        public void Configure(int channelId, Vector3 center, ProjectileFactory projectileFactory,
            Fort fort, Func<Enemy> createEnemy, Func<AllyNPC> createAlly, Func<DisguisedEnemy> createDisguised)
        {
            ChannelId = channelId;
            _center = center;
            _projectileFactory = projectileFactory;
            _fort = fort;
            _outwardDir = DirectionFor(channelId);
            _perpDir = new Vector3(_outwardDir.y, -_outwardDir.x, 0f);
            _matrix = new EnemyMatrix<Enemy>(_rowWidth);
            _pool = new ObjectPool<Enemy>(createEnemy);
            _allyPool = new ObjectPool<AllyNPC>(createAlly);
            _disguisedPool = new ObjectPool<DisguisedEnemy>(createDisguised);
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
        /// 敵を最奥（麓側）に1体湧かせる。上限に達していれば null を返す。
        /// </summary>
        public Enemy SpawnEnemy(EnemyTypeSO type)
        {
            if (IsFull) return null;
            var enemy = _pool.Get();
            enemy.Init(type, _projectileFactory, _fort);
            _matrix.AddToBack(enemy);
            RepositionAll();
            return enemy;
        }

        /// <summary>
        /// 味方NPC を専用行（隔離行）として最奥に1体湧かせる。上限に達していれば null を返す。
        /// 退場（<see cref="AllyNPC.OnExpired"/>）を購読し、自走退場時に無ペナルティで行除去する。
        /// </summary>
        public AllyNPC SpawnAlly(EnemyTypeSO allyType)
        {
            if (IsFull) return null;
            var ally = _allyPool.Get();
            ally.InitAlly(allyType);
            ally.OnExpired += OnAllyExpired;
            _matrix.AddRow(ally);
            RepositionAll();
            return ally;
        }

        /// <summary>
        /// 変装敵を専用行（隔離行）として最奥に1体湧かせる。上限に達していれば null を返す。
        /// 自走退場しないため OnExpired は購読しない（ADR-19）。
        /// </summary>
        public DisguisedEnemy SpawnDisguised(EnemyTypeSO type)
        {
            if (IsFull) return null;
            var disguised = _disguisedPool.Get();
            disguised.Init(type, _projectileFactory, _fort);
            _matrix.AddRow(disguised);
            RepositionAll();
            return disguised;
        }

        /// <summary>
        /// 雪崩 1 ヒット = 先頭行（山頂側）を消す。
        /// 行に味方が居た場合は <see cref="OnAllyCaught"/> を発火（巻き込み＝減点）。
        /// </summary>
        public int ApplyAvalanche()
        {
            if (_matrix.IsEmpty) return 0;
            var killed = _matrix.RemoveFrontRow();
            int n = killed.Count;
            for (int i = 0; i < n; i++)
            {
                var e = killed[i];
                if (e == null) continue;
                if (e is AllyNPC ally)
                {
                    // 自走退場ハンドラの二重発火を防ぐ
                    ally.OnExpired -= OnAllyExpired;
                    ally.Kill();
                    _allyPool.Release(ally);
                    OnAllyCaught?.Invoke();
                }
                else if (e is DisguisedEnemy disguised)
                {
                    // 変装敵は AllyNPC ではないので OnAllyCaught を発火しない（無ペナルティ）。
                    disguised.Kill();
                    _disguisedPool.Release(disguised);
                }
                else
                {
                    e.Kill();
                    _pool.Release(e);
                }
            }
            RepositionAll();
            return n;
        }

        /// <summary>Wave 終了時などに残敵・残味方を全消去してプールへ返す。</summary>
        public void ClearAll()
        {
            if (_matrix == null) return;
            var all = _matrix.RemoveAll();
            for (int i = 0; i < all.Count; i++)
            {
                var e = all[i];
                if (e == null) continue;
                if (e is AllyNPC ally)
                {
                    ally.OnExpired -= OnAllyExpired;
                    ally.Kill();
                    _allyPool.Release(ally);
                }
                else if (e is DisguisedEnemy disguised)
                {
                    disguised.Kill();
                    _disguisedPool.Release(disguised);
                }
                else
                {
                    e.Kill();
                    _pool.Release(e);
                }
            }
        }

        // 自走退場ハンドラ: 無ペナルティで行除去→プール返却→繰り上げ。
        private void OnAllyExpired(AllyNPC ally)
        {
            ally.OnExpired -= OnAllyExpired;
            int rowIndex = _matrix.IndexOfRowContaining(ally);
            if (rowIndex >= 0) _matrix.RemoveRowAt(rowIndex);
            _allyPool.Release(ally);
            RepositionAll();
        }

        private bool IsBackRowFull()
        {
            int rc = _matrix.RowCount;
            return rc == 0 || _matrix.RowAt(rc - 1).Count >= _rowWidth;
        }

        private void RepositionAll()
        {
            for (int row = 0; row < _matrix.RowCount; row++)
            {
                var members = _matrix.RowAt(row);
                for (int slot = 0; slot < members.Count; slot++)
                {
                    var e = members[slot];
                    if (e != null) e.transform.position = PositionFor(row, slot);
                }
            }
        }

        private Vector3 PositionFor(int row, int slot)
        {
            float along = _frontGap + row * _rowSpacing;
            float across = (slot - (_rowWidth - 1) * 0.5f) * _slotSpacing;
            return _center + _outwardDir * along + _perpDir * across;
        }
    }
}
