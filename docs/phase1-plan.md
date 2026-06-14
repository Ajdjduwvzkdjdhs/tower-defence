# Phase 1 実装計画書（Wave1 の核 / 味方なし）

> 本書は `spec-summary.md` / `architecture.md` / `phases.md` / `coding-conventions.md` / `mcp-guide.md` を正本として、Phase 1 で実装する全クラスを **依存順** と **検証可能な単位（ユニット）** に分解したもの。完了判定の bar は `phases.md` の Phase 1 受け入れ基準。

## 0. 方針

- **依存の向き**: `Core`（純C#）→ `Data`(SO) → `Input`/`Factory` → `Gameplay`(MonoBehaviour) → `UI` → `Bootstrap`。Core は上位を知らない。
- **検証の二層**:
  - **EditMode テスト**（高速・自動）: `Core/` の純ロジック（`EnemyMatrix`/`IceStock`/`Scoring`/`GameStateMachine`/雪崩タイミング）。
  - **Unity-MCP 視覚検証**（`screenshot-game-view`）: 雪崩の手触り・敵湧き・弾の飛行・UI・Wave進行。`mcp-guide.md` のパターンA〜Dに沿う。
- **1ユニット = 「単独でビルド通過し、固有の合否判定（テスト緑 or MCPスクショ）が下せる最小の塊」**。ユニットは下から積む（先行ユニットの完了が後続の前提）。
- 通信は **C# event**（ADR-3 の移行トリガー未到達）。購読は `OnDestroy` で必ず解除。
- 調整値は SO か `[SerializeField]`。Singleton は `GameManager` のみ。

## 1. 依存レイヤー図

```
[Bootstrap]  GameManager（Composition Root / 唯一のSingleton）
                 │ 生成・配線・FSM駆動
   ┌─────────────┼───────────────────────────────┐
[UI]         UIManager                            │
   │  購読: Fort/IceStock/WaveManager/Input       │
[Gameplay]   WaveManager ─→ ChannelController×4 ──┤
                 │              │  ├ AvalancheController
                 │              │  ├ Enemy ─→ ProjectileFactory ─→ Projectile
                 │              │  └ (uses) EnemyMatrix
                 │              └─→ Fort（被弾先）
[Factory]    ObjectPool<T> , ProjectileFactory
[Input]      IInputSource ← KeyboardInputSource
[Data(SO)]   WaveConfigSO → EnemyTypeSO → ProjectileTypeSO
[Core]       EnemyMatrix , IceStock , Scoring , GameStateMachine + IGameState + 各State
```

矢印 = 「参照する / 依存する」。Core は誰も参照しない（最下層）。

## 2. 全クラス一覧

| # | クラス | フォルダ / 名前空間 | 責務（Phase1スコープ） | 依存 | 主な検証 |
|---|--------|---------------------|------------------------|------|----------|
| C1 | `IceStock` | Core | 残り削り秒の管理。`TryDrain(dt)`／枯渇判定／現在値 | なし | EditMode |
| C2 | `EnemyMatrix` | Core | 行リスト操作。最奥追加 / 先頭行取得 / 先頭行消去 / 空判定（行幅3） | なし | EditMode |
| C3 | `Scoring` | Core | 終了時スコア算出（P1=残HPそのまま、ペナルティはP2で拡張） | なし | EditMode |
| C4 | `IGameState` | Core | `Enter/Tick(dt)/Exit` | なし | — |
| C5 | `GameStateMachine` | Core | `Change()` で `Exit→Enter`、`Tick` を現状態へ委譲 | C4 | EditMode |
| C6 | 各State（`Title`/`WaveIntro`/`WavePlaying`/`WaveLull`/`Result`） | Core | 進行分岐。ゲーム層へは context/コールバック越しに作用 | C4,C5 | EditMode(stub) |
| D1 | `ProjectileTypeSO` | Data | `speed`,`damage` | なし | MCP(資産生成) |
| D2 | `EnemyTypeSO` | Data | `moveSpeed`,`fireInterval`,`projectile`,`isAlly`,`isDisguised`,`tint` | D1 | MCP |
| D3 | `WaveConfigSO` | Data | `duration`,`spawnInterval`,`enemyPool[]`(+P2/P3用フィールド) | D2 | MCP |
| I1 | `IInputSource` | Input | `ActiveChannel:int(-1)`,`IsShaving:bool` | なし | — |
| I2 | `KeyboardInputSource` | Input | WASD ラッチ / Space長押し。`Input.*` はここだけ | I1 | MCP(Play) |
| F1 | `ObjectPool<T>` | Factory | `Get`/`Release`（SetActive 切替・prewarm） | なし | EditMode/MCP |
| F2 | `ProjectileFactory` | Factory | `Create(type,from,to)`→pool取得＋`Init`、着弾でRelease | F1,G2 | MCP |
| G1 | `Fort` | Gameplay | HP・`TakeDamage`・`OnHpChanged`/`OnDestroyed`・`CurrentHp` | なし | EditMode可/MCP |
| G2 | `Projectile` | Gameplay | 麓→山頂へ移動、着弾で`Fort.TakeDamage`＋`onArrive` | G1 | MCP(Play) |
| G3 | `Enemy` | Gameplay | `EnemyTypeSO`参照・`fireInterval`で発射・撃破で発射停止 | D2,F2,G1 | MCP(Play) |
| G4 | `ChannelController`×4 | Gameplay | 1谷のマトリックス保持・最奥湧き・雪崩適用・敵の配置座標 | C2,G3,G1 | MCP |
| G5 | `AvalancheController` | Gameplay | アクティブ谷で `delay→interval` ごとに先頭行消去（深さ制御） | I1,G4,(C2) | EditMode(タイミング)/MCP |
| G6 | `WaveManager` | Gameplay | `WaveConfigSO`に沿う時間管理・湧き・Wave終了時の残敵消去 | D3,G4 | EditMode(タイマ)/MCP |
| U1 | `UIManager` | UI | 氷バー/砦HP/Waveタイマー/アクティブ谷ハイライト/リザルト（購読） | G1,C1,G6,I1 | MCP |
| B1 | `GameManager` | Bootstrap | 全システム生成・配線・FSM駆動（唯一のSingleton） | 全部 | MCP(通し) |

## 3. 検証可能な単位（ユニット）と実装順序

各ユニットは **「完了条件」を満たした時点で次へ進む**。完了条件は EditMode 緑または MCP スクショ（`mcp-guide.md`）で客観確認する。

> **進捗（2026-06-14）— マイルストーン A 完了 ✅**　EditMode 全 **49 本緑**。Unity 6000.3.16f1 / test-framework 1.6.0。
> | Unit | クラス | テスト | 状態 |
> |---|---|---|---|
> | 0 | asmdef×2（`KakigoriTD.Core`(noEngineReferences)/`Tests.EditMode`）+ 足場 | ScaffoldingTests 1 | ✅ |
> | 1 | `IceStock` | IceStockTests 9 | ✅ |
> | 2 | `EnemyMatrix<T>` | EnemyMatrixTests 12 | ✅ |
> | 3 | `Scoring` | ScoringTests 4 | ✅ |
> | 4 | `GameStateMachine`+`IGameState` | GameStateMachineTests 7 | ✅ |
> | 5 | States 5種＋`IGameContext` | StatesTests 16 | ✅ |
>
> 設計判断: `EnemyMatrix<T>` はジェネリック採用（Core純粋維持）。State は `IGameContext` 境界越しに作用（**ADR-9** 追記）。ウェーブ番号は State 側保持。

### マイルストーン A — Core 基盤（Unity 実行不要・EditModeで完結）

**Unit 0: プロジェクト足場 / Assembly Definition**
- 成果物: `Core/`,`Data/`,`Input/`,`Factory/`,`Gameplay/`,`UI/`,`Bootstrap/`,`Tests/EditMode/` のフォルダと **asmdef**。`KakigoriTD.Core` は UnityEngine 非依存を保つ（ADR-8）。Tests.EditMode は Core を参照。
- 依存: なし
- 完了条件: Unity でコンパイル通過（`assets-refresh`）／空の EditMode テストが1本緑（`tests-run`）。
- 補足: **Unity バージョンを確定し CLAUDE.md の TODO を埋める**（TBD解消）。

**Unit 1: `IceStock`（C1）**
- 完了条件 (EditMode): `TryDrain_減算される` / `TryDrain_枯渇後はfalse・0未満にならない` / `Current/IsEmpty` 整合。→ 受け入れ基準「氷が0で削れない」のロジック保証。

**Unit 2: `EnemyMatrix`（C2）**
- 設計判断: Core を純粋に保つため **`EnemyMatrix<T>`（ジェネリック）** とし、`Enemy` は上位で `T` に充てる（推奨）。行幅=3 はコンストラクタ引数。
- 完了条件 (EditMode): `AddToBack_最奥行に追加` / `RemoveFrontRow_先頭行が消え次行が先頭になる` / `FrontRow_最大3体` / `空判定`。→ 「先頭行消去・最奥追加」基準。

**Unit 3: `Scoring`（C3）**
- 完了条件 (EditMode): `Score_残HPを返す`（P1）。P2のペナルティ拡張点をコメントで明示。

**Unit 4: `GameStateMachine` + `IGameState`（C4,C5）**
- 完了条件 (EditMode・stub state使用): `Change_前状態Exit後に新状態Enter` / `Tick_現状態へ委譲` / `初期状態nullでもTick安全`。→ 「GameStateMachine 遷移」基準。

**Unit 5: 各 State（C6）**
- 設計判断: States はゲーム層を直接持たず **context インターフェース / コールバック** 越しに作用させ、stub で遷移を EditMode 検証可能にする。
- 完了条件 (EditMode・stub context): `WavePlaying_時間切れ→残敵消去→n<3でWaveLull` / `WavePlaying_HP0通知→Result(Lose)` / `WaveLull→次WaveIntro` / `n==3で時間切れ→Result(Win)`。

> **進捗（2026-06-14）— マイルストーン B 完了 ✅**
> | Unit | 成果物 | 検証 | 状態 |
> |---|---|---|---|
> | 6 | `ProjectileTypeSO`/`EnemyTypeSO`/`WaveConfigSO` + Wave1実体（BasicProjectile/BasicEnemy/Wave1.asset） | `assets-get-data` で §19値・参照配線を確認（speed6/dmg5, fireInterval2, duration30/spawn1.5） | ✅ |
> | 7 | `ObjectPool<T>`（Factory） | ObjectPoolTests **7本緑**（再利用・SetActive・prewarm） | ✅ |
> | 8 | `IInputSource`/`KeyboardInputSource`（新Input System） | Play Mode で WASDラッチ・Space長押しを reflection 経由で確認（W→0/A→1/S→2/D→3, IsShaving切替） | ✅ |
>
> 実装: ultracode（サブエージェント3体で並列コード生成）＋ Unity-MCP操作はメイン直列集約。EditMode 累計 **56本緑**。
> 設計判断: 入力は新 Input System を採用（**ADR-10**、`activeInputHandler=1` のため旧 API は不使用）。ランタイムは単一 `KakigoriTD.Runtime.asmdef` に集約（Core は分離維持）。

### マイルストーン B — データと生成プリミティブ

**Unit 6: Data SO（D1→D2→D3）**
- 完了条件 (MCP): `CreateAssetMenu` から各 SO を生成でき Inspector で編集可能。**Wave1用の実体アセット**を `Assets/ScriptableObjects/` に作成（`§19` 初期値: projectile damage5/speed6, enemy fireInterval2, wave1 duration30/spawn1.5）。`assets-get-data` で値確認。

**Unit 7: `ObjectPool<T>`（F1）**
- 完了条件 (EditMode/MCP): ダミーComponentで `Get_在庫なしで生成・在庫ありで再利用` / `Release_SetActive(false)で在庫へ戻る` / `prewarm数だけ事前生成`。→ 「ホットパスにInstantiate/Destroyが無い」基準の土台。

**Unit 8: `IInputSource` + `KeyboardInputSource`（I1,I2）**
- 完了条件 (MCP Play): Play Mode で `W/A/S/D`→`ActiveChannel`が0/1/2/3にラッチ（同時1本）、`Space`長押しで`IsShaving=true`。`reflection-method-call`/ログで確認。

> **進捗（2026-06-14）— マイルストーン C 完了 ✅**
> | Unit | 成果物 | 検証 | 状態 |
> |---|---|---|---|
> | 9 | `Fort`（Gameplay: HP/TakeDamage/OnHpChanged/OnDestroyed/Normalized/Initialize） | FortTests **8本緑** ＋ Play Mode で `script-execute` 経由確認（1000→600→0、OnDestroyed 1回のみ、陥落後ダメージ無視） | ✅ |
> | 10 | `Projectile`（Gameplay）+ `ProjectileFactory`（Factory・pooled） | Play Mode: 弾が麓(0,-4.8)→砦へ飛行（`screenshot-game-view`）、着弾で HP 1000→995（=damage 5）、着弾後はプール返却（GameObject 非アクティブ・破棄なし） | ✅ |
> | 11 | `Enemy`（Gameplay: fireInterval 発射 / `Kill` で停止 / `ShotsFired`/`IsAlive`） | Play Mode: fireInterval=2 で 20発/41.97s（=1発/2s）、各着弾 5dmg（HP 1000→900）、`Kill` 後 11秒で発射ゼロ・HP 不変 | ✅ |
>
> 実装: メインセッションで直列集約（コード生成→`assets-refresh`→`tests-run`→Play Mode 検証）。EditMode 累計 **64本緑**（+FortTests 8）。可視化は URP/Unlit マテリアル（白=砦 / 赤=敵 / 黄=弾）＋ slow-mo（`Time.timeScale`）で飛行をスクショ。
> 設計判断: `ProjectileFactory` は生成方法を `Func<Projectile>` で注入（`ObjectPool` と同パターン、シーン/プレハブ非依存で testable）。`Fort`/`Projectile`/`Enemy` の単純な状態は MonoBehaviour 内に保持し、純判定は EditMode（Fort）、時間的・視覚的挙動は MCP Play で検証（**ADR-11**）。検証用に `Fort` をシーンへ仮配置（GameManager 実装時に置換予定）。

### マイルストーン C — 戦闘アクター（弾と敵）

**Unit 9: `Fort`（G1）**
- 完了条件: `TakeDamage_HP減算・0未満にならない` / `OnHpChanged発火` / `HP0でOnDestroyed発火` / `CurrentHp`。`script-execute`で`TakeDamage`を叩き、MCPで確認。→ 「砦HP着弾減算→OnDestroyed」基準。

**Unit 10: `Projectile` + `ProjectileFactory`（G2,F2）**
- 完了条件 (MCP Play): 弾が麓座標→山頂座標へ`speed`で移動、着弾で`Fort.TakeDamage(damage)`が走り **プールへ返却**（GC生成なし）。`screenshot-game-view`で飛行を、`Fort.CurrentHp`減少を確認（mcp-guide パターンD）。

**Unit 11: `Enemy`（G3）**
- 完了条件 (MCP Play): 敵が`fireInterval`ごとに弾を発射し砦へ着弾。**撃破（プール返却）すると以後の発射が止まる**（飛来中の弾は着弾）。→ 「敵を倒すと発射停止」基準。

> **進捗（2026-06-14）— マイルストーン D 完了 ✅**
> | Unit | 成果物 | 検証 | 状態 |
> |---|---|---|---|
> | 12 | `ChannelController`（Gameplay・放射状4谷） | Play: 4谷 N/W/S/E に front(3)+back(2…) 行で配置、`ApplyAvalanche` で先頭行（山頂側）消去（北谷 killed=3, enemies 5→2, rows 2→1）。survivor 静止＝撃破フロントが手前→奥へ | ✅ |
> | 13 | `AvalancheClock`（Core・純タイミング）+ `AvalancheController`（Gameplay） | AvalancheClockTests **7本緑**（delay→開始 / interval ごと / 停止リセット）。Play: W+Space で北谷 9→0、氷 50→0、入力解放で停止（9維持）、氷0では削れない（IsAvalanching=false） | ✅ |
>
> 実装: メインセッション直列。EditMode 累計 **71本緑**（+AvalancheClock 7）。コミットはユニット単位（Unit12 / Unit13）。
> 設計判断（**ADR-12**）: `ChannelController` は各敵の座標を湧き時に固定し先頭行消去で動かさない（撃破フロントが外へ進む・survivor 静止）。雪崩タイミングは §13 方針どおり純ロジック `AvalancheClock` に分離し EditMode 検証、MonoBehaviour は入力（`IInputSource`）・氷（`IceStock`）・谷適用のアダプタ。
> 使い捨てデモ `Assets/Scripts/Demo/DemoHarness.cs` を Unit 8-13 対話版に更新（W/A/S/D 選択＋Space 削り＋氷＋砦HP、`[R]` リセット）。GameManager 実装時に破棄。

### マイルストーン D — 谷と雪崩（手触りの核）

**Unit 12: `ChannelController`×4（G4）**
- 完了条件 (MCP): 4谷が放射状（N/W/S/E）に配置。最奥に敵が湧き、行が手前→奥に並ぶ座標で表示。`ApplyAvalanche()`で先頭行が消える。`screenshot-game-view`で4方向確認。

**Unit 13: `AvalancheController`（G5）**
- 設計判断: `delay`/`killInterval` の **タイミング計算は純ロジックに分離** し EditMode 検証（推奨）。MonoBehaviourは入力と谷適用のアダプタ。
- 完了条件:
  - EditMode: `削り開始からavalancheDelay後に判定開始` / `以降killIntervalごとに1行` / `削り停止・谷変更で進行停止`。
  - MCP Play（mcp-guide パターンA）: `Space`長押しでアクティブ谷の先頭行が`killInterval`ごとに消え、**削り続けると奥へ**、**止めると停止**。氷消費（`IceStock`連動・0で不可）。スクショ証拠を残す。
- → 受け入れ基準「先頭行消去」「深さ制御」「氷ストック減少・枯渇」を一括検証。

### マイルストーン E — 進行・UI・配線

**Unit 14: `WaveManager`（G6）**
- 完了条件: EditMode（タイマ/湧き判定を分離した場合）＋ MCP（パターンC）: `spawnInterval`で各谷に湧き、`duration`経過で **残敵消去**→次へ。

**Unit 15: `UIManager`（U1）**
- 完了条件 (MCP スクショ): 氷バー減少 / 砦HPバー減少（`OnHpChanged`購読）/ Waveタイマー / **アクティブ谷ハイライト（同時1本）** / リザルト表示。主要状態を各1枚記録（受け入れ基準の視覚項目）。

**Unit 16: `GameManager`（B1）**
- 完了条件: 全システムを生成・配線し FSM 駆動。Singletonは本クラスのみ・入力は`IInputSource`経由であることをコードレビューで確認。

### マイルストーン F — 統合検証（Phase 1 完了判定）

**Unit 17: Wave1 通し**
- 完了条件: `phases.md` Phase 1 受け入れ基準を **全チェック**。
  - EditMode 全緑（`EnemyMatrix`/`IceStock`/`Scoring`/`GameStateMachine`＋雪崩タイミング）。
  - MCP 通しプレイ: 照準切替→削り→殲滅、敵湧き→発射→砦HP減、`duration`でWave終了、3Wave到達でResult(Win)、HP0でResult(Lose)、スコア=残HP表示。各主要状態のスクショを記録。
  - 完了後 `phases.md` のチェックリストと Status を更新。

## 4. クリティカルパス（最短で「手触り」に到達する線）

```
Unit0 → Unit2(EnemyMatrix) → Unit6(SO) → Unit12(Channel) → Unit13(Avalanche) → MCPで雪崩の手触り検証
                                        ↘ Unit8(Input) ↗
```
弾・砦・Wave・UIは並行可能だが、**「削る→雪崩→先頭行消去」の検証**を最優先で通すと、設計の妥当性を早期に確認できる（mcp-guide の最重要ポイント）。

## 5. 要確定事項（着手前に解消 or 既定値で進行）

| 項目 | 推奨既定 | 影響 |
|------|----------|------|
| Unity バージョン | 確定して CLAUDE.md TODO を埋める | asmdef / Test Framework |
| `EnemyMatrix` の型 | ジェネリック `EnemyMatrix<T>`（Core純粋を維持） | テスト容易性 |
| State の外部作用方式 | context インターフェース＋コールバック | State の EditMode 検証可否 |
| 雪崩タイミングの分離 | 純ロジックに分離し EditMode 化 | 深さ制御の自動検証 |
| 弾の着弾判定 | 距離/到達割合ベース（コライダー不要） | Projectile実装 |

> スコープ厳守: 味方NPC・変装・突撃隊・SOイベントチャネル移行・エフェクト/サウンドは **Phase 1 では実装しない**（`phases.md` Out of scope）。`EnemyTypeSO.isAlly/isDisguised/tint` は定義のみ置き、挙動はP2/P3。
```
