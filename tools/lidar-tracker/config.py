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

# 回転数の既定値[Hz]と、T-mini Plus が受け付ける範囲。
# 範囲は SDK の isSupportScanFrequency()（YDLIDAR_Tmini は 5〜12Hz）に合わせてある。
# 外れた値を渡すと SDK が "Current scan frequency is out of range" を出して
# 既定の回転数のまま動いてしまうので、起動前にこちらで弾く。
DEFAULT_SCAN_FREQUENCY_HZ = 10.0
MIN_SCAN_FREQUENCY_HZ = 5.0
MAX_SCAN_FREQUENCY_HZ = 12.0

# 氷の判定パラメータの既定値（[ice] セクションごと省略可能）。
#
# 氷は Intensity が低く、1 回転あたり 1〜3 点程度でしか写らない。逆に障害物
# （人・機体・造形物）は Intensity が高く、まとまった点で写る。この 2 つの傾向を
# そのまま「上限 2 本」にしたのが intensity_max と max_points_per_valley。
DEFAULT_INTENSITY_MAX = 200.0
DEFAULT_MAX_POINTS_PER_VALLEY = 5

# ホールド[秒]。LiDAR は氷を毎回転は拾えない（実測で 0.75〜2 秒に 1 回）ので、
# 検出の隙間を埋めないと照準も削りも点滅する。channel は長く保持しても実害が
# 小さいが、shaving の保持時間はそのまま「削りを止めてから雪崩が止まるまでの
# 遅延」になるので、2 本に分けてある。
DEFAULT_CHANNEL_HOLD_SEC = 5.0
DEFAULT_SHAVING_HOLD_SEC = 1.5

# 谷の名前と Unity の channel の対応。並び順がそのまま channel 0..3 になる
# （Unity 側 IInputSource.ActiveChannel の 0:N / 1:W / 2:S / 3:E）。
VALLEY_NAMES = ("north", "west", "south", "east")

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


def load_serial_config():
    """config.toml からシリアル接続設定を読み込み、(ports, baudrate) を返す。

    ports は接続する LiDAR のポートのリストで、並び順がそのまま LiDAR1, LiDAR2 …
    （＝画面の上から下）になる。書き方は 2 通りで、

        port  = "/dev/tty.usbserial-0001"                 # 1 台だけ
        ports = ["/dev/tty.usbserial-0001", "...0002"]    # 複数台

    どちらでもよい（両方あるときは ports を採る）。1 台用の port を残してあるのは、
    既存の config.toml をそのまま使い続けられるようにするため。

    ボーレートは全台共通。機種を混ぜる想定は無いので台数ぶん持たせていない。

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
    """config.toml から回転数[Hz]を読み込んで返す。

    回転数を上げるほど 1 回転が速くなる（＝落下する氷を捉え直す間隔が短くなる）が、
    サンプリングレートは一定なので 1 回転あたりの点数は反比例して減る。
    どちらを取るかは現物合わせなので、スクリプトではなく config.toml に置く。

    [scan] セクションもキーも省略可能で、その場合は既定値を使う。
    値が不正（数値でない・対応範囲外）なときは終了する。
    """
    value = _load().get("scan", {}).get("frequency_hz", DEFAULT_SCAN_FREQUENCY_HZ)

    # bool は int のサブクラスなので明示的に弾く
    if isinstance(value, bool) or not isinstance(value, (int, float)):
        sys.exit(
            f"config.toml の [scan] frequency_hz は数値で指定してください"
            f"（現在: {value!r}）。"
        )

    if not (MIN_SCAN_FREQUENCY_HZ <= value <= MAX_SCAN_FREQUENCY_HZ):
        sys.exit(
            f"config.toml の [scan] frequency_hz は "
            f"{MIN_SCAN_FREQUENCY_HZ}〜{MAX_SCAN_FREQUENCY_HZ} Hz の範囲で"
            f"指定してください（現在: {value}）。"
        )

    return float(value)


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


def load_ice_config():
    """config.toml から氷の判定パラメータを読み込み、dict で返す。

    返す辞書のキーは以下（すべて省略可能で、欠けていれば既定値）:

        intensity_max          氷とみなす Intensity の上限（これ**未満**だけを氷とする）
        max_points_per_valley  1 台の 1 回転で 1 つの谷に許す点数の上限（超えたら障害物）
        channel_hold_sec       最後の検出から、その谷を照準として保持し続ける秒数（0 で無期限）
        shaving_hold_sec       最後の検出から、削り中とみなし続ける秒数

    氷は Intensity が低く 1〜3 点程度でしか写らないのに対し、障害物（人・機体・造形物）は
    Intensity が高くまとまった点で写る。この 2 つの傾向をそのまま 2 本のノブにしてある。
    どちらも設置場所と当日の光環境で変わる現物合わせの値なので config.toml に置く。

    ホールドを 2 本に分けているのは、求める挙動が逆だから。channel は長く保持しても
    実害が小さい（削っていなければ雪崩は流れない）が、shaving の保持時間はそのまま
    「削るのを止めてから雪崩が止まるまでの遅延」になる。
    """
    ice = _load().get("ice", {})

    values = {}
    for key, default, minimum in (
        ("intensity_max", DEFAULT_INTENSITY_MAX, 0),
        ("max_points_per_valley", DEFAULT_MAX_POINTS_PER_VALLEY, 1),
        ("channel_hold_sec", DEFAULT_CHANNEL_HOLD_SEC, 0),
        ("shaving_hold_sec", DEFAULT_SHAVING_HOLD_SEC, 0),
    ):
        value = ice.get(key, default)

        # bool は int のサブクラスなので明示的に弾く
        if isinstance(value, bool) or not isinstance(value, (int, float)):
            sys.exit(
                f"config.toml の [ice] {key} は数値で指定してください"
                f"（現在: {value!r}）。"
            )
        if value < minimum:
            sys.exit(
                f"config.toml の [ice] {key} は {minimum} 以上で指定してください"
                f"（現在: {value}）。"
            )

        values[key] = value

    values["max_points_per_valley"] = int(values["max_points_per_valley"])
    values["intensity_max"] = float(values["intensity_max"])
    values["channel_hold_sec"] = float(values["channel_hold_sec"])
    values["shaving_hold_sec"] = float(values["shaving_hold_sec"])

    return values


def load_valleys_config():
    """config.toml から 4 方向の谷の範囲を読み込み、チャンネル順のリストで返す。

    返すのは要素 4 個のリストで、添字がそのまま Unity の channel（0:N 1:W 2:S 3:E）。
    各要素は dict:

        {"name": "north", "channel": 0,
         "angle_start_deg": 350.0, "angle_end_deg": 10.0,
         "dist_min_mm": 200.0, "dist_max_mm": 600.0}

    LiDAR は 4 つの谷の配置の**外側**（北の谷の正面）に立つため、北の谷と南の谷は
    ほぼ同じ方位角に重なる。角度だけでは切り分けられないので、角度と距離の
    両方で「極座標の矩形」として指定する。

    角度は 0 度をまたいでよい（angle_start_deg > angle_end_deg なら、そこで折り返す）。
    距離は cm で書き、ここで mm に直して返す（点群の距離が mm のため）。

    2 台の LiDAR は同じ水平位置（ラックの上下段）に置くので、各谷への方位角は
    2 台で共通。よってこのテーブルは 1 個で、全台が同じものを使う。
    """
    valleys_cfg = _load().get("valleys")

    if not isinstance(valleys_cfg, dict):
        sys.exit(
            "config.toml に [valleys] がありません。"
            "config.example.toml を参考に 4 方向ぶん設定してください。"
        )

    valleys = []

    for channel, name in enumerate(VALLEY_NAMES):
        valley = valleys_cfg.get(name)

        if not isinstance(valley, dict):
            sys.exit(
                f"config.toml に [valleys.{name}]（channel {channel}）が"
                "ありません。4 方向すべてを設定してください。"
            )

        parsed = {"name": name, "channel": channel}

        for key in ("angle_start_deg", "angle_end_deg", "dist_min_cm", "dist_max_cm"):
            if key not in valley:
                sys.exit(
                    f"config.toml の [valleys.{name}] に {key} がありません。"
                )

            value = valley[key]

            if isinstance(value, bool) or not isinstance(value, (int, float)):
                sys.exit(
                    f"config.toml の [valleys.{name}] {key} は数値で"
                    f"指定してください（現在: {value!r}）。"
                )

            parsed[key] = float(value)

        # 角度は 0 度またぎを許すので大小関係は検査しない（0〜360 に正規化するだけ）。
        # 距離は折り返しようがないので、逆転していれば設定ミスとして弾く。
        parsed["angle_start_deg"] %= 360.0
        parsed["angle_end_deg"] %= 360.0

        if parsed["dist_min_cm"] < 0:
            sys.exit(
                f"config.toml の [valleys.{name}] dist_min_cm に負の値は"
                f"指定できません（現在: {parsed['dist_min_cm']}）。"
            )
        if parsed["dist_min_cm"] >= parsed["dist_max_cm"]:
            sys.exit(
                f"config.toml の [valleys.{name}] は dist_min_cm < dist_max_cm で"
                f"ある必要があります（現在: {parsed['dist_min_cm']}, "
                f"{parsed['dist_max_cm']}）。"
            )

        parsed["dist_min_mm"] = parsed.pop("dist_min_cm") * 10.0
        parsed["dist_max_mm"] = parsed.pop("dist_max_cm") * 10.0

        valleys.append(parsed)

    return valleys


# 内覧会（連続座標）モードの関心円の既定値[mm]（preview.toml が無い / キー欠けのとき）。
# 変換フレーム: x = r*cos(theta), y = -r*sin(theta) → theta=270° 方向が +Y。
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
