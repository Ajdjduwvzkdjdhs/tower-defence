"""`config.toml` の読み込み（Kakigori TD / lidar-tracker1+3）。

tools/lidar-tracker（T-mini Plus）・tools/lidar-tracker3（S1）の config.py と同じ
流儀（tomllib・欠けたセクションは既定値で埋める・不正値は分かりやすいメッセージで
`sys.exit`）。スキーマはこのツール専用（2 つの config.toml をマージしたものではない）。

構成:
  [circle]        全ユニット共通の関心円（落氷域そのもの）。T-mini も S1 も、各ユニット
                  の検出点を共有フレーム（このツールの出力座標系）へ変換してからこの
                  円で絞る。だから 1 つでよい（センサーごとに置かない）。
  [tmini]         T-mini Plus の共通設定（baudrate・スキャン・向き・検出範囲・intensity）
                  + [[tmini.units]] で台のリスト（各台は独立。ペアも交点法もしない）。
  [s1]            S1 の共通設定（同上） + [[s1.pairs]] でペアのリスト。各ペアは
                  port_a / port_b / baseline_mm（2 台の間隔＝交点法の基線長）を持つ。
  [hold]          shaving_hold_sec。T-mini の各台、S1 の各生ユニットの「最後の検出を
                  保持する秒数」に共通で使う。
  [output]        Unity が読む共有ファイルの出力先。
"""

import sys
import tomllib
from pathlib import Path

_CONFIG_PATH = Path(__file__).parent / "config.toml"

DEFAULT_CIRCLE_CENTER_MM = (0.0, 300.0)
DEFAULT_CIRCLE_RADIUS_MM = 300.0
DEFAULT_CIRCLE_TOLERANCE_MM = 100.0

DEFAULT_RANGE_MIN_CM = 0
DEFAULT_RANGE_MAX_CM = 80

DEFAULT_TMINI_BAUDRATE = 230400
DEFAULT_TMINI_FREQUENCY_HZ = 10.0
DEFAULT_TMINI_INTENSITY_MAX = 200.0  # T-mini は 8bit（0-255）

DEFAULT_S1_BAUDRATE = 256000
DEFAULT_S1_FREQUENCY_HZ = 10.0
DEFAULT_S1_MODE = "standard"
DEFAULT_S1_INTENSITY_MAX = 50.0  # S1 standard は 6bit（0-63）

DEFAULT_SHAVING_HOLD_SEC = 1.5
DEFAULT_POSITION_FILE = _CONFIG_PATH.parent / "out" / "position.txt"

DEFAULT_ANGLE_OFFSET_DEG = 0.0
DEFAULT_FLIP_HORIZONTAL = True
DEFAULT_FLIP_VERTICAL = True


def _load():
    if not _CONFIG_PATH.exists():
        sys.exit(
            f"{_CONFIG_PATH} がありません。"
            "config.example.toml をコピーして作成してください:\n"
            "  cp config.example.toml config.toml"
        )
    with open(_CONFIG_PATH, "rb") as f:
        return tomllib.load(f)


def _num(section, key, default, where, minimum=None):
    v = section.get(key, default)
    try:
        v = float(v)
    except (TypeError, ValueError):
        sys.exit(f"config.toml の {where} {key} は数値で指定してください（現在: {v!r}）。")
    if minimum is not None and v < minimum:
        sys.exit(f"config.toml の {where} {key} は {minimum} 以上で指定してください。")
    return v


def _str(section, key, default, where):
    v = section.get(key, default)
    if not isinstance(v, str) or not v:
        sys.exit(f"config.toml の {where} {key} は空でない文字列で指定してください（現在: {v!r}）。")
    return v


def _bool(section, key, default, where):
    v = section.get(key, default)
    if not isinstance(v, bool):
        sys.exit(f"config.toml の {where} {key} は true/false で指定してください（現在: {v!r}）。")
    return v


def load_circle_config():
    """[circle] を読む。全ユニット共通の関心円。"""
    data = _load()
    circle = data.get("circle", {})

    center = circle.get("center_offset_mm", DEFAULT_CIRCLE_CENTER_MM)
    try:
        cx, cy = (float(center[0]), float(center[1]))
    except (TypeError, ValueError, IndexError):
        sys.exit(
            "config.toml の [circle] center_offset_mm は数値 2 要素のリストで"
            f"指定してください（現在: {center!r}）。"
        )

    return {
        "center_offset_mm": (cx, cy),
        "radius_mm": _num(circle, "radius_mm", DEFAULT_CIRCLE_RADIUS_MM, "[circle]", minimum=1.0),
        "tolerance_mm": _num(circle, "tolerance_mm", DEFAULT_CIRCLE_TOLERANCE_MM, "[circle]", minimum=0.0),
    }


def load_hold_config():
    data = _load()
    hold = data.get("hold", {})
    return {
        "shaving_hold_sec": _num(
            hold, "shaving_hold_sec", DEFAULT_SHAVING_HOLD_SEC, "[hold]", minimum=0.0
        ),
    }


def load_output_config():
    import os

    env = os.environ.get("KAKIGORI_POSITION_FILE")
    if env:
        return Path(env)

    data = _load()
    output = data.get("output", {})
    rel = output.get("position_file")
    if rel is None:
        return DEFAULT_POSITION_FILE
    return (_CONFIG_PATH.parent / rel).resolve()


def _orientation(section, where):
    return {
        "angle_offset_deg": _num(
            section, "angle_offset_deg", DEFAULT_ANGLE_OFFSET_DEG, where
        ),
        "flip_horizontal": _bool(
            section, "flip_horizontal", DEFAULT_FLIP_HORIZONTAL, where
        ),
        "flip_vertical": _bool(
            section, "flip_vertical", DEFAULT_FLIP_VERTICAL, where
        ),
    }


def load_tmini_config():
    """[tmini] + [[tmini.units]] を読む。

    戻り値: dict(baudrate, frequency_hz, angle_offset_deg, flip_horizontal,
                 flip_vertical, range_min_cm, range_max_cm, intensity_max,
                 units=[{"name","port"}...])

    units が空でもエラーにしない（T-mini を 1 台も使わない構成＝S1 ペアのみの
    検証もありうるため）。
    """
    data = _load()
    tmini = data.get("tmini", {})
    detection = tmini.get("detection", {})
    ice = tmini.get("ice", {})

    units = tmini.get("units", [])
    if not isinstance(units, list):
        sys.exit(f"config.toml の [[tmini.units]] はテーブルの配列で指定してください（現在: {units!r}）。")

    parsed_units = []
    seen_ports = set()
    for i, u in enumerate(units):
        where = f"[[tmini.units]] (#{i + 1})"
        if not isinstance(u, dict):
            sys.exit(f"config.toml の {where} はテーブルで指定してください（現在: {u!r}）。")
        if "port" not in u:
            sys.exit(f"config.toml の {where} に port がありません。")
        port = _str(u, "port", None, where)
        name = u.get("name") or f"TMini{i + 1}"
        if port in seen_ports:
            sys.exit(f"config.toml の [[tmini.units]] にポートの重複があります: {port}")
        seen_ports.add(port)
        parsed_units.append({"name": name, "port": port})

    cfg = {
        "baudrate": int(_num(tmini, "baudrate", DEFAULT_TMINI_BAUDRATE, "[tmini]", minimum=1)),
        "frequency_hz": _num(tmini, "frequency_hz", DEFAULT_TMINI_FREQUENCY_HZ, "[tmini]", minimum=1.0),
        "range_min_cm": _num(detection, "range_min_cm", DEFAULT_RANGE_MIN_CM, "[tmini.detection]", minimum=0.0),
        "range_max_cm": _num(detection, "range_max_cm", DEFAULT_RANGE_MAX_CM, "[tmini.detection]", minimum=0.0),
        "intensity_max": _num(ice, "intensity_max", DEFAULT_TMINI_INTENSITY_MAX, "[tmini.ice]", minimum=0.0),
        "units": parsed_units,
    }
    cfg.update(_orientation(tmini, "[tmini]"))
    return cfg


def load_s1_config():
    """[s1] + [[s1.pairs]] を読む。

    戻り値: dict(baudrate, frequency_hz, mode, angle_offset_deg, flip_horizontal,
                 flip_vertical, range_min_cm, range_max_cm, intensity_max,
                 pairs=[{"name","port_a","port_b","baseline_mm"}...])

    pairs が空でもエラーにしない（S1 を使わない構成もありうるため）。
    baseline_mm がここでいう「2 台の間隔」の設定値（README 参照）。
    """
    data = _load()
    s1 = data.get("s1", {})
    detection = s1.get("detection", {})
    ice = s1.get("ice", {})

    mode = s1.get("mode", DEFAULT_S1_MODE)
    if mode not in ("standard", "express"):
        sys.exit(f"config.toml の [s1] mode は standard / express のいずれかです（現在: {mode!r}）。")

    pairs = s1.get("pairs", [])
    if not isinstance(pairs, list):
        sys.exit(f"config.toml の [[s1.pairs]] はテーブルの配列で指定してください（現在: {pairs!r}）。")

    parsed_pairs = []
    seen_ports = set()
    for i, p in enumerate(pairs):
        where = f"[[s1.pairs]] (#{i + 1})"
        if not isinstance(p, dict):
            sys.exit(f"config.toml の {where} はテーブルで指定してください（現在: {p!r}）。")
        port_a = p.get("port_a")
        port_b = p.get("port_b")
        if not isinstance(port_a, str) or not port_a or not isinstance(port_b, str) or not port_b:
            sys.exit(f"config.toml の {where} に port_a / port_b（文字列）が必要です。")
        if port_a == port_b:
            sys.exit(f"config.toml の {where} の port_a と port_b が同じポートです: {port_a}")
        for port in (port_a, port_b):
            if port in seen_ports:
                sys.exit(f"config.toml の [[s1.pairs]] にポートの重複があります: {port}")
            seen_ports.add(port)
        name = p.get("name") or f"S1-{chr(ord('A') + i)}"
        if "baseline_mm" not in p:
            sys.exit(f"config.toml の {where} に baseline_mm（2 台の間隔[mm]）がありません。")
        baseline_mm = _num(p, "baseline_mm", None, where, minimum=1.0)
        parsed_pairs.append({
            "name": name, "port_a": port_a, "port_b": port_b, "baseline_mm": baseline_mm,
        })

    cfg = {
        "baudrate": int(_num(s1, "baudrate", DEFAULT_S1_BAUDRATE, "[s1]", minimum=1)),
        "frequency_hz": _num(s1, "frequency_hz", DEFAULT_S1_FREQUENCY_HZ, "[s1]", minimum=1.0),
        "mode": mode,
        "range_min_cm": _num(detection, "range_min_cm", DEFAULT_RANGE_MIN_CM, "[s1.detection]", minimum=0.0),
        "range_max_cm": _num(detection, "range_max_cm", DEFAULT_RANGE_MAX_CM, "[s1.detection]", minimum=0.0),
        "intensity_max": _num(ice, "intensity_max", DEFAULT_S1_INTENSITY_MAX, "[s1.ice]", minimum=0.0),
        "pairs": parsed_pairs,
    }
    cfg.update(_orientation(s1, "[s1]"))
    return cfg
