using UnityEngine;
using KakigoriTD.Core;
using KakigoriTD.Input;

namespace KakigoriTD.Gameplay
{
    /// <summary>
    /// 削り入力（<see cref="IInputSource"/>）と氷ストック（<see cref="IceStock"/>）を読み、
    /// アクティブ谷に雪崩を適用する MonoBehaviour アダプタ（ADR-6 / ADR-8 / ADR-11）。
    /// 到達後の刻み（1 行/killInterval）の純ロジックは <see cref="AvalancheClock"/>（Core・EditMode 検証済み）に委譲する。
    ///
    /// 撃破の起点は「雪崩フロントの到達位置」（ADR-25）。削り中、山頂から前進する雪崩フロント
    /// （<see cref="AvalancheVfx"/> の雪玉と同じ運動でモデル化）が先頭行の敵の実位置に届いて初めて
    /// 撃破を開始し、以降は killInterval ごとに先頭行を 1 行消す。届く前・削り停止・谷変更・氷枯渇では
    /// フロントと刻みがリセットされる（深さ制御＝どこまで押し続けたか）。
    /// </summary>
    public class AvalancheController : MonoBehaviour
    {
        [Header("調整値（spec §主要パラメータ）")]
        [SerializeField] private float _killInterval = 0.4f;   // 到達後、1 行を消す間隔
        [SerializeField] private float _iceDrainPerSec = 1.0f; // 削り中の氷消費レート

        [Header("雪崩フロント（見た目の雪玉に同期・撃破の到達判定に使う）")]
        // Snowball / AvalancheVfx の滑走パラメータと一致させること（ズレると撃破と雪の到達がずれる）。
        [SerializeField] private float _frontStartRadius = 1.2f; // 山頂寄りの射出開始（AvalancheVfx._startRadius）
        [SerializeField] private float _frontStartSpeed = 2.5f;  // Snowball._startSpeed
        [SerializeField] private float _frontAccel = 6.0f;       // Snowball._accel
        [SerializeField] private float _frontMaxSpeed = 8.0f;    // Snowball._maxSpeed
        [SerializeField] private float _frontMaxRadius = 7.0f;   // 麓の外側でクランプ（無敵前進を防ぐ）

        private IInputSource _input;
        private IceStock _ice;
        private ChannelController[] _channels;
        private AvalancheClock _clock;
        private int _lastChannel = -1;

        // 雪崩フロントの状態（削りセッション内で前進、停止・谷変更でリセット）。
        private float _frontRadius;
        private float _frontSpeed;
        private bool _frontArrived; // フロントが先頭行に到達済み（到達後は killInterval 刻み）

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
            _clock = new AvalancheClock(0f, _killInterval); // 到達後は即時（delay=0）→ 以降 killInterval 刻み
            _lastChannel = -1;
            ResetFront();
        }

        private void Update()
        {
            if (_input == null || _ice == null || _channels == null) return;

            int ch = _input.ActiveChannel;

            // 谷を変えたら進行リセット（深さ制御：別の谷ではフロントを山頂からやり直し）。
            if (ch != _lastChannel)
            {
                _clock.Reset();
                ResetFront();
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

            // 削っていないフレームはフロントも刻みもリセット（次の削りは山頂から・深さ制御）。
            if (!activeShave)
            {
                _clock.Reset();
                ResetFront();
                return;
            }

            // 雪崩フロントが先頭行の敵に届くまでは撃破しない（位置ベースの到達判定＝見た目と一致）。
            if (!_frontArrived)
            {
                _frontSpeed = Mathf.Min(_frontSpeed + _frontAccel * Time.deltaTime, _frontMaxSpeed);
                _frontRadius = Mathf.Min(_frontRadius + _frontSpeed * Time.deltaTime, _frontMaxRadius);
                if (_frontRadius < _channels[ch].FrontRowDistance)
                    return; // まだ雪崩が先頭行に届いていない
                _frontArrived = true; // 到達＝このフレームから撃破開始
            }

            // 到達後は killInterval ごとに先頭行を 1 行ずつ消す（clock は delay=0）。
            int rows = _clock.Tick(Time.deltaTime, true);
            for (int i = 0; i < rows; i++)
                _channels[ch].ApplyAvalanche();
        }

        // 雪崩フロントを山頂寄りの初期状態へ戻す（削り開始し直し＝深さ制御）。
        private void ResetFront()
        {
            _frontRadius = _frontStartRadius;
            _frontSpeed = _frontStartSpeed;
            _frontArrived = false;
        }
    }
}
