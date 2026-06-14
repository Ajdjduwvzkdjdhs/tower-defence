# かき氷タワーディフェンス (Kakigori TD)

雪山の神となり、頂上からかき氷を削って雪崩を起こし、麓4方向から砦を遠隔攻撃する敵を殲滅レースで退ける、体験型タワーディフェンス。
このリポジトリは **Unity単体・キーボード入力で動く簡易プロトタイプ**（物理かき氷機インターフェースは未接続、後で `IInputSource` 実装の差し替えで対応）。

## 最初に読む（このプロジェクトの ground truth）

作業開始前に必ず該当箇所を読むこと。サブエージェントもこれらを正とする。
- `docs/かき氷TD_プロトタイプ仕様書.md` — プロトタイプ仕様書 v2（オリジナル仕様。**何のゲームを**作るかの一次情報・詳細）
- `docs/spec-summary.md` — 仕様の要約（**何を**作るか）
- `docs/architecture.md` — アーキテクチャ決定（**どう**作るか／採用パターンとADR）
- `docs/coding-conventions.md` — コーディング規約
- `docs/phases.md` — フェーズ毎のゴールと**受け入れ基準（＝完了判定の bar）**
- `docs/mcp-guide.md` — **Unity-MCP の活用方法**（開発時の検証フロー）

特に **実装 → 検証 → 修正** のサイクルに入る前に `docs/mcp-guide.md` を読むこと。

## 開発時の必須ドキュメント

このプロジェクトでは、作業の性質によって読むべきドキュメントが変わります。

| 作業内容 | 必ず読むべきドキュメント |
|----------|--------------------------|
| 実装前・計画時 | `docs/spec-summary.md`, `docs/architecture.md`, `docs/phases.md` |
| Unity-MCP を使って検証する時 | `docs/mcp-guide.md`（最優先） |
| コーディング時 | `docs/coding-conventions.md` |
| 設計判断を変更する時 | `docs/architecture.md`（ADR追記必須） |

## 技術スタック

- エンジン: Unity **6000.3.16f1**（Unity 6.3 / URP 17.3）
- 言語: C#
- テスト: Unity Test Framework（**EditMode を主**とする）

## 実行とテスト

- **実行**: Unity Editor でメインシーンを再生。
- **テスト**:
  - 純ロジック（`Core/` 配下）は **EditModeテスト** を優先。
  - Unity固有の動作（シーン操作・Play Mode中の挙動・UI表示）は **Unity-MCP** を通じて検証する。
- **Unity-MCPの活用（開発時の標準フロー）**:
  - Dynamic Workflowsでコードを実装した後は、Unity-MCPツールを使って以下を検証する：
    1. コードをシーンに反映（GameObject/Component追加・修正）
    2. Play Mode起動
    3. `screenshot-game-view` で現在の状態を視覚確認
    4. 問題があれば即座に修正ループ
  - 可能な限り「コードを書く → MCPで即時検証」のサイクルを回す。

## Unity-MCP Integration（開発ツールとして）

このプロジェクトでは **Unity-MCP** を開発時の主要な操作インターフェースとして使用する。

- **目的**: Claude CodeがUnity Editorを直接操作し、視覚的な検証を高速に行うため。
- **主な活用シーン**:
  - 実装後の即時検証（特にPhase 1の雪崩・敵挙動・UI）
  - Play Mode中の状態確認（`screenshot-game-view`）
  - デバッグ時のオブジェクト状態確認・修正
- **Dynamic Workflowsとの使い分け**:
  - **Dynamic Workflows**: 大規模なコード生成・並列実装・構造化されたタスク
  - **Unity-MCP**: 生成したコードを実際にUnity上で動かして検証する実行レイヤー
- **ルール**:
  - ビジュアルや実行時挙動の確認は、可能な限りUnity-MCPのツールを使う。
  - 「動いた気がする」ではなく、スクリーンショットやログで客観的に確認してから次に進む。

## リポジトリ構成

```
Assets/Scripts/
  Core/       純C#ロジック（GameStateMachine, EnemyMatrix, IceStock, Scoring）… テスト対象
  Input/      IInputSource, KeyboardInputSource
  Data/       ScriptableObject 定義（WaveConfigSO, EnemyTypeSO, ProjectileTypeSO, *EventChannelSO）
  Gameplay/   MonoBehaviour（ChannelController, Enemy, Projectile, AvalancheController, Fort, WaveManager）
  Factory/    ObjectPool, ProjectileFactory
  UI/         UIManager
  Bootstrap/  GameManager（唯一の Composition Root / Singleton）
Assets/Tests/EditMode/    純ロジックの単体テスト
Assets/ScriptableObjects/ Wave/敵/弾アセットの実体
docs/                     本ドキュメント群（Claude Code が随時更新）
```

名前空間は `KakigoriTD.<フォルダ名>`（例: `KakigoriTD.Core`）。1ファイル1クラス、ファイル名＝クラス名。

## 鉄則（詳細は coding-conventions.md / architecture.md）

- **Singleton は `GameManager` 1個のみ**。他は GameManager で生成・配線する（Composition Root）。
- 入力は `Input.*` を直接撒かない。`KeyboardInputSource` 以外は `IInputSource` 経由で読む。
- 調整値・ウェーブ設定・敵/弾パラメータは **ScriptableObject** に置く（マジックナンバー直書き禁止）。
- 敵・弾は **ObjectPool** で使い回す（ホットパスで `Instantiate/Destroy` しない）。
- システム間通信は **C# event**（移行先＝SOイベントチャネル、移行条件は architecture.md ADR-3）。購読は `OnDestroy` で必ず解除。
- **純ロジックは MonoBehaviour から分離**し、対応する EditMode テストを書く。
- **視覚的・実行時検証は Unity-MCP を優先**する（`docs/mcp-guide.md` 参照）。

## ドキュメント運用（Dynamic Workflows 前提）

- `docs/phases.md` の**受け入れ基準を完了判定の bar** とする。実装後はこの基準＋EditMode テスト＋Unity-MCP検証で確認してから報告すること。
- 進捗は `docs/phases.md` のチェックリストを更新。
- 設計判断を変更/追加したら `docs/architecture.md` に ADR を追記。
- 作業は**現在フェーズのスコープに限定**する（フェーズ外の機能を先取りしない）。

## 現在のフェーズ

**Phase 1: Wave1 の核（味方なし）**。スコープと受け入れ基準は `docs/phases.md` を参照。
