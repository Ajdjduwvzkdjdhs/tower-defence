namespace KakigoriTD.Core
{
    /// <summary>
    /// コンボ（一定時間内の連続撃破）のカウンタ。純ロジック（ADR-8）— UnityEngine 非依存。
    ///
    /// 前回の撃破から <see cref="WindowSec"/> 秒を超えて間が空くと 0 に戻る。
    /// 爆発の連鎖撃破も 1 体ずつ <see cref="AddKill"/> を呼ぶため、20体連鎖なら +20 される（ADR-30）。
    /// ビーム撃破はコンボに加算しないが、<see cref="ShortenGrace"/> で進行中コンボの残り猶予を縮める
    /// （ビームが盤面を一掃したあとにコンボだけが長く生き残らないようにする・ADR-35）。
    ///
    /// 時刻は外部注入（<c>Time.time</c>）で、EditMode から決定的に検証できるようにする。
    /// </summary>
    public class ComboTracker
    {
        // コンボが切れる時刻。「この時刻を超えたら切れる」（ちょうどは継続）。
        private float _expireAt = float.NegativeInfinity;

        /// <param name="windowSec">コンボ継続の猶予秒。0 以下は極小値に丸める。</param>
        public ComboTracker(float windowSec = 2.0f)
        {
            WindowSec = windowSec <= 0f ? 1e-4f : windowSec;
        }

        /// <summary>コンボ継続の猶予秒（<c>StageConfigSO.ComboWindowSec</c>）。</summary>
        public float WindowSec { get; }

        /// <summary>現在のコンボ数。途切れていれば 0。</summary>
        public int Count { get; private set; }

        /// <summary>このプレイでの最大コンボ数（リザルト表示用）。</summary>
        public int MaxCount { get; private set; }

        /// <summary>コンボが継続中か（Count が 2 以上）。UI の強調表示に使う。</summary>
        public bool IsActive => Count >= 2;

        /// <summary>
        /// 1 体撃破したことを通知する。前回撃破から <see cref="WindowSec"/> を超えていればコンボを
        /// 1 から数え直し、以内なら継続して +1 する。継続の猶予は改めて <see cref="WindowSec"/> に戻る
        /// （<see cref="ShortenGrace"/> で縮めた猶予は、その間に撃破できれば解除される）。
        /// </summary>
        /// <param name="now">現在時刻（秒）。<c>Time.time</c> を渡す。</param>
        public void AddKill(float now)
        {
            Count = now > _expireAt ? 1 : Count + 1;
            _expireAt = now + WindowSec;
            if (Count > MaxCount) MaxCount = Count;
        }

        /// <summary>
        /// コンボに加算せず、進行中コンボの残り猶予を <paramref name="graceSec"/> 秒以内に縮める
        /// （ビーム撃破用・ADR-35）。残り猶予がすでにそれより短ければ何もしない（延長はしない）。
        /// コンボが 0 のときも何もしない。
        /// </summary>
        /// <param name="now">現在時刻（秒）。</param>
        /// <param name="graceSec">縮めた後の猶予秒。負は 0 に丸める。</param>
        public void ShortenGrace(float now, float graceSec)
        {
            if (Count == 0) return;
            float expireAt = now + (graceSec < 0f ? 0f : graceSec);
            if (expireAt < _expireAt) _expireAt = expireAt;
        }

        /// <summary>
        /// 時間経過でコンボが切れていないか調べ、切れていれば 0 にする。毎フレーム呼ぶ（UI 表示用）。
        /// </summary>
        /// <returns>このフレームで切れたら true。</returns>
        public bool Tick(float now)
        {
            if (Count == 0 || now <= _expireAt) return false;
            Count = 0;
            return true;
        }

        /// <summary>プレイ開始時に初期化する（MaxCount も含めて全消去）。</summary>
        public void Reset()
        {
            Count = 0;
            MaxCount = 0;
            _expireAt = float.NegativeInfinity;
        }
    }
}
