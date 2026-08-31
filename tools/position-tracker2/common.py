"""かき氷機ハンドルの回転検知 — 3実装の共通土台（tools/position-tracker2）。

俯瞰カメラで「ハンドルを回しているか」を判定する 3 パターン（rotate_landmark.py /
rotate_flow.py / rotate_periodicity.py）が共有する部品を置く。**回転の見つけ方そのもの**は
各スクリプトに閉じていて、ここには共通の外枠だけを置く:

  - 出力（`seq,shaving,rpm` の原子的書き込み）と設定・パス解決
  - RPM -> 削り中フラグ の最終段（`ShavingGate`。ヒステリシス＋ホールド）
  - 角度の unwrap と角速度回帰（`AngularVelocity`）、回転中心の円フィット（`CrankCenter`）
  - ROI（回転中心と半径）のマウス調整と保存
  - フレーム供給（カメラ / 合成 `SyntheticCrank`）と HUD

`--sim` の合成ソースは、旧ロジックが壊れた 2 ケース（**一瞬フレーム外**・**両手の重なり**）を
台本として含む。3 実装を同じ台本に通せば、カメラ無しで挙動を突き合わせられる。
"""

from __future__ import annotations

import json
import math
import os
import sys
import threading
import time
from collections import deque
from pathlib import Path

import cv2
import numpy as np

SCRIPT_DIR = Path(__file__).resolve().parent
DEFAULT_REL_OUT = SCRIPT_DIR / "out" / "rotation.txt"
ROI_FILE = SCRIPT_DIR / "roi.json"

WRITE_INTERVAL_SEC = 0.05  # ~50ms（position-tracker と同じ更新頻度）

# ---- 判定の既定値（config.json で上書き可） --------------------------------
DEFAULT_ON_RPM = 12.0       # これを超えたら「回している」
DEFAULT_OFF_RPM = 6.0       # これを割ったら（＋holdSec）「止めた」
DEFAULT_MIN_ON_SEC = 0.12   # 立ち上がりのチャタリング除け
DEFAULT_HOLD_SEC = 0.45     # 見失い・停止のホールド（一瞬の欠落で切らないため）


# ---- 設定とパス解決 ---------------------------------------------------------

def load_config() -> dict:
    """config.json をスクリプト隣→cwd の順で探す。無ければ空 dict（既定値で動く）。"""
    for base in (SCRIPT_DIR, Path.cwd()):
        p = base / "config.json"
        if p.is_file():
            try:
                return json.loads(p.read_text(encoding="utf-8"))
            except (ValueError, OSError) as e:
                print(f"[common] config.json 読込失敗 ({p}): {e}", file=sys.stderr)
    return {}


def resolve_output_file(cfg: dict) -> Path:
    """出力先: 環境変数 -> config.json -> スクリプト相対 out/rotation.txt。"""
    env = os.environ.get("KAKIGORI_ROTATION_FILE")
    if env:
        return Path(env)
    p = cfg.get("rotationFile")
    return Path(p) if p else DEFAULT_REL_OUT


class RotationWriter:
    """`seq,shaving,rpm` を 1 行で原子的に書く（partial read を排除）。

    - `seq`     … long 単調増加。止まったら Unity 側が「プロセス停止」と判定できる。
    - `shaving` … 0/1。`ShavingGate` を通した後の値（ホールド込み）。
    - `rpm`     … 参考値（未計測は 0.0）。判定は `shaving` を見る。
    """

    def __init__(self, path: Path):
        self.path = path
        self._tmp = path.with_suffix(path.suffix + ".tmp")
        self.seq = 0
        path.parent.mkdir(parents=True, exist_ok=True)

    def write(self, shaving: bool, rpm: float | None) -> None:
        self.seq += 1
        self._tmp.write_text(f"{self.seq},{1 if shaving else 0},{(rpm or 0.0):.1f}\n",
                             encoding="utf-8")
        os.replace(self._tmp, self.path)


# ---- RPM -> 削り中フラグ ----------------------------------------------------

class ShavingGate:
    """回転速度[RPM] を「削り中」フラグへ落とす最終段（3 実装で共通）。

    3 つの仕掛けで、**旧ロジックの「一瞬見失うと判定が消える」問題**を出口側で潰す:

      1. ヒステリシス   … `on_rpm` で入り `off_rpm` で出る（境界のバタつきを止める）
      2. 立ち上がり待ち … `min_on_sec` 継続して初めて ON（単発ノイズで点かない）
      3. ホールド       … 計測不能（`rpm=None`）や失速でも `hold_sec` は ON を維持

    `update(None, now)` は「回転を計測できなかったフレーム」を意味する。手が枠外・
    重なり・検出漏れは全てここに落ちるので、`hold_sec` 未満の欠落は判定に出ない。
    """

    def __init__(self, on_rpm: float = DEFAULT_ON_RPM, off_rpm: float = DEFAULT_OFF_RPM,
                 min_on_sec: float = DEFAULT_MIN_ON_SEC, hold_sec: float = DEFAULT_HOLD_SEC):
        self.on_rpm = float(on_rpm)
        self.off_rpm = float(off_rpm)
        self.min_on_sec = float(min_on_sec)
        self.hold_sec = float(hold_sec)
        self._shaving = False
        self._on_since: float | None = None
        self._last_alive = 0.0   # 最後に「回っている」と言えた時刻

    @classmethod
    def from_config(cls, cfg: dict) -> "ShavingGate":
        return cls(cfg.get("onRpm", DEFAULT_ON_RPM), cfg.get("offRpm", DEFAULT_OFF_RPM),
                   cfg.get("minOnSec", DEFAULT_MIN_ON_SEC), cfg.get("holdSec", DEFAULT_HOLD_SEC))

    @property
    def shaving(self) -> bool:
        return self._shaving

    @property
    def hold_left(self) -> float:
        """ホールド残り秒（ON 中に回転を見失っているときだけ意味を持つ）。"""
        if not self._shaving:
            return 0.0
        return max(0.0, self.hold_sec - (time.time() - self._last_alive))

    def update(self, rpm: float | None, now: float) -> bool:
        hot = rpm is not None and rpm >= self.on_rpm       # 明確に回っている
        warm = rpm is not None and rpm >= self.off_rpm     # まだ回っている（ON 維持）

        if hot:
            if self._on_since is None:
                self._on_since = now
            if now - self._on_since >= self.min_on_sec:
                self._shaving = True
        else:
            self._on_since = None

        if warm:
            self._last_alive = now
        elif self._shaving and now - self._last_alive > self.hold_sec:
            self._shaving = False   # 失速 or 見失いが hold_sec を超えた
        return self._shaving


# ---- 角度まわりの共通計算 ---------------------------------------------------

def wrap180(deg: float) -> float:
    """角度差を (-180, 180] に畳む。"""
    return (deg + 180.0) % 360.0 - 180.0


class AngularVelocity:
    """角度[deg] の系列を unwrap し、直近 `window_sec` の最小二乗回帰で角速度[deg/s] を出す。

    1 フレームの差分ではなくウィンドウ回帰にするのは、検出ノイズで角度が跳ねても
    速度が跳ねないようにするため。欠落フレームは「push しない」だけでよく、時刻付きで
    回帰するので穴が空いても速度の推定は続く。
    """

    def __init__(self, window_sec: float = 0.4, min_samples: int = 4):
        self.window_sec = float(window_sec)
        self.min_samples = int(min_samples)
        self._buf: deque[tuple[float, float]] = deque()  # (t, 累積角[deg])
        self._last_raw: float | None = None
        self._cum = 0.0

    def reset(self) -> None:
        self._buf.clear()
        self._last_raw = None
        self._cum = 0.0

    def push(self, angle_deg: float, t: float, expected_delta_deg: float = 0.0) -> None:
        """角度を 1 サンプル入れる。

        `expected_delta_deg` は「前回からこれだけ回ったはず」という事前予測（前回の角速度
        × 経過時間）。素の unwrap は 1 サンプルあたり ±180° を超える回転を折り返して
        しまう（60RPM でも 0.3 秒の欠落で破綻する）。予測のまわりで折り返すことで、
        **手が一瞬フレーム外に出て戻ってきても角度が連続する**。
        """
        if self._last_raw is None:
            self._cum = 0.0
        else:
            self._cum += expected_delta_deg + wrap180(
                angle_deg - self._last_raw - expected_delta_deg)
        self._last_raw = angle_deg
        self._buf.append((t, self._cum))
        # 古いサンプルを捨てるのは「捨てても min_samples 本残るとき」だけ。検出が数フレーム
        # 抜けた直後でも回帰が即座に成立し（時刻付きなので穴は正しく効く）、復帰の瞬間に
        # 速度が一度 None へ落ちて判定が途切れるのを防ぐ。
        while len(self._buf) > self.min_samples and t - self._buf[0][0] > self.window_sec:
            self._buf.popleft()

    @property
    def cumulative_deg(self) -> float:
        return self._cum

    @property
    def deg_per_sec(self) -> float | None:
        """ウィンドウが埋まっていれば角速度[deg/s]、足りなければ None。"""
        if len(self._buf) < self.min_samples:
            return None
        ts = np.fromiter((t for t, _ in self._buf), dtype=np.float64)
        ys = np.fromiter((y for _, y in self._buf), dtype=np.float64)
        span = ts[-1] - ts[0]
        if span < self.window_sec * 0.5:
            return None
        ts = ts - ts[0]
        slope = float(np.polyfit(ts, ys, 1)[0])
        return slope


def refine_peak_hz(power, k: int, freqs) -> float:
    """離散スペクトルのピーク位置を放物線補間で細かく読む（周波数分解能 = 1/窓 の内側）。

    補正量は **±0.5 ビンに制限**し、頂点が上に凸でない（`a-2b+c >= 0`）ときは補正しない。
    素の `0.5(a-c)/(a-2b+c)` は平坦な頂点で分母がゼロに近づき、桁違いの周波数を返して
    しまう（ピークの安定性を見る判定がそれで壊れる）。
    """
    f = float(freqs[k])
    if k <= 0 or k >= len(power) - 1:
        return f
    a, b, c_ = float(power[k - 1]), float(power[k]), float(power[k + 1])
    den = a - 2.0 * b + c_
    if den >= 0.0:
        return f
    d = max(-0.5, min(0.5, 0.5 * (a - c_) / den))
    return f + d * float(freqs[1] - freqs[0])


def deg_per_sec_to_rpm(dps: float | None) -> float | None:
    """角速度[deg/s] -> 回転数[RPM]（向きは問わないので絶対値）。"""
    return None if dps is None else abs(dps) / 6.0


def circle_fit(points: np.ndarray) -> tuple[float, float, float] | None:
    """点群に円を当てる（Kåsa の代数フィット）。戻り値 (cx, cy, r)。

    手の軌跡から**回転中心を自動推定**するために使う。中心を「もう片方の手」に
    置かないので、片手だけ見えていても・両手が重なっても中心が壊れない。
    """
    if points is None or len(points) < 6:
        return None
    p = np.asarray(points, dtype=np.float64)
    x, y = p[:, 0], p[:, 1]
    a = np.column_stack([x, y, np.ones(len(p))])
    b = x * x + y * y
    try:
        sol, *_ = np.linalg.lstsq(a, b, rcond=None)
    except np.linalg.LinAlgError:
        return None
    cx, cy = sol[0] / 2.0, sol[1] / 2.0
    rsq = sol[2] + cx * cx + cy * cy
    if not np.isfinite(rsq) or rsq <= 1.0:
        return None
    return float(cx), float(cy), float(math.sqrt(rsq))


class CrankCenter:
    """手の軌跡から回転中心を推定して EMA で均す。フィットできない間は ROI 中心を使う。

    軌跡の広がり（bbox の短辺）が小さいうちは「まだ回っていない/直線的に動いている」
    としてフィットを採用しない。半径も同時に得られるので、外れ値の門番に使える。
    """

    def __init__(self, fallback: tuple[float, float], max_age_sec: float = 2.5,
                 min_spread_px: float = 24.0, ema: float = 0.15):
        self.fallback = fallback
        self.max_age_sec = float(max_age_sec)
        self.min_spread_px = float(min_spread_px)
        self.ema = float(ema)
        self._buf: deque[tuple[float, float, float]] = deque()  # (t, x, y)
        self._center: tuple[float, float] | None = None
        self.radius: float | None = None

    def push(self, pt: tuple[float, float], t: float) -> None:
        self._buf.append((t, float(pt[0]), float(pt[1])))
        while self._buf and t - self._buf[0][0] > self.max_age_sec:
            self._buf.popleft()
        if len(self._buf) < 12:
            return
        p = np.array([[x, y] for _, x, y in self._buf], dtype=np.float64)
        spread = min(np.ptp(p[:, 0]), np.ptp(p[:, 1]))  # NumPy 2.0 で ndarray.ptp() は廃止
        if spread < self.min_spread_px:
            return
        fit = circle_fit(p)
        if fit is None:
            return
        cx, cy, r = fit
        # 明らかに外れた解（軌跡から遠すぎる中心）は捨てる。
        if r > 4.0 * max(np.ptp(p[:, 0]), np.ptp(p[:, 1])):
            return
        if self._center is None:
            self._center, self.radius = (cx, cy), r
        else:
            k = self.ema
            self._center = ((1 - k) * self._center[0] + k * cx,
                            (1 - k) * self._center[1] + k * cy)
            self.radius = (1 - k) * (self.radius or r) + k * r

    def set_fallback(self, pt: tuple[float, float]) -> None:
        self.fallback = pt

    @property
    def fitted(self) -> bool:
        return self._center is not None

    @property
    def center(self) -> tuple[float, float]:
        return self._center if self._center is not None else self.fallback


# ---- ROI（回転中心と半径）のマウス調整 --------------------------------------

class Roi:
    """かき氷機のハンドルを囲む円 ROI。マウスドラッグで移動、`[`/`]` で半径、`s` で保存。

    使うのは B（rotate_flow.py）だけ。A（rotate_landmark.py）と C（rotate_periodicity.py）は
    `uses_roi=False` を宣言していて、円 ROI ではなく `full_frame()`＝カメラ映像全体を受け取る。
    """

    def __init__(self, cx: float, cy: float, radius: float):
        self.cx, self.cy, self.radius = float(cx), float(cy), float(radius)
        self._drag = False
        self.full = False   # True＝カメラ映像全体（調整も描画もしない）

    @classmethod
    def full_frame(cls, w: int, h: int) -> "Roi":
        """カメラ映像全体を対象にする ROI。`contains()` は常に真になる。

        画面のどこに手が写っても拾いたい実装（rotate_periodicity.py）が使う。
        マウス調整・半径キー・roi.json の読み書きは全て無効になる。
        """
        roi = cls(w / 2.0, h / 2.0, math.hypot(w, h) / 2.0)
        roi.full = True
        return roi

    @classmethod
    def load_or_default(cls, w: int, h: int) -> "Roi":
        if ROI_FILE.is_file():
            try:
                d = json.loads(ROI_FILE.read_text(encoding="utf-8"))
                return cls(d["cx"], d["cy"], d["radius"])
            except (ValueError, OSError, KeyError) as e:
                print(f"[common] roi.json 読込失敗: {e}", file=sys.stderr)
        return cls(w / 2, h / 2, min(w, h) * 0.35)

    def save(self) -> None:
        ROI_FILE.write_text(json.dumps({"cx": self.cx, "cy": self.cy, "radius": self.radius},
                                       indent=2), encoding="utf-8")
        print(f"[common] ROI 保存: {ROI_FILE}")

    def install(self, window: str) -> None:
        if self.full:
            return   # 全画面 ROI は動かさない
        def on_mouse(event, x, y, flags, _param):
            held = bool(flags & cv2.EVENT_FLAG_LBUTTON)
            if event == cv2.EVENT_LBUTTONDOWN:
                self._drag = True
                self.cx, self.cy = float(x), float(y)
            elif event == cv2.EVENT_MOUSEMOVE:
                if self._drag and held:
                    self.cx, self.cy = float(x), float(y)
                elif not held:
                    self._drag = False   # macOS で LBUTTONUP を取りこぼす保険
            elif event == cv2.EVENT_LBUTTONUP:
                self._drag = False

        cv2.setMouseCallback(window, on_mouse)

    def handle_key(self, key: int) -> bool:
        """`[`/`]` 半径、`s` 保存。処理したら True。"""
        if self.full:
            return False
        if key == ord("["):
            self.radius = max(16.0, self.radius - 6.0)
        elif key == ord("]"):
            self.radius += 6.0
        elif key == ord("s"):
            self.save()
        else:
            return False
        return True

    @property
    def center(self) -> tuple[float, float]:
        return (self.cx, self.cy)

    def contains(self, pt, scale: float = 1.0) -> bool:
        return math.hypot(pt[0] - self.cx, pt[1] - self.cy) <= self.radius * scale

    def draw(self, frame) -> None:
        c = (int(self.cx), int(self.cy))
        if self.full:
            h, w = frame.shape[:2]
            cv2.rectangle(frame, (1, 1), (w - 2, h - 2), (255, 255, 0), 1)
        else:
            cv2.circle(frame, c, int(self.radius), (255, 255, 0), 1)
        cv2.drawMarker(frame, c, (255, 255, 0), cv2.MARKER_CROSS, 12, 1)


# ---- フレーム供給（カメラ / 合成） ------------------------------------------
#
# 3 実装は `read() -> (frame, hint, info)` だけを見る。
#   frame … BGR 画像
#   hint  … None ならカメラ（各自の検出器を回す）。合成のときは「見えている手の座標」の
#           リストで、検出器（MediaPipe / YOLO）を迂回して回転判定だけを検証できる。
#   info  … 合成ソースの台本情報（局面名・正解・真の RPM）。カメラでは None。

class CameraSource:
    """position-tracker と同じ「取得を別スレッドへ逃がす」構成。GUI はメインスレッド。"""

    def __init__(self, index: int = 0, width: int = 640, height: int = 480, flip: bool = False):
        self.width, self.height, self.flip = width, height, flip
        self._cap = cv2.VideoCapture(index)
        self._cap.set(cv2.CAP_PROP_FRAME_WIDTH, width)
        self._cap.set(cv2.CAP_PROP_FRAME_HEIGHT, height)
        self._frame = None
        self._lock = threading.Lock()
        self._stop = threading.Event()
        self._thread = threading.Thread(target=self._worker, daemon=True)
        self._thread.start()

    def _worker(self) -> None:
        while not self._stop.is_set():
            ok, f = self._cap.read()
            if not ok:
                print("[common] カメラが読めません", file=sys.stderr)
                self._stop.set()
                break
            if f.shape[1] != self.width or f.shape[0] != self.height:
                f = cv2.resize(f, (self.width, self.height))
            if self.flip:
                f = cv2.flip(f, 1)
            with self._lock:
                self._frame = f

    @property
    def alive(self) -> bool:
        return not self._stop.is_set()

    def read(self):
        with self._lock:
            f = None if self._frame is None else self._frame.copy()
        return f, None, None

    def close(self) -> None:
        self._stop.set()
        self._thread.join(timeout=1.0)
        self._cap.release()


class SyntheticCrank:
    """カメラ無しの合成ソース。**実機で壊れた場面を台本として再生する**。

    `scenario="basic"`（16 秒周期）— 旧ロジックが壊れた 2 ケース:
        0.0-4.0   normal     … 両手が見えて等速回転                 期待 ON
        4.0-4.4   one-hand   … 片手がフレーム外                     期待 ON
        4.4-6.0   normal
        6.0-6.35  blackout   … 両手とも消える（一瞬の見失い）★      期待 ON
        6.35-9.0  normal
        9.0-11.0  overlap    … 両手が同じ位置に重なる        ★      期待 ON
        11.0-13.0 normal
        13.0-16.0 stopped    … 手はあるが回していない               期待 OFF

    `scenario="strict"`（26 秒周期）— 上に**誤検出を誘発する 3 局面**を足したもの:
        8.0-12.0  overlap    … 両手が 0.5 秒かけて重なり、離れる（期待 ON）
        12.0-15.0 sweep      … 回さずに、機体を持った手を大きな弧に沿って往復させる
                               （半径が大きく・1 周しない。A が回転と誤認する動き）
        15.0-18.0 jitter     … 手は写っているが回していない。検出点が数 px ランダムに
                               揺れ、時々もう一方の手と取り違わる（C が周期性を拾う動き）
        18.0-20.0 swing      … 機体ごと直線的に前後させる（位相直交で落とすべき動き）
        いずれも 期待 OFF。

    画像は「静止した背景テクスチャ＋動く手」なので、画素ベースの rotate_flow.py も
    そのまま通せる。`info["expected"]` は定常状態での正解（切り替わり直後は判定の遅れぶんズレる）。
    """

    CRANK_PHASES = ("normal", "one-hand", "blackout", "overlap")

    def __init__(self, width: int = 640, height: int = 480, rpm: float = 60.0,
                 fps: float = 30.0, scenario: str = "basic"):
        self.width, self.height = width, height
        self.rpm = float(rpm)
        self.fps = float(fps)
        self.scenario = scenario
        self.CYCLE = 26.0 if scenario == "strict" else 16.0
        self.overlap_span = (8.0, 12.0) if scenario == "strict" else (9.0, 11.0)
        self.cx, self.cy = width / 2.0, height / 2.0
        self.orbit = min(width, height) * 0.22        # ハンドルを回す軌道半径
        self.orbit_big = min(width, height) * 0.40    # 機体ごと動かす大きな弧の半径
        self._angle = 0.0
        self._t0 = time.time()
        self._prev = self._t0
        self._rng = np.random.default_rng(11)
        rng = np.random.default_rng(7)
        # 静止背景（テクスチャが無いと Farnebäck が背景の静止を捉えられない）。
        bg = rng.integers(40, 90, size=(height, width, 1), dtype=np.uint8)
        self._bg = cv2.GaussianBlur(np.repeat(bg, 3, axis=2), (0, 0), 2.0)

    # ---- 台本 ----------------------------------------------------------
    def _phase(self, t: float) -> str:
        u = t % self.CYCLE
        if 4.0 <= u < 4.4:
            return "one-hand"
        if 6.0 <= u < 6.35:
            return "blackout"
        if self.scenario == "strict":
            if 8.0 <= u < 12.0:
                return "overlap"
            if 12.0 <= u < 15.0:
                return "sweep"
            if 15.0 <= u < 18.0:
                return "jitter"
            if 18.0 <= u < 20.0:
                return "swing"
            if 20.0 <= u < 22.0:
                return "stopped"
            return "normal"
        if 9.0 <= u < 11.0:
            return "overlap"
        if 13.0 <= u:
            return "stopped"
        return "normal"

    def _positions(self, phase: str, t: float) -> list[tuple[float, float]]:
        """局面ごとの手の位置。戻り値は「見えている手」だけのリスト。"""
        u = t % self.CYCLE
        if phase == "sweep":
            # 大きな弧に沿って ±90° を往復。1 周は絶対にしない（＝削りではない）。
            th = math.radians(-90.0 + 90.0 * math.sin(2 * math.pi * (u - 12.0) / 2.4))
            r = self.orbit_big
            return [(self.cx + r * math.cos(th), self.cy + r * math.sin(th)),
                    (self.cx + r * math.cos(th + 0.21), self.cy + r * math.sin(th + 0.21))]
        if phase == "swing":
            # 機体ごと直線的に前後させる（円運動ではない）。
            d = 110.0 * math.sin(2 * math.pi * (u - 18.0) / 1.6)
            ux, uy = math.cos(math.radians(30.0)), math.sin(math.radians(30.0))
            return [(self.cx + d * ux - 20 * uy, self.cy + d * uy + 20 * ux),
                    (self.cx + d * ux + 20 * uy, self.cy + d * uy - 20 * ux)]
        if phase == "jitter":
            # 手は止まっているが、検出点が数 px ランダムに揺れる（キーポイントの揺らぎ）。
            a = math.radians(35.0)
            base = [(self.cx + self.orbit * math.cos(a), self.cy + self.orbit * math.sin(a)),
                    (self.cx - self.orbit * math.cos(a), self.cy - self.orbit * math.sin(a))]
            pts = [(x + self._rng.normal(0, 4.0), y + self._rng.normal(0, 4.0))
                   for x, y in base]
            if self._rng.random() < 0.15:
                pts.reverse()   # 左右の取り違え（追っている点が飛ぶ）
            return pts

        a = math.radians(self._angle)
        p1 = (self.cx + self.orbit * math.cos(a), self.cy + self.orbit * math.sin(a))
        p2 = (self.cx - self.orbit * math.cos(a), self.cy - self.orbit * math.sin(a))
        if phase == "overlap":
            # 2 つの手が重なる。実機では徐々に近づくので、0.5 秒かけて寄せて 0.5 秒かけて離す
            # （瞬間移動させると、どの実装から見ても「点が 200px 飛んだ」という非現実な
            #  信号になり、重なりそのものの検証にならない）。
            o0, o1 = self.overlap_span
            w = max(0.0, min(1.0, min((u - o0) / 0.5, (o1 - u) / 0.5)))
            tgt = (p1[0] + 4.0, p1[1] + 4.0)
            p2 = (p2[0] + (tgt[0] - p2[0]) * w, p2[1] + (tgt[1] - p2[1]) * w)
        if phase == "blackout":
            return []
        if phase == "one-hand":
            return [p1]
        return [p1, p2]

    # ---- 描画 ----------------------------------------------------------
    def _draw_hand(self, img, pt, tone: int) -> None:
        x, y = int(pt[0]), int(pt[1])
        cv2.circle(img, (x, y), 26, (tone, tone - 10, tone - 20), -1)
        # 指もどきの模様。無地の円だとオプティカルフローが「回転」を掴めない。
        for k in range(5):
            a = math.radians(self._angle * 1.0 + k * 62.0)
            cv2.circle(img, (x + int(13 * math.cos(a)), y + int(13 * math.sin(a))), 5,
                       (tone - 55, tone - 60, tone - 65), -1)

    def read(self):
        now = time.time()
        dt = min(0.1, now - self._prev)
        self._prev = now
        t = now - self._t0
        phase = self._phase(t)

        if phase in self.CRANK_PHASES:          # ハンドルが回っているのはこの局面だけ
            self._angle += 360.0 * (self.rpm / 60.0) * dt
        visible = self._positions(phase, t)

        frame = self._bg.copy()
        cv2.circle(frame, (int(self.cx), int(self.cy)), int(self.orbit + 34), (70, 70, 70), 2)
        for i, p in enumerate(visible):
            cv2.line(frame, (int(self.cx), int(self.cy)), (int(p[0]), int(p[1])),
                     (110, 110, 120), 3)
            self._draw_hand(frame, p, 205 if i == 0 else 185)

        # カメラ相当のフレームレートに合わせる（実時間で回るロジックの検証のため）。
        lag = (1.0 / self.fps) - (time.time() - now)
        if lag > 0:
            time.sleep(lag)

        expected = phase in self.CRANK_PHASES
        info = {"phase": phase, "expected": expected,
                "rpm_truth": self.rpm if expected else 0.0}
        return frame, visible, info

    @property
    def alive(self) -> bool:
        return True

    def close(self) -> None:
        pass


def build_source(args, cfg: dict):
    """`--sim` なら合成ソース、そうでなければカメラ。"""
    w = int(cfg.get("width", 640))
    h = int(cfg.get("height", 480))
    if getattr(args, "sim", False):
        return SyntheticCrank(w, h, rpm=float(getattr(args, "sim_rpm", 60.0)),
                              scenario=getattr(args, "sim_scenario", "basic"))
    return CameraSource(int(cfg.get("cameraIndex", 0)), w, h,
                        flip=bool(getattr(args, "flip", False) or cfg.get("flip", False)))


def add_common_args(ap) -> None:
    ap.add_argument("--sim", action="store_true",
                    help="カメラ無し。合成ソース（見失い・重なり・停止の台本）で判定ロジックを検証する")
    ap.add_argument("--sim-rpm", type=float, default=60.0, help="--sim の回転速度[RPM]（既定 60）")
    ap.add_argument("--sim-scenario", choices=("basic", "strict"), default="basic",
                    help="--sim の台本。strict は誤検出を誘発する局面（sweep/jitter/swing）を含む")
    ap.add_argument("--flip", action="store_true", help="映像を左右反転（水平ミラー）する")
    ap.add_argument("--headless", action="store_true",
                    help="ウィンドウを出さず標準出力だけで回す（CI・SSH 用）")
    ap.add_argument("--duration", type=float, default=0.0,
                    help="指定秒で自動終了（0＝無制限）。--sim の採点に使う")


# ---- 表示 -------------------------------------------------------------------

class FpsMeter:
    def __init__(self, n: int = 30):
        self._t = deque(maxlen=n)

    def tick(self) -> float:
        self._t.append(time.time())
        if len(self._t) < 2:
            return 0.0
        span = self._t[-1] - self._t[0]
        return (len(self._t) - 1) / span if span > 0 else 0.0


ROI_HINT = "drag: move ROI / [ ]: radius / s: save roi.json / ESC: quit"
FULL_FRAME_HINT = "ROI: full camera frame (no calibration) / ESC: quit"


def draw_hud(frame, title: str, rpm: float | None, shaving: bool, lines: list[str],
             info: dict | None = None, footer: str = ROI_HINT) -> None:
    """左上に実装名・RPM・削り判定、下に操作説明。`--sim` では正解との一致も出す。"""
    h = frame.shape[0]
    cv2.rectangle(frame, (0, 0), (frame.shape[1], 74), (0, 0, 0), -1)
    cv2.putText(frame, title, (10, 22), cv2.FONT_HERSHEY_SIMPLEX, 0.6, (255, 255, 255), 1)

    state = "SHAVING" if shaving else "idle"
    color = (0, 220, 60) if shaving else (150, 150, 150)
    rpm_txt = "--" if rpm is None else f"{rpm:5.1f}"
    cv2.putText(frame, f"{state}  rpm={rpm_txt}", (10, 52),
                cv2.FONT_HERSHEY_SIMPLEX, 0.7, color, 2)

    y = 96
    for ln in lines:
        cv2.putText(frame, ln, (10, y), cv2.FONT_HERSHEY_SIMPLEX, 0.45, (0, 255, 255), 1)
        y += 18

    if info is not None:
        ok = shaving == info["expected"]
        cv2.putText(frame, f"sim[{info['phase']}] expect={'ON' if info['expected'] else 'off'} "
                           f"{'OK' if ok else 'MISS'}",
                    (10, y + 4), cv2.FONT_HERSHEY_SIMPLEX, 0.5,
                    (0, 220, 60) if ok else (0, 80, 255), 2)

    cv2.putText(frame, footer, (10, h - 12), cv2.FONT_HERSHEY_SIMPLEX, 0.45, (255, 255, 0), 1)


# ---- 3 実装共通の実行ループ -------------------------------------------------
#
# 各スクリプトは「Detector」だけを実装する。取得・ROI・ゲート・書き込み・表示は
# ここに集約してあるので、3 パターンの差分は**回転の見つけ方だけ**になる。
#
#   class Detector:
#       name: str
#       roi: Roi                      # run() が最初のフレームで注入する
#       def update(frame, hint, t) -> (rpm|None, list[str])   # rpm=None は「計測不能」
#       def draw(frame) -> None       # 可視化（任意）
#       def close() -> None           # 後始末（任意）

class SimScorer:
    """`--sim` 用の自動採点。局面ごとに「判定 == 正解」の割合を出す。

    局面の切り替わり直後 `settle` 秒は、判定の遅れ（ウィンドウ長・ホールド）が乗るので
    採点から外す。ここで見たいのは「定常状態で正しく張り付いているか」。
    """

    def __init__(self, settle_sec: float = 0.8):
        self.settle = settle_sec
        self._phase: str | None = None
        self._expected: bool | None = None
        self._since = 0.0
        self._stats: dict[str, list[int]] = {}

    def feed(self, now: float, info: dict, shaving: bool) -> None:
        phase, expected = info["phase"], info["expected"]
        # 除外するのは「正解が変わった直後」だけ。one-hand / blackout は前後と正解が
        # 同じ（ON のまま）なので、猶予無しで丸ごと採点する＝見失いの取りこぼしを見逃さない。
        if expected != self._expected:
            self._since = now
        self._phase, self._expected = phase, expected
        if now - self._since < self.settle:
            return
        ok, total = self._stats.setdefault(phase, [0, 0])
        self._stats[phase] = [ok + (1 if shaving == info["expected"] else 0), total + 1]

    def report(self, name: str) -> None:
        print(f"\n=== {name} / --sim 採点（局面ごとの一致率・切替直後 {self.settle}s は除外）===")
        if not self._stats:
            print("  サンプル無し（--duration を伸ばす）")
            return
        for phase in ("normal", "one-hand", "blackout", "overlap",
                      "sweep", "jitter", "swing", "stopped"):
            if phase not in self._stats:
                continue
            ok, total = self._stats[phase]
            print(f"  {phase:9s} {ok:5d}/{total:<5d}  {100.0 * ok / max(1, total):5.1f}%")


def run(detector, args, cfg: dict) -> None:
    """取得 -> detector -> ShavingGate -> 原子的書き込み -> 表示 の共通ループ。"""
    source = build_source(args, cfg)
    gate = ShavingGate.from_config(cfg)
    writer = RotationWriter(resolve_output_file(cfg))
    fps = FpsMeter()
    scorer = SimScorer() if getattr(args, "sim", False) else None
    show = not args.headless
    win = detector.name
    duration = float(getattr(args, "duration", 0.0) or 0.0)

    if show:
        cv2.namedWindow(win)
    print(f"[{detector.name}] 出力: {writer.path}  ({'ESC' if show else 'Ctrl-C'} で終了)")

    roi: Roi | None = None
    t_start = time.time()
    last_write = 0.0
    last_print = 0.0
    try:
        while source.alive:
            frame, hint, info = source.read()
            if frame is None:                      # 最初のフレーム待ち
                if show and (cv2.waitKey(10) & 0xFF) == 27:
                    break
                if not show:
                    time.sleep(0.01)
                continue

            if roi is None:
                # uses_roi=False の実装（rotate_periodicity.py）は映像全体を対象にする。
                roi = (Roi.full_frame(frame.shape[1], frame.shape[0])
                       if not getattr(detector, "uses_roi", True)
                       else Roi.load_or_default(frame.shape[1], frame.shape[0]))
                detector.roi = roi
                if show:
                    roi.install(win)

            now = time.time()
            rpm, lines = detector.update(frame, hint, now)
            shaving = gate.update(rpm, now)

            if now - last_write >= WRITE_INTERVAL_SEC:
                writer.write(shaving, rpm)
                last_write = now
            if scorer is not None and info is not None:
                scorer.feed(now, info, shaving)

            f = fps.tick()
            if show:
                roi.draw(frame)
                detector.draw(frame)
                draw_hud(frame, f"{detector.name}   fps={f:4.1f}  seq={writer.seq}",
                         rpm, shaving, lines, info,
                         footer=FULL_FRAME_HINT if roi.full else ROI_HINT)
                cv2.imshow(win, frame)
                key = cv2.waitKey(1) & 0xFF
                if key == 27:
                    break
                if key != 255:
                    roi.handle_key(key)
            elif now - last_print >= 0.25:
                last_print = now
                extra = f" phase={info['phase']:9s} expect={int(info['expected'])}" if info else ""
                rpm_txt = " --  " if rpm is None else f"{rpm:5.1f}"
                print(f"[{detector.name}] t={now - t_start:6.2f} rpm={rpm_txt} "
                      f"shaving={int(shaving)}{extra} fps={f:4.1f}")

            if duration > 0.0 and now - t_start >= duration:
                break
    except KeyboardInterrupt:
        print(f"\n[{detector.name}] stopped")
    finally:
        source.close()
        if show:
            cv2.destroyAllWindows()
        close = getattr(detector, "close", None)
        if callable(close):
            close()
        if scorer is not None:
            scorer.report(detector.name)
