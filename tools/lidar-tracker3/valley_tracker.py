"""LiDAR の落氷検知 → 氷の連続座標 + 削り状態（Kakigori TD / lidar-tracker3）。

兄弟ツール `tools/lidar-tracker`（YDLIDAR T-mini Plus）の同名スクリプトを
**Slamtec RPLIDAR S1** 向けに移したもの。判定・出力・スレッド構成はそのままで、
違うのはセンサーを開く 1 行（`TMiniPlus` → `RPLidarS1`）と、機種差の吸収だけ:

  - **quality のレンジが 6bit（0〜63）**。T-mini Plus の 8bit（0〜255）とは違うので
    `[ice] intensity_max` は取り直す。express モードでは quality が無い（常に 0）。
  - **SDK 側のノイズ除去が無い**。T-mini Plus では太陽光／ガラス反射の除去を SDK に
    任せられた（FILTER_AMBIENT / FILTER_SPECULAR）が、RPLIDAR に相当機能は無い。
    距離 0・レンジ外の除去（`_is_valid`）と関心円だけで絞る。

ファイル名は兄弟ツールから引き継いだ「valley（谷）」のままだが、**谷（channel）の分類は
しない**（phase-preview-feedback.md / ADR-29）。検出した氷の**代表点の座標**を Unity へ渡し、
Unity 側がその座標を山上のワールド位置へ写して、そこから雪玉を放出する。

出力は 1 行 `seq,x,y,shaving`（例 `1837,12.5,-284.0,1`）。座標は LiDAR 原点フレーム[mm]で、
表示角 0 度を基準にした縦横のフレーム:
    x = sx * r * sin(theta) ,  y = sy * r * cos(theta)  （theta = 表示角[deg]、r = distance[mm]）
sx / sy は FLIP_HORIZONTAL / FLIP_VERTICAL で決まる ±1。反転が両方 False なら
theta=0°→+Y・theta=90°→+X（方位磁針と同じ時計回り）。**既定は両方 True** なので
theta=0°→-Y、theta=90°→-X（＝図では 0 度が画面の真下、90 度が左）。
センサーの生角のどこを表示角 0 度にするかは ANGLE_OFFSET_DEG で合わせる。

関心円の中心は既定で出力フレームの +Y へ radius_mm ぶん（(0, +300)）＝**円の下端が原点で
横軸に接する**位置にあり、円は丸ごと機体の片側に乗る。円の中心に落ちた氷はおよそ (0, +300)。
その +Y が表示角のどちらを向くかは反転で決まる（既定＝両方反転では表示角 180 度の側）。

代表点は preview.toml の関心円（中心オフセット・半径・はみ出し許容）の中の 1 点。選び方は
`[representative] mode`：既定 "nearest_lidar"（LiDAR に最も近い点。混合画素の尻尾対策）／
"nearest_center"（円中心に最も近い点）。検知なしは `seq,,,0`（x,y 空欄）。temp へ書き切って `os.replace()` の原子的
rename（partial read を排除）・`seq` 単調増加はそのまま。Unity は `seq` の停止で「ロスト」を
検知し、検知点なし・削りなし（安全側）へ倒す。

構成（台数 + 1 スレッド、+ 可視化なら メインスレッド）:
  - 読み取りスレッド : 台ごとに 1 本。read_scan() で 1 回転ぶんを読み、その回転を
                       そのまま判定して「代表点と時刻」を自分の共有状態に書く
  - ライタスレッド   : 50ms ごとに全台の共有状態を突き合わせ、共有ファイルへ 1 行書く
  - メインスレッド   : 可視化ありなら FuncAnimation で再描画。--headless なら待つだけ

可視化（`MergedView`）:
  複数台は同じ落氷域を同一座標フレームで見る冗長構成なので、画面も **全台を束ねた
  単一の点群プロット**にする（台ごとにパネルを割らない）。全台の有効点を重ねて灰色、
  関心円内の氷を赤、ライタが選んだ代表点を緑の輪で出す。左の欄に台ごとの生存確認
  （直近の有効点数・累計ヒット数）を出すので、どの台が効いていないかはそこで分かる。

設置の前提:
  LiDAR は**落氷域の縁**に、正面を落氷域へ向けて据える。関心円は
  `center_offset_mm = [0, radius_mm]` で、下端がちょうど機体の位置（原点）に接する
  ＝ 円が丸ごと前方に乗る。別の据え方をするときは `preview.toml` でずらす。
  複数台使うときはラックの上下段に並べ、水平位置を揃える（各台の座標フレームが
  一致するので、関心円の設定を 1 個で共有できる）。違うのは氷を捉える高さだけ。

氷かどうかの判定（複数台の OR は「判定した後」に取る）:
  LiDAR は氷を毎回転は拾えない（T-mini Plus の実測で 0.75〜2 秒に 1 回）。取りこぼしを
  減らすために台数を増やすが、回転の位相は同期していないので点を合算はしない。台ごとに
  1 回転を独立に判定し切り、結果として出た「代表点と時刻」だけをライタが突き合わせて、
  **最も新しい検出**を採る。

  判定は 2 段:
    - 氷は反射が弱く quality が低い / 障害物（人・機体・造形物）は高い
      → `intensity_max` **未満**だけを氷の候補にする
      → ただし express モードでは quality が来ない（常に 0）ので、この段は効かない
    - 関心円（`preview.toml` の中心・半径・はみ出し許容）の外は捨てる
      → 壁・周囲の人・かき氷機の筐体をここで落とす。express モードではこれが唯一の
        障害物対策になるので、円は必要最小限に絞ること
  残った点から代表点を 1 点選ぶ（`[representative] mode`）。既定 "nearest_lidar" は
  LiDAR に最も近い点＝距離 r が最小の点。小さな物体を測ると照射軸に沿って裏側へ点が
  尾を引く（混合画素 / mixed pixel）が、その尾は必ず本体より奥なので、手前端が本体に
  最も近い。"nearest_center" は円中心に最も近い点（旧既定）。

  静止背景の学習は使わない。quality と円の狭さで足りる見込みで、学習の待ち時間も
  リセット操作も要らない。

ホールド（検出の隙間を埋める）:
  検出はスパースなので、生のまま流すと削りが点滅する。最後の検出から
  `shaving_hold_sec` の間は「削り中」と代表点を保持し続ける。この値がそのまま
  「削るのを止めてから雪崩が止まるまでの遅延」になるので、当日の主要な調整ノブになる。

接続情報・スキャン設定・検出範囲・氷の判定パラメータ・出力先は `config.toml` から、
関心円は `preview.toml` から読み込む。それ以外の描画パラメータは下の「設定」定数で調整する。

実行:
  uv run python probe.py                       # 先に疎通確認（機種情報・モード一覧・点の出方）
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
from lidar import RPLidarS1, max_points_per_rev, sample_rate_k_for

#=============================
# 設定
#=============================

# 接続・スキャン・検出範囲は config.toml から読む。
PORTS, BAUDRATE = load_serial_config()

_SCAN = load_scan_config()
SCAN_FREQUENCY_HZ = _SCAN["frequency_hz"]
SCAN_MODE = _SCAN["mode"]                  # "standard"（quality つき）/ "express"（点数優先）
SCAN_MODE_ID = _SCAN["mode_id"]            # express のモード id。None なら機体の推奨値
MAX_POINTS_PER_REV = max_points_per_rev(
    SCAN_FREQUENCY_HZ, sample_rate_k_for(SCAN_MODE)
)

RANGE_MIN_CM, RANGE_MAX_CM = load_detection_config()
RANGE_MIN_MM = RANGE_MIN_CM * 10
RANGE_MAX_MM = RANGE_MAX_CM * 10

# 氷の判定パラメータ。連続座標モードで使うのは intensity_max と shaving_hold_sec だけ
# （谷の分類はせず、絞り込みは preview.toml の関心円が担う）。
ICE = load_ice_config()
INTENSITY_MAX = ICE["intensity_max"]
SHAVING_HOLD_SEC = ICE["shaving_hold_sec"]

# 関心円（連続座標モード。preview.toml）。出力フレーム[mm]（_to_xy_mm 参照）。
# 既定の中心は北へ radius_mm ぶん（下端が原点で 90°-270° の線に接する）。
_PREVIEW = load_preview_config()
CIRCLE_CENTER_MM = _PREVIEW["center_offset_mm"]          # (cx, cy)
CIRCLE_RADIUS_MM = _PREVIEW["radius_mm"]
CIRCLE_TOLERANCE_MM = _PREVIEW["tolerance_mm"]
CIRCLE_FILTER_MM = CIRCLE_RADIUS_MM + CIRCLE_TOLERANCE_MM  # フィルタはこの半径で通す（はみ出し許容込み）

# 円内に氷点が複数あるときの代表点の選び方（preview.toml [representative] mode）。
# 既定 "nearest_lidar"（LiDAR に最も近い点）。下の IcePointExtractor 参照。
REPRESENTATIVE_MODE = _PREVIEW["representative_mode"]

# Unity が読む共有ファイル。
POSITION_FILE = load_output_config()

# 共有ファイルの書き込み間隔[秒]。Unity 側のポーリング間隔（VisionConfigSO の
# PollIntervalSec 既定 0.05）と揃えてある。速くしても Unity は読み取れない。
WRITE_INTERVAL_SEC = 0.05

# 表示範囲 rmax = RANGE_MAX × 倍率。円外のコンテキストも少し見えるようにする。
DISPLAY_SCALE = 1.2
DISPLAY_MAX_MM = RANGE_MAX_MM * DISPLAY_SCALE

# ┌─────────────────────────────────────────────────────────────────────┐
# │ 角度合わせはこの 1 行だけをいじる（judgment も plot も position.txt も一緒に回る）│
# └─────────────────────────────────────────────────────────────────────┘
#
# 意味: センサーの「生角」を、判定・描画で使う「表示角」へ回す量[度]。
#
# 合わせ方: LiDAR は正面を落氷域へ向けて置く。氷は正面（＝関心円の中心 (0,+300)）に
#   落ちるようにする。関心円は既定の反転では **表示角 180 度＝出力 +Y の側**にあるので、
#   「筐体の正面の生角」が表示角 180 度に来るように回す:
#
#       ANGLE_OFFSET_DEG = (180 − 正面の生角) mod 360
#
#   正面の生角 →   0° |  90° | 180° | 270°
#   入れる値   → 180  |  90  |   0  | 270
#
#   分からなければ: valley_tracker.py を可視化つきで起動し、正面へ氷（か手）を出して、
#   画面の赤い点が真上（12 時方向）に来るまでこの値を 90 ずつ回す。真上＝表示角 180°。
#
# **この S1 は正面が生角 0 度**なので 180.0。
ANGLE_OFFSET_DEG = 180.0

# 左右／上下の反転。設置の向き（LiDAR をどちら向きに置くか・どちらから見た図にするか）で
# 東西・南北が入れ替わるので、ここ 2 か所で吸収する。**Unity 側の flipX / flipY では
# 直さない**（補正が二重になって原因が読めなくなる。lidar-tracker からの申し送り）。
#
# 図（プロット）と出力（position.txt）は必ず同じ向きになる。片方だけ反転することは無い。
# 両方 True は「180 度回転」と同じなので、そうなったら ANGLE_OFFSET_DEG += 180 にして
# 両方 False に畳んでもよい（意味は同じ。どちらが読みやすいかで選ぶ）。
FLIP_HORIZONTAL = True
FLIP_VERTICAL = True

# 描画の更新間隔[ms]（読み取り・書き込みとは独立）
UPDATE_INTERVAL_MS = 50

#--- ウィンドウの大きさ[インチ] -----------------------------------------
# 単一の点群プロット。台数が増えてもパネルは割らない（全台を 1 枚に束ねる）。
FIGURE_WIDTH_IN = 9.5
FIGURE_HEIGHT_IN = 9.5
TOP_MARGIN_IN = 1.0          # 上端の 2 行タイトル
BOTTOM_ROW_HEIGHT_IN = 1.1   # 下端のステータス行
LEFT_COLUMN_WIDTH_IN = 2.6   # プロットの左に空ける、台ごとの数値欄
COUNT_TEXT_LEFT_IN = 0.3

STATUS_LEFT_IN = 0.3
STATUS_BOTTOM_IN = 0.45

#--- 点の描き方 ----------------------------------------------------------
# 合わせ込みでは「氷でない点がどこにいるか」も見たいので、有効な点はすべて灰色で描き、
# そのうち関心円内の氷を赤、採用中の代表点を緑の輪で重ねる。
ALL_POINT_COLOR = "0.75"
ICE_POINT_COLOR = "tab:red"

#--- フィルタの ON/OFF（現物合わせで切り替える）-------------------------
# T-mini Plus 版にあった FILTER_AMBIENT / FILTER_SPECULAR（太陽光・ガラス反射の除去を
# SDK に任せるスイッチ）は無い。RPLIDAR に相当する機能が無いため。
FILTER_ZERO_DISTANCE = True        # 距離 0（無反射・レンジ外として落とした点）を除外
FILTER_BY_RANGE = True             # RANGE_MIN..RANGE_MAX の外を除外


def _is_valid(p):
    """フィルタ設定に照らして、その点を「有効な物体点」とみなすか。

    ここでは氷かどうかまでは見ない（Intensity・関心円の判定は IcePointExtractor）。
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


def _flip_signs():
    """左右／上下の反転を (x の符号, y の符号) の数値にする。座標変換はここ 1 か所を見る。"""
    return (
        -1.0 if FLIP_HORIZONTAL else 1.0,
        -1.0 if FLIP_VERTICAL else 1.0,
    )


def _to_xy_mm(display_deg, distance_mm):
    """表示角[deg]・距離[mm] → LiDAR 原点フレームの (x, y)[mm]。

    **0 度を基準にした縦横のフレーム**:

        x = sx * r*sin(theta) ,  y = sy * r*cos(theta)

    sx / sy は _flip_signs()（FLIP_HORIZONTAL / FLIP_VERTICAL）で決まる ±1。
    反転が両方とも False なら theta=0°→+Y・theta=90°→+X（方位磁針と同じ時計回り）で、
    反転するとその軸が裏返る。既定は両方 True なので theta=0°→-Y、theta=90°→-X。
    """
    rad = math.radians(display_deg)
    sign_x, sign_y = _flip_signs()

    return (
        sign_x * distance_mm * math.sin(rad),
        sign_y * distance_mm * math.cos(rad),
    )


def _xy_to_polar_deg_mm(x, y):
    """(x, y)[mm] → (表示角[deg], 半径[mm])。_to_xy_mm の逆（プロット用）。"""
    sign_x, sign_y = _flip_signs()

    return (
        math.degrees(math.atan2(sign_x * x, sign_y * y)) % 360.0,
        math.hypot(x, y),
    )


# 代表点の選び方（preview.toml [representative] mode）。
#   "nearest_lidar"  : LiDAR に最も近い点（＝距離 r が最小）。混合画素（mixed pixel）の
#                      尻尾は必ず本体より奥へ伸びるので、先頭＝手前側が本体に一番近い。
#                      小さな落氷にはこちらが既定。
#   "nearest_center" : 関心円の中心に最も近い点（旧既定）。背景が円内に入り込むような
#                      置き方で、中心付近だけを信じたいときに使う。
REPRESENTATIVE_MODES = ("nearest_lidar", "nearest_center")


class IcePointExtractor:
    """1 回転ぶんの samples から「関心円内の氷の代表点」を取り出す純ロジック。

    LiDAR 1 台につき 1 つ。台ごとに独立して判定し切る（2 台の突き合わせは PositionWriter）。

      1. intensity < INTENSITY_MAX の有効点だけを氷の候補にする（氷は反射が弱い）。
      2. 各点を (x, y)[mm] へ変換し、関心円の中心から filter_radius_mm 以内の点だけ残す。
      3. 残った点から mode に従って代表点を 1 つ選んで返す（無ければ None）。
         - "nearest_lidar"  : 距離 r（＝原点からの hypot）が最小の点
         - "nearest_center" : 関心円の中心に最も近い点

    谷（channel）・max_points_per_valley の概念は無い。障害物対策は intensity と円の狭さで担う。
    状態は持たない（読み取りスレッドからのみ呼ばれる）。
    """

    def __init__(self, intensity_max, circle_center_mm, filter_radius_mm,
                 mode="nearest_lidar"):
        self._intensity_max = intensity_max
        self._cx, self._cy = circle_center_mm
        self._filter_r2 = filter_radius_mm * filter_radius_mm
        if mode not in REPRESENTATIVE_MODES:
            raise ValueError(
                f"未知の代表点モード {mode!r}（{', '.join(REPRESENTATIVE_MODES)} のいずれか）"
            )
        self._mode = mode

    def extract(self, samples):
        """(代表点 (x,y) or None, 円内の氷点 [(display_deg, distance_mm), ...]) を返す。"""
        best = None
        best_key = None
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

            # どちらのモードも「小さいほど代表に近い」キーで比較する
            key = distance if self._mode == "nearest_lidar" else d2
            if best_key is None or key < best_key:
                best_key = key
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
    """LiDAR 1 台ぶんのセンサー一式（接続・判定・共有状態・読み取りスレッド）。

    描画は持たない（全台まとめて `MergedView` が受け持つ）。
    寿命は main() が持つ:  start() → stop()。
    """

    def __init__(self, name, port, baudrate):
        self.name = name
        self.port = port
        self.state = UnitState()
        self._extractor = IcePointExtractor(
            INTENSITY_MAX, CIRCLE_CENTER_MM, CIRCLE_FILTER_MM, REPRESENTATIVE_MODE
        )

        # コンストラクタは設定を持つだけで、まだポートを開かない（接続は start()）
        self._lidar = RPLidarS1(
            port,
            baudrate,
            scan_mode=SCAN_MODE,
            scan_frequency_hz=SCAN_FREQUENCY_HZ,
            mode_id=SCAN_MODE_ID,
        )

        self._thread = None

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
            # read_scan() の 1 回転待ちは最大 1 秒。その中にいる間にポートを閉じると
            # 危ないので、余裕を持って抜けきるのを待つ
            self._thread.join(timeout=3.0)
            self._thread = None

        self._lidar.stop_scan()
        self._lidar.close()


class MergedView:
    """全台を束ねた単一の点群プロット（複数台＝冗長構成の OR 表示）。

    全台は同じ落氷域を同一座標フレームで見る前提なので、点は変換せずそのまま重ねる:
      - 全台の有効点（`_is_valid`）を灰色で重ねる
      - 全台の「関心円内の氷点」（intensity < 閾値 かつ 円内）を赤で重ねる
      - ライタが選んだ代表点（＝いま Unity へ書いている点）を緑の輪で 1 つ

    左の欄に台ごとの生存確認（直近の有効点数・累計ヒット数）を出す。ある台の数字が
    伸びない＝その台が効いていない、というのがここだけで分かる。

    寿命は run_plot() が持つ:  build() → refresh() を毎フレーム。
    """

    def __init__(self, units):
        self._units = units
        self._ax = None
        self._all_scat = None
        self._ice_scat = None
        self._rep_scat = None
        self._unit_text = None
        self._status_text = None

    def build(self, fig):
        """単一の極座標プロットと左右の文字欄を組み立てる（1 度だけ呼ぶ）。"""
        ax = fig.add_subplot(1, 1, 1, projection="polar")
        self._ax = ax

        # 図を出力フレームと一致させる。上下反転は 0 度の位置（真上／真下）、
        # 左右反転は角度の回り方に効く。両方反転すると回り方は元に戻る（＝180 度回転）
        ax.set_theta_zero_location("S" if FLIP_VERTICAL else "N")
        ax.set_theta_direction(-1 if FLIP_HORIZONTAL == FLIP_VERTICAL else 1)
        ax.set_rlabel_position(135)

        # 関心円（radius）とフィルタ円（radius + tolerance）。xy 平面で円を作って polar へ写す。
        t = np.linspace(0.0, 2.0 * np.pi, 181)
        for r_mm, kw in (
            (CIRCLE_RADIUS_MM, dict(color="tab:blue", linewidth=1.4, alpha=0.85)),
            (CIRCLE_FILTER_MM, dict(color="tab:blue", linewidth=0.9, alpha=0.35, linestyle="--")),
        ):
            xs = CIRCLE_CENTER_MM[0] + r_mm * np.cos(t)
            ys = CIRCLE_CENTER_MM[1] + r_mm * np.sin(t)
            # _xy_to_polar_deg_mm と同じ逆変換（こちらは配列なので numpy で書く）
            sign_x, sign_y = _flip_signs()
            deg = np.degrees(np.arctan2(sign_x * xs, sign_y * ys)) % 360.0
            ax.plot(np.radians(deg), np.hypot(xs, ys), **kw)

        # 山の中心（＝関心円の中心）マーカー
        cc_deg, cc_r = _xy_to_polar_deg_mm(*CIRCLE_CENTER_MM)
        ax.plot([np.radians(cc_deg)], [cc_r], marker="+", color="tab:blue",
                markersize=13, markeredgewidth=1.6, linestyle="none")

        # 全台の有効点は灰色、全台の円内の氷を赤、採用中の代表点を緑の輪で重ねる
        self._all_scat = ax.scatter([], [], s=6, c=ALL_POINT_COLOR, zorder=2)
        self._ice_scat = ax.scatter([], [], s=36, c=ICE_POINT_COLOR, zorder=3)
        self._rep_scat = ax.scatter([], [], s=140, facecolors="none",
                                    edgecolors="tab:green", linewidths=2.0, zorder=4)

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

        self._unit_text = fig.text(0.0, 0.0, "", va="center", ha="left",
                                   fontsize=11, family="monospace")
        self._status_text = fig.text(0.0, 0.0, "", va="center", ha="left",
                                     fontsize=13, family="monospace")
        self._set_title(0, 0)

    def place_text(self, fig):
        """左の台ごと欄と下端のステータス行を、いまの窓の大きさに合わせて置き直す。

        位置は図に対する割合なので、リサイズのたびに呼ぶ（インチで持った値を毎回換算）。
        """
        box = self._ax.get_position()
        center_y = (box.y0 + box.y1) / 2
        self._unit_text.set_position(
            (COUNT_TEXT_LEFT_IN / fig.get_figwidth(), center_y)
        )
        self._status_text.set_position((
            STATUS_LEFT_IN / fig.get_figwidth(),
            STATUS_BOTTOM_IN / fig.get_figheight(),
        ))

    def refresh(self, writer):
        """全台の最新スナップショットを束ねてプロットを描き替える。毎フレーム呼ぶ。"""
        all_chunks = []
        ice_chunks = []
        unit_lines = []
        total_valid = 0

        for unit in self._units:
            all_points, ice_points, valid_count, hit_count = unit.state.snapshot()
            if len(all_points):
                all_chunks.append(all_points)
            if len(ice_points):
                ice_chunks.append(ice_points)
            total_valid += valid_count
            unit_lines.append(
                f"{unit.name:<7} {valid_count:4d} pts  {hit_count:5d} hits"
            )

        merged_all = np.vstack(all_chunks) if all_chunks else np.zeros((0, 2))
        merged_ice = np.vstack(ice_chunks) if ice_chunks else np.zeros((0, 2))

        self._all_scat.set_offsets(_to_offsets(merged_all))
        self._ice_scat.set_offsets(_to_offsets(merged_ice))

        point, shaving, seq = writer.status()
        if point is None:
            self._rep_scat.set_offsets(np.zeros((0, 2)))
        else:
            deg, rr = _xy_to_polar_deg_mm(*point)
            self._rep_scat.set_offsets([[np.radians(deg), rr]])

        self._set_title(total_valid, len(merged_ice))

        quality_line = (
            f"intensity < {INTENSITY_MAX:g}"
            if SCAN_MODE == "standard" else "no quality (express)"
        )
        self._unit_text.set_text(
            "\n".join(unit_lines)
            + "\n\n"
            + f"circle ({CIRCLE_CENTER_MM[0]:.0f}, {CIRCLE_CENTER_MM[1]:.0f}) mm\n"
            + f"r {CIRCLE_RADIUS_MM:.0f}  +tol {CIRCLE_TOLERANCE_MM:.0f}\n"
            + quality_line + "\n"
            + f"rep: {REPRESENTATIVE_MODE}"
        )

        loc = "  none        " if point is None else f"({point[0]:7.1f}, {point[1]:7.1f})"
        self._status_text.set_text(
            f"seq {seq:>7d}   xy {loc} mm   shaving {'ON ' if shaving else 'off'}\n"
            f"{POSITION_FILE}"
        )

        return [self._all_scat, self._ice_scat, self._rep_scat]

    def _set_title(self, total_valid, ice_count):
        """2 行のタイトルを書き替える（束ねた点数 / スキャン設定）。"""
        self._ax.set_title(
            f"merged OR of {len(self._units)} LiDAR"
            f"   {total_valid:4d} pts   {ice_count:3d} in circle\n"
            f"{SCAN_FREQUENCY_HZ:.1f} Hz   {SCAN_MODE}"
            f"   max {MAX_POINTS_PER_REV} pts/rev"
            f"   range {RANGE_MIN_CM}-{RANGE_MAX_CM} cm",
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
    """設定された台を順に開き、開けた台だけの集合を返す。

    複数台は冗長構成（全台が同じ落氷域を同一フレームで見て OR を取る）なので、
    **1 台開けなくても止めない**。開けなかった台は理由を出して飛ばし、残りで続行する。
    1 台も開けなければ、原因の当たりをつけて終了する。

    開きっぱなしのまま落ちるポートは lidar.py 側が閉じてから投げるので、ここでの
    後始末は要らない（掴みっぱなしにならない）。
    """
    started = []
    failed = []

    for unit in units:
        try:
            unit.start(stop_event)
        except Exception as e:
            failed.append(unit)
            print(
                f"[{unit.name}] {unit.port} を開けません: {e}\n"
                f"         → この台を飛ばして続行します",
                file=sys.stderr,
            )
            continue
        started.append(unit)

    if not started:
        stop_event.set()
        ports = ", ".join(unit.port for unit in units)
        sys.exit(
            "LiDAR を 1 台も開けませんでした。\n"
            f"  設定のポート : {ports}\n"
            "  ケーブル・補助 5V・ポート名（config.toml [serial]）を確認してください。\n"
            "  接続中のポートは  ls /dev/tty.usb*  で見えます。"
        )

    if failed:
        print(
            f"{len(started)}/{len(units)} 台で続行します"
            f"（不参加: {', '.join(unit.name for unit in failed)}）"
        )

    return started


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
    """可視化つきで走らせる。ウィンドウを閉じるか Ctrl-C で安全停止する。

    台数によらず単一の点群プロット（`MergedView`）。窓の大きさは固定で、台が
    増えても割らない。
    """
    import matplotlib.animation as animation
    import matplotlib.pyplot as plt

    fig = plt.figure(figsize=(FIGURE_WIDTH_IN, FIGURE_HEIGHT_IN))

    manager = fig.canvas.manager
    if manager is not None:
        manager.set_window_title("RPLIDAR S1 tracker")

    view = MergedView(units)
    view.build(fig)

    def apply_layout(_event=None):
        """インチで決めた配置を、いまの窓の大きさに合わせて割合へ換算し直す。

        これをしないと、窓を広げた分だけ左の数値欄・プロットが一緒に間延びして、
        数値とプロットが遠く離れてしまう。
        """
        width_in = fig.get_figwidth()
        height_in = fig.get_figheight()

        left = min(LEFT_COLUMN_WIDTH_IN / width_in, 0.45)
        bottom = min(BOTTOM_ROW_HEIGHT_IN / height_in, 0.2)
        top = max(1.0 - TOP_MARGIN_IN / height_in, bottom + 0.3)

        fig.subplots_adjust(left=left, right=0.97, top=top, bottom=bottom)
        view.place_text(fig)

    apply_layout()
    fig.canvas.mpl_connect("resize_event", apply_layout)

    def update(_frame):
        return view.refresh(writer)

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
        description=(
            "RPLIDAR S1 の落氷検知を氷の連続座標 (x,y)[mm] + 削り状態にして Unity へ渡す"
        )
    )
    parser.add_argument(
        "--headless",
        action="store_true",
        help="可視化ウィンドウを開かない（本番で CPU を空けたいとき）",
    )
    args = parser.parse_args()

    units = build_units()
    stop_event = threading.Event()

    # 開けた台だけで進める（開けなかった台は start_units が飛ばす）。
    active = start_units(units, stop_event)

    writer = PositionWriter(
        POSITION_FILE,
        [unit.state for unit in active],
        SHAVING_HOLD_SEC,
        WRITE_INTERVAL_SEC,
    )
    writer_thread = threading.Thread(
        target=writer.run, args=(stop_event,), daemon=True
    )
    writer_thread.start()

    print(f"position file = {POSITION_FILE}")
    print(
        f"scan: {SCAN_MODE} mode, {SCAN_FREQUENCY_HZ:.1f} Hz, "
        f"max {MAX_POINTS_PER_REV} pts/rev"
    )
    print(
        f"circle: center ({CIRCLE_CENTER_MM[0]:.0f}, {CIRCLE_CENTER_MM[1]:.0f}) mm, "
        f"r {CIRCLE_RADIUS_MM:.0f} (+tol {CIRCLE_TOLERANCE_MM:.0f}); "
        f"intensity < {INTENSITY_MAX:g}, shaving hold {SHAVING_HOLD_SEC:g}s; "
        f"representative = {REPRESENTATIVE_MODE}"
    )

    def shutdown():
        # 二重呼び出しに備えてフラグで一度きりに
        if stop_event.is_set():
            return
        stop_event.set()
        writer_thread.join(timeout=3.0)
        for unit in active:
            unit.stop()

    try:
        if args.headless:
            run_headless(stop_event)
        else:
            run_plot(active, writer, stop_event, shutdown)
    finally:
        # Ctrl-C・ウィンドウを閉じる、どちらでも必ず停止処理を通す
        shutdown()


if __name__ == "__main__":
    main()
