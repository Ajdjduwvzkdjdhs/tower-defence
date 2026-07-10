# Phase 6 — サウンド（BGM / SE）移植計画

> **正本**: 本ドキュメントが Phase 6 の確定計画。`docs/audio-plan.md`（初期メモ）を dig で改訂・確定したもの。
> **設計判断**: `docs/architecture.md` **ADR-26**。ブランチ: `phase6-audio`。
> **移植元**: `work/kakigori-3d-t`（`Assets/SoundE/` の 7 音源 ＋ `AudioManager.cs` を参照）。

## ゴール

3d-t の BGM/SE を kakigori_demo に移植する。ただし **3d-t の Singleton 直叩き（`AudioManager.Instance.PlaySound(...)`）は採用せず**、本リポジトリの流儀（Composition Root で DI 配線 → イベント購読で鳴らす）に translate する。**表現層のみ・Core ロジック無改修**（ADR-21 系と同方針）。

## 基本原則（dig で確定）

- **表現層のみ移植・Core 無改修**。追加するのは additive な cosmetic イベント／購読のみ。既存ロジックの分岐・数値・順序は一切変えない。
- 調整値（クリップ・音量）は **AudioConfigSO**（ADR-1）。
- イベント購読は **`OnDestroy` で必ず解除**（ADR-3）。
- 音は**映像に合わせる**（特に雪崩＝持続ストリームには持続ループ音）。

## 音の全体像（確定・トリガー早見表）

| 音 | 系統 / 再生方法 | トリガー | 音量 |
|---|---|---|---|
| BGM（馬車道） | `_bgmSource`（loop） | ゲーム開始時に1回 Play、**以後止めない**（Q4-A） | `bgmVolume` |
| Clear | `_seSource`（PlayOneShot） | 勝利（`ShowResult(true)`）。BGM に重ねる | 1.0 |
| GameOver | `_seSource`（PlayOneShot） | 敗北（`ShowResult(false)`／砦陥落）。BGM に重ねる | 1.0 |
| Hit（砦被弾） | `_seSource`（PlayOneShot） | 砦 HP 減少 **かつ** 新HP>0 | 0.1 |
| Monster（敵撃破） | `_seSource`（PlayOneShot） | **1 行消去（`ApplyAvalanche` 1 回）につき 1 回**・実敵/変装が居た時のみ（Q2-A） | 0.2 |
| EnemyAttack（敵発射） | `_seSource`（PlayOneShot） | 敵の弾発射（毎発。`ProjectileFactory.Create` チョークポイント）（Q3-B） | 0.1 |
| **Avalanche（雪崩）** | **`_avalancheSource`（loop・gate）** | **`IsAvalanching` true の間ループ再生、false で停止**（Q1-A） | 1.0 |
| （AllyCaught） | `_seSource`（PlayOneShot） | 味方巻き込み。**クリップ未定＝null 既定で無音** | 1.0 |

### dig で確定した設計判断（要点）

- **Q1 雪崩音＝ループ gate（A）**: `AvalancheVfx` が `IsAvalanching` で雪玉を連続射出するのに合わせ、音も同フラグでループ。**専用 `_avalancheSource`（loop=true）** を持たせることで、初期メモの弱点「`PlaySeWithDuration` の `Stop()` が同 source の PlayOneShot を切る」問題を構造的に解消。→ 初期メモの `avalancheDuration` は**不要（削除）**。
- **Q2 撃破音の粒度＝行単位（A）**: 雪崩は先頭「行」（最大 `rowWidth=3` 体）を一括消去するため、敵ごとに鳴らすと同フレームで最大3重。`ApplyAvalanche` 1 回につき 1 回だけ発火（実敵/変装を1体でも Release した時）。味方のみの行は無音。
- **Q3 発射音＝毎発（B）**: まず毎発 PlayOneShot（0.1）で実装し、MCP Play（4谷密集）で聴取。過剰なら後からスロットリング（最小発火間隔を SO 化）を足せる構造にしておく＝手戻りなし。
- **Q4 BGM＝止めない（A）**: `Awake` で1回 Play、Result でも止めない。勝敗ジングルは BGM の上に重なる。リトライ（`BeginWave(0)`）での「BGM 二度と鳴らない」バグを構造的に回避。
- **Q5 変装敵の発射音＝特別扱いしない（A）**: 変装敵は既に**見える弾**を撃っており（真の味方 `AllyNPC` は `Projectile=null` で無発射）、音は新情報ではなく既存視覚ヒントの補強。単一チョークポイントを保つ。

## 音源アセット（7つ）

`/Users/k23081kk/work/kakigori-3d-t/Assets/SoundE/` → `kakigori_demo/Assets/Audio/`（ASCII 名推奨）。

| 元ファイル | 用途 | 推奨名 |
|---|---|---|
| 馬車道.mp3 | BGM ループ | `bgm_main.mp3` |
| ゲームクリア勝利１.mp3 | 勝利 | `se_clear.mp3` |
| ゲームオーバーアーケイド.mp3 | 敗北 | `se_gameover.mp3` |
| 攻撃が砦に着弾.mp3 | 砦被弾 | `se_hit.mp3` |
| モンスター撃破.mp3 | 敵撃破 | `se_enemy_killed.mp3` |
| 敵攻撃.mp3 | 敵発射 | `se_enemy_attack.mp3` |
| 雪崩.mp3 | 雪崩発生（ループ） | `se_avalanche.mp3` |

> **コピー経路**: クロスプロジェクトのため MCP `assets-copy`（同プロジェクト内前提）は不可。**OS コピー（`.meta` を持ち込まない）→ `assets-refresh` で GUID 再生成**。

## 新規ファイル

### `Assets/Scripts/Audio/AudioConfigSO.cs`（`KakigoriTD.Audio`、ADR-1）

- clip: `bgm`, `clear`, `gameOver`, `hit`, `enemyKilled`, `enemyAttack`, `avalanche`, `allyCaught`
- 音量（3d-t 踏襲）: `enemyKilled=0.2`, `hit=0.1`, `enemyAttack=0.1`, 他=1.0, `bgmVolume`
- `allyCaught` は 3d-t に該当クリップ無し → スロットのみ・**null 既定＝無音**（後で差し替え可）。
- ~~`avalancheDuration`~~ は **不採用**（Q1-A のループ gate 化で不要）。

### `Assets/Scripts/Audio/AudioService.cs`（MonoBehaviour、薄いアダプタ ADR-8/11）

- AudioSource **3系統**を `Awake` で自動生成:
  - `_bgmSource`（loop=true）
  - `_seSource`（PlayOneShot 用）
  - `_avalancheSource`（loop=true・雪崩 gate 用。PlayOneShot と分離）
- `Configure(AudioConfigSO cfg)` で注入。
- API 例: `PlayBgm()` / `PlaySe(clip, volume)` / `SetAvalanche(bool on)`（true で `_avalancheSource` を loop 再生・false で Stop）。
- クリップ null 安全（null なら鳴らさない）。純ロジックを持たない薄いアダプタ＝EditMode テストは書かず MCP Play で検証（ADR-11）。

### `Assets/ScriptableObjects/AudioConfig.asset`

- AudioConfigSO インスタンス。7 クリップ割当（`allyCaught` は空＝無音）。GameManager の SerializeField にセット。

## 既存ファイルの改修（additive イベント追加のみ）

| ファイル | 追加 | 発火箇所 |
|---|---|---|
| `Factory/ProjectileFactory.cs` | `public event Action OnCreated;` | `Create()` 内で発火（全発射の単一チョークポイント）→ **EnemyAttack** |
| `Gameplay/ChannelController.cs` | `public event Action OnEnemyKilled;` | `ApplyAvalanche()` の**末尾**、この呼び出しで実敵（`else`）または変装敵（`DisguisedEnemy`）を1体でも Release したら **1 回だけ**発火。味方（`AllyNPC`）のみの行では発火しない → **Monster** |
| `Gameplay/AvalancheController.cs` | `public event Action<bool> OnAvalancheChanged;`（または bool 立ち上がり/立ち下がり）| `IsAvalanching` の値が**変化したフレーム**で発火（[`AvalancheController.cs:90`](../Assets/Scripts/Gameplay/AvalancheController.cs#L90) の代入を `_wasAvalanching` と比較）→ **Avalanche loop の on/off** |

> **注意**: `OnEnemyKilled` は「行単位で1回」。`ApplyAvalanche` のループ内で `bool killedReal` を立て、末尾で true なら発火。
> **注意**: `OnAvalancheChanged` は**エッジ検出**が必須（毎フレーム true のまま発火しない）。前フレーム値 `_wasAvalanching` をキャッシュし、変化時のみ発火。

## GameManager 配線（`Assets/Scripts/Bootstrap/GameManager.cs`）

1. `[SerializeField] private AudioConfigSO _audioConfig;` を追加。
2. `Awake` で `NewChild("AudioService").AddComponent<AudioService>()` → `Configure(_audioConfig)` → `PlayBgm()`。オーディオ配線は Fort/Factory/Channels 生成の**後**（購読対象が揃ってから）。
3. イベント購読:

| 音 | フック | 備考 |
|---|---|---|
| Clear / GameOver | `ShowResult(bool isWin, int)`（[`GameManager.cs:157`](../Assets/Scripts/Bootstrap/GameManager.cs#L157)） | `isWin` で分岐。**BGM は止めない**（Q4-A） |
| Hit | `Fort.OnHpChanged` | 前HP キャッシュし「減少 かつ 新HP>0」時のみ。`Initialize` の満タン通知・0到達（＝GameOver）を除外 |
| Monster | `ChannelController.OnEnemyKilled`（新規） | 各 channel を購読 |
| EnemyAttack | `ProjectileFactory.OnCreated`（新規） | `_projectileFactory` を購読 |
| Avalanche | `AvalancheController.OnAvalancheChanged`（新規） | `on` を `AudioService.SetAvalanche(on)` に渡す（loop on/off） |
| AllyCaught | `ChannelController.OnAllyCaught`（既存, [`GameManager.cs:221`](../Assets/Scripts/Bootstrap/GameManager.cs#L221)） | 既存 `OnAllyCaughtHandler` に音を1行追加。クリップ null なら無音 |

4. **`OnDestroy` で全新規購読を解除**（既存の `OnAllyCaught` 解除 [`GameManager.cs:420`](../Assets/Scripts/Bootstrap/GameManager.cs#L420) と同様）。解除対象を明記:
   - `ProjectileFactory.OnCreated`
   - `ChannelController.OnEnemyKilled`（×4ch）
   - `AvalancheController.OnAvalancheChanged`
   - `Fort.OnHpChanged`

## 受け入れ基準

- [ ] BGM が開始時にループ再生され、リトライ（`BeginWave(0)`）後も鳴り続ける
- [ ] 削り中（`IsAvalanching`）は雪崩音がループし、離すと止まる（映像の雪玉ストリームと同期）
- [ ] 敵の弾発射で発射音、砦被弾（HP 減少 & >0）で Hit 音、雪崩で1行消えるごとに Monster 音（味方のみの行は無音）
- [ ] 勝利で Clear 音、敗北で GameOver 音（BGM に重なる）
- [ ] 味方巻き込みは AllyCaught（クリップ null なら無音でエラーなし）
- [ ] Core・雪崩・生成・State・`Projectile` 無改修＝**EditMode 139本緑・回帰ゼロ**
- [ ] 全新規購読が `OnDestroy` で解除される
- [ ] 変更を `docs/architecture.md` **ADR-26** に記録（Accepted）

## 検証・確認タスク（実装時）

- `se_avalanche.mp3` が**ループ継ぎ目で不自然に切れない尺か**を確認（ループ gate 化のため）。継ぎ目が出るなら短い持続音のループ or フェード処理を検討。
- Q3 の発射音密度を MCP Play（4谷密集）で聴取。うるさければスロットリング（間隔を `AudioConfigSO` に追加）を足す。
- MCP Play で各トリガー（BGM ループ・雪崩 on/off・Hit・Monster・発射・勝敗）を確認し screenshot/ログを残す。

## Out of scope（やらない）

- 効果音の自作・差し替え（3d-t の 7 音源をそのまま使う）。
- 氷枯渇音・Wave 導入/クリアの区切り音（3d-t に無い＝対象外）。
- BGM のダッキング（勝敗時に BGM を絞る）・音の空間化（3D 定位）。
- `DontDestroyOnLoad`（単一シーン運用＝不要）。
