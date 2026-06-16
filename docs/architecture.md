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
| 実機シリアル接続 | 自前 SerialPort（別スレッド・双方向）＋ Composite 入力 | ADR-15 |

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

## ADR-12: 谷の座標は湧き時固定（survivor 静止）／雪崩タイミングは純ロジックに分離 — Accepted

- **背景**: `ChannelController` は `EnemyMatrix<T>`（Core）の行を放射状ワールド座標に対応づける。`EnemyMatrix.RemoveFrontRow` は list index をずらすため、座標を index から都度計算すると先頭行消去のたびに残存敵が砦側へ滑ってしまい、「雪崩が手前→奥へ進む（敵は静止）」という仕様の手触りと食い違う。また phase1-plan §13 は雪崩タイミングを純ロジックに分離して EditMode 検証する方針。
- **決定**:
  - ~~**座標は湧き時に固定**: 各敵の depth を単調増加カウンタで割り当て、湧き時にワールド座標を確定して以後動かさない（survivor 静止）。~~ → **ADR-14 で改訂**（単調増加 depth は長時間プレイで敵が画面外へドリフトし「見えない位置から攻撃される」不具合を生んだ。座標は現在の行インデックスで決め、先頭行消去で繰り上げる方式に変更）。
  - **雪崩タイミングは純ロジック**: `AvalancheClock`（Core・UnityEngine 非依存）が delay→開始・`killInterval` ごと 1 行・停止/谷変更でリセットを担い EditMode 検証（7 本）。`AvalancheController`（Gameplay）は `IInputSource`（ADR-6）・`IceStock`・谷適用を結ぶアダプタに徹する（ADR-8/11 を踏襲）。
- **結果**: 「削る→雪崩→先頭行消去（深さ制御）」を EditMode（AvalancheClock 7／累計 71 本）＋ Play Mode（北谷 9→0・氷 50→0・停止/氷枯渇で不可）で完了判定。既知の限界: depth 単調増加のため長時間プレイで編隊が外周へドリフトしうる（Wave 管理＝マイルストーン E で上限/間引きを検討）。ADR-8/11 を補強し覆さない。

## ADR-13: GameManager 統合方針 — UI は IMGUI（プロト）／入力境界に Confirm 追加／IGameContext で結線 — Accepted

- **背景**: マイルストーン E で Composition Root（`GameManager`）・進行 UI・Wave 進行を結線する。美術アセットは未定（spec: 形は TBD）で、State 群（Core）は `IGameContext` 越しにのみ外界へ作用する（ADR-9）。Title 開始・Result 再挑戦には「決定」入力が要るが、`IInputSource` は `ActiveChannel`/`IsShaving` のみだった。
- **決定**:
  - **唯一の Singleton = `GameManager`（ADR-5）**。`Awake` で Fort / `ProjectileFactory` / `ChannelController×4` / `IceStock` / `KeyboardInputSource` / `AvalancheController` / `WaveManager` / `UIManager` を生成・配線し、`IGameContext` を実装して `GameStateMachine` を `Update` で駆動（初期 `TitleState`）。`BeginWave(0)`＝新規プレイで砦HP・氷・残敵をリセット。
  - **UI は IMGUI（`OnGUI`）でプロトタイプ実装**（`UIManager`）。氷/砦HPバー・谷ハイライト・Wave ステータス・リザルトを描画。砦HP は `Fort.OnHpChanged` 購読（ADR-3、`OnDestroy` で解除）。uGUI 化・本番ビジュアルは後続の見栄え調整に委ねる（フォント/Canvas 依存を避け検証を高速化）。
  - **入力境界の拡張**: `IInputSource.ConfirmPressed`（Enter のエッジ）を追加し Title 開始/Result 再挑戦に使う。`UnityEngine.InputSystem` 参照は `KeyboardInputSource` 内に限定（ADR-6 を踏襲・拡張）。
  - **見た目はプレースホルダ**: 敵/弾/砦は URP/Unlit のプリミティブをコード生成（`GameManager`）。プレハブ/美術は Phase 後続。
- **結果**: Phase 1 受け入れ基準を全充足。Wave1 通しを Unity-MCP Play Mode で検証（Title→Wave1/2/3→削り→duration→Result(Win/Lose)→再挑戦、スコア=残HP）。EditMode 累計 **76 本緑**。ADR-5/6/9 を踏襲し覆さない。補助変更（`IceStock.Refill`、`Fort.Initialize` の `OnHpChanged` 発火）はリトライ時の状態/表示リセット用。

## ADR-14: 谷の座標は現在の行インデックスで決め、画面内行数を上限とする（ADR-12 の座標方針を Supersede）— Accepted

- **背景**: ADR-12 は敵座標を湧き時の単調増加 depth で固定し survivor 静止としたが、長時間プレイで編隊が外周へドリフトし、**敵がカメラ視野外に湧いて「見えない位置から砦を攻撃する」不具合**を生んだ（プレイ報告）。spec は「先頭行が消えると次行が**繰り上がる**」と明記しており、固定座標より繰り上げ（compaction）が仕様に忠実。
- **決定**:
  - 各敵のワールド座標を **現在の行インデックス**（0=砦最寄り … 麓側）から算出し、先頭行消去のたびに全敵を再配置（`ChannelController.RepositionAll`、`EnemyMatrix.RowAt` を追加）。先頭行が消えると後続が 1 つ手前へ繰り上がる（spec どおり）。
  - **画面内行数の上限 `_maxRows`**（既定4・`frontGap`/`rowSpacing` と合わせ最外 row が ortho 視野内に収まる）を設け、満杯の谷には湧かせない（`SpawnEnemy` が null を返し `WaveManager` は次の谷を試す）。敵は常に画面内＝見える位置からのみ攻撃する。
- **結果**: 「画面外から攻撃される」不具合を解消。MCP 検証: 1谷へ 30 体投入→ rows=4 / enemies=12 / `IsFull`、最外敵 y=4.15（視野±5 内）、超過22体は拒否。通常プレイでも最大距離 2.45 でドリフトなし。ADR-12 の「座標固定/survivor 静止」決定を本 ADR が置換（cap・繰り上げ方針）。雪崩タイミング分離（ADR-12 後半 / `AvalancheClock`）は不変。

## ADR-15: 実機シリアル接続は自前 SerialPort（別スレッド・双方向）＋ Composite で IsShaving を供給（ADR-6 を拡張）— Accepted

- **背景**: Phase 1.5（別計画＝`phases.md` の「実機インターフェース接続」）として、物理かき氷機の**ハンドル回転（削り）**を Arduino 経由のシリアルで取得する。ADR-6 の `IInputSource` を実機に差し替える第一歩。XY（谷選択）と決定は本フェーズではキーボード据え置き（シリアルで来るのは実質 `IsShaving` のみ）。外部アセット（Ardity 等）は使わず `System.IO.Ports` で自前実装する（参考: Qiita hoktist 記事）。自前にすることで 3rd-party の `Assembly-CSharp` 参照境界問題を避け、コードを `KakigoriTD.Runtime` 内に収める。
- **決定**:
  - `SerialConfigSO`（Data）に `portName`/`baudRate`/`readTimeoutMs`/停止コマンドを置く（ADR-1。macOS は `/dev/tty.usbserial-*`、Windows は `COMx`）。
  - `SerialHandler`（MB・**双方向 transport**）: `SerialPort` を別スレッドで `ReadLine()` し、受信行をロック付きキューへ積み、メインスレッドの `Update` で `OnDataReceived(string)` を発火（受信スレッドは Unity API に触れない）。`Send(string)` で `WriteLine`（汎用送信。**送信内容・トリガは本フェーズ未定の拡張ポイント**）。`OnDestroy`/`OnApplicationQuit` で `isRunning=false → thread.Join → Close/Dispose`（終了時に任意の停止コマンド送出）。
  - `SerialInputProtocol`（純 C#・ADR-8）: 受信行 `1`/`0` → 削り bool。無受信 watchdog で `IsShaving=false`（切断時の削りっぱなし防止）。EditMode 検証。
  - `SerialInputSource`（MB: `IInputSource`）: `OnDataReceived` を購読して protocol へ流し `Update` で `IsShaving` を反映。`ActiveChannel`=-1 / `ConfirmPressed`=false。購読は `OnDestroy` で解除（ADR-3）。
  - `CompositeInputSource`（純 C#: `IInputSource`）: `ActiveChannel`/`ConfirmPressed`=キーボード、`IsShaving`=キーボード OR シリアル。`GameManager` が `Composite(keyboard, serial)` を配線し、`SerialConfigSO` 未割り当て時はシリアルを生成せずキーボードのみ（実機なしでも全操作可能）。
  - **Player Setting 変更**: `System.IO.Ports` は .NET Standard では提供されないため、Standalone の API Compatibility Level を **.NET Framework（`NET_Unity_4_8`＝3）** に変更（`apiCompatibilityLevelPerPlatform: {Standalone: 3}`）。既定の `.NET Standard 2.1`（6）では `System.IO.Ports` が `CS0234` になる（Ardity PDF FAQ②・Qiita 記事の前提と同じ）。
- **結果**: EditMode 累計 **92 本緑**（+`SerialInputProtocol` 11 / +`CompositeInputSource` 5）。Unity-MCP 検証: 未接続ポートで警告のみ・固まらない／`_input` が `CompositeInputSource` で `AvalancheController` に同一注入／Play でキーボード→Composite の `IsShaving` OR を実証／既存キーボード操作は回帰なし。ADR-6/10 を覆さず拡張。実機 XY センサのシリアル化・送信コマンド体系の確定は将来フェーズ。
