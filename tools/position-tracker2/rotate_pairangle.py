"""パターンF — 旧ロジック（2 手のなす角）そのままで、2 つの弱点だけを塞いだ版。

    使用ライブラリ: mediapipe + opencv (+ numpy)

**基本ロジックは以前のまま**:
    片方の手を中心に、もう片方の手との**なす角**を見る。一定フレーム以内の角度変化が
    小さければ削っていない、大きければ削っている。
    （本実装では「角度変化 ÷ 経過時間」を RPM に直して共通ゲートに渡すので、
      旧来の「角度変化のしきい値」は `ShavingGate` の `onRpm`/`offRpm` に対応する。
      例: `--frames 8`・30fps なら、12RPM は 8 フレームで約 19° の変化にあたる。）

塞いだ弱点は 2 つだけ。回転の測り方そのものには手を入れていない。

  ■ 手が一瞬カメラの範囲外に出ると判定が消える
    - なす角は 2 点そろって初めて定義できるので、**計算できない間は角度の更新を凍結し、
      直前の角速度を `--hold` 秒だけ維持する**（0 に落とさない）。旧実装は瞬間値を
      そのまま判定にしていたので、片手が欠けた瞬間に判定が消えていた。
    - 復帰時は**予測付き unwrap**（前回の角速度で予測した角度のまわりで折り返す）。
      素の unwrap だと、欠落中に 180°以上回っていた場合に逆回転として読んでしまう。
    - `--hold` を過ぎたら素直に「計測不能」を返し、あとは共通の `ShavingGate` の
      ホールド（既定 0.45 秒）に委ねる。二段構えで途切れを埋める。

  ■ ハンドルを回している時に手が重なると判定がうまくいかない
    - 2 点の距離が `--min-sep` px を切ったら、なす角は数値的に暴れる（わずかな検出
      ずれで角度が跳ねる）。この間は**角度更新を凍結**して直前の角速度を維持する。
      重なりは「削っている最中」に起きるので、凍結＝直前の判定の維持でよい。
    - 左右の取り違え対策として、**MediaPipe の handedness を使わず、前フレームの位置
      からの最近傍で 2 つのスロットへ割り当てる**。旧実装が重なりで壊れた主因は、
      重なった前後で左右が入れ替わり、なす角が 180° 飛ぶことだった。
      （なお、どちらを「中心」に選んでも角度は 180° ずれるだけで**変化率は同じ**なので、
        中心役の選び方は判定に影響しない。効くのは割り当ての一貫性だけ。）

**このパターンが直していないもの**: ハンドルを回さずに機体ごと大きな弧に沿って動かす
動き（2 手の相対角も回るので削りと判定される）。そこまで見るなら
`rotate_landmark_strict.py`（D）を使う。

実行:
    uv run python rotate_pairangle.py
    uv run python rotate_pairangle.py --sim --sim-scenario strict --headless --duration 28
"""

from __future__ import annotations

import argparse
import math
from collections import deque

import cv2

import common
import rotate_landmark as base

DEFAULT_FRAMES = 8          # 「一定フレーム以内での角度変化」のフレーム数（旧ロジックの窓）
# 欠落・重なりで角度を凍結したまま直前の角速度を維持する上限。共通ゲートのホールド
# （既定 0.45 秒）と直列に効くので、実効的にはこの値＋0.45 秒ぶん途切れを埋められる。
# 長くするほど途切れに強くなるが、「重なったまま回すのをやめた」ときに OFF が遅れる。
DEFAULT_HOLD_SEC = 0.80
DEFAULT_MIN_SEP_PX = 45.0   # これより近づいたら「重なり」とみなして角度更新を凍結
DEFAULT_GATE_PX = 120.0     # スロットへの最近傍割り当ての最大距離


class PairAngleDetector:
    name = "F: pair-angle (legacy logic, hardened)"
    uses_roi = False   # 映像全体が対象（なす角は位置に依らない）

    def __init__(self, cfg: dict, args):
        self.roi: common.Roi | None = None
        self.frames = max(2, int(args.frames))
        self.hold = float(args.hold)
        self.min_sep = float(args.min_sep)
        self.gate_px = float(args.gate_px)

        # 2 つのスロット（どちらが中心役でも変化率は同じ。一貫して割り当てることだけが重要）。
        self._slots: list[tuple[float, float] | None] = [None, None]
        self._slot_t: list[float] = [0.0, 0.0]
        self._hist: deque[tuple[float, float]] = deque(maxlen=self.frames + 1)  # (t, 累積角)
        self._last_raw: float | None = None
        self._cum = 0.0
        self._last_dps = 0.0
        self._last_rpm: float | None = None
        self._last_good = 0.0
        self._state = "init"
        self._points: list[tuple[float, float]] = []

        self._hands = None
        if not args.sim:
            import mediapipe as mp
            self._hands = mp.solutions.hands.Hands(
                static_image_mode=False, max_num_hands=2, model_complexity=0,
                min_detection_confidence=float(cfg.get("minDetectionConfidence", 0.5)),
                min_tracking_confidence=float(cfg.get("minTrackingConfidence", 0.5)))

    # ---- 検出 ----------------------------------------------------------
    def _detect(self, frame) -> list[tuple[float, float]]:
        h, w = frame.shape[:2]
        res = self._hands.process(cv2.cvtColor(frame, cv2.COLOR_BGR2RGB))
        pts = []
        if res.multi_hand_landmarks:
            for hlm in res.multi_hand_landmarks:
                xs = [hlm.landmark[i].x * w for i in base.PALM_LANDMARKS]
                ys = [hlm.landmark[i].y * h for i in base.PALM_LANDMARKS]
                pts.append((sum(xs) / len(xs), sum(ys) / len(ys)))
        return pts

    # ---- スロットへの割り当て（handedness は使わない） -------------------
    def _assign(self, points: list[tuple[float, float]], t: float) -> None:
        free = list(points)
        for i in (0, 1):
            if self._slots[i] is None or not free:
                continue
            p = min(free, key=lambda q: math.hypot(q[0] - self._slots[i][0],
                                                   q[1] - self._slots[i][1]))
            if math.hypot(p[0] - self._slots[i][0], p[1] - self._slots[i][1]) <= self.gate_px:
                self._slots[i], self._slot_t[i] = p, t
                free.remove(p)
        for i in (0, 1):                       # 空きスロットを埋める
            if self._slots[i] is None and free:
                self._slots[i], self._slot_t[i] = free.pop(0), t
        for i in (0, 1):                       # hold を過ぎた古い位置は捨てる
            if self._slots[i] is not None and t - self._slot_t[i] > self.hold:
                self._slots[i] = None

    # ---- 凍結（＝直前の角速度を維持） -----------------------------------
    def _frozen(self, t: float, why: str):
        self._state = why
        if self._last_rpm is not None and t - self._last_good <= self.hold:
            return self._last_rpm, [f"{why}: 角度を凍結し直前の角速度を維持 "
                                    f"({self.hold - (t - self._last_good):4.2f}s 残り)"]
        return None, [f"{why}: 計測不能（ゲートのホールドに委ねる）"]

    # ---- Detector 契約 --------------------------------------------------
    def update(self, frame, hint, t: float):
        self._points = hint if hint is not None else self._detect(frame)
        self._assign(self._points, t)
        a, b = self._slots

        if a is None or b is None:
            return self._frozen(t, "hand lost")           # ■ 一瞬の枠外
        sep = math.hypot(b[0] - a[0], b[1] - a[1])
        if sep < self.min_sep:
            return self._frozen(t, f"overlap sep={sep:4.0f}px")   # ■ 手の重なり

        # --- ここからは旧ロジックそのもの：2 手のなす角の、一定フレーム内の変化 ---
        ang = math.degrees(math.atan2(b[1] - a[1], b[0] - a[0]))
        dt = max(1e-3, t - (self._hist[-1][0] if self._hist else t - 1e-3))
        if self._last_raw is None:
            self._cum = 0.0
        else:
            expected = self._last_dps * dt        # 予測付き unwrap（復帰時の折り返し対策）
            self._cum += expected + common.wrap180(ang - self._last_raw - expected)
        self._last_raw = ang
        self._hist.append((t, self._cum))

        if len(self._hist) < self.frames + 1:
            self._state = "filling"
            return None, [f"filling {len(self._hist)}/{self.frames + 1} frames"]

        span = self._hist[-1][0] - self._hist[0][0]
        if span <= 1e-3:
            return None, ["dt too small"]
        dps = (self._hist[-1][1] - self._hist[0][1]) / span   # 一定フレーム内の角度変化
        self._last_dps = dps
        rpm = abs(dps) / 6.0
        self._last_rpm, self._last_good = rpm, t
        self._state = "tracking"
        return rpm, [f"hands={len(self._points)} sep={sep:4.0f}px "
                     f"dir={'cw' if dps > 0 else 'ccw'}",
                     f"angle change {dps * span:+6.1f}deg / {self.frames} frames ({span * 1000:3.0f}ms)"]

    def draw(self, frame) -> None:
        a, b = self._slots
        for i, p in enumerate(self._slots):
            if p is None:
                continue
            col = (0, 200, 255) if i == 0 else (255, 160, 0)
            cv2.circle(frame, (int(p[0]), int(p[1])), 9, col, -1)
            cv2.putText(frame, "center" if i == 0 else "orbit",
                        (int(p[0]) + 12, int(p[1]) - 8), cv2.FONT_HERSHEY_SIMPLEX, 0.45, col, 1)
        if a is not None and b is not None:
            frozen = self._state.startswith(("hand lost", "overlap"))
            cv2.line(frame, (int(a[0]), int(a[1])), (int(b[0]), int(b[1])),
                     (0, 0, 255) if frozen else (0, 255, 120), 2)

    def close(self) -> None:
        if self._hands is not None:
            self._hands.close()


def main() -> None:
    ap = argparse.ArgumentParser(description="F: legacy pair-angle, hardened")
    common.add_common_args(ap)
    ap.add_argument("--frames", type=int, default=DEFAULT_FRAMES,
                    help="角度変化を見るフレーム数（旧ロジックの『一定フレーム』）")
    ap.add_argument("--hold", type=float, default=DEFAULT_HOLD_SEC,
                    help="欠落・重なりで角度を凍結したまま直前の角速度を維持する秒数")
    ap.add_argument("--min-sep", type=float, default=DEFAULT_MIN_SEP_PX,
                    help="これより 2 手が近づいたら重なりとみなす[px]")
    ap.add_argument("--gate-px", type=float, default=DEFAULT_GATE_PX,
                    help="スロットへの最近傍割り当ての最大距離[px]")
    args = ap.parse_args()

    cfg = common.load_config()
    common.run(PairAngleDetector(cfg, args), args, cfg)


if __name__ == "__main__":
    main()
