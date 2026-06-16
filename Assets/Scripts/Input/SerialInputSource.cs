using UnityEngine;

namespace KakigoriTD.Input
{
    /// <summary>
    /// シリアル受信を <see cref="IInputSource.IsShaving"/> に変換する薄い MonoBehaviour アダプタ（ADR-15）。
    /// 谷選択・決定は担当しない（<see cref="ActiveChannel"/>=-1 / <see cref="ConfirmPressed"/>=false）。
    /// <see cref="CompositeInputSource"/> 側でキーボードと合成される。
    ///
    /// <see cref="SerialHandler.OnDataReceived"/> を購読して受信行を <see cref="SerialInputProtocol"/> に流し、
    /// <c>Update</c> で watchdog を進めて <see cref="IsShaving"/> を更新する。購読は <c>OnDestroy</c> で必ず解除（ADR-3）。
    /// </summary>
    public class SerialInputSource : MonoBehaviour, IInputSource
    {
        [Tooltip("無受信でこの秒数を超えたら削りを止める（fail-safe）。")]
        [SerializeField] private float _watchdogTimeoutSec = 0.5f;

        private SerialHandler _handler;
        private SerialInputProtocol _protocol;

        /// <inheritdoc/>
        public int ActiveChannel => -1;

        /// <inheritdoc/>
        public bool IsShaving { get; private set; }

        /// <inheritdoc/>
        public bool ConfirmPressed => false;

        /// <summary>Composition Root から transport を注入して購読を開始する。</summary>
        public void Configure(SerialHandler handler)
        {
            _protocol = new SerialInputProtocol(_watchdogTimeoutSec);
            _handler = handler;
            if (_handler != null) _handler.OnDataReceived += HandleLine;
        }

        private void HandleLine(string line) => _protocol?.Ingest(line);

        private void Update()
        {
            if (_protocol == null) return;
            _protocol.Tick(Time.deltaTime);
            IsShaving = _protocol.IsShaving;
        }

        private void OnDestroy()
        {
            if (_handler != null) _handler.OnDataReceived -= HandleLine;
        }
    }
}
