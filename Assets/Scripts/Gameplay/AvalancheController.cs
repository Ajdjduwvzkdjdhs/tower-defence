using UnityEngine;
using KakigoriTD.Core;
using KakigoriTD.Input;

namespace KakigoriTD.Gameplay
{
    /// <summary>
    /// 削り入力（<see cref="IInputSource"/>）と氷ストック（<see cref="IceStock"/>）を読み、
    /// アクティブ谷に雪崩を適用する MonoBehaviour アダプタ（ADR-6 / ADR-8 / ADR-11）。
    /// タイミングの純ロジックは <see cref="AvalancheClock"/>（Core・EditMode 検証済み）に委譲する。
    ///
    /// 毎フレーム: アクティブ谷を Space 長押し中かつ氷があれば、氷を <c>iceDrainPerSec</c> で消費しつつ
    /// クロックを進め、消すべき行数だけ <see cref="ChannelController.ApplyAvalanche"/> を呼ぶ。
    /// 削り停止・谷変更・氷枯渇で進行はリセットされる（深さ制御）。
    /// </summary>
    public class AvalancheController : MonoBehaviour
    {
        [Header("調整値（spec §主要パラメータ）")]
        [SerializeField] private float _avalancheDelay = 0f;   // 削り開始から判定開始まで（0＝即時）
        [SerializeField] private float _killInterval = 0.4f;   // 1 行/秒
        [SerializeField] private float _iceDrainPerSec = 1.0f; // 削り中の氷消費レート

        private IInputSource _input;
        private IceStock _ice;
        private ChannelController[] _channels;
        private AvalancheClock _clock;
        private int _lastChannel = -1;

        /// <summary>現在照準中の谷（-1＝未選択）。UI ハイライト用。</summary>
        public int ActiveChannel => _input?.ActiveChannel ?? -1;

        /// <summary>このフレームに実際に削れているか（氷あり・Space 長押し・谷選択中）。UI/エフェクト用。</summary>
        public bool IsAvalanching { get; private set; }

        /// <summary>氷ストック（UI バー購読用）。</summary>
        public IceStock Ice => _ice;

        /// <summary>
        /// Composition Root（or デモ）から依存を注入する。
        /// </summary>
        /// <param name="input">谷選択・削りの入力源。</param>
        /// <param name="ice">有限の氷ストック。</param>
        /// <param name="channels">谷（インデックス＝ChannelId 0:N/1:W/2:S/3:E）。</param>
        public void Configure(IInputSource input, IceStock ice, ChannelController[] channels)
        {
            _input = input;
            _ice = ice;
            _channels = channels;
            _clock = new AvalancheClock(_avalancheDelay, _killInterval);
            _lastChannel = -1;
        }

        private void Update()
        {
            if (_input == null || _ice == null || _channels == null) return;

            int ch = _input.ActiveChannel;

            // 谷を変えたら進行リセット（深さ制御：別の谷では delay からやり直し）。
            if (ch != _lastChannel)
            {
                _clock.Reset();
                _lastChannel = ch;
            }

            bool wantShave = _input.IsShaving && ch >= 0 && ch < _channels.Length && _channels[ch] != null;

            // 氷を消費できたフレームだけ「削れている」。枯渇していれば削れない（0で不可）。
            bool activeShave = false;
            if (wantShave && !_ice.IsEmpty)
            {
                if (_ice.TryDrain(_iceDrainPerSec * Time.deltaTime))
                    activeShave = true;
            }
            IsAvalanching = activeShave;

            int rows = _clock.Tick(Time.deltaTime, activeShave);
            for (int i = 0; i < rows; i++)
                _channels[ch].ApplyAvalanche();
        }
    }
}
