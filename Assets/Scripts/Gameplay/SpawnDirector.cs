using UnityEngine;
using KakigoriTD.Core;
using KakigoriTD.Data;
using Random = UnityEngine.Random;

namespace KakigoriTD.Gameplay
{
    /// <summary>
    /// 単一ステージの敵湧きを管理する MonoBehaviour アダプタ（ADR-8 / ADR-30）。旧 <c>WaveManager</c> の置き換え。
    /// <see cref="StageConfigSO.SpawnRatePerSecond"/>（体/秒）から算出した間隔ごとに 1 体湧かせる。
    /// タイミングの純ロジックは <see cref="SpawnClock"/>（EditMode 検証）。
    ///
    /// 種別は <see cref="SpawnSelector"/> で抽選する。盾兵が選ばれたときは爆発兵とのペアで湧く。出現率は**撃破数の進捗 0..1** に対する
    /// <c>AnimationCurve</c> を <see cref="StageConfigSO"/> 側で評価して得る（Core を UnityEngine 非依存に保つため、
    /// カーブの評価はこの層で行い、Core には float だけを渡す）。
    /// 湧きレート自体はランプさせない（盤面の混雑度は「湧き速度 − 撃破速度」の平衡に任せる）。
    ///
    /// 終了判定・結果表示は <c>BattleState</c>＋<c>GameManager(IGameContext)</c> が担う。
    /// 本クラスは「開始されたら湧かせ、止められたら止める」だけに徹する。
    /// </summary>
    public class SpawnDirector : MonoBehaviour
    {
        private EnemyField _field;
        private KillProgress _progress;
        private SpawnClock _clock;
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
            _running = true;
            _spawned = 0;
        }

        /// <summary>湧きを停止する（決着時）。</summary>
        public void Stop() => _running = false;

        private void Update()
        {
            if (!_running || _field == null || _config == null) return;

            int n = _clock.Tick(Time.deltaTime);
            for (int i = 0; i < n; i++) SpawnOne();
        }

        private void SpawnOne()
        {
            float progress01 = _progress != null ? _progress.Progress01 : 0f;

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
