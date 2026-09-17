namespace KakigoriTD.Core
{
    /// <summary>
    /// 各 State が外界（Gameplay 層）へ作用するための境界（ADR-2 / ADR-8, ADR-9）。
    /// State を UnityEngine 非依存に保ち、stub で EditMode 検証可能にするための抽象。
    /// 具象は Bootstrap の GameManager が実装する。
    ///
    /// ADR-30 で Wave 制を廃止したため、ウェーブ数・制限時間・小休止・ペナルティは本境界から消え、
    /// 代わりに「目標撃破数への進捗」と「経過時間」が進行の軸になる。
    /// </summary>
    public interface IGameContext
    {
        /// <summary>クリアに必要な撃破数（<c>StageConfigSO.TargetKillCount</c>）。</summary>
        int TargetKillCount { get; }

        /// <summary>現在の撃破数（ビーム撃破も算入）。</summary>
        int KillCount { get; }

        /// <summary>このプレイの最大コンボ（リザルト表示用）。</summary>
        int MaxCombo { get; }

        /// <summary>戦闘開始からの経過秒（クリア時のスコア＝クリアタイム）。</summary>
        float ElapsedSec { get; }

        /// <summary>砦HP が 0 になったか（敗北判定）。</summary>
        bool IsFortDestroyed { get; }

        /// <summary>Title で開始入力があったか。</summary>
        bool IsStartRequested { get; }

        /// <summary>Result で再挑戦入力があったか。</summary>
        bool IsRetryRequested { get; }

        /// <summary>戦闘を開始する（砦HP・進捗・コンボ・ゲージ・残敵をリセットして湧きを始める）。</summary>
        void BeginBattle();

        /// <summary>終了時に残った敵を消去する。</summary>
        void ClearRemainingEnemies();

        /// <summary>リザルトを表示する。</summary>
        void ShowResult(StageResult result);
        void ShowCountdownStep(string text, bool isFinal);
        void ClearCountdown();
    }
}
