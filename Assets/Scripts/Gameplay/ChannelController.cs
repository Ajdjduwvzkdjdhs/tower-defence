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
        // Phase4.5: 隊列を山の麓（斜面基部）に静止させる。先頭行(index0)=山頂寄り(up-slope)、後続は麓の平地側。
        // 先頭 r=5.0(y≈0.39) 〜 最奥 r=5.0+3*0.55=6.65(y≈0)。俯瞰カメラ(y=20/fov46)の N谷可視半径≈8.0 内。
        // 斜面高さ実測: r=5→0.39 / r=6→0.06 / r=6.5→0.01 / r≥7→0（斜面が平地に着く＝麓は r≈6.5）。
        [SerializeField] private float _frontGap = 5.0f;    // 先頭行(index0)の砦中心からの距離（＝麓・斜面基部）
        [SerializeField] private float _rowSpacing = 0.55f; // 行間（奥行き方向）
        [SerializeField] private float _slotSpacing = 0.7f;  // 行内の体の間隔（横方向）
        [SerializeField] private int _rowWidth = 3;          // 1行の最大体数（rowWidth）
        [SerializeField] private int _maxRows = 4;           // 画面内に収まる最大行数
        [SerializeField] private float _advanceSpeed = 3.5f;  // 繰り上がり前進速度（lerp、ADR-20）
        [SerializeField] private float _entranceRadius = 10f; // 入場開始の絶対半径＝カメラ縁の遠方ライン（案B・Phase4.5/ADR-22）
        [SerializeField] private float _entranceSpeed = 6f;   // 入場の歩行速度（繰り上がり _advanceSpeed とは独立・Phase4.5）

        private EnemyMatrix<Enemy> _matrix;
        private ObjectPool<Enemy> _pool;
        private ObjectPool<AllyNPC> _allyPool;
        private ObjectPool<DisguisedEnemy> _disguisedPool;
        private ObjectPool<SnowPuff> _puffPool;
        private ProjectileFactory _projectileFactory;
        private Fort _fort;
        private Vector3 _center;
        private Vector3 _outwardDir;
        private Vector3 _perpDir;
        private Func<float, float, float> _heightAt; // (x,z)→world Y（斜面サンプラ・ADR-21）

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
            Fort fort, Func<Enemy> createEnemy, Func<AllyNPC> createAlly, Func<DisguisedEnemy> createDisguised,
            Func<SnowPuff> createPuff, Func<float, float, float> heightAt = null)
        {
            ChannelId = channelId;
            _center = center;
            _projectileFactory = projectileFactory;
            _fort = fort;
            _heightAt = heightAt;
            _outwardDir = DirectionFor(channelId);
            // XZ 平面での外向きに直交する水平ベクトル（ADR-21：空間基底を XY→XZ）
            _perpDir = new Vector3(_outwardDir.z, 0f, -_outwardDir.x);
            _matrix = new EnemyMatrix<Enemy>(_rowWidth);
            _pool = new ObjectPool<Enemy>(createEnemy);
            _allyPool = new ObjectPool<AllyNPC>(createAlly);
            _disguisedPool = new ObjectPool<DisguisedEnemy>(createDisguised);
            _puffPool = new ObjectPool<SnowPuff>(createPuff);
        }

        /// <summary>放射状4方向（XZ 平面・ADR-21）。0:N(+z) / 1:W(-x) / 2:S(-z) / 3:E(+x)。</summary>
        public static Vector3 DirectionFor(int channelId)
        {
            switch (channelId)
            {
                case 0: return new Vector3(0f, 0f, 1f);  // N = +z
                case 1: return new Vector3(-1f, 0f, 0f); // W = -x
                case 2: return new Vector3(0f, 0f, -1f); // S = -z
                case 3: return new Vector3(1f, 0f, 0f);  // E = +x
                default: return new Vector3(0f, 0f, 1f);
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
            RepositionAll(enemy); // 新規湧きはスナップ、既存はlerp
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
            RepositionAll(ally); // 新規湧きはスナップ
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
            RepositionAll(disguised); // 新規湧きはスナップ
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

                // Kill / Release より前に位置を取得して雪しぶきを発生（ADR-20）
                _puffPool?.Get().Play(e.transform.position, _puffPool.Release);

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

        // 新規湧き(justSpawned)は入場位置（麓側の外・下り斜面）へスナップしてから隊列スロットへ lerp（Option A・ADR-21/U5）。
        // 既存の敵は繰り上がりを lerp（ADR-20）。
        private void RepositionAll(Enemy justSpawned = null)
        {
            for (int row = 0; row < _matrix.RowCount; row++)
            {
                var members = _matrix.RowAt(row);
                for (int slot = 0; slot < members.Count; slot++)
                {
                    var e = members[slot];
                    if (e == null) continue;
                    if (e == justSpawned)
                    {
                        // 入場（案B・ADR-22）: 谷方向の絶対半径 _entranceRadius（カメラ縁の遠方ライン）へ snap し、
                        // スロットへ _entranceSpeed で歩かせる（cosmetic reposition。gameplay locomotion ではない＝ADR-18 整合）。
                        e.SetTarget(EntrancePositionFor(slot), snap: true, _entranceSpeed);
                        e.SetTarget(PositionFor(row, slot), snap: false, _entranceSpeed);
                    }
                    else
                    {
                        e.SetTarget(PositionFor(row, slot), snap: false, _advanceSpeed);
                    }
                }
            }
        }

        private Vector3 PositionFor(int row, int slot)
        {
            float along = _frontGap + row * _rowSpacing;
            return PointAt(along, slot);
        }

        // 入場開始点（案B・ADR-22）: スロットの across を保ったまま絶対半径 _entranceRadius の遠方（カメラ縁）に置く。
        private Vector3 EntrancePositionFor(int slot)
        {
            return PointAt(_entranceRadius, slot);
        }

        private Vector3 PointAt(float along, int slot)
        {
            float across = (slot - (_rowWidth - 1) * 0.5f) * _slotSpacing;
            Vector3 p = _center + _outwardDir * along + _perpDir * across; // XZ 平面（y=0）
            p.y = _heightAt != null ? _heightAt(p.x, p.z) : 0f;            // 斜面に乗せる（ADR-21）
            return p;
        }
    }
}
