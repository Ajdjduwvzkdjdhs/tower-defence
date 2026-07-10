"""かき氷機の位置検知 -> 谷チャンネル（Kakigori TD / Phase 5）。

MediaPipe で手首中点を追い、マウス調整した四角形(quad)内で射影変換した
正規化座標 (x,y)∈[0,1] を、放射4セクター(0:N 1:W 2:S 3:E / 未検出・quad外=-1)へ
**Python 側で**判定する（分類の責務は Python・docs/phase5.md §3-①）。
quad 内なら常にいずれかのセクターに属し、-1 は「手なし or quad 外」だけを意味する。

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
  uv run python tracker.py           # カメラ + MediaPipe（本番。左右反転なし）
  uv run python tracker.py --flip    # 映像を左右反転（水平ミラー）する
  uv run python tracker.py --sim     # カメラ無し・合成座標で契約を検証
"""

from __future__ import annotations

import argparse
import json
import math
import os
import sys
import threading
import time
from pathlib import Path

SCRIPT_DIR = Path(__file__).resolve().parent
DEFAULT_REL_OUT = SCRIPT_DIR / "out" / "position.txt"

WRITE_INTERVAL_SEC = 0.05  # ~50ms（§1 要件）

# MediaPipe の検出/追跡しきい値。プロジェクター光の映り込みで手が白く光っても見失いにくい
# よう既定を下げてある（MediaPipe 素の既定 0.5・以前の 0.7 より緩い）。config.json で上書き可。
DEFAULT_MIN_DETECTION_CONFIDENCE = 0.5
DEFAULT_MIN_TRACKING_CONFIDENCE = 0.5


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

def classify(nx: float, ny: float) -> int:
    """正規化 (x,y) を放射4セクターへ。0:N 1:W 2:S 3:E。

    正規化空間は (0,0)=左上 / (1,1)=右下（get_normalized_coords の dst と一致）。
    画像 y は下方向が増加するので N(上)=dy<0。quad 内なら必ずいずれかに属す（中心不感帯なし）。
    セクターは中心からの対角線で4分割（|dy|>=|dx| なら上下、else 左右）。
    """
    dx = nx - 0.5
    dy = ny - 0.5
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

def run_sim(writer: PositionWriter) -> None:
    """カメラ無し。合成点を円運動させ、契約(seq単調増加・channel 0-3/-1)を検証する。"""
    print(f"[tracker] --sim: writing {writer._path}  (Ctrl-C to stop)")
    t0 = time.time()
    while True:
        t = time.time() - t0
        # 5秒周期の最後の1秒は「手なし(-1)」区間にして、ロスト以外の -1 契約も検証する。
        if int(t) % 5 == 4:
            ch = -1
        else:
            ang = t * 1.2
            nx = 0.5 + 0.35 * math.cos(ang)
            ny = 0.5 + 0.35 * math.sin(ang)
            ch = classify(nx, ny)
        writer.write(ch)
        print(f"seq={writer._seq} ch={ch}", end="\r")
        time.sleep(WRITE_INTERVAL_SEC)


def run_camera(writer: PositionWriter, cfg: dict, flip: bool = False) -> None:
    """MediaPipe + カメラ（本番）。quad はマウスでドラッグ調整。

    flip=True で映像を水平ミラーする（既定はミラーなし）。左右反転はチャンネル的に
    W↔E の入れ替えと等価で、Unity 側（GameManager Inspector）でも切替可能。

    cfg から MediaPipe の検出/追跡しきい値を読む。
    """
    import cv2
    import mediapipe as mp
    import numpy as np

    # 取り込み・表示の固定解像度（下のキャプチャ設定と共通の基準）。
    DISP_W, DISP_H = 640, 480
    # 検出枠(quad)の初期位置＝カメラ映像の全域（四隅）。マウスで角をドラッグして狭められる。
    pts = np.array([[0, 0], [DISP_W, 0],
                    [DISP_W, DISP_H], [0, DISP_H]], np.int32)
    state = {"drag": -1}

    def nearest_corner(x: int, y: int) -> int:
        """クリック点に最も近い角の index。掴み損ね(Retina 等の座標ずれ)を避けるため
        しきい値なしで常に最寄りを掴む（キャリブレーション用途なので誤爆は許容）。"""
        d = [math.hypot(x - px, y - py) for px, py in pts]
        return int(np.argmin(d))

    def on_mouse(event, x, y, flags, param):
        # macOS/COCOA では LBUTTONUP を取りこぼすことがある。MOUSEMOVE の flags で
        # 「実際にボタンが押されているか」を毎回確認し、押されていなければ強制解放する。
        held = bool(flags & cv2.EVENT_FLAG_LBUTTON)
        if event == cv2.EVENT_LBUTTONDOWN:
            state["drag"] = nearest_corner(x, y)
            pts[state["drag"]] = [x, y]
        elif event == cv2.EVENT_MOUSEMOVE:
            if state["drag"] != -1 and held:
                pts[state["drag"]] = [x, y]
            elif not held:
                state["drag"] = -1  # up 取りこぼしの保険
        elif event == cv2.EVENT_LBUTTONUP:
            if state["drag"] != -1:
                pts[state["drag"]] = [x, y]
            state["drag"] = -1

    UNIT = np.array([[0, 0], [1, 0], [1, 1], [0, 1]], np.float32)  # 正規化空間の quad 角

    def normalized(tx, ty):
        m = cv2.getPerspectiveTransform(pts.astype(np.float32), UNIT)
        p = cv2.perspectiveTransform(np.array([[[tx, ty]]], np.float32), m)
        return float(p[0][0][0]), float(p[0][0][1])

    def draw_sectors(frame) -> None:
        """0/1/2/3 の判定領域を quad 内に重畳表示。マウス調整に追従する。

        判定は正規化空間で「中心からの対角線で4分割」（classify と同じ・中心不感帯なし）。
        正規化空間の幾何を逆射影して画像に描くので、quad が歪んでいても領域と一致する。
        """
        m_inv = cv2.getPerspectiveTransform(UNIT, pts.astype(np.float32))

        def to_img(norm):
            a = np.asarray(norm, np.float32).reshape(-1, 1, 2)
            return cv2.perspectiveTransform(a, m_inv).reshape(-1, 2)

        # 対角線2本（対角の角どうし）で N/W/S/E の4三角形に分割＝境界 |dy|=|dx|。
        p = pts.reshape(-1, 2)
        cv2.line(frame, tuple(p[0]), tuple(p[2]), (200, 200, 200), 1)
        cv2.line(frame, tuple(p[1]), tuple(p[3]), (200, 200, 200), 1)

        # 各領域にラベル。
        labels = [(0.5, 0.19, "0 N"), (0.19, 0.5, "1 W"),
                  (0.5, 0.81, "2 S"), (0.81, 0.5, "3 E")]
        for (lx, ly), (_, _, txt) in zip(to_img([(a, b) for a, b, _ in labels]), labels):
            cv2.putText(frame, txt, (int(lx) - 14, int(ly) + 5),
                        cv2.FONT_HERSHEY_SIMPLEX, 0.6, (0, 255, 255), 2)

    min_det = float(cfg.get("minDetectionConfidence", DEFAULT_MIN_DETECTION_CONFIDENCE))
    min_trk = float(cfg.get("minTrackingConfidence", DEFAULT_MIN_TRACKING_CONFIDENCE))
    mp_hands = mp.solutions.hands
    hands = mp_hands.Hands(static_image_mode=False, max_num_hands=2,
                           min_detection_confidence=min_det, min_tracking_confidence=min_trk)
    mp_draw = mp.solutions.drawing_utils
    mp_styles = mp.solutions.drawing_styles

    cap = cv2.VideoCapture(0)
    # キャプチャ解像度を明示的に落とす。既定では 1920x1080 で開くカメラがあり、その場合
    # cvtColor/hands.process/imshow/描画が全て 1080p になって重くなる。DISP に合わせて要求する。
    cap.set(cv2.CAP_PROP_FRAME_WIDTH, DISP_W)
    cap.set(cv2.CAP_PROP_FRAME_HEIGHT, DISP_H)

    # --- カメラ取得 + MediaPipe をバックグラウンドスレッドへ ------------------
    # cap.read() はカメラFPS(~30)でブロックし、hands.process() も ~20-26ms/フレーム。
    # これらをメインループで直列に回すと、マウス位置がループ1回に1回しか反映されず
    # ドラッグがカクつく。取得+検出をワーカースレッドに逃がし、メイン(GUI/マウス)は
    # 「最新フレームを描いて imshow するだけ」の軽量ループにして高FPSでドラッグを滑らかにする。
    # OpenCV の GUI(imshow/waitKey/mouse) は必ずメインスレッドで動かす必要がある。
    shared: dict = {"frame": None, "results": None}
    lock = threading.Lock()
    stop = threading.Event()

    def capture_worker() -> None:
        while not stop.is_set():
            ret, f = cap.read()
            if not ret:
                print("[tracker] カメラが開けません", file=sys.stderr)
                stop.set()
                break
            # cap.set を無視するカメラ対策に、表示・検出とも固定サイズへ揃える（imshow を軽量化）。
            if f.shape[1] != DISP_W or f.shape[0] != DISP_H:
                f = cv2.resize(f, (DISP_W, DISP_H))
            if flip:
                f = cv2.flip(f, 1)  # --flip 指定時のみ水平ミラー
            res = hands.process(cv2.cvtColor(f, cv2.COLOR_BGR2RGB))
            with lock:
                shared["frame"] = f
                shared["results"] = res

    worker = threading.Thread(target=capture_worker, daemon=True)
    worker.start()

    cv2.namedWindow("dst_img")
    cv2.setMouseCallback("dst_img", on_mouse)
    print(f"[tracker] camera (threaded): writing {writer._path}  (ESC to stop)")

    last_write = 0.0
    while not stop.is_set():
        with lock:
            frame = None if shared["frame"] is None else shared["frame"].copy()
            results = shared["results"]
        if frame is None:  # まだ最初のフレームが来ていない
            if cv2.waitKey(10) & 0xFF == 27:
                break
            continue
        h, w, _ = frame.shape

        cv2.polylines(frame, [pts], True, (255, 255, 0), 2)
        draw_sectors(frame)  # 0/1/2/3 領域と不感帯(-1)を quad 内に可視化
        for i, pt in enumerate(pts):
            cv2.circle(frame, tuple(pt), 8,
                       (0, 0, 255) if i == state["drag"] else (0, 255, 0), -1)

        wrists = []
        if results is not None and results.multi_hand_landmarks:
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
                channel = classify(nx, ny)

        # ~50ms 間隔で常に書く（channel が -1 でも seq を進める＝ロストと区別）。
        now = time.time()
        if now - last_write >= WRITE_INTERVAL_SEC:
            writer.write(channel)
            last_write = now

        cv2.putText(frame, f"ch={channel} seq={writer._seq}", (10, 30),
                    cv2.FONT_HERSHEY_SIMPLEX, 0.7, (0, 0, 255), 2)
        cv2.putText(frame, "drag green corners to set area / ESC to quit", (10, h - 15),
                    cv2.FONT_HERSHEY_SIMPLEX, 0.5, (255, 255, 0), 1)
        cv2.imshow("dst_img", frame)
        if cv2.waitKey(1) & 0xFF == 27:  # ESC
            break

    stop.set()
    worker.join(timeout=1.0)
    cap.release()
    cv2.destroyAllWindows()
    hands.close()


def main() -> None:
    ap = argparse.ArgumentParser(description="Kakigori TD hand-position tracker")
    ap.add_argument("--sim", action="store_true",
                    help="カメラ無し。合成座標で書き込み契約を検証する")
    ap.add_argument("--flip", action="store_true",
                    help="映像を左右反転（水平ミラー）する。既定は反転なし")
    args = ap.parse_args()

    cfg = load_config()
    out = resolve_position_file(cfg)
    writer = PositionWriter(out)

    try:
        if args.sim:
            run_sim(writer)
        else:
            run_camera(writer, cfg, flip=args.flip)
    except KeyboardInterrupt:
        print("\n[tracker] stopped")


if __name__ == "__main__":
    main()
