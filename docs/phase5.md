# Phase 5 実装計画書（かき氷機の位置検知連携 — 画像処理→ファイル→Unity）

> 本書は `docs/architecture.md`（**ADR-6** 入力 Strategy・**ADR-8** 純ロジック分離・**ADR-15** 実機シリアル＋Composite・**ADR-5** Composition Root・**ADR-1** SO）/ `docs/coding-conventions.md` / `docs/mcp-guide.md` を正本とする、実機インターフェース接続の第2弾。Phase 1.5（削り＝M5Stick シリアル）と同様の「本編に対する別計画（サイドプラン）」。完了判定の bar は §6 受け入れ基準＋EditMode 全緑（回帰ゼロ）＋Unity-MCP 視覚検証。設計判断は新規 **ADR-24** に記録する。ブランチ `phase5-vision-input`（予定）。

## 1. 前提・背景（現状）

- **2系統の入力**: 物理かき氷機には「削っているか（回転）」と「位置検知（XY）」の2系統がある。前者は **Phase 1.5（ADR-15）で M5Stick シリアル連携済み**。本 Phase は後者の **「位置検知」** を接続する。
- **位置検知の既存実装**: MediaPipe（Python/OpenCV）で手首ランドマークの中点を追跡し、マウスで四隅を調整した四角形（quad）内で**射影変換**して正規化座標 (x,y)∈[0,1] を出す**プログラムが完成済み**（`get_normalized_coords` / `pointPolygonTest`）。ただし Unity との連携は未実装で、現状は標準出力に print するのみ。
- **ゲーム側の照準モデル**: 照準は**放射状4方向の離散チャンネル**（`0:N 1:W 2:S 3:E`、未選択 -1）。`IInputSource.ActiveChannel` が唯一の照準入力口（ADR-6）。連続照準は存在しない。
- **連携方式の方針**: デモ展示に間に合わせるため、複雑な通信プロトコルは使わず **画像処理結果をテキストファイルに書き、Unity が読む**方式を採る。更新頻度は 50ms 程度・通信の信頼性は最低限でよい要件。

## 2. 目的

- かき氷機の位置（＝プレイヤーの手の中点）で**照準する谷（`ActiveChannel`）を選ぶ**体験を実現する。「谷の上に機械を構える → 削る（M5Stick）→ その谷に雪崩」が物理操作だけで成立する。
- Python↔Unity を**ファイル経由で疎結合**にし、片方が落ちても他方を巻き込まない。**Python プロセスの停止・ハングを Unity が検知**し、フェイルセーフ（雪崩を止める）＋画面表示する。
- Editor 開発（キーボード）と展示本番（Vision）を**明示モードで切り替え**、当日その場で調整（キャリブレーション・しきい値）できる形にする。

## 3. 方針（設計判断の要約）

Core（`EnemyMatrix`/雪崩/State/`Scoring`/`AvalancheClock`）・生成・`Projectile`・カメラ・座標系は**無改修**。追加は入力層（`Input/`）と Composition Root（`GameManager`）の配線、および外部の Python ツールに限定する。

- **① 照準への写像＝離散4チャンネル / 分類は Python 側**: (x,y)→channel(0-3 / -1) の**セクター判定は Python が行う**。Unity は int を1つ読むだけ。生の (x,y) は Unity に持ち込まない。（→ 既存 `ActiveChannel` モデル無改修）
- **② channel の時間的意味＝ライブ/ラッチ切替**: 既定は **A（ライブ）**＝機械の現在位置に追従、中心不感帯・手未検出は -1。保険で **B（ラッチ）**＝別セクターに入るまで直前の谷を保持。`SerializeField`（or `VisionConfigSO`）で切替。実機で試して選ぶ。
- **③ データ契約＝`seq,channel`＋原子的書き込み**: ファイルは `seq,channel`（単調増加シーケンス番号＋channel）の1行。Python は **temp ファイルに書き切ってから `os.replace()`** でアトミックに rename（partial read を原理的に排除）。Unity は seq が一定時間更新されなければ「ロスト」と判定。
- **④ Python の配置・実行＝独立起動 / uv 隔離**: Python は `tools/position-tracker/`（`Assets/`・`Packages/` の外）に置き **uv 環境で隔離**（`.venv` は Unity 非対象・gitignore）。Unity からは起動せず、`uv run` で独立起動（OpenCV 窓で quad キャリブレーション）。
- **⑤ 入力源の合流＝InputMode 明示切替**: `CompositeInputSource` を拡張し `InputMode{Keyboard,Vision}` で `ActiveChannel` の源を明示切替（暗黙の優先順位合成はしない＝キーボードのラッチが本番に紛れない）。`IsShaving` は従来通り「keyboard OR serial（M5Stick）」、`ConfirmPressed` も従来通り。
- **⑥ ロスト時＝フェイルセーフ**: seq が `_lostTimeoutSec`（既定 0.3秒目安）を超えて不変なら `ActiveChannel` を **-1 強制**（削っても雪崩が流れない安全側）＋ `OnGUI` に警告テキスト。**「正常な谷なし(-1)」と「ロスト(-1)」を別フラグで区別**し、警告はロスト時のみ。復帰で自動解除。
- **⑦ ちらつき対策＝まず入れない・保険だけ用意**: セクター境界の channel ちらつきは実機で程度が読めないため、**まず素で試す**。Unity 側にデバウンス秒の `SerializeField`（既定 0＝無効）だけ用意し、必要なら当日回す。
- **⑧ ビルド時のパス解決＝config.json 単一真実**: `config.json` を **Python・Unity 共通の単一の真実**とする（`{ "positionFile": "<絶対パス>" }`）。解決順は「① 環境変数 `KAKIGORI_POSITION_FILE`（通常未設定・手動運用の保険）→ ② config.json → ③ Editor はプロジェクトルート相対 `tools/position-tracker/out/position.txt` / ビルドは `Application.persistentDataPath`」。macOS `.app` 標的。起動は Finder ダブルクリック（`.command` 等）を将来整備。
- **⑨ コード構造＝Serial パターンに対称**: 純ロジック `VisionInputProtocol`（EditMode 検証）＋ IO の薄い `VisionInputSource : IInputSource`（ファイルポーリング）。`SerialInputProtocol`↔`SerialInputSource` と完全対称（ADR-8/15）。

## 4. 変更対象一覧

| 種別 | 対象 | 変更 |
|---|---|---|
| 新規(Python) | `tools/position-tracker/`（uv 環境・`Assets/` 外） | 既存 MediaPipe プログラムを改修。config.json/env 解決 → セクター判定 → `seq,channel` を原子的書き込み（~50ms）。手未検出・不感帯は `-1` を書く。`pyproject.toml`/`.python-version` をコミット、`.venv`/`__pycache__`/`out/` は gitignore |
| 新規(Core相当・純C#) | `VisionInputProtocol`（`Input/`） | `seq,channel` パース＋seq watchdog（ロスト判定）＋A/B ラッチ＋デバウンス（保険）。`ActiveChannel` / `IsLost` を公開。MonoBehaviour 非依存＝EditMode 検証 |
| 新規(MB) | `VisionInputSource : IInputSource`（`Input/`） | ポーリングタイマでファイルを読み `VisionInputProtocol` へ流す。`ActiveChannel`＝protocol、`IsShaving`=false / `ConfirmPressed`=false。`IsLost` を公開。パス解決を保持 |
| 新規(SO) | `VisionConfigSO`（`Data/`） | 相対パス・config.json 名・ポーリング間隔・`_lostTimeoutSec`・ラッチモード(A/B)・デバウンス秒 を集約（ADR-1、`SerialConfigSO` に倣う） |
| 改修(純C#) | `CompositeInputSource` | `vision` 入力源と `InputMode{Keyboard,Vision}` を受け取り `ActiveChannel` を mode で分岐。`IsShaving`/`ConfirmPressed` は不変（keyboard OR serial） |
| 改修(MB) | `GameManager`（`BuildInput`） | `_visionConfig`・`_inputMode` の `SerializeField` を追加。Vision 系（`VisionInputSource`）を生成・配線し `Composite(keyboard, serial, vision, mode)` を組む。未割り当てなら生成せずキーボードのみ |
| 改修(MB) | `UIManager` | ロスト（状態Y）時の警告テキストを `OnGUI` に描画（`SetTrackerWarning(string)`）。GameManager が毎フレーム Vision の `IsLost` を反映 |
| 無改修 | Core / `Projectile` / `ChannelController` / State / カメラ / `KeyboardInputSource` / `SerialInputSource` | — |

## 5. マイルストーンとユニット

### マイルストーン A — Python 側（出力契約の確定）

**Unit A1: uv 環境と `tracker.py` 改修**
`tools/position-tracker/` を作成し `uv init` → `uv add opencv-python mediapipe numpy`、`uv python pin 3.12`（MediaPipe 対応）。既存 MediaPipe プログラムを改修し、(1) パス解決（env `KAKIGORI_POSITION_FILE` → `config.json` → 相対 `out/position.txt`）、(2) (x,y)→セクター判定（中心不感帯＝-1）、(3) 単調増加 `seq` を付け **`position.txt.tmp` に書いて `os.replace()`** で原子的 rename、を ~50ms 間隔で行う。手未検出・不感帯・quad 外は `-1` を書く。OpenCV プレビュー窓（quad マウス調整）は維持。
- 合否: `uv run python tracker.py` で `out/position.txt` が生成され、`cat`/tail で `seq` が単調増加し、手の位置で channel が 0-3、外すと -1 に変わることを確認。書き込み最中に読んでも壊れた行が出ない（原子的 rename）。

### マイルストーン B — Unity 純ロジック（`VisionInputProtocol`＋EditMode）

**Unit B1: `VisionInputProtocol` 実装＋テスト**
`seq,channel` の1行パース、seq watchdog（`_lostTimeoutSec` 無更新で `IsLost=true`）、A/B ラッチ（A＝素通し・B＝-1/未検出で直前値保持）、デバウンス（新 channel が指定秒安定してから採用・既定0＝無効）を純 C# で実装。`ActiveChannel`（ロスト時は -1）と `IsLost`（状態Y）を公開。`SerialInputProtocol` の watchdog 設計に倣う。
- 合否: EditMode テスト新規（パース、不正行の無視、seq 据え置き→`IsLost`、seq 前進→復帰、A/B の -1 挙動差、デバウンス）。既存 EditMode **全緑**（回帰ゼロ）。

### マイルストーン C — Unity 統合（IO・SO・Composite・UI）

**Unit C1: `VisionInputSource`＋`VisionConfigSO`＋パス解決**
`VisionInputSource : IInputSource`（`Input/`）を実装。`Update` でポーリング間隔（~50–100ms）ごとにファイルを読み最終行を `VisionInputProtocol.Ingest` → `Tick` で watchdog を進め `ActiveChannel`/`IsLost` を反映。パス解決（env→config.json→Editor 相対/ビルド persistentDataPath）を保持。`VisionConfigSO` に調整値を集約（ADR-1）。
- 合否: MCP Play — Python を起動した状態で、手の位置に応じて `VisionInputSource.ActiveChannel` が 0-3/-1 に追従。Python を kill すると `_lostTimeoutSec` 後に `IsLost=true`＋`ActiveChannel=-1`。

**Unit C2: `CompositeInputSource` 拡張＋`GameManager` 配線＋UI 警告**
`CompositeInputSource` に `vision` と `InputMode` を追加し `ActiveChannel` を mode 分岐（`IsShaving`/`ConfirmPressed` は不変）。`GameManager.BuildInput` に `_visionConfig`/`_inputMode` の `SerializeField` を追加し Vision 系を生成・配線。`UIManager` にロスト警告テキスト（状態Y 限定）を追加。
- 合否: EditMode — `CompositeInputSource` の mode 分岐テスト（Vision モードで vision の channel、Keyboard モードで keyboard の channel、`IsShaving`=keyboard OR serial 不変）。MCP Play — Vision モードで機械位置による谷選択＋M5Stick 削りで**その谷に雪崩**が成立。ロスト時に警告テキスト表示＋雪崩停止。EditMode **全緑**。

### マイルストーン D — Wave 通し回帰＋ADR/docs

**Unit D1: Wave 通し回帰 ＋ ADR-24 / docs**
Vision 入力（実 Python or fake ファイル注入）で `ActiveChannel` を駆動し「谷選択→削り→雪崩→先頭行消去→Result→retry」が成立することを確認。Python kill でフェイルセーフ（-1＋警告）を実証。`architecture.md` に **ADR-24**（Vision 入力＝3層＋Composite への InputMode 合流／config.json 単一真実のパス解決／ロスト時フェイルセーフ）を追記、`phases.md` に Phase 5 節を追加。
- 合否: Wave 通しで谷選択・雪崩・Result・retry が成立。フェイルセーフ実証。EditMode **全緑**。ドキュメント更新。

## 6. 受け入れ基準（完了判定の bar）

- [ ] Python が `out/position.txt` に `seq,channel` を **~50ms・原子的**に書き、手の位置で channel が 0-3、外すと -1 になる
- [ ] Unity（Vision モード）が機械位置で `ActiveChannel` を選び、**M5Stick 削りでその谷に雪崩**が流れる（削り＝keyboard OR serial は不変）
- [ ] `InputMode` で **Keyboard/Vision を明示切替**でき、Keyboard モードは従来のキーボード操作が回帰なしで動く
- [ ] channel の意味を **A（ライブ）/B（ラッチ）で切替**できる
- [ ] **Python 停止・ハングを検知**し、`ActiveChannel` を -1 に倒し（雪崩停止）警告テキストを表示、復帰で自動解除。**正常な谷なし(-1)とロスト(-1)を区別**する
- [ ] パス解決が **env→config.json→Editor 相対/ビルド persistentDataPath** の順で機能し、clone した他メンバーが Editor で無編集で動く（相対デフォルト）
- [ ] Python は `tools/position-tracker/`（`Assets/`・`Packages/` の外）の **uv 環境で隔離**され、Unity のインポート・`.meta` に影響しない
- [ ] Singleton=GameManager・入力=IInputSource・物理不使用を維持（Core/`Projectile` 無改修）
- [ ] **EditMode 全緑**（既存＋新規 `VisionInputProtocol`/`CompositeInputSource` 分岐・回帰ゼロ）
- [ ] 変更を `architecture.md` **ADR-24** に記録、`phases.md` に Phase 5 節を追加

## 7. ADR / スコープ外

- **ADR**: 新規 **ADR-24（かき氷機の位置検知＝ファイル経由 Vision 入力：`VisionInputProtocol`＋`VisionInputSource`＋`CompositeInputSource` の InputMode 合流／`seq,channel` 原子的書き込み／config.json 単一真実のパス解決／ロスト時フェイルセーフ）** を追記。ADR-6（入力 Strategy）・ADR-8（純ロジック分離）・ADR-15（実機接続＋Composite）を踏襲し覆さない。分類（(x,y)→channel）は Python が持つため Unity Core は不変。
- **スコープ外**:
  - **連続照準**（(x,y) をそのまま使う任意方向の雪崩）＝離散4チャンネルに落とす方針（本 Phase では導入しない）。
  - **quad キャリブレーション UI の Unity 化**（当日は OpenCV 窓のマウス調整を使う）。
  - **Python の Unity からの子プロセス起動 / インプロセス実行**（疎結合維持のため独立起動）。
  - **ビルド配布の作り込み**（`.command` ランチャー／config.json 自動生成）＝方針のみ決めドキュメント化。デモは Editor 再生で足りる。
  - **セクター境界ヒステリシスの Python 実装**（まず素で試し、必要なら Unity 側デバウンスで対処）。
  - **バランス調整・VFX・サウンド**。

## 8. 補足：ファイル契約とパス解決の詳細

- **フォーマット**: 1行 `seq,channel`（例 `1837,2`）。`seq` は long の単調増加、`channel`∈{-1,0,1,2,3}。生の (x,y) は含めない。
- **原子的書き込み（Python）**: `out/position.txt.tmp` に書き切り `os.replace(tmp, "out/position.txt")`。OS が rename の原子性を保証 → Unity は常に完全な1バージョンだけを読む。
- **パス解決（Python・Unity 共通の順序）**:
  1. 環境変数 `KAKIGORI_POSITION_FILE`（通常未設定。手動運用時の上書き保険）
  2. `config.json` の `positionFile`（配布物同梱の単一真実。`{ "positionFile": "<絶対パス>" }`）
  3. Editor（`.app` なし）＝プロジェクトルート相対 `tools/position-tracker/out/position.txt`（`Path.Combine(Application.dataPath, "..", relative)`）／ビルド＝`Application.persistentDataPath`
- **macOS 注意**: `.app` を `open` で起動すると export した env を継承しないため、env 方式を使う手動運用では**内部バイナリ直叩き**（`./kakigori_demo.app/Contents/MacOS/kakigori_demo`）を用いる。通常は config.json 方式（起動方法非依存）を主にする。標的は macOS `.app`（`persistentDataPath`＝`~/Library/Application Support/DefaultCompany/kakigori_demo/`）。
- **実装時に潰す細部**: `seq` 桁あふれ（long 運用）、起動時にファイル未生成の初期状態（`IsLost` 扱い）、config.json の探索位置（`.app` 隣接）、`_lostTimeoutSec`/ポーリング間隔/デバウンス秒の実測チューニング。

## 9. 着手メモ

### マイルストーン A 完了（2026-07-09）— Python 側の出力契約

- **A1（uv 環境）**: `tools/position-tracker/` に `uv init` → `requires-python = ">=3.12,<3.13"`（MediaPipe 対応）＋ `uv python pin 3.12`。`uv add opencv-python mediapipe numpy` で導入（`mediapipe 0.10.35` / `opencv-python 5.0.0.93` / `numpy 2.5.1`）。`.venv`・`uv.lock`・`.python-version` はすべて `tools/position-tracker/` 内に隔離、**リポジトリ直下は無汚染**（Unity のインポート対象外）。`.venv`/`__pycache__`/`out/`/`config.json` は tool 直下の `.gitignore` で除外、`config.example.json` をコミット。
- **A1（`tracker.py`）**: 既存 MediaPipe プログラムを改修。(1) パス解決 `resolve_position_file`（env `KAKIGORI_POSITION_FILE` → `config.json` の `positionFile` → スクリプト相対 `out/position.txt`）、(2) `classify(nx,ny,deadzone)`（中心 0.5 からの距離 < deadzone=0.15 で -1、以降 `|dy|>=|dx|` で N/S・else W/E＝対角4分割。画像 y は下向き増加＝N は dy<0）、(3) `PositionWriter`＝`position.txt.tmp` に書き切って `os.replace()` で**原子的 rename**、`seq` 単調増加。手未検出・quad 外・不感帯でも `-1` を書き続け seq を進める（ロストと区別）。本番 `run_camera`（quad マウス調整・ESC 終了）と、カメラ無しの `run_sim`（合成円軌道で契約検証）の2モード。
- **A1 検証（カメラ無し）**: 当環境はカメラ非搭載のため契約を決定的に検証。①`classify` 代表点＝ (0.5,0.1)→0(N) / (0.5,0.9)→2(S) / (0.1,0.5)→1(W) / (0.9,0.5)→3(E) / 中心→-1 / 中心近傍(距離0.02)→-1。②原子的書き込み＝連続書き込みで読み出しが**常に完全な1行**（partial/multi なし）・`seq` 厳密単調増加 `[1..7]`・`channel`∈[-1,3]・`.tmp` 残留なし。③パス解決順 env>config>相対デフォルト。④`--sim` 実起動で `out/position.txt` の seq が 158→210 と進み channel が 0/-1 と変化（中心通過で不感帯）、5連続読みが同一完全行（原子性）。全 PASS。
- **合否**: §5 マイルストーン A の合否条件（ファイル生成・seq 単調増加・channel 0-3/-1・原子性）を充足。実機カメラでの手位置→channel 追従は当日 MCP/実機で確認（環境にカメラが無いため保留）。

### マイルストーン B 完了（2026-07-09）— Unity 純ロジック `VisionInputProtocol`

- **B1（`VisionInputProtocol`）**: `Assets/Scripts/Input/VisionInputProtocol.cs`。`SerialInputProtocol` と対称の純 C#。`Ingest(line)`＝`seq,channel` を厳格パース（カンマ・long・int・範囲 -1..3 検査、外れは不正行として無視）。**watchdog は seq の変化で判定**（IO 層がポーリングで同一行を繰り返し Ingest しうるため。`seq != _lastSeq` で新鮮＝再武装。Python 再起動で seq が小さく戻っても「変化」＝新鮮）。`Tick(dt)` で `_timeSinceSeqChange` 加算＋モード適用（ライブ=raw そのまま／ラッチ=-1 を直前有効値で埋める）＋デバウンス（候補が指定秒安定で採用・0 で即時）。`IsLost`（状態Y）と `ActiveChannel`（ロスト時は -1 フェイルセーフ）を公開。
- **B1（テスト）**: `Assets/Tests/EditMode/VisionInputProtocolTests.cs` 15 本 — 受信前ロスト＋-1、ライブ追従、ライブ -1 で解除、ラッチ -1 で谷保持・別谷で切替、不正行/null 無視、seq 停止でロスト＋フェイルセーフ、seq 前進で生存、復帰、Python 再起動(seq 降下)を新鮮扱い、watchdog 無効、デバウンス（安定で採用/ちらつきは不採用/0 で即時）。
- **B1 検証（MCP EditMode）**: `VisionInputProtocolTests` 15/15 緑。フル EditMode **135/135 緑**（既存 120＋新規 15・回帰ゼロ）。
- **合否**: §5 マイルストーン B の合否条件（新規テスト＋既存全緑）を充足。

### マイルストーン C〜D

（未着手。完了ごとに追記する。）
