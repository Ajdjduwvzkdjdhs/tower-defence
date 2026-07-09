namespace KakigoriTD.Input
{
    /// <summary>
    /// 位置検知ファイルの 1 行 <c>seq,channel</c> を照準チャンネルへ変換する純ロジック（ADR-8 / ADR-24 / Phase 5）。
    /// 分類（(x,y)→channel）は Python 側が行い、本クラスは受け取った channel を扱うだけ（docs/phase5.md §3-①）。
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

        /// <summary>1 行 <c>seq,channel</c> を取り込む。seq が変化したときだけ新鮮扱いで channel を更新する。</summary>
        public void Ingest(string line)
        {
            if (line == null) return;
            if (!TryParse(line, out long seq, out int channel)) return; // 不正行は無視

            _everReceived = true;
            if (!_hasSeq || seq != _lastSeq) // seq 変化＝新しい書き込み（再起動で戻っても変化）
            {
                _lastSeq = seq;
                _hasSeq = true;
                _rawChannel = channel;
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

        // "seq,channel" を厳格にパース。桁・範囲(-1..3)を検査し、外れは不正行として false。
        private static bool TryParse(string line, out long seq, out int channel)
        {
            seq = 0; channel = -1;
            int comma = line.IndexOf(',');
            if (comma <= 0) return false;
            if (!long.TryParse(line.Substring(0, comma).Trim(), out seq)) return false;
            if (!int.TryParse(line.Substring(comma + 1).Trim(), out channel)) return false;
            if (channel < -1 || channel > 3) return false; // 範囲外は不正
            return true;
        }
    }
}
