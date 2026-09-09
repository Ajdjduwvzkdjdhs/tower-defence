"""パターンA — MediaPipe Hands ＋ 幾何（手ごとに独立トラックして角速度を測る）。

    使用ライブラリ: mediapipe + opencv-python (+ numpy)
    考え方        : 手のひら中心の軌跡に円を当てて**その手だけの回転中心**を求め、
                    中心まわりの角度を unwrap して最小二乗回帰で角速度[deg/s] を出す。

旧ロジック（片手を中心に、もう片方の手とのなす角を見る）との違いはここ:

  - **中心を「もう片方の手」に置かない**。中心は片手の軌跡への円フィット（Kåsa）で
    自前に推定する。したがって *片手が見えていれば判定が成立する*。
    → 「両手が重なると壊れる」「片手が枠外に出ると消える」が原理的に起きない。
  - **手を 1 本ずつ独立にトラックし、いちばん速く回っている手を採用する**。
    実機では片手が機体を押さえ、もう片手がハンドルを回す。止まっている手は
    角速度 ~0 になって自動的に無視される（両手が必要という前提を捨てる）。
  - **角度の unwrap に予測を入れる**（common.AngularVelocity）。検出が数フレーム
    抜けても、前回の角速度で予測した角度のまわりで折り返すので角度が連続する。
  - 見失いは「rpm を出さない（None）」として上げるだけで、ON/OFF の粘りは
    `ShavingGate` のホールドに一元化してある。
  - **対象は円 ROI ではなくカメラ映像の全体**。画面のどこでハンドルを回していても拾う
    （マウスでの ROI 合わせが要らない＝現地でのキャリブレーション工程が無い）。
    回転中心は手の軌跡への円フィットで手ごとに決まるので、ROI で位置を教える必要が無い。

実行:
    uv run python rotate_landmark.py                 # カメラ
    uv run python rotate_landmark.py --sim           # カメラ無し（見失い・重なりの台本）
    uv run python rotate_landmark.py --sim --headless --duration 20   # 自動採点
"""

from __future__ import annotations

import argparse
import math

import cv2

import common

# MediaPipe の手のひら中心に使うランドマーク（手首＋各指の付け根）。
# 手首 1 点より重心の方が、握って回すときの見かけの揺れが小さい。
PALM_LANDMARKS = (0, 5, 9, 13, 17)

DEFAULT_WINDOW_SEC = 0.40   # 角速度の回帰ウィンドウ
DEFAULT_COAST_SEC = 0.50    # この秒数まで未更新のトラックを生かす（＝見失いを跨ぐ）
# トラックに検出点を紐付ける最大距離[px]。映像全体が対象なので絶対値で持つ。
# 目安: 60RPM・軌道半径 100px なら 30fps で約 22px/frame しか動かない。
DEFAULT_GATE_PX = 100.0
DEFAULT_MAX_HANDS = 2       # MediaPipe が同時に追う手の数（全画面なので通行人が入りうる）
MAX_TRACKS = 4


class HandTrack:
    """1 本の手の軌跡。自分専用の回転中心と角速度推定を持つ。"""

    _next_id = 1

    def __init__(self, pt, t: float, roi: common.Roi, window_sec: float):
        # roi.center は全画面 ROI では画面中心。円フィットが決まるまでの暫定中心にだけ使う。
        self.id = HandTrack._next_id
        HandTrack._next_id += 1
        self.pt = pt
        self.last_t = t
        self.updated = True
        self.center = common.CrankCenter(roi.center)
        self.vel = common.AngularVelocity(window_sec)
        self._last_dps = 0.0
        self.center.push(pt, t)
        self.vel.push(self._angle(pt), t)

    def _angle(self, pt) -> float:
        cx, cy = self.center.center
        return math.degrees(math.atan2(pt[1] - cy, pt[0] - cx))

    def update(self, pt, t: float) -> None:
        dt = max(1e-3, t - self.last_t)
        # 前回の角速度から「このぶんは回ったはず」を予測して unwrap に渡す。
        expected = self._last_dps * dt
        self.center.push(pt, t)
        self.vel.push(self._angle(pt), t, expected_delta_deg=expected)
        dps = self.vel.deg_per_sec
        if dps is not None:
            self._last_dps = dps
        self.pt = pt
        self.last_t = t
        self.updated = True

    @property
    def rpm(self) -> float | None:
        """このフレームで更新されたトラックだけが速度を名乗る（粘りはゲート側の責務）。"""
        if not self.updated:
            return None
        return common.deg_per_sec_to_rpm(self.vel.deg_per_sec)


class LandmarkCrankDetector:
    name = "A: mediapipe + geometry (full frame)"
    uses_roi = False   # common.run が円 ROI ではなく「映像全体」を渡す
    TRACK_CLASS = HandTrack   # 派生（rotate_landmark_strict.py）が差し替える

    def __init__(self, cfg: dict, sim: bool, window_sec: float, coast_sec: float,
                 gate_px: float = DEFAULT_GATE_PX, max_hands: int = DEFAULT_MAX_HANDS):
        self.roi: common.Roi | None = None
        self.window_sec = window_sec
        self.coast_sec = coast_sec
        self.gate_px = float(gate_px)
        self._tracks: list[HandTrack] = []
        self._best: HandTrack | None = None
        self._hands = None
        if not sim:   # --sim では検出器を迂回して回転判定だけを検証する
            import mediapipe as mp
            self._mp = mp
            self._hands = mp.solutions.hands.Hands(
                static_image_mode=False,
                max_num_hands=int(max_hands),
                model_complexity=0,   # 俯瞰の小さな手では軽い方が取りこぼしにくい
                min_detection_confidence=float(cfg.get("minDetectionConfidence", 0.5)),
                min_tracking_confidence=float(cfg.get("minTrackingConfidence", 0.5)),
            )

    # ---- 検出 ----------------------------------------------------------
    def _detect(self, frame) -> list[tuple[float, float]]:
        h, w = frame.shape[:2]
        res = self._hands.process(cv2.cvtColor(frame, cv2.COLOR_BGR2RGB))
        pts: list[tuple[float, float]] = []
        if res.multi_hand_landmarks:
            for hlm in res.multi_hand_landmarks:
                xs = [hlm.landmark[i].x * w for i in PALM_LANDMARKS]
                ys = [hlm.landmark[i].y * h for i in PALM_LANDMARKS]
                pts.append((sum(xs) / len(xs), sum(ys) / len(ys)))
        return pts

    # ---- トラック対応付け（貪欲最近傍） --------------------------------
    def _associate(self, points: list[tuple[float, float]], t: float) -> None:
        gate = self.gate_px   # 映像全体が対象なので ROI 半径ではなく絶対距離で門を張る
        free = list(points)
        for tr in self._tracks:
            tr.updated = False
        # 直近に更新されたトラックから優先して相手を取る。
        for tr in sorted(self._tracks, key=lambda x: -x.last_t):
            if not free:
                break
            cand = min(free, key=lambda p: math.hypot(p[0] - tr.pt[0], p[1] - tr.pt[1]))
            if math.hypot(cand[0] - tr.pt[0], cand[1] - tr.pt[1]) <= gate:
                tr.update(cand, t)
                free.remove(cand)
        for p in free:   # 余った検出は新しい手として起こす
            if len(self._tracks) < MAX_TRACKS:
                self._tracks.append(self.TRACK_CLASS(p, t, self.roi, self.window_sec))
        # 見失って coast_sec を過ぎたトラックは捨てる（再取得は新規トラックで）。
        self._tracks = [tr for tr in self._tracks if t - tr.last_t <= self.coast_sec]

    # ---- Detector 契約 --------------------------------------------------
    def update(self, frame, hint, t: float):
        # 位置では絞らない（映像全体が対象）。回っていない手は角速度 ~0 で自然に落ちる。
        points = hint if hint is not None else self._detect(frame)
        self._associate(points, t)

        # いちばん速く回っている手を採用（機体を押さえている手は ~0 で落ちる）。
        self._best = None
        rpm: float | None = None
        for tr in self._tracks:
            r = tr.rpm
            if r is not None and (rpm is None or r > rpm):
                rpm, self._best = r, tr

        lines = [f"hands={len(points)} tracks={len(self._tracks)}"]
        if self._best is not None:
            c = self._best.center
            lines.append(f"track#{self._best.id} center={'fit' if c.fitted else 'roi'} "
                         f"r={(c.radius or 0):5.1f}px  dir={'cw' if self._best._last_dps > 0 else 'ccw'}")
        else:
            lines.append("no measurable rotation (holding)")
        return rpm, lines

    def draw(self, frame) -> None:
        for tr in self._tracks:
            live = tr.updated
            p = (int(tr.pt[0]), int(tr.pt[1]))
            cv2.circle(frame, p, 9, (0, 200, 255) if live else (90, 90, 90), -1 if live else 2)
            cv2.putText(frame, f"#{tr.id}", (p[0] + 12, p[1] - 8),
                        cv2.FONT_HERSHEY_SIMPLEX, 0.45, (0, 200, 255), 1)
            if tr is self._best and tr.center.fitted:
                c = tr.center.center
                ci = (int(c[0]), int(c[1]))
                cv2.circle(frame, ci, 5, (0, 0, 255), -1)
                if tr.center.radius:
                    cv2.circle(frame, ci, int(tr.center.radius), (0, 0, 255), 1)
                cv2.line(frame, ci, p, (0, 0, 255), 1)

    def close(self) -> None:
        if self._hands is not None:
            self._hands.close()


def main() -> None:
    ap = argparse.ArgumentParser(description="A: MediaPipe + per-hand angular velocity")
    common.add_common_args(ap)
    ap.add_argument("--window", type=float, default=DEFAULT_WINDOW_SEC,
                    help="角速度の回帰ウィンドウ[秒]（長いほど滑らか・反応は遅い）")
    ap.add_argument("--coast", type=float, default=DEFAULT_COAST_SEC,
                    help="見失ったトラックを生かしておく秒数")
    ap.add_argument("--gate-px", type=float, default=DEFAULT_GATE_PX,
                    help="トラックに検出点を紐付ける最大距離[px]（映像全体が対象なので絶対値）")
    ap.add_argument("--max-hands", type=int, default=DEFAULT_MAX_HANDS,
                    help="MediaPipe が同時に追う手の数")
    args = ap.parse_args()

    cfg = common.load_config()
    detector = LandmarkCrankDetector(cfg, sim=args.sim, window_sec=args.window,
                                     coast_sec=args.coast, gate_px=args.gate_px,
                                     max_hands=args.max_hands)
    common.run(detector, args, cfg)


if __name__ == "__main__":
    main()
