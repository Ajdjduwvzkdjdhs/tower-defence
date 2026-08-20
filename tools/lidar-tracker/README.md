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

検出範囲だけは設置場所ごとに変わるので、スクリプトではなく `config.toml` にある（下記）。

- `DISPLAY_SCALE` — 表示範囲 = `RANGE_MAX` × 倍率。円外のコンテキストをどれだけ見せるか
- `ANGLE_OFFSET_DEG` — センサー正面を画面上のどこへ向けるか（表示全体の回転）
- `BIN_RESOLUTION_DEG` — 角度ビンの解像度。1.0° → 360 ビン
- `FILTER_ZERO_DISTANCE` / `FILTER_AMBIENT` / `FILTER_SPECULAR` — 無反射・外乱光・鏡面反射の除外
- `FILTER_BY_RANGE` — 距離による範囲外の除外
- `COLOR_BY_INTENSITY` / `INTENSITY_COLORMAP` — Intensity（反射強度）で点を色分けするか

## 検出範囲の制限（`config.toml`）

ROS を使わないので、SDK の `range_min` / `range_max` 相当を受信後の点群フィルタとして
自前で持つ。判定は `_is_valid()` に集約してあり、`range_min_cm <= distance <= range_max_cm`
（両端を含む）を外れた点は破棄する。

設置場所ごとに変わる現物合わせの値なので、スクリプトではなく `config.toml` に置く:

```toml
[detection]
range_min_cm = 0
range_max_cm = 80
```

- 外側 `range_max_cm` — 関心円。**赤線**で描かれる。壁や周囲の人を落として、
  かき氷機まわりの落氷だけを見るための主要なノブ。
- 内側 `range_min_cm` — センサー筐体・治具・支柱など、常に至近距離で返る反射を切る。
  `0` なら内側は切らない（既定）。`0` 以外なら**橙線**で描かれる。
- T-mini Plus の実力値は約 0.02〜12 m。`range_max_cm` を 1200 より大きくしても意味はない。
- `[detection]` はセクションごと省略可能（既定 `0` / `80`）。`range_min_cm < range_max_cm`
  でない・負・数値でない場合は、起動時にメッセージを出して終了する。

破棄された点は、そのビンを NaN に**クリアする**（残さない）。つまり「関心円の外に壁が
ある方向」は点が消えるので、画面上の点は常に「関心領域内に今ある物体」だけになる。

現物合わせのときは `FILTER_BY_RANGE = False` にすると、範囲外の点が `DISPLAY_SCALE` の
余白に見えるので、どこに壁があるか・`RANGE_MAX` をどこに置くべきかを確認できる。

## Intensity（反射強度）の表示

`T-mini Pro 開発マニュアル 3.1.2 Intensity Analysis` より:

> Intensity calculation formula: `Intensity(i) = Intensity[7:0]`
> サンプル `Si` の **1 バイト目**に格納され、値域は **0〜255**。

`lidar.py` の `read_packet()` は既にこれを `samples[i]["intensity"]` として取り出しているので、
`scan_plot_realtime.py` はその値を**散布図の色**として描く（右側にカラーバー）。

- `COLOR_BY_INTENSITY = True`（既定）— 点を強度で色分けし、0〜255 固定スケールの
  カラーバーを添える。`False` にすると従来どおりの単色（青）で、カラーバーも出ない。
- `INTENSITY_COLORMAP = "viridis"` — 低強度＝紫、高強度＝黄。
- `INTENSITY_MIN` / `INTENSITY_MAX` — カラースケールの下限・上限。マニュアルの値域 `0`/`255`
  に合わせてある。フレームごとに自動スケールさせないので、色と強度の対応が時間で動かない。

`ScanBuffer` は距離と強度を同じインデックスの 2 本の配列で持ち、`snapshot()` が
`(距離, 強度)` の対を返す。無効な点はどちらの配列も NaN でクリアされるので、
「点が消えるのに色だけ残る」ことはない。

強度は**表示だけ**で、フィルタ（`_is_valid()`）には使っていない。氷とそれ以外の
切り分けに使えそうなら、しきい値フィルタは後から `_is_valid()` に足す。

## 移植元との差分

- 依存から `serial>=0.0.97` を削除。PyPI の `serial` は pyserial とは無関係の別パッケージで、
  `lidar.py` が使うのは `pyserial` のみ。
- この用途で使わないスクリプト（`scan_all.py` などの旧版）は持ち込んでいない。
