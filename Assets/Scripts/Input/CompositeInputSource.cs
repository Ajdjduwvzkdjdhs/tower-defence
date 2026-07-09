namespace KakigoriTD.Input
{
    /// <summary>
    /// 複数の <see cref="IInputSource"/> を合成する Composite（ADR-6 / ADR-15 / ADR-24）。
    /// 谷選択（<see cref="ActiveChannel"/>）は <see cref="InputMode"/> で明示切替する（暗黙の優先順位合成は
    /// しない・docs/phase5.md §3-⑤）: <see cref="InputMode.Vision"/> なら位置検知、<see cref="InputMode.Keyboard"/>
    /// ならキーボード（未選択のみシリアルにフォールバック＝旧挙動）。
    /// 決定（<see cref="ConfirmPressed"/>）と削り（<see cref="IsShaving"/>）は<b>モードに依らず</b>従来通り
    /// 「キーボード OR シリアル」の論理和（位置検知は削り・決定を担当しない）。
    ///
    /// これにより実機が無くてもキーボードで全操作でき、シリアル接続時はハンドル回転でも削れる。
    /// 純 C#（MonoBehaviour 非依存）で、各子の現在値を getter で都度読む（ADR-8 / EditMode 検証）。
    /// <c>serial</c>・<c>vision</c> は null 可。
    /// </summary>
    public class CompositeInputSource : IInputSource
    {
        private readonly IInputSource _keyboard;
        private readonly IInputSource _serial;
        private readonly IInputSource _vision;
        private readonly InputMode _mode;

        /// <summary>旧シグネチャ（キーボード＋シリアル）。位置検知なし・Keyboard モードに委譲（後方互換）。</summary>
        public CompositeInputSource(IInputSource keyboard, IInputSource serial)
            : this(keyboard, serial, null, InputMode.Keyboard) { }

        public CompositeInputSource(IInputSource keyboard, IInputSource serial, IInputSource vision, InputMode mode)
        {
            _keyboard = keyboard;
            _serial = serial;
            _vision = vision;
            _mode = mode;
        }

        /// <summary>
        /// 谷選択。Vision モードは位置検知のみ。Keyboard モードはキーボード優先で、
        /// 未選択(-1)のときだけシリアル側を見る（旧挙動）。
        /// </summary>
        public int ActiveChannel
        {
            get
            {
                if (_mode == InputMode.Vision) return _vision?.ActiveChannel ?? -1;

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
