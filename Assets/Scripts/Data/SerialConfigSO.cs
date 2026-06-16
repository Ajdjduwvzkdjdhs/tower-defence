using UnityEngine;

namespace KakigoriTD.Data
{
    /// <summary>
    /// シリアル接続の設定（ポート名・ボーレート・読み取りタイムアウト・終了時の停止コマンド）を
    /// 保持する ScriptableObject（ADR-1：マジックナンバー直書き禁止 / ADR-15）。
    ///
    /// ポート名のプラットフォーム差:
    /// - macOS: <c>/dev/tty.usbserial-*</c> / <c>/dev/tty.usbmodem*</c>
    /// - Windows: <c>COMx</c>
    /// 実体アセットは <c>Assets/ScriptableObjects/</c> に置き、環境ごとに Inspector で設定する。
    /// </summary>
    [CreateAssetMenu(menuName = "KakigoriTD/SerialConfig")]
    public class SerialConfigSO : ScriptableObject
    {
        [Tooltip("シリアルポート名。macOS は /dev/tty.usbserial-* など、Windows は COMx。")]
        [SerializeField] private string _portName = "/dev/tty.usbserial-0001";
        [SerializeField] private int _baudRate = 9600;
        [Tooltip("ReadLine のタイムアウト（ms）。短すぎず長すぎず。20ms 目安。")]
        [SerializeField] private int _readTimeoutMs = 20;
        [Tooltip("終了時に送る安全停止コマンド（空なら送らない）。記事の最終 \"0\" 相当。")]
        [SerializeField] private string _stopCommand = "0";

        /// <summary>シリアルポート名。</summary>
        public string PortName => _portName;

        /// <summary>ボーレート（Arduino 側と一致させること）。</summary>
        public int BaudRate => _baudRate;

        /// <summary>ReadLine のタイムアウト（ms）。</summary>
        public int ReadTimeoutMs => _readTimeoutMs;

        /// <summary>終了時に送る安全停止コマンド（空＝送らない）。</summary>
        public string StopCommand => _stopCommand;
    }
}
