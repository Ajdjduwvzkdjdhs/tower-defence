"""Slamtec RPLIDAR S1 をシリアルプロトコル直叩きで扱う共通モジュール。

兄弟ツール `tools/lidar-tracker`（YDLIDAR T-mini Plus）の `lidar.py` と**同じ契約**に
してある。read_scan() が返すのは 1 回転ぶんのサンプル辞書のリストで、キーは:

    {"angle": 度[0,360), "distance": mm, "intensity": 品質, "flag": 0,
     "x": mm, "y": mm}

呼び出し側（valley_tracker.py）はこの辞書だけを見るので、T-mini Plus 版と
入れ替えても動く。ただし機種差が 2 つあり、これは吸収できない:

  - **intensity のレンジが違う**。T-mini Plus は 0〜255（8bit）だが、S1 の標準スキャン
    モードが返す quality は 6bit（0〜63）。`config.toml` の [ice] intensity_max は
    S1 用に取り直すこと（README「Intensity（quality）」参照）。
  - **flag は常に 0**。T-mini Plus は距離値の下位 2bit が太陽光／ガラス反射の干渉
    フラグだったが、RPLIDAR にはそれに当たるものが無い。キーだけ残してある。

## なぜ公式 SDK を呼ばず、プロトコルを自前実装するのか

YDLIDAR-SDK は SWIG の Python バインディング（`python/`）を同梱していたので、
lidar-tracker は vendor/ に clone して uv でビルドし、`import ydlidar` していた。
RPLIDAR の公式 SDK（https://github.com/Slamtec/rplidar_sdk）は **C++ のみ**で
Python バインディングを持たない。呼ぶには pybind11 などの glue を自分で書いて
CMake ビルドを通す必要があり、macOS では最新 master のビルド不具合も報告されている
（Slamtec/rplidar_sdk#137）。

RPLIDAR のシリアルプロトコルは公開仕様で、S1 が使う 2 つのモードは実装量も小さい。
pyserial だけで閉じる方が、ネイティブビルド一式（cmake / swig / パッチ）を
抱えるより現場で壊れにくい。公式 SDK は「動かないときの答え合わせ」用に
残しておく（README「切り分け」参照）。

## S1 の 2 つのスキャンモード

| モード | コマンド | 1 点あたり | intensity | 1 回転の点数(10Hz) |
|--------|----------|-----------|-----------|-------------------|
| standard | 0x20 SCAN | 5 バイト | **あり**(0〜63) | 約 410 |
| express  | 0x82 EXPRESS_SCAN (dense) | 2.1 バイト | **無し** | 約 920 |

express（dense capsule）は 84 バイトに 40 点を詰めるので全レートが流せる代わりに、
電文に距離しか載っていない（本家 SDK も quality には固定値 0x2F<<2 を入れているだけ）。

落氷の判定は intensity を主要な手がかりにしているので、**既定は standard**。

## 実機で分かったこと（S1 / firmware 1.28・2026-08-31）

この個体の express は **dense capsule (0x86) ではなく ultra capsule (0x85)** だった:

    [0] Standard    244.00 us/sample  max 40.0m  ans=0x81  ← このドライバが読める
    [1] DenseBoost  108.00 us/sample  max 40.0m  ans=0x85  ← 読めない（typical）

ultra capsule は 132 バイト・32 cabin で距離を可変ビット長の差分で詰めた別形式で、
このドライバは復号しない。よって**この個体では standard モードのみ使える**。
standard の約 410 点/回転は T-mini Plus（10Hz で 400 点）と同等で、しかも quality が
付くので、この用途では実用上の不足はない。

回転の切れ目を示す開始フラグ（S ビット）は**角度 0 度では立たない**（実機では約 69 度）。
_append() のコメントを参照。
"""

import math
import struct
import time

import serial

#=============================
# RPLIDAR プロトコル定数
#=============================

# 要求パケット: A5 <cmd> [<len> <payload...> <checksum>]
SYNC_BYTE = 0xA5
# 応答ディスクリプタ: A5 5A <30bit len | 2bit mode> <type>
SYNC_BYTE2 = 0x5A
DESCRIPTOR_LEN = 7

CMD_STOP = 0x25                  # 応答なし。送ったら 20ms 待って捨てる
CMD_RESET = 0x40                 # 応答なし。起動メッセージが流れてくる
CMD_SCAN = 0x20                  # standard スキャン開始（5 バイト/点）
CMD_EXPRESS_SCAN = 0x82          # express スキャン開始（payload にモード id）
CMD_GET_INFO = 0x50
CMD_GET_HEALTH = 0x52
CMD_GET_LIDAR_CONF = 0x84

# TOF 機（S1/S2）のモーター速度指定。A シリーズの DTR / PWM 制御ではなく rpm で渡す
CMD_HQ_MOTOR_SPEED_CTRL = 0xA8

ANS_TYPE_DEVINFO = 0x04
ANS_TYPE_DEVHEALTH = 0x06
ANS_TYPE_LIDAR_CONF = 0x20
ANS_TYPE_MEASUREMENT = 0x81              # standard スキャンの 5 バイトノード
ANS_TYPE_MEASUREMENT_CAPSULED = 0x82         # legacy capsule（A2 等・このドライバは非対応）
ANS_TYPE_MEASUREMENT_CAPSULED_ULTRA = 0x85   # ultra capsule（S1 の DenseBoost・非対応）
ANS_TYPE_MEASUREMENT_DENSE_CAPSULED = 0x86   # dense capsule（84 バイト・このドライバが読む express）

# GET_LIDAR_CONF のサブタイプ（probe.py がモード一覧を引くのに使う）
CONF_SCAN_MODE_COUNT = 0x00000070
CONF_SCAN_MODE_US_PER_SAMPLE = 0x00000071
CONF_SCAN_MODE_MAX_DISTANCE = 0x00000074
CONF_SCAN_MODE_ANS_TYPE = 0x00000075
CONF_SCAN_MODE_TYPICAL = 0x0000007C
CONF_SCAN_MODE_NAME = 0x0000007F

HEALTH_GOOD = 0
HEALTH_WARNING = 1
HEALTH_ERROR = 2

# dense capsule（express）の 1 パケット: 同期/チェックサム 2 + 開始角 2 + 距離 40*2
DENSE_CABIN_COUNT = 40
DENSE_PACKET_LEN = 2 + 2 + DENSE_CABIN_COUNT * 2
DENSE_SYNC_1 = 0xA      # 先頭 2 バイトの上位ニブル。下位ニブルはチェックサムの前半/後半
DENSE_SYNC_2 = 0x5

#=============================
# S1 の機種パラメータ
#=============================

SCAN_MODE_STANDARD = "standard"
SCAN_MODE_EXPRESS = "express"

# S1 のシリアルボーレート。A シリーズ（115200）とは違うので取り違えに注意
DEFAULT_BAUDRATE = 256000

# 目標回転数[Hz]。データシートの範囲は 8〜15Hz、標準 10Hz
SCAN_FREQUENCY_HZ = 10.0

# サンプリングレート[kHz]。実機（firmware 1.28）が GET_LIDAR_CONF で申告する値:
#   Standard   244 us/sample -> 4.098kHz （10Hz で約 410 点/回転。実測 369〜489）
#   DenseBoost 108 us/sample -> 9.259kHz
# standard は 5 バイト/点なので 256000bps のリンク上限（25600B/s ÷ 5B = 5.12kHz）も
# 効くが、機体側の 4.098kHz の方が低いので、律速はこちら。
SAMPLE_RATE_K_EXPRESS = 9.26
SAMPLE_RATE_K_STANDARD = 4.098

# デバイスとしての測距範囲[m]。S1 のデータシートは 0.1〜40 m。
# ただし standard モードの距離は q2 の uint16 なので、電文で表せるのは 16383.75mm
# （約 16.4m）まで。40m まで欲しいなら express モードを使うこと（落氷の検知は
# 1m 未満なので、この用途では効かない）。
# ここを外れた点は距離 0 にして返す（T-mini Plus 版で SDK がやっていたのと同じ扱い）。
# 用途ごとの「関心範囲」の絞り込みは呼び出し側（config.toml の [detection]）で行う。
DEVICE_MIN_RANGE_M = 0.1
DEVICE_MAX_RANGE_M = 40.0

# standard モードの quality は 6bit。intensity_max の上限の目安として公開する
MAX_QUALITY_STANDARD = 63

MM_PER_M = 1000.0

# 1 回転が揃うまで read_scan() が待つ上限[秒]。T-mini Plus 版の SDK が
# 内部で最大 1 秒ブロックしていたのに合わせてある
SCAN_TIMEOUT_SEC = 1.0

# シリアルの 1 回の read が待つ上限[秒]。停止フラグの効きを鈍らせないよう短くする
SERIAL_TIMEOUT_SEC = 0.2

# 回転の切れ目（start フラグ）が一度も来ないまま点が溜まったら復号がずれている。
# 無限に溜め込まず捨ててやり直す
MAX_BUFFERED_POINTS = 20000


def max_points_per_rev(scan_frequency_hz, sample_rate_k=SAMPLE_RATE_K_EXPRESS):
    """その回転数で 1 回転に得られる点数の理論値。

    サンプリングレートは回転数に依らず一定なので、回転を速くするとその分だけ
    1 回転あたりの点数が減る。実際に返ってくる数は無反射やノイズ除去で必ず
    これ以下になる。standard モードでは sample_rate_k に
    SAMPLE_RATE_K_STANDARD（リンク律速の上限）を渡すこと。
    """
    return int(sample_rate_k * 1000 / scan_frequency_hz)


def sample_rate_k_for(scan_mode):
    """スキャンモードに対応するサンプリングレート[kHz]を返す。"""
    return (
        SAMPLE_RATE_K_EXPRESS if scan_mode == SCAN_MODE_EXPRESS
        else SAMPLE_RATE_K_STANDARD
    )


def _checksum(data):
    """要求パケットのチェックサム（全バイトの XOR）。"""
    result = 0
    for byte in data:
        result ^= byte
    return result


class RPLidarS1:
    """RPLIDAR S1 の薄いドライバ。1 回転ぶんの点群を辞書のリストで返す。

    lidar-tracker の `TMiniPlus` と同じ寿命で使う:
        start_scan() → read_scan() を繰り返す → stop_scan() → close()
    """

    def __init__(
        self,
        port,
        baudrate=DEFAULT_BAUDRATE,
        scan_mode=SCAN_MODE_STANDARD,
        scan_frequency_hz=SCAN_FREQUENCY_HZ,
        mode_id=None,
        min_range_m=DEVICE_MIN_RANGE_M,
        max_range_m=DEVICE_MAX_RANGE_M,
    ):
        if scan_mode not in (SCAN_MODE_STANDARD, SCAN_MODE_EXPRESS):
            raise ValueError(
                f"scan_mode は {SCAN_MODE_STANDARD} / {SCAN_MODE_EXPRESS} の"
                f"どちらかです（現在: {scan_mode!r}）"
            )

        # 複数台を同時に開くので、ログにどのポートの話か出せるよう持っておく
        self._port = str(port)
        self._baudrate = int(baudrate)
        self._scan_mode = scan_mode
        self._scan_frequency_hz = float(scan_frequency_hz)
        self._mode_id = mode_id
        self._min_range_mm = float(min_range_m) * MM_PER_M
        self._max_range_mm = float(max_range_m) * MM_PER_M

        self._serial = None
        self._scanning = False
        self._closed = False

        # まだ復号していない受信バイト。パケット境界を見失っても 1 バイトずつ
        # ずらして同期を取り直せるよう、read() の戻りをここへ溜める
        self._rx = bytearray()
        # 復号済みで、まだ 1 回転として切り出していない点。先頭は必ず回転の開始点
        self._points = []
        self._last_angle = None
        # 機体の開始フラグを一度でも見たか。見えている間は角度の巻き戻りを
        # 回転の切れ目に使わない（下の _append を参照）
        self._seen_start_flag = False
        # express（dense）は 1 つ前のパケットの開始角が要るので持ち越す
        self._prev_capsule = None

    #--- 接続と寿命 -------------------------------------------------------

    def open(self):
        """ポートを開き、静止状態（スキャン停止・バッファ空）にする。

        スキャンは始めない。機種情報やスキャンモード一覧を訊くだけの probe.py が
        ここまでで足りるよう、start_scan() から切り出してある。二重呼び出しは無害。
        """
        if self._serial is not None:
            return

        try:
            self._serial = serial.Serial(
                self._port,
                self._baudrate,
                timeout=SERIAL_TIMEOUT_SEC,
                write_timeout=1.0,
            )
        except serial.SerialException as e:
            raise RuntimeError(f"LiDAR ({self._port}) に接続できません: {e}") from e

        self._closed = False

        try:
            # A シリーズは DTR を上げるとモーターが止まる。S1 では使わないが、
            # 前のプロセスが上げたままなら下ろしておく
            self._serial.dtr = False

            # 前回のプロセスがスキャンしたまま落ちていることがある。
            # 止めて溜まっているバイトを捨ててから始める
            self._send(CMD_STOP)
            time.sleep(0.05)
            self._serial.reset_input_buffer()
            self._rx.clear()

            health = self.health()
            if health["status"] == HEALTH_ERROR:
                raise RuntimeError(
                    f"LiDAR ({self._port}) がエラー状態です "
                    f"(error_code={health['error_code']})。"
                    "電源を入れ直してください。"
                )
        except Exception:
            # 開きっぱなしで落ちると次の起動でポートを掴めなくなる
            self._serial.close()
            self._serial = None
            self._closed = True
            raise

    def start_scan(self):
        """接続してスキャンを開始する。失敗したら理由つきで例外にする。"""
        self.open()

        try:
            self.set_motor_rpm(round(self._scan_frequency_hz * 60))
            # モーターが目標回転数に乗るまで待つ（乗る前のパケットは角度が飛ぶ）
            time.sleep(0.5)

            if self._scan_mode == SCAN_MODE_EXPRESS:
                self._start_express()
            else:
                self._start_standard()
        except Exception:
            # 開きっぱなしで落ちると次の起動でポートを掴めなくなる
            self._serial.close()
            self._serial = None
            self._closed = True
            raise

        self._scanning = True
        print(f"Scan Started  ({self._port}, {self._scan_mode})")

    def stop_scan(self):
        """スキャンを止める（接続は保つ）。二重呼び出しは無害。"""
        if not self._scanning:
            return

        self._scanning = False

        try:
            self._send(CMD_STOP)
            time.sleep(0.05)
            self._serial.reset_input_buffer()
            self._rx.clear()
            self.set_motor_rpm(0)
        except (serial.SerialException, OSError):
            # 抜線などで書けなくても、閉じる方は続ける
            pass

        print(f"Scan Stopped  ({self._port})")

    def close(self):
        """接続を閉じる。二重呼び出しは無害。"""
        if self._closed:
            return

        if self._serial is not None:
            self._serial.close()
            self._serial = None

        self._closed = True

    #--- 1 回転の読み取り -------------------------------------------------

    def read_scan(self):
        """1 回転ぶんの点群を読む。揃わなかったときは空リストを返す。

        タイムアウトや一時的な復号ずれでは例外にせず空リストにして、呼び出し側の
        ループを続けさせる（T-mini Plus 版の read_scan() と同じ約束）。
        """
        deadline = time.monotonic() + SCAN_TIMEOUT_SEC

        while True:
            revolution = self._take_revolution()
            if revolution is not None:
                return revolution

            if time.monotonic() >= deadline:
                return []

            self._decode_more()

    def _take_revolution(self):
        """溜まっている点から 1 回転ぶんを切り出す。揃っていなければ None。

        self._points の先頭は常に回転の開始点なので、2 つ目の開始点が現れた
        ところで切る（その手前までが 1 回転）。

        起動直後は機体の開始フラグをまだ見ていないので、角度の巻き戻りで暫定的に
        回転を組み始める。その後フラグが来ると、暫定の切れ目（0 度）と本当の切れ目
        （実機では約 69 度）の間の端数が 1 回転として出てしまうので、フラグが
        使えると分かった時点でそれは捨てる。
        """
        while True:
            revolution = None

            for i in range(1, len(self._points)):
                if self._points[i]["_start"]:
                    revolution = self._points[:i]
                    self._points = self._points[i:]
                    break

            if revolution is None:
                return None

            # 巻き戻しで始めた端数で、かつフラグが使えると分かっている ＝ 捨てる
            if not revolution[0]["_start_from_flag"] and self._seen_start_flag:
                continue

            for point in revolution:
                del point["_start"]
                del point["_start_from_flag"]

            return revolution

    def _append(self, angle_deg, distance_mm, quality, start_flag):
        """復号した 1 点を溜める。回転の開始点より前の端数は捨てる。

        回転の切れ目は機体の開始フラグ（standard の S ビット / dense の sync ビット）で
        決める。**このフラグは角度 0 度で立つとは限らない**（実機の S1 firmware 1.28 では
        約 69 度で立つ）。角度の巻き戻りを切れ目に使うと、1 回転が
        「フラグ〜359 度」と「0 度〜フラグ」の 2 つに割れてしまう。

        巻き戻しによる切り出しは、フラグを**一度も見ていない**間だけの保険にしてある
        （フラグが来ない機体・モードでも回転が切れるように）。
        """
        wrapped = (
            self._last_angle is not None
            and angle_deg + 180.0 < self._last_angle
        )
        self._last_angle = angle_deg

        if start_flag:
            self._seen_start_flag = True
            start = True
        else:
            start = wrapped and not self._seen_start_flag

        # 起動直後の、途中から始まっている回転は捨てる
        if not self._points and not start:
            return

        # レンジ外は距離 0 にして「無効な点」に倒す（呼び出し側の判定に乗せる）
        if not (self._min_range_mm <= distance_mm <= self._max_range_mm):
            distance_mm = 0.0

        rad = math.radians(angle_deg)

        self._points.append({
            "angle": angle_deg,
            "distance": distance_mm,
            "intensity": quality,
            "flag": 0,          # RPLIDAR に干渉フラグは無い。キーだけ揃えてある
            "x": distance_mm * math.cos(rad),
            "y": distance_mm * math.sin(rad),
            "_start": start,
            "_start_from_flag": bool(start_flag),
        })

        if len(self._points) > MAX_BUFFERED_POINTS:
            # 回転の切れ目が一度も来ていない＝復号がずれている。溜め込まず捨て直す。
            # 開始フラグの記憶も落として、巻き戻しによる保険を効かせ直す
            self._points = []
            self._last_angle = None
            self._seen_start_flag = False
            self._prev_capsule = None

    def _decode_more(self):
        """シリアルから 1 パケット読んで復号し、self._points へ足す。"""
        if self._scan_mode == SCAN_MODE_EXPRESS:
            self._decode_dense_capsule()
        else:
            self._decode_standard_node()

    def _decode_standard_node(self):
        """standard スキャンの 5 バイトノードを 1 つ復号する。

            byte0: bit0 = S / bit1 = !S / bit2-7 = quality(6bit)
            byte1: bit0 = C（常に 1）/ bit1-7 = angle_q6 の下位 7bit
            byte2: angle_q6 の上位 8bit
            byte3-4: distance_q2（リトルエンディアン）

        S と !S・C は「そこがノードの先頭か」の検査にそのまま使える。成立しなければ
        1 バイト捨てて次の境界を探す（バイトを丸ごと読み飛ばさないので復号がずれない）。
        """
        if not self._fill(5):
            return

        raw = self._rx
        start = raw[0] & 0x01

        if start == ((raw[0] >> 1) & 0x01) or (raw[1] & 0x01) != 1:
            del raw[0]
            return

        quality = raw[0] >> 2
        angle_q6 = (raw[1] >> 1) | (raw[2] << 7)
        distance_q2 = raw[3] | (raw[4] << 8)
        del raw[:5]

        self._append(
            (angle_q6 / 64.0) % 360.0,
            distance_q2 / 4.0,
            quality,
            start,
        )

    def _decode_dense_capsule(self):
        """express（dense capsule）の 84 バイトパケットを 1 つ読む。

            byte0: 上位ニブル 0xA / 下位ニブル checksum の下位 4bit
            byte1: 上位ニブル 0x5 / 下位ニブル checksum の上位 4bit
            byte2-3: start_angle_sync_q6（bit15 = 回転の開始）
            byte4-83: 40 点ぶんの距離[mm]（uint16 リトルエンディアン）

        各 cabin に角度は入っておらず、**前後 2 パケットの開始角の差**を 40 等分して
        補間する。よって 1 パケット遅れて点が出る（self._prev_capsule）。
        """
        if not self._fill(DENSE_PACKET_LEN):
            return

        if (self._rx[0] >> 4 != DENSE_SYNC_1) or (self._rx[1] >> 4 != DENSE_SYNC_2):
            # 同期ニブルが立っていない。1 バイト捨てて次の境界を探す
            del self._rx[0]
            return

        raw = bytes(self._rx[:DENSE_PACKET_LEN])
        del self._rx[:DENSE_PACKET_LEN]

        expected = (raw[0] & 0x0F) | ((raw[1] & 0x0F) << 4)
        actual = 0
        for byte in raw[2:]:
            actual ^= byte

        if expected != actual:
            # チェックサム不一致。このパケットは捨て、角度の基準も切る
            self._prev_capsule = None
            return

        start_angle_sync_q6 = raw[2] | (raw[3] << 8)
        distances = struct.unpack_from("<40H", raw, 4)

        previous = self._prev_capsule
        self._prev_capsule = (start_angle_sync_q6, distances)

        if previous is None:
            # 1 つ目のパケットは角度の始点にしかならない（差分が取れない）
            return

        prev_sync_q6, prev_distances = previous
        prev_angle = (prev_sync_q6 & 0x7FFF) / 64.0
        current_angle = (start_angle_sync_q6 & 0x7FFF) / 64.0

        # 回転をまたぐと current < prev になるので 360 度足して差を正にする
        diff = (current_angle - prev_angle) % 360.0
        increment = diff / DENSE_CABIN_COUNT

        # 開始フラグは「そのパケットの 1 点目が回転の先頭」を意味する。
        # いま出すのは prev_distances なので、見るのも prev 側のフラグ
        prev_start = bool(prev_sync_q6 & 0x8000)

        for i, distance_mm in enumerate(prev_distances):
            self._append(
                (prev_angle + increment * i) % 360.0,
                float(distance_mm),
                # dense には quality が無い。SDK も固定値を入れているだけなので
                # 「判定に使えない」ことが分かるよう 0 で返す
                0,
                prev_start and i == 0,
            )

    def _fill(self, count, deadline=None):
        """受信バッファが count バイト以上になるまで読む。届かなければ False。"""
        if deadline is None:
            deadline = time.monotonic() + SERIAL_TIMEOUT_SEC

        while len(self._rx) < count:
            chunk = self._serial.read(count - len(self._rx))

            if chunk:
                self._rx += chunk
            elif time.monotonic() >= deadline:
                return False

        return True

    #--- コマンド ---------------------------------------------------------

    def set_motor_rpm(self, rpm):
        """モーターの目標回転数[rpm]を指定する。0 で停止。

        A シリーズ（DTR / アクセサリボードの PWM）ではなく、TOF 機の
        HQ_MOTOR_SPEED_CTRL（0xA8・rpm を uint16 で渡す）を使う。
        """
        self._send(CMD_HQ_MOTOR_SPEED_CTRL, struct.pack("<H", int(rpm)))

    def info(self):
        """機種情報を dict で返す（model / firmware / hardware / serial）。"""
        data = self._request(CMD_GET_INFO, ANS_TYPE_DEVINFO, 20)

        return {
            "model": data[0],
            "firmware": f"{data[2]}.{data[1]}",
            "hardware": data[3],
            "serial": data[4:20][::-1].hex().upper(),
        }

    def health(self):
        """自己診断結果を dict で返す（status / error_code）。"""
        data = self._request(CMD_GET_HEALTH, ANS_TYPE_DEVHEALTH, 3)

        return {"status": data[0], "error_code": data[1] | (data[2] << 8)}

    def get_conf(self, conf_type, payload=b""):
        """GET_LIDAR_CONF を 1 件引き、先頭 4 バイト（型のエコー）を除いた本体を返す。"""
        request = struct.pack("<I", conf_type) + payload
        data = self._request(CMD_GET_LIDAR_CONF, ANS_TYPE_LIDAR_CONF, None, request)

        if len(data) < 4:
            raise RuntimeError(
                f"LiDAR ({self._port}) の GET_LIDAR_CONF 応答が短すぎます"
                f"（{len(data)} バイト）"
            )

        return data[4:]

    def typical_scan_mode_id(self):
        """機体が推奨する express のスキャンモード id を返す（S1 は dense）。"""
        return struct.unpack("<H", self.get_conf(CONF_SCAN_MODE_TYPICAL)[:2])[0]

    def scan_modes(self):
        """機体が持つスキャンモードを一覧で返す（probe.py 用）。

        各要素は dict:
            {"id": 0, "name": "Standard", "us_per_sample": 200.0,
             "max_distance_m": 40.0, "ans_type": 0x81}

        ans_type が 0x81 なら standard の 5 バイトノード、0x86 なら dense capsule。
        それ以外（0x82 legacy capsule / 0x85 ultra capsule）はこのドライバでは読めない。
        """
        count = struct.unpack("<H", self.get_conf(CONF_SCAN_MODE_COUNT)[:2])[0]
        modes = []

        for mode_id in range(count):
            payload = struct.pack("<H", mode_id)
            name = self.get_conf(CONF_SCAN_MODE_NAME, payload)
            us_per_sample_q8 = struct.unpack(
                "<I", self.get_conf(CONF_SCAN_MODE_US_PER_SAMPLE, payload)[:4]
            )[0]
            max_distance_q8 = struct.unpack(
                "<I", self.get_conf(CONF_SCAN_MODE_MAX_DISTANCE, payload)[:4]
            )[0]

            modes.append({
                "id": mode_id,
                # 応答は NUL 終端の文字列
                "name": name.split(b"\x00")[0].decode("ascii", "replace"),
                "us_per_sample": us_per_sample_q8 / 256.0,
                "max_distance_m": max_distance_q8 / 256.0,
                "ans_type": self.get_conf(CONF_SCAN_MODE_ANS_TYPE, payload)[0],
            })

        return modes

    def _start_standard(self):
        """standard スキャン（0x20）を開始する。"""
        self._send(CMD_SCAN)
        length, _mode, ans_type = self._read_descriptor()

        if ans_type != ANS_TYPE_MEASUREMENT or length != 5:
            raise RuntimeError(
                f"LiDAR ({self._port}) が standard スキャンに応じません"
                f"（type=0x{ans_type:02X}, len={length}）。"
                "config.toml の [scan] mode を \"express\" にして試してください。"
            )

    def _start_express(self):
        """express スキャン（0x82・dense capsule）を開始する。

        モード id は config.toml で指定できるが、既定では機体に推奨値を訊く
        （S1 は dense capsule を返すモードが typical になっている）。
        """
        mode_id = self._mode_id
        if mode_id is None:
            mode_id = self.typical_scan_mode_id()

        # payload は working_mode(1) + reserved(4)
        self._send(CMD_EXPRESS_SCAN, struct.pack("<BI", mode_id, 0))
        length, _mode, ans_type = self._read_descriptor()

        if ans_type != ANS_TYPE_MEASUREMENT_DENSE_CAPSULED:
            raise RuntimeError(
                f"LiDAR ({self._port}) の express モード {mode_id} が返すのは "
                f"type=0x{ans_type:02X} で、このドライバが読める dense capsule "
                f"(0x{ANS_TYPE_MEASUREMENT_DENSE_CAPSULED:02X}) ではありません。"
                + (
                    "0x85 は ultra capsule で、この個体の DenseBoost がこれに当たります"
                    "（未対応）。[scan] mode を \"standard\" にしてください。"
                    if ans_type == ANS_TYPE_MEASUREMENT_CAPSULED_ULTRA
                    else "probe.py でモード一覧を確認して [scan] mode_id を指定するか、"
                         "[scan] mode を \"standard\" にしてください。"
                )
            )
        if length != DENSE_PACKET_LEN:
            raise RuntimeError(
                f"LiDAR ({self._port}) の dense capsule 長が想定外です"
                f"（{length} != {DENSE_PACKET_LEN}）"
            )

    #--- 低レベル入出力 ---------------------------------------------------

    def _send(self, cmd, payload=b""):
        """要求パケットを組み立てて送る。payload があればチェックサムを付ける。"""
        if payload:
            request = bytes([SYNC_BYTE, cmd, len(payload)]) + payload
            request += bytes([_checksum(request)])
        else:
            request = bytes([SYNC_BYTE, cmd])

        self._serial.write(request)
        self._serial.flush()

    def _request(self, cmd, expected_type, expected_length, payload=b""):
        """単発応答のコマンドを送り、本体バイト列を返す。

        expected_length が None なら、ディスクリプタが申告した長さをそのまま読む。
        """
        self._serial.reset_input_buffer()
        self._rx.clear()
        self._send(cmd, payload)

        length, _mode, ans_type = self._read_descriptor()

        if ans_type != expected_type:
            raise RuntimeError(
                f"LiDAR ({self._port}) の応答種別が想定外です"
                f"（0x{ans_type:02X} != 0x{expected_type:02X}）"
            )
        if expected_length is not None and length != expected_length:
            raise RuntimeError(
                f"LiDAR ({self._port}) の応答長が想定外です"
                f"（{length} != {expected_length}）"
            )

        data = self._read_exact(length, time.monotonic() + SCAN_TIMEOUT_SEC)
        if data is None:
            raise RuntimeError(
                f"LiDAR ({self._port}) の応答が途中で途切れました"
                f"（cmd=0x{cmd:02X}）"
            )

        return data

    def _read_descriptor(self):
        """応答ディスクリプタ 7 バイトを読み、(長さ, 送信モード, 種別) を返す。"""
        raw = self._read_exact(DESCRIPTOR_LEN, time.monotonic() + SCAN_TIMEOUT_SEC)

        if raw is None:
            raise RuntimeError(
                f"LiDAR ({self._port}) が応答しません。"
                "ポート・ボーレート（S1 は 256000）・給電を確認してください。"
            )
        if raw[0] != SYNC_BYTE or raw[1] != SYNC_BYTE2:
            raise RuntimeError(
                f"LiDAR ({self._port}) の応答ヘッダが不正です（{raw.hex()}）。"
                "ボーレートが合っていない可能性があります（S1 は 256000）。"
            )

        size_and_mode = struct.unpack("<I", raw[2:6])[0]

        return size_and_mode & 0x3FFFFFFF, size_and_mode >> 30, raw[6]

    def _read_exact(self, count, deadline):
        """count バイト揃うまで読む。期限までに揃わなければ None。"""
        buffer = bytearray()

        while len(buffer) < count:
            chunk = self._serial.read(count - len(buffer))
            if chunk:
                buffer += chunk
            elif time.monotonic() >= deadline:
                return None

        return bytes(buffer)
