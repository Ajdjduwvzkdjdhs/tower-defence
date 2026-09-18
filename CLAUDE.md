# かき氷タワーディフェンス (Kakigori TD)

雪山の神となり、頂上からかき氷を削って雪崩を起こし、麓から砦を遠隔攻撃する敵を殲滅レースで退ける、**体験型タワーディフェンス**。
現在は **単一ステージの「目標撃破数レース」**（既定100体撃破でクリア／砦HP 0 でゲームオーバー）で、
爆発兵の連鎖・盾兵・コンボ・必殺ビーム砲が主要ギミック（ADR-30）。

このリポジトリは **展示用インスタレーションの本番ビルド**。ゲームロジックは Phase 1〜6（+ Extra 1）まで実装済み、
以降 **Phase 7（展示ビルド化：3Dアート・マルチシーン・LiDAR入力）進行中**。
物理入力（かき氷機ハンドルの回転＝シリアル／氷の位置検知＝カメラ・LiDAR）も接続済み。キーボードは**開発用のフォールバック**として残っている。

> このファイルの記述と実コード・実シーンが食い違ったら、**実コードを正**とし、このファイルを直すこと。
> **個人の運用方針・作業ポリシーは `CLAUDE.local.md`（gitignore 済み・非共有）に記載。** 本ファイルはチーム共有のプロジェクト説明。

## 最初に読む（設計・仕様の ground truth）

作業開始前に該当箇所を読むこと。ただし下記は鮮度に差がある（「ドキュメントの鮮度」節を参照）。

- `docs/かき氷TD_プロトタイプ仕様書.md` — 仕様書 v2（**何のゲームを**作るかの一次情報）
- `docs/spec-summary.md` — 仕様の要約
- `docs/architecture.md` — アーキテクチャ決定と ADR（**どう**作るか）
- `docs/coding-conventions.md` — コーディング規約
- `docs/phases.md` — フェーズ毎のゴールと受け入れ基準
- `docs/phase*.md` / `docs/extra1-plan.md` / `docs/audio-plan.md` — 各フェーズの詳細計画
- `docs/mcp-guide.md` — **旧** Unity-MCP 時代の検証フロー（現在は Unity Pipeline ＋ `unity` CLI に移行。「Unity Editor 連携」節を参照。ツール名は読み替え）

## 技術スタック

- エンジン: Unity **6000.3.16f1**（Unity 6.3 / URP 17.3）— バージョンは確定済み
- 主要パッケージ: URP 17.3 / Cinemachine 3.1 / Input System 1.19（**新** Input System のみ）/ Test Framework 1.6
- 言語: C#
- テスト: Unity Test Framework（**EditMode を主**とする。`Assets/Tests/EditMode/`、254本）

## 実行とテスト

### シーン構成（Build Settings 登録順）

1. `Assets/Scenes/TitleScene.unity` — タイトル。`Enter` で `TH` へ（`Assets/Scripts/Scenes/TitleScene.cs`、フェード付き）
2. `Assets/Scenes/TH.unity` — **ゲーム本体**。`Assets/Prefabs/TH_Stage.prefab` を配置しており、そこに
   `GameManager`（唯一の Composition Root / Singleton）・Terrain（雪山）・城・敵プレハブ・各種 SO 参照が入っている
3. `Assets/Scenes/ResultScene.unity` — クリア時のリザルト。クリアタイムを表示し `Enter` で `TitleScene` へ
4. `Assets/Scenes/GameOverScene.unity` — 敗北時。到達撃破数を表示し `Enter` で `TitleScene` へ

`GameManager` は決着後 `_resultLimit` 秒待って、クリアなら `ResultScene`・失敗なら `GameOverScene` へ遷移する
（結果は `UI/GameResultHolder` の static 経由で渡す）。

**スコアランキング（ADR-34）**: クリアタイムのみ対象（失敗は記録しない）。`GameManager` が起動時に読み、クリア時に記録→保存し、
`ResultScene` が上位 10 件＋今回の順位を表示する。保存先はローカルの JSON ファイル
`Application.persistentDataPath/ranking.json`（macOS: `~/Library/Application Support/DefaultCompany/kakigori_demo/ranking.json`。
環境変数 `KAKIGORI_RANKING_FILE` でフルパス上書き可）。リセットしたければこのファイルを消す。
壊れていれば `ranking.json.corrupt-<時刻>` に退避して空から始まる。保持件数は `GameManager` の `Ranking Capacity`（既定 100）。
**リセット**は Title / Result 画面で `D` キーを 5 秒長押し（左上に進捗バー → 「ランキングを消去しました」）。

**Build 対象外の開発用シーン**（再生して個別検証に使う）:
- `Assets/Scenes/SampleScene.unity` — Phase 5/6 期の旧セットアップ（GameManager をシーンに直置き）。TH_Stage プレハブ化前の名残
- `Assets/Scenes/nadare_test.unity` — 雪崩演出の検証
- `Assets/Scenes/character_test.unity` — アクターのモデル/アニメ検証

### テスト方針

- 純ロジック（`Assets/Scripts/Core/` 配下）は **EditMode テスト**を必須とする（`Assets/Tests/EditMode/`、現在254本）。
- Unity 固有の挙動（シーン遷移・Play Mode の動き・UI 表示・演出）は **起動中の Editor を `unity` CLI で駆動して**スクリーンショット/ログで確認する（下記）。
  「動いた気がする」で終わらせず、客観的な証拠を残してから次へ進む。

## Unity Editor 連携（Unity Pipeline ＋ `unity` CLI）

このプロジェクトは **`com.unity.pipeline`**（`Packages/manifest.json` の dependency）を入れており、起動中の Unity Editor が
`127.0.0.1` のローカル HTTP サーバ（既定ポート 7800、`Library/Pipeline/.unity-pipeline-port` にポート＋トークン）を公開する。
これを **`unity` CLI**（`~/.unity/bin/unity`、beta チャンネル）で叩いて、Claude Code が Editor をヘッドレスに操作・検証する。
接続情報は CLI が自動解決するので、**プロジェクトルートから `unity …` を実行するだけ**でよい。

> 旧「Unity-MCP」（`com.ivanmurzak.unity.mcp`）は 2026-07-30（commit `01f4316`）に manifest から削除、
> 残っていた OpenUPM の `scopedRegistries`（`com.ivanmurzak` scope 等）も `preview-feedback` ブランチで撤去済み。
> 検証は Unity Pipeline ＋ `unity` CLI に一本化する。`docs/mcp-guide.md` は旧 MCP 前提のまま残っており、ツール名（`screenshot-game-view` 等）は読み替えること。

### よく使うコマンド（プロジェクトルートで実行）

| 目的 | コマンド |
|------|----------|
| Editor が繋がるか | `unity status` / `unity pipeline list`（`セーフモード` 列に注意） |
| 公開コマンド一覧 | `unity command`（または `unity list`） |
| Editor 状態 | `unity command editor_status` |
| 再コンパイル | `unity command recompile` → `unity command recompile_status`（`completed`/`up_to_date` までポーリング） |
| EditMode テスト | `unity command run_tests --mode editor --filter <TestClass>`（全件は重いので `--filter` 推奨。JSON は `--json`） |
| ゲーム画面キャプチャ | `unity command screenshot --view game --output Temp/pipeline-screenshots/x.png`（Play中の overlay UI 込みは `capture_game_view --source screen`） |
| Console ログ | `unity command get_console_logs --severity error` |
| シーン階層 | `unity command get_scene_hierarchy` / GameObject は `find_gameobjects` |

編集→検証ループ: `.cs` を編集 → `unity command recompile` → `recompile_status` をポーリング → `unity command run_tests --mode editor --filter <対象>`。

### ルール

- **`unity status` が Editor を返すなら、`.unity` / `.prefab` / `.asset` の YAML を手編集しない**。`unity command`（`set_component_properties` / `set_serialized_field` / `create_gameobject` 等）で**動いている Editor を駆動**する。手編集は fileID/GUID を壊しやすく、Editor にも反映されない。
- **セーフモード（C# コンパイルエラーで起動）時のみ例外**: Pipeline が読み込まれず `unity command` が繋がらない。`unity pipeline list` で確認し、**その時は `.cs` を直接直してコンパイルを通す**のが正しい対応。
- Claude Code が**確認なしで自動実行**してよいのは読み取り・検証系のみ（`.claude/settings.local.json` の allowlist）。`eval` / `build` / `switch_build_target` / シーン・アセットの書き換え / `editor_play`・`editor_stop` は都度確認する。
- 破壊的コマンドは Pipeline 側で `confirm=true` ゲートあり。

### セットアップ（各開発者ローカル。リポジトリには入らない）

- `unity` CLI: `curl -fsSL https://public-cdn.cloud.unity3d.com/hub/prod/cli/install.sh | UNITY_CLI_CHANNEL=beta bash`
- `com.unity.pipeline` を未導入のプロジェクトへ: `unity pipeline install`
- コマンドリファレンス＋使い方のスキル: `unity skill install claude-code --local`（`.claude/skills/unity-cli/` に展開。`.claude/` は gitignore 済み）
- `.claude/settings.local.json` … 検証ループ用の Bash 許可リスト（個人設定・gitignore 済み）

## 入力アーキテクチャ（`Assets/Scripts/Input/`）

すべて `IInputSource`（`ActiveChannel:int(-1=未選択)` / `IsShaving:bool` / `ConfirmPressed:bool`）経由。
`GameManager` が `CompositeInputSource` を組み立て、`InputMode`（`_inputMode` SerializeField）で **谷選択の源**を切り替える。

| 実装 | 役割 | 由来 |
|------|------|------|
| `KeyboardInputSource` | 開発用。`W/A/S/D`＝谷選択（ラッチ）、`Space` 長押し＝削り、`Enter`＝決定 | ADR-6/10 |
| `SerialInputSource` + `SerialInputProtocol` + `SerialHandler` | かき氷機ハンドルの回転（シリアル/BT シリアル）→ 削り。谷選択・決定はしない | ADR-15 / Phase 1.5 |
| `VisionInputSource` + `VisionInputProtocol` | 氷の位置検知 → 谷選択（`ActiveChannel`）。LiDAR 版はさらに 3列目 `shaving` で削りも | ADR-24 / Phase 5・LiDAR は 2026-08 |
| `CompositeInputSource` + `InputMode` | 上記を合成。谷選択＝`InputMode` で Keyboard/Vision 切替、削り＝keyboard `Space` OR シリアル OR LiDAR shaving の論理和、決定＝keyboard | ADR-15/24 |

- 展示本番は `_inputMode = Vision` + シリアル併用、開発時は `_inputMode = Keyboard`。
- 調整値は SO に集約: `SerialConfigSO`（`Assets/ScriptableObjects/BTSerialConfig.asset` 等）、`VisionConfigSO`（`VisionConfig.asset`）。

### 物理トラッカー ↔ Unity の接続方式：ファイル経由ポーリング（ソケットではない）

- `tools/` 配下のトラッカープロセスが 1行ファイル `seq,channel`（LiDAR 版は `seq,channel,shaving`）を
  temp→`os.replace` の**原子的 rename** で ~20–50ms ごとに書く。
- Unity 側 `VisionInputSource` が ~50ms 間隔でその最終行をポーリングし、`VisionInputProtocol` が
  watchdog（`seq` が停滞したらロスト判定 → `-1` フェイルセーフ）・ライブ/ラッチ・デバウンスを担う。
- パス解決順（旧経路 `VisionInputSource`）: 環境変数 `KAKIGORI_POSITION_FILE` → 隣接 `config.json` の `positionFile` →
  Editor は `<projectRoot>/tools/position-tracker/out/position.txt` ／ ビルドは `persistentDataPath/position.txt`。
- **展示本番（`InputMode.LidarPosition`・`LidarPositionInputSource`）が読むファイルは `LidarPreviewConfig.asset` の
  「使用する LiDAR トラッカー」（`Tracker`）で選ぶ**（ADR-33 / Issue #83）:
  `LidarTracker`＝`tools/lidar-tracker/out/position.txt`、`LidarTracker3`＝`tools/lidar-tracker3/out/position.txt`、
  `LidarTracker1Plus3`＝`tools/lidar-tracker1+3/out/position.txt`（現在の設定）、`Custom`＝`Position File Relative` の手入力値。
  環境変数 `KAKIGORI_POSITION_FILE` があればそれが常に優先、ビルドは `persistentDataPath/position.txt`。
  Play 開始時に Console へ `[LidarPosition] tracker = … / file = …` が出るので、トラッカーを変えたらここで突き合わせる。

### `tools/`（Unity インポート対象外。`Assets/`・`Packages/` の外に uv/C++ で隔離）

- `tools/position-tracker/` — カメラ + 手位置検知（Python/uv）。`seq,channel` を書く（Phase 5 の初期実装）
- `tools/lidar-tracker/` — YDLIDAR T-mini Plus で落氷を検知（Python/uv）。`seq,channel,shaving` を書く。position-tracker の後継
- `tools/lidar-tracker3/` — Slamtec RPLIDAR S1 版の落氷検知（Python/uv）。判定・出力・スレッド構成は lidar-tracker と同じで、違うのはセンサーを開く層と機種差（quality 6bit・SDK ノイズ除去なし）だけ。連続座標モード（`seq,x,y,shaving`）
- `tools/lidar-tracker1+3/` — T-mini Plus x2（単体）+ RPLIDAR S1 x4（2ペアの交点法）を束ねた検知（Python/uv）。lidar-tracker と lidar-tracker3 の `lidar.py` をそのまま読み込んで継ぎ接ぎ（`sensors.py`）。S1 の混合画素（mixed pixel）対策として、ペアの「センサー位置→検出点群平均点」の直線同士の交点を実体位置とみなす（距離ではなく方位角を信用）。**S1 は 2 台揃わないと有効入力にしない**。T-mini は 1 台ずつ独立。詳細は `tools/lidar-tracker1+3/README.md`
- `tools/lidar-tracker2/`, `tools/lidar-tracker2.1/` — Hokuyo UBG-04LX-F01 の動作検証（C++）

> **複数台 LiDAR は「冗長性の確保」専用**（lidar-tracker / lidar-tracker3）。全台は同じ落氷域を
> 同一座標フレーム（同一原点・同一向き）で見て、取りこぼし低減のために OR を取る。
> **別範囲を分担して検知エリアを広げる構成は取らない**（台ごとの外部標定・座標フュージョンは実装しない）。
> ユーザーが「複数台使いたい」と言ったら、常に「冗長性を上げたい」の意味で解釈すること。
> 貸与上限は 6 台。可視化は全台を束ねた単一の点群プロット（`valley_tracker.py` の `MergedView`）。

## 雪崩の当たり判定モード（`LidarPreviewConfigSO.HitMode`・ADR-32）

雪崩の「敵が倒れる判定」は 2 方式あり、**Inspector で切り替える**（コード変更不要・Play 中でも即反映）。

1. Project ウィンドウで `Assets/ScriptableObjects/LidarPreviewConfig.asset` を選ぶ
2. Inspector の見出し **「雪崩の当たり判定の方式（ADR-32）」** → **Hit Mode** を選ぶ
   - **Particle Contact**（既定）… 雪の粒が敵に触れたら粒は吸収され 1 ダメージ。触れた敵はすべて倒れる（クールダウンなし）。
     静止線より外（敵の上）に落としても雪崩は起きない
   - **Legacy** … 2026-09-18 の `main`（`0d047c8`）時点の挙動そのまま。見えない判定点が落氷点から 4 u/s で外へ歩き、
     撃破半径内の前列 1 体だけを倒す（前列ルール・d 連動クールダウン・1 雪崩 1 撃）
3. モードごとに効く調整値が違う：

| モード | 効く調整値 |
|---|---|
| Particle Contact | `LidarPreviewConfig`：`Enemy Body Width Units`（敵の体幅・実測値）／`Contact Height Units`。各 `EnemyTypeSO`（`BasicEnemy` / `BombTrooper` / `ShieldTrooper` …）：`Particle Hit Points`（粒何個で倒れるか） |
| Legacy | `LidarPreviewConfig`：`Kill Radius Units`／`Overlap Radius Units`／`Cooldown Center/Edge Sec`／`End Radius`。`StageConfig`：`Shield Hits` |

> 両モードで耐久の単位が違う（Legacy＝雪崩の個数、Particle Contact＝粒の個数）ので、値は共有しない。
> `.asset` の YAML を直接編集しない（Editor で変更する）。詳細な設計判断は `docs/architecture.md` ADR-32。

## 湧きと砦 HP の調整値（`StageConfig.asset`／`TH_Stage.prefab`・ADR-36）

湧きは既定で **Population 方式**（盤面の敵数を目標人数に保ち、倒した分だけ補充）。「湧いた分しか倒せない」旧方式（FixedRate）では
クリアタイムの下限が 目標撃破数÷湧きレート に固定され、腕前がタイムに出なかった（ボット計測 1体/s→43s・3体/s→44s → 変更後 47s・18s）。
`Assets/ScriptableObjects/StageConfig.asset` の Inspector で調整する:

- **Spawn Mode**（既定 Population）… `FixedRate` にすると ADR-30 の一定間隔湧き（`Spawn Rate Per Second`）に切り戻せる
- **Field Population Start / End**（既定 8 / 16）… 開始時／目標到達時に盤面へ維持する敵数（進捗で線形補間）。多いほど砦への圧が強く、雪崩 1 回の巻き込みも増える
- **Refill Rate Per Second**（既定 6）… 補充の上限レート。腕前の上限より十分速くしておく（遅いと再び湧き律速になる）
- **Fort Hp Max**（`TH_Stage.prefab` の `GameManager`・既定 2500）… 盤面人数が一定なので被弾は「プレイ時間 × 人数」に比例する。
  どれだけ遅い人を敗北にするかはここで決める（目安: 1体/s のボットで 53 秒クリア・HP 34% 残し、放置は 107 秒で敗北。放置を早く終わらせたければ下げる）

クリアタイムはリザルト／ランキングで **0.1 秒単位** に表示する（`GameManager` の **Show Clear Time Tenths**・既定 ON）。

**ADR-36 の作業前（ADR-35 まで）の挙動に戻す**（コード変更不要・3 箇所）:
`StageConfig.asset` の **Spawn Mode = FixedRate**（`Spawn Rate Per Second` は作業前の 2 のまま）／
`TH_Stage.prefab` の `GameManager` で **Fort Hp Max = 1000**・**Show Clear Time Tenths = OFF**。

## コンボ・ビーム砲の調整値（`StageConfig.asset`・ADR-30/35）

`Assets/ScriptableObjects/StageConfig.asset` の Inspector で調整する（コード変更不要）:

- **Combo Window Sec**（既定 1.5）… この秒数以内に次を倒せばコンボ継続
- **Beam Combo Grace Sec**（既定 0.5）… ビーム砲が敵を倒した瞬間から進行中コンボが切れるまでの猶予。ビーム撃破はコンボに加算しない。
  0 にすれば衝撃波の直後にほぼ即断ち切る
- **Gauge Max**（既定 60）… 大きいほどビーム砲の発動頻度が下がる（`Gauge Per Kill`／`Gauge Combo Bonus` と合わせて逆算）

## リポジトリ構成

```
Assets/Scripts/
  Core/       純C#ロジック（UnityEngine 非依存 = KakigoriTD.Core.asmdef, noEngineReferences）
              GameStateMachine + State群（Title / Battle / Result）, KillProgress, ComboTracker,
              SpecialGauge, ExplosionChain, KillSource, StageResult,
              AvalancheClock, SpawnClock/PopulationSpawnClock/SpawnSelector, IGameContext,
              ScoreEntry/ScoreRanking/IScoreStore/HoldToConfirm（クリアタイムのランキング・ADR-34）… EditMode テスト対象
  Data/       ScriptableObject 定義（StageConfigSO, EnemyTypeSO, ProjectileTypeSO, SerialConfigSO,
              VisionConfigSO, LidarPreviewConfigSO, AudioConfigSO, SpecialAttackConfigSO, SpawnMode）
  Input/      IInputSource と各実装（上記「入力アーキテクチャ」参照）
  Factory/    ObjectPool<T>, ProjectileFactory
  Gameplay/   MonoBehaviour：EnemyField（360度の敵管理・隊列・撃破・爆発連鎖）, Enemy /
              BombTrooper / ShieldTrooper, Projectile, LidarAvalancheVfx + AvalancheSnowEffect（雪崩）,
              SpecialAttackController + BeamSweepVfx（ビーム砲の演出進行。発動は Core の SpecialGauge）,
              ExplosiveDeath + ExplosionTelegraph（爆発演出。判定は Core の ExplosionChain）, Fort,
              SpawnDirector, SnowPuff/DissolveBurst 等
  Audio/      AudioService, AudioConfigSO（Phase 6・表現層のみ・Core 無改修）
  UI/         UIManager（IMGUI ベースの HUD / バナー / 警告）, GameResultHolder（シーン間の結果・ランキング受け渡し）
  Persistence/ JsonFileScoreStore（ランキングのローカル DB ファイル＝JSON・原子的更新・破損時は退避。ADR-34）
  Bootstrap/  GameManager（唯一の Singleton / Composition Root）
  Scenes/     TitleScene / ResultScene / GameOverScene（KakigoriTD.Scenes）— `Enter` でシーン遷移する小さな MonoBehaviour,
              RankingResetHotkey（`D` 5 秒長押しでランキング消去。Title / Result が生成する plain class）
Assets/Tests/EditMode/     純ロジックの単体テスト（KakigoriTD.Tests.EditMode.asmdef）
Assets/ScriptableObjects/  StageConfig / SpecialAttackConfig / BasicEnemy / AssaultEnemy / BombTrooper /
                           ShieldTrooper / BasicProjectile / AudioConfig / VisionConfig / LidarPreviewConfig /
                           *SerialConfig の実体
Assets/Prefabs/TH_Stage.prefab   ゲーム本体の一式（GameManager 等）。TH.unity が配置
Assets/Prefabs/EnemyBombTrooper.prefab / EnemyShieldTrooper.prefab   爆発兵・盾兵のモデル（爆発兵は ExplosiveDeath 付き）
Assets/Prefabs/BeamCannon.prefab / SpecialAttackShockwave.prefab / ExplosionTelegraph.prefab   ビーム砲・衝撃波・爆発の演出
Assets/snow_effect.prefab        雪崩のパーティクル
docs/                      仕様・ADR・フェーズ計画
tools/                     位置検知トラッカー（Python/C++・Unity 対象外）
```

- asmdef は 3 つ: `KakigoriTD.Core`（`noEngineReferences:true`）/ `KakigoriTD.Runtime`（Core 以外の全ランタイム）/ `KakigoriTD.Tests.EditMode`。
- 名前空間は `KakigoriTD.<フォルダ名>`（`KakigoriTD.Core`, `KakigoriTD.Gameplay`, `KakigoriTD.Audio`, `KakigoriTD.Scenes` …）。1ファイル1クラス、ファイル名＝クラス名。

## 鉄則（詳細は `docs/coding-conventions.md` / `docs/architecture.md`）

- **Singleton は `GameManager` 1個のみ**。他は GameManager で生成・配線する（Composition Root、ADR-5）。
  実行時の `GameObject.Find` / `FindObjectOfType` 禁止。参照は配線か `[SerializeField]`。
- `public` フィールドを作らない。Inspector 公開は `[SerializeField] private` + property。
- 入力は `UnityEngine.Input.*` を直接撒かない。参照は `KeyboardInputSource` 内に限定、他は `IInputSource` 経由（ADR-6/10）。
- 調整値・ウェーブ設定・敵/弾/音/入力パラメータは **ScriptableObject** か `[SerializeField]` 定数に置く（マジックナンバー直書き禁止、ADR-1）。
- 敵・弾・パーティクルは **ObjectPool** で使い回す（ホットパスで `Instantiate/Destroy` しない、ADR-4）。
- システム間通信は **C# event**（`event Action<...>` / `OnX?.Invoke(...)`）。**購読は `OnDestroy` で必ず解除**（ADR-3、SOイベントチャネル移行は見送り中）。
- **純ロジックは Core（UnityEngine 非依存）に分離**し、対応する EditMode テストを書く（ADR-8）。MonoBehaviour は入出力アダプタに徹する。
- 演出・音・3D 表現の追加は **Core ロジックを改変しない**方針（ADR-20/21/25/26）。座標系は XZ+高さ、物理（Collider/Rigidbody）は不使用。
- 視覚・実行時の確認は **Unity Editor 再生 + スクリーンショット/ログ**で客観的に。

## ドキュメントの鮮度（重要）

- `docs/phases.md` / `docs/architecture.md` の **Phase 1〜6・ADR-1〜27（〜2026-07 中旬）** は受け入れ基準・検証記録つきで信頼できる。
- **Phase 7（`docs/phases.md`）と ADR-28（`docs/architecture.md`）は git 履歴からの遡及要約**。2026-08 に `main` へ直接マージされた作業
  （3Dアート・撃破演出・雪崩シェーダー2種・Title/Result シーン分割＋`Enter` 遷移・LiDAR 落氷検知・`com.unity.pipeline` 導入）を
  カバーするが、受け入れ基準・Play 検証・領域別 ADR は**未整備**。該当ブランチ（`4-` `5-` `6-` `7-` `11-演出雪崩表現の改善` `12-LiDARとUnityの連携` `37-` `scene_connect`）を正とする。
- 設計判断を変えたら `docs/architecture.md` に ADR を追記し、影響する `docs/` を更新する。Phase 7 まわりを触ったら、要約だった箇所を実装ベースに書き直していくのが望ましい。

## 現在の状況

Phase 1〜6 + Extra 1 完了。**Phase 7（展示ビルド化）進行中**、および
**Phase 8（ゲーム性刷新：単一ステージの目標撃破数レース・ADR-30）実装済み。`main` の演出（爆発・ビーム砲・雪崩）を ADR-31 で接続し、Editor で EditMode 192 本緑・Play 検証済み**。
新しい作業を始めるときは、`git branch -a` の作業ブランチ名と `docs/phases.md` Phase 7 の「未整備」チェックリストを突き合わせて、いま何が動いているかを確認すること。
