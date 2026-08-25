using System;
using UnityEngine;
using KakigoriTD.Factory;

namespace KakigoriTD.Gameplay
{
    /// <summary>
    /// 雪崩の見た目を担う MonoBehaviour アダプタ。<see cref="AvalancheController"/> が公開する
    /// 「このフレームに削れている谷」（<see cref="AvalancheController.IsAvalanching"/> /
    /// <see cref="AvalancheController.ActiveChannel"/>）を読み、その谷の山頂寄りから
    /// <see cref="Snowball"/> を連続射出して麓へ滑らせる（ADR-20 / ADR-21）。
    ///
    /// ADR-27 以降は撃破の駆動も担う: 射出する各雪玉に「その谷の先頭行の実位置」と撃破コールバック
    /// （<see cref="ChannelController.ApplyAvalanche"/>）を渡し、雪玉が先頭行に届いた瞬間に 1 行だけ消す。
    /// これにより「雪が敵に届いた瞬間に敵が消える」を見た目と厳密に同期させる（別フロントの模擬を廃止）。
    /// 雪玉はプールで使い回す（ADR-4）。
    /// </summary>
    public class AvalancheVfx : MonoBehaviour
    {
        [Header("ストリーム")]
        [SerializeField] private float _spawnRate = 24f;      // 1 秒あたりの雪玉射出数
        [SerializeField] private float _startRadius = 1.2f;   // 射出開始の中心からの距離（砦の外側）
        [SerializeField] private float _endRadius = 10.0f;    // 滑走終了。隊列（先頭 8.0〜最奥 9.65）を覆う必要がある
        [SerializeField] private float _laneHalfWidth = 1.0f; // 谷幅方向のばらつき（±）
        [SerializeField] private float _startJitter = 0.4f;   // 射出半径の前後ゆらぎ

        [Header("撃破クールダウン（ADR-27）")]
        // 先頭行は即時・後続行はこの間隔で消える。雪玉の連続到達でも後ろの行が一気に消えないようにするレート制御。
        [SerializeField] private float _killInterval = 0.2f;

        private AvalancheController _avalanche;
        private ChannelController[] _channels;
        private Func<float, float, float> _heightAt;
        private ObjectPool<Snowball> _pool;
        private float _spawnAccum;

        /// <summary>
        /// Composition Root から依存を注入する。
        /// </summary>
        /// <param name="avalanche">削り状態（アクティブ谷・削り中フラグ）の供給元。</param>
        /// <param name="channels">谷（外向き方向の取得元）。</param>
        /// <param name="heightAt">(x,z)→world Y の斜面サンプラ。</param>
        /// <param name="createSnowball">雪玉プールのファクトリ。</param>
        public void Configure(AvalancheController avalanche, ChannelController[] channels,
            Func<float, float, float> heightAt, Func<Snowball> createSnowball)
        {
            _avalanche = avalanche;
            _channels = channels;
            _heightAt = heightAt;
            _pool = new ObjectPool<Snowball>(createSnowball, prewarm: 24);
            _spawnAccum = 0f;
        }

        private void Update()
        {
            if (_avalanche == null || _channels == null) return;

            // 削れていないフレームは射出しない（次に削り始めたとき溜まった分が噴き出さないよう 0 に戻す）。
            if (!_avalanche.IsAvalanching)
            {
                _spawnAccum = 0f;
                return;
            }

            int ch = _avalanche.ActiveChannel;
            if (ch < 0 || ch >= _channels.Length || _channels[ch] == null) return;

            Vector3 outward = _channels[ch].OutwardDirection;

            // 山頂から連続射出し、雪玉が斜面を流れ落ちて麓に到達するまでを見せる（prewarm はしない）。
            // 撃破は各雪玉が先頭行の敵の実位置に届いた瞬間に駆動する（ADR-27）。
            _spawnAccum += _spawnRate * Time.deltaTime;
            while (_spawnAccum >= 1f)
            {
                _spawnAccum -= 1f;
                Spawn(_channels[ch], outward, _startRadius + UnityEngine.Random.Range(0f, _startJitter));
            }
        }

        private void Spawn(ChannelController channel, Vector3 outward, float startRadius)
        {
            float lateral = UnityEngine.Random.Range(-_laneHalfWidth, _laneHalfWidth);
            // channel をキャプチャし、雪玉が先頭行の実位置に届いたら 1 行だけ撃破（ADR-27）。谷が空なら距離 +∞ で発火せず。
            // 撃破はクールダウン付き（TryApplyAvalanche）：先頭行は即時・後続行は _killInterval 間隔で消える。
            _pool.Get().Launch(outward, startRadius, lateral, _endRadius, _heightAt, _pool.Release,
                () => channel.FrontRowDistance,
                () => channel.TryApplyAvalanche(_killInterval));
        }
    }
}
