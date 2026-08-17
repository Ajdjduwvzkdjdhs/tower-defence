"""YDLIDAR T-mini Plus のシリアル通信・パケット解析を担う共通モジュール。

既存スクリプトに重複コピーされていた TMiniPlus クラスをここへ抽出した。
当面は新しい描画スクリプト（scan_plot_realtime.py）からのみ利用する。
"""

import math
import struct
import time

import serial


class TMiniPlus:

    def __init__(self, port, baudrate):
        self.ser = serial.Serial(
            port=port,
            baudrate=baudrate,
            timeout=1
        )

    def send_command(self, cmd):
        self.ser.write(bytes([0xA5, cmd]))

    def start_scan(self):

        # 前回の実行が強制終了していると本体はスキャン中のまま。
        # 停止させてから溜まったデータを捨てないと応答ヘッダを読み違える
        self.send_command(0x65)
        time.sleep(0.1)
        self.ser.reset_input_buffer()

        self.send_command(0x60)

        header = self.ser.read(7)

        if len(header) != 7:
            raise RuntimeError("Scan start failed")

        if header[:2] != b"\xA5\x5A":
            raise RuntimeError("Invalid response")

        print("Scan Started")

    def stop_scan(self):

        self.send_command(0x65)

        print("Scan Stopped")

    def close(self):

        self.ser.close()

    @staticmethod
    def calculate_angles(fsa, lsa, lsn):

        start_angle = (fsa >> 1) / 64.0
        end_angle = (lsa >> 1) / 64.0

        diff = end_angle - start_angle

        if diff < 0:
            diff += 360.0

        if lsn == 1:
            return [start_angle]

        step = diff / (lsn - 1)

        return [
            (start_angle + step * i) % 360
            for i in range(lsn)
        ]

    def read_packet(self):

        while True:

            if self.ser.read(2) == b"\xAA\x55":
                break

        ct = self.ser.read(1)[0]
        lsn = self.ser.read(1)[0]

        fsa = struct.unpack("<H", self.ser.read(2))[0]
        lsa = struct.unpack("<H", self.ser.read(2))[0]

        checksum = struct.unpack("<H", self.ser.read(2))[0]

        angles = self.calculate_angles(fsa, lsa, lsn)

        samples = []

        for i in range(lsn):

            s = self.ser.read(3)

            intensity = s[0]

            flag = s[1] & 0x03

            distance = (s[2] << 6) | (s[1] >> 2)

            angle = angles[i]

            x = distance * math.cos(math.radians(angle))
            y = distance * math.sin(math.radians(angle))

            samples.append({
                "angle": angle,
                "distance": distance,
                "intensity": intensity,
                "flag": flag,
                "x": x,
                "y": y
            })

        return samples
