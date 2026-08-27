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
        /// <summary>【旧経路】開発・検証用。谷選択はキーボード（W/A/S/D）＋ Space 削り＋旧 AvalancheVfx。</summary>
        Keyboard,

        /// <summary>【旧経路】谷選択は position-tracker（camera）。seq,channel,shaving 契約。</summary>
        Vision,

        /// <summary>
        /// 【新経路・展示本番】LiDAR 位置検知（連続座標 seq,x,y,shaving）。谷選択なし。
        /// 検知点から放射状に雪玉を放出し、一般化位置到達で撃破（phase-preview-feedback.md）。
        /// </summary>
        LidarPosition,

        /// <summary>【新経路・開発用】マウス/矢印キーで検知点を疑似入力（LiDAR なしで新経路を検証）。</summary>
        SimPointer,
    }
}
