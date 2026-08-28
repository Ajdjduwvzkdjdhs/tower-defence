"""LiDAR の落氷検知 → 氷の連続座標 + 削り状態（Kakigori TD / lidar-tracker）。

`scan_plot_realtime.py`（可視化のみ）を土台に、検出した氷の**代表点の座標**を Unity へ渡す。
谷（channel）の分類はしない（phase-preview-feedback.md / ADR-29）。Unity 側はその座標を
山上のワールド位置へ写し、そこから雪玉を放出する。

出力は 1 行 `seq,x,y,shaving`（例 `1837,12.5,-284.0,1`）。座標は LiDAR 原点フレーム[mm]:
    x = r * cos(theta) ,  y = -r * sin(theta)     （theta = 表示角[deg]、r = distance[mm]）
これで theta=0° -> +X、theta=270° -> +Y。物理配置では山の中心は LiDAR の 270° 方向へ
約 300mm なので、中心に落ちた氷はおよそ (x, y) = (0, +300) になる。

代表点は preview.toml の関心円（中心オフセット・半径・はみ出し許容）の中で、円中心に最も
近い 1 点。検知なしは `seq,,,0`（x,y 空欄）。temp へ書き切って `os.replace()` の原子的
rename（partial read を排除）・`seq` 単調増加はそのまま。Unity は `seq` の停止で「ロスト」を
検知し、検知点なし・削りなし（安全側）へ倒す。

構成（台数 + 1 スレッド、+ 可視化なら メインスレッド）:
  - 読み取りスレッド : 台ごとに 1 本。read_scan() で 1 回転ぶんを読み、その回転を
                       そのまま判定して「谷と時刻」を自分の共有状態に書く
  - ライタスレッド   : 50ms ごとに全台の共有状態を突き合わせ、共有ファイルへ 1 行書く
  - メインスレッド   : 可視化ありなら FuncAnimation で散布図を再描画。--headless なら待つだけ

設置の前提（`config.toml` の [valleys] はこの前提で書く）:
  LiDAR 2 台は**どちらも北の谷の正面**、ラックの上下段に置く。水平位置が同じなので
  各谷への方位角は 2 台で共通で、違うのは氷を捉える高さだけ。よって谷のテーブルは
  1 個で全台が共有する。4 つの谷の配置の**外側**に立つため、北の谷と南の谷はほぼ
  同じ方位角に重なる。角度だけでは切り分けられないので、谷は「角度 × 距離」の
  極座標の矩形で指定する。LiDAR の正面は 225〜315 度（中心 270 度）で、4 つの谷は
  すべてこの 90 度の中に収まる（scan_plot_realtime.py の既定計測セクタと同じ範囲）。

氷かどうかの判定（2 台の OR は「判定した後」に取る）:
  LiDAR は氷を毎回転は拾えない（実測で 0.75〜2 秒に 1 回）。取りこぼしを減らすために
  2 台使うが、回転の位相は同期していないので点を合算はしない。台ごとに 1 回転を
  独立に判定し切り、結果として出た「谷と時刻」だけをライタが突き合わせて、
  **最も新しい検出**を採る。こうすると点数の上限が「1 台の 1 回転で何点まで」という
  現物合わせしやすい意味を保てる（合算すると同じ氷を 2 台が拾っただけで倍になる）。

  判定は 2 つの傾向に乗っている:
    - 氷は Intensity が低い / 障害物（人・機体・造形物）は Intensity が高い
      → `intensity_max` **未満**だけを氷の候補にする
    - 氷は 1 回転に 1〜3 点程度でしか写らない / 障害物はまとまった点で写る
      → 谷ごとの点数が `max_points_per_valley` を超えたら、その谷は障害物とみなして
        `channel` にも `shaving` にも寄与させない（「削っていないのに雪崩が流れ続ける」
        という最悪の誤りを断つ）
  そのうえで複数の谷が残ったら、**点数が最も少ない谷**を採る（少ない方が氷らしい）。
  同数なら直前に採った谷を維持して、境界での往復を抑える。

  静止背景の学習（`scan_plot_realtime.py` の background モード）は使わない。上の
  Intensity と点数の 2 本で足りる見込みで、学習の待ち時間もリセット操作も要らない。

ホールド（検出の隙間を埋める）:
  検出はスパースなので、生のまま流すと照準も削りも点滅する。最後の検出から一定時間は
  保持し続けるが、保持時間は 2 本に分けてある。`channel_hold_sec` は長く取っても実害が
  小さい（削っていなければ雪崩は流れない）のに対し、`shaving_hold_sec` はそのまま
  「削るのを止めてから雪崩が止まるまでの遅延」になるため。当日はこの 2 本が主要な
  調整ノブになる。

接続情報・回転数・検出範囲・谷の範囲・氷の判定パラメータ・出力先は `config.toml` から
読み込む。それ以外の描画パラメータは下の「設定」定数で調整する。

実行:
  uv run python valley_tracker.py              # 可視化つき（合わせ込み・本番どちらも）
  uv run python valley_tracker.py --headless   # 窓なし（本番で CPU を空けたいとき）
"""

import argparse
import math
import os
import sys
import threading
import time

import numpy as np

from config import (
    load_detection_config,
    load_ice_config,
    load_output_config,
    load_preview_config,
    load_scan_config,
    load_serial_config,
)
from lidar import TMiniPlus, max_points_per_rev

#=============================
# 設定
#=============================

# 接続・回転数・検出範囲は scan_plot_realtime.py と同じものを同じ流儀で読む。
PORTS, BAUDRATE = load_serial_config()
SCAN_FREQUENCY_HZ = load_scan_config()
MAX_POINTS_PER_REV = max_points_per_rev(SCAN_FREQUENCY_HZ)

RANGE_MIN_CM, RANGE_MAX_CM = load_detection_config()
RANGE_MIN_MM = RANGE_MIN_CM * 10
RANGE_MAX_MM = RANGE_MAX_CM * 10

# 氷の判定パラメータ。連続座標モードでは谷テーブル・max_points_per_valley・channel_hold_sec は
# 使わない（preview.toml の関心円が担う）。intensity_max と shaving_hold_sec だけ流用する。
ICE = load_ice_config()
INTENSITY_MAX = ICE["intensity_max"]
SHAVING_HOLD_SEC = ICE["shaving_hold_sec"]

# 関心円（連続座標モード。preview.toml）。x = r*cos(theta), y = -r*sin(theta) の mm フレーム。
_PREVIEW = load_preview_config()
CIRCLE_CENTER_MM = _PREVIEW["center_offset_mm"]          # (cx, cy)
CIRCLE_RADIUS_MM = _PREVIEW["radius_mm"]
CIRCLE_TOLERANCE_MM = _PREVIEW["tolerance_mm"]
CIRCLE_FILTER_MM = CIRCLE_RADIUS_MM + CIRCLE_TOLERANCE_MM  # フィルタはこの半径で通す（はみ出し許容込み）

# Unity が読む共有ファイル。
POSITION_FILE = load_output_config()

# 共有ファイルの書き込み間隔[秒]。Unity 側のポーリング間隔（VisionConfigSO の
# PollIntervalSec 既定 0.05）と揃えてある。速くしても Unity は読み取れない。
WRITE_INTERVAL_SEC = 0.05

# 表示範囲 rmax = RANGE_MAX × 倍率。円外のコンテキストも少し見えるようにする。
DISPLAY_SCALE = 1.2
DISPLAY_MAX_MM = RANGE_MAX_MM * DISPLAY_SCALE

# 現物合わせ用の角度オフセット[度]。センサー正面を画面上のどこに向けるか。
# 谷の角度は「図に見えているとおり」で指定するので、ここを回すと谷の指定も回る。
# 0 のままなら scan_plot_realtime.py と同じ見え方で、センサー正面は 225〜315 度
# （中心 270 度＝画面の左）に出る。合わせ込みの手戻りを避けるため既定は 0 にしてある。
ANGLE_OFFSET_DEG = 0.0

# 描画の更新間隔[ms]（読み取り・書き込みとは独立）
UPDATE_INTERVAL_MS = 50

#--- ウィンドウの大きさ[インチ] -----------------------------------------
FIGURE_WIDTH_IN = 9.0
PANEL_HEIGHT_IN = 4.4        # プロット 1 枚ぶん
TOP_MARGIN_IN = 0.7
BOTTOM_ROW_HEIGHT_IN = 1.1   # 下端のステータス行
LEFT_COLUMN_WIDTH_IN = 2.6   # プロットの左に空ける、台ごとの数値欄
COUNT_TEXT_LEFT_IN = 0.3
PANEL_GAP_RATIO = 0.55

# プロットの縦中心からの高さ[インチ]。
COUNT_HEAD_OFFSET_IN = 0.7
COUNT_VALUE_OFFSET_IN = -0.1
COUNT_NOW_OFFSET_IN = -0.8

STATUS_LEFT_IN = 0.3
STATUS_BOTTOM_IN = 0.45

#--- 点の描き方 ----------------------------------------------------------
# 合わせ込みでは「氷でない点がどこにいるか」も見たいので、有効な点はすべて灰色で描き、
# そのうち関心円内の氷を赤、採用中の代表点を緑の輪で重ねる。
ALL_POINT_COLOR = "0.75"
ICE_POINT_COLOR = "tab:red"

#--- フィルタの ON/OFF（現物合わせで切り替える）-------------------------
# scan_plot_realtime.py と同じ意味。AMBIENT / SPECULAR は SDK 側で効く。
# 落氷を取りこぼしたくないので、既定では SDK 側のノイズ除去は両方切ってある。
FILTER_ZERO_DISTANCE = True        # 距離 0（無反射・SDK が落とした点）を除外
FILTER_AMBIENT = False             # 外乱光（太陽光ノイズ）を SDK に除外させる
FILTER_SPECULAR = False            # 鏡面反射（ガラスノイズ）を SDK に除外させる
FILTER_BY_RANGE = True             # RANGE_MIN..RANGE_MAX の外を除外


def _is_valid(p):
    """フィルタ設定に照らして、その点を「有効な物体点」とみなすか。

    ここでは氷かどうかまでは見ない（Intensity・関心円の判定は IcePointExtractor）。
    scan_plot_realtime.py の同名関数と同じ判定にしてあるので、あちらで合わせ込んだ
    `[detection]` の値がそのままこちらでも同じ意味を持つ。
    """
    if FILTER_ZERO_DISTANCE and p["distance"] == 0:
        return False
    if FILTER_BY_RANGE and not (RANGE_MIN_MM <= p["distance"] <= RANGE_MAX_MM):
        return False
    return True


def _display_angle(angle_deg):
    """センサー角度を、図に見えている表示角[度]へ直す。

    谷の角度は図に見えているとおりで指定するので、判定も描画もこの角度で行う。
    """
    return (angle_deg + ANGLE_OFFSET_DEG) % 360.0


def _to_xy_mm(display_deg, distance_mm):
    """表示角[deg]・距離[mm] → LiDAR 原点フレームの (x, y)[mm]。
    x = r*cos(theta), y = -r*sin(theta)（theta=0°→+X, theta=270°→+Y）。"""
    rad = math.radians(display_deg)
    return distance_mm * math.cos(rad), -distance_mm * math.sin(rad)


def _xy_to_polar_deg_mm(x, y):
    """(x, y)[mm] → (表示角[deg], 半径[mm])。_to_xy_mm の逆（プロット用）。"""
    return math.degrees(math.atan2(-y, x)) % 360.0, math.hypot(x, y)


class IcePointExtractor:
    """1 回転ぶんの samples から「関心円内の氷の代表点」を取り出す純ロジック。

    LiDAR 1 台につき 1 つ。台ごとに独立して判定し切る（2 台の突き合わせは PositionWriter）。

      1. intensity < INTENSITY_MAX の有効点だけを氷の候補にする（氷は反射が弱い）。
      2. 各点を (x, y)[mm] へ変換し、関心円の中心から filter_radius_mm 以内の点だけ残す。
      3. 残った点のうち中心に最も近い 1 点を代表点として返す（無ければ None）。

    谷（channel）・max_points_per_valley の概念は無い。障害物対策は intensity と円の狭さで担う。
    状態は持たない（読み取りスレッドからのみ呼ばれる）。
    """

    def __init__(self, intensity_max, circle_center_mm, filter_radius_mm):
        self._intensity_max = intensity_max
        self._cx, self._cy = circle_center_mm
        self._filter_r2 = filter_radius_mm * filter_radius_mm

    def extract(self, samples):
        """(代表点 (x,y) or None, 円内の氷点 [(display_deg, distance_mm), ...]) を返す。"""
        best = None
        best_d2 = None
        in_circle = []
        for p in samples:
            if not _is_valid(p):
                continue
            if p["intensity"] >= self._intensity_max:
                continue
            display_deg = _display_angle(p["angle"])
            distance = p["distance"]
            x, y = _to_xy_mm(display_deg, distance)
            dx, dy = x - self._cx, y - self._cy
            d2 = dx * dx + dy * dy
            if d2 > self._filter_r2:
                continue
            in_circle.append((display_deg, distance))
            if best_d2 is None or d2 < best_d2:
                best_d2 = d2
                best = (x, y)
        return best, in_circle


class UnitState:
    """LiDAR 1 台ぶんの共有状態（読み取りスレッド → ライタ／描画スレッド）。

    持つのは 2 つだけ:
      - 直近の検出（谷と、その時刻）。ライタはこれを台ごとに見て突き合わせる
      - 直近 1 回転の点（描画用）。判定には使わない

    時刻は `time.monotonic()`。壁時計だと NTP 補正でホールドが飛ぶため。
    """

    def __init__(self):
        self._lock = threading.Lock()
        self._point = None           # 直近の代表点 (x, y)[mm] or None
        self._detected_at = None     # None = 起動してから一度も検出していない
        self._hit_count = 0          # 起動からの検出回数（ログ・表示用）
        self._all_points = np.zeros((0, 2))
        self._ice_points = np.zeros((0, 2))
        self._valid_count = 0

    def publish(self, point, ice_points, all_points, valid_count):
        """1 回転ぶんの結果を載せる。代表点があるときだけ検出時刻を更新する（ホールドの実体）。"""
        with self._lock:
            self._ice_points = np.asarray(ice_points, dtype=float).reshape(-1, 2)
            self._all_points = all_points
            self._valid_count = valid_count

            if point is not None:
                self._point = point
                self._detected_at = time.monotonic()
                self._hit_count += 1

    def detection(self):
        """(代表点 (x,y), 検出時刻) を返す。一度も検出していなければ None。"""
        with self._lock:
            if self._detected_at is None:
                return None

            return self._point, self._detected_at

    def snapshot(self):
        """描画用に (全点, 氷点, 直近1回転の有効点数, 検出回数) を返す（一度のロックで取る）。"""
        with self._lock:
            return (
                self._all_points,
                self._ice_points,
                self._valid_count,
                self._hit_count,
            )


def reader_loop(name, lidar, extractor, state, stop_event):
    """読み取りスレッド本体。停止フラグが立つまで 1 回転ぶんずつ読み続ける。

    LiDAR 1 台につき 1 本走る。エラーはその台のループを抜けるだけで、もう片方の台と
    ライタは動き続ける（片方が落ちても検出は続く）。
    """
    while not stop_event.is_set():
        try:
            samples = lidar.read_scan()
        except Exception as e:
            if not stop_event.is_set():
                print(f"[{name}] read_scan error: {e}", file=sys.stderr)
            break

        # 空 = SDK が 1 回転ぶんを揃えられなかった（タイムアウト・再接続中）。
        if not samples:
            continue

        point, ice_points = extractor.extract(samples)

        # 描画用の全点（有効なものすべて）。判定には使わないので同じループで作る
        valid = [
            (_display_angle(p["angle"]), p["distance"])
            for p in samples
            if _is_valid(p)
        ]
        all_points = np.asarray(valid, dtype=float).reshape(-1, 2)

        state.publish(point, ice_points, all_points, len(valid))

        if point is not None:
            print(f"[{name}] ice -> ({point[0]:7.1f}, {point[1]:7.1f}) mm")


class PositionWriter:
    """全台の検出を突き合わせ、共有ファイルへ `seq,x,y,shaving` を書き続ける。

    2 台の突き合わせはここで取る（最も新しい代表点を採る）。当てるのは shaving ホールドだけ:
    最後の検出から shaving_hold_sec 以内なら shaving=1 かつ直近の代表点を継続、それを過ぎたら
    「検知なし」＝ x,y 空欄（`seq,,,0`）。

    書き込みは temp へ書き切って os.replace() の原子的 rename（partial read なし）。氷が無い間も
    seq を進めるので、Unity は「正常な検知なし」と「Python 停止（seq 据置＝ロスト）」を区別できる。
    """

    def __init__(self, path, states, shaving_hold_sec, interval_sec):
        self._path = path
        self._tmp_path = path.with_name(path.name + ".tmp")
        self._states = states
        self._shaving_hold = shaving_hold_sec
        self._interval = interval_sec
        self._seq = 0
        self._lock = threading.Lock()
        self._last_line = (None, False)   # (point, shaving)。描画のステータス表示用

    def resolve(self):
        """(代表点 (x,y) or None, shaving) をいま時点で決める。"""
        latest = None

        for state in self._states:
            detection = state.detection()
            if detection is None:
                continue
            if latest is None or detection[1] > latest[1]:
                latest = detection

        if latest is None:
            return None, False

        point, detected_at = latest
        age = time.monotonic() - detected_at
        shaving = age <= self._shaving_hold

        # shaving ホールド中は直近の代表点を継続。過ぎたら検知なし。
        return (point if shaving else None), shaving

    def status(self):
        """直近に書いた (point, shaving, seq) を返す（描画のステータス表示用）。"""
        with self._lock:
            return self._last_line[0], self._last_line[1], self._seq

    def run(self, stop_event):
        """ライタスレッド本体。停止フラグが立つまで一定間隔で書き続ける。"""
        self._path.parent.mkdir(parents=True, exist_ok=True)

        while not stop_event.is_set():
            point, shaving = self.resolve()

            with self._lock:
                self._seq += 1
                self._last_line = (point, shaving)
                seq = self._seq

            if point is None:
                line = f"{seq},,,{1 if shaving else 0}\n"
            else:
                line = f"{seq},{point[0]:.1f},{point[1]:.1f},{1 if shaving else 0}\n"

            try:
                # temp へ書き切って fsync してから rename。rename は同一ディレクトリ内
                # なので原子的で、Unity が中途半端な行を読むことがない
                with open(self._tmp_path, "w", encoding="utf-8") as f:
                    f.write(line)
                    f.flush()
                    os.fsync(f.fileno())
                os.replace(self._tmp_path, self._path)
            except OSError as e:
                # 書けなくても計測は続ける（Unity 側は seq 停止でロストとして扱う）
                print(f"[writer] 書き込み失敗 ({self._path}): {e}", file=sys.stderr)

            stop_event.wait(self._interval)


class LidarUnit:
    """LiDAR 1 台ぶんの一式（センサー・判定・共有状態・自分のプロット）。

    寿命は main() が持つ:  start() → （可視化なら）build() → refresh() を毎フレーム → stop()。
    """

    def __init__(self, name, port, baudrate):
        self.name = name
        self.port = port
        self.state = UnitState()
        self._extractor = IcePointExtractor(INTENSITY_MAX, CIRCLE_CENTER_MM, CIRCLE_FILTER_MM)

        # コンストラクタは SDK に設定を積むだけで、まだ接続しない（接続は start()）
        self._lidar = TMiniPlus(
            port,
            baudrate,
            filter_sun_noise=FILTER_AMBIENT,
            filter_glass_noise=FILTER_SPECULAR,
            scan_frequency_hz=SCAN_FREQUENCY_HZ,
        )

        self._thread = None
        self._ax = None
        self._all_scat = None
        self._ice_scat = None
        self._rep_scat = None
        self._head_text = None
        self._value_text = None
        self._now_text = None

    #--- センサー ---------------------------------------------------------

    def start(self, stop_event):
        """接続してスキャンを開始し、この台の読み取りスレッドを走らせる。"""
        self._lidar.start_scan()

        self._thread = threading.Thread(
            target=reader_loop,
            args=(self.name, self._lidar, self._extractor, self.state, stop_event),
            daemon=True,
        )
        self._thread.start()

    def stop(self):
        """読み取りスレッドの終了を待ってから、スキャンを止めて接続を閉じる。

        呼ぶ前に stop_event を立てておくこと。start() 前・二重呼び出しでも無害。
        """
        if self._thread is not None:
            # SDK の 1 回転待ちは最大 1 秒。その中にいる間に turnOff すると危ないので、
            # 余裕を持って抜けきるのを待つ
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

        # 関心円（radius）とフィルタ円（radius + tolerance）。xy 平面で円を作って polar へ写す。
        t = np.linspace(0.0, 2.0 * np.pi, 181)
        for r_mm, kw in (
            (CIRCLE_RADIUS_MM, dict(color="tab:blue", linewidth=1.4, alpha=0.85)),
            (CIRCLE_FILTER_MM, dict(color="tab:blue", linewidth=0.9, alpha=0.35, linestyle="--")),
        ):
            xs = CIRCLE_CENTER_MM[0] + r_mm * np.cos(t)
            ys = CIRCLE_CENTER_MM[1] + r_mm * np.sin(t)
            deg = np.degrees(np.arctan2(-ys, xs)) % 360.0
            ax.plot(np.radians(deg), np.hypot(xs, ys), **kw)

        # 山の中心（＝関心円の中心）マーカー
        cc_deg, cc_r = _xy_to_polar_deg_mm(*CIRCLE_CENTER_MM)
        ax.plot([np.radians(cc_deg)], [cc_r], marker="+", color="tab:blue",
                markersize=13, markeredgewidth=1.6, linestyle="none")

        # 有効な点はすべて灰色、円内の氷を赤、採用中の代表点を緑の輪で重ねる
        self._all_scat = ax.scatter([], [], s=6, c=ALL_POINT_COLOR, zorder=2)
        self._ice_scat = ax.scatter([], [], s=36, c=ICE_POINT_COLOR, zorder=3)
        self._rep_scat = ax.scatter([], [], s=100, facecolors="none",
                                    edgecolors="tab:green", linewidths=1.8, zorder=4)

        # 検出範囲の外周（赤）／内周（橙・RANGE_MIN=0 のときは描かない）
        circle_theta = np.linspace(0, 2 * np.pi, 361)
        ax.plot(circle_theta, np.full_like(circle_theta, RANGE_MAX_MM),
                color="tab:red", linewidth=1.0, alpha=0.5)
        if RANGE_MIN_MM > 0:
            ax.plot(circle_theta, np.full_like(circle_theta, RANGE_MIN_MM),
                    color="tab:orange", linewidth=1.0, alpha=0.5)

        # 全アーティストを追加した後に表示範囲を固定する
        ax.set_rlim(0, DISPLAY_MAX_MM)
        ax.autoscale(False)

        self._build_count_text(fig)
        self._set_title(0)

    def refresh(self, rep_point):
        """最新の点群でこの台のプロットを描き替える。毎フレーム呼ぶ。

        rep_point は writer が選んだ代表点 (x,y)[mm] or None（全パネル共通で表示する）。
        """
        all_points, ice_points, valid_count, hit_count = self.state.snapshot()

        self._all_scat.set_offsets(_to_offsets(all_points))
        self._ice_scat.set_offsets(_to_offsets(ice_points))

        if rep_point is None:
            self._rep_scat.set_offsets(np.zeros((0, 2)))
        else:
            deg, rr = _xy_to_polar_deg_mm(*rep_point)
            self._rep_scat.set_offsets([[np.radians(deg), rr]])

        self._set_title(valid_count)
        self._set_counts(hit_count)

        return self._all_scat

    def _build_count_text(self, fig):
        """ウィンドウ左端の数値欄の文字を作る（1 度だけ呼ぶ）。

        置き場所は窓の大きさで変わるので place_count_text() が持つ。
        """
        def block(fontsize, **kwargs):
            return fig.text(
                0.0, 0.0, "", va="center", ha="left", fontsize=fontsize, **kwargs
            )

        self._head_text = block(12)
        self._value_text = block(11, family="monospace")
        self._now_text = block(11, family="monospace")

    def place_count_text(self, fig):
        """左の数値欄を、自分のプロットと同じ高さに置き直す。

        位置は図に対する割合なので、リサイズのたびに呼ぶ（窓を広げても実寸が
        変わらないよう、インチで持った値を毎回換算し直す）。
        """
        box = self._ax.get_position()
        center_y = (box.y0 + box.y1) / 2
        left = COUNT_TEXT_LEFT_IN / fig.get_figwidth()
        height_in = fig.get_figheight()

        for text, offset_in in (
            (self._head_text, COUNT_HEAD_OFFSET_IN),
            (self._value_text, COUNT_VALUE_OFFSET_IN),
            (self._now_text, COUNT_NOW_OFFSET_IN),
        ):
            text.set_position((left, center_y + offset_in / height_in))

    def _set_counts(self, hit_count):
        """左の数値欄を書き替える。毎フレーム呼ぶ。"""
        self._head_text.set_text(f"{self.name}\n{hit_count} hits")
        self._value_text.set_text(
            f"circle center\n({CIRCLE_CENTER_MM[0]:.0f}, {CIRCLE_CENTER_MM[1]:.0f}) mm\n"
            f"r {CIRCLE_RADIUS_MM:.0f}  +tol {CIRCLE_TOLERANCE_MM:.0f}"
        )
        self._now_text.set_text(f"intensity < {INTENSITY_MAX:g}")

    def _set_title(self, valid_count):
        """2 行のタイトルを書き替える（どの台か / 点の出方）。"""
        self._ax.set_title(
            f"{self.name}   {self.port}   (range = {RANGE_MIN_CM}-{RANGE_MAX_CM} cm)\n"
            f"{SCAN_FREQUENCY_HZ:.1f} Hz"
            f"   max {MAX_POINTS_PER_REV} pts/rev"
            f"   detected {valid_count:4d} pts",
            fontsize=10,
        )


def _to_offsets(points):
    """(表示角[度], 距離[mm]) の配列を、極座標散布図の offsets へ直す。"""
    if len(points) == 0:
        return np.zeros((0, 2))

    return np.column_stack((np.radians(points[:, 0]), points[:, 1]))


def build_units():
    """config.toml のポートの並び順で LiDAR1, LiDAR2 …（＝画面の上から下）を作る。"""
    return [
        LidarUnit(f"LiDAR{i + 1}", port, BAUDRATE)
        for i, port in enumerate(PORTS)
    ]


def start_units(units, stop_event):
    """全台を開いて読み取りを始める。1 台でも開けなければ全部閉じてから投げ直す。

    開きっぱなしのまま落ちると、次の起動でそのポートを掴めなくなる。
    """
    started = []

    try:
        for unit in units:
            unit.start(stop_event)
            started.append(unit)
    except Exception:
        stop_event.set()
        for unit in started:
            unit.stop()
        raise


def run_headless(stop_event):
    """窓なしで走らせる。Ctrl-C まで待つだけ（読み取りとライタは別スレッド）。"""
    print("headless: Ctrl-C で停止します")

    try:
        while not stop_event.is_set():
            stop_event.wait(0.5)
    except KeyboardInterrupt:
        print()
        print("Interrupted")


def run_plot(units, writer, stop_event, shutdown):
    """可視化つきで走らせる。ウィンドウを閉じるか Ctrl-C で安全停止する。"""
    import matplotlib.animation as animation
    import matplotlib.pyplot as plt

    figure_height_in = (
        PANEL_HEIGHT_IN * len(units) + TOP_MARGIN_IN + BOTTOM_ROW_HEIGHT_IN
    )
    fig = plt.figure(figsize=(FIGURE_WIDTH_IN, figure_height_in))

    manager = fig.canvas.manager
    if manager is not None:
        manager.set_window_title("LiDAR valley tracker")

    for i, unit in enumerate(units):
        unit.build(fig, fig.add_subplot(len(units), 1, i + 1, projection="polar"))

    # 下端に、いま Unity へ書いている値をそのまま出す。画面の点と出力の突き合わせが
    # ここだけでできるようにするため（別窓・ログを見に行かなくてよい）
    status_text = fig.text(0.0, 0.0, "", va="center", ha="left", fontsize=13,
                           family="monospace")

    def apply_layout(_event=None):
        """インチで決めた配置を、いまの窓の大きさに合わせて割合へ換算し直す。

        これをしないと、窓を広げた分だけ左の数値欄・プロットが一緒に間延びして、
        数値とプロットが遠く離れてしまう。
        """
        width_in = fig.get_figwidth()
        height_in = fig.get_figheight()

        left = min(LEFT_COLUMN_WIDTH_IN / width_in, 0.6)
        bottom = min(BOTTOM_ROW_HEIGHT_IN / height_in, 0.4)
        top = max(1.0 - TOP_MARGIN_IN / height_in, bottom + 0.2)

        fig.subplots_adjust(
            left=left,
            right=0.97,
            top=top,
            bottom=bottom,
            hspace=PANEL_GAP_RATIO,
        )

        for unit in units:
            unit.place_count_text(fig)

        status_text.set_position((
            STATUS_LEFT_IN / width_in,
            STATUS_BOTTOM_IN / height_in,
        ))

    apply_layout()
    fig.canvas.mpl_connect("resize_event", apply_layout)

    def update(_frame):
        point, shaving, seq = writer.status()
        loc = "  none        " if point is None else f"({point[0]:7.1f}, {point[1]:7.1f})"

        status_text.set_text(
            f"seq {seq:>7d}   xy {loc} mm"
            f"   shaving {'ON ' if shaving else 'off'}\n"
            f"{POSITION_FILE}"
        )

        return [unit.refresh(point) for unit in units]

    # 参照を捨てると GC でアニメーションが止まるので、ローカルに持っておく
    ani = animation.FuncAnimation(
        fig,
        update,
        interval=UPDATE_INTERVAL_MS,
        blit=False,
        cache_frame_data=False,
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
        description="LiDAR の落氷検知を氷の連続座標 (x,y)[mm] + 削り状態にして Unity へ渡す"
    )
    parser.add_argument(
        "--headless",
        action="store_true",
        help="可視化ウィンドウを開かない（本番で CPU を空けたいとき）",
    )
    args = parser.parse_args()

    units = build_units()
    stop_event = threading.Event()

    start_units(units, stop_event)

    writer = PositionWriter(
        POSITION_FILE,
        [unit.state for unit in units],
        SHAVING_HOLD_SEC,
        WRITE_INTERVAL_SEC,
    )
    writer_thread = threading.Thread(
        target=writer.run, args=(stop_event,), daemon=True
    )
    writer_thread.start()

    print(f"position file = {POSITION_FILE}")
    print(
        f"circle: center ({CIRCLE_CENTER_MM[0]:.0f}, {CIRCLE_CENTER_MM[1]:.0f}) mm, "
        f"r {CIRCLE_RADIUS_MM:.0f} (+tol {CIRCLE_TOLERANCE_MM:.0f}); "
        f"intensity < {INTENSITY_MAX:g}, shaving hold {SHAVING_HOLD_SEC:g}s"
    )

    def shutdown():
        # 二重呼び出しに備えてフラグで一度きりに
        if stop_event.is_set():
            return
        stop_event.set()
        writer_thread.join(timeout=3.0)
        for unit in units:
            unit.stop()

    try:
        if args.headless:
            run_headless(stop_event)
        else:
            run_plot(units, writer, stop_event, shutdown)
    finally:
        # Ctrl-C・ウィンドウを閉じる、どちらでも必ず停止処理を通す
        shutdown()


if __name__ == "__main__":
    main()
