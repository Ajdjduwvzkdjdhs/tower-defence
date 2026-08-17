# lidar-tracker（落氷の LiDAR 検知 / position-tracker の後継）

YDLIDAR T-mini Plus の測距値を読み、リアルタイムに極座標散布図へ描画する。
`~/work/ydlidar-serial` の `scan_plot_realtime.py` を、この Unity プロジェクト配下で
uv 環境として自閉させたもの（現状は**移植したまま・可視化のみ**）。

かき氷機の氷入力を「手の画像検知（`tools/position-tracker`）」から
「氷が落ちる様子の LiDAR 検知」へ置き換えるための土台。谷チャンネル判定と
`seq,channel` の書き出しは**まだ入っていない**（position-tracker 側の契約を後で移植する）。

`Assets/` の外（`tools/`）に置き、uv 環境で隔離している（Unity のインポート対象外）。

## 構成

| ファイル | 役割 |
|----------|------|
| `scan_plot_realtime.py` | エントリポイント。読み取りスレッド + matplotlib の極座標アニメーション |
| `lidar.py` | `TMiniPlus`：シリアル通信とパケット解析 |
| `config.py` | `config.toml` から `(port, baudrate)` を読む |
| `config.example.toml` | 接続設定のテンプレート（`config.toml` は gitignore 済み） |

## セットアップ

```sh
cd tools/lidar-tracker
uv sync                                  # .venv を作り依存（matplotlib/numpy/pyserial）を導入。Python は 3.14
cp config.example.toml config.toml       # 済みなら不要。port を実機に合わせて書き換える
```

シリアルポートの確認（macOS）:

```sh
ls /dev/tty.usb* /dev/cu.usb*            # 例: /dev/tty.usbserial-0001
```

## 実行

```sh
uv run python scan_plot_realtime.py      # ウィンドウを閉じるか Ctrl-C で安全停止（stop_scan → close）
```

## 調整パラメータ

`scan_plot_realtime.py` 冒頭の「設定」定数で現物合わせする。

- `RADIUS_CM` — 関心円（検出対象）の半径。赤い目安線として描かれる
- `DISPLAY_SCALE` — 表示範囲 = 半径 × 倍率。円外のコンテキストをどれだけ見せるか
- `ANGLE_OFFSET_DEG` — センサー正面を画面上のどこへ向けるか（表示全体の回転）
- `BIN_RESOLUTION_DEG` — 角度ビンの解像度。1.0° → 360 ビン
- `FILTER_ZERO_DISTANCE` / `FILTER_AMBIENT` / `FILTER_SPECULAR` — 無反射・外乱光・鏡面反射の除外

## 移植元との差分

- 依存から `serial>=0.0.97` を削除。PyPI の `serial` は pyserial とは無関係の別パッケージで、
  `lidar.py` が使うのは `pyserial` のみ。
- この用途で使わないスクリプト（`scan_all.py` などの旧版）は持ち込んでいない。
