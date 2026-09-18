using UnityEngine;
using KakigoriTD.Core;
using KakigoriTD.Data;
using Random = UnityEngine.Random;

namespace KakigoriTD.Gameplay
{
    /// <summary>
    /// 単一ステージの敵湧きを管理する MonoBehaviour アダプタ（ADR-8 / ADR-30 / ADR-36）。旧 <c>WaveManager</c> の置き換え。
    /// 湧き方式は <see cref="StageConfigSO.SpawnMode"/> で切り替える:
    ///  - <see cref="SpawnMode.Population"/>（既定）… 盤面の生存数を <see cref="StageConfigSO.FieldPopulationAt"/>（進捗で補間）に
    ///    保つ。倒した分だけ <see cref="StageConfigSO.RefillRatePerSecond"/> を上限に補充する（<see cref="PopulationSpawnClock"/>）。
    ///    倒す速さがそのままクリアタイムになるための方式。
    ///  - <see cref="SpawnMode.FixedRate"/> … <see cref="StageConfigSO.SpawnRatePerSecond"/>（体/秒）の一定間隔で 1 体湧かせる
    ///    （<see cref="SpawnClock"/>）。クリアタイムの下限が 目標撃破数÷湧きレート に固定される旧方式。
    /// タイミングの純ロジックはどちらも Core（EditMode 検証）。
    ///
    /// 種別は <see cref="SpawnSelector"/> で抽選する。盾兵が選ばれたときは爆発兵とのペアで湧く。出現率は**撃破数の進捗 0..1** に対する
    /// 2 値補間を <see cref="StageConfigSO"/> 側で評価して得る（Core を UnityEngine 非依存に保つため、
    /// 評価はこの層で行い、Core には float だけを渡す）。
    ///
    /// 終了判定・結果表示は <c>BattleState</c>＋<c>GameManager(IGameContext)</c> が担う。
    /// 本クラスは「開始されたら湧かせ、止められたら止める」だけに徹する。
    /// </summary>
    public class SpawnDirector : MonoBehaviour
    {
        private EnemyField _field;
        private KillProgress _progress;
        private SpawnClock _clock;                     // FixedRate 用
        private PopulationSpawnClock _populationClock; // Population 用
        private StageConfigSO _config;
        private bool _running;
        private int _spawned;

        /// <summary>稼働中（湧き中）か。</summary>
        public bool IsRunning => _running;

        /// <summary>このプレイで湧かせた累計体数（可観測性）。</summary>
        public int Spawned => _spawned;

        /// <summary>敵フィールドと進捗を注入する（Composition Root から）。</summary>
        public void Configure(EnemyField field, KillProgress progress)
        {
            _field = field;
            _progress = progress;
        }

        /// <summary>湧きを開始する（<c>IGameContext.BeginBattle</c> から呼ばれる）。</summary>
        public void StartBattle(StageConfigSO config)
        {
            _config = config;
            float rate = config != null ? config.SpawnRatePerSecond : 0.667f;
            // SpawnClock は間隔(秒)で駆動するため、Inspector 上のレート(体/秒)をここで変換する。
            _clock = new SpawnClock(rate > 0f ? 1f / rate : float.MaxValue);
            _populationClock = new PopulationSpawnClock(config != null ? config.RefillRatePerSecond : 6f);
            _running = true;
            _spawned = 0;
        }

        /// <summary>湧きを停止する（決着時）。</summary>
        public void Stop() => _running = false;

        private void Update()
        {
            if (!_running || _field == null || _config == null) return;

            int n = _config.SpawnMode == SpawnMode.Population
                ? _populationClock.Tick(Time.deltaTime, _field.EnemyCount, _config.FieldPopulationAt(Progress01))
                : _clock.Tick(Time.deltaTime);
            for (int i = 0; i < n; i++) SpawnOne();
        }

        private float Progress01 => _progress != null ? _progress.Progress01 : 0f;

        private void SpawnOne()
        {
            float progress01 = Progress01;

            var kind = SpawnSelector.Select(
                _config.BomberChanceAt(progress01),
                _config.ShieldChanceAt(progress01),
                Random.value);

            // 盾兵は単独では湧かせない: 必ず「爆発兵（前）＋盾兵（後ろ）」のペアで同じ列に置く（ADR-30）。
            // 盾兵の正攻法は前の爆発兵を倒して巻き込むことなので、盾兵が爆発兵より前に立つ配置を作らない。
            Enemy spawned = kind switch
            {
                SpawnKind.Bomber => _field.SpawnBomber(_config.BomberType),
                SpawnKind.Shield => _field.SpawnBomberShieldPair(_config.BomberType, _config.ShieldType, out _),
                _ => SpawnNormal(),
            };

            if (spawned != null) _spawned += kind == SpawnKind.Shield ? 2 : 1;
        }

        private Enemy SpawnNormal()
        {
            var type = PickEnemyType();
            return type != null ? _field.SpawnEnemy(type) : null;
        }

        private EnemyTypeSO PickEnemyType()
        {
            var pool = _config.EnemyPool;
            if (pool == null || pool.Count == 0) return null;
            return pool[Random.Range(0, pool.Count)];
        }
    }
}
