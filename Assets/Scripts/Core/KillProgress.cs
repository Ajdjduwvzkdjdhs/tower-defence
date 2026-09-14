namespace KakigoriTD.Core
{
    /// <summary>
    /// 撃破数の進捗（クリア条件＝目標撃破数への到達）。純ロジック（ADR-8）— UnityEngine 非依存。
    ///
    /// Wave 制を廃した単一ステージでは、この進捗が
    ///  ・クリア判定（<see cref="IsComplete"/>）
    ///  ・難易度ランプの軸（<see cref="Progress01"/> を爆発兵率・盾兵率のカーブに与える）
    /// の両方を兼ねる（ADR-30）。
    /// </summary>
    public class KillProgress
    {
        /// <param name="target">クリアに必要な撃破数。1 未満は 1 に丸める。</param>
        public KillProgress(int target)
        {
            Target = target < 1 ? 1 : target;
        }

        /// <summary>クリアに必要な撃破数（<c>StageConfigSO.TargetKillCount</c>）。</summary>
        public int Target { get; }

        /// <summary>現在の撃破数。</summary>
        public int Count { get; private set; }

        /// <summary>進捗 0..1。難易度カーブの評価に使う（Target 到達で 1）。</summary>
        public float Progress01
        {
            get
            {
                float p = (float)Count / Target;
                return p < 0f ? 0f : (p > 1f ? 1f : p);
            }
        }

        /// <summary>目標に到達したか（＝クリア）。</summary>
        public bool IsComplete => Count >= Target;

        /// <summary>撃破数を加算する（負値は無視）。ビーム撃破も算入する。</summary>
        public void Add(int n = 1)
        {
            if (n <= 0) return;
            Count += n;
        }

        /// <summary>プレイ開始時に初期化する。</summary>
        public void Reset() => Count = 0;
    }
}
