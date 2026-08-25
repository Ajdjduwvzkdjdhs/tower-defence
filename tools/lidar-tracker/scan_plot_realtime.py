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

各点は干渉フラグ（flag）も持つ。FILTER_AMBIENT を切っているとき、SDK が
「太陽光ノイズ」と判定した点（flag = 0x03）も捨てられずに届くので、その数を
左の欄の最下段に出す（＝フィルタを切ったことで増えた点の数）。数える範囲は
hits/pts と同じ（同じセクタ・同じ有効判定の点から数える）。

構成（台数 + 1 スレッド）:
  - 読み取りスレッド : 台ごとに 1 本。read_scan() を回し、その台の距離／強度配列を
                       更新し続ける
  - メインスレッド    : FuncAnimation が一定間隔で全台の配列を読み、散布図を再描画

センサーとの通信は公式 SDK（YDLidar-SDK）が担当する。lidar.py の TMiniPlus が
その薄いラッパで、read_scan() は 1 回転ぶんの点をまとめて返す。

接続情報・回転数・検出範囲は config.toml から読み込む。
それ以外の描画パラメータは下の「設定」定数で調整する。

各プロットのタイトルには、どの台か（名前とポート）・設定した回転数・その回転数で
1 回転に取れる点数の理論値・直近 1 回転で実際に検出できた点数が出る。

指定セクタ（既定 225〜315 度、図では薄く塗ってある）で検出された回数（立ち上がり）と
点数の累計は、起動した時点から台ごとに数えていて、ウィンドウ左の欄にその台の
プロットと同じ高さで大きく出る（離れた所から読めるように、タイトルではなく左）。

数え方は 2 モードあり、ウィンドウ下部の "Mode" ボタンで全台まとめて切り替える:
  - simple     : セクタ内に有効な点があれば「有り」。セクタに静止物が写らない前提の、
                 いちばん素直な数え方（既定）
  - background : 起動直後にビンごとの静止背景を学習し、そこより手前を通った点だけを
                 「有り」とする。セクタに壁・床・機体が写っていて simple では hits が
                 1 で張り付くときに使う（灰色の破線がそのしきい線）

"Reset counts" ボタンはカウンタを 0 に戻す（全台同時。background では背景も学習し直す）。
"""

import threading

import matplotlib.animation as animation
import matplotlib.pyplot as plt
import numpy as np
from matplotlib.widgets import Button

from config import load_detection_config, load_scan_config, load_serial_config
from lidar import FLAG_SUN_NOISE, TMiniPlus, max_points_per_rev

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
# matplotlib の位置指定はすべて「図に対する割合」なので、窓を広げると余白も
# ボタンも一緒に間延びする。ここではすべてインチで持ち、リサイズのたびに
# apply_layout() が割合へ換算し直す（＝窓を広げても各部の実寸は変わらない）。
FIGURE_WIDTH_IN = 10.0
PLOT_AREA_WIDTH_IN = 6.0    # プロット＋カラーバーに使う幅（左の欄の右どなり）
PANEL_HEIGHT_IN = 4.6       # プロット 1 枚ぶん（2 行のタイトルを含む）
TOP_MARGIN_IN = 0.75        # 1 枚目の 2 行タイトルが収まるだけの厚み
BOTTOM_ROW_HEIGHT_IN = 0.9  # 下端のボタン行
PANEL_GAP_RATIO = 0.45      # プロット間の余白（軸の高さに対する比）。
                            # 2 枚目以降のタイトルはこの隙間に入るので詰めすぎない

#--- 下端のボタン行[インチ] ----------------------------------------------
BUTTON_BOTTOM_IN = 0.28
BUTTON_HEIGHT_IN = 0.36
BUTTON_LEFT_IN = 0.35        # 左端から Reset ボタンまで
BUTTON_GAP_IN = 0.15         # ボタン同士の間隔
RESET_BUTTON_WIDTH_IN = 1.5
MODE_BUTTON_WIDTH_IN = 1.9
HELP_TEXT_LEFT_IN = 4.1      # ボタンの右に置く説明文
HELP_TEXT_BOTTOM_IN = 0.46

#--- 左のカウンタ欄 ------------------------------------------------------
# セクタのカウンタは、タイトルの小さな 1 行ではなくウィンドウ左端に大きく出す
# （計測中に離れた所から読むため）。台ごとに 1 ブロックで、その台のプロットと
# 同じ高さに置く。プロットはこの幅ぶん右に寄る。
LEFT_COLUMN_WIDTH_IN = 2.9   # プロットの左に空ける、カウンタ用の幅
COUNT_TEXT_LEFT_IN = 0.35    # ウィンドウ左端からカウンタ文字までの距離

# プロットの縦中心からの高さ[インチ]。3 つのブロックが重ならない間隔にする。
COUNT_HEAD_OFFSET_IN = 1.05    # 台名とセクタ・モード
COUNT_VALUE_OFFSET_IN = 0.15   # hits / pts（大きい数字）
COUNT_NOW_OFFSET_IN = -0.85    # 直近 1 回転の点数、または学習の進み具合
COUNT_FLAG_OFFSET_IN = -1.45   # 太陽光フラグの点数（全周が対象）

COUNT_HEAD_FONTSIZE = 13
COUNT_VALUE_FONTSIZE = 26
COUNT_NOW_FONTSIZE = 11
COUNT_FLAG_FONTSIZE = 11

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

#--- 数え方のモード（"Mode" ボタンで全台まとめて切り替える）----------------
# 既定は simple。2 つの違いは SectorCounter の docstring を参照。
COUNT_MODE_SIMPLE = "simple"           # セクタ内に点があれば「有り」
COUNT_MODE_BACKGROUND = "background"   # 静止背景より手前の点だけ「有り」

# background モードのパラメータ。
# margin は「背景のばらつき」と「拾いたい氷の手前具合」の折り合い。大きくすると
# 背景の揺れを拾わなくなるが、背景ぎりぎりを通る氷も落とす。小さくするとその逆。
COUNT_BACKGROUND_ROTATIONS = 20    # 学習に使う回転数（10Hz なら 2 秒）。この間は数えない
COUNT_FOREGROUND_MARGIN_MM = 100   # 背景より何 mm 手前なら「前を通った」とみなすか

#--- フィルタの ON/OFF（現物合わせで切り替える）-------------------------
# AMBIENT / SPECULAR は SDK 側（干渉フラグを見られるのは SDK の中だけ）、
# それ以外は受信後の _is_valid() で効く。除外された点はどちらも同じ扱いで、
# そのビンが NaN にクリアされる（SDK が落とした点は距離 0 で返るため）。
#
# AMBIENT / SPECULAR は SDK の既定がどちらも false（CYdLidar.h の
# m_SunNoise / m_GlassNoise）。true にするのは「点を捨てる」方向なので、
# 落氷を取りこぼしたくないここでは既定のまま両方 false にしてある。
# 屋外・窓際で使って偽の点が増えるようなら true に倒す。
FILTER_ZERO_DISTANCE = True        # 距離 0（無反射・SDK が落とした点）を除外
FILTER_AMBIENT = False             # 外乱光（太陽光ノイズ）を SDK に除外させる
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

    LiDAR 1 台につき 1 つ持つ。数える範囲（COUNT_SECTOR_*）とモードは全台で共通だが、
    数えるのはその台に写った点だけなので、状態は台ごとに独立している。

    読み取りスレッドが 1 回転ぶんの samples を feed() で流し込み、メインスレッドが
    snapshot() で表示用の値を取る。持っている数字は 3 つ:

      - 回数 : 無し → 有り に変わった瞬間だけ 1 回。氷が 1 個落ちて数回転
               映り続けても 1 回になる。点滅で 1 回が 2 回に割れないよう、
               release_rotations 回転ぶん連続で無くなるまでは「まだ同じ検出が
               続いている」とみなす。
      - 点数 : 「有り」と数えた点の累計。1 回の検出がどれだけ濃かったか
               （何点で写ったか）を見るための値なので、回数と違って
               映っている間ずっと積み上がる。
      - 直近 : 最後の 1 回転で「有り」と数えた点の数。累計と違って増減するので、
               「そもそもセクタに点が入ってきているのか」がその場で分かる
               （回数が増えないときの切り分け用）。

    これとは別に、干渉フラグが 0x03（太陽光ノイズ）だった点も数える。上の 3 つと
    まったく同じ点の集合（_sector_points の結果）から数えるので、セクタも有効判定も
    必ず一致する。FILTER_AMBIENT を切ったことで増えた点がどれだけあるかを見るための
    数字なので、モードにも background の学習中かどうかにも左右されず、常に数える。
    FILTER_AMBIENT が True のときは SDK が該当点を距離 0 に落とすので必ず 0 になる。

    「有り」の判定は 2 モードあり、ボタンで切り替える:

      simple     : セクタ内に有効な点があれば、それで「有り」。セクタに静止物が
                   写らない前提の、いちばん素直な数え方（既定）。
      background : セクタに壁・床・機体のような静止物が入っていると、そのビンには
                   毎回転ずっと点が入る。simple だと最初の 1 回で立ち上がりが
                   張り付き、以後どれだけ氷が落ちても増えない（hits = 1 のまま
                   pts だけ増える形で出る）。落ちる氷は静止物より手前を通って
                   背景を遮るので、learn_rotations 回転ぶん背景をビンごとに学習し、
                   そこから margin_mm 以上手前の点だけを「有り」とする。背景は
                   ビンごとの距離の中位値なので、学習中に氷が 1 個落ちても
                   引っぱられにくい。学習中に何も写らなかったビンは背景なしとして
                   扱い、そこへ来た点はすべて「有り」（何も無い方向に来たものは、
                   それ自体が検出）。

    起動した時点から数え始める。ボタンはリセット専用で、押すと 0 に戻して
    数え直す（＝1 回の計測の区切り）。「押すまで数えない」にはしていない。
    押し忘れると何も数えず、それが「カウントが動かない」に見えるため。
    """

    def __init__(self, num_bins, release_rotations, learn_rotations, margin_mm):
        self._lock = threading.Lock()
        self._num_bins = num_bins
        self._release_rotations = release_rotations
        self._learn_rotations = learn_rotations
        self._margin_mm = margin_mm
        self._mode = COUNT_MODE_SIMPLE
        # スレッドが走り出す前なので、ここだけロック無しで初期化してよい
        self._clear()

    #--- 読み取りスレッドから呼ぶ -----------------------------------------

    def feed(self, samples):
        """1 回転ぶんの samples を渡す。

        新しい検出が立ち上がった回転だけ True を返す。画面を見ていなくても
        数えられているか分かるよう、呼び出し側がログに出すために使う。
        background の学習中は数えないので、必ず False。
        """
        points = _sector_points(samples)
        flagged = sum(1 for _, _, flag in points if flag == FLAG_SUN_NOISE)

        with self._lock:
            self._flagged = flagged
            self._flagged_total += flagged

            if self._is_learning():
                self._learn(points)

                return False

            return self._accumulate(self._hit_count(points))

    #--- メインスレッドから呼ぶ -------------------------------------------

    def reset(self):
        """0 に戻して数え直す（1 回の計測の区切り）。背景も学習し直す。

        背景も引き直すのは、置き直し・氷の山の変化のあとに古い背景で数え続けると、
        あるはずの検出が出ない／無いはずの検出が出るのに気づけないため。
        """
        with self._lock:
            self._clear()

    def set_mode(self, mode):
        """数え方を切り替える。前のモードの数字と混ざって読めなくなるので 0 に戻す。"""
        with self._lock:
            self._mode = mode
            self._clear()

    def snapshot(self):
        """表示に要る値をまとめて返す。

        (モード, 回数, 点数, 直近 1 回転の点数, 検出中か, 学習の残り回転数,
         直近 1 回転のフラグ点数, フラグ点数の累計)

        表示がズレないよう、すべて一度のロックで取る。
        """
        with self._lock:
            remaining = 0
            if self._is_learning():
                remaining = self._learn_rotations - self._learned

            return (
                self._mode,
                self._count,
                self._points,
                self._last,
                self._present,
                remaining,
                self._flagged,
                self._flagged_total,
            )

    def threshold(self):
        """ビンごとのしきい距離[mm]（背景 - margin）の配列を返す（描画用）。

        simple のとき・学習中・背景が無かったビンは NaN なので、線は描かれない。
        """
        with self._lock:
            if self._mode == COUNT_MODE_SIMPLE:
                return np.full(self._num_bins, np.nan)

            return self._background - self._margin_mm

    #--- 内部（すべてロックを持ったまま呼ぶ）------------------------------

    def _clear(self):
        """カウンタと背景をまっさらにする。"""
        self._count = 0
        self._points = 0
        self._last = 0
        self._present = False   # いま検出中とみなしているか
        self._absent = 0        # 連続して「無し」だった回転数
        self._flagged = 0       # 直近 1 回転で flag=0x03 だった点の数
        self._flagged_total = 0 # その累計
        self._background = np.full(self._num_bins, np.nan, dtype=float)
        self._observed = [[] for _ in range(self._num_bins)]
        self._learned = 0

    def _is_learning(self):
        """background で、まだ背景を学習し終えていないか。"""
        return (
            self._mode == COUNT_MODE_BACKGROUND
            and self._learned < self._learn_rotations
        )

    def _hit_count(self, points):
        """この 1 回転で「有り」と数える点の数（モードで判定が変わる）。"""
        if self._mode == COUNT_MODE_SIMPLE:
            return len(points)

        return sum(
            1 for idx, distance, _ in points if self._is_foreground(idx, distance)
        )

    def _accumulate(self, hit_count):
        """回数・点数を 1 回転ぶん進める。立ち上がった回転だけ True。"""
        self._points += hit_count
        self._last = hit_count
        started = False

        if hit_count > 0:
            if not self._present:
                self._count += 1
                started = True
            self._present = True
            self._absent = 0
        elif self._present:
            self._absent += 1
            if self._absent >= self._release_rotations:
                self._present = False

        return started

    def _learn(self, points):
        """背景の学習に 1 回転ぶん取り込み、規定回転に達したら背景を確定する。"""
        for idx, distance, _ in points:
            self._observed[idx].append(distance)

        self._learned += 1

        if self._learned >= self._learn_rotations:
            for idx, distances in enumerate(self._observed):
                if distances:
                    self._background[idx] = float(np.median(distances))

            # 学習後は溜めない（回転ごとに増え続けてしまうので）
            self._observed = [[] for _ in range(self._num_bins)]

    def _is_foreground(self, idx, distance):
        """その点が静止背景より手前か。背景が無いビンに来た点はすべて手前扱い。"""
        background = self._background[idx]

        if np.isnan(background):
            return True

        return distance < background - self._margin_mm


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


def _sector_points(samples):
    """この 1 回転でセクタ内にあった有効な点を [(ビン番号, 距離mm, flag), ...] で返す。

    有効判定は画面の点と同じ _is_valid() を使う。そうしないと、関心円の外にある
    壁や範囲外のノイズでも数えてしまう。ビンの刻みは ScanBuffer と同じなので、
    background モードでビンごとに学習した静止背景とそのまま突き合わせられる。

    hits/pts も干渉フラグの集計もすべてこの 1 つのリストから数える。別々に
    集めると、片方だけ範囲や有効判定がズレても気づけないため。
    """
    return [
        (int(p["angle"] // BIN_RESOLUTION_DEG) % NUM_BINS, p["distance"], p["flag"])
        for p in samples
        if _is_valid(p) and _in_count_sector(p["angle"])
    ]


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

            if counter.feed(samples):
                _, hits, points, last, _, _, _, _ = counter.snapshot()
                print(f"[{name}] hit {hits}  ({last} pts, total {points} pts)")


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
        self.counter = SectorCounter(
            NUM_BINS,
            COUNT_RELEASE_ROTATIONS,
            COUNT_BACKGROUND_ROTATIONS,
            COUNT_FOREGROUND_MARGIN_MM,
        )

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
        self._threshold_line = None
        self._theta = None
        self._head_text = None
        self._value_text = None
        self._now_text = None
        self._flag_text = None

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

    #--- カウンタ ---------------------------------------------------------

    def reset(self):
        """カウンタを 0 に戻す（1 回の計測の区切り）。

        hits/pts も干渉フラグの累計も SectorCounter が持っているので、
        まとめて 0 に戻る。
        """
        self.counter.reset()

    def set_mode(self, mode):
        """数え方を切り替える（0 から数え直す）。"""
        self.counter.set_mode(mode)

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
            # panchor は「軸が正方形に潰れたとき、余った幅のどちら側に寄せるか」。
            # 既定の東（カラーバー側）だと窓が細いときにプロットが右へ逃げて、
            # 左のカウンタ欄から離れてしまうので西に寄せる。
            fig.colorbar(
                self._scat,
                ax=ax,
                pad=0.1,
                label="Intensity (0-255)",
                panchor=(0.0, 0.5),
            )
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

        # background モードで学習した静止背景のしきい線（背景 - margin）。この線より
        # 手前（内側）を通った点だけを数える。中身は毎フレーム差し替える。simple の
        # ときと、学習中・背景が無かったビンは NaN なので描かれない。
        self._threshold_line, = ax.plot(
            [],
            [],
            color="tab:gray",
            linewidth=1.0,
            alpha=0.8,
            linestyle="--",
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

        self._build_count_text(fig)

        self._set_title(0)
        self._set_counts(self.counter.snapshot())

    def refresh(self):
        """最新の点群でこの台のプロットを描き替える。毎フレーム呼ぶ。"""
        distances, intensities, detected = self.buffer.snapshot()

        self._scat.set_offsets(np.column_stack((self._theta, distances)))
        if COLOR_BY_INTENSITY:
            self._scat.set_array(intensities)

        self._threshold_line.set_data(self._theta, self.counter.threshold())

        self._set_title(detected)
        self._set_counts(self.counter.snapshot())

        return self._scat

    def _build_count_text(self, fig):
        """ウィンドウ左端のカウンタ欄の文字を作る（1 度だけ呼ぶ）。

        置き場所は決めない。窓の大きさで変わるので place_count_text() が持つ。
        中身は毎フレーム _set_counts() が差し替える。
        """
        def block(fontsize, **kwargs):
            return fig.text(
                0.0, 0.0, "", va="center", ha="left", fontsize=fontsize, **kwargs
            )

        self._head_text = block(COUNT_HEAD_FONTSIZE)
        # 数字は桁が変わっても左端が動かないよう、等幅で右詰めにする
        self._value_text = block(COUNT_VALUE_FONTSIZE, family="monospace")
        self._now_text = block(COUNT_NOW_FONTSIZE, family="monospace")
        self._flag_text = block(COUNT_FLAG_FONTSIZE, family="monospace")

    def place_count_text(self, fig):
        """左のカウンタ欄を、自分のプロットと同じ高さに置き直す。

        軸の縦中心を基準にするので、プロットとカウンタの対応は窓の大きさに
        よらず崩れない。位置は図に対する割合なので、リサイズのたびに呼ぶ。
        """
        box = self._ax.get_position()
        center_y = (box.y0 + box.y1) / 2
        left = COUNT_TEXT_LEFT_IN / fig.get_figwidth()
        height_in = fig.get_figheight()

        for text, offset_in in (
            (self._head_text, COUNT_HEAD_OFFSET_IN),
            (self._value_text, COUNT_VALUE_OFFSET_IN),
            (self._now_text, COUNT_NOW_OFFSET_IN),
            (self._flag_text, COUNT_FLAG_OFFSET_IN),
        ):
            text.set_position((left, center_y + offset_in / height_in))

    def _set_counts(self, count_state):
        """左のカウンタ欄を書き替える。毎フレーム呼ぶ。"""
        mode, count, points, last, present, remaining, flagged, flagged_total = (
            count_state
        )

        self._head_text.set_text(f"{self.name}\n{SECTOR_LABEL} deg  /  {mode}")

        if remaining > 0:
            # 学習中はまだ数えていない。0 のままの数字を出すと「動いていない」に
            # 見えるので、数字は伏せて、学習中であることと残り回転数を出す。
            self._value_text.set_text("   -- hits\n   -- pts")
            self._now_text.set_text(
                f"learning background ... {remaining:3d} rev left"
            )
        else:
            self._value_text.set_text(f"{count:5d} hits\n{points:5d} pts")

            # now は直近 1 回転で「有り」と数えた点数。hits が増えないときに、点が
            # 入っていないのか（now も 0）、入っているのに立ち上がらないのか
            # （now > 0）を、その場で切り分けるために出している。
            marker = "   DETECTING" if present else ""
            self._now_text.set_text(f"now {last:3d}{marker}")

        # 干渉フラグは hits/pts と同じ点の集合から数えているので、範囲の但し書きは
        # 要らない（見出しの "225-315 deg" がそのまま効く）。学習中も数え続けるので、
        # 上が "--" の間もこちらは動く。
        self._flag_text.set_text(
            f"flag 0x{FLAG_SUN_NOISE:02x} sun\n"
            f"{flagged:5d} /rev {flagged_total:8d} total"
        )

    def _set_title(self, detected):
        """2 行のタイトルを書き替える（どの台か / 点の出方）。

        セクタのカウンタは左の欄に出すので、ここには出さない。数字は桁が
        変わってもタイトルが揺れないよう幅を固定する。
        """
        self._ax.set_title(
            f"{self.name}   {self.port}   ({RANGE_LABEL})\n"
            f"{SCAN_FREQUENCY_HZ:.1f} Hz"
            f"   max {MAX_POINTS_PER_REV} pts/rev"
            f"   detected {detected:4d} pts",
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

    manager = fig.canvas.manager
    if manager is not None:
        manager.set_window_title("LiDAR realtime")

    for i, panel in enumerate(panels):
        panel.build(fig, fig.add_subplot(len(panels), 1, i + 1, projection="polar"))

    # --- 計測セクタのカウンタ UI ---
    # カウントは起動時から動いている。ボタンはどちらも全台まとめて効く（同じ落下を
    # 各台がどう捉えたか比べたいので、台ごとにばらばらの区切り・ばらばらのモードで
    # 数えても読めない）。数字はウィンドウ左の欄に出る。
    # Button はローカル変数で参照を持ち続けないと GC されて反応しなくなる。
    # 位置は apply_layout() が入れ直すので、ここでは仮置きでよい
    button_boxes = []

    def button_axes(left_in, width_in):
        ax = fig.add_axes((0.0, 0.0, 0.1, 0.1))
        button_boxes.append((ax, left_in, width_in))

        return ax

    mode = COUNT_MODE_SIMPLE
    reset_button = Button(
        button_axes(BUTTON_LEFT_IN, RESET_BUTTON_WIDTH_IN), "Reset counts"
    )
    mode_button = Button(
        button_axes(
            BUTTON_LEFT_IN + RESET_BUTTON_WIDTH_IN + BUTTON_GAP_IN,
            MODE_BUTTON_WIDTH_IN,
        ),
        f"Mode: {mode}",
    )

    def reset_counts(_event):
        for panel in panels:
            panel.reset()

    def toggle_mode(_event):
        nonlocal mode
        mode = (
            COUNT_MODE_BACKGROUND
            if mode == COUNT_MODE_SIMPLE
            else COUNT_MODE_SIMPLE
        )

        for panel in panels:
            panel.set_mode(mode)

        mode_button.label.set_text(f"Mode: {mode}")

    reset_button.on_clicked(reset_counts)
    mode_button.on_clicked(toggle_mode)

    help_text = fig.text(
        0.0,
        0.0,
        "both buttons apply to all LiDARs at once\n"
        "background mode ignores statics in the sector",
        va="center",
        fontsize=9,
    )

    def apply_layout(_event=None):
        """インチで決めた配置を、いまの窓の大きさに合わせて割合へ換算し直す。

        これをしないと、窓を広げた分だけ左のカウンタ欄・プロット・ボタンが
        一緒に間延びして、カウンタとプロットが遠く離れてしまう。窓が小さすぎて
        インチどおりに置けないときのために、割合は 0〜1 の内側へ収める。
        """
        width_in = fig.get_figwidth()
        height_in = fig.get_figheight()

        left = min(LEFT_COLUMN_WIDTH_IN / width_in, 0.6)
        right = min(
            max((LEFT_COLUMN_WIDTH_IN + PLOT_AREA_WIDTH_IN) / width_in, left + 0.2),
            0.98,
        )
        bottom = min(BOTTOM_ROW_HEIGHT_IN / height_in, 0.4)
        top = max(1.0 - TOP_MARGIN_IN / height_in, bottom + 0.2)

        fig.subplots_adjust(
            left=left,
            right=right,
            top=top,
            bottom=bottom,
            hspace=PANEL_GAP_RATIO,
        )

        for panel in panels:
            panel.place_count_text(fig)

        for button_ax, button_left_in, button_width_in in button_boxes:
            button_ax.set_position((
                button_left_in / width_in,
                BUTTON_BOTTOM_IN / height_in,
                button_width_in / width_in,
                BUTTON_HEIGHT_IN / height_in,
            ))

        help_text.set_position((
            HELP_TEXT_LEFT_IN / width_in,
            HELP_TEXT_BOTTOM_IN / height_in,
        ))

    apply_layout()
    fig.canvas.mpl_connect("resize_event", apply_layout)

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
