namespace KakigoriTD.Core
{
    /// <summary>
    /// 味方巻き込みペナルティの累積（Phase2 / ADR-17）。純ロジック（ADR-8）— UnityEngine 非依存。
    /// <see cref="Add"/> で加算、Wave リトライ時に <see cref="Reset"/>。
    /// 具象 <c>GameManager</c> が保持し、<see cref="IGameContext.PenaltyTotal"/> 越しに State へ供給する。
    /// </summary>
    public class PenaltyTally
    {
        private int _total;

        /// <summary>累積ペナルティ合計。</summary>
        public int Total => _total;

        /// <summary>ペナルティを加算する（巻き込み 1 回ごとに呼ぶ）。</summary>
        public void Add(int points)
        {
            if (points > 0) _total += points;
        }

        /// <summary>累積をリセットする（Wave リトライ時）。</summary>
        public void Reset() => _total = 0;
    }
}
