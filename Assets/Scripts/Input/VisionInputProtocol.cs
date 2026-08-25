namespace KakigoriTD.Input
{
    /// <summary>
    /// 位置検知ファイルの 1 行 <c>seq,channel</c>（LiDAR 版は <c>seq,channel,shaving</c>）を
    /// 照準チャンネルと削り状態へ変換する純ロジック（ADR-8 / ADR-24 / Phase 5）。
    /// 分類（点群→channel、落氷→削り）は Python 側が行い、本クラスは受け取った値を扱うだけ（docs/phase5.md §3-①）。
    ///
    /// 3 列目 <c>shaving</c>（0/1）は LiDAR 版（<c>tools/lidar-tracker/valley_tracker.py</c>）で追加されたもので、
    /// <b>省略可能</b>。2 列だけの旧 <c>position-tracker</c> の行も従来どおり受理し、その場合
    /// <see cref="IsShaving"/> は false になる（削りは keyboard / serial 側が担う旧構成のまま動く）。
    ///
    /// seq（単調増加）の停止で「ロスト」（Python 停止/ハング）を検知し、フェイルセーフで <see cref="ActiveChannel"/>
    /// を -1 に倒す（§3-⑥）。ライブ(A)/ラッチ(B)の切替（§3-②）と、境界ちらつき用デバウンス（保険・既定 0＝無効・§3-⑦）を持つ。
    /// MonoBehaviour・ファイル IO に依存しないため EditMode で検証できる（<see cref="SerialInputProtocol"/> と対称）。
    ///
    /// IO 層はポーリングで<b>同じ行を繰り返し Ingest しうる</b>ため、watchdog は「seq が変化したか」で判定する
    /// （Ingest 回数ではなく）。Python 再起動で seq が小さく戻っても「変化」＝新鮮とみなす。
    /// </summary>
    public class VisionInputProtocol
    {
        private readonly float _lostTimeout; // 秒。0 以下でロスト検知無効。
        private readonly bool _latch;        // false=ライブ(A) / true=ラッチ(B)
        private readonly float _debounce;    // 秒。0 以下で即時採用。

        private bool _everReceived;
        private bool _hasSeq;
        private long _lastSeq;
        private int _rawChannel = -1;        // 直近の seq 変化で得た channel
        private bool _rawShaving;            // 直近の seq 変化で得た shaving（3 列目・2 列行では false）
        private float _timeSinceSeqChange;

        private int _lastLatched = -1;       // ラッチ用：直近の有効(>=0)チャンネル
        private int _adopted = -1;           // デバウンス後に採用したチャンネル
        private int _pending = -1;           // デバウンス待ちの候補
        private float _pendingTime;

        /// <param name="lostTimeoutSeconds">seq が無変化でこの秒数を超えたらロスト。0 以下で無効。</param>
        /// <param name="latch">false＝ライブ(A：現在値に追従)／true＝ラッチ(B：-1 は直前の谷を保持)。</param>
        /// <param name="debounceSeconds">新チャンネルをこの秒数安定させてから採用（ちらつき対策・0 で即時）。</param>
        public VisionInputProtocol(float lostTimeoutSeconds, bool latch, float debounceSeconds)
        {
            _lostTimeout = lostTimeoutSeconds;
            _latch = latch;
            _debounce = debounceSeconds;
        }

        /// <summary>ロスト（seq が lostTimeout を超えて不変／一度も受信なし）。状態Y。無効時は常に false。</summary>
        public bool IsLost => _lostTimeout > 0f && (!_everReceived || _timeSinceSeqChange > _lostTimeout);

        /// <summary>照準チャンネル（-1＝谷なし）。ロスト時は常に -1（フェイルセーフ）。</summary>
        public int ActiveChannel => IsLost ? -1 : _adopted;

        /// <summary>
        /// 削り中か（LiDAR が落氷を検知している）。ロスト時は常に false（フェイルセーフ）。
        ///
        /// <see cref="ActiveChannel"/> と違い、ラッチ・デバウンスは通さない。取りこぼしを埋める
        /// ホールドは Python 側（<c>shaving_hold_sec</c>）が既に当てた後の値が届くため、ここで
        /// 二重に均すと停止遅延だけが伸びる。3 列目が無い行（旧 2 列契約）では false のまま。
        /// </summary>
        public bool IsShaving => !IsLost && _rawShaving;

        /// <summary>
        /// 1 行 <c>seq,channel</c>（または <c>seq,channel,shaving</c>）を取り込む。
        /// seq が変化したときだけ新鮮扱いで channel・shaving を更新する。
        /// </summary>
        public void Ingest(string line)
        {
            if (line == null) return;
            if (!TryParse(line, out long seq, out int channel, out bool shaving)) return; // 不正行は無視

            _everReceived = true;
            if (!_hasSeq || seq != _lastSeq) // seq 変化＝新しい書き込み（再起動で戻っても変化）
            {
                _lastSeq = seq;
                _hasSeq = true;
                _rawChannel = channel;
                _rawShaving = shaving;
                _timeSinceSeqChange = 0f;    // watchdog 再武装
            }
            // seq 同一（IO の再読み）＝据置。watchdog は Tick で進む。
        }

        /// <summary>経過時間を進め、watchdog・ラッチ・デバウンスを更新する。毎フレーム呼ぶ。</summary>
        public void Tick(float dt)
        {
            if (dt > 0f) _timeSinceSeqChange += dt;

            // モード適用：ライブは raw そのまま、ラッチは -1 を直前の有効値で埋める。
            int modeChannel;
            if (_latch)
            {
                if (_rawChannel >= 0) _lastLatched = _rawChannel;
                modeChannel = _lastLatched;
            }
            else
            {
                modeChannel = _rawChannel;
            }

            // デバウンス：候補が変わったら待ち直し、指定秒安定で採用（0 で即時）。
            if (modeChannel == _adopted)
            {
                _pending = _adopted;
                _pendingTime = 0f;
            }
            else
            {
                if (modeChannel != _pending)
                {
                    _pending = modeChannel;
                    _pendingTime = 0f;
                }
                else if (dt > 0f)
                {
                    _pendingTime += dt;
                }
                if (_pendingTime >= _debounce) _adopted = _pending;
            }
        }

        // "seq,channel" / "seq,channel,shaving" を厳格にパース。
        // 桁・範囲(channel は -1..3、shaving は 0/1)を検査し、外れは不正行として false。
        // 3 列目は省略可能（旧 position-tracker の 2 列行）で、無ければ shaving = false。
        private static bool TryParse(string line, out long seq, out int channel, out bool shaving)
        {
            seq = 0; channel = -1; shaving = false;

            int comma = line.IndexOf(',');
            if (comma <= 0) return false;
            if (!long.TryParse(line.Substring(0, comma).Trim(), out seq)) return false;

            string rest = line.Substring(comma + 1);
            int comma2 = rest.IndexOf(',');
            string channelText = comma2 < 0 ? rest : rest.Substring(0, comma2);

            if (!int.TryParse(channelText.Trim(), out channel)) return false;
            if (channel < -1 || channel > 3) return false; // 範囲外は不正

            if (comma2 >= 0)
            {
                // 3 列目があるなら厳格に検査する。壊れた 3 列目を黙って false 扱いにすると、
                // 「削っているのに雪崩が流れない」原因が行の形にあると気づけないため。
                if (!int.TryParse(rest.Substring(comma2 + 1).Trim(), out int shavingValue)) return false;
                if (shavingValue < 0 || shavingValue > 1) return false;
                shaving = shavingValue == 1;
            }

            return true;
        }
    }
}
