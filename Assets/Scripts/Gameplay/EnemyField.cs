using System;
using System.Collections.Generic;
using UnityEngine;
using KakigoriTD.Core;
using KakigoriTD.Data;
using KakigoriTD.Factory;

namespace KakigoriTD.Gameplay
{
    /// <summary>
    /// 360度化した単一の敵マネージャ（phase-preview-feedback.md / ADR-29 の 360度拡張）。
    /// 4 つの <c>ChannelController</c> ＋ <c>EnemyMatrix</c> の行 ＋ <c>EnemyRegistry</c> を置き換える。
    ///
    ///  - スポーン: 方位を <see cref="LidarPreviewConfigSO.SpawnAngleStepDeg"/>（既定15°）刻みの離散値から
    ///    ランダムに選び → 外周（<see cref="LidarPreviewConfigSO.EntranceRadius"/>）から
    ///    歩いて入場 → その方位の空き段で静止し発射を始める。
    ///    段は <see cref="EnemyQueueLayout"/> が「列（方位）×段（半径）」に離散化する。1段目が
    ///    <see cref="LidarPreviewConfigSO.RestRadius"/>、以降 <see cref="LidarPreviewConfigSO.QueueSpacing"/> ずつ
    ///    外側。後から来た敵は前の敵の背後に並ぶので**重なって表示されない**。希望方位が満段なら隣の方位へ
    ///    逃がし、全方位が満段ならスポーンを見送る。
    ///  - 撃破: 雪玉の一般化位置到達。<see cref="EnemyKillSelection.SelectFront"/> で「前段（＝より古い）が
    ///    いる敵はまだ前列でない」を適用して 1 体だけ撃破 → その列を <see cref="RepackColumn"/> で繰り上げる
    ///    （後段が 1 段ずつ前進。ADR-20 の lerp 前進）。
    ///
    /// ADR-30（ゲーム性刷新）で追加された振る舞い:
    ///  - <see cref="BombTrooper"/> を倒すと爆発し、半径内の敵を巻き込む。範囲内の爆発兵はさらに誘爆して
    ///    **無制限に連鎖**する（探索は純ロジックの <see cref="ExplosionChain"/>）。
    ///  - <see cref="ShieldTrooper"/> は雪玉では耐久を 1 減らすだけで倒れない（爆発は耐久を無視して一撃）。
    ///  - 必殺ビーム砲は <see cref="KillAllForBeam"/> で盤面を一掃する（連鎖は起こさない）。
    ///  - 撃破は <see cref="OnEnemyKilled"/> に <see cref="KillSource"/> を添えて通知し、
    ///    撃破数・コンボ・ゲージの集計ルールを上位（GameManager）が分ける。
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
        private ObjectPool<BombTrooper> _bomberPool;
        private ObjectPool<ShieldTrooper> _shieldPool;
        private ObjectPool<SnowPuff> _puffPool;
        private ObjectPool<DissolveBurst> _burstPool;

        private float _entranceRadius = 11f;
        private float _restRadius = 8f;
        private float _enemyTiltDeg = 30f;
        private float _entranceSpeed = 6f;
        private float _overlapRadius = 1.1f;
        private int _maxEnemies = 48;
        private float _knockbackDistance = 3f;
        private const float ExplosionKnockbackScale = 2.5f; // 巻き込まれた敵は通常撃破より遠くへ飛ばして「爆発で死んだ」と分かるようにする
        private float _spawnAngleStepDeg = 15f;
        private float _queueSpacing = 1f;
        private int _queueMaxDepth = 3;
        private float _queueAdvanceSpeed = 3.5f;

        // ステージ設定（ADR-30）。GameManager が StageConfigSO から与える。
        private float _explosionRadius = 3f;
        private int _shieldHits = 5;

        private readonly List<Enemy> _enemies = new List<Enemy>();          // 添字 = 古い順
        private readonly List<Vector3> _posScratch = new List<Vector3>();   // SelectFront 用（GC 回避）
        private readonly List<bool> _aliveScratch = new List<bool>();
        private readonly List<Vector3> _slotScratch = new List<Vector3>();  // スロット占有判定用（GC 回避）

        // ParticleContact モード（ADR-32）：1 フレーム内の多数の粒から共有する敵スナップショット（GC 回避）。
        private float _enemyBodyWidth = 1f;
        private float _contactHeight = 2f;
        [SerializeField] private float _absorbFeedbackMinIntervalSec = 0.1f; // 吸収の手応え（パフ・SE）の間引き。粒ごとだと連打になる
        private readonly List<Enemy> _contactScratch = new List<Enemy>();
        private readonly List<Vector3> _contactPos = new List<Vector3>();
        private readonly List<bool> _contactAlive = new List<bool>();
        private int _contactFrame = -1;
        private bool _contactDirty;
        private float _minAliveRadius;
        private float _lastAbsorbFeedbackTime = float.NegativeInfinity;

        // 爆発連鎖用のスクラッチ（毎回の撃破で確保しないよう使い回す）。
        private readonly List<float> _chainX = new List<float>();
        private readonly List<float> _chainZ = new List<float>();
        private readonly List<bool> _chainIsBomber = new List<bool>();
        private readonly List<bool> _chainAlive = new List<bool>();
        private readonly List<int> _chainKilled = new List<int>();
        private readonly List<int> _chainDetonated = new List<int>();
        private readonly List<Enemy> _victimScratch = new List<Enemy>();
        private readonly List<int> _columnScratch = new List<int>();

        private float _lastKillTime = float.NegativeInfinity;

        /// <summary>敵を 1 体撃破したフレームに発火（撃破数・コンボ・ゲージ・音の集計用）。第2引数は撃破位置（コンボポップアップ表示用）。</summary>
        public event Action<KillSource, Vector3> OnEnemyKilled;

        /// <summary>盾兵に雪玉が当たったが倒れなかったフレームに発火（手応えの音用・任意）。</summary>
        public event Action OnShieldHit;

        public int EnemyCount => _enemies.Count;
        public bool IsEmpty => _enemies.Count == 0;
        public bool IsFull => _enemies.Count >= _maxEnemies;

        /// <summary>入場開始の半径（山中心から）。必殺ビーム砲の衝撃波の見た目の半径合わせ用（盤面全員を倒すので入場中も含める）。</summary>
        public float EntranceRadius => _entranceRadius;
        /// <summary>敵が歩みを止めて攻撃を始める静止半径（最前列）。演出の半径合わせ用。</summary>
        public float RestRadius => _restRadius;

        public void Configure(ProjectileFactory projectileFactory, Fort fort,
            Func<Enemy> createEnemy, Func<BombTrooper> createBomber, Func<ShieldTrooper> createShield,
            Func<SnowPuff> createPuff, Func<DissolveBurst> createBurst,
            Func<float, float, float> heightAt, LidarPreviewConfigSO config)
        {
            _projectileFactory = projectileFactory;
            _fort = fort;
            _heightAt = heightAt;

            _enemyPool = new ObjectPool<Enemy>(createEnemy);
            _bomberPool = new ObjectPool<BombTrooper>(createBomber);
            _shieldPool = new ObjectPool<ShieldTrooper>(createShield);
            _puffPool = new ObjectPool<SnowPuff>(createPuff);
            if (createBurst != null) _burstPool = new ObjectPool<DissolveBurst>(createBurst);

            if (config != null)
            {
                _entranceRadius = config.EntranceRadius;
                _restRadius = config.RestRadius;
                _entranceSpeed = config.EntranceSpeed;
                _maxEnemies = config.MaxEnemies;
                _spawnAngleStepDeg = config.SpawnAngleStepDeg;
                _enemyTiltDeg = config.EnemyTiltDeg;
                _queueSpacing = config.QueueSpacing;
                _queueMaxDepth = config.QueueMaxDepth;
                _queueAdvanceSpeed = config.QueueAdvanceSpeed;

                // 前列判定の重なり半径が段間隔より小さいと、後段が前段を飛び越して撃破できてしまう
                // （＝前から剥がれる手触りが壊れる）。現場で段間隔だけ広げても崩れないよう下限を効かせる。
                _overlapRadius = Mathf.Max(config.OverlapRadiusUnits, config.QueueSpacing * 1.05f);

                _enemyBodyWidth = config.EnemyBodyWidthUnits;
                _contactHeight = config.ContactHeightUnits;
            }
        }

        /// <summary>ステージ設定（爆発半径・盾耐久）を与える（ADR-30）。プレイ開始時に GameManager が呼ぶ。</summary>
        public void ConfigureStage(StageConfigSO stage)
        {
            if (stage == null) return;
            _explosionRadius = stage.ExplosionRadius;
            _shieldHits = stage.ShieldHits;
        }

        // 方位スポーンの列数（360 / 角度刻み）。
        private int StepCount
        {
            get
            {
                float stepDeg = _spawnAngleStepDeg > 0f ? _spawnAngleStepDeg : 15f;
                return Mathf.Max(1, Mathf.RoundToInt(360f / stepDeg));
            }
        }

        //--- スポーン --------------------------------------------------------

        public Enemy SpawnEnemy(EnemyTypeSO type)
        {
            if (IsFull) return null;
            var e = _enemyPool.Get();
            e.Init(type, _projectileFactory, _fort);
            if (!PlaceNew(e)) { e.Kill(); _enemyPool.Release(e); return null; } // 全方位満段
            _enemies.Add(e);
            return e;
        }

        /// <summary>爆発兵を湧かせる（ADR-30）。撃破すると爆発半径内を巻き込み、爆発兵は誘爆する。</summary>
        public BombTrooper SpawnBomber(EnemyTypeSO type)
        {
            if (IsFull) return null;
            var b = _bomberPool.Get();
            b.SetExplosionRadius(_explosionRadius);
            b.Init(type, _projectileFactory, _fort);
            if (!PlaceNew(b)) { b.Kill(); _bomberPool.Release(b); return null; }
            _enemies.Add(b);
            return b;
        }

        /// <summary>盾兵を湧かせる（ADR-30）。雪玉は耐久を削るだけで、倒すには爆発が早い。</summary>
        public ShieldTrooper SpawnShield(EnemyTypeSO type)
        {
            if (IsFull) return null;
            var s = _shieldPool.Get();
            s.SetMaxHits(_shieldHits);      // Init → OnInitialized で耐久が張られるので先に渡す
            s.Init(type, _projectileFactory, _fort);
            if (!PlaceNew(s)) { s.Kill(); _shieldPool.Release(s); return null; }
            _enemies.Add(s);
            return s;
        }

        /// <summary>
        /// 爆発兵（前）＋盾兵（後ろ）を**同じ列に前後で**湧かせる（ADR-30）。
        /// 盾兵の正攻法は「前の爆発兵を倒して爆発に巻き込む」なので、盾兵が爆発兵より前に立つ配置を作らない。
        /// 爆発兵を先にリストへ入れる（＝古い）ため、繰り上がり後も爆発兵が内側（前）に残る。
        /// 2 段とも空いている列が無ければ何も湧かせず null を返す。
        /// </summary>
        public BombTrooper SpawnBomberShieldPair(EnemyTypeSO bomberType, EnemyTypeSO shieldType, out ShieldTrooper shield)
        {
            shield = null;
            if (_enemies.Count + 2 > _maxEnemies) return null;

            int steps = StepCount;
            CollectOccupiedSlots();
            if (!EnemyQueueLayout.TryAllocateColumnWithFreeDepth(_slotScratch, steps, UnityEngine.Random.Range(0, steps),
                    _restRadius, _queueSpacing, _queueMaxDepth, depth: 2, out int column))
                return null;

            var bomber = _bomberPool.Get();
            bomber.SetExplosionRadius(_explosionRadius);
            bomber.Init(bomberType, _projectileFactory, _fort);
            PlaceAt(bomber, column, slot: 0);
            _enemies.Add(bomber);

            shield = _shieldPool.Get();
            shield.SetMaxHits(_shieldHits);
            shield.Init(shieldType, _projectileFactory, _fort);
            PlaceAt(shield, column, slot: 1);
            _enemies.Add(shield);

            return bomber;
        }

        // 方位を _spawnAngleStepDeg 刻みの離散値からランダムに選び、その列の空き段を確保して
        // 外周に snap → 空き段へ歩かせる（ADR-22 と同じ入場・発射ゲートの流儀）。
        // 空きが無ければ false（呼び出し側はスポーンを見送る＝敵を重ねない）。
        private bool PlaceNew(Enemy e)
        {
            int steps = StepCount;
            CollectOccupiedSlots();
            if (!EnemyQueueLayout.TryAllocate(_slotScratch, steps, UnityEngine.Random.Range(0, steps),
                    _restRadius, _queueSpacing, _queueMaxDepth, out int column, out int slot))
                return false;

            PlaceAt(e, column, slot); // 砦（中心）を向き、外周から段へ歩く。奥の段は最低 1 段分だけ外から入れる
            return true;
        }

        // 指定の列・段へ入場させる（外周に snap → 段へ歩く）。PlaceNew の配置部分を切り出したもの。
        private void PlaceAt(Enemy e, int column, int slot)
        {
            int steps = StepCount;
            var dir = EnemyQueueLayout.Direction(column, steps);
            // 砦（中心）を向く・cosmetic。さらに前傾させ、真上に近いゲーム用カメラからでも
            // 敵の見分けが付くようにする（敵プレハブの種類を問わず、ここ1箇所で全敵に効く）。
            e.transform.rotation = Quaternion.LookRotation(-dir, Vector3.up) * Quaternion.Euler(-_enemyTiltDeg, 0f, 0f);

            float rest = EnemyQueueLayout.SlotRadius(slot, _restRadius, _queueSpacing);
            float entrance = Mathf.Max(_entranceRadius, rest + _queueSpacing);
            e.SetTarget(OnSlope(dir * entrance), snap: true, _entranceSpeed);
            e.SetTarget(OnSlope(dir * rest), snap: false, _entranceSpeed);
        }

        // 生存敵が確保済みのスロット（＝目標位置）を集める。移動中の現在位置ではなく目標位置が正。
        private void CollectOccupiedSlots()
        {
            _slotScratch.Clear();
            for (int i = 0; i < _enemies.Count; i++)
            {
                var e = _enemies[i];
                if (e == null || !e.IsAlive) continue;
                _slotScratch.Add(e.TargetPosition);
            }
        }

        /// <summary>
        /// 1 つの列を最前列から詰め直す（繰り上がり）。<c>_enemies</c> は古い順なので、
        /// 古い敵が内側の段に入る＝「先にいた方が最前列」（<see cref="EnemyKillSelection"/> の前提）が保たれる。
        /// </summary>
        private void RepackColumn(int column)
        {
            int steps = StepCount;
            int slot = 0;
            for (int i = 0; i < _enemies.Count; i++)
            {
                var e = _enemies[i];
                if (e == null || !e.IsAlive) continue;
                if (EnemyQueueLayout.ColumnOf(e.TargetPosition, steps) != column) continue;

                int s = Mathf.Min(slot, Mathf.Max(0, _queueMaxDepth - 1));
                slot++;
                var target = OnSlope(EnemyQueueLayout.SlotPosition(column, s, steps, _restRadius, _queueSpacing));
                if ((e.TargetPosition - target).sqrMagnitude <= 1e-6f) continue; // 既にその段にいる
                e.SetTarget(target, snap: false, _queueAdvanceSpeed);
            }
        }

        private Vector3 OnSlope(Vector3 p)
        {
            p.y = _heightAt != null ? _heightAt(p.x, p.z) : 0f;
            return p;
        }

        //--- 撃破 -----------------------------------------------------------

        /// <summary>
        /// <paramref name="worldPos"/> の XZ から <paramref name="killRadius"/> 内の「前列」の敵に雪玉を当てる。
        /// 前回撃破から <paramref name="cooldownSec"/> 未満なら何もせず false。
        /// 命中したら <paramref name="now"/> を記録して true（**盾兵が耐えた場合も true**＝雪玉は消費される）。
        /// 前列判定は <see cref="EnemyKillSelection.SelectFront"/>（重なり→age）。
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

            _lastKillTime = now;

            // 盾兵は耐久が残っている限り倒れない（雪玉とクールダウンは消費される）。
            if (_enemies[idx] is ShieldTrooper shield && !shield.TakeSnowballHit())
            {
                _puffPool?.Get().Play(shield.transform.position, _puffPool.Release);
                OnShieldHit?.Invoke();
                return true;
            }

            KillOne(_enemies[idx], KillSource.Snowball);
            return true;
        }

        //--- ParticleContact（ADR-32）------------------------------------------

        /// <summary>
        /// 雪崩の粒 1 個が敵に当たったか（ParticleContact モード）。当たれば敵に 1 ダメージを与え true
        /// （粒は呼び出し側が消す＝吸収）。耐久が尽きれば <see cref="KillOne"/>（爆発兵なら連鎖）。
        /// 接触半径 ＝ 敵の半身 ＋ <paramref name="particleRadius"/>、高さは敵の足元 ± ContactHeightUnits。
        /// 同一フレーム内の多数の粒から呼ばれる前提で、敵のスナップショットはフレームごとに 1 回だけ作る。
        /// </summary>
        public bool TryAbsorbParticle(Vector3 particlePos, float particleRadius)
        {
            if (_enemies.Count == 0) return false;
            PrepareContactFrame();

            float radius = _enemyBodyWidth * 0.5f + particleRadius;

            // 早期棄却：最も内側の生存敵より radius 以上内側の粒は誰にも触れない（頂上付近の粒を毎回走査しない）。
            float inner = _minAliveRadius - radius;
            if (inner > 0f && particlePos.x * particlePos.x + particlePos.z * particlePos.z < inner * inner) return false;

            int idx = AvalancheContact.FindHit(_contactPos, _contactAlive, particlePos, radius, _contactHeight);
            if (idx < 0) return false;

            var e = _contactScratch[idx];
            if (!e.TakeParticleHit())
            {
                // 耐えた（盾兵など）。手応えは間引いて出す。
                if (Time.time - _lastAbsorbFeedbackTime >= _absorbFeedbackMinIntervalSec)
                {
                    _lastAbsorbFeedbackTime = Time.time;
                    _puffPool?.Get().Play(e.transform.position, _puffPool.Release);
                    OnShieldHit?.Invoke();
                }
                return true;
            }

            _contactAlive[idx] = false;          // 同フレームの別の粒が同じ敵に当たらないように
            KillOne(e, KillSource.Snowball);
            _contactDirty = true;                // 爆発連鎖で他の敵も倒れうる → 生存フラグを同期し直す
            return true;
        }

        // フレーム先頭で敵のスナップショットを作る。同フレームで撃破があれば生存フラグだけ同期し直す
        // （_enemies から外れた参照も _contactScratch には残るので添字はフレーム内で安定）。
        private void PrepareContactFrame()
        {
            int frame = Time.frameCount;
            if (_contactFrame == frame && !_contactDirty) return;

            if (_contactFrame != frame)
            {
                _contactScratch.Clear();
                _contactPos.Clear();
                _contactAlive.Clear();
                _minAliveRadius = float.PositiveInfinity;
                for (int i = 0; i < _enemies.Count; i++)
                {
                    var e = _enemies[i];
                    bool alive = e != null && e.IsAlive;
                    Vector3 p = alive ? e.transform.position : Vector3.positiveInfinity;
                    _contactScratch.Add(e);
                    _contactPos.Add(p);
                    _contactAlive.Add(alive);
                    if (alive)
                    {
                        float r = Mathf.Sqrt(p.x * p.x + p.z * p.z);
                        if (r < _minAliveRadius) _minAliveRadius = r;
                    }
                }
                if (float.IsPositiveInfinity(_minAliveRadius)) _minAliveRadius = 0f;
                _contactFrame = frame;
            }
            else
            {
                for (int i = 0; i < _contactScratch.Count; i++)
                    _contactAlive[i] = _contactScratch[i] != null && _contactScratch[i].IsAlive;
            }
            _contactDirty = false;
        }

        /// <summary>
        /// 必殺ビーム砲の一掃（ADR-30）。生存中の敵を全て撃破する。連鎖は起こさない
        /// （ビーム撃破はゲージに算入しないため、連鎖で自己充填されることもない）。
        /// </summary>
        /// <returns>撃破した体数。</returns>
        public int KillAllForBeam()
        {
            int n = 0;
            for (int i = _enemies.Count - 1; i >= 0; i--)
            {
                var e = _enemies[i];
                if (e == null) { _enemies.RemoveAt(i); continue; }
                Vector3 pos = e.transform.position; // 撃破位置を控える（PerformKillでプール返却される前に）
                PerformKill(e);
                _enemies.RemoveAt(i);
                n++;
                OnEnemyKilled?.Invoke(KillSource.Beam, pos);
            }
            _enemies.Clear();
            return n;
        }

        // 1 体撃破する。爆発兵なら連鎖を解決してから、巻き込まれた敵をまとめて撃破する。
        private void KillOne(Enemy e, KillSource source)
        {
            if (e == null) return;

            if (e is BombTrooper bomber)
            {
                KillWithExplosion(bomber, source);
                return;
            }

            int column = EnemyQueueLayout.ColumnOf(e.TargetPosition, StepCount); // 返却前に控える
            Vector3 pos = e.transform.position; // 撃破位置を控える（PerformKillでプール返却される前に）
            PerformKill(e);
            _enemies.Remove(e); // 順序保持（古い順のまま）
            RepackColumn(column); // 空いた段を後段が繰り上がって埋める
            OnEnemyKilled?.Invoke(source, pos);
        }

        /// <summary>
        /// 爆発兵の撃破 → 爆発 → 誘爆連鎖を 1 フレームで解決する。
        /// 探索は純ロジックの <see cref="ExplosionChain"/>（無制限連鎖）。撃破順は爆発の波及順。
        /// </summary>
        private void KillWithExplosion(BombTrooper origin, KillSource originSource)
        {
            int steps = StepCount;
            int originIndex = _enemies.IndexOf(origin);
            if (originIndex < 0) return;

            // 連鎖の探索は「今の盤面のスナップショット」に対して行う（撃破でリストが動く前に確定させる）。
            _chainX.Clear(); _chainZ.Clear(); _chainIsBomber.Clear(); _chainAlive.Clear();
            for (int i = 0; i < _enemies.Count; i++)
            {
                var e = _enemies[i];
                bool ok = e != null;
                _chainX.Add(ok ? e.transform.position.x : float.PositiveInfinity);
                _chainZ.Add(ok ? e.transform.position.z : float.PositiveInfinity);
                _chainIsBomber.Add(ok && e is BombTrooper);
                _chainAlive.Add(ok && e.IsAlive);
            }

            _chainKilled.Clear();
            _chainDetonated.Clear();
            ExplosionChain.Resolve(_chainX, _chainZ, _chainIsBomber, _chainAlive,
                originIndex, origin.ExplosionRadius, _chainKilled, _chainDetonated);

            // 爆発演出。爆発兵プレハブに付いた ExplosiveDeath（見た目だけ。巻き込みの判定は上の
            // ExplosionChain で確定済みなので、演出側の撃破コールバックは使わない）。
            // 撃破で参照が失われる前に、プール返却前のこのタイミングで再生する。
            for (int i = 0; i < _chainDetonated.Count; i++)
            {
                var d = _enemies[_chainDetonated[i]];
                if (d == null) continue;
                var vfx = d.GetComponent<ExplosiveDeath>();
                if (vfx != null) vfx.Explode(origin.ExplosionRadius);
            }

            // 巻き込まれた敵の実体をインデックスが有効なうちに控える。
            _victimScratch.Clear();
            for (int i = 0; i < _chainKilled.Count; i++)
            {
                var v = _enemies[_chainKilled[i]];
                if (v != null) _victimScratch.Add(v);
            }

            // 影響を受けた列を控えてから撤去する（繰り上がりは最後にまとめて 1 回）。
            _columnScratch.Clear();
            AddColumn(EnemyQueueLayout.ColumnOf(origin.TargetPosition, steps));
            for (int i = 0; i < _victimScratch.Count; i++)
                AddColumn(EnemyQueueLayout.ColumnOf(_victimScratch[i].TargetPosition, steps));

            // 起点（雪玉で倒された爆発兵）→ 巻き込まれた敵、の順に撃破を確定する。
            Vector3 blastCenter = origin.transform.position;
            PerformKill(origin);
            _enemies.Remove(origin);
            OnEnemyKilled?.Invoke(originSource, blastCenter);

            for (int i = 0; i < _victimScratch.Count; i++)
            {
                var v = _victimScratch[i];
                Vector3 victimPos = v.transform.position; // 撃破位置を控える（PerformKillでプール返却される前に）
                PerformKill(v, blastCenter);
                _enemies.Remove(v);
                OnEnemyKilled?.Invoke(KillSource.Explosion, victimPos); // 連鎖20体ならコンボも +20（ADR-30）
            }

            for (int i = 0; i < _columnScratch.Count; i++) RepackColumn(_columnScratch[i]);

            // 現場で「巻き込みが起きているか」を Console で確かめられるようにする（爆発は稀なので負荷は無視できる）。
            Debug.Log($"[Explosion] 爆発 {_chainDetonated.Count} 回 / 巻き込み {_victimScratch.Count} 体 (半径 {origin.ExplosionRadius:F1})");
        }

        private void AddColumn(int column)
        {
            for (int i = 0; i < _columnScratch.Count; i++)
                if (_columnScratch[i] == column) return;
            _columnScratch.Add(column);
        }

        // 撃破演出（雪しぶき puff・身代わり蒸発 burst）＋ Kill ＋ プール返却。
        // blastCenter を渡すと「爆心から外へ」通常より遠くに吹き飛ばす（爆発の巻き込みを見分けるため）。
        // 渡さなければ従来どおり山頂から外向きに飛ぶ。
        // プール未設定（EditMode の直接呼び出し）でも安全に動くよう ?. で守る。
        private void PerformKill(Enemy e, Vector3? blastCenter = null)
        {
            _puffPool?.Get().Play(e.transform.position, _puffPool.Release);

            if (_burstPool != null)
            {
                Vector3 origin = blastCenter ?? Vector3.zero;
                Vector3 outward = new Vector3(e.transform.position.x - origin.x, 0f, e.transform.position.z - origin.z);
                outward = outward.sqrMagnitude > 1e-4f ? outward.normalized : Vector3.forward;
                float distance = blastCenter.HasValue ? _knockbackDistance * ExplosionKnockbackScale : _knockbackDistance;
                Vector3 landing = e.transform.position + outward * distance;
                if (_heightAt != null) landing.y = _heightAt(landing.x, landing.z);
                _burstPool.Get().Play(e.transform, landing, _burstPool.Release);
            }

            ReleaseToPool(e);
        }

        // 型に応じたプールへ返す（多態で 1 つのリストに載せているため）。
        private void ReleaseToPool(Enemy e)
        {
            e.Kill();
            switch (e)
            {
                case BombTrooper bomber: _bomberPool?.Release(bomber); break;
                case ShieldTrooper shield: _shieldPool?.Release(shield); break;
                default: _enemyPool?.Release(e); break;
            }
        }

        /// <summary>プレイ開始・終了時に全敵を消してプールへ返す（撃破としては数えない）。</summary>
        public void ClearAll()
        {
            for (int i = _enemies.Count - 1; i >= 0; i--)
            {
                var e = _enemies[i];
                if (e == null) continue;
                ReleaseToPool(e);
            }
            _enemies.Clear();
        }
    }
}
