"""パターンD — A の誤検出対策版（「回している」ことの実在を検証してから RPM を出す）。

    使用ライブラリ: mediapipe + opencv (+ numpy)。A（rotate_landmark.py）の追跡をそのまま使う。

A の弱点:
    A は「手が中心のまわりを何度/秒で動いたか」しか見ていない。そのため
    **ハンドルを回さず、かき氷機を持った手を大きな弧に沿って動かすだけ**でも
    角速度が立ってしまい、削っていると誤判定する（合成台本 `sweep` で A は 0% だった）。

D の対策 — 「その動きは本当にハンドル回転か」を 4 つの物理的な条件で検証する:

  1. **周回（いちばん効く）**: ハンドル回しは同じ向きに何周も回る。弧をなぞる動きは
     半円（180°）で折り返し、**正味の回転角が 360° に達しない**。直近
     `--turn-window` 秒の累積角の正味変化が `--min-turn-deg`（既定 360°＝1 周）に
     届かないトラックは採用しない。往復では原理的に到達できない。
  2. **方向の一貫性**: 上と同じ窓で「正味の向きと同じ向きに進んだ量 ÷ 総移動量」を見る。
     往復運動は 0.5 付近に落ちる（`--min-consistency` 既定 0.75）。
  3. **軌道半径**: ハンドルのノブが描く円は小さい。機体ごと動かす弧は大きい。
     フィット半径が `[--min-radius, --max-radius]` px の外なら採用しない。
     **この 2 値だけは画角・カメラ高さに依存する**ので現地で合わせる（HUD に実測半径が出る）。
  4. **真円度**: 円フィットの RMS 残差 ÷ 半径 が `--max-residual` を超えたら採用しない。
     直線的な往復や不規則な動きは円に乗らない。

代償は **ON までの遅れ**。「1 周回った」ことを確認してから ON にするので、60RPM なら
約 1 秒かかる。速さより誤検出の少なさが要るときに使う（`--min-turn-deg` を 270 などへ
下げれば速くなるが、その分だけ広い弧を通してしまう）。

実行:
    uv run python rotate_landmark_strict.py
    uv run python rotate_landmark_strict.py --sim --sim-scenario strict --headless --duration 26
"""

from __future__ import annotations

import argparse
from collections import deque

import cv2
import numpy as np

import common
import rotate_landmark as base

DEFAULT_TURN_WINDOW_SEC = 3.0   # 周回を数える窓
DEFAULT_MIN_TURN_DEG = 360.0    # この窓で最低これだけ「同じ向きに」回っていること
DEFAULT_MIN_CONSISTENCY = 0.75  # 正味の向きに進んだ割合（往復は 0.5 付近）
DEFAULT_MIN_RADIUS_PX = 10.0
DEFAULT_MAX_RADIUS_PX = 160.0
DEFAULT_MAX_RESIDUAL = 0.25     # 円フィットの RMS 残差 ÷ 半径


class StrictTrack(base.HandTrack):
    """A のトラックに「累積角の履歴」を足したもの。周回と方向の一貫性を数えるため。"""

    TURN_WINDOW = DEFAULT_TURN_WINDOW_SEC   # 検出器が起動時に上書きする

    def __init__(self, pt, t: float, roi: common.Roi, window_sec: float):
        super().__init__(pt, t, roi, window_sec)
        self.turns: deque[tuple[float, float]] = deque([(t, 0.0)])

    def update(self, pt, t: float) -> None:
        super().update(pt, t)
        self.turns.append((t, self.vel.cumulative_deg))
        while len(self.turns) > 2 and t - self.turns[0][0] > self.TURN_WINDOW:
            self.turns.popleft()

    @property
    def net_turn_deg(self) -> float:
        """窓内の正味の回転角[deg]。往復すると打ち消し合ってゼロに近づく。"""
        return self.turns[-1][1] - self.turns[0][1] if len(self.turns) >= 2 else 0.0

    @property
    def consistency(self) -> float:
        """正味の向きに進んだ量 ÷ 総移動量。一方向に回り続ければ 1、往復なら ~0.5。"""
        if len(self.turns) < 3:
            return 0.0
        steps = np.diff(np.array([c for _, c in self.turns]))
        total = float(np.abs(steps).sum())
        if total < 1e-6:
            return 0.0
        return float(abs(steps.sum()) / total)

    @property
    def circularity(self) -> float | None:
        """円フィットの RMS 残差 ÷ 半径。小さいほど真円に近い。"""
        c = self.center
        if not c.fitted or not c.radius:
            return None
        pts = np.array([[x, y] for _, x, y in c._buf], dtype=np.float64)
        if len(pts) < 6:
            return None
        cx, cy = c.center
        d = np.hypot(pts[:, 0] - cx, pts[:, 1] - cy)
        return float(np.sqrt(np.mean((d - c.radius) ** 2)) / max(1e-6, c.radius))


class StrictLandmarkDetector(base.LandmarkCrankDetector):
    name = "D: mediapipe + verified rotation (full frame)"
    TRACK_CLASS = StrictTrack

    def __init__(self, cfg: dict, args):
        super().__init__(cfg, sim=args.sim, window_sec=args.window, coast_sec=args.coast,
                         gate_px=args.gate_px, max_hands=args.max_hands)
        StrictTrack.TURN_WINDOW = float(args.turn_window)
        self.min_turn = float(args.min_turn_deg)
        self.min_consistency = float(args.min_consistency)
        self.min_radius = float(args.min_radius)
        self.max_radius = float(args.max_radius)
        self.max_residual = float(args.max_residual)
        self._reject = ""

    def _verify(self, tr: StrictTrack) -> tuple[bool, str]:
        """「本当にハンドルを回しているか」の 4 条件。落ちた理由を文字列で返す。"""
        c = tr.center
        if not c.fitted or not c.radius:
            return False, "center not fitted"
        if not (self.min_radius <= c.radius <= self.max_radius):
            return False, f"radius={c.radius:5.1f}px out of [{self.min_radius:.0f},{self.max_radius:.0f}]"
        circ = tr.circularity
        if circ is not None and circ > self.max_residual:
            return False, f"circularity={circ:4.2f} > {self.max_residual} (not a circle)"
        if abs(tr.net_turn_deg) < self.min_turn:
            return False, f"turned {abs(tr.net_turn_deg):5.0f}deg < {self.min_turn:.0f} (not a full turn)"
        if tr.consistency < self.min_consistency:
            return False, f"consistency={tr.consistency:4.2f} < {self.min_consistency} (back-and-forth)"
        return True, ""

    def update(self, frame, hint, t: float):
        points = hint if hint is not None else self._detect(frame)
        self._associate(points, t)

        # 検証を通ったトラックの中から、いちばん速いものを採用する。
        self._best = None
        rpm: float | None = None
        fastest_rejected: tuple[float, str] | None = None
        for tr in self._tracks:
            r = tr.rpm
            if r is None:
                continue
            ok, why = self._verify(tr)
            if not ok:
                if fastest_rejected is None or r > fastest_rejected[0]:
                    fastest_rejected = (r, why)
                continue
            if rpm is None or r > rpm:
                rpm, self._best = r, tr

        if self._best is not None:
            self._reject = ""
            c = self._best.center
            lines = [f"hands={len(points)} tracks={len(self._tracks)}",
                     f"verified: r={c.radius:5.1f}px turn={self._best.net_turn_deg:+6.0f}deg "
                     f"cons={self._best.consistency:4.2f} circ={self._best.circularity or 0:4.2f}"]
        else:
            self._reject = fastest_rejected[1] if fastest_rejected else "no rotating hand"
            lines = [f"hands={len(points)} tracks={len(self._tracks)}",
                     f"rejected: {self._reject}"]
        return rpm, lines

    def draw(self, frame) -> None:
        super().draw(frame)
        if self._best is None:
            return
        # 採用したトラックの軌跡を描く（何を「1 周」と数えたかを目で確認できる）。
        pts = [(int(x), int(y)) for _, x, y in self._best.center._buf]
        for a, b in zip(pts, pts[1:]):
            cv2.line(frame, a, b, (0, 255, 120), 1)


def main() -> None:
    ap = argparse.ArgumentParser(description="D: MediaPipe + verified crank rotation")
    common.add_common_args(ap)
    ap.add_argument("--window", type=float, default=base.DEFAULT_WINDOW_SEC, help="角速度の回帰ウィンドウ[秒]")
    ap.add_argument("--coast", type=float, default=base.DEFAULT_COAST_SEC, help="見失ったトラックを生かす秒数")
    ap.add_argument("--gate-px", type=float, default=base.DEFAULT_GATE_PX, help="トラック対応付けの最大距離[px]")
    ap.add_argument("--max-hands", type=int, default=base.DEFAULT_MAX_HANDS, help="同時に追う手の数")
    ap.add_argument("--turn-window", type=float, default=DEFAULT_TURN_WINDOW_SEC,
                    help="周回を数える窓[秒]")
    ap.add_argument("--min-turn-deg", type=float, default=DEFAULT_MIN_TURN_DEG,
                    help="窓内で必要な正味の回転角[deg]（既定 360＝1 周）")
    ap.add_argument("--min-consistency", type=float, default=DEFAULT_MIN_CONSISTENCY,
                    help="回転方向の一貫性（往復は 0.5 付近）")
    ap.add_argument("--min-radius", type=float, default=DEFAULT_MIN_RADIUS_PX, help="軌道半径の下限[px]")
    ap.add_argument("--max-radius", type=float, default=DEFAULT_MAX_RADIUS_PX, help="軌道半径の上限[px]")
    ap.add_argument("--max-residual", type=float, default=DEFAULT_MAX_RESIDUAL,
                    help="円フィットの RMS 残差÷半径の上限")
    args = ap.parse_args()

    cfg = common.load_config()
    common.run(StrictLandmarkDetector(cfg, args), args, cfg)


if __name__ == "__main__":
    main()
