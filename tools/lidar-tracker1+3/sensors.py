"""センサードライバの継ぎ接ぎ（tools/lidar-tracker と tools/lidar-tracker3 の再利用）。

このツールは新しいセンサープロトコルを実装しない。T-mini Plus は
`tools/lidar-tracker/lidar.py` の `TMiniPlus`、S1 は `tools/lidar-tracker3/lidar.py` の
`RPLidarS1` を**そのままファイルから読み込んで**使う。理由:

  - 両方とも `start_scan() / read_scan() / stop_scan() / close()` という同じ寿命で、
    `read_scan()` は同じ形の辞書のリスト（`angle` / `distance` / `intensity` / `flag`）
    を返す契約に揃っている（それぞれの lidar.py の docstring を参照）。
  - プロトコル実装をここに複製すると、片方にバグ修正が入ったときにもう片方が
    古いまま取り残される。`sys.modules` に別名で登録して import するだけにして、
    ソースは 2 tools と共有する。

`ydlidar`（T-mini Plus の公式 SDK バインディング）は sys.path 越しではなく、
**このツール自身の pyproject.toml が `../lidar-tracker/vendor/YDLidar-SDK` を
path 依存として持つ**ことで、このツールの .venv に正式にインストールされる
（`uv sync` 時に SWIG/CMake でビルドされる。git clone は発生しない。
tools/lidar-tracker が既に clone・パッチ済みのソースを再ビルドするだけ）。
"""

import importlib.util
import sys
from pathlib import Path

_TOOLS_DIR = Path(__file__).resolve().parent.parent
_TMINI_LIDAR_PY = _TOOLS_DIR / "lidar-tracker" / "lidar.py"
_S1_LIDAR_PY = _TOOLS_DIR / "lidar-tracker3" / "lidar.py"


def _load_module(module_name, path):
    """1 ファイルを、名前がぶつからない独立したモジュールとして読み込む。"""
    if not path.exists():
        raise FileNotFoundError(
            f"{path} が見つかりません。tools/lidar-tracker と tools/lidar-tracker3 が"
            "並んで存在している前提です（このツールはそこから lidar.py を読み込みます）。"
        )
    spec = importlib.util.spec_from_file_location(module_name, path)
    module = importlib.util.module_from_spec(spec)
    sys.modules[module_name] = module
    spec.loader.exec_module(module)
    return module


_tmini_mod = _load_module("lidar_tracker1p3_tmini_lidar", _TMINI_LIDAR_PY)
_s1_mod = _load_module("lidar_tracker1p3_s1_lidar", _S1_LIDAR_PY)

# --- T-mini Plus（tools/lidar-tracker/lidar.py） ---------------------------
TMiniPlus = _tmini_mod.TMiniPlus
tmini_max_points_per_rev = _tmini_mod.max_points_per_rev
TMINI_SAMPLE_RATE_K = _tmini_mod.SAMPLE_RATE_K

# --- RPLIDAR S1（tools/lidar-tracker3/lidar.py） ---------------------------
RPLidarS1 = _s1_mod.RPLidarS1
s1_max_points_per_rev = _s1_mod.max_points_per_rev
s1_sample_rate_k_for = _s1_mod.sample_rate_k_for
S1_SCAN_MODE_STANDARD = _s1_mod.SCAN_MODE_STANDARD
S1_SCAN_MODE_EXPRESS = _s1_mod.SCAN_MODE_EXPRESS
