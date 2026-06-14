# Unity-MCP 活用ガイド

このドキュメントは、**かき氷タワーディフェンス**プロジェクトにおける Unity-MCP の活用方法をまとめたものです。

Unity-MCP は単なる「便利ツール」ではなく、**このプロジェクトの開発プロセスにおける重要な検証レイヤー**として位置づけています。

## Dynamic Workflows機能との関係（重要）

このプロジェクトでは **Claude Code の Dynamic Workflows機能** と組み合わせることを想定しているが、**Unity環境特有の制約**があるため、以下を厳守すること。

### 基本原則
- **Dynamic Workflows機能** でサブエージェントを並列実行する場合でも、**Unity Editor操作はメインセッションで直列に集約**する。
- サブエージェントには主に「コード生成」と「ロジック実装」を任せ、以下の操作はメインセッションが行う：
  - `assets-refresh`
  - `tests-run`（EditMode）
  - `screenshot-game-view`
  - Play Mode の起動・操作
  - シーンへのオブジェクト配置・検証

この運用により、Unity Editorが単一リソースであることによる競合リスクを最小限に抑える。

## 1. 基本的な開発ループ

このプロジェクトでは、以下のループを標準とします。

```
1. タスクの理解と計画（仕様書・ADR・受け入れ基準を確認）
   ↓
2. 実装（Dynamic Workflows機能 または通常プロンプトでコード生成）
   ↓
3. Unity-MCP で Editor に反映・配置（メインセッション）
   ↓
4. Play Mode 起動 + screenshot-game-view で視覚・動作検証（メインセッション）
   ↓
5. 問題があれば即修正 → 4 に戻る
   ↓
6. 受け入れ基準をすべて満たしたら完了報告
```

**重要な考え方**:
- 「動いた気がする」ではなく、**スクリーンショットやログで客観的に確認**してから次に進む。
- Dynamic Workflows機能を使用する場合も、上記の検証ステップはメインセッションが責任を持って行う。

## 2. このプロジェクトでよく使う MCP ツール

| カテゴリ | ツール例 | 主な用途（本プロジェクト） |
|----------|----------|---------------------------|
| **オブジェクト操作** | `gameobject-create`, `gameobject-component-add`, `gameobject-component-modify` | ChannelController、Enemy、Projectile の配置・設定 |
| **検索・取得** | `gameobject-find`, `gameobject-component-get` | 特定オブジェクトの状態確認 |
| **シーン管理** | `scene-save`, `gameobject-set-parent` | テストシーンの整理 |
| **視覚検証** | `screenshot-game-view`, `screenshot-scene-view` | 雪崩・敵・UI の動作確認（最重要） |
| **実行時操作** | Play Mode 関連、Console ログ取得 | Wave 進行、HP 減少、氷消費の確認 |
| **アセット** | `assets-find`, `assets-prefab-create` | ScriptableObject やテスト用 Prefab の確認 |
| **デバッグ** | `reflection-method-call` | 特定メソッドの実行や状態確認 |

## 3. 具体的な活用パターン

### パターンA: コアメカニクス検証（Phase 1 で最も頻出）
**対象**: `AvalancheController`, `EnemyMatrix`, `ChannelController`

**手順例**:
1. 実装完了後、Unity-MCP でテストシーンに `ChannelController` + `Enemy` を配置
2. Play Mode を起動
3. `Space` 長押しで雪崩を流す
4. `screenshot-game-view` で以下の点を確認:
   - 先頭行が `killInterval` ごとに消えていく
   - 削りを止めると進行が止まる（深さ制御）
   - 削り続けると奥の敵まで到達する
5. 問題があれば即修正

**Dynamic Workflows機能使用時**: 複数の `ChannelController` を並列で実装する場合に有効。ただし検証はメインセッションで直列に行う。

### パターンB: 資源・UI 検証
**対象**: `IceStock`, `Fort`, `UIManager`

**確認ポイント**:
- 氷ストックが `iceDrainPerSec` で正しく減少
- 0 になったら削れなくなる
- 砦HP が着弾ごとに減少し、UI に反映される
- `screenshot-game-view` でバーと数値の同期を確認

### パターンC: Wave進行・状態遷移検証
**対象**: `WaveManager`, `GameStateMachine`, `WavePlayingState`

**確認ポイント**:
- `WaveConfigSO.duration` 経過で Wave が終了し、残敵が消える
- 次の Wave に正しく遷移する
- 3 Wave 終了で Result(Win) になる
- 砦HP が 0 になったら即 Result(Lose) になる

### パターンD: オブジェクトプール検証
**対象**: `ProjectileFactory`, `ObjectPool`

**確認ポイント**:
- 弾が頻繁に生成・破棄されても GC スパイクが発生しない
- 着弾時にプールへ正しく返却されている

**Dynamic Workflows機能使用時**: Unit 7（ObjectPool）の実装と検証を効率化できるが、Play ModeでのGC確認はメインセッションで行う。

## 4. Unity-MCP 使用時のルール

### 必須ルール
- **視覚的・実行時検証は MCP を優先**する。
- **小さく分割して検証**する。
- **スクリーンショットを証拠として残す**。
- **Dynamic Workflows機能使用時**は特に以下を厳守：
  - サブエージェントに Editor 操作を直接行わせない。
  - 並列化する範囲をプロンプトで明確に指定する。
  - 検証フェーズはメインセッションで直列に行う。

## 5. トラブルシューティング

- **MCP の応答が遅い・不安定**: 一度に大量の操作をせず、小さな単位で確認する。
- **視覚的に問題があるのに原因がわからない**: まず `screenshot-game-view` を複数枚撮り、どこが違うかを具体的に指摘する。
- **Play Mode でしか再現しないバグ**: MCP で Play Mode を起動した状態でデバッグを進める。
- **オブジェクトが見つからない**: `gameobject-find` を先に使って存在を確認してから操作する。

## 6. まとめ

- Unity-MCP は「コードを書くためのツール」ではなく、**「作ったものが正しく動くかを素早く確かめるためのツール」** として活用する。
- Dynamic Workflows機能を使用する場合も、**Unity Editor操作はメインで直列管理**するという運用を徹底する。
- 並列化のメリットを活かしつつ、Unity環境の制約を正しく理解して使うことが重要である。

---

**更新履歴**
- 2026-06-14: Dynamic Workflows機能との関係セクションを新規追加。サブエージェント運用ルールを明確化。セクション3「具体的な活用パターン」を追記。セクション5「効果的なプロンプト例」を CLAUDE.md に移動。