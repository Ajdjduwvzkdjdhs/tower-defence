"""座標変換・平均・直線交点の純ロジック（UI・スレッド・センサーに非依存）。

S1 の混合画素（mixed pixel）対策の核はここ: 1 台の距離値は信用せず、2 台の
「センサー位置 → 検出点群の平均点」を結ぶ直線を 2 本引いて、その交点を実体の
位置とみなす（三角測量 / triangulation）。距離ではなく**方位角**を信用する方法
なので、混合画素で伸びた距離がどちらか一方に乗っても、もう一方の直線との
交点はその歪みの影響を受けにくい。
"""

import math


def display_angle(raw_angle_deg, angle_offset_deg):
    """センサーの生角を、判定・描画で使う表示角へ回す。"""
    return (raw_angle_deg + angle_offset_deg) % 360.0


def flip_signs(flip_horizontal, flip_vertical):
    """左右／上下の反転を (x の符号, y の符号) にする。"""
    return (
        -1.0 if flip_horizontal else 1.0,
        -1.0 if flip_vertical else 1.0,
    )


def to_xy_mm(display_deg, distance_mm, flip_horizontal, flip_vertical):
    """表示角[deg]・距離[mm] → そのセンサー自身の原点フレームの (x, y)[mm]。

    x = sx*r*sin(theta), y = sy*r*cos(theta)（sx/sy は flip_signs）。
    tools/lidar-tracker3 の valley_tracker.py と同じ変換規則。
    """
    rad = math.radians(display_deg)
    sign_x, sign_y = flip_signs(flip_horizontal, flip_vertical)
    return (
        sign_x * distance_mm * math.sin(rad),
        sign_y * distance_mm * math.cos(rad),
    )


def average_point(points_xy):
    """(x, y) の並びの単純平均。空なら None。"""
    if not points_xy:
        return None
    n = len(points_xy)
    sx = sum(p[0] for p in points_xy)
    sy = sum(p[1] for p in points_xy)
    return (sx / n, sy / n)


def line_intersection(p1, p2, q1, q2):
    """直線 p1-p2 と直線 q1-q2 の交点。平行（またはほぼ平行）なら None。

    2 直線の交点の標準公式（各直線を通る 2 点から）。ここでの用途では
    p1/q1 が「センサー位置」、p2/q2 が「そのセンサーが見た平均点」で、
    向きは p1→p2（センサーから見た方位）が本質。p1==p2 のような退化input
    （距離ゼロ）は別途 caller 側で弾く。
    """
    x1, y1 = p1
    x2, y2 = p2
    x3, y3 = q1
    x4, y4 = q2

    denom = (x1 - x2) * (y3 - y4) - (y1 - y2) * (x3 - x4)
    if abs(denom) < 1e-9:
        return None

    a = x1 * y2 - y1 * x2
    b = x3 * y4 - y3 * x4
    px = (a * (x3 - x4) - (x1 - x2) * b) / denom
    py = (a * (y3 - y4) - (y1 - y2) * b) / denom
    return (px, py)
