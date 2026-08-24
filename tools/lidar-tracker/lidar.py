"""YDLIDAR T-mini Plus を公式 SDK（YDLidar-SDK）越しに扱う共通モジュール。

以前はシリアルポートを直接開き、0xAA55 パケットを自前で解析していた。
現在は公式 SDK（https://github.com/YDLIDAR/YDLidar-SDK）の Python バインディング
`ydlidar` に委譲している。SDK 側が面倒を見るもの:

  - スキャン開始/停止コマンドと応答の確認、機種・ファーム情報の取得
  - パケット同期・チェックサム検証・角度の補間と距離歪み補正
  - センサー個体のゼロ点角度オフセット補正
  - 断線時の自動再接続（LidarPropAutoReconnect、SDK 既定で ON）
  - レンジ外・太陽光ノイズ・ガラス反射ノイズの点を距離 0 に落とす

呼び出し側の契約は移植前と揃えてある。read_scan() が返すのは 1 回転ぶんの
サンプル辞書のリストで、キーは:

    {"angle": 度[0,360), "distance": mm, "intensity": 0-255, "x": mm, "y": mm}

移植前との差分は `flag`（干渉フラグ）が無いこと。SDK の LaserPoint は
angle / range / intensity の 3 つしか持たない。代わりに同じ判定を SDK 内部で
やらせる（filter_sun_noise / filter_glass_noise）。除かれた点は距離 0 で返るので、
呼び出し側の「距離 0 は無効」判定にそのまま乗る。

SDK のパラメータは公式サンプル python/examples/tminiplus_test.py に準拠。
"""

import math

import ydlidar

#=============================
# T-mini Plus 用の SDK 設定
#=============================

# 目標回転数[Hz]。SDK が実測値を見てモーター PWM を追い込む
SCAN_FREQUENCY_HZ = 10.0

# サンプリングレート[kHz]。T-mini Plus は 4K
SAMPLE_RATE_K = 4

# 強度のビット数。T-mini Plus は 1 バイト（0〜255）で、距離値の下位 2bit が
# 干渉フラグ（3＝太陽光 / 2＝ガラス反射）。SDK はこの 8bit 指定のときだけ
# そのフラグを読むので、enableSunNoise / enableGlassNoise とセットで効く。
INTENSITY_BIT = 8

# デバイスとしての測距範囲[m]。データシートの実力値は 0.02〜12 m。
# ここを外れた点は SDK が距離 0 にする。用途ごとの「関心範囲」の絞り込みは
# ここではなく呼び出し側（config.toml の [detection]）で行う。
DEVICE_MIN_RANGE_M = 0.02
DEVICE_MAX_RANGE_M = 12.0

MM_PER_M = 1000.0


def max_points_per_rev(scan_frequency_hz, sample_rate_k=SAMPLE_RATE_K):
    """その回転数で 1 回転に得られる点数の理論値。

    サンプリングレートは回転数に依らず一定なので、回転を速くするとその分だけ
    1 回転あたりの点数が減る（10Hz なら 4000/10 = 400 点）。実際に返ってくる数は
    無反射やノイズ除去で必ずこれ以下になる。
    """
    return int(sample_rate_k * 1000 / scan_frequency_hz)


class TMiniPlus:
    """CYdLidar の薄いラッパ。1 回転ぶんの点群を辞書のリストで返す。"""

    def __init__(
        self,
        port,
        baudrate,
        filter_sun_noise=True,
        filter_glass_noise=False,
        scan_frequency_hz=SCAN_FREQUENCY_HZ,
        sample_rate_k=SAMPLE_RATE_K,
        min_range_m=DEVICE_MIN_RANGE_M,
        max_range_m=DEVICE_MAX_RANGE_M,
    ):
        # ydlidar.os_init() はあえて呼ばない。SDK の SIGINT ハンドラが入ると
        # Python に KeyboardInterrupt が飛ばなくなり、Ctrl-C での終了処理が
        # 動かなくなる。停止は呼び出し側のフラグ（stop_event）で行う。
        # 複数台を同時に開くので、ログにどのポートの話か出せるよう持っておく
        self._port = str(port)
        self._laser = ydlidar.CYdLidar()
        self._scan = ydlidar.LaserScan()
        self._scanning = False
        self._closed = False

        # setlidaropt は値の型（int/float/bool/str）ごとのオーバーロード。
        # 型を取り違えると例外ではなく False が返るだけで黙って既定値のまま
        # 動いてしまうので、明示的にキャストしたうえで戻り値を全部検査する。
        options = [
            ("SerialPort", ydlidar.LidarPropSerialPort, self._port),
            ("SerialBaudrate", ydlidar.LidarPropSerialBaudrate, int(baudrate)),
            ("LidarType", ydlidar.LidarPropLidarType, int(ydlidar.TYPE_TRIANGLE)),
            ("DeviceType", ydlidar.LidarPropDeviceType, int(ydlidar.YDLIDAR_TYPE_SERIAL)),
            ("ScanFrequency", ydlidar.LidarPropScanFrequency, float(scan_frequency_hz)),
            ("SampleRate", ydlidar.LidarPropSampleRate, int(sample_rate_k)),
            ("SingleChannel", ydlidar.LidarPropSingleChannel, False),
            ("MinAngle", ydlidar.LidarPropMinAngle, -180.0),
            ("MaxAngle", ydlidar.LidarPropMaxAngle, 180.0),
            ("MinRange", ydlidar.LidarPropMinRange, float(min_range_m)),
            ("MaxRange", ydlidar.LidarPropMaxRange, float(max_range_m)),
            # 綴りは SDK のまま（Intenstiy）。強度を載せない 2 バイト形式との切替
            ("Intenstiy", ydlidar.LidarPropIntenstiy, True),
            ("IntenstiyBit", ydlidar.LidarPropIntenstiyBit, int(INTENSITY_BIT)),
        ]

        rejected = [
            name for name, prop, value in options
            if not self._laser.setlidaropt(prop, value)
        ]
        if rejected:
            raise RuntimeError(
                f"LiDAR ({self._port}) の設定が拒否されました: "
                + ", ".join(rejected)
            )

        # 旧実装が受信後に flag で落としていた外乱光・鏡面反射の除去に相当する。
        # SDK の LaserPoint は flag を持たないので、SDK 内部で落としてもらう。
        self._laser.enableSunNoise(filter_sun_noise)
        self._laser.enableGlassNoise(filter_glass_noise)

    def start_scan(self):
        """接続してスキャンを開始する。失敗したら SDK のエラー文言で例外にする。"""
        if not self._laser.initialize():
            raise RuntimeError(
                f"LiDAR ({self._port}) に接続できません: "
                f"{self._laser.DescribeError()}"
            )

        if not self._laser.turnOn():
            self._laser.disconnecting()
            self._closed = True
            raise RuntimeError(
                f"LiDAR ({self._port}) のスキャンを開始できません: "
                f"{self._laser.DescribeError()}"
            )

        self._scanning = True

        print(f"Scan Started  ({self._port})")

    def read_scan(self):
        """1 回転ぶんの点群を読む。取りこぼしたときは空リストを返す。

        SDK は 1 回転が揃うまで内部で最大 1 秒ブロックする。タイムアウトや
        一時的な通信エラーでは False が返るだけなので、例外にせず空リストにして
        呼び出し側のループを続けさせる（断線は SDK 側が自動再接続する）。
        """
        if not self._laser.doProcessSimple(self._scan):
            return []

        samples = []

        for point in self._scan.points:
            # SDK の角度は rad で (-π, π]。旧実装と同じ 0〜360 度に直す
            distance = point.range * MM_PER_M

            samples.append({
                "angle": math.degrees(point.angle) % 360.0,
                "distance": distance,
                "intensity": point.intensity,
                "x": distance * math.cos(point.angle),
                "y": distance * math.sin(point.angle),
            })

        return samples

    def stop_scan(self):
        """スキャンを止める（接続は保つ）。二重呼び出しは無害。"""
        if not self._scanning:
            return

        self._laser.turnOff()
        self._scanning = False

        print(f"Scan Stopped  ({self._port})")

    def close(self):
        """接続を閉じる。二重呼び出しは無害。"""
        if self._closed:
            return

        self._laser.disconnecting()
        self._closed = True
