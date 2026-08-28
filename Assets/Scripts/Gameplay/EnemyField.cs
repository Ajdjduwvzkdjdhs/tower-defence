using System;
using System.Collections.Generic;
using UnityEngine;
using KakigoriTD.Data;
using KakigoriTD.Factory;

namespace KakigoriTD.Gameplay
{
    /// <summary>
    /// 360度化した単一の敵マネージャ（phase-preview-feedback.md / ADR-29 の 360度拡張）。
    /// 4 つの <c>ChannelController</c> ＋ <c>EnemyMatrix</c> の行 ＋ <c>EnemyRegistry</c> を置き換える。
    ///
    ///  - スポーン: 方位 0〜360° ランダム → 外周（<see cref="LidarPreviewConfigSO.EntranceRadius"/>）から
    ///    歩いて入場 → 静止半径（<see cref="LidarPreviewConfigSO.RestRadius"/>）で静止し発射を始める。
    ///    同じ静止半径なので密集すると自然に重なる。
    ///  - 撃破: 雪玉の一般化位置到達。<see cref="EnemyKillSelection.SelectFront"/> で「重なっていたら
    ///    先にいた方が最前列」を適用して 1 体だけ撃破（繰り上がりなし＝位置は動かさない）。
    ///  - 味方巻き込みは <see cref="OnAllyCaught"/>、実敵/変装の撃破は <see cref="OnEnemyKilled"/>（音用）。
    ///
    /// 敵はプールで使い回す（ADR-4）。物理不使用。<c>_enemies</c> の添字がそのまま「古い順」
    /// （<see cref="List{T}.Remove"/> は順序を保つ）。
    /// </summary>
    public class EnemyField : MonoBehaviour
    {
        private ProjectileFactory _projectileFactory;
        private Fort _fort;
        private Func<float, float, float> _heightAt;

        private ObjectPool<Enemy> _enemyPool;
        private ObjectPool<AllyNPC> _allyPool;
        private ObjectPool<DisguisedEnemy> _disguisedPool;
        private ObjectPool<SnowPuff> _puffPool;
        private ObjectPool<DissolveBurst> _burstPool;

        private float _entranceRadius = 11f;
        private float _restRadius = 8f;
        private float _entranceSpeed = 6f;
        private float _overlapRadius = 0.7f;
        private int _maxEnemies = 48;
        private float _knockbackDistance = 3f;

        private readonly List<Enemy> _enemies = new List<Enemy>();          // 添字 = 古い順
        private readonly List<Vector3> _posScratch = new List<Vector3>();   // SelectFront 用（GC 回避）
        private readonly List<bool> _aliveScratch = new List<bool>();
        private float _lastKillTime = float.NegativeInfinity;

        /// <summary>実敵/変装を 1 体撃破したフレームに発火（Monster 音）。</summary>
        public event Action OnEnemyKilled;

        /// <summary>味方を巻き込んだフレームに発火（減点＋AllyCaught 音）。</summary>
        public event Action OnAllyCaught;

        public int EnemyCount => _enemies.Count;
        public bool IsEmpty => _enemies.Count == 0;
        public bool IsFull => _enemies.Count >= _maxEnemies;

        public void Configure(ProjectileFactory projectileFactory, Fort fort,
            Func<Enemy> createEnemy, Func<AllyNPC> createAlly, Func<DisguisedEnemy> createDisguised,
            Func<SnowPuff> createPuff, Func<DissolveBurst> createBurst,
            Func<float, float, float> heightAt, LidarPreviewConfigSO config)
        {
            _projectileFactory = projectileFactory;
            _fort = fort;
            _heightAt = heightAt;

            _enemyPool = new ObjectPool<Enemy>(createEnemy);
            _allyPool = new ObjectPool<AllyNPC>(createAlly);
            _disguisedPool = new ObjectPool<DisguisedEnemy>(createDisguised);
            _puffPool = new ObjectPool<SnowPuff>(createPuff);
            if (createBurst != null) _burstPool = new ObjectPool<DissolveBurst>(createBurst);

            if (config != null)
            {
                _entranceRadius = config.EntranceRadius;
                _restRadius = config.RestRadius;
                _entranceSpeed = config.EntranceSpeed;
                _overlapRadius = config.OverlapRadiusUnits;
                _maxEnemies = config.MaxEnemies;
            }
        }

        //--- スポーン --------------------------------------------------------

        public Enemy SpawnEnemy(EnemyTypeSO type)
        {
            if (IsFull) return null;
            var e = _enemyPool.Get();
            e.Init(type, _projectileFactory, _fort);
            PlaceNew(e);
            _enemies.Add(e);
            return e;
        }

        public AllyNPC SpawnAlly(EnemyTypeSO allyType)
        {
            if (IsFull) return null;
            var ally = _allyPool.Get();
            ally.InitAlly(allyType);
            ally.OnExpired += OnAllyExpired;
            PlaceNew(ally);
            _enemies.Add(ally);
            return ally;
        }

        public DisguisedEnemy SpawnDisguised(EnemyTypeSO type)
        {
            if (IsFull) return null;
            var disguised = _disguisedPool.Get();
            disguised.Init(type, _projectileFactory, _fort);
            PlaceNew(disguised);
            _enemies.Add(disguised);
            return disguised;
        }

        // 方位ランダムで外周に snap → 静止半径へ歩かせる（ADR-22 と同じ入場・発射ゲートの流儀）。
        private void PlaceNew(Enemy e)
        {
            float az = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
            var dir = new Vector3(Mathf.Sin(az), 0f, Mathf.Cos(az)); // XZ 単位ベクトル
            e.transform.rotation = Quaternion.LookRotation(-dir, Vector3.up); // 砦（中心）を向く・cosmetic

            e.SetTarget(OnSlope(dir * _entranceRadius), snap: true, _entranceSpeed);
            e.SetTarget(OnSlope(dir * _restRadius), snap: false, _entranceSpeed);
        }

        private Vector3 OnSlope(Vector3 p)
        {
            p.y = _heightAt != null ? _heightAt(p.x, p.z) : 0f;
            return p;
        }

        //--- 撃破 -----------------------------------------------------------

        /// <summary>
        /// <paramref name="worldPos"/> の XZ から <paramref name="killRadius"/> 内の「前列」の敵を 1 体撃破する。
        /// 前回撃破から <paramref name="cooldownSec"/> 未満なら何もせず false。撃破したら <paramref name="now"/> を
        /// 記録して true。前列判定は <see cref="EnemyKillSelection.SelectFront"/>（重なり→age）。
        /// </summary>
        public bool TryKillNearest(Vector3 worldPos, float killRadius, float cooldownSec, float now)
        {
            if (now - _lastKillTime < cooldownSec) return false;
            if (_enemies.Count == 0) return false;

            _posScratch.Clear();
            _aliveScratch.Clear();
            for (int i = 0; i < _enemies.Count; i++)
            {
                var e = _enemies[i];
                _posScratch.Add(e != null ? e.transform.position : Vector3.positiveInfinity);
                _aliveScratch.Add(e != null && e.IsAlive);
            }

            int idx = EnemyKillSelection.SelectFront(_posScratch, _aliveScratch, worldPos, killRadius, _overlapRadius);
            if (idx < 0) return false;

            KillOne(_enemies[idx]);
            _lastKillTime = now;
            return true;
        }

        private void KillOne(Enemy e)
        {
            if (e == null) return;
            bool ally = PerformKill(e);
            _enemies.Remove(e); // 順序保持（古い順のまま）。繰り上がり（位置移動）はしない。
            if (ally) OnAllyCaught?.Invoke();
            else OnEnemyKilled?.Invoke();
        }

        // 撃破演出（雪しぶき puff・身代わり蒸発 burst）＋ Kill ＋ プール返却。ally なら true。
        // プール未設定（EditMode の直接呼び出し）でも安全に動くよう ?. で守る。
        private bool PerformKill(Enemy e)
        {
            _puffPool?.Get().Play(e.transform.position, _puffPool.Release);

            if (_burstPool != null)
            {
                Vector3 outward = new Vector3(e.transform.position.x, 0f, e.transform.position.z);
                outward = outward.sqrMagnitude > 1e-4f ? outward.normalized : Vector3.forward;
                Vector3 landing = e.transform.position + outward * _knockbackDistance;
                if (_heightAt != null) landing.y = _heightAt(landing.x, landing.z);
                _burstPool.Get().Play(e.transform, landing, _burstPool.Release);
            }

            if (e is AllyNPC ally)
            {
                ally.OnExpired -= OnAllyExpired;
                ally.Kill();
                _allyPool?.Release(ally);
                return true;
            }
            if (e is DisguisedEnemy disguised)
            {
                disguised.Kill();
                _disguisedPool?.Release(disguised);
                return false;
            }
            e.Kill();
            _enemyPool?.Release(e);
            return false;
        }

        // 味方の自走退場: 無ペナルティで除去（ADR-16）。
        private void OnAllyExpired(AllyNPC ally)
        {
            ally.OnExpired -= OnAllyExpired;
            _enemies.Remove(ally);
            _allyPool?.Release(ally);
        }

        /// <summary>Wave 終了・リトライ時に全敵を消してプールへ返す。</summary>
        public void ClearAll()
        {
            for (int i = _enemies.Count - 1; i >= 0; i--)
            {
                var e = _enemies[i];
                if (e == null) continue;
                if (e is AllyNPC ally)
                {
                    ally.OnExpired -= OnAllyExpired;
                    ally.Kill();
                    _allyPool?.Release(ally);
                }
                else if (e is DisguisedEnemy disguised)
                {
                    disguised.Kill();
                    _disguisedPool?.Release(disguised);
                }
                else
                {
                    e.Kill();
                    _enemyPool?.Release(e);
                }
            }
            _enemies.Clear();
        }
    }
}
