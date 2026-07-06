# Phase 4.5 実装計画書（敵の登場位置・静止位置の再設計 — 遠方入場 / 麓静止）

> 本書は `docs/phase4(3D).md`（Phase 4 実装記録・特に §7〜§10）/ `docs/architecture.md`（**ADR-14** 座標＝行インデックス基準・**ADR-20** 前進 lerp・**ADR-21** 空間の XZ＋高さサンプル / Option A 入場）/ `docs/phases.md`（Phase 4 受け入れ基準）/ `docs/coding-conventions.md` / `docs/mcp-guide.md` を正本とする、Phase 4 完了後の **手触り調整（登場・静止位置の再設計）** 計画。Phase 1.5 と同様の「本編フェーズに対する別計画（サイドプラン）」として扱う。完了判定の bar は本書 §7 の受け入れ基準＋EditMode 120本緑（回帰ゼロ）＋Unity-MCP 視覚検証。

## 1. 前提（Phase 4 完了後の現状＝土台）

- **ブランチ**: `main`（Phase 4 マージ済み＝コミット `1c4062f`／§10 の俯瞰カメラ化まで反映）から `phase4-5-entrance-rework` を切る。コミットは**ユニット単位**（MCP ツーリングは除外）。
- **カメラ**: `MainScene`（`SampleScene.unity`）の Main Camera は **真上からの俯瞰（トップダウン）**＝ `LocalRotation` X=90°・`LocalPosition (0, 20, -0.5)`（`phase4(3D).md` §10）。4方向の麓・隊列・雪崩を左右対称に見下ろす構図。
- **Terrain**: `size=(316.8, 160.4, 316.8)`、山頂を world 原点（＝砦）に配置、footprint 半径 **≈7**、山頂 y≈5.4。斜面高さ実測: r=2.7→2.28 / r=3.5→1.55 / r=4.35→0.88（`phase4(3D).md` §7・ADR-21 着手メモ B）。r≈5〜7 で高さ→ほぼ 0＝**麓（斜面基部の平地）**。
- **敵の配置・入場（現状＝置き換え対象）**: すべて `ChannelController`（[Assets/Scripts/Gameplay/ChannelController.cs](../Assets/Scripts/Gameplay/ChannelController.cs)）の `[SerializeField]` 数値と `RepositionAll`／`PositionFor` の既存機構で成立している。**Core（`EnemyMatrix`）は無関与**。
  - `_frontGap = 2.7`（先頭行 index0 の砦中心からの外向き距離）／`_rowSpacing = 0.55`／`_maxRows = 4` → 隊列は along **2.7〜4.35**＝**斜面の中腹**に静止（y=2.28〜0.88）。
  - `_entranceGap = 1.5`（Option A・ADR-21/U5）→ 入場時は各スロットの along＋1.5＝麓側へ少しだけ外に置き、`snap:true`→スロットへ `snap:false` の lerp で歩かせる。入場距離が短く「麓のすぐ外からちょっと登って整列」に見える。
  - `_advanceSpeed = 3.5`（繰り上がり＝ADR-20／入場の lerp 速度も現状これを流用）。
- **Aim インジケータ**: `GameManager.UpdateAimIndicator` が外向き(XZ)に沿って along **2.0→5.5** の 2 点を `HeightAt` サンプルしレーンを寝かせる（`phase4(3D).md` §9・U6）。現状の隊列レンジ（2.7〜4.35）に合わせた寸法。

## 2. 目的（ビフォー → アフター）

| | 登場（spawn）位置 | 静止（attack）位置 | 見え方 |
|---|---|---|---|
| **現状** | 麓のすぐ外（along ≈ slot+1.5＝斜面下部） | 斜面の**中腹**（along 2.7〜4.35・y 2.28〜0.88） | 麓の少し外から中腹へ登って整列 |
| **目標** | **山からかなり遠く・カメラ視野の縁ギリギリ**（平地の外周） | **山の麓**（斜面基部の平地・along ≈5〜7・y≈0） | 遠方から麓まで歩いて接近し、麓で静止して砦を攻撃 |

- **核**: ①隊列の静止位置を「中腹」→「麓（斜面基部の平地）」へ外側に移す ②入場開始位置を「麓のすぐ外」→「カメラ視野の縁の遠方平地」へ大きく外へ出す。
- 「削る→雪崩が山頂側の先頭行（index0）を消す→後続が繰り上がる」という **ゲーム性・数値・Core ロジックは Phase 4 のまま不変**。変わるのは隊列と入場の**ワールド座標のみ**。

## 3. 方針

- **本質は数値調整＝ADR-14「方針不変・数値のみ」の踏襲**。座標は行インデックス基準（ADR-14）、繰り上がり／入場は lerp（ADR-20）、空間は XZ＋高さサンプル・Option A 入場（ADR-21）を**覆さない**。これらの上で `_frontGap`／`_rowSpacing`／`_maxRows`／`_entranceGap`（＋新規 `_entranceSpeed`／`_entranceRadius`）を再調整する。
- **縫い目は 2 点に閉じる**:
  1. **`ChannelController` の配置パラメータ**（`_frontGap`/`_rowSpacing`/`_maxRows`/`_entranceGap`）＝隊列を麓へ・入場を遠方へ。
  2. **`GameManager.UpdateAimIndicator` の along レンジ**＝新しい隊列レンジ（麓）に合わせて谷ハイライトのレーン寸法をフィット。
- **入場を「カメラ縁の一直線」にする（確定＝案B）**: 現状の `_entranceGap`（各スロットの along への相対オフセット）だと front 行と back 行で入場開始点が階段状にズレる。「かなり遠くのカメラ内ギリギリに **一斉に** 登場」をきれいに出すため、**入場開始を絶対半径 `_entranceRadius`（谷ごとに同一の遠方ライン）で置く**小改修を採用する（`RepositionAll`/`PositionFor` に入場分岐＋`[SerializeField] _entranceRadius`）。
- **入場速度を独立させる（確定）**: 入場距離が伸びるため `[SerializeField] _entranceSpeed` を新設し、入場の lerp だけこの速度を使う。雪崩後の繰り上がり（ADR-20 の `_advanceSpeed=3.5` スライド演出）は据え置き＝巻き添えで速くしない。
- **物理不使用を維持**: 高さは `Terrain.SampleHeight`（`GameManager.HeightAt`）のサンプルのみ。コライダー／Raycast 不使用（ADR-11/21）。
- **Core・雪崩・生成・`Enemy.Update`・`Projectile` は無改修**＝EditMode 120本は回帰ゼロが条件（座標は純ロジックでないため EditMode 追加は困難＝視覚検証は MCP 主）。
- **1ユニット = 単独でビルド通過し固有の合否（MCPスクショ or テスト緑）が下せる最小の塊**。下から積む。
- **スコープ厳守**: 3Dモデル・本格 VFX・サウンド・弾の放物線化・バランス数値調整（HP/damage/fireInterval 等）は**やらない**。本計画は「敵の登場・静止位置」に限定する。

## 4. 変更対象一覧

| 種別 | 対象 | 変更 |
|---|---|---|
| 改修(コード/Inspector) | `ChannelController` | ①`_frontGap`/`_rowSpacing`/`_maxRows` を**麓静止**へ。②新規 `[SerializeField] _entranceRadius`（絶対半径・入場開始ライン）＋`_entranceSpeed`（入場専用速度）を追加。`RepositionAll` の入場分岐を「絶対半径 `_entranceRadius` の外周点へ `snap:true`→スロットへ `snap:false`（速度=`_entranceSpeed`）」に変更。`_entranceGap` は撤去 or 未使用化 |
| 改修(コード) | `GameManager.UpdateAimIndicator` | Aim レーンの along レンジ（現 2.0→5.5）を新しい隊列レンジ（麓）に合わせて延長・フィット |
| 検証のみ(無改修) | `MainScene` Main Camera | 俯瞰(y=20)のまま。視野に「遠方入場ライン〜麓の隊列」が収まるか実測。収まらなければ **カメラ高さ or FOV のみ微調整**（構図＝トップダウンは不変） |
| 無改修 | Core 全部 / `EnemyMatrix` / `Enemy.Update` / 雪崩 / 生成 / `Projectile` / `Fort` / `UIManager` / Terrain / ライト | — |

## 5. 検証可能な単位（ユニット）と実装順序

### マイルストーン A — 計測と静止位置（麓への移動）

**Unit U1: カメラ視野と麓半径の実測（MCP）**
俯瞰カメラ（y=20・トップダウン）の可視 XZ 半径を `screenshot-game-view` ＋ `script-execute`（`Camera.ViewportToWorldPoint` 等）で実測。Terrain の footprint（斜面が平地に着く半径＝麓）を `HeightAt(r)` のスポットサンプルで確定。→ 目標 along（麓の隊列 index0/最奥行・遠方入場ライン）の数値レンジを決める。
- 合否: 「麓半径 R_foot」「可視半径 R_view」を数値で確定し、隊列（≈R_foot 前後）と入場ライン（R_view の内側）が両立することを机上確認。

**Unit U2: 隊列を麓へ（静止位置の外移動）**
`_frontGap`/`_rowSpacing`/`_maxRows` を調整し、隊列（index0〜最奥）が斜面基部の平地＝麓に乗る（例: `_frontGap≈5.0`／`_rowSpacing≈0.55`／`_maxRows=4` → along 5.0〜6.65・y≈0.3〜0＝要 U1 実測で確定）。**入場は一旦現状のまま**（差分を隔離して確認）。Core・雪崩・生成は無改修。
- 合否: MCP Play — ①4谷×各行が**麓（平地）に**（浮かず沈まず）静止 ②`Space`＋`W/A/S/D` で**先頭行が消え繰り上がる**（Core 不変の実証）③弾が山頂の砦へ着弾・HP減 ④EditMode 120本緑。

### マイルストーン B — 遠方入場（カメラ縁からの接近）

**Unit U3: 入場開始を遠方（カメラ縁）へ＝絶対半径＋専用速度**
新規 `_entranceRadius`（絶対半径・U1 実測の R_view 内側の遠方ライン）で入場開始点を置く。`RepositionAll` の justSpawned 分岐を「入場開始 = `_center + _outwardDir*_entranceRadius + _perpDir*across`（＋高さサンプル）へ `snap:true` → スロットへ `snap:false`（速度=`_entranceSpeed`）」に変更（`PositionFor` に絶対 along を渡せる形へ小改修）。繰り上がりは `_advanceSpeed` 据え置き。3 spawn 経路（Enemy/Ally/Disguised）は全て `RepositionAll(justSpawned)` を通るので **1 箇所の変更で basic/味方/変装すべてに適用**（ADR-21/U5 と同じ統一点）。
- 合否: MCP Play — ①敵が湧いた瞬間**山からかなり遠い平地（カメラ縁の内側）に出現** ②そこから**麓の隊列位置まで歩いて接近し静止して攻撃開始**（`alongOutward` を湧き直後→数百ms 後で実測し「遠方→麓」を数値実証）③雪崩・繰り上がりに回帰なし ④EditMode 120本緑。

### マイルストーン C — 仕上げ・整合（Phase 4.5 完了判定）

**Unit U4: Aim インジケータ整合＋Wave 通し＋記録**
`GameManager.UpdateAimIndicator` の along レンジを新しい麓の隊列レンジに合わせて延長（谷ハイライトが麓の隊列を覆う）。俯瞰カメラで「遠方入場〜麓静止〜雪崩」が画面内に収まることを確認（収まらなければカメラ高さ/FOV のみ微調整）。Wave1〜3 通しを MCP（`ConfirmPressed` 注入で通し駆動）で検証。`architecture.md`／`phase4(3D).md`／`phases.md` に記録（§8 参照）。
- 合否（＝§7 受け入れ基準の総達成）: 遠方入場→麓静止→攻撃→削り→雪崩先頭行消去→着弾・砦HP減・スコア・Result が俯瞰 3D 下で成立／物理不使用維持／Singleton=GameManager・入力=IInputSource 維持／EditMode 120本緑。

## 6. 確定した設計判断・要実測事項

**着手前に確定済み（2026-07-06）**:
- **入場方式 = 案B（絶対半径 `_entranceRadius`）**: 全敵を同一の遠方アーク（カメラ縁）から一斉登場させる。`RepositionAll`/`PositionFor` に入場分岐＋SerializeField を追加。
- **入場速度 = `_entranceSpeed` 新設**: 入場の lerp のみ独立速度。雪崩後の繰り上がり（ADR-20 `_advanceSpeed`）は据え置き。

**U1 実測で確定する数値**:
- **隊列（麓）の具体 along**: `_frontGap`/`_rowSpacing`（暫定 `_frontGap≈5.0`／最奥≈6.65）。斜面基部で y がわずかに残る場合は許容（麓の緩斜面）。
- **入場ライン `_entranceRadius`**: R_view の内側かつ最奥行より外（暫定 R_foot+α）。視野からはみ出さない値。
- **カメラ**: 俯瞰(y=20)のまま入場ライン〜麓が視野に入るか実測。入らなければ**高さ or FOV のみ**微調整（トップダウン構図は §10 決定どおり不変）。
- **`_maxRows` と満杯挙動**: 麓へ広げても最奥行が視野内に収まる上限を維持（ADR-14 の cap 方針は不変・数値のみ）。

## 7. 受け入れ基準（完了判定の bar）

- [ ] 敵が**山からかなり遠い平地（俯瞰カメラ視野の縁の内側）に登場**する
- [ ] 登場した敵が**麓（斜面基部）まで歩いて接近し、そこで静止して砦を攻撃**する（`alongOutward` 実測で「遠方→麓」を数値実証）
- [ ] 削り入力で**先頭行（山頂側）が消え後続が繰り上がる**挙動が従来どおり（Core 不変の実証）
- [ ] 弾着弾→砦HP減、スコア、Result 等、**既存 Wave1〜3 通し**が俯瞰 3D 下でも成立
- [ ] 谷ハイライト（Aim レーン）が新しい麓の隊列を覆う
- [ ] **物理不使用を維持**（コライダー不使用・高さは `Terrain.SampleHeight`）
- [ ] Singleton は `GameManager` のみ・入力は `IInputSource` 経由を維持
- [ ] **EditMode 全120本緑**（Core 無改修・回帰ゼロ）
- [ ] Unity-MCP `screenshot-game-view` で登場・接近・静止・雪崩を記録・検証済み

## 8. ADR / ドキュメント運用・スコープ外

- **ADR**: 本計画は **ADR-14（座標＝行インデックス基準・cap）／ADR-20（前進 lerp）／ADR-21（XZ＋高さ・Option A 入場）を踏襲し覆さない**。隊列を麓へ移す部分は「方針不変・数値のみ」（ADR-14 前例）。**入場に絶対半径 `_entranceRadius`＋専用速度 `_entranceSpeed` を新設する小改修**は挙動方針の追記に当たるため、軽量な **ADR-22（入場をカメラ縁の絶対半径ラインから開始する cosmetic reposition。gameplay locomotion ではない＝ADR-18 と整合、ADR-21/U5 の Option A を発展）** を `architecture.md` に追記する。
- **記録先**: 完了時に `phases.md`（Phase 4.5 の節を追加・受け入れ基準チェック）と `architecture.md`（ADR 追記時）を更新。進捗は本書のユニット表を随時更新。
- **スコープ外（意図的に未実施）**: アクターの3Dモデル化・本格 VFX・サウンド・弾の放物線化・バランス数値（HP/damage/fireInterval/spawn 等）の調整・アクターの斜面向き。本計画は「敵の登場・静止位置」に限定する。

## 9. クリティカルパス

**U1→U2→U3** が核（実測→麓へ静止→遠方から入場）。ここまでで「遠方から歩いてきて麓で止まって攻撃」という本計画の主目的が成立する。U4（Aim 整合・Wave 通し・記録）は仕上げ。

## 10. 着手メモ（マイルストーン A 完了・2026-07-06）

### U1 実測（MCP `script-execute`）
- **俯瞰カメラ**: `Main Camera` pos=(0,20,-0.5)・euler=(90,0,0)・**透視 fov=46・aspect=16:9**。y=0 地面への可視半径: **X（E/W谷）=±15.09** と広いが、**Z（N/S谷）は N=+7.99 / S=-8.99**（カメラ z=-0.5 オフセットで N が最狭）。→ **N/S 谷が視野の律速**。
- **Terrain 高さプロファイル**（`SampleHeight`・4方向）: 山は**放射非対称**（compact 造形）。麓（y→0）到達半径が方向で違う: **N(+z)≈6.5 / E(+x)≈6.5 / W(-x)≈7.5 / S(-z)≈7.5〜8**（S が最も急）。山頂 y=5.4。

### U2 実装・検証（隊列を麓へ）
- **変更**: `ChannelController._frontGap` **2.7→5.0**（`_rowSpacing 0.55`・`_maxRows 4` 据え置き＝1パラメータのみ）。行 r=5.0 / 5.55 / 6.10 / 6.65（先頭 index0＝山頂寄り〜最奥＝麓側）。
- **検証（MCP Play）**: 4谷×各12体（4行×3スロット）を直接 `SpawnEnemy` 投入。**全谷が radial 5.05〜6.69 に整列**（screenshot で 4方向対称に山の外縁＝麓へ配置を確認）。`ApplyAvalanche`(N谷) で **killed=3・rows 4→3・count 12→9**、生存行は内側へ lerp 繰り上げ（Core/ADR-14/20 無改修の実証）。**EditMode 120本緑**（回帰ゼロ）。
- **非対称の扱い**: 同一 radial でも谷ごとに y が異なる（N/E 麓 y≈0.3〜0 / W/S は下部斜面 y≈1.6〜0）。ただし**俯瞰視点では y はほぼ隠れ、4谷が等距離（同 radial）に見える**ため視覚的問題なし（screenshot 確認）。y=0 完全一致は 3Dモデル化フェーズの範疇として据え置き。

### マイルストーン B（U3）への申し送り＝要確定
1. **遠方入場に必要なカメラ余白**: 麓（back row 最大 r≈6.7、S谷を厳密に麓へ寄せるなら更に外）＋その外の**入場ライン `_entranceRadius`（"かなり遠く"＝r≈10前後）**は、現行カメラ(y=20)の **N/S 可視半径 7.99/8.99 に収まらない**。→ §6 で事前承認済みの **カメラ・ズームアウト**（例: y≈27 or fov≈60、対称化のため z=-0.5→0）を U3 で適用し screenshot 確認。ズームアウト量＝「山の見かけサイズ」とのトレードオフ（要ユーザ確認）。
2. **入場ライン半径**: 平地（全方向 r≥8 で y=0）は視野内に確保できる範囲で最大化。`_entranceRadius` は U3 でカメラ確定後に数値決定。
