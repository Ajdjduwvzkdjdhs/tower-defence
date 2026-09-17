using KakigoriTD.Core;

namespace KakigoriTD.UI
{
    /// <summary>
    /// 直前のプレイ結果をシーンをまたいで受け渡す最小のハンドラ（ADR-30）。
    /// <c>ResultScene</c> / <c>GameOverScene</c> はスコアを持たない静的シーンなので、
    /// <c>GameManager.ShowResult</c> がここに置き、遷移先が読む。
    ///
    /// Singleton は <c>GameManager</c> 1 個のみという鉄則（ADR-5）を崩さないよう、
    /// MonoBehaviour ではなく**値を置くだけの static**にとどめる（状態も 1 つだけ）。
    /// 値が無ければ（直接 ResultScene を再生した等）表示側がフォールバックする。
    /// </summary>
    public static class GameResultHolder
    {
        /// <summary>直前のプレイ結果。未プレイなら null。</summary>
        public static StageResult? Last { get; set; }

        /// <summary>受け渡し済みの結果を捨てる（タイトルに戻るときなど）。</summary>
        public static void Clear() => Last = null;
    }
}
