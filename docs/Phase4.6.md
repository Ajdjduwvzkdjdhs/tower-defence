# Phase 4.6 実装計画書（発射タイミングの修正 — 所定位置到達後に発射）

> 本書は `docs/Phase4.5.md`（遠方入場／麓静止・ADR-22）/ `docs/architecture.md`（**ADR-7** 発射・**ADR-11** 戦闘アクターの状態は MonoBehaviour・**ADR-20** 前進 lerp・**ADR-22** 入場）/ `docs/coding-conventions.md` / `docs/mcp-guide.md` を正本とする、Phase 4.5 後の**不具合修正**計画。Phase 1.5 / 4.5 と同様の「本編に対する別計画（サイドプラン）」。完了判定の bar は §6 受け入れ基準＋EditMode 120本緑（回帰ゼロ）＋Unity-MCP 視覚検証。

## 1. 前提・不具合（現状）

- **症状**: 敵が**入場中（麓へ歩いている最中）に弾を発射**している。弾は敵の現在位置（移動中）から生成されるため、**敵と弾が同時に移動**し、さらに敵が砦方向へ歩く速度の関係で**敵が自分の弾より前に出てしまう瞬間**があり不自然。
- **根本原因**: [`Enemy.Update`](../Assets/Scripts/Gameplay/Enemy.cs) が `MoveTowardTarget()` の後、**移動中かどうかに関係なく**毎フレーム `_fireTimer` を進めて `Fire()` する。`Fire()` は `transform.position`（移動中の現在位置）を弾の始点にする。
  - Phase 4.5（ADR-22）で入場開始が絶対半径 r≈10（カメラ縁）へ大きく外に出たため、入場の歩行距離が伸び、この「歩きながら発射」が顕在化した。
- **影響範囲**: `Enemy`（基本敵）と `DisguisedEnemy`（基底 `Enemy.Update` を継承＝撃つ）。`AllyNPC` は無発射なので無関係。

## 2. 目的

- 敵は**所定の隊列スロット（麓）に到達して静止してから**弾を発射する。入場中（および雪崩後の繰り上がり移動中）は発射しない。
- 結果として弾は**静止した敵の位置**から生成され、敵が弾を追い越す不自然さが解消する。

## 3. 方針

- **修正は `Enemy` の発射ゲートのみ**（MonoBehaviour の薄い挙動・ADR-11）。Core（`EnemyMatrix`/雪崩/State/`Scoring`）・生成・`Projectile`・入力・カメラ・座標系は**無改修**。
- **在位置（in-position）判定で発射を抑制**: 敵が目標位置 `_targetPos` に十分近い（＝移動完了）ときのみ `_fireTimer` を進め `Fire()` する。移動中はタイマを凍結（＝到達後に `InitialFireDelay` から計時＝初弾は到達後）。
  - `SetTarget`/`MoveTowardTarget`（ADR-20）はそのまま流用。到達判定は `transform.position` と `_targetPos` の距離で行う（`Vector3.MoveTowards` は目標に達すると座標が一致するため、微小 epsilon で確実に検出できる）。
  - この 1 箇所の変更で**基本敵・変装敵の両方**に適用される（`DisguisedEnemy` は基底 `Update` 継承）。`AllyNPC` は `Update` を override し発射しないので影響なし。
- **繰り上がり中も自然に発射停止**: 雪崩で先頭行が消え後続が新スロットへ lerp する間も「移動中」＝発射停止、到達で再開。追加コード不要（同一ゲートで成立）。
- **balance は本件のスコープ外**: 入場中に撃たなくなる分、単位時間の被弾はわずかに減るが、これは**正しい挙動**（歩きながら撃たない）であり、数値調整は行わない。
- **検証**: EditMode 120本緑（`Enemy` は Core 非依存＝回帰ゼロ想定）＋ MCP Play（入場中ゼロ発射→到達後発射、弾が敵始点＝静止位置、敵が弾を追い越さない）。

## 4. 変更対象一覧

| 種別 | 対象 | 変更 |
|---|---|---|
| 改修(コード) | `Enemy` | 在位置判定 `IsInPosition`（`_targetPos` との距離）を追加し、`Update` の発射ブロックを「在位置時のみ」ゲート |
| 無改修 | `DisguisedEnemy` | 変更なし（基底 `Update` のゲートが自動適用） |
| 無改修 | `AllyNPC` / Core / `Projectile` / `ChannelController` / `GameManager` / State / UI / カメラ | — |

## 5. マイルストーンとユニット

### マイルストーン A — 発射ゲート（在位置判定）

**Unit A1: `Enemy` に在位置ゲートを実装**
`Enemy` に `IsInPosition`（`(transform.position - _targetPos).sqrMagnitude <= epsilon²`）を追加。`Update` で `MoveTowardTarget()` 後、`!IsInPosition` なら発射処理をスキップ（`_fireTimer` を進めない）。到達後のみ `_fireTimer` 減算→`Fire()`。
- 合否: EditMode **120本緑**（回帰ゼロ）。MCP Play — ①**入場中の敵は `ShotsFired=0`**（湧き直後～麓到達まで発射なし）②**麓到達後に発射開始**（`ShotsFired>0`・弾始点＝静止スロット座標）③**敵が自分の弾を追い越さない**（弾は敵より砦寄りを進む）を screenshot／実測で確認。

### マイルストーン B — Wave 通し回帰＋記録

**Unit B1: Wave 通し回帰 ＋ ADR/docs**
Wave 通し（fake `IInputSource` 注入）で「遠方入場→麓到達→**到達後に発射**→着弾で砦HP減→雪崩→Result→retry」が成立することを確認。`architecture.md` に **ADR-23**（発射は在位置時のみ）を追記、`phases.md` に Phase 4.6 節を追加。
- 合否: Wave 通しで発射・砦ダメージ・雪崩・Result・retry が成立（初弾が麓到達後になる以外は Phase 4.5 と同等）。EditMode 120本緑。ドキュメント更新。

## 6. 受け入れ基準（完了判定の bar）

- [ ] **入場中の敵は発射しない**（麓のスロットに到達するまで `ShotsFired=0`）
- [ ] **麓到達後に発射を開始**し、弾は**静止した敵の位置**から生成される
- [ ] 敵が自分の弾を**追い越さない**（歩きながら撃つ不自然さの解消）
- [ ] 雪崩後の繰り上がり移動中も発射せず、到達で再開する
- [ ] 既存 Wave 通し（発射・砦HP減・雪崩・Result・retry）が成立
- [ ] Singleton=GameManager・入力=IInputSource・物理不使用を維持
- [ ] **EditMode 全120本緑**（Core・`Projectile` 無改修・回帰ゼロ）
- [ ] 変更を `architecture.md` **ADR-23** に記録

## 7. ADR / スコープ外

- **ADR**: `Enemy` の発射条件に「在位置」制約を加えるのは ADR-7（発射）・ADR-22（入場）を踏襲した挙動の明確化。軽量な **ADR-23（発射は所定スロット到達後のみ＝入場・繰り上がりの移動中は発射しない）** を追記（ADR-11 の「戦闘アクターの状態は MonoBehaviour」に整合、Core 不変）。
- **スコープ外**: バランス数値調整・初弾の間（`InitialFireDelay`）の演出調整・アクター3Dモデル化・VFX・サウンド・弾の放物線化。本計画は「入場中に撃たない」挙動修正に限定する。

## 8. 着手メモ（マイルストーン A・B 完了・2026-07-07）＝Phase 4.6 完了

- **A1（発射ゲート）**: `Enemy` に `IsInPosition`（`(transform.position - _targetPos).sqrMagnitude <= 0.0004`）を追加し、`Update` の発射ブロック（`_fireTimer` 減算＋`Fire()`）の前に `if (!IsInPosition) return;` を挿入。移動中はタイマ凍結＝初弾は到達後。`SetTarget`/`MoveTowardTarget`（ADR-20）不変。`DisguisedEnemy` は基底 `Update` 継承で自動適用、`AllyNPC` は `Update` override＋無発射で影響なし。**Core・`Projectile` 無改修**。
- **A1 検証（MCP Play・単体）**: `_entranceSpeed` を一時的に遅く（reflection）して入場を実測 — radial **10.02→8.37→5.82 で `ShotsFired=0`**（歩行中ゼロ発射）、radial **5.05 到達で `ShotsFired>0`**（発射開始）。screenshot で入場中の敵が平地に弾なしで立ち、到達済みの敵のみ砦へ弾を放つのを確認。EditMode **120本緑**。
- **B1 検証（Wave 通し・fake `IInputSource` 注入）**: Title→(Confirm)→**Wave1 WavePlaying**（砦HP 1000→700→… 減＝到達後に発射し着弾）→**WaveLull「Wave 1 クリア」**（波遷移・砦生存 HP 275）→…→**ResultState**（砦HP0）→Confirm 再挑戦→**WavePlaying**（HP≈1000 リセット）。retry ループ実証。EditMode **120本緑**。
- **balance 補足**: 本修正で入場中に撃たなくなる分、単位時間の被弾がわずかに減る（＝正しい挙動）。4.5 検証では砦が Wave1 で死亡したが、本修正後は Wave1 生存→Lull 遷移する場面も観測（意図した挙動差・数値調整はスコープ外）。
- **完了判定**: §6 受け入れ基準を総達成 → `architecture.md` **ADR-23 Accepted**、`phases.md` に Phase 4.6 節追加。Phase 4.6 完了。
