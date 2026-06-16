using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Threading;
using UnityEngine;
using KakigoriTD.Data;

namespace KakigoriTD.Input
{
    /// <summary>
    /// 自前のシリアル双方向 transport（ADR-15）。<see cref="SerialPort"/> を別スレッドで
    /// <c>ReadLine()</c> し、受信1行を <see cref="OnDataReceived"/> で通知する。<see cref="Send"/> で
    /// <c>WriteLine</c> 送信（汎用）。スレッド・ポートは <c>OnDestroy</c>/<c>OnApplicationQuit</c> で安全解放する。
    ///
    /// スレッド境界: 受信はバックグラウンドスレッドで行い Unity API には触れない。受信行はロック付きキューに積み、
    /// メインスレッドの <c>Update</c> で掃き出して <see cref="OnDataReceived"/> を発火する（消費は
    /// <see cref="SerialInputSource"/>）。これにより既存 <c>KeyboardInputSource</c> と同じ「Update でフレーム消費」の流儀に合う。
    ///
    /// 参考: https://qiita.com/hoktist/items/8e3ae56fd5f12c93dea8
    /// </summary>
    public class SerialHandler : MonoBehaviour
    {
        /// <summary>受信1行ごとにメインスレッドで発火する。</summary>
        public event Action<string> OnDataReceived;

        private SerialConfigSO _config;
        private SerialPort _serialPort;
        private Thread _thread;
        private volatile bool _isRunning;
        private volatile bool _isOpen;

        private readonly object _lock = new object();
        private readonly Queue<string> _inbox = new Queue<string>();

        /// <summary>ポートを開けているか（接続中）。</summary>
        public bool IsConnected => _isOpen;

        /// <summary>Composition Root から設定を注入して開始する。</summary>
        public void Begin(SerialConfigSO config)
        {
            _config = config;
            Open();
        }

        private void Open()
        {
            if (_config == null)
            {
                Debug.LogWarning("[SerialHandler] SerialConfigSO 未設定のため開始しません（キーボードのみで続行）。");
                return;
            }
            try
            {
                _serialPort = new SerialPort(_config.PortName, _config.BaudRate, Parity.None, 8, StopBits.One)
                {
                    ReadTimeout = _config.ReadTimeoutMs
                };
                _serialPort.Open();
                _isOpen = true;
                _isRunning = true;
                _thread = new Thread(ReadLoop) { IsBackground = true };
                _thread.Start();
                Debug.Log($"[SerialHandler] open {_config.PortName} @ {_config.BaudRate}");
            }
            catch (Exception e)
            {
                // 未接続・ポート不在でもゲームは続行（キーボードで遊べる）。
                _isOpen = false;
                Debug.LogWarning($"[SerialHandler] ポート {_config.PortName} を開けませんでした（未接続でも続行）: {e.Message}");
            }
        }

        // バックグラウンドスレッド: ReadLine をループ。受信行はキューへ積む（Unity API に触れない）。
        private void ReadLoop()
        {
            while (_isRunning && _serialPort != null && _serialPort.IsOpen)
            {
                try
                {
                    string line = _serialPort.ReadLine();
                    if (line != null)
                        lock (_lock) { _inbox.Enqueue(line); }
                }
                catch (TimeoutException)
                {
                    // ReadTimeout は正常。ループ継続（メインスレッドを固めない）。
                }
                catch (Exception)
                {
                    // 切断など。ループを抜け、後始末はメインスレッド（Close）に委ねる。
                    break;
                }
            }
        }

        // メインスレッド: 溜まった受信行を掃き出して購読者へ通知する。
        private void Update()
        {
            while (true)
            {
                string line;
                lock (_lock)
                {
                    if (_inbox.Count == 0) break;
                    line = _inbox.Dequeue();
                }
                OnDataReceived?.Invoke(line);
            }
        }

        /// <summary>
        /// Unity→Arduino へ 1 行送信（汎用）。送信内容・タイミングは呼び出し側が決める（本フェーズ未定の拡張ポイント）。
        /// </summary>
        public void Send(string line)
        {
            if (_serialPort == null || !_serialPort.IsOpen) return;
            try
            {
                lock (_lock) { _serialPort.WriteLine(line); }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[SerialHandler] 送信失敗: {e.Message}");
            }
        }

        private void Close()
        {
            _isRunning = false;

            // 終了時の安全停止コマンド（設定があれば送る）。記事の最終 "0" 相当。
            if (_serialPort != null && _serialPort.IsOpen && !string.IsNullOrEmpty(_config?.StopCommand))
            {
                try { _serialPort.WriteLine(_config.StopCommand); } catch { /* 切断時は無視 */ }
            }

            if (_thread != null && _thread.IsAlive) _thread.Join(200);
            _thread = null;

            if (_serialPort != null)
            {
                try { if (_serialPort.IsOpen) _serialPort.Close(); } catch { /* 無視 */ }
                _serialPort.Dispose();
                _serialPort = null;
            }
            _isOpen = false;
        }

        private void OnDestroy() => Close();
        private void OnApplicationQuit() => Close();
    }
}
