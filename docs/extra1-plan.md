# Extra 1 実装計画書（手触り調整・不具合修正：敵初弾／撃破行の見え方／移行バナー）

> 本書は `spec-summary.md` / `architecture.md` / `coding-conventions.md` / `mcp-guide.md` を正本として、Phase 3 完了後に挙がった **3 つの手触り調整・不具合修正** を、検証可能な単位（ユニット）に分解したもの。
> 位置づけは「現行フェーズの進行とは独立した **調整・不具合修正**」であり、`phases.md` のフェーズ進行（Phase 4 以降）を先取りするものではない。スコープは下記 3 点のみ。
>
> 1. **敵の初弾が遅い** → 敵が登場したら（ほぼ）即座に 1 発目を撃つ。
> 2. **複数行のとき撃破される行の見え方がおかしい** → 手前（砦寄り）の行が消えて見えるよう、繰り上がりを滑らかにし消滅エフェクトを足す。
> 3. **波の移行（開始／クリア）を画面中央に** → 中央バナーで「Wave n ― 波名」「Wave n クリア」を表示。
>
> 設計判断は事前の dig で確定済み（→ §5）。

## 0. 方針

- **着手前に必ず作業ブランチを切る（最重要）**:
  - `main`（Phase 3 までマージ済み）から新規ブランチを切ってから着手する。例: `git switch -c extra1-feel-fixes`。
  - **`main` に直接コミットしない**。コミットは **ユニット単位**（Unit A1 / A2 / B1 / B2 / C1 / C2）で行う（memory の方針）。
  - MCP ツーリング由来のファイルはコミットに含めない。
  - 全ユニット完了後にレビュー → `main` へマージ。
- **既存の純ロジックとテストは温存**: `EnemyMatrix<T>` / `AvalancheClock` の Core ロジックには手を入れない（撃破される行のインデックスは現状で正しい）。今回の修正は **データ（SO）・Gameplay の見せ方・UI・配線** に限定する。回帰として既存 EditMode を緑のまま維持する。
- **検証の二層**:
  - **EditMode テスト**（回帰中心）: 既存テストを緑に保つ。Core への新規ロジック追加は原則なし（State の getter 追加程度）。
  - **Unity-MCP 視覚検証**（`screenshot-game-view`）: 初弾の即時性／繰り上がり前進・雪しぶき／中央バナーは Play Mode で客観確認（`mcp-guide.md` パターン A〜D）。
  - 検証前提: Unity は単一共有インスタンス。MCP 操作は **メインセッションで直列**。`tests-run` 前に開いているシーンを保存（dirty だと中断する）。
- 調整値は SO か `[SerializeField]`（ADR-1）。Singleton は `GameManager` のみ。通信は C# event、購読は `OnDestroy` 解除（ADR-3）。敵・弾・エフェクトは **ObjectPool** で使い回す（ADR-4）。
- **ADR 追記予定**（→ §6）: ADR-20（撃破プレゼンテーション：前進 lerp＋雪しぶき puff）ほか。

## 1. 対象と方針サマリ

| 課題 | 根本原因（確認済み） | 対処 | 主な変更 | 検証 |
|------|----------------------|------|----------|------|
| ① 敵の初弾が遅い | `Enemy.Init` が `_fireTimer = FireInterval`（2s）で初期化＝初弾が 2 秒後 | 初弾遅延を SO 化（既定 0）。通常敵・突撃隊=0、変装敵=遅延維持 | `EnemyTypeSO`・`Enemy`・敵アセット3件 | MCP(Play) |
| ② 撃破行の見え方 | 消す行は正しいが `RepositionAll` が生き残りを**内側へ瞬間移動**→手前が即埋まり外周が消えた錯覚 | 繰り上がりを **lerp** 化（湧きはスナップ）＋撃破位置に **白い雪しぶき** | `Enemy`・`AllyNPC`・`ChannelController`・新規 `SnowPuff`・`GameManager` | MCP(Play) |
| ③ 移行を中央に | 移行文言が上部の小さい `_status` ラベル（[UIManager.cs:70](../Assets/Scripts/UI/UIManager.cs#L70)）に同居 | WaveIntro/WaveLull の文言を **中央バナー**へ（戦闘中は上部HUD据え置き）。波名付き | `WaveConfigSO`・Wave1/2/3 アセット・`WaveIntroState`/`WaveLullState`・`UIManager`・`GameManager` | MCP(Play) |

## 2. 変更・新規クラス一覧

| # | クラス | フォルダ / 名前空間 | 変更内容 | 依存 | 検証 |
|---|--------|---------------------|----------|------|------|
| A | `EnemyTypeSO` | Data | `_initialFireDelay`（既定0）追加 + `InitialFireDelay` プロパティ | なし | MCP(資産確認) |
| A | `Enemy` | Gameplay | `Init` の `_fireTimer` 初期値を `FireInterval`→`InitialFireDelay` に変更 | EnemyTypeSO | MCP(Play) |
| B | `Enemy` | Gameplay | 目標位置フィールド＋`SetTarget(pos, snap)`＋共通の `MoveTowardTarget()`（速度ベース lerp） | — | MCP(Play) |
| B | `AllyNPC` | Gameplay | `Update` 冒頭で `MoveTowardTarget()` を呼ぶ（`base.Update` 非呼出のため明示） | Enemy | MCP(Play) |
| B | `ChannelController` | Gameplay | `RepositionAll`＝目標セット(snap=false)／湧き・初期配置=snap=true。`Configure` に `createPuff` 追加。`ApplyAvalanche` で撃破位置に puff 発生。前進速度 `[SerializeField]` | Enemy, ObjectPool, SnowPuff | MCP |
| B | `SnowPuff` | Gameplay（新規） | 白球が一瞬拡大＋フェードして自動完了（コールバックでプール返却） | — | MCP(Play) |
| B | `GameManager` | Bootstrap | `CreateSnowPuff` ファクトリ＋専用マテリアル（白・加算/半透明）。`ChannelController.Configure` に注入 | 全体 | MCP |
| C | `WaveConfigSO` | Data | `_displayName` 追加 + `DisplayName` プロパティ | なし | MCP(資産確認) |
| C | `WaveIntroState` | Core | `public int WaveIndex => _waveIndex;` 公開 | — | EditMode(任意) |
| C | `WaveLullState` | Core | `public int WaveIndex => _waveIndex;` 公開 | — | EditMode(任意) |
| C | `UIManager` | UI | `_banner` 文字列＋`SetBanner`/`ClearBanner`＋`OnGUI` で中央帯（リザルト同系） | — | MCP(Play) |
| C | `GameManager` | Bootstrap | `UpdateStatusUI` で WaveIntro/WaveLull 時にバナー設定・他状態でクリア。`GetWaveDisplayName(index)` 補助 | UIManager, WaveConfigSO, States | MCP(Play) |

## 3. 検証可能な単位（ユニット）と実装順序

各ユニットは「完了条件」を満たした時点でコミットして次へ進む。3 マイルストーンは独立だが、Unity 単一インスタンスのため **直列** に進める（memory）。順序の目安は A（小）→ C（UI＋データ）→ B（最も手が入る）。

### マイルストーン A — Issue①: 敵初弾の即時化　✅ 完了（2026-06-17）

> **進捗**: EditMode **120本緑**（回帰なし）。コミット `cc2ab17`（A1）/ `df4ef09`（A2）。

**Unit A1: `EnemyTypeSO._initialFireDelay` 追加 ＋ `Enemy.Init` 改修** ✅
- 成果物:
  - `EnemyTypeSO` に `[SerializeField] private float _initialFireDelay = 0f;` と `public float InitialFireDelay => _initialFireDelay;` を追加（ADR-1）。
  - [Enemy.Init](../Assets/Scripts/Gameplay/Enemy.cs#L50) の `_fireTimer = type.FireInterval` を `_fireTimer = type != null ? type.InitialFireDelay : 0f;` に変更。コメント更新。
- 検証結果（MCP Play）: 3秒経過時点で ShotsFired=1〜8（最初フレームで即発射）、Fort HP 1000→775→215。`screenshot-game-view` で弾飛行を視覚確認。

**Unit A2: 敵アセットの初弾値設定 ＋ 種別差の検証** ✅
- 成果物（`assets-modify`）:
  - `BasicEnemy.asset` … `_initialFireDelay = 0`（即時・既定値のまま）。
  - `AssaultEnemy.asset` … `_initialFireDelay = 0`（即時・既定値のまま）。
  - `DisguisedEnemy.asset` … `_initialFireDelay = 2.0`（**遅延維持**＝発射で即バレさせない）。`_fireInterval=2.0` 据え置き。
  - `AllyNPC.asset` … 無発射（`factory=null`）のため変更なし。
- 検証結果（`assets-get-data`）: BasicEnemy=0 / AssaultEnemy=0 / DisguisedEnemy=2.0 を確認。

### マイルストーン B — Issue②: 撃破行の見え方（繰り上がり前進の lerp 化＋雪しぶき）　✅ 完了（2026-06-17）

> **進捗**: EditMode **120本緑**（回帰なし）。コミット `f54a298`（B1+B2 同一コミット）。

**Unit B1: 繰り上がり前進を滑らかに（湧きはスナップ）** ✅
- 成果物: `Enemy.SetTarget(pos, snap, speed)` / `MoveTowardTarget()` / `AllyNPC.Update` 明示呼出 / `ChannelController._advanceSpeed [SerializeField]` / `RepositionAll(justSpawned=null)` でスナップ/lerp 分離。
- 検証: `ApplyAvalanche` 直後にN谷敵が back row 座標（y≈3.25）から front 座標（y=2.70）へゆっくり lerp 前進することをスロー（timeScale=0.05）Play で確認。新規湧きはスナップ（即座に外周配置）。

**Unit B2: 雪しぶきエフェクト `SnowPuff`（プール）＋撃破位置で発生** ✅
- 成果物: 新規 `SnowPuff.cs`（0.25s 拡大＋輝度フェード → onDone でプール返却） / `ChannelController.Configure` に `Func<SnowPuff> createPuff` 追加 / `ApplyAvalanche` でKill前に `_puffPool.Get().Play(pos, Release)` / `GameManager.CreateSnowPuff`（加算 Unlit 白球）。
- 検証（MCP Play）: `ApplyAvalanche: killed=3` → `SnowPuff instances (incl. inactive): 3, all active=True`、位置 `(-0.70,2.70,0)` / `(0.70,2.70,0)` / `(0.00,2.70,0)` = 撃破された前列の正確な座標に一致。プール返却後は非アクティブで Instantiate/Destroy なし（ADR-4 維持）。

**Unit B1: 繰り上がり前進を滑らかに（湧きはスナップ）**
- 成果物:
  - `Enemy` に目標位置を追加し、前進を速度ベース lerp 化する。
    - `private Vector3 _targetPos;`、`public void SetTarget(Vector3 pos, bool snap)`（snap 時は `transform.position` も即代入）。
    - 共通メソッド `protected void MoveTowardTarget(float speed)` を用意し、`Vector3.MoveTowards` で `_targetPos` へ寄せる。
    - `Enemy.Update` 冒頭で `MoveTowardTarget(...)` を呼ぶ（発射処理の前）。
    - **`AllyNPC.Update` は `base.Update` を呼ばない**ため、その冒頭でも `MoveTowardTarget(...)` を明示的に呼ぶ（`DisguisedEnemy` は `Enemy.Update` 継承のため自動で効く）。
  - [ChannelController.RepositionAll](../Assets/Scripts/Gameplay/ChannelController.cs#L215-L226) を「`transform.position` 直接代入」から「`SetTarget(PositionFor(...), snap:false)`」に変更。
  - **新規湧き・Wave 開始の初期配置はスナップ**: `SpawnEnemy`/`SpawnAlly`/`SpawnDisguised` で行追加直後の当該個体は `SetTarget(pos, snap:true)`（プール由来の砦寄り初期 transform からの滑り込みを防ぐ）。
    - 実装メモ: `RepositionAll` に「新規個体だけ snap」を伝える術が要る。`RepositionAll(Enemy justSpawned = null)` で該当個体のみ snap=true、他は snap=false とするのが最小。
  - 前進速度は `ChannelController` の `[SerializeField] private float _advanceSpeed`（例 既定 3〜4 u/s、MCP で調整。ADR-1）。
- 依存: なし（A と独立）
- 完了条件:
  - `assets-refresh` でコンパイル通過、**既存 EditMode 全緑（純ロジック不変の回帰確認）**。
  - MCP(Play, パターンA): 先頭行撃破時、生き残りが**瞬間移動でなく内側へスッと前進**。新規湧きは外周に**スナップ**（砦から滑り込まない）。slow-mo（`Time.timeScale`）＋ `screenshot-game-view` 連番で前進の様子を記録。連続撃破（`killInterval`=0.4s 毎）でも「常に最新の目標へ lerp」で破綻しないことを確認。

**Unit B2: 雪しぶきエフェクト `SnowPuff`（プール）＋撃破位置で発生**
- 成果物:
  - 新規 `SnowPuff`（Gameplay, MonoBehaviour）: `Play(Vector3 pos, Action<SnowPuff> onDone)` で起動。寿命 ≒0.25s の間にスケールを拡大＋アルファをフェードアウトし、完了時 `onDone(this)` を呼ぶ（所有者がプール返却）。`Enemy` 同様 `MaterialPropertyBlock` で色/アルファを個別制御。
  - `GameManager` に `SnowPuff CreateSnowPuff()` ファクトリ（小さな白球プリミティブ＋専用マテリアル。URP/Unlit 半透明 or 加算）を追加し、[BuildChannels](../Assets/Scripts/Bootstrap/GameManager.cs#L164-L176) の `ch.Configure(...)` に `createPuff` を渡す。
  - `ChannelController.Configure` に `Func<SnowPuff> createPuff` 引数を追加し、`ObjectPool<SnowPuff> _puffPool` を生成。
  - [ChannelController.ApplyAvalanche](../Assets/Scripts/Gameplay/ChannelController.cs#L138-L169): **`Kill`／`Release` の前**に各個体の `transform.position` を取得し、その位置で `_puffPool.Get().Play(pos, _puffPool.Release)` を呼ぶ。敵・変装・味方を問わず雪崩で消えた各個体に発生させる。
- 依存: B1（同じ撃破経路に手を入れるため B1 の後）
- 完了条件:
  - `assets-refresh` 通過、既存 EditMode 全緑。
  - MCP(Play): 撃破位置に白球が一瞬拡大→フェード→**自動でプール返却**（GameObject 非アクティブ・`Instantiate/Destroy` がホットパスに出ない）。`screenshot-game-view` で「手前の行がそこで弾けて消えた」ことを確認。

### マイルストーン C — Issue③: 波の移行を中央バナーに　✅ 完了（2026-06-17）

> **進捗**: EditMode **120本緑**（回帰なし）。コミット `8be6034`（C1+C2 同一コミット）。

**Unit C1: `WaveConfigSO._displayName` 追加 ＋ 3 アセット設定 ＋ State の WaveIndex 公開** ✅
- 成果物:
  - `WaveConfigSO` に `[SerializeField] private string _displayName;` と `public string DisplayName => _displayName;` を追加（ADR-1）。
  - アセット設定（`assets-modify`）: `Wave1=快感波` / `Wave2=本隊侵攻` / `Wave3=突撃隊＋変装波`。
  - `WaveIntroState`・`WaveLullState` に `public int WaveIndex => _waveIndex;` を追加。
- 検証結果（`assets-modify` ＋ EditMode）: 3 アセットの値設定確認。EditMode 120本緑（回帰なし）。

**Unit C2: `UIManager` 中央バナー描画 ＋ `GameManager` 配線** ✅
- 成果物:
  - `UIManager`: `_banner` + `SetBanner(text)` / `ClearBanner()` + `OnGUI` で中央帯描画（リザルト表示中は非表示）。
  - `GameManager.UpdateStatusUI`: WaveIntro → `SetBanner("Wave n ― 波名")`、WaveLull → `SetBanner("Wave n クリア")`、WavePlaying/Title/Result → `ClearBanner()`。`GetWaveDisplayName(int i)` 補助メソッドを追加。
- 検証結果（MCP Play）:
  - WaveIntro: 中央に「Wave 1 ― 快感波」帯表示、上部ステータス空 ✅
  - WaveLull: 中央に「Wave 1 クリア」帯表示 ✅
  - WavePlaying: バナー消去・上部 HUD（「Wave 1/3 残り 24s — W/A/S/D…」）のみ ✅

## 4. クリティカルパス / 実装順

```
[A1→A2] 敵初弾   ─┐
[C1→C2] 中央バナー ─┼→ 各々独立に完結（直列実行）
[B1→B2] 撃破見え方 ─┘   ※B は最も手が入るので最後に回すと安全
```

- 3 マイルストーンは相互依存なし。最短で「分かる成果」を出すなら A（最小）→ C（UI 映え）→ B（手触り）。
- 各ユニット完了ごとに `screenshot` 証跡を残し、ユニット単位でコミット。

## 5. 要確定事項（dig で確定済み）

| 項目 | 決定 | 出所 |
|------|------|------|
| ①の主語 | 敵の発射タイミング（プレイヤー雪崩ではない） | dig Q1 |
| 初弾遅延の持たせ方 | `EnemyTypeSO._initialFireDelay`（既定0）。Inspector 調整可 | dig Q2 |
| ②の症状 | 消す行は正しいが、繰り上がりの瞬間移動で「手前が生き残り外周が消える」錯覚 | dig Q3〜Q5 |
| 「手前」の定義 | 砦に近い側（内側）の行 | dig Q4 |
| ②の対処 | 前進は残し滑らかに（lerp）＋消滅エフェクト。純ロジック温存 | dig Q5 |
| 前進と湧きの分離 | 繰り上がりは lerp、新規湧き/初期配置はスナップ（理由＝プール由来で初期 transform が砦寄り） | dig Q6 |
| 消滅エフェクト | 白い雪しぶき（小球が拡大＋フェード）。各撃破個体位置、プール化 | dig Q7 |
| ③の対象 | WaveIntro と WaveLull の両方を中央バナー。戦闘中は上部HUD据え置き | dig Q8 |
| ③の文言 | 番号＋波名（`WaveConfigSO._displayName` を追加） | dig Q9 |
| ③の見た目 | リザルト同系の半透明黒帯＋中央大文字、フェードなし | dig Q10 |
| ①と変装の干渉 | 即時発射は通常敵・突撃隊のみ。変装敵は遅延維持（tell で見分ける意図を保持） | dig Q11 |

## 6. ADR 追記予定（`architecture.md`、最新は ADR-19）

- **ADR-20（撃破プレゼンテーション）**: 撃破時の生き残り繰り上がりを瞬間移動から **速度ベース lerp** に変更し、新規湧き/初期配置はスナップで分離。撃破位置に **雪しぶき puff（プール）** を発生。`EnemyMatrix`/`AvalancheClock` の純ロジックとテストは不変（見せ方のみの変更であることを明記）。
- （任意）**ADR-21**: 初弾遅延を `EnemyTypeSO._initialFireDelay` で SO 化し、種別差（通常/突撃=即時・変装=遅延）で Phase3 の tell 識別を維持。
- （任意）**ADR-22**: 移行情報を上部ステータスから **中央バナー** へ分離（戦闘中の常時HUDと移行アナウンスの役割分担）。IMGUI 据え置き（ADR-13 の範囲内）。

## 7. スコープ外（今回やらない）

- 敵の `MoveSpeed`（define-only）の実挙動化、敵の常時前進。
- uGUI 化・フェード等の凝った演出（バナーはフェードなし）。
- SO イベントチャネル移行（ADR-3 の条件未達）。
- サウンド・パーティクルシステム（雪しぶきは簡易プリミティブで代替）。
