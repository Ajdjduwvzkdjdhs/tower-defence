"""パターンC — Ultralytics（姿勢推定で手首）＋ 周波数解析（FFT の位相直交判定）。

    使用ライブラリ: ultralytics + opencv-python (+ numpy)
    考え方        : 手首 1 点の軌跡を x(t), y(t) の 2 本の信号として溜め、窓 FFT で
                    主周波数を出す。円運動なら x と y が**同じ周波数で位相が 90° ずれる**
                    ので、その直交性（|sin Δφ|）で「回している/往復に振っているだけ」を分ける。
                    回転数 RPM = 主周波数[Hz] × 60。

旧ロジックとの違い:

  - **時刻付きの信号として扱い、欠測は補間する**。窓（既定 2 秒）の中で数フレーム
    抜けても、等間隔グリッドへ `np.interp` で埋めてからスペクトルを取るので、
    **手が一瞬フレーム外に出ても主周波数はほとんど動かない**。判定が瞬間値に
    依存しないのがこのパターンの本質。
    ただし補間が効くのは短い穴だけで、連続欠測が `--max-gap`（既定 0.5 秒＝窓の 1/4）を
    超えたら測定を諦める（埋めた直線が主ピークを別の周波数へずらすため）。
  - **必要なのは点 1 つだけ**。2 つの手の相対角を使わないので、**両手が重なっても**
    信号は途切れない（重なりは「同じ場所に 2 点」になるだけ）。
  - 直線的な往復運動（機体を前後に揺する等）は位相差が 0/180° に寄るので落とせる。
    「速く動いた＝削っている」ではなく「**周期的な円運動**」だけを削りと呼ぶ。
  - 窓が長いぶん止めたときの反応が鈍るので、直近 `--tail` 秒の振幅が小さいときは
    FFT を待たずに即「計測不能」へ落とす（停止の追従を確保）。
  - **対象は円 ROI ではなくカメラ映像の全体**。画面のどこでハンドルを回していても拾う
    （A・B のようなマウスでの ROI 合わせは不要＝現地でのキャリブレーション工程が無い）。
    追う点は「直前の点 → 直近の軌跡の重心 → 画面中心」の順に選ぶ。

注意（実機での前提）:
  - 既定の `yolo11n-pose.pt` は **人物の 17 キーポイント**モデルで、手首は肩・肘から
    推定される。真俯瞰で腕が写らない画角だと検出が落ちるので、その場合は手検出用の
    重みを `--model` で差し替える（キーポイントが無いモデルなら箱の中心を使う）。
  - 初回実行時に重みを自動ダウンロードする（ネットワークが要る）。

実行:
    uv sync --extra yolo          # ultralytics（torch を含む）を入れる
    uv run python rotate_periodicity.py
    uv run python rotate_periodicity.py --sim            # モデル無しでロジックだけ検証
    uv run python rotate_periodicity.py --sim --headless --duration 20
"""

from __future__ import annotations

import argparse
import math
from collections import deque

import cv2
import numpy as np

import common

WRIST_KEYPOINTS = (9, 10)     # COCO-pose: 9=左手首 / 10=右手首

DEFAULT_WINDOW_SEC = 2.0      # FFT 窓。長いほど欠測に強い／反応は遅い（分解能 = 1/窓）
DEFAULT_MIN_RPM = 30.0        # 探す周波数帯の下端（= 0.5Hz）
DEFAULT_MAX_RPM = 300.0       # 同 上端（= 5Hz）
DEFAULT_MIN_PEAK = 0.22       # 帯域内で主ピークが占めるパワーの割合（周期性の強さ）
DEFAULT_MIN_QUAD = 0.45       # |sin(位相差)|。1 で真円、0 で直線往復
DEFAULT_TAIL_SEC = 0.35       # 直近この秒数の振幅を見て「もう止まっている」を早く出す
DEFAULT_MIN_AMP_PX = 4.0      # 停止とみなす直近振幅[px]。映像全体が対象なので絶対値で持つ
GRID_HZ = 30.0                # 補間先の等間隔グリッド（Hz）
MIN_COVERAGE = 0.45           # 窓内でサンプルがある割合の下限（欠測が多すぎたら諦める）
DEFAULT_MAX_GAP_SEC = 0.5     # 連続欠測の上限。これを超えると補間した直線が波形を汚し、
                              # 「それらしいが誤った RPM」を出してしまうので測定を諦める


class PeriodicityDetector:
    name = "C: ultralytics pose + FFT quadrature (full frame)"
    uses_roi = False   # common.run が円 ROI ではなく「映像全体」を渡す

    def __init__(self, cfg: dict, args):
        self.roi: common.Roi | None = None
        self.window = float(args.window)
        self.min_hz = float(args.min_rpm) / 60.0
        self.max_hz = float(args.max_rpm) / 60.0
        self.min_peak = float(args.min_peak)
        self.min_quad = float(args.min_quad)
        self.tail = float(args.tail)
        self.min_amp = float(args.min_amp)
        self.max_gap = float(args.max_gap)
        self.stride = max(1, int(args.stride))
        self.imgsz = int(args.imgsz)
        self.kp_conf = float(cfg.get("yoloConfidence", 0.25))
        # (t, x, y)。x,y は画面中心からの相対（FFT は平均を落とすので基準はどこでもよい）。
        self._buf: deque[tuple[float, float, float]] = deque()
        self._last_pt: tuple[float, float] | None = None
        self._last_seen = 0.0
        self._frame_i = 0
        self._points: list[tuple[float, float]] = []
        self._size: tuple[int, int] | None = None   # (w, h)。手掛かりが無いときの中心
        self._stat = ("", 0.0, 0.0)   # (状態, peak, quad)
        self._model = None
        if not args.sim:   # --sim ではモデルを迂回して周波数解析だけを検証する
            from ultralytics import YOLO
            self._model = YOLO(str(args.model or cfg.get("yoloModel", "yolo11n-pose.pt")))

    # ---- 検出 ----------------------------------------------------------
    def _detect(self, frame) -> list[tuple[float, float]]:
        """手首キーポイント（無いモデルなら検出箱の中心）を画素座標で返す。"""
        res = self._model.predict(frame, imgsz=self.imgsz, conf=self.kp_conf, verbose=False)
        pts: list[tuple[float, float]] = []
        for r in res:
            kp = getattr(r, "keypoints", None)
            if kp is not None and kp.xy is not None and kp.xy.numel() > 0:
                xy = kp.xy.cpu().numpy()
                conf = (kp.conf.cpu().numpy() if kp.conf is not None
                        else np.ones(xy.shape[:2], dtype=np.float32))
                for p in range(xy.shape[0]):
                    for k in WRIST_KEYPOINTS:
                        if k < xy.shape[1] and conf[p, k] >= self.kp_conf:
                            pts.append((float(xy[p, k, 0]), float(xy[p, k, 1])))
                continue
            boxes = getattr(r, "boxes", None)   # キーポイントが無い重み（手検出等）
            if boxes is not None and boxes.xyxy is not None:
                for b in boxes.xyxy.cpu().numpy():
                    pts.append(((b[0] + b[2]) / 2.0, (b[1] + b[3]) / 2.0))
        return pts

    # ---- 信号づくり ----------------------------------------------------
    def _pick(self, points: list[tuple[float, float]], t: float):
        """信号にする 1 点を選ぶ。**映像全体が対象なので位置では絞らない**。

        優先順:
          1. 直前に追っていた点にいちばん近い候補（0.6 秒以内なら継続とみなす）
          2. 直近の軌跡の重心にいちばん近い候補（見失い明けに同じ手へ戻る）
          3. 画面中心にいちばん近い候補（何の手掛かりも無い起動直後）
        """
        if not points:
            return None
        if self._last_pt is not None and t - self._last_seen < 0.6:
            return min(points, key=lambda p: math.hypot(p[0] - self._last_pt[0],
                                                        p[1] - self._last_pt[1]))
        anchor = self._anchor()
        if anchor is not None:
            return min(points, key=lambda p: math.hypot(p[0] - anchor[0], p[1] - anchor[1]))
        cx, cy = (self._size[0] / 2.0, self._size[1] / 2.0) if self._size else self.roi.center
        return min(points, key=lambda p: math.hypot(p[0] - cx, p[1] - cy))

    def _anchor(self) -> tuple[float, float] | None:
        """溜めている軌跡の重心（＝ハンドルを回している辺り）を絶対座標で返す。"""
        if not self._buf:
            return None
        n = len(self._buf)
        return (self.roi.cx + sum(s[1] for s in self._buf) / n,
                self.roi.cy + sum(s[2] for s in self._buf) / n)

    def _analyze(self, t: float):
        """窓内の (t,x,y) を等間隔補間 → Hann 窓 → rFFT → 主周波数と位相直交性。"""
        if len(self._buf) < 8:
            return None, ("samples<8", 0.0, 0.0)
        tb = np.fromiter((s[0] for s in self._buf), dtype=np.float64)
        span = tb[-1] - tb[0]
        if span < self.window * 0.8:
            return None, (f"span={span:.1f}s (filling)", 0.0, 0.0)

        n = max(32, int(self.window * GRID_HZ))
        grid = np.linspace(t - self.window, t, n)
        # 欠測（枠外・検出漏れ）は線形補間で埋める。ここが「一瞬消えても平気」の要。
        xb = np.fromiter((s[1] for s in self._buf), dtype=np.float64)
        yb = np.fromiter((s[2] for s in self._buf), dtype=np.float64)
        coverage = float(np.mean([(np.abs(tb - g).min() < 0.25) for g in grid]))
        if coverage < MIN_COVERAGE:
            return None, (f"coverage={coverage:4.2f} (too sparse)", 0.0, 0.0)
        # 補間で埋められるのは「短い穴」だけ。窓の 1/4 を超えるような連続欠測は、
        # 埋めた直線そのものが強い低周波成分になり、主ピークが本来と違う所に立つ
        # （実測: 2 秒窓で 0.5 秒までは 60RPM を維持、0.9 秒で 84RPM と誤る）。
        max_gap = float(np.max(np.diff(tb))) if len(tb) > 1 else 0.0
        if max_gap > self.max_gap:
            return None, (f"gap={max_gap:4.2f}s > {self.max_gap} (lost too long)", 0.0, 0.0)
        xs = np.interp(grid, tb, xb)
        ys = np.interp(grid, tb, yb)

        # 直近 tail 秒がほぼ動いていないなら、窓の残りに過去の回転が残っていても止まりと見る。
        tail_n = max(4, int(self.tail * GRID_HZ))
        amp_tail = float(np.hypot(np.std(xs[-tail_n:]), np.std(ys[-tail_n:])))
        if amp_tail < self.min_amp:
            return None, (f"tail amp={amp_tail:4.1f}px (stopped)", 0.0, 0.0)

        win = np.hanning(n)
        xf = np.fft.rfft((xs - xs.mean()) * win)
        yf = np.fft.rfft((ys - ys.mean()) * win)
        freqs = np.fft.rfftfreq(n, d=self.window / (n - 1))
        power = np.abs(xf) ** 2 + np.abs(yf) ** 2

        band = (freqs >= self.min_hz) & (freqs <= self.max_hz)
        if not band.any() or power[band].sum() <= 0:
            return None, ("no band power", 0.0, 0.0)
        idx = np.arange(len(freqs))[band]
        k = int(idx[np.argmax(power[band])])
        peak_ratio = float(power[k] / power[band].sum())

        # 円運動なら x と y は同じ周波数で 90° ずれる（直線的な往復は 0/180°）。
        quad = float(abs(math.sin(math.atan2(xf[k].imag, xf[k].real)
                                  - math.atan2(yf[k].imag, yf[k].real))))
        if peak_ratio < self.min_peak:
            return None, (f"peak={peak_ratio:4.2f} (not periodic)", peak_ratio, quad)
        if quad < self.min_quad:
            return None, (f"quad={quad:4.2f} (linear shake)", peak_ratio, quad)

        # ピーク周辺を放物線で補間して周波数分解能（1/窓）より細かく読む。
        f = common.refine_peak_hz(power, k, freqs)
        return max(0.0, f) * 60.0, (f"peak={peak_ratio:4.2f} quad={quad:4.2f}",
                                    peak_ratio, quad)

    # ---- Detector 契約 --------------------------------------------------
    def update(self, frame, hint, t: float):
        self._frame_i += 1
        self._size = (frame.shape[1], frame.shape[0])
        if hint is not None:
            self._points = list(hint)
        elif self._frame_i % self.stride == 0:
            # 欠測に強い方式なので、推論は間引いてよい（stride で CPU を買う）。
            self._points = self._detect(frame)

        pt = self._pick(self._points, t)
        if pt is not None:
            self._last_pt, self._last_seen = pt, t
            self._buf.append((t, pt[0] - self.roi.cx, pt[1] - self.roi.cy))
        while self._buf and t - self._buf[0][0] > self.window:
            self._buf.popleft()

        rpm, self._stat = self._analyze(t)
        lines = [f"pts={len(self._points)} buf={len(self._buf)} "
                 f"{'tracking' if pt is not None else 'lost'}",
                 f"window={self.window:.1f}s  {self._stat[0]}"]
        return rpm, lines

    def draw(self, frame) -> None:
        for p in self._points:
            cv2.circle(frame, (int(p[0]), int(p[1])), 6, (200, 200, 0), 1)
        if self._last_pt is not None:
            cv2.circle(frame, (int(self._last_pt[0]), int(self._last_pt[1])), 9, (0, 200, 255), -1)
        # 溜めている軌跡（＝FFT にかけている信号）をそのまま描く。
        prev = None
        for _, x, y in self._buf:
            cur = (int(self.roi.cx + x), int(self.roi.cy + y))
            if prev is not None:
                cv2.line(frame, prev, cur, (255, 120, 0), 1)
            prev = cur


def main() -> None:
    ap = argparse.ArgumentParser(description="C: Ultralytics pose + FFT quadrature")
    common.add_common_args(ap)
    ap.add_argument("--model", default=None, help="ultralytics の重み（既定 yolo11n-pose.pt）")
    ap.add_argument("--imgsz", type=int, default=416, help="推論解像度")
    ap.add_argument("--stride", type=int, default=1, help="N フレームに 1 回だけ推論する")
    ap.add_argument("--window", type=float, default=DEFAULT_WINDOW_SEC, help="FFT 窓[秒]")
    ap.add_argument("--min-rpm", type=float, default=DEFAULT_MIN_RPM, help="探索帯の下端[RPM]")
    ap.add_argument("--max-rpm", type=float, default=DEFAULT_MAX_RPM, help="探索帯の上端[RPM]")
    ap.add_argument("--min-peak", type=float, default=DEFAULT_MIN_PEAK, help="主ピークのパワー割合")
    ap.add_argument("--min-quad", type=float, default=DEFAULT_MIN_QUAD, help="位相直交性 |sin dphi|")
    ap.add_argument("--tail", type=float, default=DEFAULT_TAIL_SEC, help="停止を早く見るための直近窓[秒]")
    ap.add_argument("--min-amp", type=float, default=DEFAULT_MIN_AMP_PX,
                    help="停止とみなす直近振幅[px]（映像全体が対象なので絶対値）")
    ap.add_argument("--max-gap", type=float, default=DEFAULT_MAX_GAP_SEC,
                    help="補間で埋めてよい連続欠測の上限[秒]。超えたら測定を諦める")
    args = ap.parse_args()

    cfg = common.load_config()
    common.run(PeriodicityDetector(cfg, args), args, cfg)


if __name__ == "__main__":
    main()
