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

## Extra 1 — 手触り調整・不具合修正（フェーズ外）

**ゴール**: Phase 3 完了後に挙がった 3 点の手触り調整・不具合修正。詳細計画は `docs/extra1-plan.md`。ブランチ: `extra1-feel-fixes`。

| # | 課題 | 対処 | 状態 |
|---|------|------|------|
| ① | 敵の初弾が遅い（2秒待ちになっていた） | `EnemyTypeSO._initialFireDelay`（既定0）を追加、`Enemy.Init` で初弾タイマを即時化。変装敵は 2.0 秒遅延を維持 | 🟢 完了 |
| ② | 撃破行の見え方がおかしい（手前が残り外周が消える錯覚） | 繰り上がり前進を速度ベース lerp 化（湧きはスナップ）＋撃破位置に雪しぶき `SnowPuff`（プール） | 🟢 完了 |
| ③ | 波の移行を画面中央に | WaveIntro/WaveLull を中央バナー（半透明帯＋波名）。`WaveConfigSO._displayName` 追加 | 🟢 完了 |

### Status: 🟢 完了（マイルストーン A・B・C すべて達成）

**マイルストーン A 完了（2026-06-17）** — EditMode **120本緑**（回帰なし）。
| Unit | 成果物 | 検証 | 状態 |
|---|---|---|---|
| A1 | `EnemyTypeSO._initialFireDelay`（既定0）追加 ＋ `Enemy.Init` 改修 | MCP Play: ShotsFired=1〜8（3秒）、Fort HP 急減を screenshot 確認 | ✅ |
| A2 | BasicEnemy/AssaultEnemy=0、DisguisedEnemy=2.0 に設定 | `assets-get-data` で 3 アセットの値を確認 | ✅ |

**マイルストーン B 完了（2026-06-17）** — EditMode **120本緑**（回帰なし）。コミット `f54a298`。
| Unit | 成果物 | 検証 | 状態 |
|---|---|---|---|
| B1 | `Enemy.SetTarget/MoveTowardTarget`（lerp）・`AllyNPC.Update` 明示呼出・`ChannelController._advanceSpeed`・`RepositionAll(justSpawned)` | MCP Play: ApplyAvalanche 後 SnowPuff 3体が active=True で撃破位置に出現、ゲーム継続 | ✅ |
| B2 | 新規 `SnowPuff`（ObjectPool）・`ChannelController.Configure` に `createPuff` 追加・`ApplyAvalanche` で puff 発生・`GameManager.CreateSnowPuff` | MCP Play: killed=3 → SnowPuff 3体 active=True、pos=撃破座標に一致（`(-0.70,2.70)` / `(0.70,2.70)` / `(0.00,2.70)`） | ✅ |

**マイルストーン C 完了（2026-06-17）** — EditMode **120本緑**（回帰なし）。コミット `8be6034`。
| Unit | 成果物 | 検証 | 状態 |
|---|---|---|---|
| C1 | `WaveConfigSO._displayName` 追加 ＋ Wave1=快感波/Wave2=本隊侵攻/Wave3=突撃隊＋変装波 設定 ＋ State の `WaveIndex` 公開 | `assets-modify` で 3 アセット値確認。EditMode 120本緑（回帰なし） | ✅ |
| C2 | `UIManager` 中央バナー描画（`SetBanner`/`ClearBanner`/`OnGUI`）＋ `GameManager.UpdateStatusUI` 配線 ＋ `GetWaveDisplayName` 補助 | MCP Play: WaveIntro「Wave 1 ― 快感波」・WaveLull「Wave 1 クリア」帯表示・WavePlaying でバナー消去・上部 HUD のみを screenshot 確認 | ✅ |

---

## Phase 4 — 3D表現化（世界の3D透視化）　【🟢 完了】

**ゴール**: 俯瞰の平面マップ表現（山＝手続き Quad・正射影・全obj z≈0）を、造形済み Terrain（`kakigori-3d-t` の `New Terrain 3.asset`）を用いた **3D 透視表現** へ置き換える。ゲームロジック（Core・雪崩・生成）は不変のまま「3D の雪山に隊列が並ぶ」見た目にする。**アクターはプリミティブ継続（案Y）**。詳細計画は `docs/phase4(3D).md`、設計判断は **ADR-21**。

### In scope

- Terrain 移植（山）、透視カメラ、Directional Light ＋ lit Terrain マテリアル、雪テクスチャ（新規・簡易）、`ChannelController.PositionFor`/`DirectionFor` の **XZ＋高さサンプル**対応（高さサンプラを `GameManager` が注入）、敵の**入場アニメ**（Option A・既存 `SetTarget` lerp 流用）、アクターは Unlit プリミティブ継続。

### Out of scope（やらない）

- 敵/ボス/弾/砦の**3Dモデル制作**、雪崩/粉雪の**本格 VFX**、サウンド、移植元の土色 ADG レイヤー流用（雪化のため不使用）、バランス調整、`Projectile` の放物線化（直進のまま／将来の任意拡張）。

### 受け入れ基準

- [x] **EditMode 全120本緑を維持**（Core 無改修・回帰ゼロの証明）
- [x] MCP `screenshot-game-view` で **Terrain 3 の雪山が透視表示**され、光と影で立体に見える
- [x] 敵が **4方向の麓から斜面上に**（浮かず沈まず）湧き、隊列を組む
- [x] 雪崩入力で**先頭行が消える**挙動が斜面上でも従来どおり（Core 不変の実証）
- [x] **Option A**：敵が麓側から隊列位置へ歩いて接近する
- [x] 弾着弾 → 砦HP減、スコア、Result 等、**既存 Wave1 通し**が 3D 表示下でも成立
- [x] **物理不使用を維持**（コライダー不使用・高さは `TerrainData.GetInterpolatedHeight`）
- [x] Singleton は `GameManager` のみ・入力は `IInputSource` 経由を維持（配線変更のみ）
- [x] 変更を `docs/architecture.md` **ADR-21** に記録（Proposed → **Accepted** 化）

### Status: 🟢 Phase 4 完了（2026-07-06）— マイルストーン A・B・C・D 達成。受け入れ基準すべて充足。ADR-21 Accepted。

`docs/phase4(3D).md` 参照。EditMode **120本緑**（Core 無改修・回帰ゼロ）。ブランチ `phase4-3d-presentation`。
| Unit | 成果物 | 検証 | 状態 |
|---|---|---|---|
| （前提）| URP **2D→3D レンダラ切替**：3d-t の `PC_RPAsset`/`PC_Renderer`/`SampleSceneProfile` を `Assets/Settings/` に流用し既定パイプラインを切替（ADR-21 着手メモ） | `currentRenderPipeline=PC_RPAsset`・`renderer[0]=UniversalRendererData` を確認 | ✅ |
| U1 | `New Terrain 3.asset` 移植（`Assets/Terrain/`）＋ `size (120,160,120)`＋山頂を原点＋雪 TerrainLayer(`SnowWhite.png`)＋`SnowTerrain.mat`(URP Terrain/Lit)＋`TerrainCollider` 除去（物理なし） | MCP screenshot：雪山が表示（`drawInstanced=false`/SSAO 無効でマゼンタ・暗沈を解消） | ✅ |
| U2 | Main Camera を透視＋俯角、Directional Light 追加、環境光（雪の反射）設定 | MCP screenshot：**光と影で立体の 3D 雪山**を確認。EditMode 120本緑 | ✅ |
| U3 | `GameManager.HeightAt`（`Terrain.SampleHeight`・物理なし）＋ `ChannelController.Configure` に高さサンプラ注入。`BuildMountain` Quad 撤去、砦を山頂へ | サンプル値＝Terrain 高さ一致（山頂 5.4）を script で確認 | ✅ |
| U4 | `DirectionFor`/`_perpDir`/`PositionFor` を XZ＋高さへ（Core・雪崩・生成は無改修）。`size` を (316.8,160.4,316.8) にフィット | MCP Play：全36体が斜面上（y=1.03〜4.24）／`ApplyAvalanche` で先頭行 killed=3・rows 3→2／EditMode 120本緑 | ✅ |
| U5 | 敵の入場（Option A）：`ChannelController._entranceGap`＋`RepositionAll` 新規湧き分岐（入場位置へ snap→スロットへ lerp）＋`PositionFor` に along オフセット引数。3 spawn 経路（Enemy/Ally/Disguised）に統一適用。gameplay locomotion ではない（ADR-18 整合） | MCP Play：北谷1体が湧き直後 alongOutward=4.20（麓側・y=0.83）→ スロット 2.70（y=2.55）へ歩いて整列／南谷6体 `ApplyAvalanche` killed=3・rows 2→1（雪崩/繰り上がり回帰なし）／EditMode 120本緑 | ✅ |
| U6 | Aim インジケータの斜面対応（`GameManager.UpdateAimIndicator` を XZ＋高さサンプル方式に・`LookRotation(法線,laneDir)`・法線へ0.35浮かせ・`_Cull=Off`）＋Wave 通し＋ADR-21 Accepted 化 | MCP Play（ConfirmPressed 注入で通し駆動）：Title→Wave→4谷斜面湧き＋発射→弾着弾で砦HP 1000→0→Lull→次Wave→Result(FORT DESTROYED・スコア0)→retry。Aim レーンが S 斜面に沿って表示。物理不使用/Singleton/IInputSource 維持。EditMode 120本緑 | ✅ |

**把握フェーズの補正**: 移植元の再利用資産は Terrain だけでなく **URP 3D パイプライン一式**も流用（demo は 2D Renderer だった）。`New Terrain 3` は「広い平面の一角に造形された compact な山」で、平地に見えたのは誤読（描画で山を確認）。山の footprint が小さいため `TerrainData.size` を (316.8,160.4,316.8) に拡大しプレイフィールドを斜面へ乗せた。詳細は ADR-21 着手メモ。

**スコープ外（意図的に未実施・後続フェーズ）**: アクターの3Dモデル化・本格 VFX・サウンド・弾の放物線化（Phase 4 Out of scope）。アクターの斜面向き（任意）も見送り＝プリミティブが斜面バンプに一部埋もれる箇所があるが視認可能で、モデル化フェーズで解消する範疇。雪マテリアルは白 lit のまま（立体視認は十分）。

---

## Phase 4.5 — 敵の登場・静止位置の再設計（遠方入場／麓静止）／別計画　【🟢 完了】

**ゴール**: 俯瞰カメラ化後の手触り調整。敵が「山からかなり遠い平地（カメラ縁）に登場 → 山の麓まで歩いて接近して静止し攻撃」する表現へ。ゲーム性・数値・Core（`EnemyMatrix`/雪崩/生成/`Projectile`）は Phase 4 のまま不変（**描画・座標のみ**）。詳細計画は `docs/Phase4.5.md`、設計判断は **ADR-22**。ブランチ `phase4-5-entrance-rework`。

### In scope

- 隊列の静止位置を斜面中腹→**麓（斜面基部）**へ（`_frontGap` 数値のみ・ADR-14 前例）、入場を**絶対半径 `_entranceRadius`（カメラ縁の遠方ライン）＋専用速度 `_entranceSpeed`**で歩かせる（案B・ADR-22）、俯瞰カメラのズームアウト（遠方入場を収容）、Aim レーンの麓隊列への整合。

### Out of scope（やらない）

- アクターの3Dモデル化・本格 VFX・サウンド・弾の放物線化・バランス数値（HP/damage/fireInterval/spawn 等）の調整・アクターの斜面向き。

### 受け入れ基準

- [x] 敵が山からかなり遠い平地（俯瞰カメラ視野の縁の内側・**radial 10**）に登場する
- [x] 登場した敵が**麓（radial 5.0〜6.65）まで歩いて接近し静止して砦を攻撃**する（`alongOutward` 10.02→5.05 を実測）
- [x] 削り入力で**先頭行が消え後続が繰り上がる**挙動が従来どおり（Core 不変・`ApplyAvalanche` killed=3・rows 減）
- [x] 弾着弾→砦HP減、Result、retry 等、**既存 Wave 通し**が成立（Title→Wave→Result(Lose)→retry をライブ実証）
- [x] 谷ハイライト（Aim レーン）が麓の新隊列を覆う（`AimNearAlong 3.5`/`AimFarAlong 7.0`）
- [x] **物理不使用を維持**（コライダー不使用・高さは `Terrain.SampleHeight`）
- [x] Singleton は `GameManager` のみ・入力は `IInputSource` 経由を維持
- [x] **EditMode 全120本緑**（Core 無改修・回帰ゼロ）
- [x] 変更を `docs/architecture.md` **ADR-22** に記録（Accepted）

### Status: 🟢 Phase 4.5 完了（2026-07-07）— マイルストーン A・B・C 達成。受け入れ基準すべて充足。ADR-22 Accepted。

`docs/Phase4.5.md` 参照。EditMode **120本緑**（Core 無改修・回帰ゼロ）。
| Unit | 成果物 | 検証 | 状態 |
|---|---|---|---|
| U1 | 俯瞰カメラ可視半径・Terrain 麓半径・高さプロファイルを MCP 実測（N/S 可視 ±8/9・麓 r≈6.5-8・放射非対称） | `script-execute` 実測値で目標 along 確定 | ✅ |
| U2 | `ChannelController._frontGap` 2.7→5.0（麓へ・1パラメータのみ） | MCP Play: 4谷 radial 5.05〜6.69 に整列・`ApplyAvalanche` killed=3/rows 4→3・EditMode 120本緑 | ✅ |
| U3 | 案B: `_entranceRadius=10`＋`_entranceSpeed=6`（`_entranceGap` 廃止）＋カメラ (0,20,-0.5)→(0,27,0) ズームアウト | MCP Play: 4谷 radial 10.02（カメラ縁）に登場→5.05（麓）へ歩行・弾着弾で砦HP減・雪崩回帰なし・EditMode 120本緑 | ✅ |
| U4 | Aim レンジ Near3.5/Far7.0＋Wave 通し＋ADR-22 Accepted | MCP Play（fake input 注入）: Wave1 WavePlaying・Aim レーンが N谷麓帯に表示・砦HP0→Result→Confirm 再挑戦→WavePlaying（retry）・EditMode 120本緑 | ✅ |

---

## Phase 4.6 — 発射タイミングの修正（所定位置到達後に発射）／別計画　【🟢 完了】

**ゴール**: Phase 4.5 後の不具合修正。敵が**入場中（麓へ歩行中）に発射**し、敵と弾が同時移動・敵が自分の弾を追い越す不自然さを解消。敵は**所定の隊列スロット（麓）に到達して静止してから**発射するよう変更。詳細計画は `docs/Phase4.6.md`、設計判断は **ADR-23**。ブランチ `phase4-6-fire-timing`。

### In scope

- `Enemy` の発射ゲート（在位置判定 `IsInPosition` で移動中は発射抑制）。Core・`Projectile`・生成・カメラ・座標系は無改修。

### Out of scope（やらない）

- バランス数値調整・初弾の間（`InitialFireDelay`）の演出調整・アクター3Dモデル化・VFX・サウンド・弾の放物線化。

### 受け入れ基準

- [x] **入場中の敵は発射しない**（麓のスロットに到達するまで `ShotsFired=0`）
- [x] **麓到達後に発射を開始**し、弾は静止した敵の位置から生成される
- [x] 敵が自分の弾を**追い越さない**（到達後は静止して撃つ）
- [x] 雪崩後の繰り上がり移動中も発射せず、到達で再開（同一ゲート）
- [x] 既存 Wave 通し（発射・砦HP減・雪崩・Result・retry）が成立
- [x] Singleton=GameManager・入力=IInputSource・物理不使用を維持
- [x] **EditMode 全120本緑**（Core・`Projectile` 無改修・回帰ゼロ）
- [x] 変更を `docs/architecture.md` **ADR-23** に記録（Accepted）

### Status: 🟢 Phase 4.6 完了（2026-07-07）— マイルストーン A・B 達成。受け入れ基準すべて充足。ADR-23 Accepted。

`docs/Phase4.6.md` 参照。EditMode **120本緑**（Core・`Projectile` 無改修・回帰ゼロ）。
| Unit | 成果物 | 検証 | 状態 |
|---|---|---|---|
| A1 | `Enemy.IsInPosition`（`_targetPos` 距離）＋`Update` の発射を在位置時のみゲート（`DisguisedEnemy` は基底継承で自動適用・`AllyNPC` は無発射） | MCP Play: 単体入場を実測—radial 10.02→8.37→5.82 で `ShotsFired=0`、radial 5.05 到達で `ShotsFired>0`・screenshot で歩行中の敵が平地に弾なし・EditMode 120本緑 | ✅ |
| B1 | Wave 通し回帰＋ADR-23＋docs | MCP Play（fake input 注入）: Title→Wave1 WavePlaying（砦HP減＝到達後発射）→WaveLull「Wave 1 クリア」→…→ResultState→Confirm 再挑戦→WavePlaying（HP リセット）・EditMode 120本緑 | ✅ |

---

## Phase 5 — かき氷機の位置検知連携（画像処理→ファイル→Unity）／別計画　【🟢 完了】

**ゴール**: 物理かき氷機の**位置検知**（MediaPipe/Python）を Unity の照準（`ActiveChannel`）に接続する。分類（(x,y)→谷）は Python、Unity は `seq,channel` をファイル経由で読む疎結合。削り（M5Stick・Phase 1.5）と合わせて 2 系統の実機入力が揃う。詳細計画は `docs/phase5.md`、設計判断は **ADR-24**。ブランチ `phase5-vision-input`。

### In scope

- Python トラッカー（`tools/position-tracker/`・uv 隔離）＝セクター判定＋`seq,channel` 原子的書き込み。
- Unity 入力層＝`VisionInputProtocol`（純C#）＋`VisionInputSource`（IO）＋`VisionConfigSO`＋`CompositeInputSource` の `InputMode` 合流＋UI ロスト警告。Core・雪崩・State・カメラは無改修。

### Out of scope（やらない）

- 連続照準（離散4chに落とす）・quad キャリブレの Unity 化・Python の Unity 起動・ビルド配布の作り込み（方針のみ）・境界ヒステリシスの Python 実装・バランス/VFX/サウンド。

### 受け入れ基準

- [x] Python が `seq,channel` を ~50ms・原子的に書き、手位置で channel 0-3／外すと -1
- [x] Vision モードで機械位置が `ActiveChannel` を選び、Composite 経由で AvalancheController まで一致（削り＝keyboard OR serial は不変）
- [x] `InputMode` で Keyboard/Vision を明示切替（Keyboard は従来操作が回帰なし）
- [x] channel の意味を A（ライブ）/B（ラッチ）で切替可能・デバウンス保険（既定0）
- [x] Python 停止/ハングを検知し -1 フェイルセーフ＋警告表示、復帰で自動解除。正常な谷なし(-1)とロストを区別
- [x] パス解決 env→config.json→Editor 相対/ビルド persistentDataPath（clone で無編集動作）
- [x] Python は `Assets/`・`Packages/` 外の uv 環境で隔離（Unity 非対象）
- [x] Singleton=GameManager・入力=IInputSource・物理不使用を維持（Core/`Projectile` 無改修）
- [x] **EditMode 全139本緑**（既存120＋Vision15＋Composite4・回帰ゼロ）
- [x] 変更を `docs/architecture.md` **ADR-24** に記録（Accepted）

### Status: 🟢 Phase 5 完了（2026-07-09）— マイルストーン A・B・C・D 達成。受け入れ基準すべて充足。ADR-24 Accepted。

`docs/phase5.md` 参照。EditMode **139本緑**（Core・`Projectile` 無改修・回帰ゼロ）。
| Unit | 成果物 | 検証 | 状態 |
|---|---|---|---|
| A1 | `tools/position-tracker/`（uv・Python 3.12）＋`tracker.py`（パス解決／セクター判定／`seq,channel` 原子的書き込み／camera・sim 2モード） | カメラ無し検証: `classify` 代表点・原子的書き込みの seq 単調増加/行完全性・パス解決順・`--sim` 実起動（全 PASS） | ✅ |
| B1 | `VisionInputProtocol`（純C#・seq 変化 watchdog／ライブ/ラッチ／デバウンス／-1 フェイルセーフ）＋EditMode 15本 | MCP EditMode 15/15・フル 135/135 緑 | ✅ |
| C1/C2 | `VisionInputSource`＋`VisionConfigSO`＋`InputMode`＋`CompositeInputSource` 拡張＋`GameManager` 配線＋`UIManager` 警告 | MCP Play（決定的注入）: パス解決・追従(2→2,1→1)・谷切替・ロスト時 -1＋赤字警告（screenshot）・Keyboard 既定は回帰なし・EditMode 139/139 緑 | ✅ |
| D1 | Wave 通し回帰＋ADR-24＋phases.md | Core/State/雪崩 無改修＝EditMode 139緑＋Vision→AvalancheController 連結実証＋Keyboard 既定 boot エラーなしで回帰ゼロを確認。ADR-24 Accepted | ✅ |

---

## Phase 6 — サウンド（BGM / SE 移植）／別計画　【🟢 完了】

**ゴール**: 3d-t の BGM 1＋SE 6（`Assets/SoundE/`）を移植する。ただし 3d-t の Singleton 直叩き（`AudioManager.Instance.PlaySound(...)`）は採用せず、Composition Root で DI 配線 → イベント購読で鳴らす本リポジトリの流儀に translate。**表現層のみ・Core 無改修**（ADR-20/21/25 と同方針）。詳細計画は `docs/phase6.md`、設計判断は **ADR-26**。ブランチ `phase6-audio`。

### In scope

- `AudioService`（薄い MB アダプタ・3系統 AudioSource）＋`AudioConfigSO`（ADR-1）＋`AudioConfig.asset`。
- additive な cosmetic イベント/購読のみ: `ProjectileFactory.OnCreated`（発射）／`ChannelController.OnEnemyKilled`（行単位・実敵/変装のみ）／`AvalancheController.OnAvalancheChanged`（`IsAvalanching` エッジ）＋既存 `Fort.OnHpChanged`・`ShowResult`・`OnAllyCaught`。
- 音源コピー（`.meta` 除外→GUID 再生成）・GameManager 配線・`OnDestroy` 解除。

### Out of scope（やらない）

- 効果音の自作・差し替え（3d-t の 7 音源をそのまま使う）。氷枯渇音・Wave 区切り音（3d-t に無い）。BGM ダッキング・音の空間化。`DontDestroyOnLoad`（単一シーン運用＝不要）。Core・雪崩・生成・State・`Projectile` の変更。

### 受け入れ基準

- [x] BGM が開始時にループ再生され、リトライ（`BeginWave(0)`）後も鳴り続ける（Result で止めない）
- [x] 削り中（`IsAvalanching`）は雪崩音がループ、離すと停止（`AvalancheVfx` の雪玉ストリームと同期）
- [x] 敵発射で発射音（毎発）／砦被弾（HP 減少 & >0）で Hit ／雪崩で1行消えるごとに Monster（味方のみの行は無音）
- [x] 勝利で Clear、敗北で GameOver（BGM に重なる）／味方巻き込みで AllyCaught（クリップ null なら無音でエラーなし）
- [x] 全新規購読が `OnDestroy` で解除される
- [x] Core・雪崩・生成・State・`Projectile` 無改修＝**EditMode 139本緑・回帰ゼロ**
- [x] 変更を `docs/architecture.md` **ADR-26** に記録（Accepted）

### Status: 🟢 Phase 6 完了（2026-07-10）— 受け入れ基準すべて充足。EditMode **139本緑**（Core・`Projectile` 無改修・回帰ゼロ）。ADR-26 Accepted。

`docs/phase6.md` 参照。ブランチ `phase6-audio`。3d-t の 7 音源を `Assets/Audio/`（ASCII 名・OS コピー→GUID 再生成）へ移植し、薄い `AudioService`（3系統 AudioSource: bgm loop / se PlayOneShot / avalanche 専用 loop）＋`AudioConfigSO`（音量 3d-t 踏襲・`allyCaught` 空＝無音）＋`AudioConfig.asset` を追加。additive cosmetic イベント（`ProjectileFactory.OnCreated`／`ChannelController.OnEnemyKilled`〈行単位・実敵/変装のみ〉／`AvalancheController.OnAvalancheChanged`〈`IsAvalanching` エッジ〉＋既存 `Fort.OnHpChanged`／`ShowResult`／`OnAllyCaught`）を GameManager で購読・`OnDestroy` で全解除。3d-t の Singleton 直叩きは不採用。
| 検証 | 結果 |
|---|---|
| EditMode | **139本緑**（回帰ゼロ・Core 無改修） |
| BGM | boot で `_bgmSource` loop・isPlaying=True・clip=bgm_main／Result でも止めない |
| 雪崩ループ | `OnAvalancheChanged` ON→`_avalancheSource`(se_avalanche) isPlaying=True／OFF→False |
| Hit/Monster/EnemyAttack | 実イベント経由で `_seSource` 再生を実測（Hit=HP減&>0・Monster=行単位1回・発射=毎発） |
| Clear/GameOver | `ShowResult(true/false)` で再生・BGM は isPlaying=True 維持（重ねる） |
| Hit 除外 | `Initialize` 満タン通知・HP→0 はどちらも Hit 無音（負検証 PASS） |
| エラー | Play 中エラー/例外ゼロ |

**音量バランス調整（2026-07-11・プレイ確認済み）**: 実プレイで hit/enemyAttack/avalanche が聞こえない報告→`ffmpeg` で音圧実測し切り分け（発火は正常＝音圧が原因）。①`AudioConfig.asset` の hit 0.1→**0.7**・enemyAttack 0.1→**0.4**（0.1 は BGM に約16dB 埋もれていた）②`se_avalanche.mp3` は頭無音のスウェルだったため大きい中核（t=4-8s）を切り出し＋ゲイン＋リミッターで作り直し（頭0.5s −9.4dB・尺8s・`.meta` 温存で GUID 維持）＝小刻み削りでも即・大音量。コード・EditMode 139本は無改修。詳細は architecture.md ADR-26 着手メモ。**残**: 実機カメラ/M5Stick 併用の end-to-end は展示現場。

---

## Phase 7 — 展示ビルド化（3Dアセット・マルチシーン・LiDAR入力・開発連携）　【🟡 進行中／記録は git 履歴からの再構成】

**ゴール**: 遊びの核（Phase 1〜6）が固まった状態から、展示に出せる本番ビルドへ仕上げる。3Dアート、演出、複数シーンのフロー、実機入力（LiDAR）の統合。

> **この節は git 履歴（2026-08）からの再構成であり、Phase 1〜6 のような受け入れ基準・EditMode・検証記録は未整備。** 実作業は各ブランチ
> （`4-敵味方キャラの3dモデル改善` / `5-演出敵撃破時のエフェクト効果音` / `6-タイトルシーン作成` / `7-リザルトシーン作成` /
> `11-演出雪崩表現の改善` / `12-LiDARとUnityの連携` / `37-samplesceneに3dモデルを実装` / `scene_connect`）で行われ、
> 受け入れ基準フローを経ずに `main` へマージされている。設計判断（ADR）は `docs/architecture.md` **ADR-28**（遡及・要レビュー）にまとめている。

### やったこと（コミット単位の要約）

| 領域 | 内容 |
|------|------|
| 3D 山アセット | 球体テスト用スポナー＋山オブジェクト → リアルな山メッシュへ差し替え（tomokican/徳久研 作）→ 虹色（サーモ風）アセットは削除。地形の基準を山メッシュにし、敵の停止位置を 8.0 に |
| アクター 3D 化 | 敵兵（`TGS_兵士赤.fbx`）・味方（`TGS_Ally.prefab`）・市民の 3D モデルとアニメーション（`Main_TGS_兵士赤 アニメーション.prefab` 等）。旧・敵モデル生成コードは削除 |
| 撃破演出 | 敵が横回転で吹き飛んで蒸発する演出、パーティクルの実装 → 改善 → prefab 化（`DissolveBurst` / `SnowPuff` / `Snowball` / `SphereSpawner`） |
| サウンド | 撃破音を 3 クリップから順送り再生（`feat(audio): 撃破音を3種のクリップから順送りで鳴らす`） |
| 雪崩シェーダー | カスタムシェーダー＋環境光の調整。「キラキラの雪崩シェーダー」と「シンプルな雪シェーダー」の 2 種を用意（`11-演出雪崩表現の改善`、`AvalancheVfx`） |
| シーン構成 | `SampleScene` に敵・味方 prefab をアサイン → `TitleScene` / `ResultScene` を作成し prefab 化 → 各シーンを「プロジェクト全体として認識」＝ Build Settings に登録 → 各シーンに `Enter` キーによるシーン遷移を追加（`scene_connect`）。ゲーム本体は `TH.unity`（`Assets/Prefabs/TH_Stage.prefab` に `GameManager` 一式） |
| LiDAR 入力 | `tools/lidar-tracker` を公式 YDLidar-SDK ベースへ載せ替え、リアルタイム可視化＋検出範囲フィルタ＋反射強度の色表示＋セクタ検出回数カウント＋干渉フラグ(0x03/太陽光ノイズ)集計＋複数台の縦並び表示。`valley_tracker.py` が落氷を 4 谷へ写して `seq,channel,shaving` を書き、Unity 側 `VisionInputSource` が読む（谷選択＋削り、ADR-24 のファイル契約に 3 列目を追加）。`tools/lidar-tracker2` / `2.1` は Hokuyo UBG-04LX-F01（別センサー）の C++ 検証 |
| 開発連携 | `com.unity.pipeline`（`0.5.0-exp.1`）を導入し、`unity` CLI から起動中 Editor をヘッドレス駆動（recompile / run_tests / screenshot / シーン・GameObject 操作）。旧 Unity-MCP（`com.ivanmurzak.unity.mcp`）の残骸（OpenUPM scoped registry）は撤去。詳細は `CLAUDE.md`「Unity Editor 連携」 |

### 未整備（Phase 7 を「完了」と呼ぶ前にやること）

- [ ] 3Dモデル/シェーダー/演出まわりの設計判断を ADR に落とす（現状 ADR-28 に要約のみ）
- [ ] マルチシーン構成（Title→TH→Result）の Play 通し検証と受け入れ基準の明文化
- [ ] LiDAR 実機との end-to-end 検証（`seq,channel,shaving` → 谷選択＋削り → 雪崩 → 撃破）
- [ ] `Assets/Scenes/SampleScene.unity`（旧セットアップ）の要否判断（現状 Build 対象外で残置）
- [ ] EditMode 151 本が緑であることの CI/記録（現状ローカルの `unity command run_tests` で確認）

---

## Phase 8 — ゲーム性刷新（単一ステージの「目標撃破数レース」）　【🟡 実装済み／Editor で EditMode 192 本緑・Play 検証は連鎖・ビーム・雪崩・敗北遷移まで（ADR-31）】

3Wave 制のジレンマ型から、**大勢の敵を倒す爽快感**を主役にしたゲーム性へ差し替える（ADR-30）。
設計判断の根拠は `docs/architecture.md` ADR-30 を正とする。

### ゴール

- Wave の区切りを廃し、単一ステージで「目標撃破数（既定100体）を倒しきる」レースにする
- 爆発兵の**無制限連鎖**で盤面が一気に吹き飛ぶ瞬間を作る
- 盾兵が「爆発を使った方が早い」ことを学習させる
- コンボと必殺ビーム砲（ゲージMAXで自動発動）で、上手いほど気持ちよくなる往復を作る

### 変更の内訳

| 領域 | 内容 |
|------|------|
| 進行 | `WaveIntro`/`WavePlaying`/`WaveLull` → `BattleState` 1 本。`WaveConfigSO`×3 → `StageConfigSO` 1 本 |
| 終了条件 | クリア＝`TargetKillCount` 体撃破 ／ 敗北＝砦HP 0（同フレームなら敗北優先） |
| スコア | `Scoring` → `StageResult`。クリア＝クリアタイム ／ 失敗＝到達撃破数。砦HP は生存リソースに純化 |
| 新しい敵 | `BombTrooper`（爆発・誘爆連鎖）／ `ShieldTrooper`（雪玉N発・爆発なら一撃） |
| 新しい集計 | `KillProgress` / `ComboTracker` / `SpecialGauge` / `KillSource` / `ExplosionChain`（すべて Core・UnityEngine 非依存） |
| 必殺技 | `SpecialAttackController`（旧 `BeamCannon`）。ゲージMAXで自動発動 → 砲出現→光線一周→衝撃波の瞬間に全方位一掃（ADR-31）。撃破数には算入、ゲージには非算入、コンボは中立 |
| 難易度 | 撃破数の進捗に対する爆発兵率・盾兵率のカーブのみ。湧きレートは固定 |
| 削除 | `EnemyMatrix` / `IceStock` / `PenaltyTally` / `Scoring` / `AllyNPC` / `DisguisedEnemy` / `WaveConfigSO` と対応テスト |
| シーン | 結果を `GameResultHolder` で受け渡し、クリア→`ResultScene` / 失敗→`GameOverScene` |

### 受け入れ基準

- [x] Core 純ロジックに EditMode テストがある（`ComboTracker` / `SpecialGauge` / `KillProgress` / `ExplosionChain` / `StageResult` / `SpawnSelector` / `States`）
- [x] Core / Runtime / Tests の 3 アセンブリが型エラーなくコンパイルされる
- [ ] Unity Editor で EditMode 全件が緑（`unity command run_tests --mode editor`）
- [ ] `StageConfig.asset` / `BombTrooper.asset` / `ShieldTrooper.asset` を作成し `TH_Stage.prefab` に割り当て済み
- [ ] `GameOverScene.unity` に `GameOverScene.cs` が載っている
- [ ] Play 検証: 爆発兵を倒すと赤い円が広がり周囲が消える／連鎖が伝播する
- [x] Play 検証: 盾兵は雪玉 N 発（既定20）で倒れ、爆発に巻き込むと一撃で倒れる
- [ ] Play 検証: コンボが連鎖で跳ね上がり、2秒放置で 0 に戻る
- [ ] Play 検証: ゲージMAXで自動的にビームが出て盤面が一掃され、**その撃破ではゲージが再充填されない**
- [ ] Play 検証: `TargetKillCount` 到達 → `ResultScene`（クリアタイム表示）／砦HP 0 → `GameOverScene`（到達撃破数表示）
- [ ] ゲージ係数の現物合わせ（1ゲーム中に約2回MAX）

### 未整備

- [x] 爆発・ビームの正式 VFX（2026-09-17 `main` の `ExplosionTelegraph` / `BeamCannon.prefab`＋`BeamSweepVfx`＋衝撃波 / `AvalancheSnowEffect` を接続。ADR-31）
- [x] 雪崩の当たり判定を粒そのものにする `ParticleContact` モード（2026-09-18 `76-avalanche-hitbox`。粒が敵に触れたら吸収＋1 ダメージ、耐久は `EnemyTypeSO.ParticleHitPoints`、静止線より外は雪崩なし。分岐点の判定は `Legacy` として `LidarPreviewConfigSO.HitMode` で切替。ADR-32）
  - [ ] `LidarPreviewConfig.asset` の `_enemyBodyWidthUnits` と `ShieldTrooper.asset` の `_particleHitPoints` を現場で実測・調整
- [x] Unity が読む LiDAR 共有ファイルを Inspector のトラッカー選択で切替（2026-09-18 `83-changeLiDARtoUnity`・Issue #83。`LidarPreviewConfigSO.Tracker`＝`LidarTracker` / `LidarTracker3` / `LidarTracker1Plus3` / `Custom`。アセットは `LidarTracker1Plus3` に設定。ADR-33）
  - [x] Play 検証（Unity-MCP・2026-09-18）: Console に `[LidarPosition] tracker = LidarTracker1Plus3 / file = <root>/tools/lidar-tracker1+3/out/position.txt` を確認
  - [ ] 現物検証: `tracker1p3.py` 起動 → 落氷で雪崩が出ること
- [ ] 盾兵の残り耐久表示（現状は per-instance MPB で暗くするだけ。ヒビ・欠け等への差し替え）
- [ ] クリアタイムのランキング保存（現状は 1 プレイ分を表示するのみ）

---

## 既知の TBD（フェーズ進行中に確定）

- 味方/敵/変装の**形**のデザイン → 3Dモデル化で概ね確定（Phase 7）。変装の tell（見た目差）の具体は未確定
- バランス数値（`iceTotal`, `projectile.damage`, `fireInterval`, `spawnInterval` 等）のプレイテスト調整
- 弾の見た目・着弾エフェクト
- ~~Unity バージョンの確定~~ → **6000.3.16f1 で確定**（`ProjectSettings/ProjectVersion.txt`）
