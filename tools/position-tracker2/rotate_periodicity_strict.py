"""パターンE — C の誤検出対策版（「周期的に見える」だけでは削りと認めない）。

    使用ライブラリ: ultralytics + opencv (+ numpy)。C（rotate_periodicity.py）の信号づくりを使う。

C の弱点:
    C は「主ピークが立っていて、x と y の位相が直交ぎみ」なら削りと判定する。ところが
    しきい値が緩く（`quad>=0.45` は位相差 27〜153° を許す）、**ランダムな揺れでも
    かなりの確率で通ってしまう**。手首キーポイントが数 px ばたつく、左右の手を
    取り違えて点が飛ぶ、機体を前後に揺する——どれも「削っていないのに ON」になる
    （合成台本で C は jitter 22.8% / swing 69.8% しか正解できなかった）。

E の対策 — 6 つの条件を足して「本物の円運動」だけを通す:

  1. **位相直交を厳しく**: `--min-quad` 0.45 → **0.80**（位相差 53〜127°）。
     ランダム位相が偶然通る確率を大きく下げる。
  2. **振幅のつり合い（アスペクト比）**: 円運動なら x と y の振幅が同程度になる。
     `min(|X|,|Y|)/max(|X|,|Y|) >= --min-aspect`（既定 0.5）。弧をなぞる動きや
     直線的な往復は片方の軸に偏るので、ここで落ちる。
  3. **ピークの卓越度を厳しく**: `--min-peak` 0.22 → **0.35**。
  4. **周波数の安定性**: 直近 `--stable-sec`（既定 0.6 秒）の推定周波数のばらつきが
     `--max-drift-bins`（既定 1.0 ビン）以内であること。雑音の主ピークは帯域内を
     何ビンも跳ぶので通らない。ばらつきを %ではなく**ビン数**で測るのは、推定精度
     そのものが ±0.5 ビン（放物線補間の限界）で、%だと本物の回転まで落ちるため。
  5. **軌道半径**: 円フィットの半径が `[--min-radius, --max-radius]` px の範囲にあること。
     数 px のばたつき（小さすぎ）も、機体ごと動かす大きな弧（大きすぎ）も落ちる。
  5a. **円への当てはまり**: 円フィットの RMS 残差 ÷ 半径 が `--max-residual` 以下であること。
     短い弧に無理に円を当てると中心がずれ、そのずれた中心のまわりで「回っているように
     見える」ことがある。残差で弾く。
  5b. **正味の回転角**: フィット中心のまわりで、窓の中の軌跡が正味
     `--min-sweep-deg`（既定 300°）以上まわっていること。弧を往復する動きは
     行って戻るので正味の角度が伸びない——**周期的に見えても 1 周しない動きを外す**、
     このパターンでいちばん効く門。
  6. **点の飛びの混入**: 直近 `--jump-window`（既定 0.8 秒）で `--jump-px`（既定 120px）を
     超える座標の飛びが `--max-jumps`（既定 2）回を超えたら、追う手を取り違えていると
     みなして棄却する（取り違えは繰り返し起きるが、正常時の 1 回の飛びは引きずらない）。

代償は感度。ゆっくり・小さく回すと 2・5 で落ちやすくなるので、現地では HUD に出る
実測値（aspect / radius / drift）を見ながら `--min-aspect` と半径帯を合わせる。

実行:
    uv run python rotate_periodicity_strict.py
    uv run python rotate_periodicity_strict.py --sim --sim-scenario strict --headless --duration 26
"""

from __future__ import annotations

import argparse
import math
from collections import deque

import numpy as np

import common
import rotate_periodicity as base

# C の 2.0 秒より短くしてある。門を増やしたぶん「周期的に見えるだけ」を窓の長さで
# 我慢する必要が減り、短くすると過渡（手を寄せる・持ち替える）からの復帰が速くなる。
# 実測（合成 60RPM）: 2.0秒→1.5秒で normal 76.6%→86.0%、誤検出局面は 100% のまま。
# ただし周波数分解能は 1/窓 = 40RPM になるので、40RPM を下回る遅い回しを見るなら 2.0 に戻す。
DEFAULT_WINDOW_SEC = 1.5
DEFAULT_MIN_PEAK = 0.35
DEFAULT_MIN_QUAD = 0.80
DEFAULT_MIN_ASPECT = 0.50
DEFAULT_MIN_AMP_PX = 10.0
DEFAULT_MIN_RADIUS_PX = 25.0
DEFAULT_MAX_RADIUS_PX = 160.0
DEFAULT_STABLE_SEC = 0.6
DEFAULT_MAX_DRIFT_BINS = 1.0
DEFAULT_MIN_SWEEP_DEG = 300.0
DEFAULT_MAX_RESIDUAL = 0.25
DEFAULT_JUMP_PX = 120.0
DEFAULT_MAX_JUMPS = 2
DEFAULT_JUMP_WINDOW_SEC = 0.8


class StrictPeriodicityDetector(base.PeriodicityDetector):
    name = "E: ultralytics pose + strict periodicity (full frame)"

    def __init__(self, cfg: dict, args):
        super().__init__(cfg, args)
        self.min_aspect = float(args.min_aspect)
        self.min_radius = float(args.min_radius)
        self.max_radius = float(args.max_radius)
        self.stable_sec = float(args.stable_sec)
        self.max_drift_bins = float(args.max_drift_bins)
        self.min_sweep_deg = float(args.min_sweep_deg)
        self.max_residual = float(args.max_residual)
        self.jump_px = float(args.jump_px)
        self.max_jumps = int(args.max_jumps)
        self.jump_window = float(args.jump_window)
        self._jumps: deque[tuple[float, float]] = deque()   # (t, 直前の点からの距離)
        self._freqs: deque[tuple[float, float]] = deque()   # (t, 主周波数[Hz])

    # ---- 点の飛び（取り違え）を記録する ---------------------------------
    def _pick(self, points, t: float):
        pt = super()._pick(points, t)
        if pt is not None and self._last_pt is not None:
            self._jumps.append((t, math.hypot(pt[0] - self._last_pt[0],
                                              pt[1] - self._last_pt[1])))
        while self._jumps and t - self._jumps[0][0] > self.jump_window:
            self._jumps.popleft()
        return pt

    # ---- 解析（C の門に 6 つ足す） --------------------------------------
    def _analyze(self, t: float):
        if len(self._buf) < 8:
            return None, ("samples<8", 0.0, 0.0)
        tb = np.fromiter((s[0] for s in self._buf), dtype=np.float64)
        span = tb[-1] - tb[0]
        if span < self.window * 0.8:
            return None, (f"span={span:.1f}s (filling)", 0.0, 0.0)

        n = max(32, int(self.window * base.GRID_HZ))
        grid = np.linspace(t - self.window, t, n)
        xb = np.fromiter((s[1] for s in self._buf), dtype=np.float64)
        yb = np.fromiter((s[2] for s in self._buf), dtype=np.float64)
        coverage = float(np.mean([(np.abs(tb - g).min() < 0.25) for g in grid]))
        if coverage < base.MIN_COVERAGE:
            return None, (f"coverage={coverage:4.2f} (too sparse)", 0.0, 0.0)
        max_gap = float(np.max(np.diff(tb))) if len(tb) > 1 else 0.0
        if max_gap > self.max_gap:
            return None, (f"gap={max_gap:4.2f}s > {self.max_gap} (lost too long)", 0.0, 0.0)

        # 追加①: 追っている点が窓の中で何度も飛んでいたら、手を取り違えている。
        jumps = sum(1 for _, d in self._jumps if d > self.jump_px)
        if jumps > self.max_jumps:
            return None, (f"jumps={jumps} > {self.max_jumps} (track switching)", 0.0, 0.0)

        xs = np.interp(grid, tb, xb)
        ys = np.interp(grid, tb, yb)

        tail_n = max(4, int(self.tail * base.GRID_HZ))
        amp_tail = float(np.hypot(np.std(xs[-tail_n:]), np.std(ys[-tail_n:])))
        if amp_tail < self.min_amp:
            return None, (f"tail amp={amp_tail:4.1f}px (stopped)", 0.0, 0.0)

        # 追加②: 軌道の大きさ。ばたつき（小さすぎ）も機体ごとの移動（大きすぎ）も落とす。
        # 重心からの RMS ではなく**円フィットの半径**で測る。弧を往復する動きは、通った
        # 範囲こそ狭いが乗っている円は大きいので、フィットしないと見抜けない。
        fit = common.circle_fit(np.column_stack([xs, ys]))
        radius = (fit[2] if fit is not None
                  else float(np.sqrt(np.mean((xs - xs.mean()) ** 2 + (ys - ys.mean()) ** 2))))
        if not (self.min_radius <= radius <= self.max_radius):
            return None, (f"radius={radius:5.1f}px out of "
                          f"[{self.min_radius:.0f},{self.max_radius:.0f}]", 0.0, 0.0)

        # 追加②a: 円への当てはまり。中心を取り違えたフィットはここで落ちる。
        if fit is not None:
            dd = np.hypot(xs - fit[0], ys - fit[1])
            resid = float(np.sqrt(np.mean((dd - fit[2]) ** 2)) / max(1e-6, fit[2]))
            if resid > self.max_residual:
                return None, (f"residual={resid:4.2f} > {self.max_residual} (not a circle)",
                              0.0, 0.0)

        # 追加②b: フィット中心まわりに正味どれだけ回ったか。弧の往復は行って戻るので伸びない。
        swept = 0.0
        if fit is not None:
            ang = np.unwrap(np.arctan2(ys - fit[1], xs - fit[0]))
            swept = abs(float(np.degrees(ang[-1] - ang[0])))
            if swept < self.min_sweep_deg:
                return None, (f"swept={swept:5.0f}deg < {self.min_sweep_deg:.0f} "
                              f"(not a full turn)", 0.0, 0.0)

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
        ax, ay = float(abs(xf[k])), float(abs(yf[k]))
        aspect = min(ax, ay) / max(1e-9, max(ax, ay))
        quad = float(abs(math.sin(math.atan2(xf[k].imag, xf[k].real)
                                  - math.atan2(yf[k].imag, yf[k].real))))

        # 追加③: 主周波数の履歴。棄却されたフレームも含めて「ピークが同じ所に居るか」を見る。
        # 記録するのは**放物線補間した後**の周波数。ビン中心のままだと、隣のビンへ移った
        # だけで 50% のばらつきに見えてしまい、本物の回転を落としてしまう。
        f0 = common.refine_peak_hz(power, k, freqs)
        self._freqs.append((t, f0))
        while self._freqs and t - self._freqs[0][0] > self.stable_sec:
            self._freqs.popleft()

        if peak_ratio < self.min_peak:
            return None, (f"peak={peak_ratio:4.2f} < {self.min_peak} (not periodic)",
                          peak_ratio, quad)
        if aspect < self.min_aspect:
            return None, (f"aspect={aspect:4.2f} < {self.min_aspect} (not circular)",
                          peak_ratio, quad)
        if quad < self.min_quad:
            return None, (f"quad={quad:4.2f} < {self.min_quad} (not orthogonal)",
                          peak_ratio, quad)

        # 追加④: 直近 stable_sec の主周波数がばらついていたら、たまたま立ったピーク。
        fs = np.array([f for _, f in self._freqs], dtype=np.float64)
        if len(fs) >= 4 and (t - self._freqs[0][0]) >= self.stable_sec * 0.8:
            bin_hz = float(freqs[1] - freqs[0])
            drift = float((fs.max() - fs.min()) / max(1e-9, bin_hz))
            if drift > self.max_drift_bins:
                return None, (f"drift={drift:4.2f}bin > {self.max_drift_bins} (unstable peak)",
                              peak_ratio, quad)
        else:
            return None, ("stability window filling", peak_ratio, quad)

        return max(0.0, f0) * 60.0, (f"peak={peak_ratio:4.2f} quad={quad:4.2f} "
                                    f"aspect={aspect:4.2f} r={radius:4.0f}px "
                                    f"swept={swept:4.0f}deg",
                                    peak_ratio, quad)


def main() -> None:
    ap = argparse.ArgumentParser(description="E: Ultralytics pose + strict periodicity")
    common.add_common_args(ap)
    ap.add_argument("--model", default=None, help="ultralytics の重み（既定 yolo11n-pose.pt）")
    ap.add_argument("--imgsz", type=int, default=416, help="推論解像度")
    ap.add_argument("--stride", type=int, default=1, help="N フレームに 1 回だけ推論する")
    ap.add_argument("--window", type=float, default=DEFAULT_WINDOW_SEC, help="FFT 窓[秒]")
    ap.add_argument("--min-rpm", type=float, default=base.DEFAULT_MIN_RPM, help="探索帯の下端[RPM]")
    ap.add_argument("--max-rpm", type=float, default=base.DEFAULT_MAX_RPM, help="探索帯の上端[RPM]")
    ap.add_argument("--tail", type=float, default=base.DEFAULT_TAIL_SEC, help="停止を早く見る直近窓[秒]")
    ap.add_argument("--max-gap", type=float, default=base.DEFAULT_MAX_GAP_SEC, help="補間してよい欠測[秒]")
    ap.add_argument("--min-peak", type=float, default=DEFAULT_MIN_PEAK, help="主ピークのパワー割合")
    ap.add_argument("--min-quad", type=float, default=DEFAULT_MIN_QUAD, help="位相直交性 |sin dphi|")
    ap.add_argument("--min-aspect", type=float, default=DEFAULT_MIN_ASPECT,
                    help="x と y の振幅比（円は 1 に近い）")
    ap.add_argument("--min-amp", type=float, default=DEFAULT_MIN_AMP_PX, help="停止とみなす直近振幅[px]")
    ap.add_argument("--min-radius", type=float, default=DEFAULT_MIN_RADIUS_PX, help="軌道半径の下限[px]")
    ap.add_argument("--max-radius", type=float, default=DEFAULT_MAX_RADIUS_PX, help="軌道半径の上限[px]")
    ap.add_argument("--stable-sec", type=float, default=DEFAULT_STABLE_SEC, help="周波数安定性を見る窓[秒]")
    ap.add_argument("--max-residual", type=float, default=DEFAULT_MAX_RESIDUAL,
                    help="円フィットの RMS 残差÷半径の上限")
    ap.add_argument("--min-sweep-deg", type=float, default=DEFAULT_MIN_SWEEP_DEG,
                    help="窓の中で必要な正味の回転角[deg]（往復運動を外す）")
    ap.add_argument("--max-drift-bins", type=float, default=DEFAULT_MAX_DRIFT_BINS,
                    help="その窓での主周波数のばらつき上限[ビン]")
    ap.add_argument("--jump-px", type=float, default=DEFAULT_JUMP_PX, help="取り違えとみなす座標の飛び[px]")
    ap.add_argument("--max-jumps", type=int, default=DEFAULT_MAX_JUMPS, help="窓内で許す飛びの回数")
    ap.add_argument("--jump-window", type=float, default=DEFAULT_JUMP_WINDOW_SEC,
                    help="飛びを数える窓[秒]。短いほど 1 回の取り違えを引きずらない")
    args = ap.parse_args()

    cfg = common.load_config()
    common.run(StrictPeriodicityDetector(cfg, args), args, cfg)


if __name__ == "__main__":
    main()
