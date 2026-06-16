namespace KakigoriTD.Input
{
    /// <summary>
    /// 複数の <see cref="IInputSource"/> を合成する Composite（ADR-6 / ADR-15）。
    /// 谷選択（<see cref="ActiveChannel"/>）と決定（<see cref="ConfirmPressed"/>）はキーボードを正とし、
    /// 削り（<see cref="IsShaving"/>）は「キーボード OR シリアル」の論理和にする。
    ///
    /// これにより実機が無くてもキーボードで全操作でき、シリアル接続時はハンドル回転でも削れる。
    /// 純 C#（MonoBehaviour 非依存）で、各子の現在値を getter で都度読む（ADR-8 / EditMode 検証）。
    /// <c>serial</c> は null 可（その場合キーボードのみ）。
    /// </summary>
    public class CompositeInputSource : IInputSource
    {
        private readonly IInputSource _keyboard;
        private readonly IInputSource _serial;

        public CompositeInputSource(IInputSource keyboard, IInputSource serial)
        {
            _keyboard = keyboard;
            _serial = serial;
        }

        /// <summary>谷選択はキーボードを優先。未選択(-1)のときだけシリアル側を見る。</summary>
        public int ActiveChannel
        {
            get
            {
                int kb = _keyboard?.ActiveChannel ?? -1;
                if (kb >= 0) return kb;
                return _serial?.ActiveChannel ?? -1;
            }
        }

        /// <summary>削りは論理和（キーボード Space 長押し OR ハンドル回転）。</summary>
        public bool IsShaving => (_keyboard?.IsShaving ?? false) || (_serial?.IsShaving ?? false);

        /// <summary>決定/再挑戦はエッジ。どちらかが押されたら true。</summary>
        public bool ConfirmPressed => (_keyboard?.ConfirmPressed ?? false) || (_serial?.ConfirmPressed ?? false);
    }
}
