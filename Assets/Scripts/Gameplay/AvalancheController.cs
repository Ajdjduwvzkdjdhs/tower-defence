using UnityEngine;
using KakigoriTD.Core;
using KakigoriTD.Input;

namespace KakigoriTD.Gameplay
{
    /// <summary>
    /// 削り入力（<see cref="IInputSource"/>）と氷ストック（<see cref="IceStock"/>）を読み、
    /// 「このフレームに削れているか」（谷選択中・削り長押し・氷あり）を解決する MonoBehaviour アダプタ
    /// （ADR-6 / ADR-8 / ADR-27）。氷を消費しつつ <see cref="IsAvalanching"/> / <see cref="ActiveChannel"/>
    /// を公開し、雪崩の見た目（<see cref="AvalancheVfx"/>）と音（<see cref="AudioService"/>）を駆動する。
    ///
    /// 撃破そのものは本クラスでは行わない（ADR-27）。撃破の起点は「実際に流れている雪玉の到達」に一本化し、
    /// <see cref="AvalancheVfx"/> が射出する各 <see cref="Snowball"/> が先頭行の敵の実位置に届いた瞬間に
    /// その谷の先頭行を 1 行だけ消す（1 雪玉 = 1 行）。これにより別フロントの模擬・固定タイマー・
    /// killInterval 刻みを廃し、見た目と撃破を同期させ、入力（位置検知）の一瞬のちらつきにも頑健にする
    /// （発射済みの雪玉は入力の切替に関係なく麓まで滑って撃破するため）。
    /// </summary>
    public class AvalancheController : MonoBehaviour
    {
        [Header("調整値（spec §主要パラメータ）")]
        [SerializeField] private float _iceDrainPerSec = 1.0f; // 削り中の氷消費レート

        private IInputSource _input;
        private IceStock _ice;
        private ChannelController[] _channels;
        private bool _wasAvalanching; // OnAvalancheChanged のエッジ検出用（前フレーム値）

        /// <summary>現在照準中の谷（-1＝未選択）。UI ハイライト用。</summary>
        public int ActiveChannel => _input?.ActiveChannel ?? -1;

        /// <summary>このフレームに実際に削れているか（氷あり・削り長押し・谷選択中）。UI/エフェクト用。</summary>
        public bool IsAvalanching { get; private set; }

        /// <summary>
        /// <see cref="IsAvalanching"/> が変化したフレームに発火する（true=削り開始 / false=停止・ADR-26）。
        /// 雪崩ループ音（<see cref="AudioService"/>）の on/off に使う。エッジ検出済みなので購読側は
        /// 毎フレーム発火を気にしなくてよい。純 cosmetic なイベントで撃破ロジックには干渉しない。
        /// </summary>
        public event System.Action<bool> OnAvalancheChanged;

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
        }

        private void Update()
        {
            if (_input == null || _ice == null || _channels == null) return;

            int ch = _input.ActiveChannel;
            bool wantShave = _input.IsShaving && ch >= 0 && ch < _channels.Length && _channels[ch] != null;

            // 氷を消費できたフレームだけ「削れている」。枯渇していれば削れない（0で不可）。
            bool activeShave = false;
            if (wantShave && !_ice.IsEmpty)
            {
                if (_ice.TryDrain(_iceDrainPerSec * Time.deltaTime))
                    activeShave = true;
            }
            IsAvalanching = activeShave;

            // 削り状態が変化したフレームだけ通知（エッジ検出＝雪崩ループ音の on/off・ADR-26）。
            if (activeShave != _wasAvalanching)
            {
                _wasAvalanching = activeShave;
                OnAvalancheChanged?.Invoke(activeShave);
            }
        }
    }
}
