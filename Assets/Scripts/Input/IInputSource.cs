namespace KakigoriTD.Input
{
    /// <summary>
    /// 入力の Strategy 境界（ADR-6）。GameManager をはじめとする利用側はこの抽象だけを見て入力を読む。
    /// 現状はキーボード実装（<see cref="KeyboardInputSource"/>）、将来は物理かき氷機実装に差し替える。
    /// </summary>
    public interface IInputSource
    {
        /// <summary>
        /// 照準中の谷。0:N / 1:W / 2:S / 3:E、未選択は -1。
        /// ラッチ式で同時に1本のみアクティブ（最後に押された方向が保持される）。
        /// </summary>
        int ActiveChannel { get; }

        /// <summary>
        /// 削り（Space）が押されているか。長押し中だけ true（雪崩ストリームが流れる）。
        /// </summary>
        bool IsShaving { get; }
    }
}
