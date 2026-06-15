namespace KakigoriTD.Core
{
    /// <summary>
    /// 雪崩の撃破タイミング（純ロジック、ADR-8 / ADR-11）。UnityEngine 非依存で EditMode 検証する。
    ///
    /// 仕様（spec「撃破解決＝深さ制御」）:
    /// - 削り開始から <c>avalancheDelay</c> 秒後に判定開始（＝最初の 1 行消去）。avalancheDelay=0 で即時。
    /// - 以降 <c>killInterval</c> 秒ごとに 1 行。
    /// - 削り停止・谷変更で進行リセット（次の削り開始時に delay から再開＝深さ制御）。
    ///
    /// MonoBehaviour（<c>AvalancheController</c>）は入力・氷・谷適用のアダプタとして本クロックを駆動する。
    /// </summary>
    public class AvalancheClock
    {
        private readonly float _delay;
        private readonly float _interval;
        private float _shaveTime; // 現在の連続削り経過秒
        private int _emitted;     // この削りセッションで消した行の累計

        /// <param name="avalancheDelay">削り開始から判定開始までの秒（0＝即時）。負値は 0 に丸める。</param>
        /// <param name="killInterval">1 行を消す間隔（秒）。極小値に下限を設けて 0 除算を防ぐ。</param>
        public AvalancheClock(float avalancheDelay, float killInterval)
        {
            _delay = avalancheDelay < 0f ? 0f : avalancheDelay;
            _interval = killInterval < 1e-4f ? 1e-4f : killInterval;
        }

        /// <summary>削り経過秒（可観測性／デバッグ用）。</summary>
        public float ShaveTime => _shaveTime;

        /// <summary>
        /// 1 フレーム進める。このフレームで消すべき行数（0 以上）を返す。
        /// </summary>
        /// <param name="dt">経過秒（<c>Time.deltaTime</c>）。</param>
        /// <param name="active">このフレームにアクティブ谷を実際に削れているか（谷選択中・Space 長押し・氷あり）。</param>
        public int Tick(float dt, bool active)
        {
            if (!active)
            {
                Reset();
                return 0;
            }

            if (dt > 0f) _shaveTime += dt;
            if (_shaveTime < _delay) return 0;

            // delay 経過時点で 1 回目、以降 interval ごと。累計との差分を今フレームの消去数とする
            // （フレーム落ちで複数行ぶん遅れても取りこぼさず追いつく）。
            int total = (int)((_shaveTime - _delay) / _interval) + 1;
            int n = total - _emitted;
            _emitted = total;
            return n < 0 ? 0 : n;
        }

        /// <summary>進行をリセット（削り停止・谷変更時）。次の active で delay から再開する。</summary>
        public void Reset()
        {
            _shaveTime = 0f;
            _emitted = 0;
        }
    }
}
