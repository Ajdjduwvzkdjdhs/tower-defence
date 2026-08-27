using System.Globalization;

namespace KakigoriTD.Input
{
    /// <summary>
    /// LiDAR 位置検知ファイルの 1 行 <c>seq,x,y,shaving</c> を「検知点（LiDAR 原点フレーム・mm）」と
    /// 「削り中フラグ」へ変換する純ロジック（ADR-8 / docs/phase-preview-feedback.md §3.1）。
    ///
    /// Phase 5 の <see cref="VisionInputProtocol"/>（<c>seq,channel,shaving</c>）の後継。谷選択（channel）は
    /// LiDAR 経路では廃止し、氷が落ちた連続座標をそのまま受け取る。分類（点群→代表点・落氷→削り）は
    /// Python（<c>tools/lidar-tracker/valley_tracker.py</c>）が行い、本クラスは受け取った値を扱うだけ。
    ///
    /// 行の形は必ず 3 カンマ 4 フィールド:
    ///  - <c>seq</c>    … 単調増加。IO のポーリングで同じ行を繰り返し Ingest しうるため、watchdog は
    ///                     「seq が変化したか」で判定する（Python 再起動で小さく戻っても「変化」＝新鮮）。
    ///  - <c>x,y</c>    … 代表点の座標[mm]（LiDAR 原点・<c>x = r·cosθ, y = −r·sinθ</c>）。
    ///                     <b>両方空欄</b>（<c>seq,,,0</c>）で「検知なし」。片方だけ空欄は不正行として捨てる。
    ///  - <c>shaving</c> … 0/1。Python 側で <c>shaving_hold_sec</c> のホールドを当てた後の値。
    ///
    /// seq の停止（Python 停止/ハング）を <see cref="IsLost"/> で検知し、検知点なし・削りなしへフェイルセーフする。
    /// ラッチ・デバウンス・平滑化は持たない（連続座標を生値で使う）。MonoBehaviour・ファイル IO 非依存で
    /// EditMode 検証できる（<see cref="VisionInputProtocol"/> と対称）。
    /// </summary>
    public class LidarPositionProtocol
    {
        private readonly float _lostTimeout; // 秒。0 以下でロスト検知無効。

        private bool _everReceived;
        private bool _hasSeq;
        private long _lastSeq;
        private bool _rawHasPoint;   // 直近の seq 変化で得た「検知点あり」
        private float _rawX;         // 直近の seq 変化で得た x[mm]（_rawHasPoint 時のみ有効）
        private float _rawY;         // 同 y[mm]
        private bool _rawShaving;    // 直近の seq 変化で得た shaving
        private float _timeSinceSeqChange;

        /// <param name="lostTimeoutSeconds">seq が無変化でこの秒数を超えたらロスト。0 以下で無効。</param>
        public LidarPositionProtocol(float lostTimeoutSeconds)
        {
            _lostTimeout = lostTimeoutSeconds;
        }

        /// <summary>ロスト（seq が lostTimeout を超えて不変／一度も受信なし）。無効時は常に false。</summary>
        public bool IsLost => _lostTimeout > 0f && (!_everReceived || _timeSinceSeqChange > _lostTimeout);

        /// <summary>検知点があるか。ロスト時は常に false（フェイルセーフ）。</summary>
        public bool HasPoint => !IsLost && _rawHasPoint;

        /// <summary>直近の検知点[mm]（LiDAR 原点フレーム）。<see cref="HasPoint"/> が false のときの値は不定。</summary>
        public (float x, float y) RawPointMm => (_rawX, _rawY);

        /// <summary>削り中か（LiDAR が落氷を検知している）。ロスト時は常に false（フェイルセーフ）。</summary>
        public bool IsShaving => !IsLost && _rawShaving;

        /// <summary>
        /// 1 行 <c>seq,x,y,shaving</c> を取り込む。seq が変化したときだけ新鮮扱いで値を更新する。
        /// </summary>
        public void Ingest(string line)
        {
            if (line == null) return;
            if (!TryParse(line, out long seq, out bool hasPoint, out float x, out float y, out bool shaving))
                return; // 不正行は無視

            _everReceived = true;
            if (!_hasSeq || seq != _lastSeq) // seq 変化＝新しい書き込み（再起動で戻っても変化）
            {
                _lastSeq = seq;
                _hasSeq = true;
                _rawHasPoint = hasPoint;
                _rawX = x;
                _rawY = y;
                _rawShaving = shaving;
                _timeSinceSeqChange = 0f;    // watchdog 再武装
            }
            // seq 同一（IO の再読み）＝据置。watchdog は Tick で進む。
        }

        /// <summary>経過時間を進め、watchdog を更新する。毎フレーム呼ぶ。</summary>
        public void Tick(float dt)
        {
            if (dt > 0f) _timeSinceSeqChange += dt;
        }

        // "seq,x,y,shaving" を厳格にパース。3 カンマ 4 フィールド固定。
        // x,y は「両方空欄＝検知なし」/「両方数値＝検知あり」。片方だけ空欄は不正行として false。
        // shaving は 0/1 のみ。桁・範囲外は行ごと捨てる（黙って既定値にすると原因が読めなくなるため）。
        private static bool TryParse(string line, out long seq, out bool hasPoint,
            out float x, out float y, out bool shaving)
        {
            seq = 0; hasPoint = false; x = 0f; y = 0f; shaving = false;

            string[] f = line.Split(',');
            if (f.Length != 4) return false;

            if (!long.TryParse(f[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out seq))
                return false;

            string xs = f[1].Trim();
            string ys = f[2].Trim();
            bool xe = xs.Length == 0;
            bool ye = ys.Length == 0;
            if (xe != ye) return false; // 片方だけ空欄は不正
            if (!xe)
            {
                if (!float.TryParse(xs, NumberStyles.Float, CultureInfo.InvariantCulture, out x)) return false;
                if (!float.TryParse(ys, NumberStyles.Float, CultureInfo.InvariantCulture, out y)) return false;
                if (float.IsNaN(x) || float.IsInfinity(x) || float.IsNaN(y) || float.IsInfinity(y)) return false;
                hasPoint = true;
            }

            if (!int.TryParse(f[3].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int sv))
                return false;
            if (sv < 0 || sv > 1) return false;
            shaving = sv == 1;

            return true;
        }
    }
}
