# アーキテクチャ決定事項 (Architecture / ADRs)

採用パターンの一覧と、各決定の背景・結果。設計判断を変える/追加するときは、末尾に ADR を追記すること（既存 ADR は原則 immutable、覆す場合は新 ADR で Supersede）。

## パターン適用マップ

| 領域 | 採用 | 参照 ADR |
|------|------|----------|
| データ管理 | ScriptableObject（WaveConfigSO / EnemyTypeSO / ProjectileTypeSO） | ADR-1 |
| ゲーム進行 | State パターン（IGameState + GameStateMachine） | ADR-2 |
| システム通信 | C# event（初期）→ SOイベントチャネル（移行） | ADR-3 |
| 生成 | Object Pool + Factory | ADR-4 |
| 依存配線 | 単一 Composition Root（GameManager のみ Singleton） | ADR-5 |
| 入力 | Strategy（IInputSource） | ADR-6 |
| 遠隔攻撃 | プロジェクタイル（pooled）／攻撃方式 Strategy は将来用に予約 | ADR-7 |
| テスト容易性 | 純ロジックを MonoBehaviour から分離 | ADR-8 |

---

## ADR-1: データ管理は ScriptableObject — Accepted

- **背景**: ウェーブ毎の制限時間や敵/弾パラメータを Inspector で調整したい。ウェーブ・敵種の追加が見込まれる。
- **決定**: 調整可能データは `WaveConfigSO`・`EnemyTypeSO`・`ProjectileTypeSO` 等の ScriptableObject アセットに置く。コードにマジックナンバーを直書きしない。
- **結果**: デザイナ調整・拡張が容易。実体アセットは `Assets/ScriptableObjects/` に置く。

## ADR-2: ゲーム進行は State パターン — Accepted

- **背景**: Title→各Wave→Result の流れ。割込み（敗北）や分岐があり、enum+switch は肥大しやすい。
- **決定**: `IGameState{Enter/Tick/Exit}` と `GameStateMachine` を用意し、状態ごとにクラス化（`TitleState`/`WaveIntroState`/`WavePlayingState`/`WaveLullState`/`ResultState`）。
- **結果**: 状態追加が局所化。コルーチン逐次案は割込み/分岐に弱いため不採用。

## ADR-3: 通信は C# event で開始し、条件成立で SOイベントチャネルへ移行 — Accepted

- **背景**: プロト速度を優先しつつ、将来の疎結合（シーン非依存）も確保したい。
- **決定**: 初期は C# event（例 `Fort.OnHpChanged`, `OnDestroyed`）。購読は `OnDestroy` で必ず解除し、購読箇所は少数に集中（UI はまとめて購読）して移行を局所化する。
- **移行トリガー**（いずれか成立で `*EventChannelSO` へ移行）:
  1. プレハブ（弾・敵）が**シーン参照なしで**イベントを発火したい。
  2. クロスシーン通信が必要。
  3. 相互参照するシステムが概ね5つを超える。
- **結果**: 早く作れる。移行時は publisher が `IntEventChannelSO.Raise()` を呼び、subscriber が同アセットを購読する形に置換。

## ADR-4: 生成は Object Pool + Factory — Accepted

- **背景**: 弾は高頻度で生成/破棄され、GC スパイクで処理落ちしうる。
- **決定**: `ObjectPool<T>` で敵・弾を使い回す（ホットパスで Instantiate/Destroy 禁止）。弾の種別生成は `ProjectileFactory`（`ProjectileTypeSO` を受け取り、着弾でプール返却）。
- **結果**: GC 負荷低減。貫通弾・拡散弾は SO 追加＋Factory 分岐で既存コード非変更で拡張。

## ADR-5: 単一 Composition Root — Accepted

- **背景**: Singleton 乱用は密結合・隠れ依存・テスト困難を招く。
- **決定**: Singleton は `GameManager` 1個のみ。GameManager が各システム（IceStock, ChannelController×4, WaveManager, Fort, Factory/Pool, UI, InputSource）を生成・参照配線し、StateMachine を駆動する。
- **結果**: 依存が1箇所で見える。DIコンテナ（VContainer等）は現規模ではオーバーのため不採用（将来再検討可）。

## ADR-6: 入力は Strategy（IInputSource） — Accepted

- **背景**: 現在はキーボード、将来は物理かき氷機（画像処理＋回転センサ）に差し替える。
- **決定**: `IInputSource{ActiveChannel, IsShaving}` を介して入力を読む。`Input.*` の直接参照は `KeyboardInputSource` 内のみ。
- **結果**: 実機接続は `InterfaceInputSource : IInputSource` を作り GameManager の代入を差し替えるだけ。

## ADR-7: 遠隔攻撃はプロジェクタイル — Accepted

- **背景**: 「麓から砦への遠隔攻撃」を視覚化したい。Pool/Factory の有効性検証にも適する。
- **決定**: 各敵が `fireInterval` ごとに弾を発射、麓→山頂へ飛び着弾で `Fort.TakeDamage`。弾は pooled。
- **将来**: 「連続DPS / プロジェクタイル」を状況で切り替える要件が出たら、攻撃方式を Strategy 化して差し替える（現時点では実装しない）。
- **結果**: 連続DPS抽象案は不採用（遠隔感が視覚に出ないため）。

## ADR-8: 純ロジックを MonoBehaviour から分離 — Accepted

- **背景**: Unity 起動を要するテストは遅く、Dynamic Workflows の検証 bar に不向き。
- **決定**: 判定・状態・計算（`GameStateMachine`, `EnemyMatrix`, `IceStock`, スコア算出 等）は `Assets/Scripts/Core/` に純 C# クラスとして置き、MonoBehaviour は薄いアダプタにする。Core には EditMode テストを書く。
- **結果**: 高速なEditModeテストが可能。受け入れ基準の自動検証に使う。  
  Unity-MCPは実行時・視覚的な検証を補完する開発ツールとして活用する（純ロジック部分はEditModeテストを優先）。

## ADR-9: State は IGameContext 境界越しに外界へ作用 — Accepted

- **背景**: ADR-2 の State 群は進行制御のため、ウェーブ開始・残敵消去・砦HP参照・リザルト表示など Gameplay 層へ作用する必要がある。一方 ADR-8 に従い State は UnityEngine 非依存（Core）に保ち、遷移を EditMode で高速検証したい。
- **決定**: State が外界へ作用する操作を `IGameContext`（Core）に集約し、State はこの抽象だけを参照する。具象は Composition Root の `GameManager` が実装する（後続マイルストーン）。ウェーブ番号は State 側が保持し、`IGameContext` は状態を持たない純粋な作用の窓口とする。
- **結果**: 5 つの State（Title/WaveIntro/WavePlaying/WaveLull/Result）と全遷移を stub context で EditMode 検証可能（`StatesTests` 16 本緑）。`GameManager` 実装時は同インターフェースを満たすだけで配線が完了する。本 ADR は ADR-2／ADR-8 を補強するもので、これらを覆さない。

## ADR-10: 入力は新 Input System で読む（旧 Input Manager API は不使用）— Accepted

- **背景**: 本プロジェクトの Active Input Handling は「Input System Package (New)」のみ（`activeInputHandler = 1`）。この設定下では旧 `UnityEngine.Input.GetKey/GetKeyDown`（レガシー API）は実行時に `InvalidOperationException` を投げる。仕様書 §4 の `KeyboardInputSource` スケルトンは旧 API を用いていた。
- **決定**: `KeyboardInputSource` は新 Input System（`UnityEngine.InputSystem.Keyboard.current`）で入力を読む。WASD は `wasPressedThisFrame` でラッチ（同時1本）、Space は `isPressed` で長押し判定。`UnityEngine.InputSystem` への直接参照は `KeyboardInputSource` 内に限定し、ADR-6 の「`Input.*` は実装内のみ」を新 API に読み替えて踏襲する。アセンブリ参照は `KakigoriTD.Runtime.asmdef` に `Unity.InputSystem` を追加。
- **結果**: New-only バックエンドで実行時例外なく入力取得。Play Mode で検証済み（`W→0 / A→1 / S→2 / D→3`、押下なしフレームはラッチ保持、`IsShaving` の Space 長押し切替）。ADR-6 を覆さず実装詳細を確定する補足。実機インターフェース差し替え方針（`IInputSource` 実装の差し替え）は不変。

> 補足（アセンブリ構成）: Phase1 マイルストーンB時点で、Core 以外のランタイムコード（Data/Input/Factory/Gameplay/UI/Bootstrap）は `Assets/Scripts/KakigoriTD.Runtime.asmdef`（`KakigoriTD.Core` と `Unity.InputSystem` を参照）にまとめる。Core は `KakigoriTD.Core`（`noEngineReferences:true`、ADR-8 をビルドレベルで強制）として分離を維持。EditMode テストは両者を参照。

## ADR-11: 戦闘アクター（Fort/Projectile/Enemy）の状態は MonoBehaviour 内に保持し、検証は性質で二層化 — Accepted

- **背景**: ADR-8 は「判定・計算・状態を Core の純 C# に分離」する方針。一方、戦闘アクター（`Fort` の HP、`Projectile` の飛行、`Enemy` の発射間隔）は本質的に Unity の Transform・`Time.deltaTime`・プール・C# event と密結合し、Core へ切り出しても薄い委譲が増えるだけで益が小さい。
- **決定**: `Fort`/`Projectile`/`Enemy` の単純な状態（HP の離散減算、到達判定、発射タイマ）は各 MonoBehaviour 内に保持する（Core へ別クラスを切らない）。検証は性質で二層に分ける:
  - **EditMode**: 純粋に判定できるもの（`Fort` の HP 減算・0 クランプ・`OnHpChanged`/`OnDestroyed`）は `AddComponent` でインスタンス化し EditMode テスト（FortTests 8 本）。
  - **MCP Play Mode**: 時間的・視覚的挙動（弾の麓→山頂飛行と着弾減算、敵の `fireInterval` 発射、撃破での発射停止）は Unity-MCP の Play Mode ＋ `screenshot-game-view`/ログで検証（phase1-plan のユニット完了条件どおり）。
- **補足（生成口の注入）**: `ProjectileFactory` は弾インスタンスの生成方法を `Func<Projectile>` で受け取り、内部に `ObjectPool<Projectile>` を持つ（ADR-4）。シーンやプレハブのアセット参照に依存せず、Composition Root（GameManager）やテストが生成デリゲートを注入する。これにより Factory 自体は純粋でテスト容易、`Projectile`→`Fort.TakeDamage` の着弾は ADR-7 を踏襲する。
- **結果**: ADR-8 をビルド境界（`KakigoriTD.Core` の `noEngineReferences`）で維持しつつ、戦闘アクターは EditMode 緑（Fort 8 本／累計 64 本）＋ Play Mode 実証（弾飛行・HP 1000→995、発射 1発/2s、Kill で停止）で完了判定。ADR-8 を覆さず、適用範囲（純ロジックか時間的・視覚的挙動か）を明確化する補足。
