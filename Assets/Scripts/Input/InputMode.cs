namespace KakigoriTD.Input
{
    /// <summary>
    /// <see cref="CompositeInputSource"/> が <see cref="IInputSource.ActiveChannel"/> の源として
    /// どちらを使うかの明示切替（ADR-24 / docs/phase5.md §3-⑤）。暗黙の優先順位合成はしない
    /// （キーボードのラッチが本番に紛れ込む事故を防ぐ）。削り（<see cref="IInputSource.IsShaving"/>）は
    /// モードに依らず常に「キーボード OR シリアル(M5Stick)」で合成する。
    /// </summary>
    public enum InputMode
    {
        /// <summary>開発・検証用。谷選択はキーボード（W/A/S/D）。</summary>
        Keyboard,

        /// <summary>展示本番。谷選択は位置検知（かき氷機の位置）。</summary>
        Vision,
    }
}
