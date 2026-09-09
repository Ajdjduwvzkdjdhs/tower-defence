# Phase preview-feedback — LiDAR を連続座標入力に置換し、検知点から雪玉を放出する

ブランチ: `preview-feedback`。内覧会（プレビュー）フィードバック対応。
本ドキュメントは Q&A で確定した仕様と実装手順。**未確定事項は末尾「実装中に詰める」にまとめてある。**

---

## 0. ゴール（1 段落）

LiDAR が Unity に送る値を「4 谷のどれか（`channel`）」から「氷が落ちた**連続座標 (x, y)**」へ完全に置換する。
Unity は受け取った座標を山上のワールド位置 P に写し、**P から山の中心を通る放射方向へ雪玉ストリームを放出**する。
雪玉は物理を使わず（ADR-11/21/25/27 踏襲）、スクリプト滑走中に**敵の近く（XZ で一定半径内）を最初に通った 1 体を撃破**する。
撃破レートと雪玉の拡散幅は、**検知点の山中心からの距離 d に対して線形**に変化する（中心＝広く弱い / 外周＝狭く強い）。
実機ズレ用に、**キー長押しで山中心オフセットを平行移動キャリブレーション**できる（角度・回転・反転は設置時に config で一度確定して以後固定）。

谷選択（`ActiveChannel`）の概念は LiDAR 経路では廃止。開発時はゲーム画面のマウス操作（またはキー）で検知点を疑似入力する。

---

## 1. データ契約（LiDAR → ファイル → Unity）

### 1.1 座標系

- LiDAR の生値: `angle`[deg]（プロット表示角。`_display_angle` = `(raw + ANGLE_OFFSET_DEG) % 360`、既定 `ANGLE_OFFSET_DEG = 0`）、`distance`[mm]。
- **変換**: `x = r·cos(θ)` , `y = −r·sin(θ)` （θ = 表示角[deg]、r = distance[mm]）。
  - θ=0° → +X 方向、θ=270° → +Y 方向。
  - 物理配置: 山の中心は LiDAR の 270° 方向へ約 300mm。よって山中心に落ちた氷は概ね `(x, y) ≈ (0, +300)` になる。
- **単位は全経路 mm で統一**（Python 出力・config とも mm）。

### 1.2 関心円（Python が持つ）

- 中心 `center_offset_mm`（既定 `[0, 300]`、上記フレーム）。
- 半径 `radius_mm`（既定 `300` ＝ 直径 60cm）。
- はみ出し許容 `tolerance_mm`（既定 `100`）。**フィルタは「中心からの距離 ≤ radius_mm + tolerance_mm（＝既定 400mm）」で通す。**
- 円の外の氷点は Unity に送らない。

### 1.3 代表点

1 回転ぶん（2 台の検出を単純に足し合わせた集合）で、円内に氷点が複数あるときは
**円中心（山の中心）に最も近い 1 点**を代表とする。

> 2 台の LiDAR は垂直に積んで設置し、システム上は同一座標系。`valley_tracker.py` は両台の氷点を 1 つのリストに
> 結合してから円フィルタ・代表点選択を行う（現行の per-unit 分類・OR は不要になる）。

### 1.4 出力ファイル 1 行

- 形式: `seq,x,y,shaving`
  - `seq` … 単調増加（現行どおり。停止でロスト検知）。
  - `x,y` … 代表点の LiDAR 原点フレーム座標[mm]（整数 or 小数）。
  - `shaving` … `0/1`。現行の `shaving_hold_sec` ホールドをそのまま適用。
- **検知なし**（円内に氷なし、かつ shaving ホールド切れ）: `seq,,,0`（x,y 空欄）。
- **shaving ホールド中**（直近に氷を見たが今回転は無し）: 直近の x,y を `shaving=1` で継続送信（ストリームを直近点から出し続ける）。
- 旧 `seq,channel,shaving` / position-tracker の 2 列契約は**廃止**（LiDAR 経路の完全置換）。

---

## 2. Python 側（`tools/lidar-tracker/`）

### 2.1 新 config `tools/lidar-tracker/preview.toml`（新規・専用ファイル）

```toml
[circle]
# 変換フレーム (x = r·cosθ, y = −r·sinθ) での関心円。単位 mm。
center_offset_mm = [0.0, 300.0]   # 山の中心（LiDAR の 270° 方向へ 300mm）
radius_mm        = 300.0          # 直径 60cm
tolerance_mm     = 100.0          # はみ出し許容（フィルタは radius+tolerance で通す）

[representative]
mode = "nearest_center"           # 現状これのみ（円中心に最も近い氷点）
```

`config.py` に読み込みを追加（`config.example.toml` に対応する `preview.example.toml` も置く。`preview.toml` は
`.gitignore` に追加＝各マシン設定）。

### 2.2 `valley_tracker.py` の改修

- `IceClassifier`（谷ごとの極座標矩形判定）→ **`IcePointExtractor`** に置換:
  1. `_is_valid` + `intensity < intensity_max` の氷候補点を全台ぶん収集。
  2. 各点を `(x, y)` へ変換（`x = r·cosθ, y = −r·sinθ`、θ は `_display_angle`）。
  3. `dist_to_center = hypot(x - cx, y - cy)` が `radius_mm + tolerance_mm` 以下の点だけ残す。
  4. 残った点のうち `dist_to_center` 最小の 1 点を代表点として返す（無ければ `None`）。
- `[valleys.*]`・`max_points_per_valley`・`channel_hold_sec` は不要になる（削除 or 無視）。
  `intensity_max` / `shaving_hold_sec` / `range_*` / `frequency_hz` は残す。
- `UnitState` / `PositionWriter`:
  - 保持するのは「直近の代表点 (x,y) とその時刻」。
  - `resolve()` は `(x, y, shaving)` を返す。`shaving = age <= shaving_hold_sec`。
  - 代表点の age が `shaving_hold_sec` 超で「検知なし」＝ `x,y` 空欄。ただし shaving ホールド中は直近 `x,y` を継続。
  - 書き込み行: `f"{seq},{x},{y},{1 if shaving else 0}\n"`（x,y 空欄時は `f"{seq},,,0\n"`）。原子的 rename は現行どおり。
- 可視化（`run_plot`）: 谷の塗り分けを廃し、関心円（radius / radius+tolerance の 2 円）と代表点マーカーを描く。
  ステータス行は `seq / x / y / shaving` 表示に変更。台数ぶんの段組みは維持。
- README・`環境構築について/LiDAR環境設定メモ.md` の該当箇所を更新（谷設定 → 円設定）。

### 2.3 実装状況（2026-08-28 時点）

- **Unity 側（§3・§5 の 3〜10）は実装・検証済み**（EditMode 187本＋PlayMode 2本 緑、`unity command` で確認）。
  `_inputMode = SimPointer` にすればマウスで新経路をすぐ試せる（LiDAR 不要）。
- **Python 側（この §2）は未着手**。`tools/lidar-tracker/preview.example.toml` のみ用意済み（`.gitignore` に `preview.toml` 追加）。
  `valley_tracker.py` は 900行・スレッド＋matplotlib＋SDK 依存で、**実機なしでは検証できない**ため、
  ハードウェアのある回で下記の関数単位パッチを当てる:

| 対象 | 変更 |
|---|---|
| 設定読み込み（`config.py` / モジュール冒頭） | `preview.toml` を読む `load_preview_config()` を追加。`CIRCLE_CENTER_MM`（tuple）・`CIRCLE_RADIUS_MM`・`CIRCLE_TOLERANCE_MM`・`REPRESENTATIVE_MODE` を公開。`VALLEYS = load_valleys_config()` と `MAX_POINTS_PER_VALLEY` / `CHANNEL_HOLD_SEC` は撤去可（残しても未使用）。 |
| `IceClassifier.classify(samples)` | 返り値を `(channel, counts, ice_points)` → `(point_or_None, ice_points)` に。中身: `_is_valid` かつ `intensity < INTENSITY_MAX` の各 `p` を `theta=_display_angle(p["angle"])`, `r=p["distance"]` で `x=r*cos(radians(theta))`, `y=-r*sin(radians(theta))` に変換。`hypot(x-cx, y-cy) <= CIRCLE_RADIUS_MM + CIRCLE_TOLERANCE_MM` の点だけ残し、その中心距離が最小の 1 点を返す（無ければ `None`）。`_pick` / `_last_channel` は削除。 |
| `UnitState` | `self._channel` → `self._point`（`(x,y)` or `None`）。`publish(point, ice_points, all_points, valid_count)`。`detection()` は `(point, detected_at)`。`point is not None` のときだけ `_detected_at` を更新（ホールドの実体）。 |
| `reader_loop` | `classifier.classify` の新シグネチャに合わせ、`point` を `state.publish` へ。ログは `ice -> (x, y)` 形式。 |
| `PositionWriter` | `_channel_hold` / `_pick` 系を撤去。`resolve()`: 全台の `detection()` を突き合わせ、`age = now - detected_at`。`age <= SHAVING_HOLD_SEC` なら `shaving=True` かつ直近 `point` を継続。`point is None` かつ `age > SHAVING_HOLD_SEC` なら「検知なし」。`run()` の行組み立て: `x,y = point if (point and shaving系条件) else ("","")` → `f"{seq},{x},{y},{1 if shaving else 0}\n"`（原子的 rename・seq 単調増加は不変）。 |
| `LidarUnit.build` / `refresh` / `run_plot.update` | 谷の扇形塗り（`_valley_fills` / `valley_spans`）を撤去し、関心円 2 本（`CIRCLE_RADIUS_MM` と `+TOLERANCE`）＋代表点マーカーを描く。極座標プロットのままなので、円は `theta=linspace(0,2π)` で半径一定の線として描ける（`x=r*cosθ,y=-r*sinθ` の中心オフセットは Cartesian になるので、`ax.plot` を Cartesian サブプロットに変えるか、中心が原点近似なら極座標のまま円で可）。ステータス行を `seq / x / y / shaving` に。 |
| 2 台の扱い | 現行は台ごとに `classify` → `PositionWriter` が突き合わせ。連続座標では **全台の氷点を 1 リストに結合してから円フィルタ・代表点選択**する方が素直（メモ「2台は垂直に積む＝同一座標系・単純に足し合わせ」より）。`reader_loop` を台ごとに回すのは維持しつつ、`PositionWriter.resolve` で全 `UnitState` の「直近点」から中心最近傍を 1 つ選ぶ形でよい。 |

> 変換の健全性チェック: 氷を山の物理中心へ落として `valley_tracker.py` のログ `(x, y)` が概ね `(0, +300)` 付近になること。
> ずれるなら `ANGLE_OFFSET_DEG`（LiDAR の物理向き）か `center_offset_mm` を現物合わせ。左右が反転していたら Unity 側
> `LidarPreviewConfigSO._flipX/_flipY`（設置時に一度確定）で直す。Python 側の符号はいじらない。

---

## 3. Unity 側

### 3.1 新規: 位置検知プロトコル（純ロジック・EditMode 対象）

`Assets/Scripts/Input/LidarPositionProtocol.cs`（`VisionInputProtocol` と対称）:

- `Ingest(string line)` … `seq,x,y,shaving` を厳格パース。x,y 空欄は「検知なし」。seq 変化時のみ新鮮扱い。
- `Tick(float dt)` … watchdog（seq 停滞 → ロスト）。ロスト時は検知なし・shaving=false へフェイルセーフ。
- 公開: `bool HasPoint`（検知あり）、`(float x, float y) RawPointMm`（LiDAR 原点フレーム mm）、`bool IsShaving`、`bool IsLost`。
- ラッチ/デバウンス/平滑化は入れない（連続座標・**生値で使う**。氷点のフレーム間ジッタが問題になったら別途検討）。

### 3.2 新規: 入力ソース（MonoBehaviour・IO アダプタ）

`Assets/Scripts/Input/LidarPositionInputSource.cs`:

- `VisionInputSource` と同じくファイルを間隔ポーリング（既定 50ms）→ `LidarPositionProtocol` へ流す。
- パス解決順は現行踏襲（env `KAKIGORI_POSITION_FILE` → `config.json` → Editor 相対 `tools/lidar-tracker/out/position.txt` / ビルド `persistentDataPath`）。
- **座標写像**（このクラス or 専用ヘルパー `LidarPointMapper`）:
  1. `RawPointMm (x,y)` から `center = center_offset_mm + fieldOffsetMm`（PlayerPrefs・§3.9）を引く → `local_mm`。
  2. `world_rotation_deg` 回転 → `flip_x` なら x 反転 → `flip_y` なら y 反転。
  3. `scale_units_per_mm` を掛ける → `(offX, offZ)` = 山中心（原点）からのゲームワールド XZ オフセット（既定 `x→X, y→Z`）。
  4. `P = new Vector3(offX, HeightAt(offX, offZ), offZ)`。
  5. `d_mm = clamp(|local_mm|, 0, radius_mm)`（＋許容帯の点は `d_mm = radius_mm` にクランプ）。
- 公開: `Vector3? DetectedWorldPoint`、`float DetectionDistanceMm`（＝ d_mm）、`bool IsShaving`、`bool IsLost`。

### 3.3 新規: 疑似入力（開発用）

`Assets/Scripts/Input/SimPositionInputSource.cs`（同じ公開契約）:

- **マウス**: ゲーム画面上で左ボタン押下中、スクリーンレイ → 接地面（`_groundSurface` / Terrain）ヒット点を `DetectedWorldPoint` に、押下中 `IsShaving=true`。
- **フォールバック**: 矢印キーで検知点を平面移動、`Space` で `IsShaving`。
- `d_mm` は `DetectedWorldPoint` から逆算（`|P.xz| / scale_units_per_mm`、clamp 0..radius_mm）。

### 3.4 新規: 雪玉ストリーム放出＋撃破

`Assets/Scripts/Gameplay/LidarAvalancheVfx.cs`（現 `AvalancheVfx` を置換 or 併存）:

- 依存: 位置入力ソース（`DetectedWorldPoint` / `DetectionDistanceMm` / `IsShaving`）、`HeightAt`、`Snowball` プール、
  敵レジストリ（3.6）、`LidarPreviewConfigSO`。
  （**氷消費は当面無効＝氷は無限。** 3.10 参照。`IceStock` には触らない。）
- `Update()`:
  - `IsShaving` かつ `DetectedWorldPoint != null` でない限り射出しない（accum を 0 に戻す）。
  - `P = DetectedWorldPoint`、`d = DetectionDistanceMm`。
  - `radialDir = normalize(P.xz)`（山中心→P 方向）。P が中心ちょうど（`|P.xz|≈0`）のときは前フレームの向き or ランダム方位。
  - `startRadius = |P.xz|`（ゲーム単位）。
  - **拡散＝放射方向に対する角度ばらつき**（横オフセットではない）:
    `spreadDeg(d) = lerp(spread_center_deg, spread_edge_deg, d / radius_mm)`。
  - `_spawnAccum += spawn_rate * dt`。1 以上ぶん `Spawn()`：
    `a = Random(-spreadDeg, +spreadDeg)`、`dir = Quaternion.AngleAxis(a, Vector3.up) * radialDir`。
  - `Snowball.Launch(dir, startRadius, lateral: 0, end_radius, HeightAt, pool.Release, tryKillNear)`
    （`lateral` は使わず、方位角そのものを回して拡散を表現する）。
- **撃破コールバック `tryKillNear(Vector3 worldPos)`**（雪玉が毎フレーム自分の現在位置で呼ぶ）:
  - グローバルな `_lastKillTime` ゲート: `Time.time - _lastKillTime < cooldown(dLive)` なら false。
    `cooldown(d) = lerp(cooldown_center_sec, cooldown_edge_sec, d / radius_mm)`（既定 0.6 → 0.0、中点 0.3）。
    `dLive` ＝ **今フレームの検知点の d**（＝クールダウンは雪玉を放った位置ではなく“現在の検知点”に依存する。
    既知の割り切り・今回はこれで良しと確認済み）。検知が切れていたら射出済み雪玉は直近の d を使う。
  - `worldPos.xz` から `kill_radius_units` 内の**生存敵で最も近い 1 体**をレジストリから取得。
  - 取得できたら `enemy.Kill()`、`_lastKillTime = Time.time`、`OnSnowballKill?.Invoke(enemy)` を発火、true を返す
    （雪玉は 1 撃で終了：既存 `_reachedFront` フラグ流用）。
  - 取得した敵が `AllyNPC` なら味方巻き込み扱い（`OnAllyCaught` 相当＝ペナルティ `_allyHitPenalty`）。
  - `DisguisedEnemy` は実敵として撃破（現行の無ペナ撃破を踏襲）。

### 3.5 `Snowball.cs` の拡張

- 現 `Launch(... frontRowDistance, onReachFrontRow)` に加え、**汎用撃破オーバーロード**を追加:
  `Launch(dir, startRadius, lateral, endRadius, heightAt, onDone, Func<Vector3,bool> tryKillNear)`。
  新経路は `lateral = 0` で呼び、拡散は `dir`（方位角を回した放射方向）で表現する。
- `Update()` 内、`!_reachedFront && tryKillNear != null` なら毎フレーム `tryKillNear(transform.position)` を呼び、
  true が返ったら `_reachedFront = true`（1 雪玉 1 撃）。それ以外の滑走・返却ロジックは不変。
- 既存の谷版 `Launch` は残す（キーボード旧経路を残す判断をした場合のため）。

### 3.6 新規: 敵レジストリ

`Assets/Scripts/Gameplay/EnemyRegistry.cs`（軽量・Singleton ではなく GameManager が配線）:

- 4 つの `ChannelController` を保持し、`Enemy TryGetNearestLive(Vector3 worldPos, float radius)` を提供
  （各 `EnemyMatrix<Enemy>` を走査し `IsAlive` かつ XZ 距離最小、`radius` 内）。
- または `ChannelController` に `IEnumerable<Enemy> LiveEnemies` を足して `LidarAvalancheVfx` 側で最近傍探索。
  → 実装時に軽い方を選ぶ。毎フレーム × 雪玉数なので、敵数（最大 48 前後）なら素朴走査で十分。

### 3.7 撃破音（`AudioService`）

- 現行の「Monster」SE は `ChannelController.OnEnemyKilled`（行単位）で鳴る。新経路は行消去を通らないため、
  `LidarAvalancheVfx.OnSnowballKill` を `GameManager` で購読して同じ SE を鳴らす（味方巻き込みは AllyCaught SE）。
- 雪崩ループ音（`OnAvalancheChanged` エッジ）: `LidarAvalancheVfx` にも同等のエッジ通知を持たせて配線。

### 3.8 config（Unity 側）

新規 `Assets/Scripts/Data/LidarPreviewConfigSO.cs` ＋ `Assets/ScriptableObjects/LidarPreviewConfig.asset`:

| フィールド | 既定（暫定） | 用途 |
|---|---|---|
| `_scaleUnitsPerMm` | `0.0333`（＝ 10 / 300mm） | mm → ゲーム単位。円の縁がゲーム r=10（雪崩終端）想定の暫定値。現場調整。 |
| `_worldRotationDeg` | `0` | 設置時に一度だけ確定。以後固定。 |
| `_flipX` | `false` | 設置時確定（プロジェクタ鏡像 / モニタ直で切替）。 |
| `_flipY` | `false` | 同上。 |
| `_centerOffsetMm` | `(0, 300)` | Python の `center_offset_mm` と一致させる（キャリブの基準原点）。 |
| `_radiusMm` | `300` | d のクランプ上限（Python の `radius_mm` と一致）。 |
| `_killRadiusUnits` | `0.6` | 雪玉→敵 撃破半径（XZ）。 |
| `_cooldownCenterSec` | `0.6` | d=0（中心）での撃破クールダウン。 |
| `_cooldownEdgeSec` | `0.0` | d=radius_mm（外周）での撃破クールダウン。 |
| `_spreadCenterDeg` | `25` | d=0 での放射方向からの角度ばらつき（±deg・広い）。 |
| `_spreadEdgeDeg` | `3` | d=radius_mm での角度ばらつき（±deg・狭い）。 |
| `_spawnRate` | `24` | 1 秒あたり雪玉数（現 `AvalancheVfx` 踏襲）。 |
| `_endRadius` | `10` | 滑走終了半径（現 `AvalancheVfx` 踏襲）。 |
| `_consumeIce` | `false` | 氷消費の有効/無効。**既定 false＝氷は無限**（3.10）。 |
| `_fieldOffsetStepMm` | `10` | キャリブ（`field_offset`）の 1 押しの移動量。 |

> `_centerOffsetMm` / `_radiusMm` は Python `preview.toml` と二重定義になる。Python 側が物理の真実（円フィルタ）、
> Unity 側はキャリブの基準。両者は一致させ、小さなズレは `tolerance_mm`（+100mm）で吸収する。

### 3.9 キャリブレーション（TH プレイ中）

`Assets/Scripts/Gameplay/LidarCalibration.cs`（or `UIManager` に統合）。アクション名 **`field_offset`**（山中心の平行移動オフセット）:

- キー `C` を**長押し中**だけ照準オーバーレイを表示:
  - ワールド原点（山の中心）に十字。
  - 受信中の代表点をキャリブ適用「前」「後」でそれぞれマーカー表示（ズレが見える）。
  - 現在の `fieldOffsetMm` を数値表示。
- 長押し中、矢印キーで `fieldOffsetMm` を `_fieldOffsetStepMm`（既定 **10mm**）ずつ加減（x/y）。角度・回転・反転は触らない。
- キーを離したら `PlayerPrefs`（キー: `kakigori.lidar.field_offset.x` / `.y`）へ保存。起動時に読み込み。
- リセット（`C` 長押し中に `Backspace`）で `fieldOffsetMm = (0,0)`。
- IMGUI で可（現 `UIManager` と同方式）。
- §3.2 の写像で引く中心は `center_offset_mm + fieldOffsetMm`。

### 3.10 GameManager 配線 / InputMode

- `InputMode` に `LidarPosition` を追加（or 既存 `Vision` を置換）。`_inputMode` で
  `Keyboard`（開発・後述）/ `LidarPosition`（本番）/ `SimPointer`（開発・マウス疑似）を切替。
- `LidarPosition` / `SimPointer` のとき: `LidarAvalancheVfx` を生成・`Configure`、`LidarCalibration` を配線、
  旧 `AvalancheVfx` は生成しない。`AvalancheController` はこの経路では使わない（`ActiveChannel`・氷ゲート不要）。
- **氷消費は当面無効＝氷は無限**（`_consumeIce = false`）。理由: システム側のリソース管理を好まない方針。
  - `IceStock` / `AvalancheController` の氷ドレインコードは**消さずに残す**（保険）。`LidarAvalancheVfx` は
    `_consumeIce` が true のときだけ `IceStock.TryDrain` で射出をゲートする分岐を持つ（既定 false で素通り）。
  - UI の氷バーは `LidarPosition`/`SimPointer` 時は非表示 or 満タン固定。
  - **「氷を完全排除して」の指示が来たら**: `_consumeIce` 分岐・`IceStock`・`AvalancheController` の氷まわり・
    UI 氷バー・`WaveConfigSO` 等の氷パラメータを一括削除（Core の `IceStock` と `IceStockTests` も対象）。
    それ以外の Core・撃破・敵は無関係なので影響は局所。
- `Keyboard`（旧 4 谷 W/A/S/D + Space + 旧 `AvalancheVfx`）: **当面は残置**（`_inputMode` で選ぶだけ）。
  新モデルが現場で通ったら別コミットで削除。

---

## 4. 保持 / 置換 / 削除

| 区分 | 対象 |
|---|---|
| **保持（無改修）** | Core 全部（`EnemyMatrix` / State / `Scoring` / `PenaltyTally` / 各 Clock）、EditMode 151 本、`ChannelController` の敵スポーン・行列（4 方向の敵配置は不変）、`Enemy`/`AllyNPC`/`DisguisedEnemy`、`Fort`、`WaveManager`、`Projectile`、`ProjectileFactory`、物理不使用。 |
| **拡張** | `Snowball`（汎用撃破オーバーロード追加）、`ChannelController` or 新 `EnemyRegistry`（最近傍生存敵）、`AudioService` 配線（新撃破イベント）、`InputMode`、`GameManager` 配線。 |
| **新規** | `LidarPositionProtocol`（+EditMode テスト）、`LidarPositionInputSource`、`SimPositionInputSource`、`LidarAvalancheVfx`、`LidarPreviewConfigSO`＋`.asset`、`LidarCalibration`、`preview.toml`（Python）。 |
| **置換 / 廃止（LiDAR 経路）** | `VisionInputProtocol`/`VisionInputSource`/`VisionConfigSO` の channel セマンティクス、`valley_tracker.py` の谷分類、`config.toml` の `[valleys.*]`、出力 `seq,channel,shaving`。 |
| **残置（判断保留）** | 旧 `AvalancheVfx`（谷ストリーム）、キーボード W/A/S/D 谷選択経路。新モデル確認後に削除。 |

---

## 5. 実装順（各ステップで `unity command` 検証）

> 検証は `unity` CLI（`docs/... CLAUDE.md「Unity Editor 連携」`）。`.cs` 編集 → `unity command recompile` →
> `recompile_status` ポーリング → `unity command run_tests --mode editor` / `screenshot`。

1. **Python: `preview.toml` + `config.py` 読み込み**（実機なしで単体確認）。
2. **Python: `valley_tracker.py` を座標抽出＋円フィルタ＋`seq,x,y,shaving` 出力に改修**。
   `--sim` 相当 or 録画データで、出力行の形・seq 単調増加・空欄パースを確認。可視化を円表示に。
3. **Unity: `LidarPositionProtocol` + EditMode テスト**（`VisionInputProtocolTests` に倣う。パース・ロスト・空欄・
   shaving 継続）。`unity command run_tests --mode editor --filter LidarPositionProtocol`。
4. **Unity: `LidarPreviewConfigSO` + `.asset`**（暫定値）。
5. **Unity: `SimPositionInputSource`（マウス）**＋ `GameManager` 配線（`InputMode.SimPointer`）。
   まだ雪玉は旧経路のままでも可。
6. **Unity: `EnemyRegistry`（or ChannelController 拡張）＋ `Snowball` 汎用撃破オーバーロード**。
   `unity command run_tests`（回帰ゼロ）。
7. **Unity: `LidarAvalancheVfx`（放出＋汎用撃破＋動的 cooldown/spread）**。`GameManager` で `SimPointer` 時に配線。
   Play で `screenshot`：マウス押下位置から放射状に雪玉、通過した敵が消える、中心付近は広く遅く・外周は狭く速く。
8. **Unity: 撃破音・雪崩ループ音の再配線**（`OnSnowballKill` / エッジ）。Play で SE 発火を `get_console_logs` 等で確認。
9. **Unity: `LidarCalibration`（長押し照準＋矢印 nudge＋PlayerPrefs）**。Play で `screenshot`：長押しで照準、nudge で
   マーカー移動、離す→再生で復元。
10. **Unity: `LidarPositionInputSource`（実ファイル）**＋ `InputMode.LidarPosition`。
    `tools/lidar-tracker/out/position.txt` を手書き / Python から流して追従・ロスト・座標写像を確認。
11. **実機 end-to-end**（現場）: 円設定・`scale`・`world_rotation_deg`/`flip` を詰め、キャリブで中心を合わせる。
12. **回帰**: `unity command run_tests --mode editor`（151 本＋新規、緑）。旧 `AvalancheVfx`・キーボード経路の
    残置に問題がないこと。`docs/architecture.md` に **ADR-29**（LiDAR 位置入力・雪玉放出モデル）を追記。

---

## 6. 確定事項（Q&A で決定済み）

| 項目 | 決定 |
|---|---|
| `d` の許容帯 | `radius_mm < 距離 ≤ radius_mm + tolerance_mm` の点は `d = radius_mm` にクランプ（cooldown=edge・spread=edge）。**外挿しない**（許容誤差で拾っただけなので外周より強く/広くはしない）。 |
| cooldown ゲート | グローバル単一 `_lastKillTime` ＋ライブ d の動的間隔。**クールダウンが“現在の検知点”に依存する割り切りは承知の上で採用**（当面これで良い）。 |
| spread の意味 | **放射方向に対する角度ばらつき（±deg）**。横オフセットではない。`Quaternion.AngleAxis(Random(-spreadDeg, spreadDeg), Vector3.up) * radialDir`。config は `_spreadCenterDeg` / `_spreadEdgeDeg`。 |
| 暫定数値 | そのまま暫定値で入れる。現場調整。 |
| 氷消費 | **無効＝氷は無限**（`_consumeIce = false`）。ドレインコードは保険として残す。「完全排除」指示で一括削除（§3.10 に手順）。 |
| キャリブ | アクション名 `field_offset`、`C` 長押し＋矢印キー、step **10mm**、PlayerPrefs `kakigori.lidar.field_offset.x/.y`。角度・回転・反転は触らない。 |
| 旧経路 | 残置のまま新モデルを現場確認 → **別コミットで削除**。 |
| `ANGLE_OFFSET_DEG` | 既定 0 のまま。現場で回す場合は変換式の θ にそのまま含まれる。 |
| 座標平滑化 | **入れない（生値）**。ジッタが問題化したら別途。 |

---

## 7. セッション / 引き継ぎについて

本ドキュメントは単体で実装に入れるだけの粒度で書いてある。**このまま現セッションで実装を続けるのを推奨**
（Q&A で確定したニュアンスがコンテキストに乗っている／トークン残量は十分）。
新セッションに切り替える場合は、本ファイル＋以下を読めば復帰できる:

- `CLAUDE.md`「Unity Editor 連携」節（`unity` CLI での検証手順）
- `CLAUDE.md`「入力アーキテクチャ」節＋本ファイル §1〜4（現行 Vision 経路と置換範囲）
- `Assets/Scripts/Input/VisionInputProtocol.cs` / `VisionInputSource.cs`（対称に作る参照実装）
- `Assets/Scripts/Gameplay/{Snowball,AvalancheVfx,AvalancheController,ChannelController}.cs`（放出・撃破の現行）
- `tools/lidar-tracker/valley_tracker.py`（`IceClassifier` → `IcePointExtractor` 置換対象）
- `docs/architecture.md` ADR-24/25/27（ファイル経由 Vision 入力・雪玉ストリーム・撃破駆動の経緯）
