import sys
import tomllib
from pathlib import Path

# config.py 自身の場所を基準にするため、実行ディレクトリに依存しない
_CONFIG_PATH = Path(__file__).parent / "config.toml"


def load_serial_config():
    """config.toml からシリアル接続設定を読み込み、(port, baudrate) を返す。

    ファイルが存在しない／必須キーが欠けている場合は、
    分かりやすいメッセージを表示して終了する。
    """
    if not _CONFIG_PATH.exists():
        sys.exit(
            "config.toml が見つかりません。\n"
            "config.example.toml をコピーして config.toml を作成し、"
            "ご自身の環境に合わせて設定してください:\n"
            "    cp config.example.toml config.toml"
        )

    with open(_CONFIG_PATH, "rb") as f:
        data = tomllib.load(f)

    try:
        serial_cfg = data["serial"]
        return serial_cfg["port"], serial_cfg["baudrate"]
    except KeyError as e:
        sys.exit(
            f"config.toml に必須キー {e} がありません。"
            "config.example.toml を参考に設定してください。"
        )
