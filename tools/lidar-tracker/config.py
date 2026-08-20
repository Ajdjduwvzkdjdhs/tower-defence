import sys
import tomllib
from pathlib import Path

# config.py 自身の場所を基準にするため、実行ディレクトリに依存しない
_CONFIG_PATH = Path(__file__).parent / "config.toml"

# 検出範囲の既定値[cm]。config.toml に [detection] が無いときに使う。
# セクションごと省略できるので、既存の config.toml をそのまま使い続けられる。
DEFAULT_RANGE_MIN_CM = 0
DEFAULT_RANGE_MAX_CM = 80


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


def load_serial_config():
    """config.toml からシリアル接続設定を読み込み、(port, baudrate) を返す。

    ファイルが存在しない／必須キーが欠けている場合は、
    分かりやすいメッセージを表示して終了する。
    """
    data = _load()

    try:
        serial_cfg = data["serial"]
        return serial_cfg["port"], serial_cfg["baudrate"]
    except KeyError as e:
        sys.exit(
            f"config.toml に必須キー {e} がありません。"
            "config.example.toml を参考に設定してください。"
        )


def load_detection_config():
    """config.toml から検出範囲を読み込み、(range_min_cm, range_max_cm) を返す。

    SDK の range_min / range_max 相当。設置場所ごとに変わる現物合わせの値なので、
    スクリプトではなくマシンごとの config.toml に置く。

    [detection] セクションもキーも省略可能で、その場合は既定値を使う。
    値が不正（数値でない・負・min >= max）なときは終了する。
    """
    det = _load().get("detection", {})

    range_min = det.get("range_min_cm", DEFAULT_RANGE_MIN_CM)
    range_max = det.get("range_max_cm", DEFAULT_RANGE_MAX_CM)

    for key, value in (("range_min_cm", range_min), ("range_max_cm", range_max)):
        # bool は int のサブクラスなので明示的に弾く（range_max_cm = true を通さない）
        if isinstance(value, bool) or not isinstance(value, (int, float)):
            sys.exit(
                f"config.toml の [detection] {key} は数値で指定してください"
                f"（現在: {value!r}）。"
            )
        if value < 0:
            sys.exit(
                f"config.toml の [detection] {key} に負の値は指定できません"
                f"（現在: {value}）。"
            )

    if range_min >= range_max:
        sys.exit(
            "config.toml の [detection] は range_min_cm < range_max_cm である必要が"
            f"あります（現在: range_min_cm = {range_min}, range_max_cm = {range_max}）。"
        )

    return range_min, range_max
