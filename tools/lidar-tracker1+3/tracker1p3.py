"""T-mini Plus x2（単体）+ RPLIDAR S1 x4（2 ペアの交点法）を束ねた落氷検知。

tools/lidar-tracker（T-mini Plus）と tools/lidar-tracker3（S1）の継ぎ接ぎ（`sensors.py`
参照）。両者を 1 つの出力に束ねる理由と方法:

  - **T-mini Plus は単体で完結**。S1 ほど混合画素（mixed pixel）が酷くないので、
    1 台の「関心円内の点群の単純平均」をそのままその台の代表点にする。ペアにしない。
    1 台でも 2 台でも独立に動く（片方が繋がらなくてももう片方は動く）。

  - **S1 は必ずペアで使う（交点法 / triangulation）**。距離値そのものは混合画素で
    実体より奥へ伸びるので信用せず、「センサー位置 → 検出点群の平均点」が作る
    **方位角**だけを信用する。2 台を既知の間隔（`baseline_mm`）で並べ、それぞれの
    直線を延長した**交点**を実体の位置と見なす。片方の台しか開けない・片方の台が
    円内に何も見ていない、という瞬間はその瞬間だけ交点を出さない（後述のホールド
    で埋める）。**1 台だけの S1 は絶対に有効入力にしない**（1 台では交点が作れず、
    従来どおり混合画素の影響をそのまま受けるため）。

  - 最終出力は、その時点で有効な「T-mini 単体の代表点」「S1 ペアの交点」（最大 4 個:
    T-mini x2 + S1 ペア x2）の**単純平均**。1 個も無ければ検知なし。

構成（トラッカー数 x 2 スレッド + 書き込み1 + 可視化ならメインスレッド）:
  - 読み取りスレッド : 物理 LiDAR 1 台につき 1 本（`LidarReader._reader_loop`）。
                       1 回転ぶんを読み、関心円内点群とその平均点を自分の
                       `HoldState` に書く。
  - ライタスレッド   : 50ms ごとに「T-mini 各台の代表点」「S1 各ペアの交点
                       （2 台の HoldState から都度計算）」を集め、有効なものの
                       平均を共有ファイルへ 1 行書く（`AveragingWriter`）。
  - メインスレッド   : 可視化ありなら単一の Cartesian プロット（`CombinedView`）を
                       FuncAnimation で再描画。--headless なら待つだけ。

ホールド（検出の隙間を埋める）:
  各 `HoldState`（T-mini 各台、S1 各生ユニット）は「最後に検出した点」を
  `[hold] shaving_hold_sec` の間だけ保持する。S1 ペアの交点は、都度その瞬間の
  両生ユニットの保持値から計算し直す（交点そのものは別途ホールドしない。
  両生ユニットが保持している間は同じ交点が出続けるので、二重にホールドする
  意味が無い）。

座標系（共有フレーム、[mm]）:
  T-mini の frame 原点＝そのユニットの位置＝共有フレームの原点そのもの
  （xy_offset = (0, 0)）。S1 は基線の中点を共有フレーム原点とし、ペアの
  port_a を (-baseline/2, 0)、port_b を (+baseline/2, 0) に置く。関心円
  （落氷域そのもの）は `[circle]` で 1 つだけ定義し、T-mini・S1 のどちらも
  この共有フレームへ変換してから同じ円で絞る（センサーごとに円を持たない）。

実行:
  uv run python tracker1p3.py               # 可視化つき
  uv run python tracker1p3.py --headless    # 窓なし
"""

import argparse
import os
import sys
import threading
import time

import numpy as np

import config
import geometry
import sensors

#=============================
# 設定の読み込み
#=============================

_CIRCLE = config.load_circle_config()
CIRCLE_CENTER_MM = _CIRCLE["center_offset_mm"]
CIRCLE_RADIUS_MM = _CIRCLE["radius_mm"]
CIRCLE_TOLERANCE_MM = _CIRCLE["tolerance_mm"]
CIRCLE_FILTER_MM = CIRCLE_RADIUS_MM + CIRCLE_TOLERANCE_MM

TMINI_CFG = config.load_tmini_config()
S1_CFG = config.load_s1_config()
HOLD_SEC = config.load_hold_config()["shaving_hold_sec"]
POSITION_FILE = config.load_output_config()

WRITE_INTERVAL_SEC = 0.05
UPDATE_INTERVAL_MS = 50

# 表示範囲。関心円＋基線ぶんが収まるように、フィルタ円半径と基線の最大値から決める。
_MAX_BASELINE_MM = max([p["baseline_mm"] for p in S1_CFG["pairs"]], default=0.0)
DISPLAY_HALF_EXTENT_MM = max(CIRCLE_FILTER_MM, _MAX_BASELINE_MM / 2.0 + CIRCLE_FILTER_MM) * 1.25

TMINI_MARKER_COLOR = "tab:orange"
S1_MARKER_COLOR = "tab:purple"
FINAL_MARKER_COLOR = "tab:green"
RAY_COLOR = "0.55"

# 物理LiDAR 1台ごとの点群の色（どの台がどちらを向いているか目視で切り分けるため）。
# T-mini各台・S1各生ユニット、あわせて最大6台ぶん。単色（赤一色）だと「どの台が
# 逆を向いているか」が分からないので、台ごとに変える。円マーカー色（橙/紫/緑/青）
# とは意図的に重ならない配色にしてある。
UNIT_COLORS = ["tab:red", "tab:brown", "tab:pink", "tab:olive", "tab:cyan", "tab:gray"]


#=============================
# 点群フィルタ（センサー非依存）
#=============================

class CloudFilter:
    """1 回転ぶんの samples から、共有フレーム [mm] の有効点・関心円内点を取り出す。

    T-mini（xy_offset=(0,0)）・S1（xy_offset=±(baseline/2, 0)）のどちらでも使う
    共通ロジック。intensity のレンジ（T-mini 8bit / S1 6bit）は呼び出し側の
    `intensity_max` に反映済みという前提で、ここでは大小比較だけする。
    """

    def __init__(self, *, range_min_mm, range_max_mm, intensity_max,
                 angle_offset_deg, flip_horizontal, flip_vertical,
                 circle_center_mm, circle_filter_radius_mm, xy_offset_mm=(0.0, 0.0)):
        self._range_min_mm = range_min_mm
        self._range_max_mm = range_max_mm
        self._intensity_max = intensity_max
        self._angle_offset_deg = angle_offset_deg
        self._flip_h = flip_horizontal
        self._flip_v = flip_vertical
        self._cx, self._cy = circle_center_mm
        self._filter_r2 = circle_filter_radius_mm * circle_filter_radius_mm
        self._ox, self._oy = xy_offset_mm

    def extract(self, samples):
        """(共有フレームの全有効点 [(x,y)...], うち関心円内の氷候補点 [(x,y)...])。"""
        all_valid = []
        in_circle = []

        for p in samples:
            distance = p["distance"]
            if distance == 0:
                continue
            if not (self._range_min_mm <= distance <= self._range_max_mm):
                continue

            display_deg = geometry.display_angle(p["angle"], self._angle_offset_deg)
            x, y = geometry.to_xy_mm(display_deg, distance, self._flip_h, self._flip_v)
            x += self._ox
            y += self._oy
            all_valid.append((x, y))

            if p["intensity"] >= self._intensity_max:
                continue
            dx, dy = x - self._cx, y - self._cy
            if dx * dx + dy * dy > self._filter_r2:
                continue
            in_circle.append((x, y))

        return all_valid, in_circle


#=============================
# 保持状態（トラッカー共通）
#=============================

class HoldState:
    """1 台の生 LiDAR ぶんの共有状態（読み取りスレッド → ライタ／描画スレッド）。

    tools/lidar-tracker3 の `UnitState` と同じ形: 直近の代表点＋検出時刻を持ち、
    hold_sec 以内なら resolve() がその点を返す。T-mini はこれをそのまま自分の
    代表点として使い、S1 は 2 台ぶんの HoldState から都度 `S1Pair` が交点を作る。
    """

    def __init__(self):
        self._lock = threading.Lock()
        self._point = None
        self._detected_at = None
        self._cloud = []
        self._in_circle = []
        self._hit_count = 0

    def publish(self, avg_point, cloud, in_circle):
        with self._lock:
            self._cloud = cloud
            self._in_circle = in_circle
            if avg_point is not None:
                self._point = avg_point
                self._detected_at = time.monotonic()
                self._hit_count += 1

    def resolve(self, hold_sec):
        """(点 or None, hold内かどうか) を返す。一度も検出していなければ (None, False)。"""
        with self._lock:
            if self._detected_at is None:
                return None, False
            fresh = (time.monotonic() - self._detected_at) <= hold_sec
            return (self._point if fresh else None), fresh

    def snapshot(self):
        with self._lock:
            return list(self._cloud), list(self._in_circle), self._hit_count


#=============================
# 物理 LiDAR 1 台（T-mini Plus または S1 の片側）
#=============================

class LidarReader:
    """1 台の物理 LiDAR ＋ 読み取りスレッド ＋ 関心円フィルタ ＋ HoldState。

    T-mini はこれ単体が「トラッカー」（`.resolve()` が最終候補点になる）。
    S1 はこれをペアで束ねて `S1Pair` が交点を作る（`.resolve()` は単体としても
    呼べるが、通常は `S1Pair` 経由でしか使わない）。
    """

    def __init__(self, kind, name, port, sensor, cloud_filter, pos_mm=(0.0, 0.0)):
        self.kind = kind          # "tmini" | "s1"
        self.name = name
        self.port = port
        self.pos_mm = pos_mm      # 共有フレームでのこのユニットの位置
        self._sensor = sensor
        self._filter = cloud_filter
        self.state = HoldState()
        self._thread = None
        # start_s1_pairs がペアの片方だけ開いたあと諦めて stop() するときは、
        # まだ全体の stop_event が立っていない（起動シーケンス途中のため）。
        # その状況を read_scan() のエラーメッセージで騒がしくしないための自分専用フラグ。
        self._own_stop = threading.Event()

    def start(self, stop_event):
        """接続してスキャンを開始し、読み取りスレッドを走らせる。"""
        self._sensor.start_scan()
        self._thread = threading.Thread(
            target=self._reader_loop, args=(stop_event,), daemon=True
        )
        self._thread.start()

    def _reader_loop(self, stop_event):
        while not stop_event.is_set():
            try:
                samples = self._sensor.read_scan()
            except Exception as e:
                if not stop_event.is_set() and not self._own_stop.is_set():
                    print(f"[{self.name}] read_scan error: {e}", file=sys.stderr)
                break

            if not samples:
                continue

            cloud, in_circle = self._filter.extract(samples)
            avg = geometry.average_point(in_circle)
            self.state.publish(avg, cloud, in_circle)

            if avg is not None:
                print(f"[{self.name}] avg -> ({avg[0]:7.1f}, {avg[1]:7.1f}) mm  ({len(in_circle)} pts)")

    def stop(self):
        """スキャンを止めて接続を閉じてから、読み取りスレッドの終了を待つ。

        read_scan() はブロッキングなので、呼び出し側がまだ共有の stop_event を
        立てていないタイミングで呼ばれる（S1 ペアの片方だけ開けて諦める場合）と、
        先に join してしまうと本来の意味でスレッドが自分で気づいて終わるまで
        待つしかなく、無駄に長く止まる。先にセンサーを閉じてブロッキング中の
        読み取りを即座にエラーで返らせてから join する。
        """
        self._own_stop.set()
        try:
            self._sensor.stop_scan()
        except Exception:
            pass
        try:
            self._sensor.close()
        except Exception:
            pass
        if self._thread is not None:
            self._thread.join(timeout=2.0)
            self._thread = None

    def resolve(self, hold_sec):
        return self.state.resolve(hold_sec)[0]


#=============================
# S1 ペア（交点法）
#=============================

class S1Pair:
    """S1 2 台のペア。代表点＝両者の (センサー位置→平均点) 直線の交点。

    両生ユニットが hold_sec 以内に検出していないと交点は作らない
    （＝S1 は 2 台揃わないと有効入力にならない、を毎フレーム反映する）。
    """

    def __init__(self, name, unit_a, unit_b):
        self.name = name
        self.unit_a = unit_a
        self.unit_b = unit_b

    def resolve(self, hold_sec):
        a, _ = self.unit_a.state.resolve(hold_sec)
        b, _ = self.unit_b.state.resolve(hold_sec)
        if a is None or b is None:
            return None
        return geometry.line_intersection(self.unit_a.pos_mm, a, self.unit_b.pos_mm, b)

    def debug_rays(self, hold_sec):
        """可視化用: (posA, avgA or None, posB, avgB or None) を返す。"""
        a, _ = self.unit_a.state.resolve(hold_sec)
        b, _ = self.unit_b.state.resolve(hold_sec)
        return self.unit_a.pos_mm, a, self.unit_b.pos_mm, b


#=============================
# config → オブジェクト
#=============================

def build_tmini_readers():
    cfg = TMINI_CFG
    range_min_mm = cfg["range_min_cm"] * 10.0
    range_max_mm = cfg["range_max_cm"] * 10.0

    readers = []
    for u in cfg["units"]:
        # 台ごとの angle_offset_deg 上書き（config.toml [[tmini.units]]）。無ければ
        # [tmini] の共通値。個体のエンコーダのゼロ点ズレを吸収する。
        angle_offset_deg = u["angle_offset_deg"]
        if angle_offset_deg is None:
            angle_offset_deg = cfg["angle_offset_deg"]
        cloud_filter = CloudFilter(
            range_min_mm=range_min_mm,
            range_max_mm=range_max_mm,
            intensity_max=cfg["intensity_max"],
            angle_offset_deg=angle_offset_deg,
            flip_horizontal=cfg["flip_horizontal"],
            flip_vertical=cfg["flip_vertical"],
            circle_center_mm=CIRCLE_CENTER_MM,
            circle_filter_radius_mm=CIRCLE_FILTER_MM,
            xy_offset_mm=(0.0, 0.0),
        )
        sensor = sensors.TMiniPlus(
            u["port"], cfg["baudrate"], scan_frequency_hz=cfg["frequency_hz"]
        )
        readers.append(
            LidarReader("tmini", u["name"], u["port"], sensor, cloud_filter, pos_mm=(0.0, 0.0))
        )
    return readers


def build_s1_pairs():
    cfg = S1_CFG
    range_min_mm = cfg["range_min_cm"] * 10.0
    range_max_mm = cfg["range_max_cm"] * 10.0

    pairs = []
    for p in cfg["pairs"]:
        half = p["baseline_mm"] / 2.0
        pos_a = (-half, 0.0)
        pos_b = (half, 0.0)

        # 台ごとの angle_offset_deg 上書き（config.toml [[s1.pairs]] angle_offset_a/b）。
        # 無ければ [s1] の共通値。個体のエンコーダのゼロ点ズレを吸収する。
        angle_offset_a = p["angle_offset_a"] if p["angle_offset_a"] is not None else cfg["angle_offset_deg"]
        angle_offset_b = p["angle_offset_b"] if p["angle_offset_b"] is not None else cfg["angle_offset_deg"]

        def make_filter(offset, angle_offset_deg):
            return CloudFilter(
                range_min_mm=range_min_mm,
                range_max_mm=range_max_mm,
                intensity_max=cfg["intensity_max"],
                angle_offset_deg=angle_offset_deg,
                flip_horizontal=cfg["flip_horizontal"],
                flip_vertical=cfg["flip_vertical"],
                circle_center_mm=CIRCLE_CENTER_MM,
                circle_filter_radius_mm=CIRCLE_FILTER_MM,
                xy_offset_mm=offset,
            )

        sensor_a = sensors.RPLidarS1(
            p["port_a"], cfg["baudrate"], scan_mode=cfg["mode"], scan_frequency_hz=cfg["frequency_hz"]
        )
        sensor_b = sensors.RPLidarS1(
            p["port_b"], cfg["baudrate"], scan_mode=cfg["mode"], scan_frequency_hz=cfg["frequency_hz"]
        )
        reader_a = LidarReader("s1", f"{p['name']}.a", p["port_a"], sensor_a,
                               make_filter(pos_a, angle_offset_a), pos_mm=pos_a)
        reader_b = LidarReader("s1", f"{p['name']}.b", p["port_b"], sensor_b,
                               make_filter(pos_b, angle_offset_b), pos_mm=pos_b)
        pairs.append(S1Pair(p["name"], reader_a, reader_b))
    return pairs


#=============================
# 起動（開けなかった台・ペアは飛ばして続行）
#=============================

def start_tmini(readers, stop_event):
    """T-mini は 1 台ずつ独立。開けなかった台は飛ばす。"""
    active = []
    for r in readers:
        try:
            r.start(stop_event)
        except Exception as e:
            print(
                f"[{r.name}] {r.port} を開けません: {e}\n"
                f"         → この台を飛ばして続行します",
                file=sys.stderr,
            )
            continue
        active.append(r)
    return active


def start_s1_pairs(pairs, stop_event):
    """S1 はペア単位。片方でも開けなければそのペアごと無効化する。"""
    active = []
    for pair in pairs:
        opened = []
        ok = True
        for reader in (pair.unit_a, pair.unit_b):
            try:
                reader.start(stop_event)
            except Exception as e:
                print(f"[{pair.name}] {reader.port}（{reader.name}）を開けません: {e}", file=sys.stderr)
                ok = False
                break
            opened.append(reader)

        if ok:
            active.append(pair)
            print(f"[{pair.name}] 両台オープン、交点法を有効化（baseline {pair.unit_b.pos_mm[0] - pair.unit_a.pos_mm[0]:.0f}mm）")
        else:
            for reader in opened:
                reader.stop()
            print(
                f"[{pair.name}] → ペアとして無効化して続行します"
                "（S1 は 2 台揃わないと有効入力にしません）",
                file=sys.stderr,
            )
    return active


#=============================
# 出力（平均を書く）
#=============================

class AveragingWriter:
    """有効な全トラッカー（T-mini 各台 + S1 各ペア）の代表点を束ね、平均を書く。

    1 個でも有効なら shaving=1 でその平均、1 個も無ければ検知なし（`seq,,,0`）。
    書き込みは temp へ書き切って `os.replace()` の原子的 rename（partial read 排除）。
    `seq` は検知の有無に関わらず単調増加し、Unity は `seq` の停止で「ロスト」を検知する。
    """

    def __init__(self, path, trackers, hold_sec, interval_sec):
        self._path = path
        self._tmp_path = path.with_name(path.name + ".tmp")
        self._trackers = trackers
        self._hold_sec = hold_sec
        self._interval = interval_sec
        self._seq = 0
        self._lock = threading.Lock()
        self._last = (None, False, 0, {})  # (avg_point, shaving, seq, {tracker_name: point or None})

    def resolve_all(self):
        contributions = {t.name: t.resolve(self._hold_sec) for t in self._trackers}
        live = [p for p in contributions.values() if p is not None]
        avg = geometry.average_point(live) if live else None
        return avg, (avg is not None), contributions

    def status(self):
        """(avg_point or None, shaving, seq, {tracker_name: point or None}) を返す。"""
        with self._lock:
            return self._last

    def run(self, stop_event):
        self._path.parent.mkdir(parents=True, exist_ok=True)

        while not stop_event.is_set():
            avg, shaving, contributions = self.resolve_all()

            with self._lock:
                self._seq += 1
                self._last = (avg, shaving, self._seq, contributions)
                seq = self._seq

            if avg is None:
                line = f"{seq},,,0\n"
            else:
                line = f"{seq},{avg[0]:.1f},{avg[1]:.1f},{1 if shaving else 0}\n"

            try:
                with open(self._tmp_path, "w", encoding="utf-8") as f:
                    f.write(line)
                    f.flush()
                    os.fsync(f.fileno())
                os.replace(self._tmp_path, self._path)
            except OSError as e:
                print(f"[writer] 書き込み失敗 ({self._path}): {e}", file=sys.stderr)

            stop_event.wait(self._interval)


#=============================
# 可視化（単一の Cartesian プロット）
#=============================

class CombinedView:
    """T-mini・S1 を束ねた単一プロット。

      - 台ごとの色: 各物理LiDAR（T-mini各台・S1各生ユニット）の点群を別々の色で描く。
        薄い点＝その台の全有効点、濃い点＝その台の関心円内・氷候補点。
        どの台がどちら向きに据わっているか（正面がズレていないか）を色で見分けるため
        （赤一色だと台の区別がつかない、という問題への対応）。凡例は右上。
      - 橙三角: T-mini 各台の代表点（点群平均）
      - 紫四角: S1 各ペアの交点（交点法の推定点）＋薄いレイ2本（デバッグ用）
      - 緑の輪: 最終的にUnityへ送る代表点（有効な代表点の平均）

    ポーラーではなく直交座標。S1 ペアは原点の異なる 2 直線の交点を見るので、
    直交座標のほうがレイ（直線）をそのまま直線として描け、交点を目視確認しやすい。
    """

    def __init__(self, tmini_readers, s1_pairs, writer):
        self._tmini = tmini_readers
        self._pairs = s1_pairs
        self._writer = writer
        # 色分け対象＝全ての物理LiDAR（T-mini各台 + S1各生ユニット）。
        self._units = list(tmini_readers) + [
            unit for pair in s1_pairs for unit in (pair.unit_a, pair.unit_b)
        ]
        self._unit_colors = {
            reader.name: UNIT_COLORS[i % len(UNIT_COLORS)]
            for i, reader in enumerate(self._units)
        }
        self._ax = None
        self._cloud_scats = {}   # reader.name -> (all_scat, ice_scat)
        self._tmini_scat = None
        self._s1_scat = None
        self._final_scat = None
        self._ray_lines = []
        self._unit_text = None
        self._status_text = None

    def build(self, fig):
        ax = fig.add_subplot(1, 1, 1)
        self._ax = ax
        ax.set_aspect("equal")
        ax.set_xlim(-DISPLAY_HALF_EXTENT_MM, DISPLAY_HALF_EXTENT_MM)
        ax.set_ylim(-DISPLAY_HALF_EXTENT_MM * 0.3, DISPLAY_HALF_EXTENT_MM * 1.6)
        ax.set_xlabel("x [mm]")
        ax.set_ylabel("y [mm]")
        ax.grid(True, linewidth=0.4, alpha=0.4)

        # 関心円・フィルタ円
        t = np.linspace(0.0, 2.0 * np.pi, 181)
        for r_mm, kw in (
            (CIRCLE_RADIUS_MM, dict(color="tab:blue", linewidth=1.4, alpha=0.85)),
            (CIRCLE_FILTER_MM, dict(color="tab:blue", linewidth=0.9, alpha=0.35, linestyle="--")),
        ):
            xs = CIRCLE_CENTER_MM[0] + r_mm * np.cos(t)
            ys = CIRCLE_CENTER_MM[1] + r_mm * np.sin(t)
            ax.plot(xs, ys, **kw)
        ax.plot([CIRCLE_CENTER_MM[0]], [CIRCLE_CENTER_MM[1]], marker="+", color="tab:blue",
                markersize=13, markeredgewidth=1.6, linestyle="none")

        # 物理ユニットの位置（原点マーカー）
        ax.plot([0.0], [0.0], marker="s", color="tab:orange", markersize=6,
                markerfacecolor="none", zorder=2)
        for pair in self._pairs:
            for reader in (pair.unit_a, pair.unit_b):
                ax.plot([reader.pos_mm[0]], [reader.pos_mm[1]], marker="^",
                        color="tab:purple", markersize=6, markerfacecolor="none", zorder=2)

        # レイ（S1ペアごとに2本、先に空で作っておいて毎フレーム更新）
        for _ in self._pairs:
            line_a, = ax.plot([], [], color=RAY_COLOR, linewidth=1.0, linestyle=":", zorder=1)
            line_b, = ax.plot([], [], color=RAY_COLOR, linewidth=1.0, linestyle=":", zorder=1)
            self._ray_lines.append((line_a, line_b))

        for reader in self._units:
            color = self._unit_colors[reader.name]
            all_scat = ax.scatter([], [], s=6, c=color, alpha=0.35, zorder=3)
            ice_scat = ax.scatter([], [], s=34, c=color, alpha=0.95, zorder=4, label=reader.name)
            self._cloud_scats[reader.name] = (all_scat, ice_scat)

        if self._units:
            ax.legend(loc="upper right", fontsize=8, framealpha=0.85,
                      markerscale=1.3, title="LiDAR", title_fontsize=8)

        self._tmini_scat = ax.scatter([], [], s=110, marker="^", facecolors="none",
                                      edgecolors=TMINI_MARKER_COLOR, linewidths=1.8, zorder=5)
        self._s1_scat = ax.scatter([], [], s=110, marker="s", facecolors="none",
                                   edgecolors=S1_MARKER_COLOR, linewidths=1.8, zorder=5)
        self._final_scat = ax.scatter([], [], s=160, facecolors="none",
                                      edgecolors=FINAL_MARKER_COLOR, linewidths=2.2, zorder=6)

        self._unit_text = fig.text(0.02, 0.5, "", va="center", ha="left",
                                   fontsize=9, family="monospace")
        self._status_text = fig.text(0.02, 0.04, "", va="center", ha="left",
                                     fontsize=12, family="monospace")

        ax.set_title(
            f"lidar-tracker1+3   T-mini x{len(self._tmini)}   S1 pairs x{len(self._pairs)}",
            fontsize=11,
        )

    def _extend(self, pos, avg):
        """pos→avg の直線を、表示範囲の外まで延長した2点にする（レイの描画用）。"""
        dx, dy = avg[0] - pos[0], avg[1] - pos[1]
        length = max((dx * dx + dy * dy) ** 0.5, 1e-6)
        scale = (DISPLAY_HALF_EXTENT_MM * 3.0) / length
        far = (pos[0] + dx * scale, pos[1] + dy * scale)
        return pos, far

    def _update_cloud(self, reader):
        """1台ぶんの点群スキャッタを更新する。呼び出し側の unit_lines 用に値も返す。"""
        cloud, in_circle, hit_count = reader.state.snapshot()
        all_scat, ice_scat = self._cloud_scats[reader.name]
        all_scat.set_offsets(np.asarray(cloud, dtype=float).reshape(-1, 2))
        ice_scat.set_offsets(np.asarray(in_circle, dtype=float).reshape(-1, 2))
        return len(in_circle), hit_count

    def refresh(self):
        unit_lines = []

        for r in self._tmini:
            n_in_circle, hit_count = self._update_cloud(r)
            point, fresh = r.state.resolve(HOLD_SEC)
            unit_lines.append(f"{r.name:<9}{n_in_circle:3d}p{hit_count:5d}h {'LIVE' if fresh else '----'}")

        tmini_points = []
        for r in self._tmini:
            p, _ = r.state.resolve(HOLD_SEC)
            if p is not None:
                tmini_points.append(p)

        s1_points = []
        for i, pair in enumerate(self._pairs):
            pos_a, avg_a, pos_b, avg_b = pair.debug_rays(HOLD_SEC)
            for reader in (pair.unit_a, pair.unit_b):
                self._update_cloud(reader)

            line_a, line_b = self._ray_lines[i]
            if avg_a is not None:
                p0, p1 = self._extend(pos_a, avg_a)
                line_a.set_data([p0[0], p1[0]], [p0[1], p1[1]])
            else:
                line_a.set_data([], [])
            if avg_b is not None:
                p0, p1 = self._extend(pos_b, avg_b)
                line_b.set_data([p0[0], p1[0]], [p0[1], p1[1]])
            else:
                line_b.set_data([], [])

            point = pair.resolve(HOLD_SEC)
            if point is not None:
                s1_points.append(point)

            a_hit = pair.unit_a.state.snapshot()[2]
            b_hit = pair.unit_b.state.snapshot()[2]
            status = "LIVE" if point is not None else ("half" if (avg_a is not None) != (avg_b is not None) else "----")
            unit_lines.append(f"{pair.name:<9}a{a_hit:4d}h b{b_hit:4d}h {status}")

        self._tmini_scat.set_offsets(np.asarray(tmini_points, dtype=float).reshape(-1, 2))
        self._s1_scat.set_offsets(np.asarray(s1_points, dtype=float).reshape(-1, 2))

        avg, shaving, seq, contributions = self._writer.status()
        if avg is None:
            self._final_scat.set_offsets(np.zeros((0, 2)))
        else:
            self._final_scat.set_offsets([[avg[0], avg[1]]])

        self._unit_text.set_text("\n".join(unit_lines))

        loc = "  none        " if avg is None else f"({avg[0]:7.1f}, {avg[1]:7.1f})"
        n_live = sum(1 for p in contributions.values() if p is not None)
        self._status_text.set_text(
            f"seq {seq:>7d}   final xy {loc} mm   shaving {'ON ' if shaving else 'off'}"
            f"   ({n_live}/{len(contributions)} live)\n"
            f"{POSITION_FILE}"
        )

        artists = [self._tmini_scat, self._s1_scat, self._final_scat]
        for all_scat, ice_scat in self._cloud_scats.values():
            artists.append(all_scat)
            artists.append(ice_scat)
        return artists


#=============================
# 実行モード
#=============================

def run_headless(stop_event):
    print("headless: Ctrl-C で停止します")
    try:
        while not stop_event.is_set():
            stop_event.wait(0.5)
    except KeyboardInterrupt:
        print()
        print("Interrupted")


def run_plot(view, stop_event, shutdown):
    import matplotlib.animation as animation
    import matplotlib.pyplot as plt

    fig = plt.figure(figsize=(9.5, 9.5))
    manager = fig.canvas.manager
    if manager is not None:
        manager.set_window_title("lidar-tracker1+3")

    view.build(fig)
    fig.subplots_adjust(left=0.33, right=0.97, top=0.90, bottom=0.12)

    def update(_frame):
        return view.refresh()

    ani = animation.FuncAnimation(
        fig, update, interval=UPDATE_INTERVAL_MS, blit=False, cache_frame_data=False
    )
    fig.canvas.mpl_connect("close_event", lambda _e: shutdown())

    try:
        plt.show()
    except KeyboardInterrupt:
        print()
        print("Interrupted")

    del ani


def main():
    parser = argparse.ArgumentParser(
        description="T-mini Plus x2（単体）+ RPLIDAR S1 x4（2ペアの交点法）を束ねた落氷検知"
    )
    parser.add_argument("--headless", action="store_true", help="可視化ウィンドウを開かない")
    args = parser.parse_args()

    tmini_readers = build_tmini_readers()
    s1_pairs = build_s1_pairs()
    stop_event = threading.Event()

    # **S1 を先に、T-mini を後に開く。** 実機で確認した現象: T-mini は公式 SDK
    # （コンパイル済み C++、内部で独自に USB/シリアルへ触る）を使い、1 台の起動だけで
    # 2.5〜3.5 秒の重いやりとりをする。T-mini の起動（成功・失敗を問わず）の**直後**に
    # 別のシリアルポートを新規に開くと、そのポートが「応答ヘッダが不正（0 埋め）」で
    # 壊れる現象が高い再現性で確認された（詳細は README「起動順序について」）。
    # S1 は生プロトコル実装で身が軽いので、先に全部開いてしまえば T-mini 側の
    # 重い処理の影響を受けない。
    active_pairs = start_s1_pairs(s1_pairs, stop_event)
    active_tmini = start_tmini(tmini_readers, stop_event)
    trackers = active_tmini + active_pairs

    if not trackers:
        stop_event.set()
        sys.exit(
            "T-mini・S1 ともに有効な入力がありません。\n"
            f"  config.toml の [[tmini.units]]: {len(tmini_readers)} 台設定\n"
            f"  config.toml の [[s1.pairs]]   : {len(s1_pairs)} ペア設定\n"
            "  ケーブル・補助5V・ポート名を確認してください"
            "（S1 はペアの両方が開けないとそのペアごと無効です）。"
        )

    writer = AveragingWriter(POSITION_FILE, trackers, HOLD_SEC, WRITE_INTERVAL_SEC)
    writer_thread = threading.Thread(target=writer.run, args=(stop_event,), daemon=True)
    writer_thread.start()

    print(f"position file = {POSITION_FILE}")
    print(f"circle: center ({CIRCLE_CENTER_MM[0]:.0f}, {CIRCLE_CENTER_MM[1]:.0f}) mm, r {CIRCLE_RADIUS_MM:.0f} (+tol {CIRCLE_TOLERANCE_MM:.0f})")
    print(f"T-mini: {len(active_tmini)}/{len(tmini_readers)} 台 有効, S1: {len(active_pairs)}/{len(s1_pairs)} ペア 有効, shaving hold {HOLD_SEC:g}s")

    def shutdown():
        if stop_event.is_set():
            return
        stop_event.set()
        writer_thread.join(timeout=3.0)
        for t in active_tmini:
            t.stop()
        for pair in active_pairs:
            pair.unit_a.stop()
            pair.unit_b.stop()

    try:
        if args.headless:
            run_headless(stop_event)
        else:
            view = CombinedView(active_tmini, active_pairs, writer)
            run_plot(view, stop_event, shutdown)
    finally:
        shutdown()


if __name__ == "__main__":
    main()
