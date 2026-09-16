# lidar-tracker1+3（T-mini Plus x2 + RPLIDAR S1 x4・交点法）

`tools/lidar-tracker`（YDLIDAR T-mini Plus）と `tools/lidar-tracker3`（Slamtec RPLIDAR S1）
の継ぎ接ぎ（`sensors.py` が両方の `lidar.py` をそのまま読み込んで使う。詳細はファイル冒頭）。

想定する設置は 2 段のラック、各段が S1 - T-mini - S1 の 3 台構成（左右端が S1、真ん中が
T-mini）で、6 台合計 = T-mini x2 + S1 x4。同じ落氷域を上下 2 段で見る**冗長構成**
（`CLAUDE.md` の「複数台 LiDAR は冗長性の確保専用」の一部）。

## なぜ T-mini と S1 で扱いが違うのか（混合画素 / mixed pixel）

S1 は小さい物体（落氷）を測ると、照射スポットが物体の縁にまたがったとき、手前の
反射と奥の反射が混ざった中間の距離を返す現象（**混合画素 / mixed pixel**、ghost points
とも）が無視できない精度で出る。ビームの角度発散という物理限界なので、S1 自身の
距離値は完全には信用できない。

そこで S1 は**必ずペアで使い、距離ではなく方位角だけを信用する三角測量（交点法）**
にする:

1. 各台が「関心円内の検出点群の単純平均点」を出す（1 点の生距離ではなく平均を使うことで
   ノイズを均す）。
2. 「センサー位置 → その平均点」を結ぶ直線を両台ぶん引く。
3. 2 直線の交点を実体の位置と見なす。

混合画素で片方の距離が奥へ伸びても、方位角（直線の向き）はさほど崩れないので、
交点はどちらか一方の生距離よりよほど正確になる。**S1 は 2 台揃わないと使わない**
（1 台では直線が 1 本しか引けず交点が作れない＝混合画素の影響をそのまま受けるため）。

T-mini Plus は同じ現象がここまで酷くないので、単体で完結させる。**代表点は
関心円内の検出点群の単純平均**（S1 の各台が交点を作る前に出す平均と同じ計算）。
ペアも交点法もしない。1 台でも 2 台でも独立に動く。

**最終的に Unity へ送る点は、その瞬間に有効な代表点（T-mini 各台の平均点、
S1 各ペアの交点。最大 4 個）の単純平均。** 1 個も無ければ検知なし。

## セットアップ

```sh
cd "tools/lidar-tracker1+3"
uv sync                                  # .venv を作り依存を導入
cp config.example.toml config.toml       # ポート・間隔を実機に合わせて書き換える
```

`uv sync` は T-mini 用の SDK バインディング（`ydlidar`）を `tools/lidar-tracker/vendor/YDLidar-SDK`
（`tools/lidar-tracker` の `./setup-sdk.sh` が既に clone・パッチ済みのもの）から**path 依存として
再ビルド**する。**`tools/lidar-tracker` が先にセットアップ済みである前提**（`vendor/` が
存在しないと `uv sync` が失敗する。`tools/lidar-tracker/README.md` の「セットアップ」を先に）。
S1 側（`tools/lidar-tracker3`）はネイティブビルドが無いので前提なし。

シリアルポートの確認（macOS）:

```sh
ls /dev/tty.usb*
```

## config.toml（設定の置き場所）

`config.example.toml` をコピーして作る（`config.toml` は gitignore 済み・実機ごと）。

| 知りたいこと | セクション・キー |
|---|---|
| **S1 ペアの 2 台の間隔（基線長）** | `[[s1.pairs]]` の `baseline_mm`。ペアごとに設定できる |
| **Unity が読む出力先のパス** | `[output]` の `position_file`（または環境変数 `KAKIGORI_POSITION_FILE`。後述） |
| T-mini の台リスト | `[[tmini.units]]`（`name` / `port`）。1 台でも 2 台でも可 |
| S1 のペアリスト | `[[s1.pairs]]`（`name` / `port_a` / `port_b` / `baseline_mm`）。**必ず 2 台 1 組**で書く（自動検出はしない。取り違えを避けるため明示） |
| 落氷域そのもの（関心円） | `[circle]`（`center_offset_mm` / `radius_mm` / `tolerance_mm`）。T-mini・S1 共通で 1 つだけ |
| T-mini / S1 それぞれの検出範囲・intensity 閾値 | `[tmini.detection]` / `[tmini.ice]`、`[s1.detection]` / `[s1.ice]`（quality のレンジが違うので別）|
| 検出の隙間を埋める保持秒数 | `[hold]` の `shaving_hold_sec`。T-mini 各台・S1 各生ユニット共通 |
| 生角の向き合わせ | `[tmini]` / `[s1]` の `angle_offset_deg` / `flip_horizontal` / `flip_vertical`（種別ごとに 1 つ。台ごとの個別値は無い＝全台同じ向きで据える前提） |

`port_a` は共有フレームの −X 側（基線の半分だけ左）、`port_b` は +X 側に自動的に置かれる
（`baseline_mm` から計算。設定は間隔だけでよい）。逆に挿すと交点が左右反転した位置に出る
ので、GUI で交点マーカーが実際の落氷位置とズレて見えたら `port_a`/`port_b` を入れ替える。

## Unity 側の出力パスを変える

Unity が読むファイルは `[output] position_file`（`config.toml` からの相対パス）。
解決順は他ツールと同じ:

1. 環境変数 `KAKIGORI_POSITION_FILE`
2. `config.toml` の `[output] position_file`
3. スクリプト相対 `out/position.txt`

**Unity 側**は `CLAUDE.md` の「物理トラッカー ↔ Unity の接続方式」にある通り、
`VisionInputSource` が読むパスは Unity の `VisionConfigSO`
（`Assets/ScriptableObjects/VisionConfig.asset` の `_relativePath` / 環境変数
`KAKIGORI_POSITION_FILE` / 隣接 `config.json`）で決まる。**現状 `VisionConfig.asset` の
`_relativePath` は `tools/lidar-tracker/out/position.txt`**（＝ lidar-tracker3 ではなく
lidar-tracker、T-mini 単体版の出力）を指している（2026-09-14 時点で確認）。

**このツールに乗り換えるなら**、Unity Editor 上で `VisionConfig.asset` を選び、
`_relativePath` をこのツールの出力（既定 `tools/lidar-tracker1+3/out/position.txt`、
または `config.toml` の `[output] position_file` で変えた先）に書き換える。
`unity status` が Editor を返すなら `.asset` を手編集せず `unity command set_serialized_field`
等で書き換えること（`CLAUDE.md` の「Unity Editor 連携」参照）。もしくは、環境変数
`KAKIGORI_POSITION_FILE` を Unity とこのツールの両方に同じ値で設定して揃えてもよい。

## 実行

```sh
uv run python tracker1p3.py               # 可視化つき
uv run python tracker1p3.py --headless    # 窓なし
```

GUI は単一の直交座標プロット 1 枚（ポーラーではない。S1 ペアは原点の異なる 2 直線の
交点を見るので、直線がそのまま直線に見える直交座標のほうが交点法の目視確認がしやすい）:

- 灰色: 全ユニットの有効点（共有フレームへ変換済み）
- 赤: 全ユニットの関心円内・氷候補点
- 橙の三角: T-mini 各台の代表点（点群平均）— 最大 2 個
- 紫の四角: S1 各ペアの交点（交点法の推定点）＋薄い点線 2 本（デバッグ用のレイ）— 最大 2 個
- 緑の輪: 最終的に Unity へ送る代表点（有効な代表点の平均）— 1 個
- 左の欄: 台・ペアごとの生存確認（検出点数・累計ヒット数・LIVE/hold 状態）
- 下端: いま Unity へ書いている値そのまま（`seq` / xy / shaving / 出力先パス）

## 台数の増減（検証用の縮小構成もそのまま動く）

- **T-mini は 1 台ずつ独立**。開けなかった台は飛ばして残りで続行する（1 台だけ・2 台とも、
  どちらも動く）。
- **S1 は必ずペア単位**。ペアの片方しか開けなければ、そのペアごと無効化する
  （両方揃わない S1 は絶対に有効入力にしない）。S1 を 2 台だけ接続して 1 ペアの
  交点法の検証をする、という使い方もこれでそのまま動く。
- T-mini・S1 は完全に独立（片方が 0 台でも、もう片方だけで動く）。
- 一段分の 3 台構成（S1 x2 + T-mini x1）でのテストは、`config.toml` の
  `[[tmini.units]]` / `[[s1.pairs]]` を 1 段分だけ残す（もう 1 段の分をコメントアウト
  するか削除する）。
- 全トラッカー（T-mini 各台・S1 各ペア）が 0 個になったときだけ、原因の当たりをつけて
  終了する。

## 構成

| ファイル | 役割 |
|----------|------|
| `tracker1p3.py` | 本番のエントリポイント。`CloudFilter` / `HoldState` / `LidarReader` / `S1Pair` / `AveragingWriter` / `CombinedView` |
| `sensors.py` | `tools/lidar-tracker`・`tools/lidar-tracker3` の `lidar.py` を読み込む継ぎ接ぎ層 |
| `geometry.py` | 純ロジック（座標変換・平均・直線交点）。センサー・スレッドに非依存 |
| `config.py` | `config.toml` の読み込みと検証 |
| `config.example.toml` | テンプレート（`config.toml` は gitignore 済み） |
