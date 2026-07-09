"""かき氷機の位置検知 -> 谷チャンネル（Kakigori TD / Phase 5）。

MediaPipe で手首中点を追い、マウス調整した四角形(quad)内で射影変換した
正規化座標 (x,y)∈[0,1] を、放射4セクター(0:N 1:W 2:S 3:E / 不感帯・未検出=-1)へ
**Python 側で**判定する（分類の責務は Python・docs/phase5.md §3-①）。

結果は共有テキストファイルへ 1 行 `seq,channel` で書く。書き込みは temp へ書き切って
os.replace() で原子的 rename（partial read を排除・§3-③, §8）。seq は単調増加し、
Unity は seq の停止で「ロスト」を検知する（§3-⑥）。手未検出・quad 外でも -1 を
書き続ける（seq を進める）ため、Unity は「正常な谷なし(-1)」と「プロセス停止(seq据置)」を
区別できる。

パス解決順（Python・Unity 共通・§8）:
  1. 環境変数 KAKIGORI_POSITION_FILE（通常未設定・手動運用の上書き保険）
  2. config.json の "positionFile"（配布物同梱の単一真実）
  3. スクリプト相対 out/position.txt（Editor 開発の既定）

実行:
  uv run python tracker.py           # カメラ + MediaPipe（本番）
  uv run python tracker.py --sim     # カメラ無し・合成座標で契約を検証
"""

from __future__ import annotations

import argparse
import json
import math
import os
import sys
import time
from pathlib import Path

SCRIPT_DIR = Path(__file__).resolve().parent
DEFAULT_REL_OUT = SCRIPT_DIR / "out" / "position.txt"

# 中心不感帯の半径（正規化空間・[0,1] の中心 0.5 からの距離）。config.json で上書き可。
DEFAULT_DEADZONE = 0.15
WRITE_INTERVAL_SEC = 0.05  # ~50ms（§1 要件）


# ---- パス解決 --------------------------------------------------------------

def load_config() -> dict:
    """config.json をスクリプト隣→cwd の順で探す。無ければ空 dict。"""
    for base in (SCRIPT_DIR, Path.cwd()):
        p = base / "config.json"
        if p.is_file():
            try:
                return json.loads(p.read_text(encoding="utf-8"))
            except (ValueError, OSError) as e:
                print(f"[tracker] config.json 読込失敗 ({p}): {e}", file=sys.stderr)
    return {}


def resolve_position_file(cfg: dict) -> Path:
    env = os.environ.get("KAKIGORI_POSITION_FILE")
    if env:
        return Path(env)
    cfg_path = cfg.get("positionFile")
    if cfg_path:
        return Path(cfg_path)
    return DEFAULT_REL_OUT


# ---- セクター判定 -----------------------------------------------------------

def classify(nx: float, ny: float, deadzone: float) -> int:
    """正規化 (x,y) を放射4セクターへ。0:N 1:W 2:S 3:E、不感帯=-1。

    正規化空間は (0,0)=左上 / (1,1)=右下（get_normalized_coords の dst と一致）。
    画像 y は下方向が増加するので N(上)=dy<0。中心からの距離が deadzone 未満なら -1。
    セクターは中心からの対角線で4分割（|dy|>=|dx| なら上下、else 左右）。
    """
    dx = nx - 0.5
    dy = ny - 0.5
    if math.hypot(dx, dy) < deadzone:
        return -1
    if abs(dy) >= abs(dx):
        return 0 if dy < 0 else 2   # N / S
    return 1 if dx < 0 else 3       # W / E


# ---- 原子的書き込み ---------------------------------------------------------

class PositionWriter:
    def __init__(self, path: Path):
        self._path = path
        self._tmp = path.with_suffix(path.suffix + ".tmp")
        self._seq = 0
        path.parent.mkdir(parents=True, exist_ok=True)

    def write(self, channel: int) -> None:
        """`seq,channel` を temp へ書き切って原子的 rename（§3-③）。"""
        self._seq += 1
        self._tmp.write_text(f"{self._seq},{channel}\n", encoding="utf-8")
        os.replace(self._tmp, self._path)


# ---- 実行モード -------------------------------------------------------------

def run_sim(writer: PositionWriter, deadzone: float) -> None:
    """カメラ無し。合成点を円運動させ、契約(seq単調増加・channel 0-3/-1)を検証する。"""
    print(f"[tracker] --sim: writing {writer._path}  (Ctrl-C to stop)")
    t0 = time.time()
    while True:
        t = time.time() - t0
        # 円周を巡り、時々中心(不感帯=-1)を通す合成軌道。
        r = 0.35 * abs(math.sin(t * 0.5))       # 0..0.35（中心付近で不感帯に入る）
        ang = t * 1.2
        nx = 0.5 + r * math.cos(ang)
        ny = 0.5 + r * math.sin(ang)
        ch = classify(nx, ny, deadzone)
        writer.write(ch)
        print(f"seq={writer._seq} ch={ch} (nx={nx:.2f} ny={ny:.2f})", end="\r")
        time.sleep(WRITE_INTERVAL_SEC)


def run_camera(writer: PositionWriter, deadzone: float) -> None:
    """MediaPipe + カメラ（本番）。quad はマウスでドラッグ調整。"""
    import cv2
    import mediapipe as mp
    import numpy as np

    pts = np.array([[200, 150], [440, 150], [440, 330], [200, 330]], np.int32)
    state = {"drag": -1}
    CLICK_THRESHOLD = 20

    def on_mouse(event, x, y, flags, param):
        if event == cv2.EVENT_LBUTTONDOWN:
            for i, pt in enumerate(pts):
                if math.hypot(x - pt[0], y - pt[1]) < CLICK_THRESHOLD:
                    state["drag"] = i
                    break
        elif event == cv2.EVENT_MOUSEMOVE and state["drag"] != -1:
            pts[state["drag"]] = [x, y]
        elif event == cv2.EVENT_LBUTTONUP:
            state["drag"] = -1

    def normalized(tx, ty):
        src = pts.astype(np.float32)
        dst = np.array([[0, 0], [1, 0], [1, 1], [0, 1]], np.float32)
        m = cv2.getPerspectiveTransform(src, dst)
        p = cv2.perspectiveTransform(np.array([[[tx, ty]]], np.float32), m)
        return float(p[0][0][0]), float(p[0][0][1])

    mp_hands = mp.solutions.hands
    hands = mp_hands.Hands(static_image_mode=False, max_num_hands=2,
                           min_detection_confidence=0.7, min_tracking_confidence=0.7)
    mp_draw = mp.solutions.drawing_utils
    mp_styles = mp.solutions.drawing_styles

    cap = cv2.VideoCapture(0)
    cv2.namedWindow("dst_img")
    cv2.setMouseCallback("dst_img", on_mouse)
    print(f"[tracker] camera: writing {writer._path}  (ESC to stop)")

    last_write = 0.0
    while cap.isOpened():
        ret, frame = cap.read()
        if not ret:
            print("[tracker] カメラが開けません", file=sys.stderr)
            break
        h, w, _ = frame.shape
        frame = cv2.flip(frame, 1)

        cv2.polylines(frame, [pts], True, (255, 255, 0), 2)
        for i, pt in enumerate(pts):
            cv2.circle(frame, tuple(pt), 8,
                       (0, 0, 255) if i == state["drag"] else (0, 255, 0), -1)

        results = hands.process(cv2.cvtColor(frame, cv2.COLOR_BGR2RGB))
        wrists = []
        if results.multi_hand_landmarks:
            for hlm in results.multi_hand_landmarks:
                mp_draw.draw_landmarks(frame, hlm, mp_hands.HAND_CONNECTIONS,
                                       mp_styles.get_default_hand_landmarks_style(),
                                       mp_styles.get_default_hand_connections_style())
                wr = hlm.landmark[mp_hands.HandLandmark.WRIST]
                wrists.append((int(wr.x * w), int(wr.y * h)))

        # 手の中点（2手なら中点・1手ならその手・0手なら None）。
        mid = None
        if len(wrists) == 2:
            mid = ((wrists[0][0] + wrists[1][0]) // 2, (wrists[0][1] + wrists[1][1]) // 2)
        elif len(wrists) == 1:
            mid = wrists[0]

        # 谷判定：手あり かつ quad 内 → セクター、それ以外は -1。
        channel = -1
        if mid is not None:
            cv2.circle(frame, mid, 8, (0, 0, 255), -1)
            if cv2.pointPolygonTest(pts, (float(mid[0]), float(mid[1])), False) >= 0:
                nx, ny = normalized(mid[0], mid[1])
                channel = classify(nx, ny, deadzone)

        # ~50ms 間隔で常に書く（channel が -1 でも seq を進める＝ロストと区別）。
        now = time.time()
        if now - last_write >= WRITE_INTERVAL_SEC:
            writer.write(channel)
            last_write = now

        cv2.putText(frame, f"ch={channel} seq={writer._seq}", (10, 30),
                    cv2.FONT_HERSHEY_SIMPLEX, 0.7, (0, 0, 255), 2)
        cv2.imshow("dst_img", frame)
        if cv2.waitKey(1) & 0xFF == 27:  # ESC
            break

    cap.release()
    cv2.destroyAllWindows()
    hands.close()


def main() -> None:
    ap = argparse.ArgumentParser(description="Kakigori TD hand-position tracker")
    ap.add_argument("--sim", action="store_true",
                    help="カメラ無し。合成座標で書き込み契約を検証する")
    args = ap.parse_args()

    cfg = load_config()
    out = resolve_position_file(cfg)
    deadzone = float(cfg.get("deadzone", DEFAULT_DEADZONE))
    writer = PositionWriter(out)

    try:
        if args.sim:
            run_sim(writer, deadzone)
        else:
            run_camera(writer, deadzone)
    except KeyboardInterrupt:
        print("\n[tracker] stopped")


if __name__ == "__main__":
    main()
