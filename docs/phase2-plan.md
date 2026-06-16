# Phase 2 実装計画書（味方NPC＝深さ制御ジレンマ / Wave2）

> 本書は `spec-summary.md` / `docs/かき氷TD_プロトタイプ仕様書.md`（§8 味方NPC・§19 パラメータ）/ `architecture.md` / `phases.md` / `coding-conventions.md` / `mcp-guide.md` を正本として、Phase 2 で実装する全クラス（新規＋既存改修）を **依存順** と **検証可能な単位（ユニット）** に分解したもの。完了判定の bar は `phases.md` の Phase 2 受け入れ基準。
> Phase 1（main マージ済み）／Phase 1.5（main マージ済み・**実機 M5StickC Plus でライブ検証済み 2026-06-16**）はいずれも完了。本フェーズは **Phase 1 の資産（`EnemyMatrix<T>`／プール／雪崩＝先頭行消去／States／`Scoring`）を再利用して味方NPCを差し込む**ことを基本方針とする。

## 前提（Phase 1.5 後の現状＝土台）

本計画は **Phase 1.5 完了後**の状態を土台とする。着手前に以下を確認すること。

- **ブランチ**: Phase 1.5 は `phase1.5-serial` → **`main` にマージ済み**（2026-06-16）。したがって **Phase 2 は `main` を土台に切る**（`phase2-allies`）。`main` に Composite 入力・ADR-15・シリアル16本テスト・実機検証成果物（`BTSerialConfig.asset` ＋ `SampleScene.unity` の `_serialConfig` 配線）が含まれる。
- **EditMode ベースライン**: **92本緑**（Phase 1 の76＋シリアル16：`SerialInputProtocol` 11／`CompositeInputSource` 5）。Phase 2 の新規テストはこの上に積む（回帰ゼロが条件）。
- **入力**: `GameManager` は既に `CompositeInputSource`（キーボード＋シリアル）を `BuildInput()` で配線済み。**Phase 2 は入力層を変更しない**（味方は谷選択／削り入力に手を入れない）。`SerialConfigSO` 未割り当ての既定シーンはキーボードのみで Phase 1 同様に動作（回帰なし）。
- **ADR**: 最新は **ADR-15**（シリアル）。本計画が追加するのは **ADR-16 / ADR-17**。
- **Player Setting**: Standalone の API Compatibility Level は .NET Framework（`NET_Unity_4_8`）へ変更済み（ADR-15）。Phase 2 では追加変更不要。

## 0. 方針

- **Phase 2 が増やす遊び**: 味方を巻き込まずに敵を捌く判断（**待つ／手前で止める／谷を変える**）。Phase 1 の「削る→雪崩→先頭行消去（深さ制御）」の手触りに、**ガード行（味方）** という制約を1枚重ねるだけ。新メカニクスはほぼ既存の延長で成立する。
- **依存の向き**（Phase 1 と不変）: `Core`（純C#）→ `Data`(SO) → `Input`/`Factory` → `Gameplay`(MonoBehaviour) → `UI` → `Bootstrap`。Core は上位を知らない。
- **検証の二層**（Phase 1 同様）:
  - **EditMode テスト**（高速・自動）: ペナルティ累積（`PenaltyTally`）／味方ガード下の行ロジック（`EnemyMatrix<T>` の専用行・行除去）／penalty を載せた `Scoring` 配線（stub context）。
  - **Unity-MCP 視覚検証**（`screenshot-game-view`）: 青い味方の出現・自走退場・巻き込み（雪崩がガード行に達した瞬間）・手前ノーペナ撃破・奥の敵の解放・Wave2 通し。`mcp-guide.md` のパターンに沿う。
- **1ユニット = 「単独でビルド通過し、固有の合否判定（テスト緑 or MCPスクショ）が下せる最小の塊」**。下から積む。
- 通信は **C# event**（ADR-3 の移行トリガー未到達。味方巻き込みは `ChannelController.OnAllyCaught` の C# event で `GameManager` へ）。購読は `OnDestroy` で必ず解除。
- 調整値は SO か `[SerializeField]`（`allySpawnChance`/`allyType`=SO、`allyHitPenalty`/`allyLifetime`=SO/Inspector）。Singleton は `GameManager` のみ。
- **提案する設計判断（実装時に ADR 追記）**:
  - **ADR-16**: 味方NPC は `AllyNPC : Enemy`。同一 `EnemyMatrix<Enemy>` に多態で保持し、谷ごとに **ally 専用プール**を持つ。味方は **専用行を占有**し **無発射＋`allyLifetime` 自走退場**（退場＝行除去で後続が繰り上がる）。雪崩がガード行に達したら `OnAllyCaught` を発火。色は `Enemy.Init` で **per-instance tint**（プール再利用で赤⇄青を差し替え）。
  - **ADR-17**: スコアの巻き込みペナルティは Core `PenaltyTally` に累積し、`IGameContext.PenaltyTotal` 越しに `Scoring.Compute(fortHp, penalty)` へ供給する（ADR-8/9 を踏襲・覆さない）。
- **スコープ厳守**: **変装（青に化けた敵）・突撃隊は Phase 3**。本フェーズでは実装しない（`EnemyTypeSO.IsDisguised`／`WaveConfigSO.DisguiseChance` は define-only のまま据え置き）。SOイベントチャネル移行（ADR-3）も本フェーズの対象外。

## 1. 依存レイヤー図（Phase 1 からの差分）

```
[Bootstrap]  GameManager（唯一のSingleton）
                 │  ＋ PenaltyTally 生成 / OnAllyCaught 購読→allyHitPenalty 加算 / PenaltyTotal 実装 / createAlly 注入
   ┌─────────────┼───────────────────────────────┐
[UI]         UIManager（＋リザルトに −ペナルティ／最終スコア。味方は青で可視）
[Gameplay]   WaveManager ─→ ChannelController×4 ──┤  ＊改
                 │ ＋allySpawn   │  ├ AvalancheController（不変）
                 │              │  ├ Enemy ◀── AllyNPC（新・継承） ＊改: Update virtual / Init で tint
                 │              │  ├ ally 専用プール / SpawnAlly（専用行）/ OnAllyCaught
                 │              │  └ (uses) EnemyMatrix<Enemy>  ＊改: AddRow / RemoveRowAt
[Factory]    ObjectPool<T>（不変・ally 用に2本目）, ProjectileFactory（不変）
[Input]      IInputSource / Composite（不変）
[Data(SO)]   WaveConfigSO ＊改(allyType) → EnemyTypeSO ＊改(allyLifetime) → ProjectileTypeSO（不変）
[Core]       EnemyMatrix<T> ＊改 , PenaltyTally（新） , Scoring（penalty 既存引数を使用）
             GameStateMachine/States ＊改(PenaltyTotal を Scoring へ) , IGameContext ＊改(PenaltyTotal)
```

`＊改` = 既存クラスの改修。`新` = 新規クラス。矢印 = 参照／依存。**`EnemyMatrix<T>` がジェネリックである恩恵（Phase 1 の設計判断）**で、Core 側は `AllyNPC` 追加の影響を受けない（テストは `EnemyMatrix<int>` のまま緑）。

## 2. 全クラス一覧（新規＋改修）

| # | クラス | フォルダ / 名前空間 | 種別 | 責務（Phase2スコープ） | 依存 | 主な検証 |
|---|--------|---------------------|------|------------------------|------|----------|
| C1 | `PenaltyTally` | Core | 新 | 巻き込みペナルティの累積。`Add(int)` / `Reset()` / `Total` | なし | EditMode |
| C2 | `EnemyMatrix<T>` | Core | 改 | `AddRow(item)`（専用行を最奥に追加）＋ `RemoveRowAt(index)`（自走退場：行除去で繰り上げ） | なし | EditMode |
| C3 | `IGameContext` | Core | 改 | `int PenaltyTotal { get; }` を追加（勝敗時スコアに減点を供給） | なし | — |
| C4 | `WavePlayingState` / `ResultState` | Core | 改 | `Scoring.Compute(ctx.FortHp, ctx.PenaltyTotal)` に変更 | C3 | EditMode(stub) |
| C5 | `Scoring` | Core | 不変 | 既存の `Compute(fortHp, penalty)` をそのまま使用（Phase1 で拡張点を用意済み） | なし | EditMode |
| D1 | `EnemyTypeSO` | Data | 改 | `allyLifetime` 追加（既存 `IsAlly`/`Tint` を活用、味方型は `Projectile=null`） | — | MCP(資産) |
| D2 | `WaveConfigSO` | Data | 改 | `allyType`（味方 `EnemyTypeSO`）追加。`AllySpawnChance` は既存 | D1 | MCP(資産) |
| G1 | `AllyNPC` | Gameplay | 新 | `Enemy` を継承。**無発射**・`allyLifetime` 自走退場・退場通知 `OnExpired` | G2,D1 | MCP(Play) |
| G2 | `Enemy` | Gameplay | 改 | `Update` を `protected virtual` 化、`Init` で `type.Tint` を per-instance 反映（プール再利用で色替え） | D1 | MCP(Play) |
| G3 | `ChannelController` | Gameplay | 改 | ally 専用プール＋`SpawnAlly`（専用行占有）／自走退場で行除去／雪崩がガード行に達したら `OnAllyCaught` 発火（自走退場は無ペナルティ） | C2,G1 | EditMode(行)/MCP |
| G4 | `WaveManager` | Gameplay | 改 | 各湧き機会に `AllySpawnChance` を判定し、当たれば `allyType` を専用行で湧かせる | D2,G3 | MCP |
| U1 | `UIManager` | UI | 改 | リザルトに「−ペナルティ／最終スコア」を表示（味方の可視化はシーン側の青で担保） | C1 | MCP |
| B1 | `GameManager` | Bootstrap | 改 | `PenaltyTally` 生成・`PenaltyTotal` 実装、各谷 `OnAllyCaught` 購読→`_allyHitPenalty` 加算（`OnDestroy` 解除）、`createAlly` 注入、リトライ時に tally リセット | 全部 | MCP(通し) |

## 3. 検証可能な単位（ユニット）と実装順序

各ユニットは **「完了条件」を満たした時点で次へ進む**。完了条件は EditMode 緑または MCP スクショ（`mcp-guide.md`）で客観確認する。コミットは **ユニット単位**（土台＝`main` から切った `phase2-allies`／MCP ツーリングは除外）。

> **進捗**: ⬜ 未着手（本書は計画）。各マイルストーン完了時にこの位置へ Phase 1 と同様の進捗テーブル（Unit／成果物／検証／状態）を追記し、`phases.md` Phase 2 チェックリストと ADR を更新する。

### マイルストーン A — Core 拡張（Unity 実行不要・EditMode で完結）

**Unit P1: `PenaltyTally`（C1）**
- 成果物: `Assets/Scripts/Core/PenaltyTally.cs`。純 C#（UnityEngine 非依存・ADR-8）。`Add(int points)` / `Reset()` / `int Total`。負値や 0 の扱いはコメントで明示。
- 依存: なし
- 完了条件 (EditMode): `Add_累積される` / `Reset_0に戻る` / `初期Total_0`。→ 受け入れ基準「`allyHitPenalty` が加算される」のロジック保証（spec の "ScoreManager" 役割を Core の純ロジックで担う）。

**Unit P2: `EnemyMatrix<T>` 拡張（C2）**
- 設計判断: 味方は**専用行を占有**する（spec §8「マトリックスの1行を占有」）。混在行を避けるため `AddRow` は新しい最奥行に単独で積み、以後の `AddToBack` がその行を埋めない（＝ガード行として隔離）。自走退場は**先頭行とは限らない中段の行を除去**するため `RemoveRowAt` を追加し、除去で後続行が手前へ繰り上がる（spec「次行が先頭に＝退場後に奥が解放」）。
- 成果物: `EnemyMatrix<T>` に `void AddRow(T item)`（専用・隔離行を最奥へ）と `IReadOnlyList<T> RemoveRowAt(int index)`（範囲外は空・除去で index を詰める）を追加。`int IndexOfRowContaining(T)` 等の補助は必要に応じて。
- 完了条件 (EditMode): `AddRow_単独の最奥行になる_後続AddToBackで埋まらない` / `RemoveRowAt_指定行が消え後続が繰り上がる` / `RemoveRowAt_範囲外は空・無変化` / **ガード行シナリオ**: `手前行→ガード行→奥行 を組み、先頭行消去を繰り返すと先頭がガード行で止まる`（＝手前は消せる）／`RemoveRowAt(ガード行) 後に奥行が先頭側へ繰り上がる`（＝退場後に奥が解放）。→ 受け入れ基準③④の純ロジック保証。

**Unit P3: Scoring 配線（C3, C4）**
- 設計判断（ADR-17）: State はゲーム層を直接持たず `IGameContext` 越しに減点合計を読む。`PenaltyTotal` は context が公開し、具象は `GameManager` が `PenaltyTally.Total` を返す。
- 成果物: `IGameContext` に `int PenaltyTotal { get; }`。`WavePlayingState`／`ResultState` のスコア算出を `Scoring.Compute(_ctx.FortHp)` → `Scoring.Compute(_ctx.FortHp, _ctx.PenaltyTotal)` に変更。
- 完了条件 (EditMode・stub context): 既存 `StatesTests` を拡張。`勝利時_スコア=残HP−penalty` / `敗北時も penalty を反映` / `penalty=0 で Phase1 と同値`（回帰なし）。→ 受け入れ基準②の終端反映。

### マイルストーン B — データ（SO）

**Unit P4: 味方データ（D1, D2）＋ Wave2 を味方混在へ**
- 成果物:
  - `EnemyTypeSO` に `[SerializeField] float _allyLifetime`（既定 6・spec §19）＋ `AllyLifetime` プロパティ。`IsAlly`/`Tint` は既存（define-only を本フェーズで活用）。
  - `WaveConfigSO` に `[SerializeField] EnemyTypeSO _allyType` ＋ `AllyType` プロパティ（`AllySpawnChance` は既存）。
  - **味方型アセット** `Assets/ScriptableObjects/AllyNPC.asset`（`IsAlly=true` / `Tint=青` / `Projectile=null`〔無発射〕/ `AllyLifetime=6`）。
  - 既存 `Wave2.asset` に `AllySpawnChance>0`（初期値はプレイテストで調整、例 0.25）＋ `AllyType=AllyNPC` を設定（敵＋味方混在）。`Wave1` は据え置き（味方なし）。
- 完了条件 (MCP): `assets-get-data` で各値・参照配線を確認（`AllyNPC` の `IsAlly=true`/`Projectile=null`/`AllyLifetime=6`、`Wave2.AllyType` 参照、`AllySpawnChance` 値）。Inspector で編集可能。→ 受け入れ基準①⑤のデータ土台。

### マイルストーン C — 味方挙動と巻き込み（手触りの核）

**Unit P5: `AllyNPC : Enemy`（G1）＋ `Enemy` の改修（G2）**
- 設計判断（ADR-16）: 味方は敵の派生として `EnemyMatrix<Enemy>` に多態で載る。**無発射**は味方型 `Projectile=null` で既存 `Enemy.Fire` のガードが効くため自動成立。`Enemy.Update` を `protected virtual` 化し、`AllyNPC` は発射せず `allyLifetime` を数えて尽きたら `OnExpired` を発火（所有者がプール返却・行除去）。プール再利用で同一 GameObject が赤敵⇄青味方に変わるため、`Enemy.Init` で `type.Tint` を **per-instance**（`MaterialPropertyBlock` 等で `sharedMaterial` を汚さず）反映。
- 成果物: `Assets/Scripts/Gameplay/AllyNPC.cs`（`Init` で lifetime セット／`Update` で減算→`OnExpired`／`IsAlly` 判定用フック）。`Enemy.cs`: `Update` を virtual 化、`Init` に tint 反映を追加。
- 完了条件 (MCP Play): 青い味方を1体生成し、**`allyLifetime` 秒で自走退場**（GameObject 非アクティブ／プール返却・破棄なし）を `screenshot-game-view`＋ログで確認。味方は**弾を撃たない**（`ShotsFired==0`・砦HP不変）。赤敵と同じプール経路でも色が混ざらない（per-instance tint）。→ 受け入れ基準①。

**Unit P6: `ChannelController` 味方対応（G3）**
- 設計判断（ADR-16）: 谷は `_enemyPool`（`Enemy`）に加え `_allyPool`（`AllyNPC`）を持ち、`Configure` で `Func<AllyNPC> createAlly` を受け取る。`SpawnAlly` は `EnemyMatrix.AddRow` で**専用行**に味方を置き、`OnExpired` を購読して退場時に `RemoveRowAt` ＋プール返却（**無ペナルティ**）。`ApplyAvalanche` は先頭行除去時に**味方を含む行なら `OnAllyCaught` を発火**してから返却（＝巻き込み＝減点）。雪崩・退場いずれも `RepositionAll` で繰り上げ。
- 成果物: `ChannelController.cs` に ally 専用プール・`SpawnAlly(EnemyTypeSO allyType)`・`event Action OnAllyCaught`・退場ハンドラ・型別リリース（`e is AllyNPC` 判定）を追加。`Configure` シグネチャに `createAlly` を追加。
- 完了条件:
  - EditMode 可（純行ロジックは P2 で担保済み。本ユニットはアダプタ）。
  - MCP Play（mcp-guide パターンA/D）: ①谷に敵→**専用行の味方**→敵 を湧かせ配置を確認。②`Space` 削りで**先頭から手前の敵が消え、先頭が味方になった所で止めればノーペナ**（`OnAllyCaught` 未発火・`PenaltyTally.Total` 不変）。③そのまま削り続け**味方を巻き込むと `OnAllyCaught` 発火**（減点）。④削らず待つと味方が**自走退場し、奥の敵が手前へ繰り上がって撃破可能**になる。各局面をスクショ記録。→ 受け入れ基準②③④の中核。

**Unit P7: `WaveManager` 味方湧き（G4）**
- 設計判断: 既存のラウンドロビン湧きに**味方確率**を重ねる。各湧き機会で `Random.value < config.AllySpawnChance` かつ `config.AllyType != null` なら、選んだ谷に `SpawnAlly` を試す（満杯谷は次へ）。1谷に味方が同時多数で出ないよう上限を設けるかは要検討（§5）。
- 成果物: `WaveManager.SpawnOne` に味方分岐を追加（敵 or 味方の選択）。`AllySpawnChance==0` の Wave1 では完全に従来動作（回帰なし）。
- 完了条件 (MCP Play): Wave2 で青い味方が `AllySpawnChance` 相当の頻度で混ざって出現、`AllyLifetime` で自走退場。Wave1 では味方が一切出ない（回帰確認）。→ 受け入れ基準①⑤。

### マイルストーン D — 配線・統合（Phase 2 完了判定）

**Unit P8: `GameManager` 配線（B1）＋ UI（U1）**
- 成果物:
  - `PenaltyTally` を生成し `IGameContext.PenaltyTotal => _penalty.Total` を実装。
  - 各 `ChannelController.OnAllyCaught` を購読し `_penalty.Add(_allyHitPenalty)`（`[SerializeField] int _allyHitPenalty = 50`・spec §19）。購読は `OnDestroy` で解除（ADR-3）。
  - `createAlly`（青キューブ生成デリゲート）を `ChannelController.Configure` に注入（赤敵の `CreateEnemy` と対）。
  - `BeginWave(0)`（新規/リトライ）で `_penalty.Reset()` を追加（砦HP・氷・残敵リセットと同列）。
  - `UIManager`: リザルトに「砦HP − ペナルティ = 最終スコア」を表示（巻き込みが見える）。
- 完了条件 (MCP): `_input`/配線がコードレビューで Singleton=`GameManager` のみ・入力は `IInputSource` 経由を維持。Play で味方巻き込み→リザルトのスコアが `残HP − allyHitPenalty×巻き込み数` になることをスクショ確認。→ 受け入れ基準②⑥＋鉄則順守。

**Unit P9: Wave2 通し統合検証（Phase 2 完了判定）**
- 完了条件: `phases.md` Phase 2 受け入れ基準を **全チェック**。
  - EditMode 全緑（`PenaltyTally` ／`EnemyMatrix` ガード行・行除去 ／penalty を載せた States）。Phase 1/1.5 の既存テストも緑（回帰なし／累計本数を記録）。
  - MCP 通しプレイ: Wave2 で味方が `AllySpawnChance` で出現し `AllyLifetime` で自走退場／ガード行に雪崩が達すると `allyHitPenalty` 減点／**手前の敵は手前で止めればノーペナ撃破**／**奥の敵は味方退場後に撃破**（恒久ロックされない）／Wave2 を敵＋味方混在で通しプレイ／リザルトに −ペナルティ反映。各局面のスクショを記録。
  - 完了後 `phases.md` Phase 2 チェックリストと Status を更新、`architecture.md` に **ADR-16 / ADR-17** を追記。

## 4. クリティカルパス（最短で「味方ジレンマの手触り」に到達する線）

```
P2(EnemyMatrix: 専用行/行除去) → P5(AllyNPC: 無発射/自走退場) → P6(Channel: SpawnAlly/巻き込み)
                                                              → MCP で「手前ノーペナ撃破 / 待って奥を解放 / 巻き込み減点」を検証
P1(PenaltyTally) ─┐
P3(Scoring 配線) ─┴→ P8(GameManager 配線) → 減点がスコアに乗る
```
**最重要は「ガード行（味方）を挟んだ深さ制御」の手触り**（P2→P5→P6）。ここを最初に MCP で通せば、Phase 2 のゲーム性（待つ／止める／谷を変える）が成立するかを早期に確認できる。スコア配線（P1/P3/P8）は並行可能。

## 5. 要確定事項（着手前に解消 or 既定値で進行）

| 項目 | 推奨既定 | 影響 |
|------|----------|------|
| 味方の実装形 | `AllyNPC : Enemy`（同一 `EnemyMatrix<Enemy>` に多態保持＋ally 専用プール）＝ ADR-16 | プール／行ロジックの再利用度 |
| ペナルティの置き場所 | Core `PenaltyTally` ＋ `IGameContext.PenaltyTotal`（spec の "ScoreManager" 役割）＝ ADR-17 | EditMode 検証可否・スコア配線 |
| `allyLifetime` の置き場所 | `EnemyTypeSO`（味方型の属性）。`allyHitPenalty` は `GameManager` の `[SerializeField]` | SO/Inspector 調整 |
| 味方の行占有 | 専用行（`AddRow` で隔離、後続 `AddToBack` で埋めない） | 「手前/奥」の分離の明瞭さ |
| `allySpawnChance` の初期値 | 0.25（プレイテストで調整）／1谷同時味方数の上限は要検討 | Wave2 の難度・ジレンマ頻度 |
| 味方の per-instance 色 | `MaterialPropertyBlock`（`sharedMaterial` を汚さず赤⇄青） | プール再利用時の色混じり防止 |
| 巻き込み時の挙動 | ガード行の味方は雪崩で**消滅＋減点**（その先の奥は解放）。自走退場は無ペナルティ | 受け入れ基準②④の整合 |

> **スコープ厳守**: 変装（青に化けた敵・tell）・突撃隊・SOイベントチャネル移行・エフェクト/サウンド・本格バランス調整は **Phase 2 では実装しない**（`phases.md` Phase 2 Out of scope ／Phase 3 へ）。`EnemyTypeSO.IsDisguised` ・ `WaveConfigSO.DisguiseChance` は define-only のまま据え置く。
