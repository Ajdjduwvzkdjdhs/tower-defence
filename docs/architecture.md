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
| 味方NPC | AllyNPC : Enemy（同一 EnemyMatrix<Enemy> 多態保持＋専用プール・専用行） | ADR-16 |
| 巻き込みペナルティ | Core PenaltyTally + IGameContext.PenaltyTotal → Scoring.Compute | ADR-17 |
| 突撃隊 | EnemyTypeSO 差のみ（短 fireInterval + tint）。locomotion 不採用 | ADR-18 |
| 変装敵 | DisguisedEnemy : Enemy（専用行・tell マーカー・発射継続・退場しない・無ペナ撃破） | ADR-19 |
| 撃破プレゼン | 繰り上がりを速度ベース lerp＋撃破位置に雪しぶき puff（見せ方のみ・純ロジック不変） | ADR-20 |
| 3D表現 | URP 2D→3D レンダラ切替＋Terrain移植＋透視カメラ＋斜面 PositionFor＋lit（描画のみ・Core 不変） | ADR-21 |
| 敵の登場・静止位置 | 隊列を麓へ（_frontGap 数値のみ）＋入場を絶対半径 _entranceRadius から専用速度で歩かせる（cosmetic・Core 不変） | ADR-22 |
| 発射タイミング | 敵は所定スロット到達後のみ発射（入場・繰り上がりの移動中は発射しない・IsInPosition ゲート） | ADR-23 |
| 実機位置検知 | かき氷機の位置＝ファイル経由 Vision 入力（VisionInputProtocol＋VisionInputSource＋Composite の InputMode 合流。分類は Python・seq 原子的書き込み・config.json 単一真実・ロスト時 -1） | ADR-24 |
| 雪崩の視覚＋撃破起点 | 白い雪玉ストリーム（pooled）＋撃破の起点を雪崩フロントの到達位置（位置ベース・Core 不変）→ **ADR-27 で撃破駆動を実雪玉の到達へ移管** | ADR-25 |
| サウンド | 表現層のみ移植＝薄い AudioService＋AudioConfigSO＋additive cosmetic イベント（Core 無改修・3系統 AudioSource・雪崩は IsAvalanching gate ループ） | ADR-26 |
| 撃破の駆動 | 実際に流れている Snowball(VFX) が先頭行の敵の実位置に届いた瞬間にその谷の先頭行を1行撃破（1雪玉=1行）。別フロントの模擬を廃止＝見た目と同期・入力ちらつきに頑健。後続行の即死は谷ごとの撃破クールダウン（TryApplyAvalanche・既定0.2s）でレート制御 | ADR-27 |
| 展示ビルド化 | 3Dアート（山メッシュ＋FBXアクター＋Animator）・演出・マルチシーン（Title→TH→Result）・LiDAR入力（`seq,channel,shaving` 3列）・`unity` CLI＋`com.unity.pipeline` 開発連携。**git履歴からの遡及要約・要レビュー** | ADR-28 |
| LiDAR 連続座標入力 | LiDAR は谷でなく氷の座標 `seq,x,y,shaving` を送る。検知点から放射状に雪玉ストリーム→一般化位置到達で1体撃破。d 連動で cooldown(0.6→0)/角度spread(25→3)。氷消費は無効。角度は設置時 config 固定・平行移動のみキー calib。**Unity 側実装済み／Python・実機は未着手（Proposed）** | ADR-29 |
| ゲーム性刷新 | 3Wave 制を廃し、単一ステージの「目標撃破数レース」へ。爆発兵の無制限連鎖・盾兵（雪玉N発）・コンボ・ゲージMAXで自動発動する必殺ビーム。スコアはクリア＝クリアタイム／失敗＝到達撃破数。味方NPC・変装敵・ペナルティ・氷ストックは削除 | ADR-30 |
| 演出とロジックの合流 | `main` で独立に作られた演出（爆発テレグラフ・ビーム砲モデル＋光線＋衝撃波・パーティクル雪崩）を ADR-30 のロジックに接続。爆発演出は見た目のみ（判定は `ExplosionChain`）、ビームは光線一周→衝撃波の瞬間に全員撃破、半径は常にロジック側が渡す。旧プレースホルダ（`ExplosionRing`／白フラッシュ `BeamCannon`）と `main` 側の独自ロジック（`KillTally`／撃破数閾値／`KillAllWithin`）は削除 | ADR-31 |
| 雪崩の当たり判定＝粒 | 雪崩の粒そのものを判定にする `ParticleContact` モード：粒が敵に触れたら吸収されて 1 ダメージ（耐久は `EnemyTypeSO.ParticleHitPoints`・種別ごと）。前列が生きている間は雪がそこで止まるので前列から倒れる。クールダウン・1 雪崩 1 撃なし。静止線より外に落としたら雪崩なし。分岐点の挙動は `Legacy` として Inspector で切替可 | ADR-32 |

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

## ADR-16: 味方NPC は AllyNPC : Enemy（同一 EnemyMatrix<Enemy> に多態保持＋ally 専用プール・専用行）— Accepted（Phase 2）

- **背景**: spec §8「マトリックスの1行を占有する味方NPC」を Phase 2 で実装。行ロジック（`EnemyMatrix<T>`）と Pool（`ObjectPool<T>`）を再利用し、最小コストで実装したい。
- **決定**:
  - `AllyNPC : Enemy`（継承）。`EnemyMatrix<Enemy>` に多態で保持することで、Core の行ロジックを無改修で流用。
  - 味方は **`EnemyMatrix.AddRow`（隔離行）**で専用行に置き、`AddToBack` では埋められない（ガード行として隔離）。退場は `RemoveRowAt` で中段繰り上げ（奥の敵が解放される）。
  - `AllyNPC.Update` を override し `allyLifetime` カウントダウン→`OnExpired` 発火で自走退場（無発射は `Projectile=null` で `Fire()` ガードが自動成立）。
  - 色は `Enemy.Init` で `MaterialPropertyBlock` を per-instance 設定（`sharedMaterial` を汚さずプール再利用で赤⇄青を切り替え）。
  - `ChannelController` は ally 専用 `ObjectPool<AllyNPC>` を持ち、`OnAllyCaught`（C# event）を持つ。雪崩がガード行に達すると発火、自走退場は無ペナルティ。
- **結果**: `EnemyMatrix<Enemy>` の型パラメータにより Core は `AllyNPC` を知らず回帰なし。EditMode は `EnemyMatrix<int>` のまま緑。MCP 検証: SpawnAlly→青キューブ出現・ShotsFired=0・OnAllyCaught 発火実証済み。ADR-4（Pool）・ADR-8（純ロジック分離）・ADR-11（MonoBehaviour 状態許容）を踏襲。

## ADR-17: 巻き込みペナルティは Core PenaltyTally + IGameContext.PenaltyTotal で供給（ADR-8/9 を踏襲）— Accepted（Phase 2）

- **背景**: spec §8 の「`allyHitPenalty` 加算」を実現するため、減点合計をスコア算出（`Scoring.Compute`）まで届ける経路が必要。State が外界に直接触れないという ADR-9 の制約のもと、最小変更で実現したい。
- **決定**:
  - `PenaltyTally`（Core・純 C#）を新設し `Add(int)` / `Reset()` / `int Total` を持つ。MonoBehaviour 非依存で EditMode 検証可能。
  - `IGameContext` に `int PenaltyTotal { get; }` を追加（ADR-9 の拡張点）。`GameManager` が `PenaltyTally.Total` を返す。
  - `WavePlayingState` / `ResultState` のスコア算出を `Scoring.Compute(ctx.FortHp, ctx.PenaltyTotal)` に変更（ADR-5 の Phase 1 で用意した `penalty` 引数を初めて使用）。
  - `GameManager` が各 `ChannelController.OnAllyCaught` を購読し `_penalty.Add(_allyHitPenalty)`（ADR-3。`OnDestroy` で解除）。
- **結果**: EditMode で `PenaltyTally` 6本・`StatesTests` +3本（win/lose/penalty=0 回帰）緑。MCP 検証: `PenaltyTotal=0→0→50`（手前ノーペナ→ally巻き込み +50）実証済み。`Scoring.Compute` は不変（Phase 1 で拡張点を用意済み）。ADR-8/9 を覆さず拡張。

## ADR-18: 突撃隊は EnemyTypeSO パラメータのみで表現（locomotion 不採用）— Accepted（Phase 3）

- **背景**: spec §9 では突撃隊を「速い敵」と定義。実装方針として ①`moveSpeed` による実際の移動（locomotion）か、②`fireInterval` 短縮（発射テンポ）かの2択が生じた。
- **決定**: 突撃隊は **`EnemyTypeSO` のパラメータ差（短い `fireInterval=0.8` ＋ 識別用 `tint` オレンジ）のみで表現**し、**新規クラスを作らない**。`moveSpeed` による locomotion は不採用。「速さ」＝発射テンポの差として解釈する。
  - **理由①（行モデルとの非整合）**: 現行の静止行モデル（ADR-12/14：座標は行インデックス基準・雪崩で繰り上げ）では、敵が行の外を自由移動すると「先頭行から削る」深さ制御の核が崩れる。
  - **理由②（プロトタイプ目的）**: 遠隔攻撃モデルでは「砦に近い＝より危険」が成立しないため locomotion のゲーム的恩恵が薄い。発射テンポの差で「速いプレッシャー」は十分体感できる。
  - **理由③（実証）**: MCP Play 検証で BasicEnemy avg ShotsFired 24.4 に対し AssaultEnemy 61.8（≈2.5倍）を実測。`fireInterval=0.8/2.0` の理論値と一致。
- **結果**: 突撃隊は `AssaultEnemy.asset`（EnemyTypeSO）として定義。コード追加ゼロで受け入れ基準①（「速さ＝発射テンポ」と微修正）を充足。`moveSpeed` フィールドは将来の locomotion 要件発生時のために define-only で据え置く（実装時は攻撃方式 Strategy 化／移動サブシステムを別 ADR で検討）。ADR-1（SO）・ADR-11（MonoBehaviour 状態許容）を踏襲。

## ADR-19: 変装敵は DisguisedEnemy : Enemy（専用行＋tell マーカー・発射継続・退場しない）— Accepted（Phase 3）

- **背景**: spec §9 の「青に化けた敵を見破る」緊張を実現するため、「真の味方（AllyNPC）と見た目が似ているが実は撃ち続ける敵」が必要。ペナルティ経路（ADR-17）を改修せずに済む設計が望ましい。
- **決定**:
  - `DisguisedEnemy : Enemy`（継承）。同一 `EnemyMatrix<Enemy>` に多態で保持（Core 無改修）。
  - **擬態行（専用行）**: `ChannelController.SpawnDisguised` が `EnemyMatrix.AddRow`（隔離行）で配置。`AllyNPC` と同じ行占有形式なので視覚的に同じ位置に出る。
  - **発射継続**: 基底 `Enemy.Update` のまま（`Projectile≠null` で撃つ）。自走退場しない（`AllyNPC` と違い lifetime カウントなし・`OnExpired` 非購読）。
  - **tell（見た目差）**: `Enemy.OnInitialized()` フック（`protected virtual`）で暗色キューブ（`_tellMarker`）を角に付加。味方は tell なし、変装敵は常に tell あり。注意すれば識別可能＝「理不尽でない」を担保。
  - **ペナルティ経路**: `ApplyAvalanche` の type check で `e is DisguisedEnemy → _disguisedPool.Release`（`OnAllyCaught` 未発火）。変装撃破＝無ペナルティ、真の味方撃破＝既存 `allyHitPenalty`（ADR-17）を自動成立させる。
  - **専用プール**: `ChannelController` が `ObjectPool<DisguisedEnemy> _disguisedPool` を持つ。`ClearAll`・`ApplyAvalanche` でプール返却時に `Kill()` して非アクティブ化。
- **結果**: コード変更は `Enemy.cs`（`OnInitialized` 仮想フック1点追加）・`DisguisedEnemy.cs`（新規）・`ChannelController.cs`（`_disguisedPool`・`SpawnDisguised`・`ApplyAvalanche`/`ClearAll` 分岐追加）・`WaveManager.cs`（`SpawnSelector` 分岐追加）・`GameManager.cs`（`CreateDisguised` デリゲート注入）のみ。`EnemyMatrix<T>`・`PenaltyTally`・`Scoring`・State 群はゼロ変更。ADR-16/17 を踏襲し覆さない。MCP Play 検証: 青キューブ＋tell マーカー視認・ShotsFired>0・変装雪崩→PenaltyTotal delta=0 実証済み。

## ADR-3 移行判断（Phase 3 完了時評価）— 見送り（C# event 継続）

- **評価日**: 2026-06-17（Phase 3 マイルストーン D 完了時点）
- **ADR-3 の3トリガー評価**:
  1. **プレハブがシーン参照なしでイベントを発火したい** → 未成立。本プロジェクトはプレハブを使わず `GameManager` のコード生成で全 GameObject を生成（ADR-13）。弾・敵もプール済み GameObject であり独立したプレハブアセットではない。
  2. **クロスシーン通信が必要** → 未成立。シーンは単一（`MainScene` のみ）。クロスシーン通信の要件は現時点で存在しない。
  3. **相互参照するシステムが概ね5つを超える** → 未成立。`GameManager` が Composition Root（ADR-5）として全配線を一元管理。システム間の直接相互参照はなく、依存は常に `GameManager` → 各システムの単方向。C# event を購読するのは `GameManager` のみ（`OnAllyCaught`・`Fort.OnHpChanged`）で`OnDestroy` で確実に解除済み。
- **結論**: **見送り**。3トリガーがいずれも未成立のため、`*EventChannelSO` への移行は行わない。C# event + GameManager 集約の現構成を継続する。移行を再検討するタイミングは、①マルチシーン化、②プレハブ単位のイベント発火、③5システム以上の相互参照が実際に発生した時点とする。

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

## ADR-20: 撃破プレゼンテーション — 繰り上がり前進を速度ベース lerp 化＋撃破位置に雪しぶき puff（見せ方のみ・純ロジック不変）— Accepted（Extra 1）

- **背景**: 複数行のとき、雪崩で先頭行（砦寄り）を消すと `ChannelController.RepositionAll` が生き残りを内側へ**瞬間移動**させ、「手前が即埋まって外周が消えた」錯覚が出る（Extra 1 課題②）。spec の「先頭行が消える → 後続が繰り上がる」手触りを、純ロジック（`EnemyMatrix`/`AvalancheClock`）とそのテストを変えずに、**見せ方だけ**で改善したい。
- **決定**:
  - **繰り上がり前進を速度ベース lerp 化**: `Enemy` に目標位置 `_targetPos` と `SetTarget(pos, snap, speed)` / 共通 `MoveTowardTarget()`（`Vector3.MoveTowards`）を追加し、`Update` 冒頭（発射処理の前）で目標へ寄せる。`AllyNPC.Update` は `base.Update` を呼ばないため冒頭で明示呼出（`DisguisedEnemy` は `Enemy.Update` 継承で自動）。
  - **前進と湧きの分離**: `RepositionAll` は `SetTarget(PositionFor(...), snap:false)`（lerp）、新規湧き・初期配置（`SpawnEnemy`/`SpawnAlly`/`SpawnDisguised` の当該個体）は `snap:true`（プール由来の砦寄り初期 transform からの滑り込み防止）。前進速度は `ChannelController._advanceSpeed`（`[SerializeField]`・ADR-1）。
  - **撃破位置に雪しぶき puff**: 新規 `SnowPuff`（Gameplay・プール）。`ApplyAvalanche` が Kill/Release の**前**に位置を取得し `_puffPool.Get().Play(pos, Release)`（0.25s 拡大＋フェード → `onDone` でプール返却）。`GameManager.CreateSnowPuff`（加算/半透明 Unlit の白球）を `ChannelController.Configure` に `Func<SnowPuff> createPuff` として注入（ADR-4/5）。
- **不変（覆さない）**: `EnemyMatrix`/`AvalancheClock` の純ロジックと EditMode テストは不変（**見せ方のみの変更**）。ADR-4（プール）／ADR-8（純ロジック分離）／ADR-11 を踏襲。ADR-12/14（座標＝行インデックス基準・雪崩で繰り上げ）を覆さず、その「繰り上げ」の**描画**を lerp 化するもの。
- **結果**: EditMode **120本緑**（回帰なし）。Unity-MCP Play 検証: `ApplyAvalanche` 直後に生き残りが back 座標 → front 座標へ lerp 前進（新規湧きはスナップ）、`killed=3` で `SnowPuff` 3体が撃破座標（`(-0.70,2.70)`/`(0.70,2.70)`/`(0.00,2.70)`）に一致・プール返却で `Instantiate/Destroy` なし。Extra 1 マイルストーン B（コミット `f54a298`）。

## ADR-21: 表現層を3D透視化する（描画のみ・Core の座標非依存性を利用／ADR-13「美術は後続」を具体化）— Accepted（Phase 4）

- **背景**: 見栄え向上のため、俯瞰の平面マップ表現（山＝手続き生成 Quad、全 GameObject を z≈0 に置く正射影）を、造形済み Terrain（別プロジェクト `kakigori-3d-t` の `New Terrain 3.asset`）を用いた **3D 透視表現** に置き換えたい。移植元 `kakigori-3d-t` に再利用可能な資産は実質この Terrain 1 つのみ（アクターは全て Unity プリミティブ＝独自3Dモデル/VFX/雪テクスチャは存在しない）。Core（`EnemyMatrix`/各種 Clock/State/Pool/`Scoring`）と雪崩の行消去・生成パイプラインは不変に保つ（ADR-8/9/11/12/14 を覆さない）。アクターの見た目はプリミティブ継続（案Y／モデル制作は後続）。
- **決定**:
  - **空間基底を XY 平面 → XZ 平面へ**。`ChannelController.DirectionFor` と `_perpDir` を XZ（N=+z / S=-z / E=+x / W=-x）に変更。`PositionFor` は従来の放射状 `along`/`across` を XZ で算出し、**高さ Y は Terrain からサンプル**する。Terrain 依存を Gameplay に持ち込まないため、`GameManager` が高さサンプラ `Func<float,float,float>`（(x,z)→y、内部で `TerrainData.GetInterpolatedHeight`）を `ChannelController.Configure` に注入する（ADR-5 の Composition Root・既存の `Func<T>` 注入に倣う）。**コライダー不使用＝物理なしを維持**（ADR-11 の非物理判定と整合）。→ 行列・雪崩・移動（`SetTarget`/`MoveTowards`）・弾（`Projectile.MoveTowards`）は無改修で 3D 化する。
  - **山 = Terrain 移植**。`GameManager.BuildMountain` の手続き Quad を廃し、`New Terrain 3.asset` から Terrain を配置。移植元の土色 ADG レイヤーは使わず、**雪用の簡易マテリアル/レイヤーを新規**に当てる（雪テクスチャは Phase 4 の新規制作物）。
  - **カメラを透視化**。MainScene のカメラを俯瞰正射影 → 山を斜め上から見る透視に再設定。
  - **ライティング導入**。Directional Light ＋ Terrain は lit マテリアル。**アクターは Unlit プリミティブのまま可**（案Y・視認性優先）。「2D の Unlit / ライト無し」は平面マップ限定の選択で、3D 透視化に伴い補う。
  - **敵の入場（Option A）**: `SpawnEnemy` で敵を隊列スロットより麓側（下り斜面）に置き、`PositionFor(backRow,slot)` へ `snap:false` で歩かせる。既存の `_advanceSpeed`/`SetTarget` lerp を流用する **cosmetic な reposition** であり、gameplay の locomotion（行の外を自由移動）ではない＝**ADR-18（locomotion 不採用）と整合**する。
- **不変（覆さない）**: Core（`EnemyMatrix`/Clock/State/Pool/`Scoring`）・`IGameContext`・入力（`IInputSource`）・`Projectile` のロジック・EditMode テスト・雪崩の先頭行消去。物理不使用（コライダーなし・高さは API サンプル）。ADR-8/9/11/12/14/18 を踏襲し、ADR-13 の「見た目は後続」を具体化する。
- **結果（想定）**: 縫い目 3 点（`PositionFor`＋カメラ＋ライティング）＋ Terrain 移植のみで 3D 雪山を実現。受け入れ基準は `phases.md` Phase 4／詳細計画は `docs/phase4(3D).md`。着手時に Unity-MCP 視覚検証で確認し **Accepted** 化する。

### 着手メモ（マイルストーン A 完了・2026-07-06）— 前提補正と縫い目追加

把握フェーズの想定と実機で食い違った点を記録する（ADR-21 本文は Proposed のまま。Accepted 化は空間の核＝`PositionFor` の XZ＋高さサンプル実装後＝マイルストーン D）。

- **縫い目は 3 点 → 4 点（描画パイプラインを追加）**: demo は URP **2D Renderer**（`Renderer2D` ＋ Global Light 2D ＋ 正射影）構成で、この renderer では Directional Light が 3D ジオメトリを照らさない。Phase 4 の「透視＋ライティング」には **3D Forward レンダラ**が前提。移植元 `kakigori-3d-t` の URP 3D 設定（`PC_RPAsset`＝`UniversalRenderPipelineAsset` ＋ `PC_Renderer`＝`UniversalRendererData` ＋ `SampleSceneProfile`＝VolumeProfile）を `Assets/Settings/` に流用し、`GraphicsSettings.defaultRenderPipeline` と全 Quality レベルの `customRenderPipeline` を PC_RPAsset へ切替。UI（IMGUI）・EditMode は影響なし。→ **ADR-21 の「再利用可能資産は Terrain 1 つのみ」を補正**：Terrain に加え **URP 3D パイプライン一式**も 3d-t から流用する。
- **Terrain の実態**: `New Terrain 3.asset` は「広い平面キャンバスの一角（footprint 約 13%・隅寄り）に造形された compact な山」。正規化高さ max≈0.034・mean≈0 という数値は“平坦”ではなく“広い平地＋小さい山”を意味する（当初フラットと誤読 → 描画で山を確認）。`TerrainData.size` を (1000,600,1000) → **(120,160,120)** に縮小し、**山頂を world 原点**（＝砦／頂上）に合わせて配置。周囲の平面はそのまま「麓の雪原」として残す。
- **雪マテリアル**: 白 TerrainLayer（`SnowWhite.png`）＋ `Universal Render Pipeline/Terrain/Lit` マテリアル（`SnowTerrain.mat`）。移植元の土色 ADG レイヤーは不使用（ADR-21 どおり）。
- **物理不使用**: `Terrain.CreateTerrainGameObject` 生成後に `TerrainCollider` を除去（高さは将来 `GetInterpolatedHeight` サンプル＝マイルストーン B、ADR-11 と整合）。
- **描画 gotcha（2 点）**: ① `Terrain.drawInstanced=true` は instanced 用シェーダバリアント未コンパイルで**マゼンタ化** → `false`。② PC_Renderer 同梱の **SSAO を無効化**（雪面が暗く沈むため）。環境光を強めに（雪の反射光）設定して雪山が白く立体に見えることを screenshot で確認。
### 着手メモ（マイルストーン B 完了・2026-07-06）— 空間の核（斜面配置）

- **空間基底 XY→XZ**: `ChannelController.DirectionFor` を XZ（N=+z / W=-x / S=-z / E=+x）、`_perpDir=(outward.z,0,-outward.x)`、`PositionFor` は along/across を XZ で置き **Y=高さサンプラ** で斜面に乗せる。`RepositionAll`/`SetTarget`/`MoveTowards`（ADR-20）・雪崩・生成は**無改修**で 3D 化。
- **高さサンプラ注入（U3）**: `GameManager.HeightAt(x,z) = _terrain.transform.position.y + _terrain.SampleHeight((x,0,z))`（`SampleHeight`＝`GetInterpolatedHeight` の world 版・コライダー/Raycast 不使用＝物理なし維持）。`ChannelController.Configure(..., Func<float,float,float> heightAt)` に注入（ADR-5 の `Func<T>` 注入に倣う）。`_terrain` は `GameManager` の `[SerializeField]`（シーンの Terrain(Snow) を割当）。未割当なら y=0 フォールバック。
- **砦を山頂へ**: `BuildFort` で `position=(0, HeightAt(0,0), 0)`（＝山頂 y≈5.4）。`BuildMountain` の手続き Quad は撤去（山は Terrain）。
- **スケール確定**: 当初 size=(120,160,120) では山の footprint 半径が約2ユニットしかなく、プレイフィールド（`_frontGap`2.7〜最奥4.35）が山を越えて平地（y≈0）に落ちた。footprint を実測（正規化半径≈0.022）し、**size=(316.8,160.4,316.8)**（footprint 半径≈7・山頂 y≈5.4）へ拡大、山頂を原点に配置（terrain pos≈(-16.1,0,-16.7)）。斜面高さ: r=2.7→2.28 / 3.5→1.55 / 4.35→0.88。ADR-14 同様「方針不変・数値のみ」。
- **検証（MCP Play）**: ①4谷に敵9体ずつ→**全36体が斜面上**（y=1.03〜4.24・平地ゼロ）、砦=山頂(5.40) ②`ApplyAvalanche`（S谷）で **killed=3 / rows 3→2** ＝先頭行（山頂側）消去＋繰り上げが斜面上で成立（Core 無改修）③弾着弾で砦HP減（3D 下でも戦闘成立）。EditMode **120本緑**。
- **残（マイルストーン C/D）**: 入場アニメ（Option A・U5）、Aim インジケータの斜面対応（現状 XY 前提で不整合・U6）、カメラ/雪マテリアル仕上げ、Wave1〜3 通し、アクター斜面向き。ADR-21 Accepted 化は U6。

### 着手メモ（マイルストーン C 完了・2026-07-06）— 敵の入場（Option A）

- **U5**: `ChannelController` に `_entranceGap`（=1.5）を追加。`RepositionAll(justSpawned)` の新規湧き分岐を「入場位置（スロットより麓側＝下り斜面外側）へ `snap:true` → スロットへ `snap:false`」の2段に変更し、既存 `SetTarget`/`_advanceSpeed` lerp（ADR-20）で麓側から歩かせる。`PositionFor(row, slot, extraAlong=0)` に along オフセット引数を追加。
- **1 箇所で統一**: 3 spawn 経路（`SpawnEnemy`/`SpawnAlly`/`SpawnDisguised`）はすべて `RepositionAll(justSpawned)` を通るため basic/味方/変装すべてに入場が適用される。cosmetic reposition であり gameplay locomotion ではない＝**ADR-18 と整合**。Core・雪崩・生成・`Enemy.Update` は無改修。
- **検証（MCP Play）**: 北谷1体が湧き直後 `alongOutward=4.20`（=slot 2.7＋entranceGap 1.5・麓側 y=0.83）→ 数百ms 後 `alongOutward=2.70`（=slot・y=2.55＝斜面を登って整列）を実測。南谷6体 `ApplyAvalanche` killed=3・rows 2→1（雪崩/繰り上がり回帰なし）。EditMode **120本緑**。

### 着手メモ（マイルストーン D 完了・2026-07-06）— 仕上げ＋Wave 通し＝ADR-21 Accepted 化

- **U6 / Aim インジケータの斜面対応**: 従来 XY 平面の Quad（`dir*2.9 + z=0.6`・Z軸回転）だったのを、外向き(XZ)に沿って砦寄り端（along 2.0）→麓側端（along 5.5）の 2 点を `HeightAt` でサンプルし、その傾き（laneDir）に沿って寝かせる方式へ変更。`Quaternion.LookRotation(斜面法線, laneDir)`（+Z=法線＝面をカメラへ／+Y=レーン方向＝長さ）、scale=(幅2.0, 3D長さ, 1)。単一平面帯なので凸斜面のバンプに食い込む → 法線方向へ 0.35 浮かせ、`_Cull=Off`（両面）で向きに依らず表示。物理不使用維持（`SampleHeight` のみ、コライダーなし）。
- **検証（MCP Play・ConfirmPressed 注入で通し駆動）**: Title→(Enter)→WaveIntro→WavePlaying へ遷移。**4谷が斜面上に湧き**（y=2.3〜3.8）、各敵が `fireInterval` で発射（totalShots 増）、**弾が山頂の砦へ着弾し HP 1000→…→0**。Wave 終了→Lull→次 Wave の遷移も 3D 下で成立。砦破壊で **ResultState（FORT DESTROYED・スコア=残り砦HP=0）**、`ConfirmPressed` で再挑戦→`BeginWave(0)` で HP リセット→再び WavePlaying（retry ループ実証）。Aim レーンが S 斜面に沿って（`pos=(0,2.89,-3.79)`・euler pitch≈-47°・長さ 4.81）表示、削り中は加算量UP で明るく。
- **物理不使用・Singleton=GameManager・入力=IInputSource を維持**（配線変更なし。検証時のみ reflection で fake `IInputSource` を注入したが、製品コードは無改修）。EditMode **120本緑**（Core 無改修・回帰ゼロ）。
- **スコープ外（未実施・意図的）**: アクターの3Dモデル化・本格 VFX・サウンド・弾の放物線化（`phases.md` Out of scope）。アクターの斜面向き（任意）も見送り。プリミティブが斜面バンプに半分埋もれる箇所があるが視認可能で、モデル化フェーズで解消する範疇。雪マテリアルは白 lit のまま（十分に立体視認可）。
- **結論**: 縫い目（`PositionFor`＋カメラ＋ライティング＋URP 3D パイプライン）＋ Terrain 移植＋Aim 斜面対応で 3D 雪山を実現。`phases.md` Phase 4 受け入れ基準を総達成したため **本 ADR を Proposed→Accepted 化**。

## ADR-22: 敵の登場・静止位置の再設計 — 隊列を麓へ（数値のみ）＋入場を絶対半径ラインから専用速度で歩かせる（cosmetic・ADR-14/18/20/21 を踏襲）— Accepted（Phase 4.5）

- **背景**: Phase 4 の俯瞰カメラ化（`phase4(3D).md` §10）後、プレイ体験として「敵が**山からかなり遠い平地（カメラ縁）に登場**し、そこから**山の麓まで歩いて接近して静止し攻撃**する」表現にしたい。Phase 4 時点の敵は麓のすぐ外（`_entranceGap=1.5`・ADR-21/U5 Option A）から斜面**中腹**（`_frontGap=2.7`・along 2.7〜4.35）へ登って整列していた。ゲーム性・数値・Core（`EnemyMatrix`/雪崩/生成/`Projectile`）は Phase 4 のまま不変に保ち、**隊列と入場のワールド座標のみ**を変える。
- **決定**:
  - **静止位置を麓へ（数値のみ・ADR-14 前例「方針不変・数値のみ」）**: `ChannelController._frontGap` 2.7→**5.0**（`_rowSpacing 0.55`/`_maxRows 4` 据え置き）。隊列は along 5.0〜6.65＝斜面基部（麓）に静止。座標＝行インデックス基準・cap（ADR-14）は不変。
  - **入場を絶対半径ラインから（案B）**: `_entranceGap`（各スロットからの相対オフセット）を廃し、新規 `_entranceRadius`（＝谷ごとに同一の絶対半径・カメラ縁の遠方ライン）を追加。`RepositionAll` の新規湧き分岐を「谷方向の `_entranceRadius`（スロットの across を保持）へ `snap:true` → スロットへ `snap:false`」に変更。全敵が**同一の遠方アークから一斉登場**する。`PositionFor`/`EntrancePositionFor`/`PointAt` に整理。
  - **入場速度を独立**: 新規 `_entranceSpeed`（＝入場の lerp 速度）。雪崩後の繰り上がり（ADR-20 `_advanceSpeed`）は据え置き＝入場を速くしても繰り上がり演出を巻き添えにしない。
  - **カメラのズームアウト**: 遠方入場ライン（r≈10）＋麓の隊列は俯瞰カメラ(y=20)の N/S 可視半径（±8〜9）に収まらないため、Main Camera を **y=27・z=0（対称化）**へズームアウト（トップダウン構図・fov46 は不変）。N/S 可視半径 ±11.46 を確保。
  - **cosmetic reposition であり gameplay locomotion ではない**（行の外を自由移動しない・入場後は行モデルに収まる）＝**ADR-18（locomotion 不採用）と整合**。ADR-21/U5 Option A を「絶対半径ライン＋専用速度」に発展させたもの。
- **不変（覆さない）**: Core（`EnemyMatrix`/Clock/State/Pool/`Scoring`）・雪崩の先頭行消去・繰り上げ（ADR-14/20）・生成・`Enemy.Update`・`Projectile`・入力（`IInputSource`）・EditMode テスト。物理不使用（コライダーなし・高さは `Terrain.SampleHeight` サンプル・ADR-11/21）。ADR-14/18/20/21 を踏襲。
- **結果**: EditMode **120本緑**（Core 無改修・回帰ゼロ）。MCP Play 検証: ①4谷とも敵が **radial 10.02（カメラ縁の平地・y=0）に一斉登場** ②`_entranceSpeed` で **radial 5.05（麓の隊列スロット）へ歩いて静止**（screenshot: 遠方登場→麓クラスタ）③発射・弾着弾で砦HP減（遠方からでも戦闘成立）④`ApplyAvalanche` killed=3・rows 3→2（雪崩/繰り上がり回帰なし）。カメラ y=27 で N/S 可視 ±11.46。Phase 4.5 マイルストーン A（`_frontGap`）＋B（`_entranceRadius`/`_entranceSpeed`/カメラ）。

## ADR-23: 発射は所定スロット到達後のみ（入場・繰り上がりの移動中は発射しない）— Accepted（Phase 4.6）

- **背景**: Phase 4.5（ADR-22）で入場開始が絶対半径 r≈10（カメラ縁）へ大きく外に出た結果、`Enemy.Update` が**移動中かどうかに関係なく**毎フレーム発射していた既存挙動が顕在化。敵が入場歩行中に `transform.position`（移動中の現在位置）から弾を撃つため、**敵と弾が同時に移動**し、敵が砦方向へ歩く速度の関係で**敵が自分の弾を追い越す**不自然さが出た（プレイ報告）。
- **決定**:
  - `Enemy` に**在位置判定** `IsInPosition`（`(transform.position - _targetPos).sqrMagnitude <= ε²`、ε=0.02）を追加。`Update` の発射ブロックを **`IsInPosition` が true のときのみ**実行し、移動中は `_fireTimer` を進めない（凍結）。→ 初弾は**スロット到達後**に `InitialFireDelay` から計時される。
  - `SetTarget`/`MoveTowardTarget`（ADR-20）は不変。`Vector3.MoveTowards` は目標到達で座標が一致するため ε 判定で確実に検出。
  - **1 箇所で統一**: `DisguisedEnemy` は基底 `Enemy.Update` 継承で自動適用。`AllyNPC` は `Update` を override し発射しないため影響なし。**入場（ADR-22）と繰り上がり（ADR-20）の両方の移動中**に発射停止し、到達で再開する（同一ゲートで成立・追加コード不要）。
- **不変（覆さない）**: Core（`EnemyMatrix`/Clock/State/`Scoring`）・雪崩・生成・`Projectile`・`ProjectileFactory`・入力・カメラ・座標系・EditMode テスト。物理不使用維持。ADR-7（発射）・ADR-11（戦闘アクターの状態は MonoBehaviour）・ADR-22（入場）を踏襲した挙動の明確化。
- **balance 補足（スコープ外）**: 入場中に撃たなくなる分、単位時間あたりの被弾がわずかに減る（＝正しい挙動）。数値調整は行わない。MCP Play では本修正後 Wave1 で砦が生存（HP 275）→Lull 遷移する場面も観測されたが、これは意図した挙動差であり balance 調整の対象外。
- **結果**: EditMode **120本緑**（Core・`Projectile` 無改修・回帰ゼロ）。MCP Play 検証（単体の入場を実測）: 敵が radial 10.02→8.37→5.82 の**歩行中は `ShotsFired=0`**、radial 5.05（麓スロット）**到達後に発射開始**（`ShotsFired>0`）。到達後は静止して撃つため弾を追い越さない。Wave 通し（fake `IInputSource` 注入）: Title→Wave1（砦HP減＝到達後発射）→WaveLull「Wave 1 クリア」→…→ResultState→Confirm 再挑戦→WavePlaying（HP リセット）。Phase 4.6。

## ADR-24: かき氷機の位置検知はファイル経由 Vision 入力（Python 分類＋seq 原子的書き込み＋Composite の InputMode 合流＋ロスト時フェイルセーフ）— Accepted（Phase 5）

- **背景**: 物理かき氷機には「削り（回転）」と「位置検知（XY）」の 2 系統があり、前者は Phase 1.5（ADR-15）で M5Stick シリアル接続済み。後者の位置検知は MediaPipe（Python/OpenCV）で手の中点を追う既存プログラムがあり、これを Unity の照準（`IInputSource.ActiveChannel`）に接続する。デモ展示に間に合わせるため、複雑な通信プロトコルを避け**テキストファイル経由で疎結合**にする（更新 ~50ms・信頼性は最低限で可）。設計判断は `docs/phase5.md`（dig で確定した Q1〜Q12）を正本とする。
- **決定**:
  - **照準への写像＝離散4チャンネル / 分類は Python**: 正規化 (x,y)→放射4セクター（`0:N 1:W 2:S 3:E` / 中心不感帯・未検出 -1）の判定は **Python 側**で行い、Unity は `seq,channel` の int を読むだけ（既存 `ActiveChannel` モデル無改修・連続照準は導入しない）。生の (x,y) は Unity に持ち込まない。
  - **データ契約＝`seq,channel`＋原子的書き込み**: Python は 1 行 `seq,channel`（`seq` 単調増加）を `position.txt.tmp` に書き切って `os.replace()` で**原子的 rename**（partial read を排除）。手未検出・quad 外・不感帯でも `-1` を書き続け seq を進める（正常な「谷なし」と Python 停止を区別）。
  - **Vision 入力＝3層（ADR-15 と対称）**: 純ロジック `VisionInputProtocol`（`seq,channel` パース＋seq 変化ベースの watchdog＋ライブ(A)/ラッチ(B)＋デバウンス。**IO のポーリング再読みに耐えるため watchdog は seq の変化で判定**・Python 再起動で seq が降下しても新鮮扱い）＋ IO の薄い `VisionInputSource : IInputSource`（間隔ポーリングのファイル読み・パス解決）＋ 調整値 `VisionConfigSO`（ADR-1）。`VisionInputProtocol` は MonoBehaviour 非依存＝EditMode 検証。
  - **入力源の合流＝InputMode 明示切替**: `CompositeInputSource` を拡張し `InputMode{Keyboard,Vision}` で `ActiveChannel` の源を明示切替（暗黙の優先順位合成はしない＝キーボードのラッチが本番に紛れ込む事故を防ぐ）。`IsShaving`（keyboard OR serial(M5Stick)）・`ConfirmPressed` は**モードに依らず不変**（位置検知は削り・決定を担当しない）。旧2引数コンストラクタは Keyboard モードへ委譲（後方互換）。
  - **ロスト時フェイルセーフ**: seq が `_lostTimeoutSec`（既定 0.3s）を超えて不変なら「ロスト（Python 停止/ハング）」＝ `ActiveChannel` を **-1 強制**（削っても雪崩が流れない安全側・ADR-15 の watchdog 思想と一貫）＋ `UIManager` の `OnGUI` に赤字警告（**状態Y 限定**・正常な谷なし -1 とは別フラグで区別）。復帰で自動解除。
  - **ちらつき対策＝まず入れない・保険のみ**: セクター境界の channel ちらつきは実機で程度が読めないため、まず素で試し、必要なら `VisionConfigSO` のデバウンス秒（既定 0＝無効）を当日回す。分類本体は Python、ちらつき吸収は調整頻度が高いので Unity 側に置く役割分担。
  - **配置・実行・パス解決（将来ビルド方針）**: Python は `tools/position-tracker/`（`Assets/`・`Packages/` の外）の **uv 環境で隔離**（`.venv` は Unity 非対象）。Unity からは起動せず独立起動（OpenCV 窓で quad キャリブレーション）。共有ファイルのパス解決順（Python・Unity 共通）は「① 環境変数 `KAKIGORI_POSITION_FILE`（手動運用の保険）→ ② **`config.json` の `positionFile`（両者が読む単一の真実）** → ③ Editor はプロジェクトルート相対 / ビルドは `persistentDataPath`」。標的は macOS `.app`（`open` は env を継承しないため env 方式は内部バイナリ直叩き・通常は起動法非依存の config.json を主にする）。
- **不変（覆さない）**: Core（`EnemyMatrix`/各種 Clock/State/`Scoring`/`AvalancheClock`）・雪崩・生成・`Projectile`・カメラ・座標系・`KeyboardInputSource`・`SerialInputSource`・EditMode テスト。物理不使用維持。ADR-6（入力 Strategy）・ADR-8（純ロジック分離）・ADR-15（実機接続＋Composite）を踏襲し覆さない。
- **結果**: EditMode **139本緑**（既存 120＋`VisionInputProtocol` 15＋`CompositeInputSource` mode 分岐 4・回帰ゼロ）。MCP Play 検証（カメラ無し・決定的な `seq,channel` 注入）: ①パス解決＝Editor 相対で `…/tools/position-tracker/out/position.txt` ②Vision モードで file `2208,2`→`vision.ActiveChannel=2`／`avalanche.ActiveChannel=2`、谷切替 `2571,1`→`1`（Composite 経由で AvalancheController まで一致）③seq 凍結で `IsLost=True`・`ActiveChannel=-1`・谷ハイライト消灯・赤字警告「⚠ 位置トラッキング未接続（Python を確認）」表示（screenshot 確認）④Keyboard モード（コミット既定）は Vision の -1/ロストを無視し従来通り・boot にエラーなし。Python 側は `--sim` で `seq,channel` の原子的書き込み・seq 単調増加を確認。実機カメラでの手位置→channel 追従、M5Stick 削りとの併用による雪崩は展示現場で確認（環境にカメラ・実機が無いため headless 範囲外）。Phase 5。

## ADR-25: 雪崩の視覚表現＝白い雪玉ストリーム＋撃破の起点を「雪崩フロントの到達位置」に（位置ベース・Core ロジック不変）— Accepted（Phase 5 後）

- **背景**: これまで雪崩の視覚表現は撃破位置に一瞬出る雪しぶき（`SnowPuff`・ADR-20）のみで、「山頂から麓へ流れ落ちる雪崩そのもの」の表現が無かった。プレイヤーの削り操作（`Space` 長押し／実機ハンドル）に対する視覚フィードバックを強め、Wave1「快感波」の手応えを補強したい。
- **決定**:
  - **プール化した手続き生成の白い球を斜面に流す**: ユーザー案「白い球を prefab 化して大量に落とす」を、**`ObjectPool<Snowball>` で使い回す手続き生成の白球**として実装（ADR-4）。本リポジトリは prefab アセットを持たず、敵・弾・puff はすべて `GameManager` で `CreatePrimitive`＋共有マテリアルで生成する既存パターンに合わせる（＝「再利用できる白球を大量にスタンプ」という狙いはプールが満たす）。
  - **物理なしのスクリプト滑走**: Rigidbody は使わず、`Snowball` が中心（山頂）からの外向き距離 `radius` を重力の代わりに加速させつつ進め、各フレーム `HeightAt(x,z)` で斜面 Y に乗せる（ADR-21 の「コライダー不使用＝物理なし」を維持）。谷幅方向の `lateral` ばらつきでストリームに幅を持たせ、`endRadius`（麓）到達または `maxLifetime` で自動プール返却（ADR-20 の onDone コールバック方式に倣う）。
  - **薄い MonoBehaviour アダプタ `AvalancheVfx`**: `AvalancheController` が既に公開する `IsAvalanching`／`ActiveChannel`（テスト済み状態）だけを読み、削れているフレームにアクティブ谷の `OutwardDirection` 方向へ `spawnRate` 個/秒で `Snowball` を射出。削っていないフレームは spawn アキュムレータを 0 に戻す（溜め噴き出し防止）。ゲームプレイ（撃破解決）には一切干渉しない純 cosmetic。
  - **Composition Root で配線**: `GameManager` に白 URP/Unlit の `_snowballMat`・`CreateSnowball` ファクトリを追加し、`_avalanche` 構築直後に `AvalancheVfx` を子として `Configure(_avalanche, _channels, HeightAt, CreateSnowball)`。
  - **ヒットの起点＝雪崩フロントの到達位置（位置ベース／固定タイマーは不採用）**: 望む因果は「雪崩が山頂で発生→斜面を流れ落ちる→**敵に届いた瞬間に敵が消える**」。試行錯誤の経緯を残す:
    - ❌ 案1（`_avalancheDelay=0`）: 削り開始 t=0 で先頭行が即消える→雪玉が届く前に消える。
    - ❌ 案2（prewarm）: 立ち上がりで山頂→麓全域へ一斉射出→流れ落ちる過程が無く「麓の雪が早すぎる」。
    - ❌ 案3（`_avalancheDelay≈0.78s` の固定タイマー）: 理想状態（敵が r≈5.0 に静止・連続削り）では一致するが、**固定タイマーは脆い**——敵の実位置が入場中は r=7〜10 と一定でない／同期するのは初弾だけ／小刻み削りでクロックがリセットされ 0.78s たまらず何も消えない。「雪崩が敵に届いても消えない」というプレイ報告に至る。
    - ✅ 採用（位置ベース・到達後は killInterval）: `AvalancheController` に**雪崩フロント**（`_frontRadius`）を持たせ、削り中のみ `Snowball` と同じ運動（`startRadius`1.2 / `startSpeed`2.5 / `accel`6 / `maxSpeed`8・`_frontMaxRadius`7 でクランプ）で山頂から前進させる。フロントが `ChannelController.FrontRowDistance`（先頭行の敵の**実位置**の谷軸距離＝`Dot(pos, outwardDir)`）に届いて初めて撃破開始（`_frontArrived`）、以降は `AvalancheClock(delay=0, killInterval)` で先頭行を **killInterval(0.4s) ごとに 1 行**消す（ユーザー選択）。削り停止・谷変更・氷枯渇でフロントと刻みをリセット＝深さ制御（どこまで押し続けたか）。到達判定が敵の実位置ベースなので入場中・小刻み削りでも「雪が敵に届いた時だけ消える」が常に成立。
    - **coupling の明記**: フロントの運動パラメータは `Snowball`/`AvalancheVfx` と一致させる（ズレると撃破と雪の到達がずれる）。両ファイルにコメントを残す。`AvalancheClock` のロジックは無改修（`delay=0` で使用）。
- **不変（覆さない）**: Core（`EnemyMatrix`/各種 Clock/State/`Scoring`/`AvalancheClock` の**ロジック**）・撃破解決の粒度（先頭行×`rowWidth`／1 行 1 ヒット）・敵/弾/味方/変装の生成・`Projectile`・入力・カメラ・座標系・EditMode テスト。物理不使用維持。`SnowPuff`（撃破の雪しぶき）は据え置き＝雪崩本体（流れ）と撃破点（しぶき）で役割分担。ADR-4（プール）・ADR-20・ADR-21 を踏襲。変更は撃破の**起点を時刻ベース→位置ベース**にした点（`AvalancheClock` は到達後の刻みに限定使用）。
- **結果**: 純 cosmetic 部（雪玉ストリーム）は EditMode 追加せず（`SnowPuff` と同方針）。撃破ゲートは MonoBehaviour＋敵実位置依存のため MCP 検証。MCP（fake `IInputSource` 注入・`Time.timeScale=0.05` スロー・reflection で内部状態ログ）で計測: ①削り開始→雪玉が中腹（`front≈3.32` が実際の雪玉先端 `snowMaxR≈3.49` と一致）→`arrived=False`・敵6体維持（**到達前は撃破しない**）②フロントが先頭行 `FrontRowDistance=5.00` に到達→`arrived=True`→撃破開始→killInterval で 2 行とも消滅。流れ落ちる過程は先の 2 枚の screenshot（中腹→麓）で確認済み。削り実入力（Title 通過→WASD→Space）はキーボード実入力依存で MCP 未駆動＝当日プレイで最終微調整。Phase 5 後の視覚補強。

## ADR-26: サウンド（BGM/SE）は表現層のみ移植＝薄い AudioService＋AudioConfigSO＋additive cosmetic イベント（Core 無改修・3d-t の Singleton 直叩きは不採用）— Accepted（Phase 6）

- **背景**: Phase 4 が「サウンド」を後続フェーズへ明示的に予約（`phases.md` Phase 4 Out of scope）。移植元 `kakigori-3d-t` に BGM 1＋SE 6 の 7 音源（`Assets/SoundE/`）と `AudioManager.cs` があるが、3d-t は `AudioManager.Instance.PlaySound("Hit")` の **Singleton 直叩き**で、本リポジトリの鉄則（Singleton は `GameManager` のみ・ADR-5／通信は C# event・ADR-3／調整値は SO・ADR-1）と相容れない。**表現層のみ移植し Core・雪崩・生成・State・`Projectile` は無改修**（ADR-20/21/25 と同じ「見せ方のみ」方針）に保つ。設計判断は `docs/phase6.md`（dig で確定した Q1〜Q6）を正本とする。
- **決定**:
  - **薄い MonoBehaviour アダプタ `AudioService`（ADR-8/11）＋ `AudioConfigSO`（ADR-1）**: `GameManager`（Composition Root）が `AudioService` を子生成し `Configure(cfg)`。3d-t の Singleton は採用しない。純ロジックを持たない薄いアダプタのため EditMode テストは書かず MCP Play で検証（`SnowPuff`/`AvalancheVfx` と同方針・ADR-11/25）。調整値（クリップ・音量・`bgmVolume`）は `AudioConfigSO`。音量は 3d-t 踏襲（`enemyKilled=0.2`/`hit=0.1`/`enemyAttack=0.1`/他=1.0）。
  - **AudioSource 3 系統**: `_bgmSource`（loop）／`_seSource`（PlayOneShot）／`_avalancheSource`（loop・雪崩 gate 専用）。**雪崩を専用 source に分離**することで、3d-t の `PlayAndStop`（`clip=…;Play();…Stop()`）が PlayOneShot と同 source を共有して他 SE を切る問題を構造的に回避。
  - **追加は additive な cosmetic イベント/購読のみ**（既存ロジックの分岐・数値・順序は不変）:
    - `ProjectileFactory.OnCreated`（発射の単一チョークポイント）→ EnemyAttack。
    - `ChannelController.OnEnemyKilled`＝**1 回の `ApplyAvalanche`（先頭行消去）につき 1 回**・実敵/変装を1体でも Release した時のみ（味方のみの行は無音）→ Monster。行は最大 `rowWidth=3` 体なので「敵ごと」だと同フレーム多重になるのを回避（Q2）。
    - `AvalancheController.OnAvalancheChanged(bool)`＝`IsAvalanching` の**エッジ検出**（前フレーム値と比較）→ `_avalancheSource` の loop on/off。`AvalancheVfx` が同 `IsAvalanching` で雪玉を連続射出するのに音を合わせる（Q1）。→ 3d-t の `PlaySoundWithDuration(固定尺)` は**不採用**（持続アクションに対し途中無音・連打多重になるため）。
    - `Fort.OnHpChanged`（既存）→ Hit（**前HP キャッシュし「減少 かつ 新HP>0」時のみ**。`Initialize` の満タン通知・0到達＝GameOver を除外）。
    - `ShowResult(isWin,_)`（既存）→ Clear/GameOver（**BGM は止めず**上に重ねる。Result で止めるとリトライ `BeginWave(0)` で BGM が復帰せず＝バグ源になるため・Q4）。
    - `ChannelController.OnAllyCaught`（既存）→ AllyCaught（3d-t に該当クリップ無し＝**null 既定で無音**・後で差し替え可）。
  - **変装敵の発射音は特別扱いしない（Q5）**: 変装敵は基底 `Enemy.Update` で**見える弾**を撃っており（真の味方 `AllyNPC` は `Projectile=null` で無発射）、発射音は新情報ではなく既存視覚ヒントの補強。単一チョークポイント（`ProjectileFactory.Create`）を濁さない。ADR-19 の緊張（湧き直後・撃つ前に tell で見抜く）は損なわない。
  - **発射音はまず毎発（Q3）**: `OnCreated` を毎発 PlayOneShot（0.1）。密集時の洪水は MCP Play で聴取し、過剰なら `AudioConfigSO` に最小発火間隔を追加（`AudioService` に「最後に鳴らした時刻」を持つだけ）＝手戻りなしの拡張点。
  - **購読解除**: 新規購読（`OnCreated`／`OnEnemyKilled`×4ch／`OnAvalancheChanged`／`OnHpChanged`）を `GameManager.OnDestroy` で解除（既存 `OnAllyCaught` 解除に倣う・ADR-3）。
- **アセンブリ/配置**: スクリプトは `Assets/Scripts/Audio/`（`KakigoriTD.Audio`）＝既存 `KakigoriTD.Runtime.asmdef` 配下に自動収容（新規参照不要）。音源は `Assets/Audio/`（クロスプロジェクトのため OS コピー・`.meta` 除外→`assets-refresh` で GUID 再生成）。`AudioConfig.asset` は `Assets/ScriptableObjects/`。
- **不変（覆さない）**: Core（`EnemyMatrix`/各種 Clock/State/`Scoring`/`AvalancheClock`）・雪崩の撃破解決・生成・`Projectile`・`ProjectileFactory` のプール運用・入力（`IInputSource`）・カメラ・座標系・EditMode テスト（**139本**）。物理不使用維持。ADR-1/3/5/8/11/19/20/21/25 を踏襲し覆さない。
- **結果**: 表現層のみで BGM/SE を実現。実装は `Assets/Scripts/Audio/{AudioConfigSO,AudioService}.cs`（`KakigoriTD.Audio`）＋`Assets/ScriptableObjects/AudioConfig.asset`（7 クリップ割当・`allyCaught` 空＝無音）＋既存4ファイルへ additive イベント追加（`ProjectileFactory.OnCreated`／`ChannelController.OnEnemyKilled`〈行単位・`killedReal` フラグ〉／`AvalancheController.OnAvalancheChanged`〈`_wasAvalanching` エッジ〉＋既存 `Fort.OnHpChanged`）。GameManager が `AudioService` を子生成し `Configure→PlayBgm`＋全購読、`OnDestroy` で全解除。音源は 3d-t からクロスプロジェクト OS コピー（`.meta` 除外）→`assets-refresh` で GUID 再生成。**EditMode 139本緑（Core・`Projectile` 無改修・回帰ゼロ）**。MCP Play 検証（headless・AudioSource 状態＋実イベント経由で実測）: ①boot で `_bgmSource` loop・isPlaying=True・clip=bgm_main（開始時ループ）②`OnAvalancheChanged` ON→`_avalancheSource`（se_avalanche・loop）isPlaying=True／OFF→False（gate ループ on/off）③`Fort.TakeDamage(10)`（1000→990）で Hit 再生／④`ApplyAvalanche` killed=1 で Monster 再生（行単位1回）／⑤`ProjectileFactory.Create` で EnemyAttack 再生／⑥`ShowResult(true/false)` で Clear/GameOver 再生＝**いずれも BGM は止まらず**（isPlaying=True 維持）／⑦Hit 除外の負検証: `Fort.Initialize(1000)` 満タン通知・`TakeDamage→0` はどちらも Hit 無音。Play 中エラー/例外ゼロ。`AudioService` は薄いアダプタのため EditMode テストは追加せず（ADR-11 と同方針）。**当日プレイでの聴取確認（残タスク）**: `se_avalanche.mp3`（尺 15.7s）のループ継ぎ目は長尺のため通常の削りバーストでは表面化しにくいが長押し時に確認、発射音（0.1）の 4 谷密集時の音量は現場聴取し過剰なら `AudioConfigSO` にスロットリング（最小発火間隔）を追加＝手戻りなしの拡張点。Phase 6 受け入れ基準を総達成したため **Proposed→Accepted 化**。

### 着手メモ（音量バランス調整・2026-07-11・プレイ確認済み）

実プレイで 3 音（hit / enemyAttack / avalanche）が聞こえない報告を受け、`ffmpeg volumedetect` で各クリップ音圧を実測して原因を切り分け・修正した（発火は Play 検証で確認済＝原因は音圧）。
- **hit / enemyAttack＝設定音量が小さすぎ**: クリップ自体は大（mean −13dB 前後）だが `AudioConfigSO` の音量 0.1（実効 −33dB）で BGM（−16.8dB）に約16dB 埋もれていた。→ `AudioConfig.asset` の `_hitVolume` **0.1→0.7**、`_enemyAttackVolume` **0.1→0.4**（enemyAttack は頻発のため中庸）。コード変更なし。
- **avalanche＝設定 1.0 で正常だが素材が「頭無音のスウェル」**: `se_avalanche.mp3`（元15.7s）は頭0.5s −28dB→中盤 t=5-6s で最大→末尾減衰の一発音で、ループ gate が毎エッジで頭出しするため小刻み削りで静かな頭しか鳴らなかった（長押しでのみ聞こえる症状＝プレイで確認）。→ **最も大きい t=4〜8s を切り出し＋固定ゲイン＋リミッター（頭0.5s −9.4dB / 全体 −9.3dB / ピーク −0.3dB）** で作り直し、`.meta` 温存で上書き（GUID/参照維持・尺 8s）。頭出しから即・大音量になり小刻み削りでも鳴る。音量設定は 1.0 のまま素材強化で対応。
- **不変**: コード（`AudioService`/イベント/購読）・EditMode 139本は無改修（アセット差し替え＋SO 音量のみ）。追加のバランス調整余地（BGM を 0.7 に下げる／enemyAttack を絞る／スロットリング追加）は `AudioConfigSO` で手戻りなく可能。

## ADR-27: 撃破の駆動を「別フロントの模擬」から「実際に流れている雪玉(Snowball)の到達」へ移管（ADR-25 の撃破起点を改訂・Core ロジック不変）— Accepted

- **背景**: ADR-25 は撃破の起点を「位置ベースの雪崩フロント到達」に定めたが、その実装は `AvalancheController` が **`Snowball` と別に**フロント（`_frontRadius`）を同一運動パラメータ（`startRadius`/`startSpeed`/`accel`/`maxSpeed`）で模擬し、先頭行の実位置に届いたら `AvalancheClock(delay=0, killInterval)` で **killInterval(0.4s) ごとに 1 行**消す方式だった。これは 2 つの実害を生んだ:
  - **二重管理の脆さ**: フロントの運動パラメータを `Snowball`/`AvalancheVfx` と手で一致させ続ける必要があり（両ファイルにコメント警告あり）、ズレると「雪が敵に届いても消えない／届く前に消える」が再発する。
  - **連続削り時間への依存＝位置検知ちらつきで撃破不能**: フロントは削り中のみ前進し、**谷変更・削り停止で山頂へリセット**（深さ制御）。かき氷機の位置検知（ADR-24 の Vision）は境界で隣接谷と一瞬入れ替わるため、フロントが先頭行（r≈5.0）へ到達する連続時間（≈0.8s）を溜められず、**「削っているのに敵が倒せない」**が頻発した（ユーザー報告）。デバウンス（ADR-24 の保険）で緩和はできるが、撃破が「連続時間の閾値」に依存する構造そのものが根本原因。
- **決定**（dig で 2 点確定: 到達モデル＝「雪玉VFX到達」／到達後＝「先頭行1行を即撃破」）:
  - **撃破の駆動を実 `Snowball` に一本化**: `AvalancheVfx` が射出する各雪玉に、その谷の「先頭行の谷軸距離」（`ChannelController.FrontRowDistance` を返す `Func<float>`・毎フレーム再評価）と撃破コールバック（`() => channel.ApplyAvalanche()`）を渡す。雪玉の `radius` がその距離に届いた瞬間、**1 雪玉につき 1 回だけ**（`_reachedFront` フラグ）先頭行を 1 行消す。谷が空なら距離は `+∞` で発火せず純 cosmetic に戻る。→ 撃破と雪の見た目が定義上つねに一致（別フロントの模擬・パラメータ二重管理を撤廃）。
  - **`AvalancheController` を縮小**: 別フロント（`_frontRadius`/`_frontSpeed`/`_frontArrived`/`ResetFront`）・`AvalancheClock`・`killInterval` を撤去。残す責務は氷消費・`IsAvalanching`/`ActiveChannel` の公開・`OnAvalancheChanged` エッジ通知（音・VFX gate 用）のみ。撃破の権威（kill authority）は持たない。
  - **旧 killInterval 刻み（持続削り 0.4s/行）は廃止し、谷ごとの撃破クールダウンに置換**: 到達後の「削り続けないと次行が消えない」ドリップは撤廃（1 雪玉 = 1 到達 = 1 行の即時撃破）。ただし `_spawnRate=24`/s の連続射出では麓に届く雪玉が高頻度で、放置すると先頭行に続いて**後続行まで一気に消えてしまう**（深さ制御の喪失）。→ `ChannelController` に**谷ごとの撃破クールダウン** `TryApplyAvalanche(killInterval)` を設け、前回撃破から `killInterval`（既定 0.2s・`AvalancheVfx._killInterval`）未満の到達は撃破しない。**先頭行は（前回から十分空いていれば）即時**・**後続行は killInterval 間隔**で消える。クールダウンは谷ごとに独立（`_lastKillTime`）なので複数谷が干渉しない。旧ドリップとの本質差: 連続削り時間に依存せず（発射済み雪玉が完走）、削りを止めても在庫雪玉が間隔を保って撃破する。
  - **入力ちらつきへの頑健性**: いったん発射された雪玉は入力の一瞬の切替に**関係なく**麓まで滑って撃破するため、位置検知が隣接谷と一瞬入れ替わっても、主谷へ発射済みの雪玉が届いて撃破する。「連続時間の閾値」に依存しない構造になり、根本原因を解消（デバウンスは撃破の前提ではなく谷ハイライトの安定化としてのみ任意）。
- **coupling の明記（ADR-25 からの反転）**: ADR-25 は「`AvalancheVfx`/`Snowball` は撃破に一切干渉しない純 cosmetic」としたが、本 ADR は **`Snowball` を撃破の駆動に昇格**させる（`Launch` に `frontRowDistance`/`onReachFrontRow` を追加。既定 null で従来の純 cosmetic も維持）。トレードオフ: 見た目と撃破の同期・パラメータ二重管理の解消・入力頑健性を得る代わりに、gameplay が VFX ストリームに依存する。撃破の粒度（先頭行 × `rowWidth` / 1 行 1 ヒット）・`ApplyAvalanche` の中身（`SnowPuff`・`OnEnemyKilled` 音・味方巻き込み `OnAllyCaught`）は不変。
- **不変（覆さない）**: Core（`EnemyMatrix`/State/`Scoring`/各種 Clock の**ロジック**）・`ChannelController.ApplyAvalanche` の撃破解決と粒度・敵/弾/味方/変装の生成・`Projectile`・入力（`IInputSource`）・カメラ・座標系・EditMode テスト。物理不使用維持（`Snowball` は従来どおりコライダーなしのスクリプト滑走）。`AudioService` 連携（`OnEnemyKilled`＝行単位1回・`OnAvalancheChanged` gate）は `ApplyAvalanche`/`AvalancheController` を経由するため据え置きで機能。ADR-4（プール）・ADR-20・ADR-21・ADR-24 を踏襲。`AvalancheClock`（Core・EditMode 済）は本改訂で**未使用**になるが、Core ロジックとテストは残置（将来のペース制御拡張点／削除は別判断）。
- **残タスク（MCP Play 検証）**: ①削り→雪玉が麓（先頭行）到達の瞬間に先頭行が消える（届く前に消えない・届いて消える）②**先頭行は即時・後続行は約 0.2s 間隔**で消える（クールダウンが効き、後ろの行まで一気に消えない）③位置検知ちらつき相当（谷を小刻みに切替）でも主谷へ発射済みの雪玉で撃破が成立④タップ（短い削り）でも発射された雪玉が届いて撃破＝連続長押し不要⑤氷枯渇で新規射出停止・在庫雪玉は完走⑥撃破音（Monster・行単位）/雪崩ループ音が従来どおり。`AvalancheController`/`AvalancheVfx`/`Snowball`/`ChannelController` の撃破まわりは薄いアダプタ／単純タイマー（ADR-11）のため EditMode テストは追加せず MCP Play で検証（ADR-11/25 と同方針）。

## ADR-28: 展示ビルド化（3Dアート・マルチシーン・LiDAR入力・Pipeline 開発連携）— 記録（遡及・要レビュー）

> **このエントリは 2026-08 に `main` へ直接マージされた作業を git 履歴から再構成した要約であり、正式な「決定／結果／不変」の精査は未了。** 詳細は各ブランチと `docs/phases.md` Phase 7 を参照。ADR-1〜27 のように「これに従えば配線が完了する」レベルの規範性はまだない。

- **背景**: Phase 1〜6（ADR-1〜27）で遊びの核と表現層の骨格が固まった。展示に出すため、プリミティブのままだったアクター・山を 3D アセットへ、平面的な演出を本番演出へ、単一シーン運用を Title→ゲーム→Result のフローへ、開発時の検証を Editor 手動から自動連携へ、それぞれ引き上げる必要が生じた。

- **変更点（領域別・git 履歴ベース）**:
  - **3D アート**: 手続き生成の山 Quad／プリミティブのアクターを、造形済み山メッシュ（外部作。虹色サーモ風の中間アセットは廃棄）と FBX アクター（`TGS_兵士赤` 敵兵・`TGS_Ally` 味方・市民）＋ Animator へ置換。地形の高さ基準を山メッシュへ移し、敵停止位置を radial 8.0 に。旧・敵モデル生成コードは削除。
  - **演出**: 撃破時に敵が横回転で吹き飛んで蒸発（`DissolveBurst`）、パーティクルを prefab 化（`SnowPuff`/`Snowball`/`SphereSpawner`）。撃破音は 3 クリップの順送り再生。
  - **雪崩シェーダー**: カスタムシェーダー 2 種（「キラキラの雪崩」「シンプルな雪」）＋環境光調整（`AvalancheVfx` 経由で切替。ADR-25/27 の雪玉ストリーム駆動は不変）。
  - **マルチシーン**: `TitleScene` / `ResultScene` を新規作成し prefab 化、Build Settings に `TitleScene → TH → ResultScene` を登録。各シーンに `Enter` キー遷移（`TitleScene.cs` / `ResultScene.cs` = `KakigoriTD.Scenes`、`GameManager` は勝敗確定後 `SceneManager.LoadScene("ResultScene")`）。ゲーム本体は `TH.unity`（`Assets/Prefabs/TH_Stage.prefab` に `GameManager` 一式）。`SampleScene` は旧セットアップとして Build 対象外で残置。
  - **LiDAR 入力（ADR-24 の拡張）**: `tools/lidar-tracker` を公式 YDLidar-SDK ベースへ載せ替え、`valley_tracker.py` が落氷を 4 谷へ分類して **`seq,channel,shaving`（3 列）** を原子的に書く。ADR-24 の 2 列契約（`seq,channel`）に 3 列目 `shaving` を追加し、`VisionInputSource` が谷選択に加えて削りも供給できるようにした（旧 2 列ファイル＝position-tracker では常に false で後方互換）。`tools/lidar-tracker2` / `2.1` は Hokuyo UBG-04LX-F01 の別系統検証。
  - **開発連携**: `com.unity.pipeline`（`0.5.0-exp.1`）を `manifest.json` に追加。`unity` CLI（`~/.unity/bin/unity`）で起動中 Editor をヘッドレス駆動（recompile / run_tests / screenshot / GameObject・シーン操作 / eval）。旧 Unity-MCP（`com.ivanmurzak.unity.mcp`、2026-07-30 に manifest から削除済み）の OpenUPM scoped registry 残骸を撤去。運用ルールは `CLAUDE.md`「Unity Editor 連携」節。`docs/mcp-guide.md` は旧 MCP 前提のまま（ツール名は読み替え）。

- **不変（この作業で覆していないはず。要確認）**: Core（`EnemyMatrix`/State/`Scoring`/各種 Clock のロジック）・雪崩の撃破解決（ADR-27）・生成/プール（ADR-4）・入力抽象 `IInputSource`（ADR-6）・Singleton は `GameManager` のみ（ADR-5）・調整値は SO（ADR-1）・物理不使用。EditMode は現在 **151 本**（`unity command run_tests --mode editor` で緑を確認）。

- **未了（このエントリを正式 ADR に昇格させる前に）**: ①3Dモデル/シェーダー/Animator 統合の設計判断を各領域の ADR に分割 ②マルチシーン通しの受け入れ基準と Play 検証記録 ③LiDAR 実機 end-to-end ④`SampleScene` の要否判断 ⑤各ブランチが Core/入力/Singleton 方針を破っていないかのレビュー。

## ADR-29: LiDAR 入力を連続座標に置換し、検知点から放射状に雪玉を放出する（谷選択を廃止・ADR-24/25/27 を LiDAR 経路で改訂）— Proposed（`preview-feedback`）

- **背景**: ADR-24 は LiDAR/カメラの入力を「4 谷のどれか（`ActiveChannel`）＋削り」に落としてファイル経由で渡す設計だった。内覧会のフィードバックを受け、氷が落ちた**連続座標そのもの**を使い、その位置から雪玉を出す方式に変えたい（先生コメント「中心近くは雪崩の範囲が拡大するがダメージが低下する」等のギミック余地を作るため）。詳細と Q&A の確定事項は `docs/phase-preview-feedback.md`。

- **決定**:
  - **データ契約**: Python は氷の代表点（関心円内で中心最近傍の 1 点）の座標 `seq,x,y,shaving` を書く（mm・LiDAR 原点フレーム・`x = r·cosθ`, `y = −r·sinθ`）。検知なしは `seq,,,0`。旧 `seq,channel,shaving` は LiDAR 経路で廃止。関心円（中心オフセット・半径・許容）は Python 側 `tools/lidar-tracker/preview.toml`。
  - **谷選択の廃止（LiDAR 経路）**: `ActiveChannel` を使わない。`ChannelController` の**敵スポーン（4 方向の行列）は不変**。撃破は谷を経由せず、`EnemyRegistry` が 4 谷を横断して「雪玉の現在位置から `killRadius` 内の最も近い生存敵を 1 体撃破」する（`EnemyMatrix<T>.Remove(item)` を新設）。**1 雪玉 = 1 撃**（`Snowball._reachedFront` 流用）。
  - **放出**: `LidarAvalancheVfx` が検知点 P から**山中心を通る放射方向**へ雪玉ストリーム（`_spawnRate`/s）。拡散は横オフセットではなく**放射方向に対する角度ばらつき（±deg）**で表す。
  - **d 連動ギミック**: `d`＝検知点の中心距離[mm]（0..`radiusMm` にクランプ）。撃破クールダウン `cooldown(d) = lerp(中心 0.6s, 外周 0.0s)`（グローバル単一ゲート・「今フレームの d」に依存する割り切りを承知で採用）、拡散 `spread(d) = lerp(中心 ±25°, 外周 ±3°)`。中心＝広く弱い / 外周＝狭く強い。両端は `LidarPreviewConfigSO`。
  - **物理不使用を維持**: 撃破は `Snowball` の一般化位置到達（コライダーなし・スクリプト滑走）。ADR-11/21/25/27 の「物理を使わない」方針は踏襲。
  - **氷消費は無効（無限）**: `LidarPreviewConfigSO._consumeIce = false` 既定。`IceStock`／`AvalancheController` の氷ドレインは保険として残置し、`true` のときだけゲートする分岐を持つ。「完全排除」指示で一括削除（手順は phase-preview-feedback §3.10）。
  - **キャリブレーション**: 角度・回転・反転は設置時に `LidarPreviewConfigSO`（`_worldRotationDeg`/`_flipX`/`_flipY`）で一度確定して以後固定。プレイ中は `C` 長押し＋矢印キーで**山中心の平行移動オフセット（`field_offset`・10mm 刻み）**のみを nudge、離すと PlayerPrefs へ保存。
  - **旧経路の残置**: `InputMode.Keyboard`（W/A/S/D 谷選択＋旧 `AvalancheVfx`）と `InputMode.Vision` は当面残す。`_inputMode` で排他。新モデルが現場で通ったら別コミットで削除。

- **不変（覆さない）**: Core（`EnemyMatrix` の行ロジック本体・State・`Scoring`・`PenaltyTally`・各 Clock）、`ChannelController` の敵スポーン・隊列・繰り上げ、`Enemy`/`AllyNPC`/`DisguisedEnemy`、`Fort`、`WaveManager`、`Projectile`/`ProjectileFactory`、`AvalancheController`（旧経路用に据置）、Singleton は `GameManager` のみ（ADR-5）、調整値は SO（ADR-1）、物理不使用。味方巻き込みペナルティ（`OnAllyCaught`→`PenaltyTally`）と撃破音（`OnEnemyKilled`）は新経路の `KillOne` からも発火して従来の配線を流用。

- **検証状況**: Unity 側は EditMode **187 本**（`LidarPositionProtocol` 13 / `LidarPointMapper` 9 / `EnemyMatrix.Remove` 4 / `EnemyRegistry` 5 / `LidarPreviewConfigSO` 5 ＋既存 151）＋ PlayMode 2 本（`LidarAvalancheVfx` の放出／撃破パス到達／`IsEmitting` の toggle）緑。`unity command` で確認。**Python 側（`valley_tracker.py` の連続座標化）と実機 end-to-end は未着手**（`preview.example.toml` のみ用意。関数単位パッチは phase-preview-feedback §2.3）。

- **昇格条件（Proposed → Accepted）**: ①`valley_tracker.py` の改修と `preview.toml` の現物合わせ ②LiDAR 実機で「氷を落とす→その位置から雪玉→当たった敵が消える」「中心＝広く弱い / 外周＝狭く強い」を確認 ③マルチシーン通し（Title→TH→Result）で回帰なし ④旧経路の削除 or 正式残置の判断。

## ADR-30: 3Wave 制を廃し、単一ステージの「目標撃破数レース」へ（爆発連鎖・盾兵・コンボ・必殺ビーム）— Accepted（`64-vansuv`）

- **背景**: 従来は 3 Wave 制で、Wave ごとに敵の特徴を変え、味方NPC・変装敵で「撃ってよいか」のジレンマを出す設計だった（ADR-17/19）。だが展示での体験としては「大勢の敵を倒す爽快感」が薄く、ジレンマ系の要素は既に実質停止していた（`WaveManager` が `hasAlly:false` 固定、`ConsumeIce=false` 既定、`EnemyMatrix` は実参照ゼロ）。ゲーム性そのものを差し替える。

- **決定**:
  - **単一ステージ化**: `WaveIntroState` / `WavePlayingState` / `WaveLullState` と `WaveConfigSO`×3 を廃止し、`BattleState` ＋ `StageConfigSO` 1 本に統合。Wave バナー・小休止・Wave 終了時の残敵一掃は消える（コンボが途切れないようにするため）。
  - **終了条件**: クリア＝`StageConfigSO.TargetKillCount`（既定100）体撃破 ／ 敗北＝砦HP 0。同フレームに両立したら**敗北を優先**（旧 `WavePlayingState` の流儀を踏襲）。
  - **スコア**: `Scoring.Compute(fortHp, penalty)` を廃止し `StageResult` に置換。**クリア＝クリアタイム**（短いほど上位・ランキング対象）／**失敗＝到達撃破数**（「62 体で力尽きた」・ランキング対象外）。砦HP はスコアに含めず**生存リソースに純化**する（削られてもタイムを縮めれば勝てる＝攻めの動機）。
  - **撃破スループット**: 通常の雪玉は**単体撃破のまま**維持（`EnemyKillSelection.SelectFront` の前列判定を残す）。テンポは `LidarPreviewConfigSO._cooldownCenterSec` を 0.6 → **0.25** に短縮して稼ぐ。**範囲殲滅は爆発兵の専売**にする（通常雪玉を AoE 化すると盾兵の設計意図が崩れるため）。
  - **爆発兵（`BombTrooper`）と連鎖**: 撃破すると爆発半径（既定 4.5。敵は 24 列・半径 8 に並ぶため隣列 ≈2.1、2 列先 ≈4.2。3.0 では隣列にしか届かず巻き込みがほぼ起きなかった）内の敵を全滅させ、範囲内の爆発兵はさらに誘爆して**無制限に連鎖**する。段数制限・半径減衰といった**不可視のルールは入れない**——観客に「爆弾に当てた→画面中が吹き飛ぶ」という因果が一目で伝わることを優先し、制御は**出現率と爆発半径という可視のパラメータだけ**で行う。探索は純ロジック `ExplosionChain.Resolve`（BFS・二重処理なし）。
  - **盾兵（`ShieldTrooper`）**: 雪玉 `StageConfigSO.ShieldHits`（既定20）発で撃破、爆発なら耐久を無視して一撃。 **単独では湧かず、必ず「爆発兵（前）＋盾兵（後ろ）」のペアで同じ列に湧く**（`EnemyField.SpawnBomberShieldPair` / `EnemyQueueLayout.TryAllocateColumnWithFreeDepth`）。盾兵の正攻法は前の爆発兵を倒して巻き込むことなので、盾兵が爆発兵より前に立つ配置を作らない。爆発兵を先にリストへ入れる（＝古い）ため、繰り上がり後も爆発兵が前に残る。2 段とも空いている列が無ければそのスポーンは見送る。**完全無敵にはしない**——`SelectFront` は「重なり半径内により古い生存敵がいる敵は前列でない」として除外するため、不死身の盾が居座るとその背後が永久に撃破不能なデッドゾーンになる。耐久制なら必ず退くので詰みが起きない。現場で「何度当てても無反応＝故障」と誤解されるのも防ぐ。
  - **コンボ（`ComboTracker`）**: 前回撃破から `ComboWindowSec`（既定2.0秒）以内なら継続。**爆発の連鎖撃破も 1 体ずつ加算**する（20体連鎖＝+20）。爆弾を狙うのが本作で唯一の能動的な上手さなので、それがコンボとゲージの両方に効くようにする。
  - **必殺ビーム砲（`SpecialGauge` + `BeamCannon`）**: ゲージ＝`PerKill × (1 + ComboBonus × (combo-1))`（**撃破数がメイン・コンボがボーナス**）。MAX で**自動発動**する——展示にはプレイヤーが押せるボタンが無く（`ConfirmPressed` は開発用キーボードのみ）、来場者は説明を読まないため、発動条件の理解コストを 0 にする。効果は**全方位一掃**（照準入力が無い以上、方向を持たせると操作と無関係に見える）。**ビーム撃破は撃破数に算入するがゲージには算入せず**（ビーム→大量撃破→即MAX の自己充填ループを断つ）、コンボは加算も中断もしない中立扱い。
  - **難易度ランプ**: 軸は**撃破数の進捗**（`KillProgress.Progress01`）。ランプさせるのは**爆発兵率・盾兵率の 2 つだけ**で、`StageConfigSO` に「開始時／終了時」の 2 値で持ち線形補間する（当初は `AnimationCurve` だったが、現場で数字を直接触れるよう 2 値に簡略化）。**湧きレート（`SpawnRatePerSecond`）は固定値**とし、盤面の混雑は「湧き速度 − 撃破速度」の自然な平衡に任せる。ステージの長さ・難度調整の主ノブは `TargetKillCount`。
  - **旧資産の削除**: `EnemyMatrix` / `IceStock` / `PenaltyTally` / `Scoring` / `AllyNPC` / `DisguisedEnemy` / `WaveConfigSO` と対応テストを削除。`DisguisedEnemy`（`Enemy` 派生＋専用プール＋見た目マーカー）の構造は `BombTrooper` / `ShieldTrooper` に転用した。パラメータを 0 にして無効化する方式は展示事故の温床になるため採らない。
  - **結果のシーン受け渡し**: `GameResultHolder`（static のみ・MonoBehaviour にしないので ADR-5 の Singleton 方針は不変）に `StageResult` を置き、**クリア→`ResultScene` / 失敗→`GameOverScene`** へ遷移する（従来は勝利時のみ遷移し、敗北時は TH に留まっていた）。TH の HUD と遷移先の両方で結果を出す。
  - **演出はプレースホルダ優先**: 爆発は `ExplosionRing`（敵の足元から赤い円が広がる簡易版）、ビームは画面の白フラッシュ。正式な VFX は別担当が作るため、差し替え箇所を `ExplosionRing.cs` ＋ `GameManager.CreateExplosionRing()` の 2 箇所に閉じてある。モデルは既存の `EnemyBombTrooper.prefab` / `EnemyShieldTrooper.prefab`。**→ ADR-31 で `main` の正式演出に差し替え済み（プレースホルダは削除）。**

- **隊列の調整**: 1 方位に並ぶのは最大 **2 段**（`LidarPreviewConfigSO._queueMaxDepth` 3→2）、前後の間隔は **1.8**（`_queueSpacing` 1→1.8）。間隔の上限は隣の列との距離（2π×8÷24 ≒ 2.09）÷1.05 ≒ 1.99 で、これを超えると前列判定（`_overlapRadius = max(overlap, spacing×1.05)`）が隣の列まで拾って撃破できなくなる。2 段目は半径 9.8 に立つので、画面の縦方向が狭いと上下の端で見切れる（静止半径か間隔で調整）。

- **不変（覆さない）**: ADR-29 の LiDAR 連続座標入力・放射状の雪玉ストリーム・d 連動ギミック、`EnemyField` の 360度スポーンと隊列（列×段）・繰り上がり、`EnemyKillSelection.SelectFront`、`Enemy`/`Fort`/`Projectile`/`ProjectileFactory`、プール（ADR-4）、入力抽象 `IInputSource`（ADR-6）、Singleton は `GameManager` のみ（ADR-5）、調整値は SO（ADR-1）、Core は UnityEngine 非依存（ADR-8）、物理不使用。敵の挙動も現状維持（入場歩行 → 静止半径で停止 → 砦へ遠隔射撃）で、圧力は「倒し損ねた敵の同時火力の蓄積」が担う。

- **検証状況**: Core 純ロジックは EditMode を新設（`ComboTracker` 9 / `SpecialGauge` 8 / `KillProgress` 7 / `ExplosionChain` 9 / `StageResult` 5 / `SpawnSelector` 7 / `States` 10）。当初は `csc` によるオフライン型チェックのみだったが、**ADR-31 のマージ時（2026-09-17）に Unity Editor で EditMode 192 本緑・Play 検証（連鎖・ビーム自動発動・雪崩撃破・敗北遷移）を実施済み**。

- **昇格条件（残作業）**: ①盾耐久・コンボ・クリア遷移の Play 検証 ②ゲージ係数の現物合わせ（1ゲーム約2回MAX）。（`StageConfig.asset` 等の作成、`GameOverScene` 配置、EditMode 全件緑は完了。）

## ADR-31: `main` の演出（爆発テレグラフ・ビーム砲・パーティクル雪崩）を ADR-30 のロジックに接続する（ロジック＝`64-vansuv`、演出＝`main`）— Accepted（`64-vansuv` ← `main` マージ・2026-09-17）

- **背景**: ADR-30（`64-vansuv`）が 3Wave 制を廃してゲーム性を刷新している間に、`main` では**旧ロジックのまま**同じ題材（爆発兵の巻き込み・必殺ビーム砲・雪崩）の**演出**が独立に実装されていた（PR #69 砲台 / #71 雪崩パーティクル / #74 ゲームオーバー遷移 / #78）。両者は同じ機能を別設計で持つため、PR #75 で `GameManager` / `EnemyField` / `UIManager` / `LidarAvalancheVfx` / `AllyNPC` / `TH_Stage.prefab` が衝突した。「どちらの設計を残すか」を機能ごとに決めた記録。

- **決定（大方針）**: **ゲーム構造・爆発判定・ビーム発動条件・敵プールは `64-vansuv`（ADR-30）を正**とし、**爆発・ビーム砲・雪崩の見た目は `main` を正**とする。`main` 側で演出と一体だったロジック（`KillTally`＋撃破数閾値で発動、`KillAllWithin` の再帰的巻き込み、`EnemyTypeSO` キーの Dictionary プール、`IceStock` ゲート、味方 NPC の微修正）は採らない。

- **決定（機能ごと）**:
  - **爆発**: 巻き込み・誘爆の判定は `ExplosionChain.Resolve`（純ロジック・1 フレームで確定）のまま。`EnemyField.KillWithExplosion` は爆発した各 `BombTrooper` のプレハブに付いた `ExplosiveDeath` を **見た目だけ**再生する（`Explode(radius)`。演出側の「爆発した瞬間に撃破を頼む」コールバックは使わない）。演出の出所を GameManager からの `Func<T>` 注入に統一する案は退け、`main` の「プレハブに演出が閉じる＝VFX 担当が Unity 上で差し替えられる」利点を残した（`Instantiate/Destroy` は爆発時のみで稀なので ADR-4 の例外とする）。
  - **範囲の正は 1 か所**: 爆発半径は `StageConfigSO.ExplosionRadius`、ビームの半径は `EnemyField.EntranceRadius + SpecialAttackConfigSO.RingRadiusPadding`。`ExplosionTelegraph.PlayImmediate(radius, onExplode)` を追加してロジック側が半径を渡す（プレハブ焼き込みの `_radius` と `main` が使っていたリフレクション上書きは廃止）。「見えている範囲＝効く範囲」を常に保つため。
  - **ビーム砲**: 発動条件は `SpecialGauge`（ゲージ MAX で自動・ADR-30）。`SpecialAttackController` は演出の進行役に純化し、`Fire()` で **砲出現 → 光線が一周（0.85s）→ 遅延（0.35s）→ 衝撃波の瞬間に `EnemyField.KillAllForBeam()` で盤面全員撃破 → 衝撃波が消えたら砲を隠す**。撃破を「ゲージ満タンの瞬間」ではなく「衝撃波の瞬間」に遅らせる（約 1.2 秒は敵が撃ち続ける＝溜めとして受け入れる）。撃破範囲は ADR-30 どおり**全員（入場中も含む）**とし、衝撃波の見た目を入場口まで届かせる（縮めたい場合は `RingRadiusPadding` を負に。2 段目の半径 9.3 を下回らないこと）。演出中（`IsPlaying`）は GameManager が次の発動を待たせる。
  - **雪崩**: `main` の `AvalancheSnowEffect`（検知点の真上にパーティクルを 1 個生成し外向きに流す。撃破は滑走中の位置で半径判定・1 個 1 撃＝旧 `Snowball` と同じ契約）を採用。`IceStock` ゲートは撤去（ADR-30）。ADR-29 の「角度ばらつき（spread）」は雪玉の放出方向に依存した仕組みだったため、粒子の広がりに置き換わる形で消える（`LidarPreviewConfigSO.SpreadDegFor` は未参照）。
  - **調整値の置き場**: 演出の数値（光線の秒数・色・輪の太さ・衝撃波までの遅延・砲の位置・半径余白）は `SpecialAttackConfigSO`（`Assets/ScriptableObjects/SpecialAttackConfig.asset`）に残し、ゲーム性の数値（ゲージの溜まり方・爆発半径・盾耐久）は `StageConfigSO`。担当（演出／ゲーム性）で触るファイルを分ける。
  - **取り込んだ `main` の改善**: 敵を砦向きに **30° 前傾**させて置く（真上からのプロジェクタ投影で種類が見分けられるように。`LidarPreviewConfigSO.EnemyTiltDeg`）。敵の弾（`Projectile`）は**非表示**（敵に攻撃モーションが付いたため。被弾は砦 HP バーと Hit 音で伝える）。決着後の遷移秒数を**クリア `_resultLimit`（3s）／ゲームオーバー `_gameOverLimit`（1s）**に分離（PR #74 の判断を値として採用。仕組みは ADR-30 の `GameResultHolder` 経由）。
  - **削除**: `ExplosionRing`（仮の赤い球）、`BeamCannon`（白フラッシュ）、`KillTally`＋テスト、`SpecialAttackConfigSO.KillThreshold`、`AllyNPC`（`main` 側の微修正ごと）、`EnemyTypeSO.Prefab`（`main` の Dictionary プール用・未使用）。
  - **`TH_Stage.prefab`**: コンフリクトは `64-vansuv` 側で解決し、`main` 由来の参照（`BeamCannon.prefab` / `SpecialAttackShockwave.prefab` / `snow_effect.prefab` / `SpecialAttackConfig.asset`）は **Editor 経由**で後付け（YAML 手編集はしない）。
  - **カメラシェイク**: `ExplosionTelegraph` のシェイク基準位置を static で共有し、複数の爆発（連鎖・衝撃波との重なり）が同時に走っても最後の 1 つが終わるまで基準を戻さない（個別に基準を控えると揺れた途中の位置を基準にして戻り先がズレる）。

- **不変（覆さない）**: ADR-30 の全決定（単一ステージ・`KillProgress`／`ComboTracker`／`SpecialGauge`／`StageResult`・爆発兵＋盾兵ペア・固定 3 プール）、ADR-29 の LiDAR 連続座標入力、Singleton は `GameManager` のみ（ADR-5）、Core は UnityEngine 非依存（ADR-8）、物理不使用。`main` から入った `Enemy` の攻撃モーション（Walk/Attack の Animator 制御）と検証用シーン `TH 1` / `TH2` はそのまま。

- **検証**: Unity 同梱 Roslyn＋Bee の rsp で 3 アセンブリのオフライン型チェック（エラー 0）→ Editor（Unity-MCP）で EditMode **192 本緑** → Play で①爆発兵 1 体撃破から 3 体誘爆・巻き込み 5 体（ログ `[Explosion] 爆発 3 回 / 巻き込み 5 体 (半径 4.5)`）と赤いテレグラフの表示 ②ゲージ MAX → 砲出現 → 光線掃引 → 衝撃波で 14 体全滅 → ゲージ 0・砲非表示 ③パーティクル雪崩で同列 2 段の撃破 ④砦 HP 0 → 1 秒後に `GameOverScene`（到達撃破数・最大コンボの受け渡し）を確認。

- **残課題**: ①ビームの衝撃波の半径（12）が Game View ではほぼ画面外。実機の投影画角で `RingRadiusPadding` を現物合わせ ②盾耐久・コンボ・クリア遷移の Play 検証（ADR-30 の残作業）③`docs/phases.md` Phase 8 の VFX 項目を「済」に更新（本 ADR で対応）。

---

## ADR-32: 雪崩の当たり判定を「粒そのもの」にする（ParticleContact）＋分岐点の判定を Legacy として Inspector 切替 — Accepted（`76-avalanche-hitbox`・2026-09-18）

- **背景**: ADR-31 のパーティクル雪崩は、見た目（Cone 半径 2・半角 25°・粒が地形で跳ねる）と当たり判定（4 u/s で外へ歩く点の半径 1.5＝一定幅 3.0 の帯）が別の形で、静止線付近では見た目が幅 6〜7 に広がるため「雪が当たっているのに倒れない」帯が出る。粒（10 u/s）と判定点（4 u/s）の速度差で「届いてから 1 秒以上遅れて倒れる」ズレもある。
  当初は「見た目を判定に合わせる」（扇の形を判定から導き、はみ出た粒を消す）方針で実装・Play 検証したが、幅を厳密に守ると雪が細い一筋になり、粒の 88% を消すことになった（物理の bounce が「流れ下る」推進力なので下げると届かなくなる）。判定を見た目に合わせる方向へ転換した。

- **決定**:
  - **ParticleContact モード（既定）**: 毎フレーム雪崩の粒の実位置を走査し、粒の XZ 距離が **敵の半身（`EnemyBodyWidthUnits`/2）＋粒の半径（Prefab の `startSize` 最大 × スケール ÷ 2）** 以内、かつ Y が敵の足元 ± `ContactHeightUnits` の中なら接触。接触した粒は**吸収されて消え**（寿命 0）、敵に 1 ダメージ。耐久は `EnemyTypeSO.ParticleHitPoints`（種別ごと・既定 1。盾兵は 150 から現場調整）。耐久が尽きれば `KillOne`（爆発兵は連鎖）。粒が吸収されるので前列が生きている間は後列に雪が届かず、前列ルールをコードで持たなくても前から倒れる。クールダウン・1 雪崩 1 撃の制限は持たない（大量撃破は爽快感として許容。難易度は敵の出現数で調整する）。落氷点が敵の静止半径 `RestRadius` より外（＝敵の上に直接）なら雪崩を起こさない（演出・判定・雪崩音なし。「山から雪を転がして雪崩を起こす」ゲーム概念に沿う）。白塗り演出（`OnAvalanchePassed`）は「最も遠くまで届いた粒」の位置で駆動する。**氷が落ちなくなったら雪崩も止まる**：吹き出し口を固定したことで Legacy の「r=10 到達で打ち切り」が効かず最後の雪崩が Prefab の 2 s 吹き続けたため、`IsEmitting` の立ち下がりで活性中の全雪崩に `StopEmitting` を送り、粒が尽きたらプールへ返す（停止後 0.1 s で吹き出し停止・1.1 s で消滅）。
  - **Legacy モード**: 分岐点（`main` 0d047c8）の挙動そのまま。歩く判定点・`KillRadiusUnits`・前列ルール（`EnemyKillSelection`）・d 連動クールダウン・盾兵は `StageConfigSO.ShieldHits`（雪崩 1 個 = 1）。コードもデータも無改変で残し、比較の基準にする。
  - **切替**: `LidarPreviewConfigSO.HitMode`（Inspector）。`LidarAvalancheVfx` が毎フレーム読むので Play 中でも切り替わる。
  - **純ロジック**: `Gameplay/AvalancheContact.FindHit`（`EnemyKillSelection` と同じ流儀・EditMode 8 本）。`EnemyField.TryAbsorbParticle` はフレームごとに敵スナップショットを 1 回作り、最内半径より内側の粒は早期棄却、同フレームの撃破後は生存フラグだけ同期し直す。吸収の手応え（パフ・SE）は 0.1 s に 1 回へ間引く。
  - **耐久の置き場**: ParticleContact の耐久は `EnemyTypeSO`（種別の性質と同じ場所・通常敵/爆発兵も Inspector で変更可）、Legacy の盾耐久は `StageConfigSO.ShieldHits` のまま。モードで単位が違う（雪崩の個数 vs 粒の個数）ため 1 フィールドで兼ねない。

- **不採用**: ①判定を見た目に広げる（当初「倒しすぎ」を懸念して却下したが、最終的に方針転換で吸収）②見た目を判定に合わせる（扇の形の導出 `AvalancheFan`・固定判定点・先頭 1 粒到達・包絡線の強制。Play 検証で雪が細い一筋になり不採用。探索コミットは破棄）③粒 N 個の到達しきい値（`ShieldHits` と単位が二重になる）④粒が敵を通過する（盾兵の存在意義が消える）。

- **検証**: EditMode 207 本緑（`AvalancheContactTests` 8 本追加）。Unity-MCP で TH を Play、位置ファイルへ `seq,x,y,1` を書く模擬トラッカーで ①ParticleContact・中腹 r=5：雪崩の通り道の敵が触れた順に倒れ、52〜63 秒で 100 体クリア、FPS 175（粒 4,300 個の走査で性能問題なし）②Legacy へ実行時切替：判定点が r=9.9 まで歩き `TryKillNearest` で撃破 ③境界外 r=9：`IsEmitting=false`・撃破 0、を確認。

- **残課題**: ①`EnemyBodyWidthUnits` と `ShieldTrooper.asset` の `ParticleHitPoints` を現場で実測・調整（盾兵は前の爆発兵の爆発でほぼ倒れるため、粒耐久が効く場面は稀）②境界付近で落氷点が揺れると雪崩が点滅しうる（必要なら余裕幅）③~~Unity 側の位置ファイル既定パス（`tools/lidar-tracker/out/position.txt`）と `tracker1p3.py` の出力先（`tools/lidar-tracker1+3/out/position.txt`）が違う~~ → ADR-33 で Inspector 切替に解消。

## ADR-33: Unity が読む LiDAR 共有ファイルを「トラッカーの選択」で切り替える（`LidarPreviewConfigSO.Tracker`）— Accepted（`83-changeLiDARtoUnity`・2026-09-18・Issue #83）

- **背景**: `tools/` には出力契約（`seq,x,y,shaving`）が同じ LiDAR トラッカーが 3 つある（`lidar-tracker`＝T-mini 単体 / `lidar-tracker3`＝S1 単体 / `lidar-tracker1+3`＝T-mini + S1 ペアの束ね・展示本番）。どれも自分の `out/position.txt` に書く。一方 Unity 側は `LidarPreviewConfig.asset` の `_positionFileRelative`（文字列）が `tools/lidar-tracker/out/position.txt` を指したままで、`tracker1p3.py` を動かしても Unity が読めない（ADR-32 残課題③）。文字列パスの手入力は typo とフォルダ名の記憶に依存し、現場で切り替えにくい。
- **決定**: `LidarTrackerKind` enum（`LidarTracker` / `LidarTracker3` / `LidarTracker1Plus3` / `Custom`）を `LidarPreviewConfigSO._tracker` として Inspector の先頭に置き、`PositionFileRelative` は `ResolveRelativePath(kind, customPath)`（純関数・static）で解決する。プリセットは各トラッカーの既定出力先 `tools/<dir>/out/position.txt` に固定し、`Custom` のときだけ従来の `_positionFileRelative` 文字列を使う。既存アセットは `_tracker` を持たない＝値 0＝`LidarTracker` に解決するので追加だけでは挙動が変わらない（後方互換）。`LidarPreviewConfig.asset` は Editor 経由で `LidarTracker1Plus3` に設定した（展示本番の構成）。`LidarPositionInputSource.Configure` はトラッカー名と解決パスをログに出し、Editor でファイルが無ければ警告する（トラッカー未起動・選択違いを起動直後に気付けるように）。
- **変わらないもの**: 解決順の上位（環境変数 `KAKIGORI_POSITION_FILE`）とビルド時の `persistentDataPath/position.txt` はそのまま。Python 側の `config.toml [output] position_file` の解決順も無改変。`InputMode.Vision`（旧 `VisionInputSource` / `VisionConfigSO`）はこの選択の対象外（旧経路・`seq,channel,shaving` 契約）。
- **不採用**: ①環境変数で揃える（両プロセスの起動手順に依存し、展示当日の手順が増える）②Python 側の出力先を全ツールで 1 箇所に統一する（各ツールを単独でも動かすので、他ツールの出力を上書きし合う事故が起きる）③トラッカー選択を `GameManager` の `[SerializeField]` に置く（パスは LiDAR 設定の一部なので `LidarPreviewConfigSO` に同居させた方が現場で 1 画面に収まる）。
- **検証**: EditMode `LidarPreviewConfigSOTests` に 3 本追加（プリセット 3 種の解決 / Custom の透過 / 既定値の後方互換）、計 214 本緑（Unity-MCP `tests-run`・全件）。

