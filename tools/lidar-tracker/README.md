# lidar-tracker（落氷の LiDAR 検知 / position-tracker の後継）

YDLIDAR T-mini Plus の測距値を読み、リアルタイムに極座標散布図へ描画する。
`~/work/ydlidar-serial` の `scan_plot_realtime.py` を、この Unity プロジェクト配下で
uv 環境として自閉させたもの（現状は**可視化のみ**）。
センサーとの通信は**公式 SDK（[YDLidar-SDK](https://github.com/YDLIDAR/YDLidar-SDK)）**が担う。

かき氷機の氷入力を「手の画像検知（`tools/position-tracker`）」から
「氷が落ちる様子の LiDAR 検知」へ置き換えるための土台。谷チャンネル判定と
`seq,channel` の書き出しは**まだ入っていない**（position-tracker 側の契約を後で移植する）。

`Assets/` の外（`tools/`）に置き、uv 環境で隔離している（Unity のインポート対象外）。

## 構成

| ファイル | 役割 |
|----------|------|
| `scan_plot_realtime.py` | エントリポイント。読み取りスレッド + matplotlib の極座標アニメーション |
| `lidar.py` | `TMiniPlus`：公式 SDK（`ydlidar` モジュール）の薄いラッパ |
| `config.py` | `config.toml` から `(port, baudrate)` と検出範囲を読む |
| `config.example.toml` | 接続設定のテンプレート（`config.toml` は gitignore 済み） |
| `setup-sdk.sh` | 公式 SDK を `vendor/` へ clone し、macOS 用パッチを当てる |
| `patches/ydlidar-sdk-macos.patch` | その macOS 用パッチ（内容は下記） |

## セットアップ

```sh
brew install cmake swig                  # SDK のビルドに必要（初回のみ）
cd tools/lidar-tracker
./setup-sdk.sh                           # SDK を vendor/ に取得しパッチを当てる（初回のみ）
uv sync                                  # .venv を作り依存を導入。SDK もここでビルドされる。Python は 3.14
cp config.example.toml config.toml       # 済みなら不要。port を実機に合わせて書き換える
```

`uv sync` は `vendor/YDLidar-SDK` を **path 依存**としてビルドする（`pyproject.toml` の
`[tool.uv.sources]`）。git 依存にしていないのは、uv がキャッシュ上に clone したものへは
パッチを当てられないため。`vendor/` は gitignore 済みで、リポジトリに入るのはパッチだけ。

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
- `FILTER_ZERO_DISTANCE` — 無反射（距離 0）の除外
- `FILTER_AMBIENT` / `FILTER_SPECULAR` — 外乱光（太陽光）・鏡面反射（ガラス）の除外。
  干渉フラグを読めるのは SDK の中だけなので、この 2 つは `TMiniPlus` 経由で
  SDK（`enableSunNoise` / `enableGlassNoise`）に渡している
- `FILTER_BY_RANGE` — 距離による範囲外の除外
- `COLOR_BY_INTENSITY` / `INTENSITY_COLORMAP` — Intensity（反射強度）で点を色分けするか

## 検出範囲の制限（`config.toml`）

SDK にも `LidarPropMinRange` / `LidarPropMaxRange` はあるが、そちらは**デバイスの
測距能力**（0.02〜12 m。`lidar.py` の `DEVICE_MIN_RANGE_M` / `DEVICE_MAX_RANGE_M`）に
据え置き、設置ごとの**関心範囲**は受信後の点群フィルタとして自前で持つ。
SDK 側を絞ってしまうと、範囲外を表示して現物合わせする（`FILTER_BY_RANGE = False`）が
できなくなるため。判定は `_is_valid()` に集約してあり、
`range_min_cm <= distance <= range_max_cm`（両端を含む）を外れた点は破棄する。

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

SDK はこれを `LaserPoint.intensity` として復元する（`LidarPropIntenstiyBit = 8` を指定。
このとき距離値の下位 2bit が干渉フラグとして読まれ、太陽光・ガラス反射の判定に使われる）。
`lidar.py` がその値を `samples[i]["intensity"]` に入れ、`scan_plot_realtime.py` が
**散布図の色**として描く（右側にカラーバー）。

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

## 公式 SDK への移行

当初は pyserial で `0xAA55` パケットを直接解析していたが、公式 SDK
（[YDLidar-SDK](https://github.com/YDLIDAR/YDLidar-SDK)）の Python バインディングに
置き換えた。設定値は公式サンプル `python/examples/tminiplus_test.py` に準拠している。

SDK に任せたもの:

- スキャン開始/停止コマンドと応答の確認、機種・ファーム情報の取得
- パケット同期・チェックサム検証・角度の補間と距離歪み補正
- センサー個体のゼロ点角度オフセット補正
- 断線時の自動再接続
- レンジ外・太陽光ノイズ・ガラス反射ノイズの点を距離 0 に落とす

呼び出し側の契約は保ってある。`read_scan()` が返すのは 1 回転ぶんのサンプル辞書の
リストで、キーは `angle`（度 `[0,360)`）/ `distance`（mm）/ `intensity`（0-255）/ `x` / `y`。
旧 `read_packet()`（1 パケット単位）からの差分は 2 点だけ:

- 名前が `read_scan()` になり、返す単位が 1 パケット → **1 回転**になった
- `flag`（干渉フラグ）が無くなった。SDK の `LaserPoint` は `angle` / `range` / `intensity`
  しか持たない。同じ除去は SDK 内部で行われ、除かれた点は**距離 0**で返るので、
  `FILTER_ZERO_DISTANCE` の一本で拾える。

依存からは `pyserial` を落とした（SDK が自前でシリアルを開くため）。

## macOS 用パッチ（`patches/ydlidar-sdk-macos.patch`）

SDK は Linux / Windows 向けで、素の macOS ではビルドが通らない。`setup-sdk.sh` が
検証済みコミット（`01cdda4`）を clone して、このパッチを当てる。中身は 7 箇所:

| 対象 | 直したこと |
|------|-----------|
| `core/base/locker.h` | `pthread_mutex_timedlock` / `pthread_condattr_setclock` は macOS に無い。前者は既存の Android 向けフォールバック（trylock ループ）に載せ、後者は外して条件変数の待ち時刻を `CLOCK_REALTIME` で作るようにした |
| `core/serial/impl/unix/lock.c` | Linux 専用の `<sys/sysmacros.h>` を除外 |
| `core/serial/impl/unix/unix_serial.cpp` | Linux 専用の `<asm/ioctls.h>` と `setCustomBaudRate()`（termios2）を除外。**`setBaudrate()` が macOS 経路で `return` を欠いていた**のを修正（戻り値が不定になり、ポートを開けなかった） |
| `core/serial/impl/unix/list_ports_osx.cpp` | 新規。SDK には Linux/Windows 版しかなく `list_ports()` が未定義でリンクできない。IOKit を使わず `/dev` を glob するだけの最小実装 |
| `core/network/CMakeLists.txt` | macOS に無い `librt` のリンクを外す |
| `CMakeLists.txt` | C++ サンプルのビルドを既定 OFF（`librt` を要求する）。非推奨の `FindPythonInterp`/`FindPythonLibs` が `PYTHON_EXECUTABLE` を無視して別バージョンの libpython を掴む問題を `find_package(Python3)` で解消 |
| `python/CMakeLists.txt` | 拡張モジュールを `-undefined dynamic_lookup` でリンク（libpython を焼き込まない） |

さらに `pyproject.toml` 側で 2 つ手当てしている:

- `[tool.uv.extra-build-dependencies]` — SDK の CMake は `cmake_minimum_required(VERSION 2.8)`
  と CMake 4 で削除された古いポリシーに依存しているので、ビルド時だけ `cmake<4` を使う。
- `[tool.uv.extra-build-variables]` — SDK の socket 実装は macOS 用ヘッダを `_DARWIN` で
  ガードしているのに CMakeLists がそれを定義しないので、`CXXFLAGS=-D_DARWIN` を渡す。
  **Linux で動かすときはこの指定を外すこと**（`net/if.h` と `linux/if.h` が衝突する）。

SDK を別コミットへ上げるときは、`setup-sdk.sh` の `COMMIT` を書き換えて
パッチが当たるか（`git apply`）を確認する。

## 移植元との差分

- 依存から `serial>=0.0.97` を削除。PyPI の `serial` は pyserial とは無関係の別パッケージ。
- この用途で使わないスクリプト（`scan_all.py` などの旧版）は持ち込んでいない。
