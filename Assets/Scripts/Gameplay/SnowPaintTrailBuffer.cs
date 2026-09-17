using UnityEngine;

namespace KakigoriTD.Gameplay
{
    /// <summary>
    /// 山中心（XZ原点）からの方位角で 360/sectorDeg 個の扇形に分割し、各扇形について
    /// 「最後に塗られた時刻」「雪玉が現れた半径（積もる起点）」「雪玉が実際に到達した最遠半径
    /// （積もる範囲の現在の広がり）」を保持する純粋 C# クラス（MonoBehaviour ではない）。
    /// 到達最遠半径を別途追跡するのは、雪玉が滑走しきる前から扇形全体を即座に塗ってしまうと
    /// 「雪崩本体より先に雪が積もって見える」ため、実際に雪玉が通った分だけ広がるようにするため。
    /// <see cref="SnowPaintTrail"/> が毎フレーム <see cref="BuildUploadArray"/> を呼び、
    /// シェーダーへ渡す配列（インデックス＝扇形番号、xyz＝(最終塗布時刻, 起点半径, 到達最遠半径)）
    /// を組み立てる。
    ///
    /// 見た目だけの付随データであり Core のゲーム進行ロジックではないため、
    /// Vector3 を使う本クラスは Core ではなく Gameplay（Runtime asmdef）に置く。
    /// </summary>
    public sealed class SnowPaintTrailBuffer
    {
        // 未塗りの扇形は十分に過去の時刻にしておき、age = now - time が常に fadeSec を超えるようにする。
        public const float NeverPainted = -1_000_000f;

        private readonly float[] _sectorTimes;
        private readonly float[] _innerRadius;
        private readonly float[] _reachedRadius;
        private readonly float _sectorDeg;
        private readonly float _fadeSec;

        public int SectorCount => _sectorTimes.Length;
        public float SectorDeg => _sectorDeg;

        public SnowPaintTrailBuffer(int sectorCount, float sectorDeg, float fadeSec)
        {
            sectorCount = Mathf.Max(1, sectorCount);
            _sectorTimes = new float[sectorCount];
            _innerRadius = new float[sectorCount];
            _reachedRadius = new float[sectorCount];
            for (int i = 0; i < _sectorTimes.Length; i++) _sectorTimes[i] = NeverPainted;
            _sectorDeg = Mathf.Max(0.01f, sectorDeg);
            _fadeSec = Mathf.Max(0.0001f, fadeSec);
        }

        /// <summary>山中心から見た worldPos の方位角が属する扇形のインデックスを返す。</summary>
        public int SectorIndexFor(Vector3 worldPos)
        {
            float angleDeg = Mathf.Atan2(worldPos.z, worldPos.x) * Mathf.Rad2Deg;
            if (angleDeg < 0f) angleDeg += 360f;
            int idx = Mathf.FloorToInt(angleDeg / _sectorDeg) % _sectorTimes.Length;
            if (idx < 0) idx += _sectorTimes.Length;
            return idx;
        }

        /// <summary>
        /// worldPos が属する扇形を「今塗られた」として記録する。
        /// その扇形が既にフェードしきっていた（＝新規の雪玉）なら、その位置の半径を
        /// 起点・到達最遠半径の両方に設定する（雪はまだ何も積もっていない状態から開始）。
        /// まだ生存中（同じ雪玉が滑走中など）なら、起点はより内側（山頂寄り）へだけ、
        /// 到達最遠半径はより外側へだけ更新する（雪玉が外向きに進むにつれて積もる範囲が広がり、
        /// 逆に後退はしない）。
        /// </summary>
        public void MarkSector(Vector3 worldPos, float now)
        {
            int idx = SectorIndexFor(worldPos);
            float dist = new Vector2(worldPos.x, worldPos.z).magnitude;

            float age = now - _sectorTimes[idx];
            if (age > _fadeSec)
            {
                _innerRadius[idx] = dist;
                _reachedRadius[idx] = dist;
            }
            else
            {
                _innerRadius[idx] = Mathf.Min(_innerRadius[idx], dist);
                _reachedRadius[idx] = Mathf.Max(_reachedRadius[idx], dist);
            }
            _sectorTimes[idx] = now;
        }

        /// <summary>
        /// dest（長さ SectorCount 以上を想定）へ各扇形の
        /// (最終塗布時刻, 起点半径, 到達最遠半径, 0) を書き出す。
        /// </summary>
        public void BuildUploadArray(Vector4[] dest)
        {
            int n = Mathf.Min(dest.Length, _sectorTimes.Length);
            for (int i = 0; i < n; i++)
                dest[i] = new Vector4(_sectorTimes[i], _innerRadius[i], _reachedRadius[i], 0f);
            for (int i = n; i < dest.Length; i++) dest[i] = new Vector4(NeverPainted, 0f, 0f, 0f);
        }
    }
}
