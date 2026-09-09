# preview-feedback ブランチ 引き継ぎ

内覧会フィードバック対応。**このブランチ = LiDAR を連続座標入力に置換し、敵を 360 度化し、
検知点から雪玉を放出して重なり順に撃破する**大改修。ローカルで積み上げ、`origin/preview-feedback` へ push 済み
（PR なし・main 未マージ）。

このドキュメントはセッション引き継ぎ用。実装計画の正本は `docs/phase-preview-feedback.md`、
設計判断は `docs/architecture.md` **ADR-29**。

---

## 1. いまのアーキテクチャ（改修後）

```
LiDAR 実機
  └ tools/lidar-tracker/valley_tracker.py
      氷点 → x=r·cosθ / y=−r·sinθ [mm] へ変換 → preview.toml の関心円で絞り
      → 中心最近傍の代表点 → out/position.txt に "seq,x,y,shaving"（検知なしは "seq,,,0"）
          ↓ ファイル経由ポーリング（~50ms・原子的 rename）
Unity（TH.unity / TH_Stage.prefab の GameManager が唯一の Composition Root）
  ├ LidarPositionInputSource（実ファイル）／ SimPositionInputSource（マウス疑似・開発）
  │    = IPositionInputSource（HasPoint / WorldPoint / DistanceMm(=d) / IsShaving / IsLost）
  │    LidarPositionProtocol でパース＋watchdog、LidarPointMapper で mm→ワールド XZ 写像
  ├ LidarCalibration（LidarPosition 時のみ）: C 長押し＋矢印で field_offset（山中心の平行移動）を nudge、離すと PlayerPrefs 保存
  ├ EnemyField（360度の単一敵マネージャ）: 方位ランダム＋外周入場＋静止半径。ChannelController×4＋EnemyMatrix行＋EnemyRegistry を置換
  └ LidarAvalancheVfx: 検知点 P から山中心を通る放射方向へ雪玉ストリーム（角度拡散は d 連動）。
       各雪玉が EnemyField.TryKillNearest → EnemyKillSelection.SelectFront で
       「killRadius 内の候補から overlapRadius 内により古い生存敵がいる敵を除外し、残りの最近傍」を 1 体撃破。
       撃破クールダウンは d 連動（中心 0.6s → 外周 0.0s）。氷消費は無効（無限）。
```

- **d** = 検知点の中心距離[mm]（0..radiusMm クランプ）。中心＝広く弱い / 外周＝狭く強い。
- 物理不使用（Collider/Rigidbody なし）。雪玉はスクリプト滑走＋プール。
- `_inputMode`（`InputMode` enum）は入力ソース選択だけに縮小: `LidarPosition`(=2) / `SimPointer`(=3)。
  `Keyboard`(0) / `Vision`(1) は旧値（未処理・enum は serialize 互換のため残置）。

---

## 2. 変更・追加・削除スクリプト一覧

### 追加
| ファイル | 役割 |
|---|---|
| `Assets/Scripts/Input/LidarPositionProtocol.cs` | `seq,x,y,shaving` パーサ＋seq 停滞 watchdog（純ロジック・EditMode 13） |
| `Assets/Scripts/Input/LidarPointMapper.cs` | mm→ワールド XZ 写像（中心減算→d クランプ→回転→flip→scale。純関数・EditMode 9） |
| `Assets/Scripts/Input/IPositionInputSource.cs` | 連続座標入力の抽象 |
| `Assets/Scripts/Input/LidarPositionInputSource.cs` | 実ファイルをポーリング→写像。field_offset を PlayerPrefs 永続化 |
| `Assets/Scripts/Input/SimPositionInputSource.cs` | マウス左押下で接地面の点＝検知点／矢印+Space フォールバック（開発用） |
| `Assets/Scripts/Data/LidarPreviewConfigSO.cs` | 全調整値の SO。d 連動カーブ `CooldownSecFor` / `SpreadDegFor` も持つ（EditMode 5） |
| `Assets/Scripts/Gameplay/LidarAvalancheVfx.cs` | 放射状の雪玉ストリーム放出＋一般化位置到達撃破（旧 AvalancheVfx 置換） |
| `Assets/Scripts/Gameplay/LidarCalibration.cs` | field_offset のキー calib＋IMGUI 照準オーバーレイ |
| `Assets/Scripts/Gameplay/EnemyKillSelection.cs` | `SelectFront`（純関数）: 重なり→age で前列を選ぶ（EditMode 7） |
| `Assets/Scripts/Gameplay/EnemyField.cs` | 360度の単一敵マネージャ。spawn/kill/clear、OnEnemyKilled/OnAllyCaught |
| `Assets/ScriptableObjects/LidarPreviewConfig.asset` | 上記 SO の実体。GameManager に割当済み |
| `tools/lidar-tracker/preview.example.toml` | 関心円テンプレ（`preview.toml` は `.gitignore`） |
| `docs/phase-preview-feedback.md` / `docs/preview-feedback-handoff.md` | 実装計画 / 本書 |

### 変更
| ファイル | 変更 |
|---|---|
| `Assets/Scripts/Bootstrap/GameManager.cs` | 4谷ビルド・旧 AvalancheVfx/AvalancheController・照準インジケータ・Vision/Serial/Composite 入力を撤去。`KeyboardInputSource`（Enter専用）＋位置入力＋`EnemyField`＋`LidarAvalancheVfx` に。撃破音=`_field.OnEnemyKilled`、雪崩ループ音=`_lidarVfx.OnAvalancheChanged` |
| `Assets/Scripts/Gameplay/WaveManager.cs` | `ChannelController[]` ラウンドロビン → `EnemyField.SpawnEnemy/Ally/Disguised`（方位ランダム） |
| `Assets/Scripts/Gameplay/Snowball.cs` | 汎用撃破オーバーロード `Launch(dir,…,tryKillNear)` を追加（旧 valley 版 `Launch` は残置・未使用） |
| `Assets/Scripts/Core/EnemyMatrix.cs` | `Remove(item)`（単体除去）を追加（EditMode 4）。※ EnemyMatrix 自体は現在未使用 |
| `Assets/Scripts/UI/UIManager.cs` | 4谷ハイライト表示を撤去。`Configure(Fort, IceStock)` |
| `Assets/Scripts/Input/InputMode.cs` | `LidarPosition` / `SimPointer` を追加（`Keyboard` / `Vision` は残置） |
| `Assets/Scripts/Scenes/TitleScene.cs` / `ResultScene.cs` | ルート直下 → `Assets/Scripts/Scenes/` へ移動＋`namespace KakigoriTD.Scenes` |
| `tools/lidar-tracker/valley_tracker.py` | `IceClassifier`(谷分類) → `IcePointExtractor`(円内代表点)。出力 `seq,x,y,shaving`。プロットは関心円表示に |
| `tools/lidar-tracker/config.py` | `load_preview_config()` 追加 |
| `CLAUDE.md` / `docs/architecture.md` / `docs/phases.md` / `docs/mcp-guide.md` 周辺 | 実態に合わせて更新（別コミット群） |

### 削除
- `Assets/Scripts/Gameplay/ChannelController.cs`（4谷制御）
- `Assets/Scripts/Gameplay/EnemyRegistry.cs`（`IEnemyChannel` ごと）
- `Assets/Scripts/Gameplay/AvalancheVfx.cs`（旧・谷ストリーム）
- `Assets/Scripts/Gameplay/AvalancheController.cs`（氷消費＋ActiveChannel）
- `Assets/Tests/EditMode/EnemyRegistryTests.cs` / `Assets/Tests/PlayMode/LidarAvalancheVfxPlayTests.cs`
- `docs/CLAUDE.md`（ルート CLAUDE.md の古い重複）

### 残置（未使用・後で消せる）
`EnemyMatrix<T>`＋`EnemyMatrixTests` / `CompositeInputSource` / `Vision*` / `Serial*`＋各テスト /
`AvalancheClock`（Core・テスト済）/ `Snowball` の旧 valley `Launch` オーバーロード。

---

## 3. 調整値（コンフィグ）の場所

- **Python**: `tools/lidar-tracker/preview.toml`（関心円: `center_offset_mm` / `radius_mm` / `tolerance_mm`）、
  `tools/lidar-tracker/config.toml`（`[serial]` / `[scan]` / `[detection]` / `[ice] intensity_max` / `shaving_hold_sec`。
  `[valleys.*]` / `max_points_per_valley` / `channel_hold_sec` は連続座標モードでは未使用）。両方 `.gitignore` 済み＝各自 `cp *.example.toml`。
- **Unity**: `Assets/ScriptableObjects/LidarPreviewConfig.asset`（`GameManager._lidarPreviewConfig` に割当済み）。
  `_scaleUnitsPerMm` / `_worldRotationDeg` / `_flipX` / `_flipY` / `_centerOffsetMm` / `_radiusMm` /
  `_entranceRadius` / `_restRadius` / `_entranceSpeed` / `_maxEnemies` /
  `_killRadiusUnits` / `_overlapRadiusUnits` / `_cooldownCenterSec`(0.6) / `_cooldownEdgeSec`(0) /
  `_spreadCenterDeg`(25) / `_spreadEdgeDeg`(3) / `_spawnRate` / `_endRadius` / `_consumeIce`(false) /
  `_fieldOffsetKey`(C) / `_fieldOffsetStepMm`(10) / `_pollIntervalSec` / `_lostTimeoutSec`。
  現物調整で `_flipY=1`, `_killRadiusUnits=1.5` に変更済み。

---

## 4. 検証状況

- **EditMode 189 本 緑**（`unity command run_tests --mode editor`）。純ロジックは網羅:
  `LidarPositionProtocol` 13 / `LidarPointMapper` 9 / `LidarPreviewConfigSO` 5 / `EnemyMatrix.Remove` 4 / `EnemyKillSelection` 7 ＋既存。
- **edit-mode `eval` スモーク**で `EnemyField` の spawn(6)/kill(→5)/ClearAll(→0) が統合状態で動作確認。
- **Python: 実機（YDLIDAR T-mini Plus）で headless 起動を確認**。出力形式・seq 単調増加・座標変換（山中心 → `(0,+300)`）検証済み。
- **未検証（要 Play / 現場）**:
  - `GameManager.Awake` の実 Play 実行（TH シーンで目視。console エラーが出ないこと）
  - 敵の 360 度スポーン・重なり古い順の撃破の見た目
  - LiDAR 実機 end-to-end（`valley_tracker.py` → Unity）
  - `preview.toml` / `config.toml` / `LidarPreviewConfig` の現物合わせ（`intensity_max`・円・scale・flip）
  - キャリブ（`C` 長押し）の IMGUI 照準

---

## 5. ハマりどころ（次セッションで踏まないこと）

- **PlayMode テストを `unity command run_tests --mode playmode` で回すと Editor のメインスレッドが
  ~20分ウェッジした（2 回）**。復旧に Editor 再起動が必要だった。
  → **ランタイム確認は edit-mode の `unity command eval_file` を使う**（`eval` は
  トップレベル `using` 不可＝完全修飾名で書く）。どうしても PlayMode が要るなら 1 ファイル・少数
  `[UnityTest]`・ファイル IO なし・`--async_tests` ＋こまめな `test_status` ポーリングで。
- **`git-lfs` が要る**（`.gitattributes` が mp3/png/fbx 等を LFS 管理）。未導入だと `git status` が
  `fatal` で落ちる。`brew install git-lfs && git lfs install && git lfs pull`。
  未導入時は `git config --local filter.lfs.process ""` 等で一時回避したが、導入後は `git lfs install --local --force` で戻す。
  **LFS ファイルは絶対に `git add -A` しない**（ポインタ↔実体のズレで生バイナリを焼き込む）。ファイル指定で add。
- **`com.unity.pipeline` はチーム push の manifest から外した**（この回のユーザー判断）。
  ローカルの `unity` CLI 連携を使うには `cd <repo> && unity pipeline install` で再導入する
  （manifest.json がローカルのみ dirty になる。push しない）。`.claude/` と `CLAUDE.local.md` は gitignore 済み＝各自。
- `unity status` が `ready` でも `editor_status`（main-thread）が無応答なら＝メインスレッド閉塞。
  off-thread（`unity status` / `recompile_status`）だけ答える。
- `.cs` を消したら `.prefab` / `.unity` に missing script が残ることがある。
  `GameObjectUtility.RemoveMonoBehavioursWithMissingScript` を `eval` で（`PrefabUtility.LoadPrefabContents`→save）。

---

## 6. 残タスク

- **旧デッドコードの削除**（§2「残置」）— 別コミットで。`EnemyMatrixTests`（21）を消すか判断。
- **`docs/phases.md` Phase 7 / `docs/architecture.md` ADR-29 を 360度化・EnemyField 実装済みに更新**
  （ADR-29 はまだ Proposed、360 度拡張の記述が薄い）。
- **チームメイトが LiDAR なしで遊ぶには `_inputMode` を `SimPointer` に**（README / セットアップに明記が要る）。
  現在の push は `LidarPosition` 固定。
- `TH.unity` の orphan prefab override（`_visionConfig` / `_visionFlipHorizontal` — 消えたフィールド）。
  次のシーン保存で自動整理されるが、明示的に掃除しても良い。
- 実機 end-to-end と現場調整（ADR-29 の Proposed→Accepted 条件）。

---

## 7. セッション運用（ユーザーとの約束）

- **思考は英語で良いが、ユーザーへの連絡は日本語で**。
- ユーザーは **Git 初心者**。指示が Git 的におかしい・危険なときは黙って従わず理由を説明して聞き返す。
- **このプロジェクトの変更はローカルに留める。`git push` / PR は明示指示があるまでしない。
  明示的に指示されても、うっかりの可能性があるので実行前に必ずもう一度、対象ブランチ・リモートを口頭確認する。**
  （このブランチは 2026-08-28 に本人確認のうえ `origin/preview-feedback` へ push 済み。以降の追加 push も都度確認。）
- `CLAUDE.local.md` と `/.claude/` は gitignore 済み・個人用・git では復元不可（手動バックアップ）。
- 検証は EditMode ＋ `unity command`（`docs/CLAUDE.md`「Unity Editor 連携」節… は削除したので `CLAUDE.md` の同節）。
