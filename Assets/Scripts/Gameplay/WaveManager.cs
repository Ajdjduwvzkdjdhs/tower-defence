using UnityEngine;
using KakigoriTD.Core;
using KakigoriTD.Data;
using Random = UnityEngine.Random;

namespace KakigoriTD.Gameplay
{
    /// <summary>
    /// ウェーブ中の敵湧きを管理する MonoBehaviour アダプタ（ADR-8）。
    /// <see cref="WaveConfigSO.SpawnInterval"/> ごとに <see cref="WaveConfigSO.EnemyPool"/> から 1 体を選び、
    /// <see cref="EnemyField"/> に方位ランダムで湧かせる。タイミングの純ロジックは <see cref="SpawnClock"/>（EditMode 検証）。
    ///
    /// ウェーブの時間管理・残敵消去・遷移は <c>WavePlayingState</c>＋<c>GameManager(IGameContext)</c> が担う。
    /// 本クラスは「開始されたら湧かせ、止められたら止める」だけに徹する。
    /// </summary>
    public class WaveManager : MonoBehaviour
    {
        private EnemyField _field;
        private SpawnClock _clock;
        private WaveConfigSO _config;
        private bool _running;
        private int _spawnedThisWave;

        /// <summary>稼働中（湧き中）か。</summary>
        public bool IsRunning => _running;

        /// <summary>現在のウェーブで湧かせた累計体数（可観測性）。</summary>
        public int SpawnedThisWave => _spawnedThisWave;

        /// <summary>敵フィールドを注入する（Composition Root から）。</summary>
        public void Configure(EnemyField field)
        {
            _field = field;
        }

        /// <summary>ウェーブの湧きを開始する（<c>IGameContext.BeginWave</c> から呼ばれる）。</summary>
        public void StartWave(WaveConfigSO config)
        {
            _config = config;
            _clock = new SpawnClock(config != null ? config.SpawnInterval : 1.5f);
            _running = true;
            _spawnedThisWave = 0;
        }

        /// <summary>湧きを停止する（Wave 終了・残敵消去時）。</summary>
        public void StopWave()
        {
            _running = false;
        }

        private void Update()
        {
            if (!_running || _field == null || _config == null) return;

            int n = _clock.Tick(Time.deltaTime);
            for (int i = 0; i < n; i++) SpawnOne();
        }

        private void SpawnOne()
        {
            var kind = SpawnSelector.Select(
                _config.AllySpawnChance,
                _config.DisguiseChance,
                _config.AllyType != null,
                _config.DisguiseType != null,
                Random.value);

            Enemy spawned = kind switch
            {
                SpawnKind.Disguised => _field.SpawnDisguised(_config.DisguiseType),
                SpawnKind.Ally => _field.SpawnAlly(_config.AllyType),
                _ => SpawnOneEnemy(),
            };

            if (spawned != null) _spawnedThisWave++;
        }

        private Enemy SpawnOneEnemy()
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
