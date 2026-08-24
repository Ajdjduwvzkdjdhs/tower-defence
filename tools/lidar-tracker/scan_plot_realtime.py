"""LiDAR の検出値をリアルタイムに極座標散布図へ描画する。

config.toml に書いた台数ぶんの LiDAR を同時に読み、1 つのウィンドウへ
上から順（LiDAR1, LiDAR2, …）に縦に並べて描く。台ごとに独立しているのは
センサー・点群バッファ・セクタカウンタだけで、回転数・検出範囲・計測セクタ・
フィルタといった設定は全台で共通。

全周を 360 個の角度ビンに分け、各ビンを「最新の距離」で更新し続ける。
物体がある方向のビンには距離が入り、物体が消えた（無反射／ノイズの）方向の
ビンは NaN でクリアされ点が消える。常に「直近 1 回転ぶんの最新点」が見える。

各点は距離と同時に Intensity（反射強度 0〜255）も保持しており、
COLOR_BY_INTENSITY = True のとき点の色として描かれる（カラーバー付き）。

構成（台数 + 1 スレッド）:
  - 読み取りスレッド : 台ごとに 1 本。read_scan() を回し、その台の距離／強度配列を
                       更新し続ける
  - メインスレッド    : FuncAnimation が一定間隔で全台の配列を読み、散布図を再描画

センサーとの通信は公式 SDK（YDLidar-SDK）が担当する。lidar.py の TMiniPlus が
その薄いラッパで、read_scan() は 1 回転ぶんの点をまとめて返す。

接続情報・回転数・検出範囲は config.toml から読み込む。
それ以外の描画パラメータは下の「設定」定数で調整する。

各プロットのタイトルには、どの台か（名前とポート）・設定した回転数・その回転数で
1 回転に取れる点数の理論値・直近 1 回転で実際に検出できた点数・セクタのカウンタが出る。

指定セクタ（既定 225〜315 度、図では薄く塗ってある）に点が現れた回数（立ち上がり）と、
そこに写った点数の累計は、起動した時点から台ごとに数えていて、各プロットの
タイトル 3 行目に出る。ウィンドウ下部のボタンは 0 に戻すだけ（全台同時）。
"""

import threading

import matplotlib.animation as animation
import matplotlib.pyplot as plt
import numpy as np
from matplotlib.widgets import Button

from config import load_detection_config, load_scan_config, load_serial_config
from lidar import TMiniPlus, max_points_per_rev

#=============================
# 設定
#=============================

# シリアルポート・ボーレートは config.toml から読み込む。
# PORTS は接続する LiDAR のポートのリストで、並び順がそのまま画面の上から下になる
# （先頭が LiDAR1）。1 台だけなら要素 1 つのリストになり、表示は 1 段になる。
PORTS, BAUDRATE = load_serial_config()

# 回転数[Hz]も config.toml の [scan] から読み込む（既定 10.0、対応範囲 5〜12）。
# サンプリングレートは一定なので、回転を速くすると 1 回転あたりの点数は反比例して減る。
# MAX_POINTS_PER_REV はその理論値で、実際の検出数はノイズ除去や無反射で必ずこれ以下。
SCAN_FREQUENCY_HZ = load_scan_config()
MAX_POINTS_PER_REV = max_points_per_rev(SCAN_FREQUENCY_HZ)

# 検出範囲（関心領域）の内外境界も config.toml の [detection] から読み込む。
# 設置場所ごとに変わる現物合わせの値なので、マシンごとの config.toml に置く。
# 外側 RANGE_MAX は「関心円」として赤線で描かれる。内側 RANGE_MIN は、センサー自身の
# 筐体・治具・支柱など常に至近距離で返ってくる反射を切るためのもの（0 なら切らない）。
# ここを外れた点は破棄する（FILTER_BY_RANGE）。
RANGE_MIN_CM, RANGE_MAX_CM = load_detection_config()
RANGE_MIN_MM = RANGE_MIN_CM * 10
RANGE_MAX_MM = RANGE_MAX_CM * 10

# 表示範囲 rmax = RANGE_MAX × 倍率。円外のコンテキストも少し見えるようにする。
# 大きくすると関心円が相対的に小さく、小さくすると円が画面いっぱいに寄る。
# FILTER_BY_RANGE=False にすると、この余白に範囲外の点が見えるので現物合わせに使える。
DISPLAY_SCALE = 1.2
DISPLAY_MAX_MM = RANGE_MAX_MM * DISPLAY_SCALE

# 現物合わせ用の角度オフセット[度]。センサー正面を画面上のどこに向けるか。
# 0 度＝画面上・時計回りを基準に、この値だけ表示全体を回転させる。全台に同じ値が効く。
ANGLE_OFFSET_DEG = 0.0

# 角度ビンの解像度[度]。1.0° → 360 ビン。
BIN_RESOLUTION_DEG = 1.0
NUM_BINS = int(round(360.0 / BIN_RESOLUTION_DEG))

# 描画の更新間隔[ms]（読み取りとは独立）
UPDATE_INTERVAL_MS = 50

#--- ウィンドウの大きさ[インチ] -----------------------------------------
# 縦は「1 台ぶんの高さ × 台数 + ボタン行」で決める。台数が増えても 1 枚あたりの
# 大きさとボタン行の高さが変わらないよう、割合ではなくインチで持つ。
FIGURE_WIDTH_IN = 7.0
PANEL_HEIGHT_IN = 4.6      # プロット 1 枚ぶん（3 行のタイトルを含む）
TOP_MARGIN_IN = 0.85        # 1 枚目の 3 行タイトルが収まるだけの厚み
BOTTOM_ROW_HEIGHT_IN = 0.9  # 下端の Start/Reset ボタン行
PANEL_GAP_RATIO = 0.45      # プロット間の余白（軸の高さに対する比）。
                            # 2 枚目以降のタイトルはこの隙間に入るので詰めすぎない

#--- Intensity（反射強度）の表示 -----------------------------------------
# T-mini Pro 開発マニュアル 3.1.2 Intensity Analysis:
#   Intensity(i) = Intensity[7:0] — サンプル Si の 1 バイト目に格納され、値域は 0〜255。
# SDK が LaserPoint.intensity として復元した値を、lidar.py がそのまま
# samples[i]["intensity"] に入れている。
# ここではその値を散布図の色として描く（強度そのものはフィルタに使っていない）。
COLOR_BY_INTENSITY = True          # False にすると従来どおり単色（青）
INTENSITY_MIN = 0                  # カラースケールの下限（マニュアルの値域）
INTENSITY_MAX = 255                # カラースケールの上限（同上）
INTENSITY_COLORMAP = "viridis"     # 低強度＝紫、高強度＝黄

#--- 計測セクタ（ボタンで数える範囲）-----------------------------------
# 図に見えているとおりの角度[度]で指定する（ANGLE_OFFSET_DEG 適用後の表示角）。
# 0 度をまたぐ指定（例 315 → 45）もできる。全台に同じセクタが効く。
COUNT_SECTOR_START_DEG = 225.0
COUNT_SECTOR_END_DEG = 315.0

# 何回転ぶん連続で「点が無い」が続いたら、次の検出を新しい 1 回と数えるか。
# 落ちる氷が点滅して 1 回の落下が 2 回に割れるのを防ぐ。10Hz なら 2 回転 = 0.2 秒。
COUNT_RELEASE_ROTATIONS = 2

#--- フィルタの ON/OFF（現物合わせで切り替える）-------------------------
# AMBIENT / SPECULAR は SDK 側（干渉フラグを見られるのは SDK の中だけ）、
# それ以外は受信後の _is_valid() で効く。除外された点はどちらも同じ扱いで、
# そのビンが NaN にクリアされる（SDK が落とした点は距離 0 で返るため）。
FILTER_ZERO_DISTANCE = True        # 距離 0（無反射・SDK が落とした点）を除外
FILTER_AMBIENT = True              # 外乱光（太陽光ノイズ）を SDK に除外させる
FILTER_SPECULAR = False            # 鏡面反射（ガラスノイズ）を SDK に除外させる
FILTER_BY_RANGE = True             # RANGE_MIN..RANGE_MAX の外を除外

#--- タイトルに出す固定文言（全台共通。上の設定から組み立てる）-----------
if FILTER_BY_RANGE:
    RANGE_LABEL = f"range = {RANGE_MIN_CM}-{RANGE_MAX_CM} cm"
else:
    RANGE_LABEL = f"range filter OFF, circle = {RANGE_MAX_CM} cm"

SECTOR_LABEL = f"{COUNT_SECTOR_START_DEG:g}-{COUNT_SECTOR_END_DEG:g}"


class ScanBuffer:
    """全周 360 ビンの最新値を保持する共有バッファ（スレッド間で共有）。

    LiDAR 1 台につき 1 つ持つ。距離[mm]と Intensity[0-255] を同じインデックスの
    2 本の配列で持ち、無反射／ノイズ／未計測のビンは、どちらの配列も NaN。
    """

    def __init__(self, num_bins):
        self._lock = threading.Lock()
        self._distances = np.full(num_bins, np.nan, dtype=float)
        self._intensities = np.full(num_bins, np.nan, dtype=float)
        self._num_bins = num_bins
        # 直近 1 回転で有効だった点の数（ビンではなく点の数。同じビンに複数の点が
        # 入ることがあるので、画面上の点の数とは必ずしも一致しない）
        self._valid_count = 0

    def update(self, samples):
        """1 パケットぶんの samples でビンを更新する。

        有効な点はその距離・強度で上書きし、無効な点（無反射・ノイズ）は
        そのビンを NaN でクリアする。こうすることで、ビームがその方向を
        通過したのに物体が無ければ、残っていた古い点が消える。

        あわせて、この 1 回転で有効だった点の数を数えて持っておく（タイトル表示用）。
        """
        valid_count = 0

        with self._lock:
            for p in samples:
                idx = int(p["angle"] // BIN_RESOLUTION_DEG) % self._num_bins

                if _is_valid(p):
                    self._distances[idx] = p["distance"]
                    self._intensities[idx] = p["intensity"]
                    valid_count += 1
                else:
                    self._distances[idx] = np.nan
                    self._intensities[idx] = np.nan

            self._valid_count = valid_count

    def snapshot(self):
        """(距離配列, 強度配列, 直近 1 回転の有効点数) を返す。

        3 つは必ず同じ瞬間のものを一度のロックで取る（表示のズレを防ぐため）。
        配列はコピーを返す。
        """
        with self._lock:
            return (
                self._distances.copy(),
                self._intensities.copy(),
                self._valid_count,
            )


class SectorCounter:
    """計測セクタの検出回数と点数を数える（スレッド間で共有）。

    LiDAR 1 台につき 1 つ持つ（数える範囲の定義は全台共通だが、数えるのは
    その台に写った点だけ）。1 回転ごとに「セクタ内にあった有効な点の数」を
    受け取り、3 つを持つ:

      - 回数 : 無し → 有り に変わった瞬間だけ 1 回。氷が 1 個落ちて数回転
               映り続けても 1 回になる。点滅で 1 回が 2 回に割れないよう、
               release_rotations 回転ぶん連続で無くなるまでは「まだ同じ検出が
               続いている」とみなす。
      - 点数 : セクタに写った点の累計。1 回の検出がどれだけ濃かったか
               （何点で写ったか）を見るための値なので、回数と違って
               映っている間ずっと積み上がる。
      - 直近 : 最後の 1 回転でセクタ内にあった点の数。累計と違って増減するので、
               「そもそもセクタに点が入ってきているのか」がその場で分かる
               （回数が増えないときの切り分け用）。

    起動した時点から数え始める。ボタンはリセット専用で、押すと 0 に戻して
    数え直す（＝1 回の計測の区切り）。「押すまで数えない」にはしていない。
    押し忘れると何も数えず、それが「カウントが動かない」に見えるため。
    """

    def __init__(self, release_rotations):
        self._lock = threading.Lock()
        self._release_rotations = release_rotations
        self._count = 0
        self._points = 0        # セクタに写った点の累計
        self._last = 0          # 直近 1 回転でセクタ内にあった点の数
        self._present = False   # いま検出中とみなしているか
        self._absent = 0        # 連続して「無し」だった回転数

    def reset(self):
        """0 に戻して数え直す（1 回の計測の区切り）。"""
        with self._lock:
            self._count = 0
            self._points = 0
            self._last = 0
            self._present = False
            self._absent = 0

    def feed(self, point_count):
        """1 回転ぶんの「セクタ内の有効点数」を渡す。読み取りスレッドから呼ぶ。"""
        with self._lock:
            self._points += point_count
            self._last = point_count

            if point_count > 0:
                if not self._present:
                    self._count += 1
                self._present = True
                self._absent = 0
            elif self._present:
                self._absent += 1
                if self._absent >= self._release_rotations:
                    self._present = False

    def snapshot(self):
        """(回数, 点数, 直近 1 回転の点数, いま検出中か) を一度のロックで返す。"""
        with self._lock:
            return self._count, self._points, self._last, self._present


def sector_spans():
    """計測セクタを [(開始角, 終了角), ...] で返す（表示角[度]）。

    0 度をまたぐ指定は 2 つに割れる。描画にも判定にも同じ定義を使う。
    """
    start = COUNT_SECTOR_START_DEG % 360.0
    end = COUNT_SECTOR_END_DEG % 360.0

    if start <= end:
        return [(start, end)]

    return [(start, 360.0), (0.0, end)]


def _in_count_sector(angle_deg):
    """センサー角度が計測セクタに入るか。

    セクタは図に見えているとおりの角度で指定してあるので、ANGLE_OFFSET_DEG で
    表示を回している場合も画面と一致するよう、表示角に直してから判定する。
    """
    display = (angle_deg + ANGLE_OFFSET_DEG) % 360.0

    return any(lo <= display <= hi for lo, hi in sector_spans())


def _sector_point_count(samples):
    """この 1 回転で、計測セクタ内にあった有効な点の数。

    有効判定は画面の点と同じ _is_valid() を使う。そうしないと、関心円の外にある
    壁や範囲外のノイズでも数えてしまう。
    """
    return sum(1 for p in samples if _is_valid(p) and _in_count_sector(p["angle"]))


def _is_valid(p):
    """フィルタ設定に照らして、その点を「有効な物体点」とみなすか。

    外乱光・鏡面反射は SDK 側で距離 0 に落とされてここへ来るので、
    FILTER_ZERO_DISTANCE の一本で拾える。

    無効と判定された点はそのビンを NaN でクリアする（update 参照）。距離での棄却も
    同じ扱いにしてあるので、「関心円の外に壁がある方向」は点が出ない＝
    その方向の関心領域内には何も無い、と読める。
    """
    if FILTER_ZERO_DISTANCE and p["distance"] == 0:
        return False
    if FILTER_BY_RANGE and not (RANGE_MIN_MM <= p["distance"] <= RANGE_MAX_MM):
        return False
    return True


def reader_loop(name, lidar, buffer, counter, stop_event):
    """読み取りスレッド本体。停止フラグが立つまで 1 回転ぶんずつ読み続ける。

    LiDAR 1 台につき 1 本走る。エラーはその台のループを抜けるだけで、
    他の台の表示は動き続ける（どの台が落ちたか分かるよう name を出す）。
    """
    while not stop_event.is_set():
        try:
            samples = lidar.read_scan()
        except Exception as e:
            if not stop_event.is_set():
                print(f"[{name}] read_scan error: {e}")
            break

        # 空 = SDK が 1 回転ぶんを揃えられなかった（タイムアウト・再接続中）。
        # 次の回転で戻ってくるので、表示はそのままにして読み続ける。
        if samples:
            buffer.update(samples)
            counter.feed(_sector_point_count(samples))


class LidarPanel:
    """LiDAR 1 台ぶんの一式（センサー・共有バッファ・カウンタ・自分のプロット）。

    台数ぶん作って縦に並べる。台ごとに独立しているのはこの中身だけで、
    設定（回転数・検出範囲・計測セクタ・フィルタ）は全台で共通のモジュール定数。

    寿命は main() が持つ:  start() → build() → refresh() を毎フレーム → stop()。
    """

    def __init__(self, name, port, baudrate):
        self.name = name
        self.port = port
        self.buffer = ScanBuffer(NUM_BINS)
        self.counter = SectorCounter(COUNT_RELEASE_ROTATIONS)

        # コンストラクタは SDK に設定を積むだけで、まだ接続しない
        # （接続は start() の start_scan()）。
        # 干渉フラグを見られるのは SDK の中だけなので、この 2 つは SDK へ渡す
        self._lidar = TMiniPlus(
            port,
            baudrate,
            filter_sun_noise=FILTER_AMBIENT,
            filter_glass_noise=FILTER_SPECULAR,
            scan_frequency_hz=SCAN_FREQUENCY_HZ,
        )

        self._thread = None
        self._ax = None
        self._scat = None
        self._theta = None

    #--- センサー ---------------------------------------------------------

    def start(self, stop_event):
        """接続してスキャンを開始し、この台の読み取りスレッドを走らせる。"""
        self._lidar.start_scan()

        self._thread = threading.Thread(
            target=reader_loop,
            args=(self.name, self._lidar, self.buffer, self.counter, stop_event),
            daemon=True,
        )
        self._thread.start()

    def stop(self):
        """読み取りスレッドの終了を待ってから、スキャンを止めて接続を閉じる。

        呼ぶ前に stop_event を立てておくこと（立てないとスレッドが抜けない）。
        start() 前・二重呼び出しのどちらでも無害。
        """
        if self._thread is not None:
            # SDK の 1 回転待ちは最大 1 秒。読み取りスレッドがその中にいる間に
            # turnOff すると危ないので、余裕を持って抜けきるのを待つ
            self._thread.join(timeout=3.0)
            self._thread = None

        self._lidar.stop_scan()
        self._lidar.close()

    #--- 描画 -------------------------------------------------------------

    def build(self, fig, ax):
        """自分の極座標プロットを組み立てる（1 度だけ呼ぶ）。"""
        self._ax = ax

        ax.set_theta_zero_location("N")    # 0 度を画面上（真上）に
        ax.set_theta_direction(-1)         # 時計回り
        ax.set_rlabel_position(135)

        # 各ビンの中心角[ラジアン]（オフセットで表示全体を回転）。以後固定。
        bin_centers_deg = (np.arange(NUM_BINS) + 0.5) * BIN_RESOLUTION_DEG
        self._theta = np.radians(bin_centers_deg + ANGLE_OFFSET_DEG)

        # 物体点（散布図）。r と色は毎フレーム差し替える。NaN のビンは描画されない。
        if COLOR_BY_INTENSITY:
            # vmin/vmax を明示するので、全ビンが NaN の初期状態でも色スケールは固定。
            self._scat = ax.scatter(
                self._theta,
                np.full(NUM_BINS, np.nan),
                s=8,
                c=np.full(NUM_BINS, np.nan),
                cmap=INTENSITY_COLORMAP,
                vmin=INTENSITY_MIN,
                vmax=INTENSITY_MAX,
            )
            # カラーバーは台ごとに付ける。共有にすると軸の幅が台ごとに変わって
            # 上下のプロットの大きさが揃わない。
            fig.colorbar(self._scat, ax=ax, pad=0.1, label="Intensity (0-255)")
        else:
            self._scat = ax.scatter(
                self._theta, np.full(NUM_BINS, np.nan), s=8, c="tab:blue"
            )

        # 計測セクタ（ボタンで数える範囲）を薄く塗って、どこを数えているか見せる。
        for lo, hi in sector_spans():
            sector_theta = np.radians(np.linspace(lo, hi, 91))
            ax.fill_between(
                sector_theta,
                0,
                DISPLAY_MAX_MM,
                color="tab:blue",
                alpha=0.08,
            )

        # 検出範囲の境界線を重ねる。外側＝赤、内側＝橙（RANGE_MIN=0 のときは描かない）。
        circle_theta = np.linspace(0, 2 * np.pi, 361)
        ax.plot(
            circle_theta,
            np.full_like(circle_theta, RANGE_MAX_MM),
            color="tab:red",
            linewidth=1.0,
            alpha=0.7,
        )
        if RANGE_MIN_MM > 0:
            ax.plot(
                circle_theta,
                np.full_like(circle_theta, RANGE_MIN_MM),
                color="tab:orange",
                linewidth=1.0,
                alpha=0.7,
            )

        # 全アーティストを追加した後に表示範囲を固定する。
        # 先に set_rmax しても、後から plot するとオートスケールが rmax を
        # 描画データ（赤円 = RADIUS）に合わせ直してしまい DISPLAY_SCALE が効かない。
        ax.set_rlim(0, DISPLAY_MAX_MM)
        ax.autoscale(False)

        self._set_title(0, self.counter.snapshot())

    def refresh(self):
        """最新の点群でこの台のプロットを描き替える。毎フレーム呼ぶ。"""
        distances, intensities, detected = self.buffer.snapshot()

        self._scat.set_offsets(np.column_stack((self._theta, distances)))
        if COLOR_BY_INTENSITY:
            self._scat.set_array(intensities)

        self._set_title(detected, self.counter.snapshot())

        return self._scat

    def _set_title(self, detected, count_state):
        """3 行のタイトルを書き替える（どの台か / 点の出方 / セクタのカウンタ）。

        台が増えるとボタン横の 1 行には収まらないので、カウンタもここに出す。
        数字は桁が変わってもタイトルが揺れないよう幅を固定する。
        """
        count, points, last, present = count_state

        # now は直近 1 回転の点数。hits が増えないときに、セクタに点が入って
        # いないのか（now も 0）、入っているのに立ち上がらないのか（now > 0）を
        # その場で切り分けるために出している。
        marker = "DETECTING" if present else ""
        count_line = (
            f"{SECTOR_LABEL} deg:"
            f"  {count:4d} hits"
            f"  {points:6d} pts"
            f"  now {last:3d}  {marker}"
        )

        self._ax.set_title(
            f"{self.name}   {self.port}   ({RANGE_LABEL})\n"
            f"{SCAN_FREQUENCY_HZ:.1f} Hz"
            f"   max {MAX_POINTS_PER_REV} pts/rev"
            f"   detected {detected:4d} pts\n"
            f"{count_line}",
            fontsize=10,
        )



def main():
    # config.toml のポートの並び順がそのまま LiDAR1, LiDAR2 …（＝上から下）になる
    panels = [
        LidarPanel(f"LiDAR{i + 1}", port, BAUDRATE)
        for i, port in enumerate(PORTS)
    ]

    stop_event = threading.Event()

    # 1 台でも開けなければ、先に開いた台を閉じてから投げ直す。
    # 開きっぱなしのまま落ちると、次の起動でそのポートを掴めなくなる。
    started = []
    try:
        for panel in panels:
            panel.start(stop_event)
            started.append(panel)
    except Exception:
        stop_event.set()
        for panel in started:
            panel.stop()
        raise

    # --- 描画の準備 ---
    # 台数ぶん縦に積む。ウィンドウの高さも台数に比例させて、1 枚あたりの大きさが
    # 台数で変わらないようにする。
    figure_height_in = (
        PANEL_HEIGHT_IN * len(panels) + TOP_MARGIN_IN + BOTTOM_ROW_HEIGHT_IN
    )
    fig = plt.figure(figsize=(FIGURE_WIDTH_IN, figure_height_in))

    # 余白は割合ではなくインチ換算で指定する（台数が増えても上下の余白は同じ厚み）。
    # タイトルが 3 行あるぶん上に、ボタンのぶん下に取る。毎フレーム書き換えるので、
    # constrained_layout のような都度計算だと軸がわずかに動く。
    fig.subplots_adjust(
        top=1.0 - TOP_MARGIN_IN / figure_height_in,
        bottom=BOTTOM_ROW_HEIGHT_IN / figure_height_in,
        hspace=PANEL_GAP_RATIO,
    )

    manager = fig.canvas.manager
    if manager is not None:
        manager.set_window_title("LiDAR realtime")

    for i, panel in enumerate(panels):
        panel.build(fig, fig.add_subplot(len(panels), 1, i + 1, projection="polar"))

    # --- 計測セクタのカウンタ UI ---
    # カウントは起動時から動いている。ボタンはリセット専用で、1 つで全台ぶんを
    # 0 に戻す（同じ落下を各台がどう捉えたか比べたいので、台ごとにばらばらの
    # 区切りで数えても読めない）。
    # Button はローカル変数で参照を持ち続けないと GC されて反応しなくなる。
    button_ax = fig.add_axes((
        0.03,
        0.25 / figure_height_in,
        0.20,
        0.40 / figure_height_in,
    ))
    reset_button = Button(button_ax, "Reset counts")

    def reset_counts(_event):
        for panel in panels:
            panel.counter.reset()

    reset_button.on_clicked(reset_counts)

    fig.text(
        0.26,
        0.45 / figure_height_in,
        "counting from launch; this zeroes all LiDARs (counts are in each title)",
        va="center",
        fontsize=9,
    )

    def update(_frame):
        return [panel.refresh() for panel in panels]

    ani = animation.FuncAnimation(
        fig,
        update,
        interval=UPDATE_INTERVAL_MS,
        blit=False,
        cache_frame_data=False,
    )

    def shutdown():
        # 二重呼び出しに備えてフラグで一度きりに
        if stop_event.is_set():
            return
        stop_event.set()
        for panel in panels:
            panel.stop()

    # ウィンドウを閉じたら安全終了
    fig.canvas.mpl_connect("close_event", lambda _e: shutdown())

    try:
        plt.show()
    except KeyboardInterrupt:
        print()
        print("Interrupted")
    finally:
        # Ctrl+C・ウィンドウを閉じる、どちらでも必ず停止処理を通す
        shutdown()


if __name__ == "__main__":
    main()
