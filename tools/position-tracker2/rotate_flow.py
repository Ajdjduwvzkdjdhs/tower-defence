"""パターンB — OpenCV のみ（ROI 内の密オプティカルフローから「回転成分」を測る）。

    使用ライブラリ: opencv-python (+ numpy)。学習済みモデルを一切使わない。
    考え方        : ハンドル周りの円 ROI で Farnebäck の密フローを求め、中心まわりの
                    **接線成分（回転成分＝渦度）** を動きの強さで重み付き平均する。
                    ω = Σ w·(r × v)/|r|²  ÷ Σ w   [rad/frame] → RPM。

旧ロジックとの違い:

  - **手を「見つけない」**。手・指・ハンドル・氷、ROI 内で回っている画素すべてが
    そのまま証拠になる。手の同定をしないので、**両手が重なっても影響が無い**
    （そもそも 2 つの手を区別していない）。
  - 手の一部が枠外に出ても、ROI に残った画素の回転成分だけで測り続けられる。
    完全に何も動かない間は「計測不能（None）」として上げ、粘りは `ShavingGate` に任せる。
  - **並進（腕ごと動かす・カメラ揺れ）を弾く**ために coherence を見る:
        coherence = |Σ w·(v·t̂)| / Σ w·|v|   （動きのうち接線方向に揃っている割合）
    平行移動は接線方向が打ち消し合って coherence が落ちるので、回転だけが残る。
  - スケール不変（(r×v)/|r|² は ROI の拡大縮小で変わらない）なので、ROI 半径を
    現地で変えても RPM の目盛りがズレない。

実行:
    uv run python rotate_flow.py
    uv run python rotate_flow.py --sim
    uv run python rotate_flow.py --sim --headless --duration 20
"""

from __future__ import annotations

import argparse

import cv2
import numpy as np

import common

DEFAULT_PATCH = 96          # ROI をこの正方形へ縮めてからフローを取る（速度と安定の折衷）
DEFAULT_MIN_MOTION = 0.30   # ROI の平均速度[patch px/frame]。これ未満は「動いていない」
DEFAULT_MIN_COHERENCE = 0.35  # 接線方向にどれだけ揃っているか（並進を弾く）
INNER_HOLE = 0.18           # 中心付近は 1/|r| が暴れるので除外する割合
EMA = 0.35                  # RPM の平滑化


class FlowCurlDetector:
    name = "B: opencv optical-flow curl (no model)"

    def __init__(self, patch: int, min_motion: float, min_coherence: float):
        self.roi: common.Roi | None = None
        self.patch = int(patch)
        self.min_motion = float(min_motion)
        self.min_coherence = float(min_coherence)
        self._prev: np.ndarray | None = None
        self._prev_t: float | None = None
        self._rpm_ema: float | None = None
        self._motion = 0.0
        self._coh = 0.0
        self._sign = 0.0
        self._flow: np.ndarray | None = None
        # patch 座標系の中心からのベクトル（毎フレーム作り直さない）。
        g = (np.arange(self.patch, dtype=np.float32) - (self.patch - 1) / 2.0)
        self._rx, self._ry = np.meshgrid(g, g)
        rr = self._rx ** 2 + self._ry ** 2
        rad = (self.patch - 1) / 2.0
        self._rr = np.maximum(rr, 1e-3)
        self._r = np.sqrt(self._rr)
        self._mask = ((rr <= rad ** 2) & (self._r >= rad * INNER_HOLE)).astype(np.float32)

    def _crop(self, frame) -> np.ndarray | None:
        """ROI の外接正方形を切り出して patch×patch のグレースケールへ。"""
        r = self.roi.radius
        x0, y0 = int(self.roi.cx - r), int(self.roi.cy - r)
        x1, y1 = int(self.roi.cx + r), int(self.roi.cy + r)
        h, w = frame.shape[:2]
        # 画面外へはみ出した ROI は 0 埋めで受ける（枠際でも判定を続けるため）。
        pad_l, pad_t = max(0, -x0), max(0, -y0)
        pad_r, pad_b = max(0, x1 - w), max(0, y1 - h)
        sub = frame[max(0, y0):min(h, y1), max(0, x0):min(w, x1)]
        if sub.size == 0:
            return None
        if pad_l or pad_t or pad_r or pad_b:
            sub = cv2.copyMakeBorder(sub, pad_t, pad_b, pad_l, pad_r, cv2.BORDER_CONSTANT)
        gray = cv2.cvtColor(sub, cv2.COLOR_BGR2GRAY)
        return cv2.resize(gray, (self.patch, self.patch), interpolation=cv2.INTER_AREA)

    def update(self, frame, hint, t: float):
        cur = self._crop(frame)
        if cur is None:
            return None, ["ROI が画面外"]

        prev, prev_t = self._prev, self._prev_t
        self._prev, self._prev_t = cur, t
        if prev is None or prev_t is None:
            return None, ["first frame"]
        dt = t - prev_t
        if dt <= 1e-4 or dt > 0.5:      # 停止・カクつきは信用しない
            return None, [f"dt={dt:.3f}s outlier"]

        flow = cv2.calcOpticalFlowFarneback(prev, cur, None,
                                            pyr_scale=0.5, levels=3, winsize=15,
                                            iterations=3, poly_n=5, poly_sigma=1.2, flags=0)
        self._flow = flow
        vx, vy = flow[..., 0], flow[..., 1]
        speed = cv2.magnitude(vx, vy)

        w = speed * self._mask                       # 動いている画素だけを証拠にする
        w_sum = float(w.sum())
        self._motion = float(w_sum / max(1.0, float(self._mask.sum())))
        if w_sum < 1e-3 or self._motion < self.min_motion:
            self._coh = 0.0
            return None, [f"motion={self._motion:4.2f} < {self.min_motion} (still)"]

        cross = self._rx * vy - self._ry * vx        # r × v の z 成分
        omega = float((w * (cross / self._rr)).sum() / w_sum)   # [rad/frame]
        tangential = cross / self._r                 # 接線方向の速度成分
        self._coh = float(abs((w * tangential).sum()) / max(1e-6, (w * speed).sum()))
        self._sign = 1.0 if omega >= 0 else -1.0

        if self._coh < self.min_coherence:
            # 動いてはいるが回っていない（腕ごと平行移動・カメラ揺れ・人が横切る）。
            return None, [f"coh={self._coh:4.2f} < {self.min_coherence} (translation)"]

        rpm = abs(omega) / dt * 60.0 / (2.0 * np.pi)
        self._rpm_ema = rpm if self._rpm_ema is None else (1 - EMA) * self._rpm_ema + EMA * rpm
        return self._rpm_ema, [
            f"motion={self._motion:4.2f}px/f  coh={self._coh:4.2f}  "
            f"dir={'cw' if self._sign > 0 else 'ccw'}",
            f"omega={omega:+6.3f} rad/frame  dt={dt * 1000:4.0f}ms  patch={self.patch}",
        ]

    def draw(self, frame) -> None:
        if self._flow is None:
            return
        # フローを間引いて ROI 上に重ねる（回っていれば矢印が同心円に並ぶ）。
        step = max(4, self.patch // 12)
        scale = (2.0 * self.roi.radius) / self.patch
        x0, y0 = self.roi.cx - self.roi.radius, self.roi.cy - self.roi.radius
        for j in range(step // 2, self.patch, step):
            for i in range(step // 2, self.patch, step):
                if self._mask[j, i] < 0.5:
                    continue
                vx, vy = self._flow[j, i]
                if vx * vx + vy * vy < 0.25:
                    continue
                px, py = int(x0 + i * scale), int(y0 + j * scale)
                qx, qy = int(px + vx * scale * 3), int(py + vy * scale * 3)
                cv2.arrowedLine(frame, (px, py), (qx, qy), (0, 200, 255), 1, tipLength=0.35)
        cv2.circle(frame, (int(self.roi.cx), int(self.roi.cy)),
                   int(self.roi.radius * INNER_HOLE), (80, 80, 80), 1)


def main() -> None:
    ap = argparse.ArgumentParser(description="B: OpenCV dense optical-flow curl")
    common.add_common_args(ap)
    ap.add_argument("--patch", type=int, default=DEFAULT_PATCH, help="フロー計算の解像度[px]")
    ap.add_argument("--min-motion", type=float, default=DEFAULT_MIN_MOTION,
                    help="静止とみなす平均速度のしきい値[patch px/frame]")
    ap.add_argument("--min-coherence", type=float, default=DEFAULT_MIN_COHERENCE,
                    help="回転とみなす接線成分の割合（下げると並進も拾う）")
    args = ap.parse_args()

    cfg = common.load_config()
    detector = FlowCurlDetector(args.patch, args.min_motion, args.min_coherence)
    common.run(detector, args, cfg)


if __name__ == "__main__":
    main()
