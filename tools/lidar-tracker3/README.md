# lidar-tracker3（落氷の LiDAR 検知 / RPLIDAR S1 版）

`tools/lidar-tracker`（YDLIDAR T-mini Plus）を **Slamtec RPLIDAR S1** 向けに移したもの。
判定ロジック・出力形式・スレッド構成は同じで、違うのは**センサーを開く層だけ**。

Unity へ渡すのは 1 行 `seq,x,y,shaving`（氷の連続座標モード / ADR-29）。
谷（channel）の分類はしない。

```
seq,x,y,shaving        例) 1837,12.5,-284.0,1
                       検知なしは  1838,,,0
```

座標は LiDAR 原点フレーム[mm]:

```
x = sx·r·sinθ ,  y = sy·r·cosθ        （θ = 表示角[deg]、r = 距離[mm]）
```

`sx` / `sy` は `FLIP_HORIZONTAL` / `FLIP_VERTICAL` で決まる ±1。反転が両方 False なら
θ=0°→+Y・θ=90°→+X（方位磁針と同じ時計回り）。**既定は両方 True** なので θ=0°→−Y、
θ=90°→−X。センサーの生角のどこを θ=0° にするかは `ANGLE_OFFSET_DEG` で合わせる。

関心円の中心は +Y へ `radius_mm` ぶん（`(0, +300)`）＝**円の下端が原点で横軸に接する**位置。
LiDAR を落氷域の縁に据え、正面に広がる円だけを見る前提で、円は丸ごと機体の前方に乗る。
円の中心に落ちた氷はおよそ `(0, +300)` になる。

`Assets/` の外（`tools/`）に置き、uv 環境で隔離している（Unity のインポート対象外）。

## 構成

| ファイル | 役割 |
|----------|------|
| `valley_tracker.py` | **本番のエントリポイント**。落氷の代表点を求め、`seq,x,y,shaving` を Unity へ書き出す |
| `probe.py` | **最初に走らせるもの**。機種情報・スキャンモード一覧・点の出方と quality 分布を出す |
| `lidar.py` | `RPLidarS1`：RPLIDAR のシリアルプロトコル実装（pyserial のみ） |
| `config.py` | `config.toml` / `preview.toml` の読み込みと検証 |
| `config.example.toml` | 接続・スキャン・検出範囲・氷の判定のテンプレート（`config.toml` は gitignore 済み） |
| `preview.example.toml` | 関心円のテンプレート（`preview.toml` は gitignore 済み） |

## セットアップ

```sh
cd tools/lidar-tracker3
uv sync                                  # .venv を作り依存を導入。Python は 3.14
cp config.example.toml config.toml       # port を実機に合わせて書き換える
cp preview.example.toml preview.toml     # 関心円。現物合わせ前は既定のままでよい
```

lidar-tracker と違い、**ネイティブビルドは要らない**（`brew install cmake swig` も
`setup-sdk.sh` も `vendor/` も無い）。依存は pyserial・numpy・matplotlib だけ。

シリアルポートの確認（macOS）:

```sh
ls /dev/tty.usb* /dev/cu.usb*            # 例: /dev/tty.usbserial-0001
```

- **Silicon Labs CP210x の VCP ドライバは入れなくてよい**（このキットの USB アダプタは
  macOS 標準のドライバで `/dev/tty.usbserial-*` として見える）。
- **S1 は補助 5V を必ず挿すこと**。起動電流が大きく、USB 給電だけだとモーターが
  回りきらずスキャンが始まらない。

## 実行

```sh
uv run python probe.py                   # まず疎通確認（下記）
uv run python valley_tracker.py          # 本番。可視化つき
uv run python valley_tracker.py --headless   # 窓なし（本番で CPU を空けたいとき）
```

`valley_tracker.py` はウィンドウを閉じるか Ctrl-C で安全停止する（stop_scan → close）。
ウィンドウは `config.toml` の台数ぶんの段になる。起動時に 1 台でも開けなければ、
先に開いた台を閉じてから終了する。

### まず `probe.py`

`lidar.py` は公式 SDK ではなく**自前のプロトコル実装**なので、現物で最初に当たるのは
ここ。3 つを順に出す。

1. 機種情報（model / firmware / serial）と自己診断
2. スキャンモード一覧（id・名前・サンプリング周期・最大距離・応答種別）と推奨モード
3. 実スキャン（1 回転の点数・距離のレンジ・quality の分布）

3 がそのまま `config.toml` の合わせ込みに使える。距離のレンジ → `[detection]`、
quality の分布 → `[ice] intensity_max`。

うまくいかないときの見分け:

| 症状 | 見るところ |
|------|-----------|
| 「応答ヘッダが不正です」 | ボーレート。**S1 は 256000**（A シリーズの 115200 / T-mini Plus の 230400 ではない） |
| 「応答しません」 | ポート名、補助 5V、ケーブル |
| モード一覧は出るがスキャンが始まらない | `--mode express` で試す。standard に応じないファームがある |
| express で「このドライバが読めない応答種別」 | モード一覧で `ans=0x86`（dense capsule）の id を `[scan] mode_id` に指定する |
| 有効な点が 0 | `[detection] range_max_cm`。S1 の最短測距は 10cm |

実機で確認済みの正常な出力は「実機で確認した結果」を参照。

## スキャンモード（`config.toml` の `[scan] mode`）

S1 は 2 つの読み方があり、**quality（反射強度）が付くか**が決定的に違う。

| | `standard`（既定） | `express` |
|---|---|---|
| コマンド | `0x20 SCAN` | `0x82 EXPRESS_SCAN`（dense capsule） |
| 1 点あたり | 5 バイト | 2.1 バイト |
| quality | **あり**（0〜63） | **なし** |
| 1 回転の点数（10Hz） | 約 410 | 約 920 |
| 距離の上限 | 約 16.4m（電文の桁） | 40m |

express は 84 バイトに 40 点を詰めるので全レートが流せる代わりに、電文に距離しか
載っていない。落氷の判定は quality を主要な手がかりにしているので **standard が既定**。

> **実機（firmware 1.28）では express は使えない**（下記「実機で確認した結果」）。
> standard の約 410 点/回転は T-mini Plus（10Hz で 400 点）と同等で、しかも quality が
> 付くので、この用途では不足しない。

## 実機で確認した結果（S1 1 台 / firmware 1.28 / 2026-08-31）

`/dev/tty.usbserial-0001`、標準ドライバのみ（CP210x VCP は未導入）で全経路が動いた。

```
model 0x61  firmware 1.28  hardware 18  health good
[0] Standard    244.00 us/sample  max 40.0 m  ans=0x81  ← 使えるのはこちら
[1] DenseBoost  108.00 us/sample  max 40.0 m  ans=0x85  ← typical だが読めない
```

- **`express` は不可**。この個体の DenseBoost が返すのは **ultra capsule（0x85）**で、
  このドライバが読む dense capsule（0x86）ではない。ultra capsule は 132 バイト・32 cabin で
  距離を可変ビット長の差分に詰めた別形式で、未対応。`--mode express` はその旨のエラーで
  止まる。手持ちの機体がどちらかは `probe.py` のモード一覧（`ans=` の値）で分かる。
- **`standard` は良好**。1 回転あたり 369〜489 点（申告 244us/sample から期待される
  約 410 点と一致）、距離 17.6〜1554cm、`valley_tracker.py` は 20 行/秒で
  `seq,x,y,shaving` を書き続けた。Ctrl-C で `Interrupted` → `Scan Stopped` → 終了コード 0。
- **開始フラグは角度 0 度で立たない**（この個体では約 69 度）。ここを踏んで最初は
  1 回転が 2 つに割れていた（98 点 + 412 点の交互）。回転の切れ目は機体のフラグだけで
  決め、角度の巻き戻りはフラグを一度も見ていない間の保険に限定して直してある。
- **quality の実測は 5〜58、86% が 24〜39**（室内・氷なし）。既定の `intensity_max = 50`
  では周囲の物体もほぼ素通りするので、**氷を落としながら取り直す**こと。

## quality（反射強度）— lidar-tracker から値を写さないこと

氷は反射が弱く quality が低い / 障害物（人・機体・造形物）は高い、という傾向に乗って
`[ice] intensity_max` **未満**だけを氷の候補にする。ここは lidar-tracker と同じ。

ただし**レンジが違う**。

| | quality | `intensity_max` の既定 |
|---|---|---|
| T-mini Plus（lidar-tracker） | 8bit・0〜255 | 200 |
| **S1 standard**（このツール） | **6bit・0〜63** | 50（仮値） |
| S1 express | 無し | 効かない |

lidar-tracker の 200 をそのまま写すと全点が素通りする。既定の 50 も同じ割合を
当てただけの仮値で、**室内実測では quality の 86% が 24〜39 に集まっていた**ため、
50 のままでは周囲の物体もほぼ素通りする。氷を落としながら `probe.py` の分布を見て、
氷の点が出る位置の上に閾値を置くこと。64 以上にすると事実上フィルタ無効になる。

## 調整パラメータ

| どこ | キー | 意味 |
|------|------|------|
| `config.toml` `[serial]` | `port` / `ports` / `baudrate` | 接続。S1 は 256000 |
| `config.toml` `[scan]` | `frequency_hz` | 回転数[Hz]。S1 は 8〜15、標準 10 |
| | `mode` / `mode_id` | standard / express（上記） |
| `config.toml` `[detection]` | `range_min_cm` / `range_max_cm` | この外の点は捨てる。壁や周囲の人を落とす主要なノブ |
| `config.toml` `[ice]` | `intensity_max` | 氷とみなす quality の上限（上記） |
| | `shaving_hold_sec` | 最後の検出から削り中とみなす秒数。**そのまま「削りを止めてから雪崩が止まるまでの遅延」になる**。当日の主要なノブ |
| `preview.toml` `[circle]` | `center_offset_mm` / `radius_mm` / `tolerance_mm` | 関心円。この中の点だけを氷の候補にする。中心は既定で `[0, radius_mm]`（下端が原点で接する） |
| `valley_tracker.py` | `ANGLE_OFFSET_DEG` | センサーの生角のどこを表示角 0 度にするか |
| | `FLIP_HORIZONTAL` / `FLIP_VERTICAL` | 左右／上下の反転。設置と見え方の向きを吸収する |
| `config.toml` `[output]` | `position_file` | Unity が読む共有ファイルの出力先 |

`valley_tracker.py` の画面では、有効な点がすべて灰色、関心円内の氷が赤、採用中の
代表点が緑の輪で出る。下端に「いま Unity へ書いている行」がそのまま出るので、
点と出力の突き合わせは画面だけでできる。

## 座標フレーム（左右・上下とも反転／関心円は原点で接する）

既定（`FLIP_HORIZONTAL = True` / `FLIP_VERTICAL = True`）の見え方:

```
        180° = +Y          ← 関心円はこちら側
            │
  90°  ────┼────  270°
  = −X      │      = +X
         0° = −Y
```

- **θ=0° は画面の真下**（`FLIP_VERTICAL = True`）、**90° は画面の左**（`FLIP_HORIZONTAL = True`）。
  生角のどこを θ=0° にするかは `ANGLE_OFFSET_DEG` で回す（例: 生角 270 度を 0 度にしたいなら `90.0`）。
- **反転は 2 本とも `valley_tracker.py` の定数**。設置の向きで東西・南北が入れ替わるので、
  現物で逆なら**ここだけ**を切り替える。図（プロット）と出力（`position.txt`）は必ず同じ
  向きになり、片方だけ反転することはない。
  **Unity 側の `flipX` / `flipY` では直さない**（補正が二重になって原因が読めなくなる）。
- 反転 2 本を**両方 True にするのは 180 度回転と同じ**。読みにくければ
  `ANGLE_OFFSET_DEG += 180` にして両方 False へ畳んでもよい（意味は変わらない）。
- **関心円は機体の真正面に接して乗る**（`center_offset_mm = [0, radius_mm]` ＝ 既定
  `[0, 300]`）。円の下端がちょうど原点で横軸に接するので、円は丸ごと +Y 側（機体の前方）に
  入る。LiDAR を落氷域の**縁**に据える前提。既定の反転では +Y ＝ 表示角 180 度の側。
  **`radius_mm` を変えたら `center_offset_mm` の y も同じ値に**すること（接するのは
  `center_y == radius_mm` のときだけ）。
  はみ出し許容（`tolerance_mm`）ぶんは接線より下にもはみ出す。原点は中心から 300mm ＝
  フィルタ半径 400mm の内側なので、センサー筐体・治具など至近の反射は円の中に入る ——
  それらは `[detection] range_min_cm` で切ること。

> **Unity 側と二重にならないよう合わせること。** `LidarPointMapper` は
> `local = raw − centerMm` → `worldRotationDeg` 回転 → `flipX`/`flipY` の順で同じ補正を
> 持っている。現行の `LidarPreviewConfig.asset` は `_centerOffsetMm {0, 300}` /
> `_flipY 1`（＝ lidar-tracker 用）なので、**lidar-tracker3 を Unity に繋ぐなら
> `_centerOffsetMm` を `{0, 0}`、`_flipY` を `0` にする**必要がある。
> `_positionFileRelative` も現状 `tools/lidar-tracker/out/position.txt` を指している。

## Unity への受け渡し

`temp` へ書き切って `os.replace()` の原子的 rename で差し替えるので、Unity が
中途半端な行を読むことはない。氷が無い間も `seq` を進めるので、Unity 側は
「正常な検知なし」と「Python が落ちた（`seq` 据え置き＝ロスト）」を区別できる。

出力先の解決順（Unity 側 `VisionInputSource` と対）:

1. 環境変数 `KAKIGORI_POSITION_FILE`
2. `config.toml` の `[output] position_file`
3. スクリプト相対 `out/position.txt`

> **注意**: Unity 側 `VisionConfig.asset` の `_relativePath` は
> `tools/position-tracker/out/` を指している。lidar-tracker3 から渡すなら
> どちらかを合わせること。

## なぜ公式 SDK を使わないか

lidar-tracker では YDLidar-SDK が SWIG の Python バインディングを同梱していたので、
`vendor/` に clone してビルドし `import ydlidar` していた。
RPLIDAR の公式 SDK（[Slamtec/rplidar_sdk](https://github.com/Slamtec/rplidar_sdk)）は
**C++ のみ**で Python バインディングを持たない。呼ぶには pybind11 などの glue を
自分で書いて CMake ビルドを通す必要があり、macOS では最新 master のビルド不具合も
報告されている（[#137](https://github.com/Slamtec/rplidar_sdk/issues/137)）。

RPLIDAR のシリアルプロトコルは公開仕様で、S1 が使う 2 つのモードは実装量も小さい。
pyserial だけで閉じる方が、ネイティブビルド一式（cmake / swig / パッチ）を抱えるより
現場で壊れにくい。

### 切り分け：公式 SDK で答え合わせする

`lidar.py` を疑うときは、公式 SDK の `ultra_simple` と突き合わせる。
macOS + S1 は公式にサポートされている（最新 master ではなく **タグ付きリリース**を使う）。

```sh
git clone --branch release/v1.12.0 https://github.com/Slamtec/rplidar_sdk.git
cd rplidar_sdk && make                   # Xcode Command Line Tools（g++/make）が要る
./output/Darwin/Release/ultra_simple --channel --serial /dev/tty.usbserial-0001 256000
```

これで点が出て `probe.py` で出ないなら `lidar.py` 側の問題、どちらも出ないなら
配線・給電・ポートの問題。

評価用 GUI の RoboStudio は **macOS 非対応**（Windows / Android のみ）。

## lidar-tracker から移していないもの

- `scan_plot_realtime.py`（可視化専用ツール）。`valley_tracker.py` の画面が
  全点・関心円・代表点を出すので、範囲の下見はそちらで足りる。
- `[valleys]`（4 方向の谷の極座標矩形）と `max_points_per_valley` / `channel_hold_sec`。
  連続座標モード（ADR-29）では谷の分類をしないため、`config.py` にも読み込みは無い。
- SDK 側のノイズ除去スイッチ（`FILTER_AMBIENT` / `FILTER_SPECULAR`）と干渉フラグ
  （`LaserPoint.flag`）。RPLIDAR に相当する機能が無い。`flag` はキーだけ 0 で残してある。
