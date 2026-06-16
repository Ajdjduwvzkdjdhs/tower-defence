# Phase 3 実装計画書（突撃隊 + 変装波 / Wave3）

> 本書は `spec-summary.md` / `docs/かき氷TD_プロトタイプ仕様書.md`（§9 変装・§11 Wave 構成・§19 パラメータ）/ `architecture.md` / `phases.md` / `coding-conventions.md` / `mcp-guide.md` を正本として、Phase 3 で実装する全クラス（新規＋既存改修）を **依存順** と **検証可能な単位（ユニット）** に分解したもの。完了判定の bar は `phases.md` の Phase 3 受け入れ基準。
> Phase 1（main マージ済み）／Phase 1.5（main マージ済み・実機 M5StickC Plus ライブ検証済み）／Phase 2（`phase2-allies`・受け入れ基準充足）はいずれも完了。本フェーズは **Phase 2 までの資産（`EnemyMatrix<T>` の専用行 `AddRow`／`RemoveRowAt`・ally 専用プール・per-instance tint・`PenaltyTally`・`Scoring`）を再利用して、突撃隊と変装敵を差し込む**ことを基本方針とする。

## 前提（Phase 2 後の現状＝土台）

本計画は **Phase 2 完了後**の状態を土台とする。着手前に以下を確認すること。

- **ブランチ**: Phase 2 は `phase2-allies`（main 未マージ・main より先行）。**Phase 2 を `main` にマージ後、`main` から `phase3-assault-disguise` を切る**（未マージのまま進める場合は `phase2-allies` の完了コミットを土台にする）。コミットは **ユニット単位**（MCP ツーリングは除外）。
- **EditMode ベースライン**: **110本緑**（Phase 1 の76＋シリアル16＋Phase 2 の18：`PenaltyTally` 6／`EnemyMatrix` 拡張 9／States penalty 3）。Phase 3 の新規テストはこの上に積む（**回帰ゼロが条件**）。
- **入力**: `GameManager` は `CompositeInputSource`（キーボード＋シリアル）配線済み。**Phase 3 は入力層を変更しない**（変装・突撃隊は谷選択／削り入力に手を入れない）。
- **既存の足場（define-only → 本フェーズで活用）**:
  - `EnemyTypeSO._isDisguised`（bool）/ `_moveSpeed`（float）/ `_tint`（Color）/ `_fireInterval`（float）はすべて定義済み。
  - `WaveConfigSO._disguiseChance`（float）は定義済み。`_allyType` / `_allySpawnChance` は Phase 2 で実装済み。
  - `Wave3.asset` は現状 `duration=45` / `spawnInterval=0.6` / `enemyPool=[BasicEnemy]` / `allySpawnChance=0` / `disguiseChance=0`（＝素の状態）。
  - per-instance tint（`Enemy.Init` の `MaterialPropertyBlock`）／ally 専用プール・専用行（`ChannelController.SpawnAlly`／`AddRow`）／`PenaltyTally`＋`OnAllyCaught`→`allyHitPenalty` は Phase 2 で実装済み。
  - `EnemyMatrix<T>` は `AddRow`/`RemoveRowAt`/`IndexOfRowContaining`/`RowAt` を完備（**Phase 3 は Core を改修しない**）。
- **ADR**: 最新は **ADR-17**。本計画が追加するのは **ADR-18 / ADR-19** と、ADR-3 移行判断の記録（後述）。
- **Player Setting**: 変更不要（ADR-15 の .NET Framework 設定を据え置き）。

## 0. 方針

- **Phase 3 が増やす遊び**: ①**「青に化けた敵を見破る」緊張**（青い物体が真の味方か変装敵かを tell で判断する）。②**速い敵への対応**（高プレッシャーの突撃隊を優先処理する）。Phase 2 の「ガード行（味方）を挟んだ深さ制御」に、**識別（見破り）** と **時間圧（速い発射）** を重ねる。
- **依存の向き**（Phase 1/2 と不変）: `Core`（純C#）→ `Data`(SO) → `Input`/`Factory` → `Gameplay`(MonoBehaviour) → `UI` → `Bootstrap`。Core は上位を知らない。
- **検証の二層**（Phase 1/2 同様）:
  - **EditMode テスト**（高速・自動）: 湧き種別の3択選択ロジック（`SpawnSelector`：敵／味方／変装の確率分岐と優先順位）。回帰として既存 110 本も緑。
  - **Unity-MCP 視覚検証**（`screenshot-game-view`）: 突撃隊の速い発射・見た目差／変装敵が青く出て tell を持つ・発射継続・自走退場しない／真の味方との見分け／変装敵を雪崩で撃破＝無ペナルティ／真の味方を撃つ＝減点／Wave3 通し。`mcp-guide.md` のパターンに沿う。
- **1ユニット = 「単独でビルド通過し、固有の合否判定（テスト緑 or MCPスクショ）が下せる最小の塊」**。下から積む。
- 通信は **C# event**（ADR-3 の移行判断は本フェーズで明示評価＝後述）。購読は `OnDestroy` で必ず解除。
- 調整値は SO か `[SerializeField]`（突撃隊の `fireInterval`/`moveSpeed`/`tint`＝`EnemyTypeSO`、`disguiseChance`/`disguiseType`＝`WaveConfigSO`）。Singleton は `GameManager` のみ。
- **提案する設計判断（実装時に ADR 追記）**:
  - **ADR-18（突撃隊）**: 突撃隊は **`EnemyTypeSO` のパラメータ（短い `fireInterval` ＋識別用の見た目差）だけで表現**し、**新規クラスを作らない**。`moveSpeed` は **現行の静止行モデル（ADR-12/14：座標は行インデックス基準・雪崩で繰り上げ・連続移動なし）では挙動を持たない**ため **define-only 据え置き**。受け入れ基準の「速さ」は**発射テンポの差**として解釈する（`phases.md` の基準文を「速さ＝発射テンポ」に微修正）。locomotion 追加は深さ制御の核を崩すリスクが高く、本プロトタイプの遠隔攻撃モデルでは「砦に近い＝より危険」が成立しないため不採用。
  - **ADR-19（変装）**: 変装敵は **`DisguisedEnemy : Enemy`**。同一 `EnemyMatrix<Enemy>` に多態保持し、**味方に擬態するため専用行（`AddRow`）を占有**（青・味方と同じ配置に見える）。ただし**発射する（`Projectile≠null`）／自走退場しない（`Enemy` 既定挙動＝lifetime を持たない）**。**tell（見た目差）**を持ち、注意すれば真の味方と見分けられる。**雪崩で撃破すると無ペナルティ**（＝正しい見破りを報酬）。**真の味方を撃つと既存の `allyHitPenalty`**（ADR-17 の `OnAllyCaught`）が課される。色は `Enemy.Init` の per-instance MPB（青）＋ tell マーカー子オブジェクト。
  - **ADR-3 移行判断の記録**: 受け入れ基準⑥に従い、本フェーズ着手時点で ADR-3 の3トリガー（①プレハブがシーン参照なしでイベント発火／②クロスシーン通信／③相互参照システムが5超）を評価し、**移行 or 見送りの結論と理由を `architecture.md` に記録**する（現状の見立て＝単一シーン・コード生成プリミティブ（プレハブ未使用）・`GameManager` 集約で**いずれも未成立 → 見送り**が妥当。実装時に再評価して確定）。
- **スコープ厳守**: 実機インターフェース接続（XY センサのシリアル化等）は **別計画（Phase 1.5 系）**。本フェーズでは扱わない。エフェクト/サウンド・本格バランス調整も最小限（tell とプレッシャー差が成立する範囲）。

## 1. 依存レイヤー図（Phase 2 からの差分）

```
[Bootstrap]  GameManager（唯一のSingleton）
                 │  ＋ createDisguised 注入 / Wave3 を突撃隊＋変装＋味方混在で配線
   ┌─────────────┼───────────────────────────────┐
[UI]         UIManager（不変。減点表示は Phase 2 で実装済み）
[Gameplay]   WaveManager ─→ ChannelController×4 ──┤  ＊改
                 │ ＋disguise   │  ├ AvalancheController（不変）
                 │  分岐        │  ├ Enemy（ほぼ不変。tell は派生で実装）
                 │              │  │   ├ AllyNPC（不変：青・無発射・自走退場）
                 │              │  │   └ DisguisedEnemy（新：青＋tell・発射継続・退場しない）
                 │              │  ├ disguised 専用プール / SpawnDisguised（専用行）
                 │              │  └ (uses) EnemyMatrix<Enemy>（不変・AddRow/RemoveRowAt 既存）
[Factory]    ObjectPool<T>（不変・disguised 用に3本目）, ProjectileFactory（不変）
[Input]      IInputSource / Composite（不変）
[Data(SO)]   WaveConfigSO ＊改(disguiseType) → EnemyTypeSO（不変・isDisguised/tint 活用）→ ProjectileTypeSO（不変）
[Core]       SpawnSelector（新・純ロジック）
             EnemyMatrix<T> / PenaltyTally / Scoring / States / IGameContext（すべて不変）
```

`＊改` = 既存クラスの改修。`新` = 新規クラス。矢印 = 参照／依存。**Core は `SpawnSelector` 追加のみ**で、行ロジック・スコア・State 群は不変（`EnemyMatrix<Enemy>` のジェネリック恩恵で `DisguisedEnemy` 追加の影響を受けない）。

## 2. 全クラス一覧（新規＋改修）

| # | クラス | フォルダ / 名前空間 | 種別 | 責務（Phase3スコープ） | 依存 | 主な検証 |
|---|--------|---------------------|------|------------------------|------|----------|
| C1 | `SpawnSelector` | Core | 新 | 湧き種別の純ロジック。`(allyChance, disguiseChance, hasAlly, hasDisguise, roll)` → `{Enemy/Ally/Disguised}`。確率分岐と優先順位を1箇所に集約（マジック分岐を排除） | なし | EditMode |
| D1 | `EnemyTypeSO` | Data | 不変 | 既存 `_fireInterval`/`_moveSpeed`/`_isDisguised`/`_tint` を活用（突撃隊＝短 fireInterval、変装＝青＋isDisguised） | — | MCP(資産) |
| D2 | `WaveConfigSO` | Data | 改 | `_disguiseType`（変装敵 `EnemyTypeSO`）を追加（`_allyType` と対）。`_disguiseChance` は既存 | D1 | MCP(資産) |
| G1 | `DisguisedEnemy` | Gameplay | 新 | `Enemy` を継承。**発射継続・自走退場しない**・青＋**tell マーカー**を持つ。専用行（`AddRow`）に擬態配置 | G2,D1 | MCP(Play) |
| G2 | `Enemy` | Gameplay | （ほぼ不変） | tell は派生（`DisguisedEnemy`）で実装。必要なら `Init` 後フック（`OnInitialized` 等）を `protected virtual` で1点追加 | D1 | MCP(Play) |
| G3 | `ChannelController` | Gameplay | 改 | disguised 専用プール＋`SpawnDisguised(EnemyTypeSO)`（専用行・擬態）。雪崩が変装行に達したら**無ペナルティで撃破**（`AllyNPC` 判定の外＝既存ロジックで自動成立）。`Configure` に `createDisguised` 追加 | C(なし),G1 | MCP |
| G4 | `WaveManager` | Gameplay | 改 | `SpawnSelector` を用いて毎湧き機会に 敵／味方／変装 を選択し、対応する Spawn を呼ぶ。`disguiseChance==0` の Wave1/2 は従来動作（回帰なし） | C1,D2,G3 | MCP |
| B1 | `GameManager` | Bootstrap | 改 | `createDisguised`（青＋tell キューブ生成デリゲート）を `ChannelController.Configure` に注入。Wave3 を突撃隊＋変装＋味方混在で通し駆動。ADR-3 移行判断を実装時に記録 | 全部 | MCP(通し) |

> **ペナルティ経路は不変**: 変装敵を雪崩で撃破 = `AllyNPC` ではない = 既存 `ApplyAvalanche` の else 分岐で通常撃破（無ペナルティ）。真の味方を撃つ = 既存 `OnAllyCaught`→`allyHitPenalty`（ADR-17）。**`PenaltyTally`／`Scoring`／States は一切触らない**。

## 3. 検証可能な単位（ユニット）と実装順序

各ユニットは **「完了条件」を満たした時点で次へ進む**。完了条件は EditMode 緑または MCP スクショ（`mcp-guide.md`）で客観確認する。コミットは **ユニット単位**（土台＝`main` から切った `phase3-assault-disguise`／MCP ツーリングは除外）。

### マイルストーン A — Core / 純ロジック（Unity 実行不要・EditMode で完結）

**Unit D1: `SpawnSelector`（C1）**
- 設計判断: Phase 2 まで `WaveManager` 内に `Random.value < AllySpawnChance` をインラインで持っていたが、Phase 3 で **敵／味方／変装の3択＋優先順位**が増えるため、純ロジックに切り出して EditMode 検証可能にする（ADR-8 を踏襲）。`SpawnSelector.Select(allyChance, disguiseChance, hasAlly, hasDisguise, roll)`（`roll`∈[0,1) を引数注入＝決定的にテスト）→ `SpawnKind{Enemy, Ally, Disguised}`。優先順位は **変装 → 味方 → 敵**（または明示した規約。`disguiseType`/`allyType` が null の場合はその種別をスキップ）。
- 成果物: `Assets/Scripts/Core/SpawnSelector.cs`（純 C#・UnityEngine 非依存・ADR-8）。`enum SpawnKind` を併設。
- 依存: なし
- 完了条件 (EditMode): `roll<disguiseChance かつ hasDisguise → Disguised` / `次に roll が allyChance 帯 → Ally` / `どちらにも当たらない → Enemy` / `hasDisguise=false なら Disguised を返さない` / `hasAlly=false なら Ally を返さない` / `chance=0 同士で常に Enemy（Wave1/2 回帰）`。→ 受け入れ基準②⑤の湧き分岐ロジック保証。

### マイルストーン B — データ（SO）

**Unit D2: 突撃隊データ（D1）＋ Wave3 に突撃隊投入**
- 成果物:
  - `Assets/ScriptableObjects/AssaultEnemy.asset`（`EnemyTypeSO`）: 短い `_fireInterval`（例 **0.8**・通常 2.0）／識別用の `_tint`（例 **オレンジ** `r1,g0.5,b0.1`・赤敵と区別）／`_projectile=BasicProjectile`／`_isAlly=false`／`_isDisguised=false`／`_moveSpeed` は据え置き（define-only）。
  - `Wave3.asset` の `_enemyPool` に `AssaultEnemy` を追加（`[BasicEnemy, AssaultEnemy]`）。
- 完了条件 (MCP): `assets-get-data` で `AssaultEnemy` の `fireInterval=0.8`／`tint`／`projectile` 参照、`Wave3.enemyPool` に2種含むことを確認。Inspector で編集可能。→ 受け入れ基準①の突撃隊データ土台。

**Unit D3: 変装データ（D2）＋ Wave3 を変装＋味方混在へ**
- 設計判断: 見破りジレンマには **真の味方（青・自走退場）と変装敵（青・退場しない）が同時に出る**ことが必須。Wave3 は両方を混在させる。
- 成果物:
  - `WaveConfigSO` に `[SerializeField] EnemyTypeSO _disguiseType` ＋ `DisguiseType` プロパティ（`_allyType` と対）。
  - `Assets/ScriptableObjects/DisguisedEnemy.asset`（`EnemyTypeSO`）: `_isDisguised=true`／`_tint=青`（味方と同色 `r0,g0,b1`）／`_projectile=BasicProjectile`（発射する）／`_fireInterval=2.0`（通常）／`_isAlly=false`／`_allyLifetime` は無効（退場しないため未使用）。
  - `Wave3.asset`: `_disguiseChance>0`（例 **0.2**）＋ `_disguiseType=DisguisedEnemy`、加えて `_allySpawnChance>0`（例 **0.15**）＋ `_allyType=AllyNPC`（真の味方も混ぜる）。
- 完了条件 (MCP): `assets-get-data` で `DisguisedEnemy` の `isDisguised=true`/`tint=青`/`projectile≠null`、`Wave3` の `disguiseType`/`disguiseChance`/`allyType`/`allySpawnChance` を確認。→ 受け入れ基準②⑤のデータ土台。

### マイルストーン C — 挙動（手触りの核）

**Unit D4: 突撃隊の体感（D1 のデータ検証＝コード変更なし）**
- 設計判断（ADR-18）: 突撃隊は**データのみ**（短 fireInterval ＋ tint）。挙動コードは追加しない（per-instance tint は Phase 2 で実装済みのため色も自動反映）。
- 成果物: なし（D2 のアセットを MCP で挙動確認するユニット）。
- 完了条件 (MCP Play): Wave3（または一時的に突撃隊のみの検証構成）で突撃隊が**通常敵より明らかに高頻度で発射**（弾ストリームが密）し、**オレンジ色で区別**できることを `screenshot-game-view`＋`ShotsFired`／砦HP 減少レートで確認（例: 通常 1発/2s に対し突撃隊 ≈1発/0.8s）。→ 受け入れ基準①。

**Unit D5: `DisguisedEnemy : Enemy`（G1）＋ `Enemy` の最小フック（G2）**
- 設計判断（ADR-19）: 変装敵は敵の派生。**発射は基底 `Enemy.Update` のまま**（`Projectile≠null` なので撃つ）。**自走退場しない**（`AllyNPC` と違い lifetime カウントを持たない）。**tell** は `DisguisedEnemy` が `Init` 時に**マーカー子オブジェクト**（例: 立方体面の小さな暗色クアッド／ノッチ）を付け、真の味方（マーカーなし）と見分けられるようにする。tell は**静止マーカーを主**とし（`screenshot-game-view` で検証可能）、副次的に緩い明滅/上下動を任意で添える（理不尽でない＝注意すれば分かる）。色の青は per-instance MPB（`type.Tint`）で自動。`Enemy` 側は必要なら `Init` 末尾で `protected virtual void OnInitialized()` を1点呼ぶだけ（tell 生成フック）。
- 成果物: `Assets/Scripts/Gameplay/DisguisedEnemy.cs`（`OnInitialized` で tell マーカー生成・`Kill`/プール返却時に隠す）。`Enemy.cs`: `Init` 末尾に `OnInitialized()` 仮想呼び出しを追加（既定は空。`AllyNPC` の挙動は不変）。
- 完了条件 (MCP Play): 変装敵を1体生成し、**青く出る**が**tell マーカーが視認できる**（味方と並べてスクショ比較）／**弾を撃つ**（`ShotsFired>0`・砦HP 減）／**`allyLifetime` 相当の時間が過ぎても退場しない**（生存・発射継続）。赤敵⇄青味方⇄青変装がプール再利用で色・マーカー混在しない（per-instance）。→ 受け入れ基準②③④。

**Unit D6: `ChannelController` 変装対応（G3）＋ `WaveManager` 変装分岐（G4）**
- 設計判断（ADR-19）: 谷は `_pool`（Enemy）・`_allyPool`（AllyNPC）に加え `_disguisedPool`（DisguisedEnemy）を持つ。`SpawnDisguised` は **`EnemyMatrix.AddRow` で専用行**に置き（味方に擬態）、ただし `OnExpired` は購読しない（退場しない）。`ApplyAvalanche` は**改修不要**: 変装敵は `AllyNPC` ではないので既存の else 分岐で通常撃破（`OnAllyCaught` 未発火＝無ペナルティ）。`WaveManager` は `SpawnSelector` の結果で `SpawnEnemy`／`SpawnAlly`／`SpawnDisguised` を呼び分ける（満杯谷は次を試す既存ロジックを踏襲）。
- 成果物: `ChannelController.cs` に `_disguisedPool`・`SpawnDisguised(EnemyTypeSO)`・型別リリース（`e is DisguisedEnemy` を含む。ただしペナルティ分岐は `AllyNPC` のみ）を追加。`Configure` に `Func<DisguisedEnemy> createDisguised` を追加。`WaveManager.SpawnOne` を `SpawnSelector` ベースに再構成。
- 完了条件 (MCP Play, mcp-guide パターンA/D):
  - ①Wave3 の谷に **青い味方** と **青い変装敵（tell 付き）** が混在して出現する。
  - ②**変装敵を見逃す（削らず待つ）と、自走退場せず砦を攻撃し続ける**（味方は退場するのに変装は残り発射継続）→ 砦HP が減り続ける。
  - ③**変装敵を雪崩で撃破すると無ペナルティ**（`PenaltyTally.Total` 不変）。
  - ④**真の味方を雪崩で撃つと減点**（`OnAllyCaught`→`allyHitPenalty`、Phase 2 経路の回帰確認）。
  - ⑤Wave1/2 では変装が一切出ない（`disguiseChance=0` の回帰）。
  - 各局面をスクショ記録。→ 受け入れ基準②③の中核＋①⑤。

### マイルストーン D — 配線・統合・移行判断（Phase 3 完了判定）

**Unit D7: `GameManager` 配線（B1）＋ ADR-3 移行判断＋ Wave3 通し統合検証**
- 成果物:
  - `createDisguised`（青＋tell キューブ生成デリゲート。`CreateAlly` と対）を `ChannelController.Configure` に注入。
  - Wave3（`_waveConfigs[2]`）が突撃隊＋変装＋味方混在で通し駆動することを確認（追加の状態リセットは Phase 2 で実装済みのため新規配線は最小）。
  - **ADR-3 移行判断の記録**: 着手時点で3トリガーを評価し、結論（見送り想定）＋理由を `architecture.md` に記録（移行する場合は `*EventChannelSO` 化のスコープを別途切る）。
- 完了条件 (MCP): `_input`/配線がコードレビューで **Singleton=`GameManager` のみ・入力は `IInputSource` 経由**を維持。`createDisguised` 注入で青＋tell が出ることを Play で確認。→ 受け入れ基準②③＋鉄則順守。

**Unit D7（続き）: Wave3 通し統合検証（Phase 3 完了判定）**
- 完了条件: `phases.md` Phase 3 受け入れ基準を **全チェック**。
  - EditMode 全緑（`SpawnSelector` ＋ Phase 1/1.5/2 の既存テスト＝回帰なし。累計本数を記録：**110＋`SpawnSelector` 数本**）。
  - MCP 通しプレイ（Wave3）:
    - **突撃隊**が `EnemyTypeSO` 定義で出現し、**発射テンポ（と色）の差が体感できる**（受け入れ基準①。「速さ＝発射テンポ」と解釈・ADR-18）。
    - **変装敵**が青（味方色）＋ **tell** を持ち、`disguiseChance` で出現する（②）。
    - 変装敵を**見逃すと退場せず攻撃継続**／**真の味方を撃つと減点**（③）。
    - **tell（見た目差）**が実装され、注意すれば見分けられる（**理不尽でない**＝味方/変装を並べたスクショで識別可能なことを示す）（④）。
    - **Wave3 構成（突撃隊＋変装＋味方）が通しで遊べる**（⑤）。
    - **ADR-3 トリガー評価結果**を `architecture.md` に記録（移行 or 見送りの理由）（⑥）。
  - 各局面のスクショを記録。完了後 `phases.md` Phase 3 チェックリストと Status を更新、`architecture.md` に **ADR-18 / ADR-19** ＋ ADR-3 判断を追記。

## 4. クリティカルパス（最短で「見破りの手触り」に到達する線）

```
D2(突撃隊データ) ─→ D4(突撃隊の体感) … 速い発射＋色で「速い敵」が成立（コード不要・最短）

D1(SpawnSelector) ─┐
D3(変装データ)    ─┼→ D5(DisguisedEnemy: 青+tell/発射継続/退場しない)
                   └→ D6(Channel: SpawnDisguised + WaveManager 分岐)
                        → MCP で「見逃すと攻撃継続 / 変装を撃つ＝無ペナ / 真の味方を撃つ＝減点 / tell で見分け」を検証
```
**最重要は「青い味方 vs 青い変装敵を tell で見破る」手触り**（D3→D5→D6）。ここを最初に MCP で通せば、Phase 3 のゲーム性（識別と時間圧）が成立するかを早期に確認できる。突撃隊（D2/D4）はデータのみで独立に並行可能・最短で1項目を満たせる。

## 5. 要確定事項（着手前に解消 or 既定値で進行）

| 項目 | 推奨既定 | 影響 |
|------|----------|------|
| 突撃隊の「速さ」表現 | **発射テンポで表現（locomotion なし）＝ ADR-18**（確定: ユーザー選択）。`moveSpeed` は define-only 据え置き、基準文を「速さ＝発射テンポ」に微修正 | 計画構造（移動サブシステムの有無） |
| 突撃隊の見分け | `tint`＝オレンジ（赤敵と区別）／`fireInterval`≈0.8 | 「速い敵」の体感・識別 |
| 変装敵の実装形 | `DisguisedEnemy : Enemy`（専用行で味方に擬態・発射継続・退場しない）＝ ADR-19 | 行/プール再利用度・ペナルティ経路の無改修 |
| tell（見た目差）の具体 | **静止マーカー（小さな暗色クアッド/ノッチ）を主**＋任意で緩い明滅/上下動。味方=マーカーなし | 「理不尽でない」識別性・スクショ検証可否 |
| 変装の行占有 | 専用行（`AddRow`・味方と同じ見た目）。`OnExpired` 非購読（退場しない） | 擬態の説得力・撃破時の無ペナルティ成立 |
| 湧き種別の選択 | `SpawnSelector`（Core・純ロジック）に集約。優先順位＝変装→味方→敵、null 種別はスキップ | EditMode 検証可否・回帰安全性 |
| `disguiseChance`/`allySpawnChance`（Wave3） | 0.2 / 0.15（プレイテストで調整）。突撃隊は `enemyPool` 経由 | Wave3 の難度・見破り頻度 |
| ペナルティ新設の要否 | **新設しない**。変装撃破＝無ペナ（通常撃破）／真味方撃破＝既存 `allyHitPenalty` | スコア経路の無改修（ADR-17 流用） |
| ADR-3（SOイベントチャネル移行） | 着手時に3トリガー評価 → **見送り想定**（単一シーン・プレハブ未使用・GameManager 集約）。結論を `architecture.md` に記録 | 受け入れ基準⑥の充足 |

> **スコープ厳守**: 実機インターフェース接続（XY センサのシリアル化・送信コマンド体系）は **別計画**。本フェーズでは扱わない。`moveSpeed` による locomotion は ADR-18 で不採用（将来要件が出たら攻撃方式 Strategy 化／移動サブシステムを別 ADR で検討）。
