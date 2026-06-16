namespace KakigoriTD.Input
{
    /// <summary>
    /// シリアル受信行を「削り状態（<see cref="IsShaving"/>）」へ変換する純ロジック（ADR-8 / ADR-15）。
    /// <c>1</c>＝削り中 / <c>0</c>＝停止。その他の行は無視する。
    ///
    /// watchdog: 一定時間（<c>watchdogTimeoutSeconds</c>）無受信なら fail-safe で <see cref="IsShaving"/> を
    /// false に倒す（切断時の「削りっぱなし」防止）。MonoBehaviour・スレッド・SerialPort に依存しないため
    /// EditMode で検証できる。
    /// </summary>
    public class SerialInputProtocol
    {
        private readonly float _watchdogTimeout; // 秒。0 以下で watchdog 無効。
        private bool _rawShaving;
        private float _timeSinceLastMessage;
        private bool _everReceived;

        /// <param name="watchdogTimeoutSeconds">無受信でこの秒数を超えたら削りを止める。0 以下で watchdog 無効。</param>
        public SerialInputProtocol(float watchdogTimeoutSeconds)
        {
            _watchdogTimeout = watchdogTimeoutSeconds;
        }

        /// <summary>削り中か。直近の受信が <c>1</c> で、かつ watchdog 内に受信があるときだけ true。</summary>
        public bool IsShaving => _rawShaving && !IsStale;

        /// <summary>
        /// watchdog タイムアウト超過（無受信で接続喪失とみなす）。一度も受信していなければ stale。
        /// watchdog 無効（タイムアウト 0 以下）のときは常に false。
        /// </summary>
        public bool IsStale => _watchdogTimeout > 0f && (!_everReceived || _timeSinceLastMessage > _watchdogTimeout);

        /// <summary>
        /// 受信した 1 行を取り込む。<c>1</c>/<c>0</c> で削り状態を更新し、その他の行は無視する。
        /// どの行でも「受信＝生存」として watchdog をリセットする（前後の空白・CR は無視）。
        /// </summary>
        public void Ingest(string line)
        {
            if (line == null) return;
            string s = line.Trim();

            // 受信があった事実で watchdog を更新（生存）。
            _everReceived = true;
            _timeSinceLastMessage = 0f;

            if (s == "1") _rawShaving = true;
            else if (s == "0") _rawShaving = false;
            // それ以外の行は無視（削り状態は変えない）。
        }

        /// <summary>経過時間を進め watchdog を更新する。毎フレーム呼ぶ。</summary>
        public void Tick(float dt)
        {
            if (dt > 0f) _timeSinceLastMessage += dt;
        }
    }
}
