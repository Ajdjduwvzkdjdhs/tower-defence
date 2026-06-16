using UnityEngine;
using KakigoriTD.Core;
using KakigoriTD.Data;

namespace KakigoriTD.Gameplay
{
    /// <summary>
    /// ウェーブ中の敵湧きを管理する MonoBehaviour アダプタ（ADR-8）。
    /// <see cref="WaveConfigSO.SpawnInterval"/> ごとに <see cref="WaveConfigSO.EnemyPool"/> から 1 体を選び、
    /// 4 谷へ順番（ラウンドロビン）に湧かせる。タイミングの純ロジックは <see cref="SpawnClock"/>（EditMode 検証）。
    ///
    /// ウェーブの時間管理・残敵消去・遷移は <c>WavePlayingState</c>＋<c>GameManager(IGameContext)</c> が担う。
    /// 本クラスは「開始されたら湧かせ、止められたら止める」だけに徹する。
    /// </summary>
    public class WaveManager : MonoBehaviour
    {
        private ChannelController[] _channels;
        private SpawnClock _clock;
        private WaveConfigSO _config;
        private bool _running;
        private int _nextChannel;
        private int _spawnedThisWave;

        /// <summary>稼働中（湧き中）か。</summary>
        public bool IsRunning => _running;

        /// <summary>現在のウェーブで湧かせた累計体数（可観測性）。</summary>
        public int SpawnedThisWave => _spawnedThisWave;

        /// <summary>谷を注入する（Composition Root から）。</summary>
        public void Configure(ChannelController[] channels)
        {
            _channels = channels;
        }

        /// <summary>ウェーブの湧きを開始する（<c>IGameContext.BeginWave</c> から呼ばれる）。</summary>
        public void StartWave(WaveConfigSO config)
        {
            _config = config;
            _clock = new SpawnClock(config != null ? config.SpawnInterval : 1.5f);
            _running = true;
            _nextChannel = 0;
            _spawnedThisWave = 0;
        }

        /// <summary>湧きを停止する（Wave 終了・残敵消去時）。</summary>
        public void StopWave()
        {
            _running = false;
        }

        private void Update()
        {
            if (!_running || _channels == null || _config == null) return;

            int n = _clock.Tick(Time.deltaTime);
            for (int i = 0; i < n; i++) SpawnOne();
        }

        private void SpawnOne()
        {
            // AllySpawnChance > 0 かつ今回の抽選で当たった場合は味方を湧かせる。
            bool tryAlly = _config.AllyType != null && Random.value < _config.AllySpawnChance;
            if (tryAlly)
            {
                SpawnOneAlly();
                return;
            }
            SpawnOneEnemy();
        }

        private void SpawnOneEnemy()
        {
            var type = PickEnemyType();
            if (type == null) return;

            // 4 谷へ順番に配る（全谷に均等に圧力をかける）。満杯の谷は飛ばして次を試す。
            for (int tries = 0; tries < _channels.Length; tries++)
            {
                var channel = _channels[_nextChannel % _channels.Length];
                _nextChannel++;
                if (channel.SpawnEnemy(type) != null)
                {
                    _spawnedThisWave++;
                    return;
                }
            }
        }

        private void SpawnOneAlly()
        {
            for (int tries = 0; tries < _channels.Length; tries++)
            {
                var channel = _channels[_nextChannel % _channels.Length];
                _nextChannel++;
                if (channel.SpawnAlly(_config.AllyType) != null)
                {
                    _spawnedThisWave++;
                    return;
                }
            }
        }

        private EnemyTypeSO PickEnemyType()
        {
            var pool = _config.EnemyPool;
            if (pool == null || pool.Count == 0) return null;
            return pool[Random.Range(0, pool.Count)];
        }
    }
}
