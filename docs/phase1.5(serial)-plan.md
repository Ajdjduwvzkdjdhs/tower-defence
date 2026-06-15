# Phase 1.5 実装計画書（Serial / 実機シリアル接続の足場）

> 本書は `spec-summary.md` / `architecture.md` / `phases.md` / `coding-conventions.md` / `mcp-guide.md` を正本とし、**Phase 1 と Phase 2 のあいだに挟む橋渡し（別計画）** として、物理かき氷機との**シリアル通信**を `IInputSource` 抽象の差し替えで導入する計画を、検証可能な単位（ユニット）に分解したもの。完了判定の bar は本書 §7 受け入れ基準。
>
> 位置づけの根拠: `phases.md` の Phase 3 Out of scope に **「実機インターフェース接続（別計画）」** と明記されており、本書がその「別計画」にあたる。Phase 1（完了・main マージ済み）を覆さず、Phase 2（味方NPC）に着手する前の足場として実施する。

## 0. 方針

- **目的**: ADR-6（入力 Strategy `IInputSource`）の設計どおり、実機接続を「`IInputSource` 実装の差し替え」で実現する第一歩。**ハンドル回転（削り）だけをシリアル化**し、谷選択（XY）と決定は従来どおりキーボードで読む。
- **入力統合 = Composite（併用）**: キーボードとシリアルを同時に読む。実機が無くてもキーボードで開発・検証でき、接続時はそのまま両対応。
- **シリアル実装は自前**（外部アセット Ardity 等は不採用）。`System.IO.Ports.SerialPort` を別スレッドで読むパターンを採用する（参考: Qiita「Unity と Arduino のシリアル通信」 https://qiita.com/hoktist/items/8e3ae56fd5f12c93dea8）。自前にすることで 3rd-party の `Assembly-CSharp` 参照境界問題を避け、コードを `KakigoriTD.Runtime` 内に収める。
- **送信（Unity→Arduino）は汎用送信口のみ**用意する（双方向化）。「何を・いつ送るか」の具体は本フェーズでは未定とし、将来の consumer が配線する拡張ポイントに留める。
- **検証の二層**:
  - **EditMode テスト**（高速・自動）: 受信文字列の解釈（`SerialInputProtocol`）と Composite マージ（`CompositeInputSource`）の純ロジック。
  - **Unity-MCP / smoke**（`screenshot-game-view`・Console ログ）: シリアル接続・スレッド後始末・実機（またはテスト Arduino）での削り反映。`mcp-guide.md` のパターンに沿う。
- 鉄則を踏襲: Singleton は `GameManager` のみ（ADR-5）／入力は `IInputSource` 経由（ADR-6/10）／調整値は SO（ADR-1）／購読は `OnDestroy` で解除（ADR-3）／純ロジックは MonoBehaviour から分離（ADR-8）。

## 1. 入力マッピング（このフェーズの肝）

| ゲーム信号 | 取得元 | `IInputSource` メンバ | 備考 |
|---|---|---|---|
| 谷選択 N/W/S/E | キーボード WASD（既存 `KeyboardInputSource`） | `ActiveChannel` | 変更なし（実機の XY センサ化は将来フェーズ） |
| 決定/開始/再挑戦 | キーボード Enter（既存） | `ConfirmPressed` | 変更なし（エッジ） |
| 削り（ハンドル回転） | **自前シリアル受信**（`SerialInputSource`）＋ KB `Space`（dev フォールバック） | `IsShaving` | **シリアルで来るのは実質これだけ** |

仕様書 §4 の対応表（`W/A/S/D` ↔ かき氷機 XY 座標 / `Space` 長押し ↔ ハンドル回転検知）に忠実。

**`CompositeInputSource` のマージ規則**:

- `ActiveChannel` = キーボード（シリアル側は常に `-1` を返す）
- `ConfirmPressed` = キーボード（シリアル側は常に `false`）
- `IsShaving` = **論理 OR**（ハンドル回転 OR `Space` 長押し）→ 実機が無くても `Space` で削れる

## 2. コンポーネント一覧

| # | クラス / 成果物 | フォルダ・名前空間 | 責務 | 依存 | 検証 |
|---|---|---|---|---|---|
| S1 | `SerialConfigSO` | Data / `KakigoriTD.Data` | `portName`・`baudRate`・`readTimeout`・任意の停止コマンド文字列を SO 化（macOS ポート名／マジックナンバー禁止 ADR-1） | なし | MCP(資産) |
| S2 | `SerialHandler` | Input / `KakigoriTD.Input`（MonoBehaviour） | **双方向 transport**。受信: `SerialPort` を別スレッドで `ReadLine()` し `OnDataReceived(string)` 発火。送信: `Send(string)`（スレッドセーフな `WriteLine`・汎用）。スレッド/ポートを `OnDestroy`/`OnApplicationQuit` で安全解放 | S1 | MCP/smoke |
| S3 | `SerialInputProtocol` | Input / `KakigoriTD.Input`（**純 C#**） | 受信文字列 → 削り `bool` ＋ watchdog（無受信タイムアウトで `false`） | なし | EditMode |
| S4 | `SerialInputSource` | Input / `KakigoriTD.Input`（MB: `IInputSource`） | `SerialHandler.OnDataReceived` を購読し `SerialInputProtocol` へ。`Update` で `IsShaving` を反映。`ActiveChannel`=-1 / `ConfirmPressed`=false。`OnDestroy` で購読解除 | S2,S3 | MCP/smoke |
| S5 | `CompositeInputSource` | Input / `KakigoriTD.Input`（**純 C#**: `IInputSource`） | キーボードとシリアルの 2 つの `IInputSource` を §1 規則でマージ | `IInputSource` | EditMode |
| S6 | `GameManager`（改修） | Bootstrap / `KakigoriTD.Bootstrap` | `KeyboardInputSource` + `SerialInputSource`(+`SerialHandler`) を生成し `CompositeInputSource` で包んで配線（`_input` を Composite に差し替え） | 全部 | MCP(通し) |

純ロジック（S3/S5）は MonoBehaviour から分離して EditMode 化（ADR-8）。`SerialHandler` / `SerialInputSource` は Unity・スレッド・シリアルに密結合する薄いアダプタに徹する（ADR-11 と同方針）。

### 参考 Qiita 記事との対応（どのクラスが記事のどの役割を担うか）

| Qiita 記事のクラス | 記事での役割 | 本計画での担当 |
|---|---|---|
| `SerialHandler`（MB） | `SerialPort`＋別スレッド＋`OnDataReceived` 発火（transport） | **`SerialHandler`**（同名・同役割を踏襲。送信 `Send` を追加） |
| `SerialManager`（MB） | `OnDataReceived` を購読・受信を解釈してアプリ状態へ／Arduino への送信（`usendmsg`/`dsendmsg`・`cansend` 重複抑制） | 受信解釈: **`SerialInputSource`**（購読・ライフサイクル・`Update` 反映）＋ **`SerialInputProtocol`**（解釈＝純ロジック・EditMode）に分割。送信: **`SerialHandler.Send(string)`**（汎用送信口）。送信トリガと `cansend` 的な重複抑制は本フェーズ未定＝将来の consumer 側 |

- 記事の単一 `SerialManager` を、ADR-8 に従い **薄い MB アダプタ（`SerialInputSource`）＋ 純ロジック（`SerialInputProtocol`）** に分割し、解釈部を EditMode で検証可能にする。
- 記事の送信責務は本フェーズでは **`SerialHandler.Send(string)` の汎用送信口のみ**を用意し、「何を・いつ送るか」は拡張ポイントとして残す（`SerialFeedbackSender` のような送信 consumer は今回作らない＝TODO）。

## 3. `SerialHandler` の要点（記事準拠・プロジェクト規約合わせ）

- `new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One)` → `Open()`、`ReadTimeout = readTimeout`（例 20ms）。
- **別スレッドで読み取り**: `isRunning_` フラグ＋`while` ループで `ReadLine()`。`TimeoutException` は握りつぶしてループ継続（メインスレッドを固めない）。
- **スレッド境界の安全策**: 読み取りはバックグラウンドスレッドで行い、**Unity API には触れない**。受信文字列は `volatile`／ロックで保持し、**Unity メインスレッドの `Update`（`SerialInputSource`）で消費**する（既存 `KeyboardInputSource` の `Update` ポーリングと同じ流儀＝`IInputSource` のフレーム単位契約に合う）。
- **後始末（必須）**: `OnDestroy`/`OnApplicationQuit` で `isRunning_=false` → `thread_.Join()` → `serialPort_.Close()/Dispose()`。Editor の Play 停止でポートを掴みっぱなしにせず、フリーズを防ぐ（記事の注意点）。必要なら直前に停止コマンド（`SerialConfigSO` の停止文字列）を `Send` する。
- **送信（汎用）**: `public void Send(string line)` で `serialPort_.WriteLine(line)` を実行（ポート open 判定＋lock でスレッドセーフ）。送信内容/トリガは本フェーズ未定（拡張ポイント）。
- **イベント購読解除**: `OnDataReceived` の購読は購読側（`SerialInputSource`）の `OnDestroy` で必ず解除（ADR-3 の鉄則）。

## 4. シリアルプロトコル（暫定・実機で調整可）

- 改行区切り行（`ReadLine()` / `WriteLine()`）。**双方向**だが、受信（Arduino→Unity）が主、送信（Unity→Arduino）は汎用口のみ。
- **受信**: `1` = ハンドル回転中（削り）/ `0` = 停止。**状態変化時に送信＋約 200ms ごとの heartbeat**（取りこぼし自己修復）。
- **Unity 側 watchdog**: 一定時間（例 500ms）無受信なら `IsShaving=false` に倒す（切断時の「削りっぱなし」防止＝fail-safe）。`SerialInputProtocol` が最終受信時刻を保持して判定する。
- 回転 → bool 変換は **Arduino ファーム側**が持つ（Unity 側を単純・テスト可能に保つ）。将来拡張: 回転速度を送って削り強度を可変にする（本フェーズ未実装）。
- **送信（Unity→Arduino）**: フォーマット・送信タイミングは**本フェーズ未定**。`SerialHandler.Send(string)` の汎用口のみ用意し、検証は「任意文字列を送ると Arduino 側で受信できる」ことの smoke まで（具体コマンド体系は将来フェーズで確定）。
- 付録（§10）に**テスト用 Arduino スケッチ**を同梱（受信側: ボタン/エンコーダ → `1`/`0` 送出＋heartbeat。送信検証側: Unity から受けた行を Serial Monitor へエコー）。実機かき氷機が無くても、削り受信パスと送信パスを bench 検証できる。

## 5. 前提・環境

- **API Compatibility Level**: ⚠️ 当初「`apiCompatibilityLevel: 6` ＝ .NET Framework で変更不要」と見積もったが**誤り**。`6` は **.NET Standard 2.1** で `System.IO.Ports` を含まず、コンパイルで `CS0234`（`System.IO.Ports` が無い）になる。**実装時に Standalone の API Compatibility Level を .NET Framework（`NET_Unity_4_8`＝3）へ変更**した（`apiCompatibilityLevelPerPlatform: {Standalone: 3}`）。記事／Ardity PDF FAQ② の前提どおり。詳細は ADR-15。
- **3rd-party import 不要**: 自前コードは `KakigoriTD.Runtime.asmdef` 内に置く。`System.IO.Ports` は BCL 名前空間で asmdef からそのまま使える（Ardity 採用時に生じる `Assembly-CSharp` 参照問題が無い）。
- **macOS ポート名**: 記事の `COM4` は Windows 表記。macOS は `/dev/tty.usbserial-*` / `/dev/tty.usbmodem*`。`SerialConfigSO.portName` で設定し環境差を吸収する。
- **注意（Mono の癖）**: Unity/Mono の `System.IO.Ports` は macOS で挙動に癖がある場合がある（`ReadTimeout` の効き・列挙不可など）。実機検証時に問題が出たら本書 §9 に追記する。

## 6. 実装ユニットと順序

各ユニットは「完了条件」を満たした時点で次へ進む。コミットはユニット単位（プロジェクト方針）。

- **Unit S0: `SerialConfigSO` ＋ `SerialHandler`（双方向 transport）**
  - 完了条件: コンパイル通過（`assets-refresh`）。実機 or テスト Arduino 接続時に `OnDataReceived` でログ受信でき、`Send(string)` で送信できる。**未接続でも例外で Editor が固まらない**（スレッド・タイムアウト・後始末が正しい）。
- **Unit S1: `SerialInputProtocol`（純 C#）**
  - 完了条件 (EditMode): `1`/`0` 解析で `IsShaving` 切替 / 不正行は無視 / **watchdog タイムアウトで `IsShaving=false`** / 連続受信で状態保持。
- **Unit S2: `CompositeInputSource`（純 C#）**
  - 完了条件 (EditMode): `ActiveChannel`/`ConfirmPressed` はキーボード値を返す / `IsShaving` はキーボード OR シリアルの論理和になる（スタブ 2 実装でマージ検証）。
- **Unit S3: `SerialInputSource`（MB）＋ `GameManager` 配線改修**
  - 完了条件: `GameManager` が `Composite(Keyboard, Serial)` を生成・配線。**既存キーボード操作が回帰しない**（Play で `Space` 削り・WASD 谷選択・Enter 確定）＋ シリアル smoke（接続/未接続）。
- **Unit S4: 統合検証**
  - 完了条件 (MCP Play): テスト Arduino（または実機）で `1`/`0` を送出 → `W/A/S/D` で谷選択しながら回転信号で雪崩が走る／止まることを `screenshot-game-view` で実証。ハード無の場合は `Space` 併用で回帰確認のみ（実機ライブ検証は §9 に TODO 化）。

## 7. 受け入れ基準（完了判定の bar）

- [x] `CompositeInputSource` がキーボード+シリアルを §1 規則で正しくマージ（EditMode 緑：5本）
- [x] `SerialInputProtocol` が削り行を解析し、無受信 watchdog で `IsShaving=false`（EditMode 緑：11本）
- [x] `SerialConfigSO` で `portName`/`baudRate` 等を Inspector 調整可（マジックナンバー直書きなし）
- [x] `SerialHandler` がスレッド/ポートを安全に開閉（`OnDestroy`/`OnApplicationQuit`）／**未接続ポートでも警告のみで落ちない・固まらない**（MCP smoke 検証済み）
- [ ] `SerialHandler.Send(string)` で Unity→Arduino へ任意文字列を送信できる（**接続時 smoke はハード入手後 = TODO**）／未接続でも落ちない（検証済み）
- [x] **キーボード操作が回帰しない**（`_input`＝`CompositeInputSource`、`AvalancheController` に同一注入、Play で KB→Composite の `IsShaving` OR を実証）
- [ ] ハンドル回転（または テスト Arduino の `1`/`0`）で `IsShaving` が切り替わり、選択中の谷に雪崩（Play + screenshot）（**ハード入手後 = TODO**）
- [x] Singleton は `GameManager` のみ・入力は `IInputSource`（Composite）経由を維持（ADR-5/6）
- [x] `docs/architecture.md` に **ADR-15** を追記、`docs/phases.md` に Phase 1.5 エントリを追加、関連 docs を更新（Definition of Done）
- [x] Standalone の API Compatibility Level を .NET Framework（`NET_Unity_4_8`）へ変更（`System.IO.Ports` の前提・§5/ADR-15）

## 8. ADR-15 ドラフト（実装時に `docs/architecture.md` へ追記）

> **ADR-15: 実機シリアル接続は自前 SerialPort（別スレッド・双方向）＋ Composite で IsShaving を供給（ADR-6 を拡張）— Proposed**
> - **背景**: ADR-6 の `IInputSource` を実機に差し替える第一歩。物理かき氷機のハンドル回転をシリアルで取得する。XY（谷選択）は本フェーズではキーボード据え置き。Ardity 等の外部アセットは使わず `System.IO.Ports` で自前実装（参考: Qiita hoktist 記事）。Arduino への送信口も将来用に確保する。
> - **決定**: `SerialConfigSO`（port/baud/timeout/停止コマンド）、`SerialHandler`（**双方向**: `SerialPort` を別スレッドで `ReadLine`＋`OnDataReceived` 発火／`Send(string)` で `WriteLine`、`OnDestroy`/`OnApplicationQuit` で `thread.Join`＋`Close/Dispose`）、純ロジック `SerialInputProtocol`（行→shave＋watchdog, EditMode）、薄い MB アダプタ `SerialInputSource`（`IsShaving` のみ供給）、`CompositeInputSource`（`ActiveChannel`/`ConfirmPressed`=キーボード, `IsShaving`=OR）。`GameManager` が Composite を配線。**送信の具体トリガ／コマンド体系は本フェーズ未定（汎用 `Send` 口のみ、将来 consumer が配線）**。`apiCompatibilityLevel` は .NET Framework のまま。
> - **結果**: 外部依存なしで、実機が無くてもキーボードで全操作可能、接続時は回転で削れる＋Unity→Arduino 送信路を確保。ADR-6/10 を覆さず拡張。実機 XY センサのシリアル化・送信コマンド確定は将来フェーズ。

## 9. 要確定事項（着手前に解消 or 既定値で進行）

| 項目 | 推奨既定 | 影響 |
|---|---|---|
| プロトコル詳細 | 受信 `1`/`0`＋heartbeat＋500ms watchdog（暫定） | 実機ファームと突き合わせ |
| スレッド→メイン受け渡し | `volatile`/lock ＋ `Update` 消費 | スレッド安全性 |
| macOS ポート名 | `SerialConfigSO` で設定 | 環境差吸収 |
| 送信の具体トリガ/コマンド | 本フェーズ未定（汎用 `Send` のみ） | 将来 consumer で確定 |
| 実機ライブ検証 | ハード入手後に実施（TODO） | §6 Unit S4 の達成度 |

> スコープ厳守: 味方NPC・変装・突撃隊（Phase 2/3）、実機 **XY センサ**のシリアル化、送信コマンド体系の確定は **Phase 1.5 では実装しない**。本フェーズはハンドル回転→`IsShaving` の受信と、汎用送信口の確保までを足場として通す。

## 10. 付録: テスト用 Arduino スケッチ（実機かき氷機が無くても検証可能）

実機のハンドル回転センサの代わりに、押しボタン（または手回しエンコーダ）で削り信号 `1`/`0` を送る最小スケッチ。Unity からの送信行は Serial Monitor へエコーして送信パスも確認できる。

```cpp
// 削り信号テスター: ボタン押下中=1（削り）/ 離す=0。約200ms heartbeat。
// Unity から届いた行はそのままエコー（送信パスの smoke 用）。
const int BUTTON_PIN = 2;     // INPUT_PULLUP（押下で LOW）
const unsigned long HEARTBEAT_MS = 200;
unsigned long lastBeat = 0;
int lastState = -1;

void setup() {
  Serial.begin(9600);
  pinMode(BUTTON_PIN, INPUT_PULLUP);
}

void loop() {
  int shaving = (digitalRead(BUTTON_PIN) == LOW) ? 1 : 0;

  // 状態変化 or heartbeat で送信
  if (shaving != lastState || millis() - lastBeat >= HEARTBEAT_MS) {
    Serial.println(shaving);
    lastState = shaving;
    lastBeat = millis();
  }

  // Unity からの受信をエコー（Send(string) の検証用）
  if (Serial.available() > 0) {
    String line = Serial.readStringUntil('\n');
    Serial.print("echo:");
    Serial.println(line);
  }

  delay(10); // バッファあふれ防止（記事の注意点）
}
```

---

### 検証（本フェーズ実装時の進め方）

1. EditMode: `SerialInputProtocol` / `CompositeInputSource` のテストを緑にする（純ロジック）。
2. Unity-MCP: `SerialHandler` を載せた Play Mode で、未接続時に警告ログ・固まらないこと、接続時に `OnDataReceived` 受信と `Send` 送信を Console/Serial Monitor で確認。
3. Unity-MCP: `W/A/S/D` 谷選択＋（テスト Arduino の）回転信号で雪崩が走る／止まるを `screenshot-game-view` で記録。
4. キーボードのみでの回帰（`Space` 削り）も確認し、Composite が両対応であることを担保。
5. 完了後、`docs/architecture.md` に ADR-15、`docs/phases.md` に Phase 1.5 エントリを追記。

参考: https://qiita.com/hoktist/items/8e3ae56fd5f12c93dea8 （自前シリアル実装の参照元）
