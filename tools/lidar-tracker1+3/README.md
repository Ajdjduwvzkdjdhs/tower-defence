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
| 検出の隙間を埋める保持秒数 | `[hold]` の `shaving_hold_sec`。**Unity へ送る最終平均にのみ**効く（系統ごとの代表点はリアルタイム。下記「ホールドの効き方」参照） |
| 生角の向き合わせ | `[tmini]` / `[s1]` の `angle_offset_deg` / `flip_horizontal` / `flip_vertical`（種別ごとの既定値。台ごとに違うなら `[[tmini.units]]` の `angle_offset_deg` や `[[s1.pairs]]` の `angle_offset_a`/`angle_offset_b` で上書き） |

`port_a` は共有フレームの **−X 側**、`port_b` は **+X 側**に自動的に置かれる
（`(±baseline_mm/2, 0)`。設定は間隔だけでよい）。−X／+X は「**電源ポートを背にして
正面を向いたときの左／右**」と対にしてある（正面＝表示角 180°＝出力 +Y）ので、
**port_a＝左、port_b＝右**になるように挿す。逆に挿すと交点が左右反転した位置に出るので、
GUI で交点マーカーが実際の落氷位置とズレて見えたら `port_a`/`port_b` を入れ替える。

`baseline_mm` は **S1 2 台間の全長**（中心＝T-mini からの片道距離の 2 倍）。
「T-mini から片側 295mm ずつ」なら `baseline_mm = 590.0`。

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

- **台ごとの色**: 各物理LiDAR（T-mini 各台・S1 各生ユニット、最大 6 台）の点群を別々の色で
  描く（薄い点＝その台の全有効点、濃い点＝その台の関心円内・氷候補点）。右上に凡例。
  どの台が正しい向きを向いていないかを、単色（赤一色）ではなく色の位置で見分けるため。
  **凡例・左欄・ログに出る名前は config.toml の `name` ではなく実際のポート番号
  （例 `/dev/tty.usbserial-0001` → `0001`）で統一**してある。台数が多いと `name` の
  ラベルより現物のポート番号（`ls /dev/tty.usb*` で見えるもの）で照合する方が早いため。
- 橙の三角: T-mini 各台の代表点（点群平均）— 最大 2 個
- 紫の四角: S1 各ペアの交点（交点法の推定点）＋薄い点線 2 本（デバッグ用のレイ）— 最大 2 個
- 緑の輪: 最終的に Unity へ送る代表点（有効な代表点の平均）— 1 個
- 左の欄: 台・ペアごとの生存確認（検出点数・累計ヒット数・LIVE/hold 状態）
- 下端: いま Unity へ書いている値そのまま（`seq` / xy / shaving / 出力先パス）

### 台ごとに向き（`angle_offset_deg`）がズレているとき

色分けした GUI で、ある台の点群だけが物理的にありえない場所・向きに出ているなら、
その台の生角ゼロ点が他の台とズレている（エンコーダの個体差）。`[tmini]` / `[s1]` の
共通 `angle_offset_deg` とは別に、**台ごとに上書き**できる:

```toml
[[tmini.units]]
name = "TMini-A"
port = "..."
angle_offset_deg = 90.0   # この台だけ

[[s1.pairs]]
...
angle_offset_a = 0.0      # port_a 側だけ
angle_offset_b = 180.0    # port_b 側だけ
```

**具体的な角度の値は、現物を見ながらでないと決められない**（生角ゼロ点と筐体の向き
の関係はセンサー個体差があり、机上・想像では割り出せない）。合わせ方は
`tools/lidar-tracker3/valley_tracker.py` の `ANGLE_OFFSET_DEG` コメントと同じ考え方:
その台の正面（電源コネクタの逆側、など据え方で決めた基準）に向けて手や氷を出し、
この GUI でその台の色の点が関心円の位置（表示上は円の中）に出るまで、その台の
`angle_offset_deg` を回す。台ごとに色が分かれているので、複数台を同時に見ながら
1 台ずつ合わせられる。

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

## ホールドの効き方（`shaving_hold_sec` はどこに効くか）

**`shaving_hold_sec` は、Unity へ送る最終的な 1 点にだけ効く。各系統（T-mini 各台の
代表点・S1 各ペアの交点）自身はリアルタイムで、保持しない。**

以前は各系統の代表点そのものを hold_sec ぶん保持していたが、それだと**片方の系統が
検出できていない間、その系統の古い代表点が生き残ったまま、もう片方のいま正しく
検出できている系統の値と平均されてしまい、正しい方の値が引っ張られる**という不具合が
実機で確認された（2026-09）。

いまの動き:

1. 各系統（`LidarReader` / `S1Pair`）の代表点は、**直近 1 回転の結果をそのまま**返す。
   その回転で検出できなければ、間を置かず `None` になる（保持しない）。
2. `AveragingWriter` が 50ms ごとに、**その瞬間**検出できている系統だけを集めて平均する
   （1 個も無ければ瞬間平均は無し）。
3. **この瞬間平均だけ**を `shaving_hold_sec` の間保持し、それを Unity へ送る
   （直前に出せていた最終平均を延命する。1 個も検出が無いまま hold_sec を過ぎたら
   検知なし＝`seq,,,0`）。

「削るのを止めてから雪崩が止まるまでの遅延」という当日の主要ノブの意味はそのまま
最終出力に残るが、系統間の引っ張り合いは起きない。

## 起動順序について（S1 が「応答ヘッダが不正」で失敗するとき）

**必ず S1 を先に、T-mini を後に開く（`main()` で固定済み。変えないこと）。**

T-mini は公式 SDK（コンパイル済み C++、内部で独自に USB/シリアルへ触る）を使い、
1 台の起動だけで 2.5〜3.5 秒の重いやりとりをする（`Lidar successfully connected` →
`health` 確認 → `Successed to start the lidar` の一連）。**この T-mini の起動処理の
直後に別のシリアルポートを新規に open すると、そのポートが「応答ヘッダが不正です
（00000000000000）」で壊れる**現象が実機ログで高い再現性で確認されている
（T-mini が 2 台ともフル起動した直後は毎回発生。1 台だけ／どちらも一瞬で失敗した
ときは発生しない＝T-mini 側の SDK 処理時間の長さと相関）。S1 は自前の軽量プロトコル
実装なので、後から重い T-mini の処理が来ても影響を受けにくい。**S1・T-mini それぞれの
本体故障ではなく、同一プロセス内でのポートを開く順序の問題**なので、もし将来
コードをいじって順序が入れ替わったら、まずここを疑うこと。

T-mini 自体の起動時に出る `internal error[0x202]`（health status bad）は上記とは別の、
無害な一過性エラー。毎回ほぼ確実に自己回復し、その後は継続して正常動作する。

## 構成

| ファイル | 役割 |
|----------|------|
| `tracker1p3.py` | 本番のエントリポイント。`CloudFilter` / `HoldState` / `LidarReader` / `S1Pair` / `AveragingWriter` / `CombinedView` |
| `sensors.py` | `tools/lidar-tracker`・`tools/lidar-tracker3` の `lidar.py` を読み込む継ぎ接ぎ層 |
| `geometry.py` | 純ロジック（座標変換・平均・直線交点）。センサー・スレッドに非依存 |
| `config.py` | `config.toml` の読み込みと検証 |
| `config.example.toml` | テンプレート（`config.toml` は gitignore 済み） |
