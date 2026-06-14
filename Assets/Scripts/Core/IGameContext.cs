namespace KakigoriTD.Core
{
    /// <summary>
    /// 各 State が外界（Gameplay 層）へ作用するための境界（ADR-2 / ADR-8, ADR-9）。
    /// State を UnityEngine 非依存に保ち、stub で EditMode 検証可能にするための抽象。
    /// 具象は Bootstrap の GameManager が実装する（後続マイルストーン）。
    /// ウェーブ番号は State 側が保持し、本コンテキストには状態を持たせない。
    /// </summary>
    public interface IGameContext
    {
        /// <summary>総ウェーブ数（Phase1 = 3）。</summary>
        int WaveCount { get; }

        /// <summary>指定ウェーブ（0-based）の制限時間（<c>WaveConfigSO.duration</c>）。</summary>
        float GetWaveDuration(int waveIndex);

        /// <summary>ウェーブ間の小休止秒（<c>waveLull</c>）。</summary>
        float WaveLullDuration { get; }

        /// <summary>ウェーブ開始バナーの表示秒。0 なら即時開始。</summary>
        float WaveIntroDuration { get; }

        /// <summary>砦HP が 0 になったか（敗北判定）。</summary>
        bool IsFortDestroyed { get; }

        /// <summary>現在の砦HP（勝利時スコア算出に使用）。</summary>
        int FortHp { get; }

        /// <summary>Title で開始入力があったか。</summary>
        bool IsStartRequested { get; }

        /// <summary>Result で再挑戦入力があったか。</summary>
        bool IsRetryRequested { get; }

        /// <summary>指定ウェーブの戦闘を開始する（湧き開始など）。</summary>
        void BeginWave(int waveIndex);

        /// <summary>Wave 終了時に残った敵を消去する。</summary>
        void ClearRemainingEnemies();

        /// <summary>リザルトを表示する。</summary>
        void ShowResult(bool isWin, int score);
    }
}
