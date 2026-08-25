# lidar-tracker（落氷の LiDAR 検知 / position-tracker の後継）

YDLIDAR T-mini Plus の測距値を読み、リアルタイムに極座標散布図へ描画する。
`config.toml` に並べたぶんだけ**複数台を同時に**読み、1 つのウィンドウへ上から順
（LiDAR1, LiDAR2 …）に縦に並べて描く。
`~/work/ydlidar-serial` の `scan_plot_realtime.py` を、この Unity プロジェクト配下で
uv 環境として自閉させたもの（現状は**可視化のみ**）。
センサーとの通信は**公式 SDK（[YDLidar-SDK](https://github.com/YDLIDAR/YDLidar-SDK)）**が担う。

かき氷機の氷入力を「手の画像検知（`tools/position-tracker`）」から
「氷が落ちる様子の LiDAR 検知」へ置き換えるためのツール群。谷チャンネル判定と
共有ファイルの書き出しは `valley_tracker.py` が担う（`scan_plot_realtime.py` は
可視化のみで、範囲やノイズの下見に使う）。

`Assets/` の外（`tools/`）に置き、uv 環境で隔離している（Unity のインポート対象外）。

## 構成

| ファイル | 役割 |
|----------|------|
| `valley_tracker.py` | **本番のエントリポイント**。落氷を 4 方向の谷へ写し、`seq,channel,shaving` を Unity へ書き出す（下記） |
| `scan_plot_realtime.py` | エントリポイント。台数ぶんの読み取りスレッド + matplotlib の極座標アニメーション。`LidarPanel` が 1 台ぶん（センサー・点群バッファ・カウンタ・プロット）を持つ |
| `lidar.py` | `TMiniPlus`：公式 SDK（`ydlidar` モジュール）の薄いラッパ |
| `config.py` | `config.toml` から `(ports, baudrate)`・回転数・検出範囲を読む |
| `config.example.toml` | 接続設定のテンプレート（`config.toml` は gitignore 済み） |
| `setup-sdk.sh` | 公式 SDK を `vendor/` へ clone し、パッチ 2 本を当てる |
| `patches/ydlidar-sdk-macos.patch` | macOS でビルドを通すためのパッチ（内容は下記） |
| `patches/ydlidar-sdk-expose-flag.patch` | 干渉フラグを `LaserPoint` に通すパッチ（同上） |

## セットアップ

```sh
brew install cmake swig                  # SDK のビルドに必要（初回のみ）
cd tools/lidar-tracker
./setup-sdk.sh                           # SDK を vendor/ に取得しパッチを当てる（初回のみ）
uv sync                                  # .venv を作り依存を導入。SDK もここでビルドされる。Python は 3.14
cp config.example.toml config.toml       # 済みなら不要。ports を実機に合わせて書き換える
```

`uv sync` は `vendor/YDLidar-SDK` を **path 依存**としてビルドする（`pyproject.toml` の
`[tool.uv.sources]`）。git 依存にしていないのは、uv がキャッシュ上に clone したものへは
パッチを当てられないため。`vendor/` は gitignore 済みで、リポジトリに入るのはパッチだけ。

シリアルポートの確認（macOS）:

```sh
ls /dev/tty.usb* /dev/cu.usb*            # 例: /dev/tty.usbserial-0001
```

2 台つなぐと `-0001` と `-0002` のように 2 つ出る。どちらがどの機体かは、片方だけ挿して
確認するのが確実（挿す順で番号が入れ替わることがある）。

## 実行

```sh
uv run python valley_tracker.py          # 本番。落氷を谷へ写して Unity へ渡す
uv run python valley_tracker.py --headless   # 窓なし（本番で CPU を空けたいとき）

uv run python scan_plot_realtime.py      # 可視化のみ。範囲・ノイズの下見用
```

どちらもウィンドウを閉じるか Ctrl-C で安全停止する（stop_scan → close）。

ウィンドウは `config.toml` の台数ぶんの段になる（1 台なら 1 段、2 台なら上下 2 段）。
起動時に 1 台でも開けなければ、先に開いた台を閉じてから終了する。

## 複数台の LiDAR（`config.toml`）

```toml
[serial]
ports = [
    "/dev/tty.usbserial-0001",   # LiDAR1（画面の上）
    "/dev/tty.usbserial-0002",   # LiDAR2（画面の下）
]
baudrate = 230400
```

- **並び順がそのまま画面の上から下**（先頭が LiDAR1）。どちらが上か変えたいときは
  この 2 行を入れ替える。
- 1 台だけなら `port = "/dev/tty.usbserial-0001"` と 1 行で書いてもよい（従来の書き方。
  既存の `config.toml` はそのまま動く）。`ports` と両方あるときは `ports` を採る。
- `baudrate` は全台共通。同じポートを 2 つ書くと、起動時にメッセージを出して終了する
  （後から開いた方が黙って失敗するのを防ぐため）。

台ごとに独立しているのは**センサー・点群バッファ・セクタカウンタ**だけで、
**回転数・検出範囲・計測セクタ・角度オフセット・各フィルタは全台で共通**。
台ごとに違う値にしたくなったら、`LidarPanel` に持たせて `config.toml` を台ごとの
テーブルにする（現状はその必要が出ていないので共通にしてある）。

読み取りは**台ごとに 1 スレッド**。片方の読み取りが落ちても、そのループが抜けるだけで
もう片方の表示は動き続ける（どの台かは `[LiDAR2] read_scan error: ...` の形で出る）。

## 調整パラメータ

`scan_plot_realtime.py` 冒頭の「設定」定数で現物合わせする。

回転数と検出範囲は設置場所ごとに変わるので、スクリプトではなく `config.toml` にある（下記）。

- `DISPLAY_SCALE` — 表示範囲 = `RANGE_MAX` × 倍率。円外のコンテキストをどれだけ見せるか
- `ANGLE_OFFSET_DEG` — センサー正面を画面上のどこへ向けるか（表示全体の回転。全台に効く）
- `BIN_RESOLUTION_DEG` — 角度ビンの解像度。1.0° → 360 ビン
- `FILTER_ZERO_DISTANCE` — 無反射（距離 0）の除外
- `FILTER_AMBIENT` / `FILTER_SPECULAR` — 外乱光（太陽光）・鏡面反射（ガラス）の除外。
  干渉フラグを読めるのは SDK の中だけなので、この 2 つは `TMiniPlus` 経由で
  SDK（`enableSunNoise` / `enableGlassNoise`）に渡している
- `FILTER_BY_RANGE` — 距離による範囲外の除外
- `COLOR_BY_INTENSITY` / `INTENSITY_COLORMAP` — Intensity（反射強度）で点を色分けするか
- `COUNT_SECTOR_START_DEG` / `COUNT_SECTOR_END_DEG` — 検出回数を数える範囲（下記）
- `COUNT_RELEASE_ROTATIONS` — 何回転消えたら「次の検出」と見なすか（下記）
- `FIGURE_WIDTH_IN` / `PANEL_HEIGHT_IN` — ウィンドウの幅と、プロット 1 枚ぶんの高さ[インチ]。
  全体の高さは「1 枚ぶん × 台数 + ボタン行」で決まるので、台数が増えても 1 枚あたりの
  大きさは変わらない（画面に入りきらないときは `PANEL_HEIGHT_IN` を下げる）

## 回転数の設定（`config.toml`）

```toml
[scan]
frequency_hz = 10.0
```

- 省略可能（既定 `10.0`）。**対応範囲は 5〜12 Hz**で、外れると起動時にメッセージを出して
  終了する。範囲は SDK の `isSupportScanFrequency()`（`YDLIDAR_Tmini` は 5〜12）に合わせた。
  SDK に範囲外の値を渡すと `out of range` を出しつつ既定の回転数で動いてしまうため、
  こちら側で先に弾いている。
- サンプリングレートは回転数に依らず **4 kHz** で一定なので、**1 回転あたりの点数は
  回転数に反比例する**:

  | 回転数 | 1 回転の最大点数 | 同じ場所を捉え直す間隔 |
  |--------|------------------|------------------------|
  | 5 Hz   | 800 点           | 200 ms |
  | 10 Hz  | 400 点           | 100 ms |
  | 12 Hz  | 333 点           | 83 ms |

  落ちる氷を追うなら速く、点を密に取りたいなら遅く、というトレードオフ。

### ウィンドウの表示

各プロットのタイトルは 3 行で、2 行目に合わせ込みに要る 3 つの値が出る:

```
LiDAR1   /dev/tty.usbserial-0001   (range = 0-40 cm)
10.0 Hz   max 400 pts/rev   detected   33 pts
225-315 deg:     3 hits      127 pts   DETECTING
```

- 1 行目 — どの台か（名前とポート）と検出範囲
- 3 行目 — セクタのカウンタ（下記）

- `10.0 Hz` — `config.toml` で**設定した**回転数
- `max 400 pts/rev` — その回転数で 1 回転に取れる点数の理論値（4000 ÷ 回転数）
- `detected 33 pts` — **直近 1 回転**でフィルタを通った点の数。無反射・ノイズ除去・
  検出範囲外の棄却があるので必ず `max` 以下になる

`detected` は点の数であって画面上の点の数ではない（同じ角度ビンに複数の点が入りうる）。
実測では 10 Hz で 1 回転あたり約 400 点が返り、理論値とほぼ一致する。

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

## セクタの検出回数カウント（Start / Reset ボタン）

ウィンドウ左下の **Start / Reset** ボタンを押すと、指定セクタ（既定 **225〜315 度**、
図では薄い青で塗ってある）の検出回数と点数を数え始める。もう一度押すと 0 に戻して
数え直す。数値は**各プロットのタイトル 3 行目**に出る:

```
225-315 deg:     3 hits      127 pts   DETECTING
```

ボタンは 1 つで、**全台のカウンタを同時に**リセットして数え始める（同じ落下を各台が
どう捉えたか比べたいので、台ごとにばらばらに数え始めても読めないため）。数える範囲の
定義も全台共通だが、数えるのはその台に写った点だけなので、値は台ごとに別々に増える。

- `3 hits` — ボタンを押してからの**検出回数**（立ち上がりの数。下記）
- `127 pts` — ボタンを押してからセクタに**写った点数の累計**
- `DETECTING` — いまセクタ内に点がある間だけ出る

`hits` は 1 回の落下を 1 と数え、`pts` はその落下が何点で写ったかが積み上がる。
両方見ると「3 回落ちて、平均 40 点ぶん写った」のように濃さまで読める。

### 「1 回」の数え方

**セクタが「点なし」→「点あり」に変わった回数**を数える（立ち上がりのみ）。氷が 1 個
落ちて数回転映り続けても **1 回**で、映っている間ずっと加算されるわけではない
（`pts` の方は映っている間ずっと積み上がる）。

点滅で 1 回の落下が 2 回に割れるのを防ぐため、`COUNT_RELEASE_ROTATIONS` 回転ぶん
連続で点が無くなるまでは「まだ同じ検出が続いている」と見なす（既定 2 回転＝10Hz で
0.2 秒）。氷が速く連続で落ちて 1 回に丸められるようなら、この値を小さくする。

### 数える点の条件

`hits` / `pts` とも、散布図に出る点と同じ `_is_valid()` を通ったものだけを数える。つまり検出範囲
（`config.toml` の `[detection]`）の外にある壁や、無反射・ノイズの点では増えない。

セクタは**図に見えているとおりの角度**で指定する（`ANGLE_OFFSET_DEG` 適用後の表示角）。
`315 → 45` のように 0 度をまたぐ指定もできる。

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
| `python/CMakeLists.txt` | 拡張モジュールを `-undefined dynamic_lookup` でリンク（libpython を焼き込まない）。SWIG に `-threads` を渡し、ブロックする C++ 呼び出しの間 GIL を手放させる（下記） |

さらに `pyproject.toml` 側で 2 つ手当てしている:

- `[tool.uv.extra-build-dependencies]` — SDK の CMake は `cmake_minimum_required(VERSION 2.8)`
  と CMake 4 で削除された古いポリシーに依存しているので、ビルド時だけ `cmake<4` を使う。
- `[tool.uv.extra-build-variables]` — SDK の socket 実装は macOS 用ヘッダを `_DARWIN` で
  ガードしているのに CMakeLists がそれを定義しないので、`CXXFLAGS=-D_DARWIN` を渡す。
  **Linux で動かすときはこの指定を外すこと**（`net/if.h` と `linux/if.h` が衝突する）。

SDK を別コミットへ上げるときは、`setup-sdk.sh` の `COMMIT` を書き換えて
パッチ 2 本が当たるか（`git apply`）を確認する。

## 干渉フラグのパッチ（`patches/ydlidar-sdk-expose-flag.patch`）

T-mini 系は距離値の下位 2bit に干渉フラグを載せてくる（`3` = 太陽光ノイズ、
`2` = ガラス／鏡面反射）。SDK はこれを `node.is` に取り出したあと、
`enableSunNoise()` / `enableGlassNoise()` が有効なら距離を 0 に潰し、**フラグ自体は
捨ててしまう**。`LaserPoint` は `angle` / `range` / `intensity` しか持たないので、
呼び出し側からは「どの点が疑わしいと判定されたか」が分からない。

そこで 2 箇所だけ足している:

| 対象 | 直したこと |
|------|-----------|
| `core/common/ydlidar_def.h` | `LaserPoint` に `uint8_t flag` を追加。SWIG が `%include` しているので Python から `point.flag` で読める |
| `src/CYdLidar.cpp` | `doProcessSimple()` が点を組み立てるところで `point.flag = node.is` を積む |

これで `FILTER_AMBIENT = False`（＝太陽光フィルタ off）のときに、SDK が疑わしいと
判定していた点を捨てずに受け取りつつ、その数だけを別に数えられる。
画面では左の欄の最下段に `flag 0x03 sun` として出る。

数え方も範囲も上の hits/pts とまったく同じ（立ち上がり回数・点数の累計・直近
1 回転の点数を、同じ計測セクタ・同じ有効判定の点から数える）。数え方の本体は
`RisingEdgeCounter` に切り出してあり、`SectorCounter` がそれを 2 つ持つ。
2 つとも `_sector_points()` の返す 1 つのリストから数えるので、範囲がズレることはない。

hits/pts と違うのは 2 点だけ:

- モードに左右されない（`background` でも静止背景での絞り込みをしない）
- `background` の背景学習中も数え続ける（信号の質を見るための数字で、
  検出の数え上げとは別のため）

注意点:

- フラグが入るのは `IntenstiyBit = 8` のときだけ。SDK は 10bit 強度のとき
  同じ 2bit を強度の上位ビットに使うので、フラグは復号されず常に 0 になる。
- `setAutoIntensity` は SDK 既定で有効。チェックサムエラーが 2 回続くと強度ビットを
  8 → 0 → 16 と勝手に切り替えるため、そのときはフラグが来なくなる
  （SDK が `The intensity has been automatically adjusted to[N]bit` とログを出す）。
- `#pragma pack(1)` 下の構造体を変えるので、SDK を作り直したら
  `uv sync --reinstall-package ydlidar` で Python バインディングも必ず入れ直すこと。

### SWIG の `-threads` が要る理由

SWIG は既定で GIL を保持したまま C++ を呼ぶ。`doProcessSimple()` は 1 回転ぶんが
揃うまで内部で最大 1 秒ブロックするので、読み取りスレッドが GIL を握りっぱなしになり、
**メインスレッドの GUI イベントループが回らずウィンドウがグレーのまま描画されない**。

実測（メインスレッドが 3 秒間に回れた回数 / 最大停止時間）:

| | ループ回数 | 最大停止 |
|---|---|---|
| 読み取りスレッド無し（基準） | 726/秒 | 5.8 ms |
| `-threads` 無し | 10/秒 | 105.0 ms |
| `-threads` 有り | 704/秒 | 10.0 ms |

105 ms は 1 回転（10 Hz）とちょうど一致する。

## 移植元との差分

- 依存から `serial>=0.0.97` を削除。PyPI の `serial` は pyserial とは無関係の別パッケージ。
- この用途で使わないスクリプト（`scan_all.py` などの旧版）は持ち込んでいない。


## Unity への受け渡し（`valley_tracker.py`）

落氷の検出を「どの谷か（`channel`）」と「削っているか（`shaving`）」に変換し、共有
テキストファイルへ 1 行 `seq,channel,shaving`（例 `1837,2,1`）を書く。Unity 側は
`VisionInputSource` がこのファイルをポーリングし、`InputMode.Vision` のとき
`ActiveChannel` と `IsShaving` に使う（`seq,channel` の 2 列だけの旧 `position-tracker`
契約も引き続き読める）。

- 書き込みは `position.txt.tmp` に書き切って `os.replace()` で原子的 rename
  （Unity が中途半端な行を読まない）。間隔は 50ms
- 氷が無い間も `-1,0` を書き続けて `seq` を進める。Unity は `seq` の停止で
  「Python 停止（ロスト）」を検知し、`channel` を -1・`shaving` を false に倒す
- 出力先の解決順：環境変数 `KAKIGORI_POSITION_FILE` → `config.toml` の
  `[output] position_file` → スクリプト相対 `out/position.txt`
  （既定のままで Unity Editor から読める。`VisionConfig.asset` の `_relativePath` と対）

### 谷の判定（`config.toml` の `[valleys]` / `[ice]`）

LiDAR 2 台はどちらも**北の谷の正面**、ラックの上下段に置く。水平位置が同じなので各谷への
方位角は 2 台で共通で、違うのは氷を捉える高さだけ。4 つの谷の配置の**外側**に立つため
北の谷と南の谷はほぼ同じ方位角に重なり、角度だけでは切り分けられない。そのため谷は
「角度 × 距離」の**極座標の矩形**で指定する。

**LiDAR の正面は 225〜315 度**（中心 270 度＝極座標プロットの画面左）で、
`scan_plot_realtime.py` の既定計測セクタと同じ範囲。4 つの谷はすべてこの 90 度の中に
収める（範囲外を指定しても点が来ない）。仮値は正面を素直に 4 分割してある。

```toml
[valleys.north]     # north=0 / west=1 / south=2 / east=3（Unity の channel と対応）
angle_start_deg = 250   # 正面の中心。south と同じ方位角で、距離だけが違う
angle_end_deg = 290     # 0 度をまたぐ指定は start > end で書く
dist_min_cm = 15
dist_max_cm = 40
```

`west`（225-250）と `east`（290-315）の左右は LiDAR の向きで入れ替わるので、現物で
確認して逆なら**この 2 つの角度を入れ替える**。Unity 側の `_visionFlipHorizontal` では
直さないこと（補正が二重になって原因が読めなくなる）。

氷かどうかは 2 つの傾向で判定する（どちらも `[ice]` のノブ）:

- **氷は Intensity が低い / 障害物は高い** → `intensity_max`（既定 200）**未満**だけを氷の候補に
- **氷は 1〜3 点でしか写らない / 障害物はまとまって写る** → 谷ごとの点数が
  `max_points_per_valley`（既定 5）を超えたら、その谷は障害物とみなして照準にも削りにも
  寄与させない

複数の谷に残ったら**点数が最も少ない谷**を採る（少ない方が氷らしい）。同数なら直前の谷を
維持する。静止背景の学習（`scan_plot_realtime.py` の background モード）は使わない。

2 台の OR は**判定した後**に取る。回転の位相が同期していないので点は合算せず、台ごとに
1 回転を独立に判定し切って、その結果のうち**最も新しい検出**を採る（合算すると同じ氷を
2 台が拾っただけで `max_points_per_valley` に引っかかる）。

### ホールド（検出の隙間を埋める）

LiDAR は氷を毎回転は拾えない（実測で 0.75〜2 秒に 1 回）。生のまま流すと照準も削りも
点滅するので、最後の検出から一定時間は保持する。保持時間は求める挙動が逆なので 2 本ある:

- `channel_hold_sec`（既定 5.0、0 で無期限）— 長く取っても実害は小さい。谷が -1 に飛ぶなら伸ばす
- `shaving_hold_sec`（既定 1.5）— **そのまま「削りを止めてから雪崩が止まるまでの遅延」**に
  なる。短くすると氷が流れているのに雪崩が途切れる。当日の主要な調整ノブ

### 画面の見方

- 有効な点はすべて**灰色**、そのうち氷とみなした点だけ**赤**で重なる
- 4 つの谷は**青**く塗られ、採用中の谷だけ濃くなる
- 左の欄に台ごとの検出回数と**谷ごとの点数**（`max_points_per_valley` を決める根拠）
- 下端に、いま Unity へ書いている `seq` / `channel` / `shaving` と出力先パス
