"""`config.toml` / `preview.toml` の読み込み（Kakigori TD / lidar-tracker3）。

兄弟ツール `tools/lidar-tracker` の `config.py` と同じ流儀。RPLIDAR S1 に合わせて
変えたのは 4 点:

  - ボーレートの現物値が 256000（T-mini Plus は 230400）
  - 回転数の対応範囲が 8〜15Hz（T-mini Plus は 5〜12Hz）
  - `[scan] mode` / `[scan] mode_id` が増えた（standard / express の切り替え）
  - `[ice] intensity_max` の既定値が 6bit quality 向け（T-mini Plus は 8bit）

`[valleys]`（4 方向の谷）の読み込みは持っていない。連続座標モード（ADR-29）では
谷の分類をせず、`preview.toml` の関心円だけを使うため。
"""

import os
import sys
import tomllib
from pathlib import Path

# config.py 自身の場所を基準にするため、実行ディレクトリに依存しない
_CONFIG_PATH = Path(__file__).parent / "config.toml"
_PREVIEW_PATH = Path(__file__).parent / "preview.toml"

# 検出範囲の既定値[cm]。config.toml に [detection] が無いときに使う。
# セクションごと省略できるので、既存の config.toml をそのまま使い続けられる。
DEFAULT_RANGE_MIN_CM = 0
DEFAULT_RANGE_MAX_CM = 80

# 回転数の既定値[Hz]と、S1 が受け付ける範囲。
# 範囲は S1 のデータシート（8〜15Hz、標準 10Hz）に合わせてある。外れた値を渡すと
# モーターが目標に乗らないまま動いてしまうので、起動前にこちらで弾く。
DEFAULT_SCAN_FREQUENCY_HZ = 10.0
MIN_SCAN_FREQUENCY_HZ = 8.0
MAX_SCAN_FREQUENCY_HZ = 15.0

# スキャンモード。standard は 1 点 5 バイトで quality つき（既定）、
# express は dense capsule で点数が約 2 倍になる代わりに quality が無い。
# 詳細は lidar.py の冒頭コメント。
SCAN_MODES = ("standard", "express")
DEFAULT_SCAN_MODE = "standard"

# 氷の判定パラメータの既定値（[ice] セクションごと省略可能）。
#
# 氷は Intensity が低く、1 回転あたり 1〜3 点程度でしか写らない。逆に障害物
# （人・機体・造形物）は Intensity が高く、まとまった点で写る。この傾向に乗る。
#
# S1 の standard モードが返す quality は **6bit（0〜63）**。T-mini Plus の 8bit
# （0〜255）とはレンジが違うので、lidar-tracker の値をそのまま持ってきてはいけない。
# 既定の 50 は T-mini Plus の 200/255 と同じ割合を当てただけの仮値で、現物で
# 取り直すこと（probe.py が実測の quality 分布を出す）。64 以上にすると事実上無効。
DEFAULT_INTENSITY_MAX = 50.0

# ホールド[秒]。LiDAR は氷を毎回転は拾えない（T-mini Plus の実測で 0.75〜2 秒に
# 1 回）ので、検出の隙間を埋めないと照準も削りも点滅する。shaving の保持時間は
# そのまま「削りを止めてから雪崩が止まるまでの遅延」になる。
DEFAULT_SHAVING_HOLD_SEC = 1.5

# 共有ファイルの既定の出力先（[output] も環境変数も無いとき）。
DEFAULT_POSITION_FILE = _CONFIG_PATH.parent / "out" / "position.txt"


def _load():
    """config.toml を読んで dict を返す。無ければ分かりやすく終了する。"""
    if not _CONFIG_PATH.exists():
        sys.exit(
            "config.toml が見つかりません。\n"
            "config.example.toml をコピーして config.toml を作成し、"
            "ご自身の環境に合わせて設定してください:\n"
            "    cp config.example.toml config.toml"
        )

    with open(_CONFIG_PATH, "rb") as f:
        return tomllib.load(f)


def _require_number(value, where, key, minimum=None):
    """数値であることを確かめて float で返す。不正なら分かりやすく終了する。"""
    # bool は int のサブクラスなので明示的に弾く（range_max_cm = true を通さない）
    if isinstance(value, bool) or not isinstance(value, (int, float)):
        sys.exit(f"config.toml の {where} {key} は数値で指定してください（現在: {value!r}）。")

    if minimum is not None and value < minimum:
        sys.exit(
            f"config.toml の {where} {key} は {minimum} 以上で指定してください"
            f"（現在: {value}）。"
        )

    return float(value)


def load_serial_config():
    """config.toml からシリアル接続設定を読み込み、(ports, baudrate) を返す。

    ports は接続する LiDAR のポートのリストで、並び順がそのまま LiDAR1, LiDAR2 …
    （＝画面の上から下）になる。書き方は 2 通りで、

        port  = "/dev/tty.usbserial-0001"                 # 1 台だけ
        ports = ["/dev/tty.usbserial-0001", "...0002"]    # 複数台

    どちらでもよい（両方あるときは ports を採る）。

    ボーレートは全台共通。機種を混ぜる想定は無いので台数ぶん持たせていない。
    S1 は 256000（A シリーズの 115200 でも T-mini Plus の 230400 でもない）。

    ファイルが存在しない／必須キーが欠けている・値が不正な場合は、
    分かりやすいメッセージを表示して終了する。
    """
    data = _load()

    try:
        serial_cfg = data["serial"]
        baudrate = serial_cfg["baudrate"]
    except KeyError as e:
        sys.exit(
            f"config.toml に必須キー {e} がありません。"
            "config.example.toml を参考に設定してください。"
        )

    if "ports" in serial_cfg:
        ports = serial_cfg["ports"]

        if not isinstance(ports, list) or not ports:
            sys.exit(
                "config.toml の [serial] ports は 1 つ以上の文字列のリストで"
                f"指定してください（現在: {ports!r}）。"
            )
        if not all(isinstance(port, str) for port in ports):
            sys.exit(
                "config.toml の [serial] ports の要素は文字列で指定してください"
                f"（現在: {ports!r}）。"
            )
    elif "port" in serial_cfg:
        # 1 台だけの旧来の書き方。リストに包んで以降を共通化する
        ports = [serial_cfg["port"]]
    else:
        sys.exit(
            "config.toml の [serial] に port（1 台）か ports（複数台）が"
            "ありません。config.example.toml を参考に設定してください。"
        )

    # 同じポートを 2 度開くと後から開いた方が失敗するので、起動前に弾く。
    # 台数ぶんウィンドウを分割してしまう前に気づける。
    duplicates = {port for port in ports if ports.count(port) > 1}
    if duplicates:
        sys.exit(
            "config.toml の [serial] ports に同じポートが複数あります: "
            + ", ".join(sorted(duplicates))
        )

    return ports, baudrate


def load_scan_config():
    """config.toml からスキャン設定を読み、dict で返す。

    返す辞書のキー:

        frequency_hz  回転数[Hz]（8〜15）
        mode          "standard"（quality つき）か "express"（点数が多い代わりに quality なし）
        mode_id       express のスキャンモード id。None なら機体の推奨値を使う

    回転数を上げるほど 1 回転が速くなる（＝落下する氷を捉え直す間隔が短くなる）が、
    サンプリングレートは一定なので 1 回転あたりの点数は反比例して減る。
    どちらを取るかは現物合わせなので、スクリプトではなく config.toml に置く。

    [scan] セクションもキーも省略可能で、その場合は既定値を使う。
    """
    scan = _load().get("scan", {})

    frequency = _require_number(
        scan.get("frequency_hz", DEFAULT_SCAN_FREQUENCY_HZ), "[scan]", "frequency_hz"
    )

    if not (MIN_SCAN_FREQUENCY_HZ <= frequency <= MAX_SCAN_FREQUENCY_HZ):
        sys.exit(
            f"config.toml の [scan] frequency_hz は "
            f"{MIN_SCAN_FREQUENCY_HZ}〜{MAX_SCAN_FREQUENCY_HZ} Hz の範囲で"
            f"指定してください（現在: {frequency}）。"
        )

    mode = scan.get("mode", DEFAULT_SCAN_MODE)

    if mode not in SCAN_MODES:
        sys.exit(
            "config.toml の [scan] mode は "
            + " / ".join(f'"{m}"' for m in SCAN_MODES)
            + f" のどれかで指定してください（現在: {mode!r}）。"
        )

    mode_id = scan.get("mode_id")

    if mode_id is not None:
        if isinstance(mode_id, bool) or not isinstance(mode_id, int):
            sys.exit(
                "config.toml の [scan] mode_id は整数で指定してください"
                f"（現在: {mode_id!r}）。probe.py がモード一覧を出します。"
            )
        if not (0 <= mode_id <= 255):
            sys.exit(
                f"config.toml の [scan] mode_id は 0〜255 です（現在: {mode_id}）。"
            )

    return {"frequency_hz": frequency, "mode": mode, "mode_id": mode_id}


def load_detection_config():
    """config.toml から検出範囲を読み込み、(range_min_cm, range_max_cm) を返す。

    設置場所ごとに変わる現物合わせの値なので、スクリプトではなくマシンごとの
    config.toml に置く。

    [detection] セクションもキーも省略可能で、その場合は既定値を使う。
    値が不正（数値でない・負・min >= max）なときは終了する。
    """
    det = _load().get("detection", {})

    range_min = _require_number(
        det.get("range_min_cm", DEFAULT_RANGE_MIN_CM), "[detection]", "range_min_cm", 0
    )
    range_max = _require_number(
        det.get("range_max_cm", DEFAULT_RANGE_MAX_CM), "[detection]", "range_max_cm", 0
    )

    if range_min >= range_max:
        sys.exit(
            "config.toml の [detection] は range_min_cm < range_max_cm である必要が"
            f"あります（現在: range_min_cm = {range_min}, range_max_cm = {range_max}）。"
        )

    return range_min, range_max


def load_ice_config():
    """config.toml から氷の判定パラメータを読み込み、dict で返す。

    返す辞書のキー（すべて省略可能で、欠けていれば既定値）:

        intensity_max      氷とみなす quality の上限（これ**未満**だけを氷とする）
        shaving_hold_sec   最後の検出から、削り中とみなし続ける秒数

    氷は反射が弱く quality が低いのに対し、障害物（人・機体・造形物）は高い、
    という傾向に乗る。設置場所と当日の光環境で変わる現物合わせの値。

    **S1 の quality は 6bit（0〜63）**なので、lidar-tracker（T-mini Plus・0〜255）の
    値をそのまま移してはいけない。express モードでは quality が常に 0 で返るので、
    この閾値は事実上「全点が氷の候補」になる（README 参照）。
    """
    ice = _load().get("ice", {})

    return {
        "intensity_max": _require_number(
            ice.get("intensity_max", DEFAULT_INTENSITY_MAX), "[ice]", "intensity_max", 0
        ),
        "shaving_hold_sec": _require_number(
            ice.get("shaving_hold_sec", DEFAULT_SHAVING_HOLD_SEC),
            "[ice]", "shaving_hold_sec", 0,
        ),
    }


# 内覧会（連続座標）モードの関心円の既定値[mm]（preview.toml が無い / キー欠けのとき）。
# 変換フレーム: x = sx*r*sin(theta), y = sy*r*cos(theta)
# （sx / sy は valley_tracker.py の FLIP_HORIZONTAL / FLIP_VERTICAL で決まる ±1）。
#
# 中心は +Y 方向へ radius_mm ぶん＝**円の下端が原点で横軸に接する**位置。
# LiDAR を落氷域の縁に据え、正面に広がる円だけを見る前提。
# センサー自身がちょうど円の接点に来るので、円は丸ごと機体の前方に乗る。
DEFAULT_CIRCLE_CENTER_MM = (0.0, 300.0)
DEFAULT_CIRCLE_RADIUS_MM = 300.0
DEFAULT_CIRCLE_TOLERANCE_MM = 100.0
DEFAULT_REPRESENTATIVE_MODE = "nearest_center"


def load_preview_config():
    """preview.toml から連続座標モードの関心円設定を読み、dict で返す。

    キー: center_offset_mm (2 要素の数値リスト), radius_mm, tolerance_mm, representative_mode。
    ファイルが無い / セクションやキーが欠けている場合は既定値で埋める（現物合わせ前でも動く）。
    """
    data = {}
    if _PREVIEW_PATH.exists():
        with open(_PREVIEW_PATH, "rb") as f:
            data = tomllib.load(f)

    circle = data.get("circle", {})
    rep = data.get("representative", {})

    center = circle.get("center_offset_mm", DEFAULT_CIRCLE_CENTER_MM)
    try:
        cx, cy = (float(center[0]), float(center[1]))
    except (TypeError, ValueError, IndexError):
        sys.exit(
            "preview.toml の [circle] center_offset_mm は数値 2 要素のリストで"
            f"指定してください（現在: {center!r}）。"
        )

    def _num(section, key, default, minimum=0.0):
        v = section.get(key, default)
        try:
            v = float(v)
        except (TypeError, ValueError):
            sys.exit(f"preview.toml の [circle] {key} は数値で指定してください（現在: {v!r}）。")
        if v < minimum:
            sys.exit(f"preview.toml の [circle] {key} は {minimum} 以上で指定してください。")
        return v

    return {
        "center_offset_mm": (cx, cy),
        "radius_mm": _num(circle, "radius_mm", DEFAULT_CIRCLE_RADIUS_MM, minimum=1.0),
        "tolerance_mm": _num(circle, "tolerance_mm", DEFAULT_CIRCLE_TOLERANCE_MM),
        "representative_mode": rep.get("mode", DEFAULT_REPRESENTATIVE_MODE),
    }


def load_output_config():
    """共有ファイル（Unity が読む position.txt）の出力先を Path で返す。

    解決順（Unity 側 VisionInputSource のパス解決と対になる）:
      1. 環境変数 KAKIGORI_POSITION_FILE（通常未設定・手動運用の上書き保険）
      2. config.toml の [output] position_file
      3. スクリプト相対 out/position.txt（Editor 開発の既定・無設定で動く）

    相対パスで書かれた場合は config.toml の場所を基準に解決する（実行ディレクトリに
    依存させない）。
    """
    env = os.environ.get("KAKIGORI_POSITION_FILE")
    if env:
        return Path(env).expanduser()

    value = _load().get("output", {}).get("position_file")

    if value:
        if not isinstance(value, str):
            sys.exit(
                "config.toml の [output] position_file は文字列で指定して"
                f"ください（現在: {value!r}）。"
            )

        path = Path(value).expanduser()

        return path if path.is_absolute() else (_CONFIG_PATH.parent / path)

    return DEFAULT_POSITION_FILE
