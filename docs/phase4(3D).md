# Phase 4 実装計画書（3D表現化 / 世界の3D透視化）

> 本書は `spec-summary.md` / `docs/architecture.md`（**ADR-21**・および踏襲する ADR-8/9/11/12/14/18）/ `phases.md`（Phase 4 受け入れ基準）/ `coding-conventions.md` / `mcp-guide.md` を正本として、Phase 4 で実装する変更（改修＋新規アセット）を **依存順** と **検証可能な単位（ユニット）** に分解したもの。完了判定の bar は `phases.md` の Phase 4 受け入れ基準。
> 本フェーズは **描画（表現層）のみ**を変更し、Core（`EnemyMatrix`/各種 Clock/State/Pool/`Scoring`）・雪崩の行消去・生成パイプライン・入力・`Projectile` ロジックは**無改修**とする（ADR-21）。移植元 `kakigori-3d-t` の再利用可能資産は実質 `New Terrain 3.asset`（造形済みの山）**1つのみ**で、アクターの3Dモデル・VFX・雪テクスチャは存在しないため、それらは本フェーズの新規制作物（アクターはプリミティブ継続＝案Y）。

## 前提（Phase 3 / Extra 1 後の現状＝土台）

- **ブランチ**: `main`（Phase 3・Extra 1 完了）から `phase4-3d-presentation` を切る。コミットは**ユニット単位**（MCP ツーリングは除外）。
- **EditMode ベースライン**: **120本緑**。本フェーズは Core を触らないため **新規テストは原則なし・120本の回帰ゼロが条件**（座標が純ロジックでないため EditMode 追加は困難＝視覚検証は MCP 主）。
- **現状の表現（置き換え対象）**:
  - 山＝`GameManager.BuildMountain`（手続き生成テクスチャの Quad、z=3）。
  - 空間＝**XY 平面**。`ChannelController.DirectionFor` は `Vector3.up/left/down/right`、`_perpDir=(outward.y,-outward.x,0)`、`PositionFor` は `_center + _outwardDir*along + _perpDir*across`（全て z=0）。
  - カメラ＝俯瞰の正射影（MainScene）。ライト無し・全マテリアル URP/Unlit。
  - アクター＝`GameManager.Create*` がプリミティブ（Cube/Sphere）を生成し `DestroyCollider`（**物理なし**）。
- **不変の足場（本フェーズで流用）**:
  - `Enemy.SetTarget(pos, snap, speed)` ＋ `Update` の `MoveTowards`（Extra1/ADR-20 の繰り上がり lerp）は**座標系非依存**＝斜面座標でもそのまま動く。入場（Option A）にも流用。
  - `ChannelController.Configure(... Func<Enemy> ...)` の `Func<T>` 注入（ADR-5）に倣い、**高さサンプラも注入**する。
  - `ProjectileFactory` / `ObjectPool` / `IGameContext` / `UIManager`（IMGUI・スクリーン空間＝3D化の影響なし）。
- **ADR**: 本計画が Proposed 化するのは **ADR-21**（着手時 Accepted 化）。ADR-8/9/11/12/14/18 を踏襲し覆さない。

## 0. 方針

- **Phase 4 が変えるもの**: 見た目だけ。「3D の雪山に隊列が並び、麓から敵が上ってくる」表現。**ゲーム性・数値・ロジックは Phase 3 のまま**。
- **依存の向き（不変）**: `Core`（純C#）→ `Data`(SO) → `Input`/`Factory` → `Gameplay`(MonoBehaviour) → `UI` → `Bootstrap`。Core は上位を知らない。
- **縫い目は3点に閉じる**（把握フェーズで確認済み）:
  1. **`ChannelController.PositionFor`/`DirectionFor`** ＝ 空間埋め込み（XY→XZ＋高さ）。
  2. **`GameManager` の View 生成** ＝ 山を Terrain へ（アクターはプリミティブ維持）。
  3. **MainScene のカメラ／ライト** ＝ 透視化＋Directional Light。
- **物理なし不変条件の維持**: 高さは `TerrainData.GetInterpolatedHeight` で**サンプル**（コライダー/Raycast 不使用）。アクターの `DestroyCollider` も継続。
- **検証の二層**:
  - **EditMode**（回帰）: 既存 120本が緑のまま（Core 無改修の証明）。
  - **Unity-MCP 視覚検証**（`screenshot-game-view`）: 3D 雪山表示・斜面上の隊列・雪崩の先頭行消去・入場アニメ・Wave1 通し。`mcp-guide.md` に沿う。
- **1ユニット = 「単独でビルド通過し、固有の合否判定（MCPスクショ or テスト緑）が下せる最小の塊」**。下から積む。
- **スコープ厳守**: 3Dモデル制作・本格VFX・サウンド・バランス調整・弾の放物線化は**やらない**（`phases.md` Out of scope）。

## 1. 依存レイヤー図（Phase 3 からの差分）

```
Bootstrap  GameManager ── BuildMountain(Quad) ▶ 【改修】Terrain 配置 ＋ heightSampler 生成注入 ＋ カメラ/ライト設定
              │ Func<T> 注入（既存）＋ Func<float,float,float> heightSampler（新規注入）
              ▼
Gameplay   ChannelController ── PositionFor/DirectionFor/_perpDir 【改修：XY→XZ＋高さサンプル】
              │                   SpawnEnemy 【改修：Option A 入場】
              │ SetTarget（既存・座標系非依存）
              ▼
           Enemy / Projectile / Fort … 【無改修】（transform だけ触る＝自動で3D化）
Core/Data/Input/UI … 【全て無改修】
Assets     New Terrain 3.asset【移植】 / Snow lit material【新規】 / MainScene カメラ・ライト【改修】
```

## 2. 全クラス・アセット一覧（改修＋新規）

| 種別 | 対象 | 変更 |
|---|---|---|
| 改修(コード) | `ChannelController` | `DirectionFor`/`_perpDir` を XZ、`PositionFor` に高さサンプル、`Configure` に `Func<float,float,float> heightAt` 追加、`SpawnEnemy` に入場（Option A） |
| 改修(コード) | `GameManager` | `BuildMountain`→Terrain 配置、`_heightAt` サンプラ生成（`TerrainData.GetInterpolatedHeight` 包み）を各 `ChannelController.Configure` に注入、Terrain の lit マテリアル、（Aim/床の z→XZ 対応） |
| 改修(シーン) | `MainScene` | Main Camera を透視＋俯角に、Directional Light 追加、環境光/Fog 任意 |
| 新規(アセット) | `New Terrain 3.asset`（＋必要な TerrainLayer） | `kakigori-3d-t` から移植（.asset＋.meta を取り込み） |
| 新規(アセット) | Snow lit material（＋任意 Snow TerrainLayer） | 雪用の簡易 lit（土色 ADG は不使用） |
| 無改修 | Core 全部 / State / Input / `Projectile` / `Fort` / `Enemy`(Init) / `ObjectPool` / `ProjectileFactory` / `UIManager` | — |

## 3. 検証可能な単位（ユニット）と実装順序

### マイルストーン A — 世界の器（Terrain＋カメラ＋ライト）

**Unit U1: Terrain 移植＋配置**
`New Terrain 3.asset`（＋参照 TerrainLayer）を demo の `Assets/` に取り込み、MainScene に Terrain を配置。雪用の簡易 lit マテリアルを当てる（土色 ADG レイヤーは使わない）。
- 合否: MCP `screenshot-scene-view`/`game-view` で山が表示される（この時点でアクターは XY 平面のままなので浮いて見えてよい）。

**Unit U2: 透視カメラ＋ライティング**
Main Camera を正射影→透視・俯角に。Directional Light を追加し Terrain が陰影で立体に見える。
- 合否: MCP screenshot で「光と影のある3D雪山」。EditMode 120本緑（ロジック無改修）。

### マイルストーン B — 空間の核（斜面配置）

**Unit U3: 高さサンプラの用意（GameManager）**
`Func<float,float,float> _heightAt = (x,z)=> terrainData.GetInterpolatedHeight(u,v) + terrainWorldY`（(x,z)→world Y、コライダー不使用）。各 `ChannelController.Configure` に注入。
- 合否: ビルド通過＋サンプル値が Terrain 高さに一致（MCP `script-execute` でスポット確認）。

**Unit U4: `PositionFor`/`DirectionFor` を XZ＋高さへ**
`DirectionFor` を N=+z/S=-z/E=+x/W=-x、`_perpDir` を XZ 直交、`PositionFor` は `along/across` を XZ で置き、Y=`_heightAt(x,z)`。`RepositionAll` 経由で敵が斜面に乗る。**Core・雪崩・生成は無改修**。
- 合否: MCP Play — ①敵が4方向の麓から**斜面上に**（浮かず沈まず）湧き隊列を組む ②`Space`＋`W/A/S/D` で**先頭行が消える**（Core 不変の実証）③EditMode 120本緑（回帰ゼロ）。

### マイルストーン C — 手触り（入場アニメ・Option A）

**Unit U5: 敵の入場（Option A）**
`SpawnEnemy` で敵を隊列スロットより麓側（下り斜面の外側）に置き、`PositionFor(backRow,slot)` へ `snap:false` で歩かせる（既存 `_advanceSpeed`/`SetTarget`）。**gameplay locomotion ではない**（ADR-18 整合）。
- 合否: MCP Play — 敵が麓側から隊列位置へ歩いて接近して整列する。雪崩・繰り上がりに回帰なし。

### マイルストーン D — 統合・仕上げ（Phase 4 完了判定）

**Unit U6: 仕上げ＋Wave1 通し＋ADR 確定**
Aim インジケータの斜面対応（谷ハイライトを XZ／斜面へ）、雪マテリアル調整、（任意）アクターの斜面向き。Wave1〜3 通しを MCP で検証し、`architecture.md` ADR-21 を Proposed→Accepted 化。
- 合否（＝`phases.md` Phase 4 受け入れ基準の総達成）: Terrain 雪山が透視表示／4方向斜面湧き／雪崩で先頭行消去／Option A 入場／弾着弾・砦HP減・スコア・Result が3D下で成立／物理不使用維持／EditMode 120本緑／Singleton=GameManager・入力=IInputSource 維持。

## 4. クリティカルパス（最短で「3D の手触り」に到達する線）

**U1→U2→U4** が核（山を出す→透視で見せる→敵を斜面に乗せる）。ここまで通れば「3D 雪山に隊列が並び、雪崩が走る」という Phase 4 の主目的が成立する。U3 は U4 の前提（並行不可）。U5（入場）・U6（仕上げ）は手触り強化で、核の後に積む。

## 5. 要確定事項（着手前に解消 or 既定値で進行）

- **Terrain の実サイズ／向き**: `New Terrain 3.asset` の width/length/height と、砦中心（`_center`）を山頂に合わせる配置・スケール（既定: 山頂を原点、`_frontGap`/`_rowSpacing` は現行値のまま斜面へ写像。視野に収まらなければ数値調整＝ADR-14 同様「方針不変・数値のみ」）。
- **カメラ俯角／画角**: 4方向すべての麓が画面に入る俯角（既定: 斜め上 45°付近から）。奥の谷（カメラ反対側）が見切れないこと。
- **雪マテリアル**: 単色白 lit で開始（テクスチャ制作は最小限）。必要なら Snow TerrainLayer を1枚追加。
- **高さサンプルの原点対応**: Terrain のワールド原点・`TerrainData.size` と `PositionFor` の (x,z) → 正規化 (u,v) 変換の突き合わせ（U3 で確定）。
- **アクターのスケール**: 3D 俯瞰で視認できるサイズ（現行 0.5 立方を Terrain スケールに合わせ調整の可能性）。

## 6. 着手メモ（マイルストーン A 完了・2026-07-06）

把握フェーズの想定と実機の差分（詳細は `architecture.md` ADR-21 着手メモ）:

- **縫い目は 3 点 → 4 点**: demo は URP **2D Renderer** 構成だったため、Directional Light が効く **3D Forward レンダラ**への切替が前提（4 点目）。`kakigori-3d-t` の `PC_RPAsset`/`PC_Renderer`/`SampleSceneProfile` を `Assets/Settings/` に流用して切替済み（`GraphicsSettings` 既定＋全 Quality レベル）。→ 「再利用資産は Terrain 1 つのみ」を **Terrain＋URP 3D パイプライン一式**に補正。
- **Terrain**: `New Terrain 3` は「広い平面の隅に造形された compact な山」（当初フラットと誤読 → 描画で山を確認）。`size` を **(120,160,120)** に縮小、山頂を原点に配置。雪は白 TerrainLayer＋URP Terrain/Lit。周囲平面は麓の雪原として残置。
- **描画 gotcha**: `Terrain.drawInstanced=false`（instanced バリアント未コンパイルでマゼンタ化を回避）、PC_Renderer 同梱の SSAO 無効化、環境光強め。
- **U3/U4 への申し送り**: 5 章の「Terrain 実サイズ／向き」は size=(120,160,120)・山頂原点で暫定確定。高さサンプラ (u,v) 変換は size と terrain 原点（山頂を原点に置いた分のオフセット）を突き合わせる。`GameManager.BuildMountain` の Quad はマイルストーン B で撤去。

## 7. 着手メモ（マイルストーン B 完了・2026-07-06）

- **U3**: `GameManager.HeightAt(x,z)=_terrain.SampleHeight` を `ChannelController.Configure` に注入（`Func<float,float,float>`・物理なし）。`_terrain` は `[SerializeField]`（シーン Terrain(Snow)）。`BuildMountain` Quad 撤去、砦を山頂（HeightAt(0,0)≈5.4）へ。
- **U4**: `DirectionFor`（N=+z/W=-x/S=-z/E=+x）・`_perpDir=(z,0,-x)`・`PositionFor` に Y=高さサンプル。Core・雪崩・生成・EditMode（120本）は無改修。
- **スケール確定**: size=(120,…) では山 footprint 半径≈2 でプレイフィールド（2.7〜4.35）が平地に落ちた → 実測して **size=(316.8,160.4,316.8)**（footprint 半径≈7）へ拡大。斜面高さ r=2.7→2.28 / 4.35→0.88、山頂 5.4。5 章「Terrain 実サイズ」はこれで確定。
- **検証（MCP Play）**: 4谷×9体＝全36体が斜面上（y=1.03〜4.24）、砦=山頂、`ApplyAvalanche` で先頭行 killed=3・rows 3→2。弾着弾で砦HP減も確認。
- **C/D への申し送り**: Aim インジケータは XY 前提のままで斜面と不整合（U6 で XZ／斜面対応）。カメラは暫定（山頂俯瞰）。入場アニメ（Option A）は U5。

## 8. 着手メモ（マイルストーン C 完了・2026-07-06）

- **U5（Option A 入場）**: `ChannelController` に `[SerializeField] _entranceGap=1.5`（隊列スロットより麓側＝下り斜面外側へのオフセット）を追加。`RepositionAll(justSpawned)` の新規湧き分岐を「入場位置へ `snap:true` → スロットへ `snap:false`」の2段に変更し、既存の `SetTarget`/`_advanceSpeed` lerp（ADR-20）で麓側から歩かせる。`PositionFor(row, slot, extraAlong=0)` に along オフセット引数を追加（入場＝`_entranceGap`）。3 spawn 経路（`SpawnEnemy`/`SpawnAlly`/`SpawnDisguised`）は全て `RepositionAll(justSpawned)` を通るので**1 箇所の変更で統一適用**（basic/ally/変装すべて麓側から入場）。gameplay locomotion ではない cosmetic reposition ＝ **ADR-18 と整合**。
- **不変**: Core（`EnemyMatrix`/Clock/State/Pool/`Scoring`）・雪崩・生成・`Enemy.Update` は無改修。既存の繰り上がり（`RepositionAll()` 無引数）は従来どおり lerp。
- **検証（MCP Play）**: ①北谷に1体湧き → 湧き直後 `alongOutward=4.20`（=slot 2.7＋entranceGap 1.5・y=0.83＝麓側の低い位置）→ 数百ms 後 `alongOutward=2.70`（=slot・y=2.55＝斜面を登って整列）を実測＝**麓側から隊列位置へ歩いて接近**。②南谷に6体（2行）→ `ApplyAvalanche` killed=3・rows 2→1・count 6→3＝**雪崩/繰り上がり回帰なし**。③screenshot で斜面上の survivor 3体を確認。EditMode **120本緑**（回帰ゼロ）。
- **D への申し送り**: 残 U6＝Aim インジケータの斜面対応（XY 前提で不整合）、カメラ/雪マテリアル仕上げ、（任意）アクター斜面向き、Wave1〜3 通し、ADR-21 Proposed→Accepted 化。
