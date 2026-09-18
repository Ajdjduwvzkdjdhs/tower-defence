namespace KakigoriTD.Core
{
    /// <summary>
    /// 「キーを N 秒押し続けたら確定」の判定（ADR-34：ランキング消去の誤操作防止）。純ロジック（ADR-8）。
    /// 押し続けている間だけ経過を積算し、途中で離せばリセット。到達で 1 回だけ発火し、
    /// 離して押し直すまで再発火しない。
    /// </summary>
    public sealed class HoldToConfirm
    {
        private float _held;

        public HoldToConfirm(float holdSec)
        {
            HoldSec = holdSec > 0f ? holdSec : 0.01f;
        }

        /// <summary>確定に必要な押下秒数。</summary>
        public float HoldSec { get; }

        /// <summary>いま押し続けているか（発火後も離すまでは true）。</summary>
        public bool IsHolding { get; private set; }

        /// <summary>この押下で既に発火したか（離すまで true）。表示側が「完了」を出し続けるのに使う。</summary>
        public bool HasFired { get; private set; }

        /// <summary>押下の進捗 0〜1（発火後は 1 に張り付く）。</summary>
        public float Progress01
        {
            get
            {
                if (!IsHolding) return 0f;
                float p = _held / HoldSec;
                return p > 1f ? 1f : p;
            }
        }

        /// <summary>
        /// 毎フレーム呼ぶ。<paramref name="held"/> が押下中か。到達した最初の 1 回だけ true を返す。
        /// </summary>
        public bool Tick(float deltaSec, bool held)
        {
            if (!held)
            {
                IsHolding = false;
                _held = 0f;
                HasFired = false;
                return false;
            }

            IsHolding = true;
            if (HasFired) return false;
            if (deltaSec > 0f) _held += deltaSec;
            if (_held < HoldSec) return false;

            HasFired = true;
            return true;
        }
    }
}
