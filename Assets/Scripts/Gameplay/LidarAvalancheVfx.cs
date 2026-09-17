using System;
using UnityEngine;
using KakigoriTD.Core;
using KakigoriTD.Data;
using KakigoriTD.Factory;
using KakigoriTD.Input;

namespace KakigoriTD.Gameplay
{
    /// <summary>
    /// LiDAR 位置入力（連続座標）版の雪崩。谷を持たず、検知点 P（<see cref="IPositionInputSource.WorldPoint"/>）
    /// の**真上に snowEffectPrefab（ParticleSystem 付き）を生成し最初から再生**する（雪玉転がし廃止）。
    /// 粒子速度の XZ 成分は山中心→検知点の外向きベクトルへ整え（AvalancheSnowEffect が向き設定）、
    /// 山から外側へ流れる（phase-preview-feedback.md §3.4 の後継・物理不使用）。
    /// 演出はプール（<see cref="AvalancheSnowEffect"/>、ADR-4 踏襲）、撃破は移動中の位置で半径判定し 1 個 1 撃。
    ///
    /// d（＝検知点の中心距離[mm]）に連動して:
    ///  - **撃破クールダウン** <see cref="LidarPreviewConfigSO.CooldownSecFor"/>（中心 0.6s → 外周 0.0s）
    /// 中心付近＝クールダウン長め / 外周＝連射可。
    ///
    /// 氷は無限（ADR-30 で <c>IceStock</c> ごと廃止。システム側のリソース管理を使わない方針）。
    /// </summary>
    public class LidarAvalancheVfx : MonoBehaviour
    {
        private IPositionInputSource _input;
        private EnemyField _field;
        private Func<float, float, float> _heightAt;
        private ObjectPool<AvalancheSnowEffect> _pool;
        private LidarPreviewConfigSO _config;

        private float _spawnAccum;
        private float _lastDMm;
        private bool _wasEmitting;

        /// <summary>放出（＝雪崩）の on/off が変化したフレームに発火（音の雪崩ループ用・エッジ）。</summary>
        public event Action<bool> OnAvalancheChanged;

        /// <summary>雪崩の演出が移動中に通過した現在位置で毎フレーム発火（山を白く塗る演出用）。</summary>
        public event Action<Vector3> OnAvalanchePassed;

        /// <summary>このフレームに放出しているか（デバッグ表示用）。</summary>
        public bool IsEmitting => _wasEmitting;

        public void Configure(IPositionInputSource input, EnemyField field,
            Func<float, float, float> heightAt, Func<AvalancheSnowEffect> createEffect,
            LidarPreviewConfigSO config)
        {
            _input = input;
            _field = field;
            _heightAt = heightAt;
            _config = config;
            _pool = new ObjectPool<AvalancheSnowEffect>(createEffect, prewarm: 8);
            _spawnAccum = 0f;
            _wasEmitting = false;
        }

        private void Update()
        {
            bool emitting = false;

            if (_input != null && _field != null && _config != null && _pool != null
                && _input.HasPoint && _input.IsShaving && !_input.IsLost)
            {
                emitting = true;
                Emit();
            }

            if (!emitting) _spawnAccum = 0f; // 溜まった分が次に噴き出さないよう 0 に戻す

            if (emitting != _wasEmitting)
            {
                _wasEmitting = emitting;
                OnAvalancheChanged?.Invoke(emitting);
            }
        }

        private void Emit()
        {
            Vector3 p = _input.WorldPoint;
            _lastDMm = _input.DistanceMm;

            _spawnAccum += _config.SpawnRate * Time.deltaTime;
            while (_spawnAccum >= 1f)
            {
                _spawnAccum -= 1f;
                // 雪崩の発生位置の真上に 1 個生成し、外向き（XZ の中心→検知点）へ粒子を流す。
                _pool.Get().Spawn(p, _heightAt, _config.EndRadius, TryKill, RaisePassed, _pool.Release);
            }
        }

        // 雪玉が毎フレーム自分の現在位置で呼ぶ。cooldown は「今フレームの検知点の d」（切れていたら直近の d）。
        private bool TryKill(Vector3 worldPos)
        {
            float d = (_input != null && _input.HasPoint) ? _input.DistanceMm : _lastDMm;
            return _field.TryKillNearest(worldPos, _config.KillRadiusUnits, _config.CooldownSecFor(d), Time.time);
        }

        private void RaisePassed(Vector3 pos) => OnAvalanchePassed?.Invoke(pos);
    }
}
