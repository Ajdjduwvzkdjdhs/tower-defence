# フェーズ毎のゴール (Phases)

各フェーズの**受け入れ基準を完了判定の bar** とする。実装後はこの基準 + EditMode テスト + Unity-MCP検証で確認してから報告。進捗チェックボックスは Claude Code が更新する。

## 全フェーズ共通の Definition of Done

- [ ] 該当フェーズの受け入れ基準をすべて満たす
- [ ] 対応する EditMode テストが緑
- [ ] `docs/coding-conventions.md` / `docs/architecture.md` に準拠（Singleton は GameManager のみ、入力は IInputSource 経由、調整値は SO、弾はプール）
- [ ] 変更した設計判断があれば `docs/architecture.md` に ADR を追記、関連 docs を更新
- [ ] Unity-MCP を使った視覚検証を適切に行っている（特にPhase 1）

---

## Phase 1 — Wave1 の核（味方なし）　【現在のフェーズ】

**ゴール**: 「削る→雪崩→殲滅」の手触りと、資源(i)＋注意(ii)の判断が成立するかを検証できる最小の遊べる状態。

### In scope

- 放射状4谷・砦・敵マトリックス・弾・氷ストック・砦HP・単純なUI・1〜数Wave分の進行。

### Out of scope（やらない）

- 味方NPC、変装、突撃隊、SOイベントチャネル移行、エフェクト/サウンド、本格的なバランス調整、実機インターフェース接続。

### 実装コンポーネント

`GameManager`(Composition Root) / `GameStateMachine`+各State / `WaveManager` / `ChannelController`×4(敵のみ) / `EnemyMatrix`(Core) / `Enemy` / `Projectile` / `ProjectileFactory` / `ObjectPool` / `AvalancheController` / `IceStock`(Core) / `Fort` / `Scoring`(Core) / `KeyboardInputSource` / `UIManager`。通信は C# event。

### 受け入れ基準

- [x] 4谷が放射状に表示され、`W/A/S/D` で照準谷が切り替わり UI でハイライトされる（同時1本のみ）（マイルストーンE / `UIManager` 谷ハイライト + Play検証）
- [x] `Space` 長押し中、アクティブ谷の先頭行が `killInterval` ごとに消える。判定開始は `avalancheDelay` 後（初期値0＝即時）（マイルストーンD / `AvalancheClock` 7本 + Play検証）
- [x] 削り続けると先頭→奥へ進む（深さ制御）。削りを止める/谷を変えると進行が止まる（マイルストーンD / Play検証: 北谷9→0、入力解放で停止、谷変更で `clock.Reset`）
- [x] 氷ストックが削り中に `iceDrainPerSec` で減り、0で削れない。UI にバー表示（マイルストーンD+E / Play検証: 氷 50→0、枯渇で不可、`UIManager` 氷バー）
- [x] 各谷で敵が最奥に湧き、`fireInterval` ごとに弾を発射。弾は麓→山頂へ飛び、着弾で `Fort.TakeDamage`（マイルストーンC+D / `ChannelController.SpawnEnemy` + Play検証）
- [x] 弾は `ObjectPool` で使い回され、着弾でプール返却（ホットパスに `Instantiate/Destroy` が無い）（マイルストーンC / Play Mode検証: 着弾後 GameObject 非アクティブ・破棄なし）
- [x] 敵を倒すと以後の発射が止まる（飛来中の弾は着弾する）（マイルストーンC / Play Mode検証: `Enemy.Kill()` 後 11秒で発射ゼロ）
- [x] 砦HP が着弾で減り、0で `Fort.OnDestroyed` → Result(Lose)（マイルストーンE / Play検証: Wave2 で HP0→FORT DESTROYED）
- [x] `WaveConfigSO.duration` 経過で Wave 終了 → 残敵消去 → 次へ/Result。3Wave到達で Result(Win)（マイルストーンE / Play検証: Wave1→Lull→Wave2、3波生存→YOU WIN）
- [x] スコア＝終了時の `Fort.CurrentHp` が Result に表示（マイルストーンE / Play検証: Lose=0 / Win=残りHP）
- [x] `avalancheDelay` / `killInterval` / `iceTotal` / `fortHpMax` 等が Inspector もしくは SO で調整できる（`AvalancheController` の `[SerializeField]` / `Fort._maxHp` / `IceStock(total)`）
- [x] **Unity-MCP経由でPlay Modeを起動し、雪崩ストリーム・敵の湧き・弾の飛行・UI表示が正しく動作していることを`screenshot-game-view`で確認済み**（マイルストーンA〜E通し）
- [x] **主要な状態（照準中の谷ハイライト、氷ストック減少、砦HP減少、Wave終了時処理）をUnity-MCPのスクリーンショットで記録・検証済み**（Title/Wave/削り/Lull/Lose/Win のスクショ）
- [x] EditMode テスト緑: `EnemyMatrix`/`IceStock`/`Scoring`/`GameStateMachine`+States ＋ `ObjectPool`/`Fort`/`AvalancheClock`/`SpawnClock`（**全76本緑** 2026-06-14）
- [x] Singleton は `GameManager` のみ。入力は `IInputSource` 経由（`GameManager` が唯一の `Instance`、入力は `KeyboardInputSource`→`IInputSource` 経由）

### Status: 🟢 Phase 1 完了 — マイルストーンA〜F 達成。受け入れ基準すべて充足。EditMode **76本緑**。
`GameManager`（唯一のSingleton/Composition Root）が全システムを生成・配線し `GameStateMachine` を駆動。Wave1 通しを Unity-MCP Play Mode で検証済み：Title→開始→Wave1/2/3（湧き・発射・砦HP減・タイマー）→`W/A/S/D`照準＋`Space`削りで雪崩（先頭行消去・深さ制御・氷消費）→`duration`でWave終了・残敵消去→次Wave、砦HP0で Result(Lose)、3Wave生存で Result(Win)、スコア=残り砦HP、Enter で再挑戦。UI は `UIManager`（氷/砦HPバー・谷ハイライト・Waveステータス・リザルト）。
**Phase 1 スコープ外（Phase 2 以降）**: 味方NPC・変装・突撃隊、SOイベントチャネル移行、エフェクト/サウンド、本格バランス調整、実機インターフェース接続。見た目は URP/Unlit プリミティブのプレースホルダ（美術は後続）。

**完了後ポリッシュ（2026-06-15）**: ①画面外の敵から攻撃される不具合を修正（谷の座標を行インデックス基準にし上限行数を設定＝敵は常に画面内、**ADR-14**）。②照準中の谷を画面内の発光レーンで明示（削り中は明るく）。③雪山の俯瞰背景（頂上=中央/砦・麓=外周・等高線）を追加し、敵が麓に居ることを視覚化。

**グラフィック調整（2026-06-15）**: 山を「緑の同心円（弓道の的状・中央=頂上の砦 → 外側ほど濃い緑）」、その一番外側の円(r≈2.2)より外を「茶色い地面（麓）」に再配色。敵の湧きを一番外側の円の外＝麓の地面へ移動（`ChannelController._frontGap` 1.6→2.7 / `_rowSpacing` 0.85→0.55、最奥行でも y±4.6 でカメラ視野±5 内に収まる）。配置方針（行インデックス基準＋行数上限）は **ADR-14** のまま、数値のみ調整。EditMode 76本緑・Unity-MCP Play 検証済み（敵 minR=2.70 で山の縁≈2.36 の外、maxAbsY=4.35<5）。

---

## Phase 1.5 — 実機シリアル接続（ハンドル回転 → 削り）／別計画　【🟢 コード実装・検証済み】

**ゴール**: ADR-6 の `IInputSource` 差し替え方針どおり、実機（物理かき氷機）の**ハンドル回転**を Arduino 経由シリアルで取得し、削り（`IsShaving`）として読む足場を入れる。XY（谷選択）・決定はキーボード据え置き、Composite で併用。詳細計画は `docs/phase1.5(serial)-plan.md`、設計判断は **ADR-15**。

### In scope

- 自前シリアル transport（`System.IO.Ports` 別スレッド／Ardity 不使用）、`SerialConfigSO`、純ロジック `SerialInputProtocol`（行→削り＋watchdog）、`SerialInputSource`、`CompositeInputSource`、`GameManager` 配線、Unity→Arduino 汎用送信口（`SerialHandler.Send`）。

### Out of scope（やらない）

- 実機 **XY センサ**のシリアル化、送信コマンド体系の確定（汎用 `Send` 口のみ）、味方NPC など Phase 2/3 の要素。

### 受け入れ基準

- [x] `CompositeInputSource` が KB+シリアルを正しくマージ（`ActiveChannel`/`ConfirmPressed`=KB、`IsShaving`=OR）（EditMode 5 本）
- [x] `SerialInputProtocol` が `1`/`0` を解析、無受信 watchdog で `IsShaving=false`、不正行無視（EditMode 11 本）
- [x] `SerialConfigSO` で `portName`/`baudRate` 等を Inspector 調整可（マジックナンバーなし）
- [x] `SerialHandler` が別スレッド受信＋ `Send` 送信、`OnDestroy`/`OnApplicationQuit` で安全解放／未接続ポートでも警告のみで落ちない・固まらない（MCP smoke 検証）
- [x] **キーボード操作が回帰しない**（`_input`＝`CompositeInputSource`、`AvalancheController` に同一注入、Play で KB→Composite の `IsShaving` OR を実証）
- [x] Standalone の API Compatibility Level を .NET Framework に変更（`System.IO.Ports` 利用の前提・ADR-15）
- [x] EditMode テスト緑（**全92本**：従来76＋シリアル16）／`architecture.md` に ADR-15 追記
- [x] 実機（**M5StickC Plus** / `/dev/cu.M5StickCPlus` / 9600baud）でハンドル回転 → 雪崩を Play + screenshot で実証（**ライブ検証済み 2026-06-16**）

### Status: 🟢 完了（実機ライブ検証済み）

`SerialConfigSO`/`SerialHandler`/`SerialInputProtocol`/`SerialInputSource`/`CompositeInputSource` を実装し `GameManager` が Composite を配線。`SerialConfigSO` 未割り当ての既定シーンはキーボードのみで Phase 1 同様に動作（回帰なし）。EditMode 92本緑、Unity-MCP で未接続 smoke・配線・KB→Composite OR を検証。**実機 M5StickC Plus（`/dev/cu.M5StickCPlus`）でハンドル回転 → `IsShaving` → 選択谷の雪崩が走る／止まるをライブ検証済み（2026-06-16）**。配線は `BTSerialConfig.asset`（`SerialConfigSO`）を `GameManager._serialConfig` に割り当て。Unity→Arduino の `Send` は送信トリガ未配線のため smoke 未実施＝将来 consumer 実装時の拡張ポイント（受信パスは実機検証済み）。

---

## Phase 2 — 味方NPC（深さ制御ジレンマ）/ Wave2

**ゴール**: 味方を巻き込まずに敵を捌く判断（待つ/手前で止める/谷を変える）が成立する。

### In scope

- `AllyNPC`（一時出現・自走退場）、巻き込みペナルティ、Wave2 構成。

### Out of scope

- 変装、突撃隊。

### 受け入れ基準

- [x] 青の味方NPCが `WaveConfigSO.allySpawnChance` で出現し、`allyLifetime` 秒で自走退場する
- [x] 味方が居る行に雪崩が達すると巻き込み、`ScoreManager` に `allyHitPenalty` が加算（減点）される
- [x] 味方の**手前の敵**は手前で削りを止めれば**ノーペナルティで撃破**できる（深さ制御）
- [x] 味方の**奥の敵**は味方退場後に撃破できる（恒久ロックされない＝一方的に撃たれ続けない）
- [x] Wave2 構成（敵＋味方混在）が `WaveConfigSO` で定義され、通しで遊べる
- [x] EditMode テスト緑: 味方ガード下の深さ制御、ペナルティ加算ロジック

### Status: 🟢 マイルストーン C/D 完了（2026-06-16）— Phase 2 全受け入れ基準充足

EditMode **110本緑**（+18: PenaltyTally 6 / EnemyMatrix 拡張 9 / States penalty 3）。
| Unit | 成果物 | 検証 | 状態 |
|---|---|---|---|
| P1 | `PenaltyTally`（Core） | PenaltyTallyTests **6本緑** | ✅ |
| P2 | `EnemyMatrix<T>` 拡張（`AddRow`/`RemoveRowAt`・隔離行） | EnemyMatrixTests **9本追加＝21本緑**（ガード行シナリオ含む） | ✅ |
| P3 | `IGameContext.PenaltyTotal` ＋ `WavePlayingState` スコア配線 | StatesTests **3本追加緑**（win/lose ペナルティ・回帰） | ✅ |
| P4 | `EnemyTypeSO.AllyLifetime` / `WaveConfigSO.AllyType` / `AllyNPC.asset` / `Wave2.asset` 味方混在 | MCP assets-get-data 全値確認 | ✅ |
| P5 | `Enemy.Update` virtual + per-instance tint + `AllyNPC` 自走退場 | MCP Play: 青キューブ・IsAlive/ShotsFired=0 確認 | ✅ |
| P6 | `ChannelController` 味方対応（SpawnAlly/OnAllyCaught/自走退場ハンドラ） | MCP Play: caught=0→1（手前ノーペナ/巻き込み）・奥e2解放 | ✅ |
| P7 | `WaveManager` 味方湧き分岐（AllySpawnChance） | MCP Play: 40回で AllyNPC≈20%（期待値25%） | ✅ |
| P8 | `GameManager` penalty 配線 + UIManager ペナルティ表示 | MCP Play: PenaltyTotal 0→0→50（手前ノーペナ/巻き込み+50） | ✅ |
| P9 | Wave2 通し統合検証 | MCP Play: 4谷に敵＋味方混在・ガード行構造・ShotsFired=0 確認 | ✅ |

---

## Phase 3 — 突撃隊 + 変装波 / Wave3

**ゴール**: 「青に化けた敵を見破る」緊張と、速い敵への対応が成立する。

### In scope

- 突撃隊（短 `fireInterval`＝高発射テンポ、**ADR-18: `moveSpeed` による locomotion は採用しない。「速さ」＝発射テンポの差で体感させる**）、変装敵（青＋tell）、Wave3 構成。
- 通信が ADR-3 の移行トリガーに達していれば SOイベントチャネルへの移行を検討/実施。

### Out of scope

- 実機インターフェース接続（別計画）。

### 受け入れ基準

- [x] 突撃隊が `EnemyTypeSO` で定義され、**発射テンポ（短 `fireInterval`）と見た目（tint）の差が体感できる**（ADR-18）
- [x] 変装敵は青（味方色）＋ tell を持ち、`disguiseChance` で出現する
- [x] 変装敵を見逃すと退場せず攻撃を継続する／真の味方を撃つとペナルティになる
- [x] tell（見た目差）が実装され、注意すれば見分けられる（理不尽でない）
- [x] Wave3 構成（突撃隊＋変装）が通しで遊べる
- [x] ADR-3 トリガー成立時、SOイベントチャネル移行の判断結果を `docs/architecture.md` に記録（移行 or 見送りの理由）

### Status: 🟢 Phase 3 完了 — マイルストーン D 達成（2026-06-17）

EditMode **120本緑**（回帰ゼロ）。Singleton=`GameManager` のみ・入力=`IInputSource` 経由を維持。ADR-18/19 + ADR-3 移行判断を `architecture.md` に記録。
| Unit | 成果物 | 検証 | 状態 |
|---|---|---|---|
| D1 | `SpawnSelector`（Core）＋ `SpawnKind` enum | SpawnSelectorTests **10本緑** | ✅ |
| D2 | `AssaultEnemy.asset`（EnemyTypeSO）＋ Wave3 に追加 | `assets-get-data` で `fireInterval=0.8`・オレンジ tint・projectile 参照確認 | ✅ |
| D3 | `WaveConfigSO._disguiseType` 追加 ＋ `DisguisedEnemy.asset` ＋ Wave3 全設定 | `assets-get-data` で全値確認・EditMode 120本緑（回帰ゼロ） | ✅ |
| D4 | 突撃隊 体感検証（コード変更なし） | MCP Play: ShotsFired 比較で 2.5倍（0.8/2.0 理論値）・オレンジ色区別確認 | ✅ |
| D5 | `Enemy.OnInitialized` フック ＋ `DisguisedEnemy.cs`（青＋tell・発射継続・退場しない） | MCP Play: 青キューブ＋暗色 tell マーカー視認・ShotsFired>0・AllyNPC の挙動回帰なし | ✅ |
| D6 | `ChannelController` 変装プール＋`SpawnDisguised` ＋ `WaveManager` SpawnSelector 分岐 ＋ `GameManager` 注入 | MCP Play: 赤/オレンジ/青混在・変装退場しない・変装雪崩→PenaltyTotal delta=0 | ✅ |
| D7 | `architecture.md` ADR-18/ADR-19/ADR-3 移行判断記録 ＋ Wave3 通し統合検証 | EditMode **120本緑**（回帰ゼロ）。MCP Play: ①赤/オレンジ/青＋tell マーカー混在スクショ確認 ②変装雪崩 PenaltyDelta=0（PASS）③真味方雪崩 PenaltyDelta=50（PASS）。Singleton/IInputSource 経路維持。ADR-3 見送り記録 | ✅ |

---

## 既知の TBD（フェーズ進行中に確定）

- 味方/敵/変装の**形**のデザイン
- 変装の tell（見た目差）の具体
- バランス数値（`iceTotal`, `projectile.damage`, `fireInterval`, `spawnInterval` 等）のプレイテスト調整
- 弾の見た目・着弾エフェクト
- Unity バージョンの確定（CLAUDE.md の TODO）
