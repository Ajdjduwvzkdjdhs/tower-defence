"""LiDAR の検出値をリアルタイムに極座標散布図へ描画する。

全周を 360 個の角度ビンに分け、各ビンを「最新の距離」で更新し続ける。
物体がある方向のビンには距離が入り、物体が消えた（無反射／ノイズの）方向の
ビンは NaN でクリアされ点が消える。常に「直近 1 回転ぶんの最新点」が見える。

構成（2 スレッド）:
  - 読み取りスレッド : read_packet() を回し、共有の距離配列を更新し続ける
  - メインスレッド    : FuncAnimation が一定間隔で配列を読み、散布図を再描画

接続情報は config.toml（config.load_serial_config）から読み込む。
描画パラメータは下の「設定」定数で調整する。
"""

import threading

import matplotlib.animation as animation
import matplotlib.pyplot as plt
import numpy as np

from config import load_serial_config
from lidar import TMiniPlus

#=============================
# 設定
#=============================

# シリアルポート・ボーレートは config.toml から読み込む
PORT, BAUDRATE = load_serial_config()

RADIUS_CM = 80                     # 関心円（検出対象）の半径。ここを書き換える
RADIUS_MM = RADIUS_CM * 10

# 表示範囲 rmax = RADIUS × 倍率。円外のコンテキストも少し見えるようにする。
# 大きくすると関心円が相対的に小さく、小さくすると円が画面いっぱいに寄る。
DISPLAY_SCALE = 1.2
DISPLAY_MAX_MM = RADIUS_MM * DISPLAY_SCALE

# 現物合わせ用の角度オフセット[度]。センサー正面を画面上のどこに向けるか。
# 0 度＝画面上・時計回りを基準に、この値だけ表示全体を回転させる。
ANGLE_OFFSET_DEG = 0.0

# 角度ビンの解像度[度]。1.0° → 360 ビン。
BIN_RESOLUTION_DEG = 1.0
NUM_BINS = int(round(360.0 / BIN_RESOLUTION_DEG))

# 描画の更新間隔[ms]（読み取りとは独立）
UPDATE_INTERVAL_MS = 50

#--- フィルタの ON/OFF（現物合わせで切り替える）-------------------------
FILTER_ZERO_DISTANCE = True        # 距離 0（無反射）を除外
FILTER_AMBIENT = True              # 外乱光（flag == 3）を除外
FILTER_SPECULAR = False            # 鏡面反射（flag == 2）を除外（既定は残す）


class ScanBuffer:
    """全周 360 ビンの最新距離を保持する共有バッファ（スレッド間で共有）。

    値は距離[mm]。無反射／ノイズ／未計測のビンは NaN。
    """

    def __init__(self, num_bins):
        self._lock = threading.Lock()
        self._distances = np.full(num_bins, np.nan, dtype=float)
        self._num_bins = num_bins

    def update(self, samples):
        """1 パケットぶんの samples でビンを更新する。

        有効な点はその距離で上書きし、無効な点（無反射・ノイズ）は
        そのビンを NaN でクリアする。こうすることで、ビームがその方向を
        通過したのに物体が無ければ、残っていた古い点が消える。
        """
        with self._lock:
            for p in samples:
                idx = int(p["angle"] // BIN_RESOLUTION_DEG) % self._num_bins

                if _is_valid(p):
                    self._distances[idx] = p["distance"]
                else:
                    self._distances[idx] = np.nan

    def snapshot(self):
        with self._lock:
            return self._distances.copy()


def _is_valid(p):
    """フィルタ設定に照らして、その点を「有効な物体点」とみなすか。"""
    if FILTER_ZERO_DISTANCE and p["distance"] == 0:
        return False
    if FILTER_AMBIENT and p["flag"] == 3:
        return False
    if FILTER_SPECULAR and p["flag"] == 2:
        return False
    return True


def reader_loop(lidar, buffer, stop_event):
    """読み取りスレッド本体。停止フラグが立つまでパケットを読み続ける。"""
    while not stop_event.is_set():
        try:
            samples = lidar.read_packet()
        except Exception as e:
            if not stop_event.is_set():
                print(f"read_packet error: {e}")
            break
        buffer.update(samples)


def main():

    lidar = TMiniPlus(PORT, BAUDRATE)
    lidar.start_scan()

    buffer = ScanBuffer(NUM_BINS)
    stop_event = threading.Event()

    reader = threading.Thread(
        target=reader_loop,
        args=(lidar, buffer, stop_event),
        daemon=True,
    )
    reader.start()

    # --- 描画の準備 ---
    fig = plt.figure()
    ax = fig.add_subplot(projection="polar")
    ax.set_theta_zero_location("N")    # 0 度を画面上（真上）に
    ax.set_theta_direction(-1)         # 時計回り
    ax.set_rlabel_position(135)
    ax.set_title(f"LiDAR realtime  (radius = {RADIUS_CM} cm)")

    # 各ビンの中心角[ラジアン]（オフセットで表示全体を回転）。以後固定。
    bin_centers_deg = (np.arange(NUM_BINS) + 0.5) * BIN_RESOLUTION_DEG
    theta = np.radians(bin_centers_deg + ANGLE_OFFSET_DEG)

    # 物体点（散布図）。r は毎フレーム差し替える。NaN のビンは描画されない。
    scat = ax.scatter(theta, np.full(NUM_BINS, np.nan), s=8, c="tab:blue")

    # 関心円（目安線）を重ねる
    circle_theta = np.linspace(0, 2 * np.pi, 361)
    ax.plot(
        circle_theta,
        np.full_like(circle_theta, RADIUS_MM),
        color="tab:red",
        linewidth=1.0,
        alpha=0.7,
    )

    # 全アーティストを追加した後に表示範囲を固定する。
    # 先に set_rmax しても、後から plot するとオートスケールが rmax を
    # 描画データ（赤円 = RADIUS）に合わせ直してしまい DISPLAY_SCALE が効かない。
    ax.set_rlim(0, DISPLAY_MAX_MM)
    ax.autoscale(False)

    def update(_frame):
        distances = buffer.snapshot()
        offsets = np.column_stack((theta, distances))
        scat.set_offsets(offsets)
        return (scat,)

    ani = animation.FuncAnimation(
        fig,
        update,
        interval=UPDATE_INTERVAL_MS,
        blit=False,
        cache_frame_data=False,
    )

    def shutdown():
        # 二重呼び出しに備えてフラグで一度きりに
        if stop_event.is_set():
            return
        stop_event.set()
        reader.join(timeout=2.0)
        lidar.stop_scan()
        lidar.close()

    # ウィンドウを閉じたら安全終了
    fig.canvas.mpl_connect("close_event", lambda _e: shutdown())

    try:
        plt.show()
    except KeyboardInterrupt:
        print()
        print("Interrupted")
    finally:
        # Ctrl+C・ウィンドウを閉じる、どちらでも必ず停止処理を通す
        shutdown()


if __name__ == "__main__":
    main()
