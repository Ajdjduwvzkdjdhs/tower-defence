"""RPLIDAR S1 の疎通確認（Kakigori TD / lidar-tracker3）。

`valley_tracker.py` を動かす前に、これで「機体と話せているか」「どのスキャンモードが
使えるか」「点がどう出るか」を確かめる。lidar.py は公式 SDK ではなく自前のプロトコル
実装なので、現物で最初に当たるのはここになる。

出すもの:
  1. 機種情報（model / firmware / serial）と自己診断
  2. スキャンモード一覧（id・名前・サンプリング周期・最大距離・応答種別）と推奨モード
  3. 指定回数ぶんの実スキャン（1 回転の点数・距離のレンジ・quality の分布）

3 が肝心で、`config.toml` の [ice] intensity_max と [detection] range_*_cm を
当てるための実測値がここに出る。T-mini Plus（0〜255）と S1（0〜63）で quality の
レンジが違うので、lidar-tracker の値を写してはいけない。

実行:
  uv run python probe.py                       # config.toml の 1 台目・設定どおりのモード
  uv run python probe.py --mode express        # express（dense capsule）で試す
  uv run python probe.py --port /dev/tty.usbserial-0001 --revs 30
"""

import argparse
import itertools
import sys

import lidar as lidar_module
from config import SCAN_MODES, load_scan_config, load_serial_config
from lidar import RPLidarS1

HEALTH_LABELS = {
    lidar_module.HEALTH_GOOD: "good",
    lidar_module.HEALTH_WARNING: "warning",
    lidar_module.HEALTH_ERROR: "error",
}

# scan_modes() が返す応答種別のうち、このドライバが読めるもの
READABLE_ANS_TYPES = {
    lidar_module.ANS_TYPE_MEASUREMENT: "standard（5 バイト/点・quality あり）",
    lidar_module.ANS_TYPE_MEASUREMENT_DENSE_CAPSULED: "dense capsule（express・quality なし）",
}

# 読めない応答種別。名前だけは出して、何が起きているか分かるようにする
UNSUPPORTED_ANS_TYPES = {
    lidar_module.ANS_TYPE_MEASUREMENT_CAPSULED: "legacy capsule",
    lidar_module.ANS_TYPE_MEASUREMENT_CAPSULED_ULTRA: "ultra capsule",
}

# quality の分布を出すときの区切り。standard の quality は 6bit（0〜63）
QUALITY_BUCKETS = (0, 1, 8, 16, 24, 32, 40, 48, 56, 64)


def print_device(device):
    """機種情報と自己診断を出す。"""
    info = device.info()
    health = device.health()
    label = HEALTH_LABELS.get(health["status"], f"unknown({health['status']})")

    print("--- device ---")
    print(f"  model     : 0x{info['model']:02X}")
    print(f"  firmware  : {info['firmware']}")
    print(f"  hardware  : {info['hardware']}")
    print(f"  serial    : {info['serial']}")
    print(f"  health    : {label} (error_code={health['error_code']})")


def print_scan_modes(device, requested_mode):
    """スキャンモード一覧と推奨モードを出す。

    express を使うときの `[scan] mode_id` はここに出る id から選ぶ。
    """
    print("--- scan modes ---")

    try:
        modes = device.scan_modes()
        typical = device.typical_scan_mode_id()
    except RuntimeError as e:
        # GET_LIDAR_CONF に応じないファームでも standard スキャンは動くことがある
        print(f"  取得できませんでした: {e}")
        return

    for mode in modes:
        readable = READABLE_ANS_TYPES.get(mode["ans_type"])
        if readable is None:
            name = UNSUPPORTED_ANS_TYPES.get(mode["ans_type"], "不明な形式")
            readable = f"**{name}・このドライバでは読めない**"
        mark = " <- typical" if mode["id"] == typical else ""

        print(
            f"  [{mode['id']}] {mode['name']:<16} "
            f"{mode['us_per_sample']:6.2f} us/sample  "
            f"max {mode['max_distance_m']:5.1f} m  "
            f"ans=0x{mode['ans_type']:02X} {readable}{mark}"
        )

    if requested_mode == lidar_module.SCAN_MODE_EXPRESS:
        print(f"  → express は typical の id {typical} を使う（[scan] mode_id で上書き可）")


def print_scans(device, revolutions):
    """実スキャンを回して、点の出方と quality の分布を出す。"""
    print(f"--- scan ({revolutions} rev) ---")

    distances = []
    qualities = []
    empty = 0

    for i in range(revolutions):
        samples = device.read_scan()

        if not samples:
            empty += 1
            print(f"  rev {i + 1:3d}: (1 回転が揃わずタイムアウト)")
            continue

        # 距離 0 は無反射・レンジ外としてドライバが落とした点
        valid = [s for s in samples if s["distance"] > 0]

        distances.extend(s["distance"] for s in valid)
        qualities.extend(s["intensity"] for s in valid)

        if valid:
            near = min(s["distance"] for s in valid)
            far = max(s["distance"] for s in valid)
            span = f"{near / 10:6.1f} - {far / 10:6.1f} cm"
        else:
            span = "有効な点なし"

        print(f"  rev {i + 1:3d}: {len(samples):4d} pts ({len(valid):4d} valid)  {span}")

    print("--- summary ---")
    print(f"  タイムアウト  : {empty} / {revolutions} rev")

    if not distances:
        print("  有効な点が 1 つも取れませんでした。")
        print("  ボーレート（S1 は 256000）・補助電源・[detection] の範囲を確認してください。")
        return

    print(
        f"  距離          : {min(distances) / 10:.1f} - {max(distances) / 10:.1f} cm"
        f"（[detection] range_min_cm / range_max_cm の目安）"
    )
    print_quality_histogram(qualities)


def print_quality_histogram(qualities):
    """quality の分布を出す。[ice] intensity_max を当てるための実測値。"""
    if max(qualities) == 0:
        print("  quality       : すべて 0（express モードは quality を持たない）")
        print("                  → [ice] intensity_max は効かない。関心円だけで絞ること")
        return

    print(f"  quality       : {min(qualities)} - {max(qualities)}")

    width = max(len(qualities) // 40, 1)

    for low, high in itertools.pairwise(QUALITY_BUCKETS):
        count = sum(1 for q in qualities if low <= q < high)
        share = count / len(qualities) * 100

        print(f"    {low:3d}-{high - 1:3d} : {count:6d} ({share:5.1f}%) "
              + "#" * (count // width))

    print("                  → 氷だけが入る上限を [ice] intensity_max に入れる")


def main():
    ports, baudrate = load_serial_config()
    scan = load_scan_config()

    parser = argparse.ArgumentParser(description="RPLIDAR S1 の疎通確認")
    parser.add_argument(
        "--port",
        default=ports[0],
        help=f"シリアルポート（既定: config.toml の 1 台目 = {ports[0]}）",
    )
    parser.add_argument(
        "--baudrate", type=int, default=baudrate,
        help=f"ボーレート（既定: config.toml = {baudrate}。S1 は 256000）",
    )
    parser.add_argument(
        "--mode", choices=SCAN_MODES, default=scan["mode"],
        help=f"スキャンモード（既定: config.toml = {scan['mode']}）",
    )
    parser.add_argument(
        "--revs", type=int, default=10, help="読む回転数（既定: 10）",
    )
    args = parser.parse_args()

    device = RPLidarS1(
        args.port,
        args.baudrate,
        scan_mode=args.mode,
        scan_frequency_hz=scan["frequency_hz"],
        mode_id=scan["mode_id"],
    )

    print(f"port {args.port} @ {args.baudrate} bps, mode = {args.mode}")

    try:
        # まずスキャンせずに開く。ここまで通れば配線とボーレートは合っている
        device.open()
        print_device(device)
        print_scan_modes(device, args.mode)

        device.start_scan()
        print_scans(device, args.revs)
    except (RuntimeError, ValueError) as e:
        sys.exit(f"\n失敗しました: {e}")
    except KeyboardInterrupt:
        print("\nInterrupted")
    finally:
        device.stop_scan()
        device.close()


if __name__ == "__main__":
    main()
