# コーディング規約 (Coding Conventions)

C# / Unity 標準に、本プロジェクト固有のルールを足したもの。`docs/architecture.md` の決定に従うこと。

## 命名
- 型・メソッド・public メンバ・定数: `PascalCase`。
- ローカル変数・引数: `camelCase`。
- private フィールド: `_camelCase`。
- Inspector 公開する private フィールドは `[SerializeField] private` を使い、**public フィールドは作らない**（参照は property で公開）。
- ScriptableObject 型は接尾辞 `SO`（例 `WaveConfigSO`）。インターフェースは接頭辞 `I`（例 `IInputSource`）。
- ブール: `is/has/can` で始める（`IsShaving`, `isAlly`）。

## ファイル・名前空間
- **1ファイル1クラス**、ファイル名＝型名。
- 名前空間は `KakigoriTD.<フォルダ名>`（`KakigoriTD.Core`, `KakigoriTD.Gameplay` 等）。
- フォルダ配置は CLAUDE.md「リポジトリ構成」に従う。Core には UnityEngine 依存を持ち込みすぎない（テスト容易性のため、ADR-8）。

## Unity 固有
- ホットパス（毎フレーム・大量生成）で `Instantiate`/`Destroy` を呼ばない。敵・弾は `ObjectPool` 経由（ADR-4）。
- `GameObject.Find` / `FindObjectOfType` を実行時に使わない。参照は GameManager から配線するか `[SerializeField]` で渡す（ADR-5）。
- 時間依存は `Time.deltaTime` を使う。フレームレート依存の加算をしない。
- `Update()` には「毎フレーム必要な処理」だけ。状態変化の通知はイベントで行う（ポーリングしない）。
- マジックナンバーを直書きしない。調整値は ScriptableObject か `[SerializeField]` 定数に（ADR-1）。

## 入力
- `UnityEngine.Input.*` の直接参照は `KeyboardInputSource` の中だけ。他は `IInputSource` を介す（ADR-6）。

## イベント（通信）
- C# event は `event Action<...>`。発火は null 条件演算子で `OnX?.Invoke(...)`。
- **購読したら `OnDestroy`（または明示的 Dispose）で必ず解除**。購読箇所は少数に集中させる（移行を局所化、ADR-3）。
- 将来 SOイベントチャネルへ移行する前提で、publisher/subscriber を疎に保つ。

## State / ロジック
- 状態は `IGameState` 実装クラスで表現し、`GameStateMachine.Change()` で遷移（ADR-2）。
- 判定・計算・状態は Core の純 C# に置き、MonoBehaviour は入出力アダプタに徹する（ADR-8）。

## テスト
- Core の各ロジックに **EditMode テスト**を用意（`Assets/Tests/EditMode/`）。
- テストは Arrange-Act-Assert。1テスト1観点。命名は `Method_条件_期待結果`（例 `RemoveFrontRow_WhenAllyInRow_DoesNotKillEnemiesBehind`）。
- 受け入れ基準（`docs/phases.md`）に対応するテストを書き、報告前に緑であることを確認する。

## コメント / ドキュメント
- 「なぜ」を書く。「何を」はコードで表す。
- public API には簡潔な XML doc（`/// <summary>`）。
- 設計判断を変えたら `docs/architecture.md` に ADR を追記し、影響する `docs/` を更新する。

## コミット（任意・推奨）
- 1コミット1論理変更。先頭に対象を示す接頭辞（例 `core:`, `gameplay:`, `data:`, `docs:`）。
