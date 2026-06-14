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

---

## Phase 2 — 味方NPC（深さ制御ジレンマ）/ Wave2

**ゴール**: 味方を巻き込まずに敵を捌く判断（待つ/手前で止める/谷を変える）が成立する。

### In scope

- `AllyNPC`（一時出現・自走退場）、巻き込みペナルティ、Wave2 構成。

### Out of scope

- 変装、突撃隊。

### 受け入れ基準

- [ ] 青の味方NPCが `WaveConfigSO.allySpawnChance` で出現し、`allyLifetime` 秒で自走退場する
- [ ] 味方が居る行に雪崩が達すると巻き込み、`ScoreManager` に `allyHitPenalty` が加算（減点）される
- [ ] 味方の**手前の敵**は手前で削りを止めれば**ノーペナルティで撃破**できる（深さ制御）
- [ ] 味方の**奥の敵**は味方退場後に撃破できる（恒久ロックされない＝一方的に撃たれ続けない）
- [ ] Wave2 構成（敵＋味方混在）が `WaveConfigSO` で定義され、通しで遊べる
- [ ] EditMode テスト緑: 味方ガード下の深さ制御、ペナルティ加算ロジック

### Status: ⬜ 未着手

---

## Phase 3 — 突撃隊 + 変装波 / Wave3

**ゴール**: 「青に化けた敵を見破る」緊張と、速い敵への対応が成立する。

### In scope

- 突撃隊（高 `moveSpeed`／短 `fireInterval`）、変装敵（青＋tell）、Wave3 構成。
- 通信が ADR-3 の移行トリガーに達していれば SOイベントチャネルへの移行を検討/実施。

### Out of scope

- 実機インターフェース接続（別計画）。

### 受け入れ基準

- [ ] 突撃隊が `EnemyTypeSO` で定義され、速さ/発射間隔の差が体感できる
- [ ] 変装敵は青（味方色）＋ tell を持ち、`disguiseChance` で出現する
- [ ] 変装敵を見逃すと退場せず攻撃を継続する／真の味方を撃つとペナルティになる
- [ ] tell（見た目差）が実装され、注意すれば見分けられる（理不尽でない）
- [ ] Wave3 構成（突撃隊＋変装）が通しで遊べる
- [ ] ADR-3 トリガー成立時、SOイベントチャネル移行の判断結果を `docs/architecture.md` に記録（移行 or 見送りの理由）

### Status: ⬜ 未着手

---

## 既知の TBD（フェーズ進行中に確定）

- 味方/敵/変装の**形**のデザイン
- 変装の tell（見た目差）の具体
- バランス数値（`iceTotal`, `projectile.damage`, `fireInterval`, `spawnInterval` 等）のプレイテスト調整
- 弾の見た目・着弾エフェクト
- Unity バージョンの確定（CLAUDE.md の TODO）
