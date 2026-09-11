# かき氷タワーディフェンス (Kakigori TD)

雪山の神となり、頂上からかき氷を削って雪崩を起こし、麓4方向から砦を遠隔攻撃する敵を殲滅レースで退ける、**体験型タワーディフェンス**。

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
- テスト: Unity Test Framework（**EditMode を主**とする。`Assets/Tests/EditMode/`、15ファイル・151本）

## 実行とテスト

### シーン構成（Build Settings 登録順）

1. `Assets/Scenes/TitleScene.unity` — タイトル。`Enter` で `TH` へ（`Assets/Scripts/Scenes/TitleScene.cs`、フェード付き）
2. `Assets/Scenes/TH.unity` — **ゲーム本体**。`Assets/Prefabs/TH_Stage.prefab` を配置しており、そこに
   `GameManager`（唯一の Composition Root / Singleton）・Terrain（雪山）・城・敵/味方プレハブ・各種 SO 参照が入っている
3. `Assets/Scenes/ResultScene.unity` — リザルト。`Enter` で `TitleScene` へ（`Assets/Scripts/Scenes/ResultScene.cs`）

`GameManager` は勝敗確定後 `_resultLimit` 秒待って `SceneManager.LoadScene("ResultScene")` へ遷移する。

**Build 対象外の開発用シーン**（再生して個別検証に使う）:
- `Assets/Scenes/SampleScene.unity` — Phase 5/6 期の旧セットアップ（GameManager をシーンに直置き）。TH_Stage プレハブ化前の名残
- `Assets/Scenes/nadare_test.unity` — 雪崩演出の検証
- `Assets/Scenes/character_test.unity` — アクターのモデル/アニメ検証

### テスト方針

- 純ロジック（`Assets/Scripts/Core/` 配下）は **EditMode テスト**を必須とする（`Assets/Tests/EditMode/`、現在151本）。
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
- パス解決順: 環境変数 `KAKIGORI_POSITION_FILE` → 隣接 `config.json` の `positionFile` →
  Editor は `<projectRoot>/tools/position-tracker/out/position.txt` ／ ビルドは `persistentDataPath/position.txt`。

### `tools/`（Unity インポート対象外。`Assets/`・`Packages/` の外に uv/C++ で隔離）

- `tools/position-tracker/` — カメラ + 手位置検知（Python/uv）。`seq,channel` を書く（Phase 5 の初期実装）
- `tools/lidar-tracker/` — YDLIDAR T-mini Plus で落氷を検知（Python/uv）。`seq,channel,shaving` を書く。position-tracker の後継
- `tools/lidar-tracker3/` — Slamtec RPLIDAR S1 版の落氷検知（Python/uv）。判定・出力・スレッド構成は lidar-tracker と同じで、違うのはセンサーを開く層と機種差（quality 6bit・SDK ノイズ除去なし）だけ。連続座標モード（`seq,x,y,shaving`）
- `tools/lidar-tracker2/`, `tools/lidar-tracker2.1/` — Hokuyo UBG-04LX-F01 の動作検証（C++）

> **複数台 LiDAR は「冗長性の確保」専用**（lidar-tracker / lidar-tracker3）。全台は同じ落氷域を
> 同一座標フレーム（同一原点・同一向き）で見て、取りこぼし低減のために OR を取る。
> **別範囲を分担して検知エリアを広げる構成は取らない**（台ごとの外部標定・座標フュージョンは実装しない）。
> ユーザーが「複数台使いたい」と言ったら、常に「冗長性を上げたい」の意味で解釈すること。
> 貸与上限は 6 台。可視化は全台を束ねた単一の点群プロット（`valley_tracker.py` の `MergedView`）。

## リポジトリ構成

```
Assets/Scripts/
  Core/       純C#ロジック（UnityEngine 非依存 = KakigoriTD.Core.asmdef, noEngineReferences）
              GameStateMachine + State群, EnemyMatrix<T>, IceStock, Scoring, PenaltyTally,
              AvalancheClock, SpawnClock/SpawnSelector, IGameContext … EditMode テスト対象
  Data/       ScriptableObject 定義（WaveConfigSO, EnemyTypeSO, ProjectileTypeSO, SerialConfigSO,
              VisionConfigSO, AudioConfigSO）
  Input/      IInputSource と各実装（上記「入力アーキテクチャ」参照）
  Factory/    ObjectPool<T>, ProjectileFactory
  Gameplay/   MonoBehaviour：ChannelController, Enemy / AllyNPC / DisguisedEnemy, Projectile,
              AvalancheController, AvalancheVfx, Fort, WaveManager, SnowPuff/Snowball/DissolveBurst 等
  Audio/      AudioService, AudioConfigSO（Phase 6・表現層のみ・Core 無改修）
  UI/         UIManager（IMGUI ベースの HUD / バナー / 警告）
  Bootstrap/  GameManager（唯一の Singleton / Composition Root）
  Scenes/     TitleScene / ResultScene（KakigoriTD.Scenes）— `Enter` でシーン遷移する小さな MonoBehaviour
Assets/Tests/EditMode/     純ロジックの単体テスト（KakigoriTD.Tests.EditMode.asmdef）
Assets/ScriptableObjects/  Wave1-3 / BasicEnemy / AssaultEnemy / AllyNPC / DisguisedEnemy /
                           BasicProjectile / AudioConfig / VisionConfig / *SerialConfig の実体
Assets/Prefabs/TH_Stage.prefab   ゲーム本体の一式（GameManager 等）。TH.unity が配置
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

Phase 1〜6 + Extra 1 完了。**Phase 7（展示ビルド化：3Dアート・マルチシーン・LiDAR入力・現場調整）進行中**。
新しい作業を始めるときは、`git branch -a` の作業ブランチ名と `docs/phases.md` Phase 7 の「未整備」チェックリストを突き合わせて、いま何が動いているかを確認すること。
