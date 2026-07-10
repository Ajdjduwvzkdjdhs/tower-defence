# BGM / SE 移植方針（kakigori-3d-t → kakigori_demo）

> **目的**: 3d-t（`work/kakigori-3d-t`）で実装済みの BGM/SE を kakigori_demo に移植する。
> **前提**: 実装は本ドキュメントを読んだ別セッションが kakigori_demo 上で行う。ここには方針・フック・改修点を確定情報として記す。

## 基本原則

- **表現層のみ移植・Core ロジック無改修**（既存の 3D 移植・ADR-21 と同じ方針）。
- 3d-t の Singleton 直叩き（`AudioManager.Instance.PlaySound("Hit")`）は **採用しない**。
  kakigori_demo の流儀に合わせ、**GameManager（Composition Root）で DI 配線 → イベント購読で鳴らす**。
- 調整値（クリップ・音量・持続時間）は **AudioConfigSO**（ADR-1: SO で調整値）。
- イベント購読は **OnDestroy で必ず解除**（ADR-3）。
- 追加するのは **additive な cosmetic イベントのみ**。既存ロジックの分岐・数値・順序は一切変えない。

## 音源アセット（7つ）

`/Users/k23081kk/work/kakigori-3d-t/Assets/SoundE/` からコピー → `kakigori_demo/Assets/Audio/`（ASCII 名推奨）。

| 元ファイル | 用途 | 推奨名 |
|---|---|---|
| 馬車道.mp3 | BGM ループ | `bgm_main.mp3` |
| ゲームクリア勝利１.mp3 | 勝利 | `se_clear.mp3` |
| ゲームオーバーアーケイド.mp3 | 敗北 | `se_gameover.mp3` |
| 攻撃が砦に着弾.mp3 | 砦被弾 | `se_hit.mp3` |
| モンスター撃破.mp3 | 敵撃破 | `se_enemy_killed.mp3` |
| 敵攻撃.mp3 | 敵発射 | `se_enemy_attack.mp3` |
| 雪崩.mp3 | 雪崩発生 | `se_avalanche.mp3` |

> コピーは `.meta` を持ち込まず、Unity に GUID を再生成させる（クロスプロジェクトのため）。

## 新規ファイル

### `Assets/Scripts/Audio/AudioConfigSO.cs`（`KakigoriTD.Audio`、ADR-1）

- clip フィールド: `bgm`, `clear`, `gameOver`, `hit`, `enemyKilled`, `enemyAttack`, `avalanche`, `allyCaught`
- 音量フィールド（3d-t の値を踏襲）: `enemyKilled = 0.2`, `hit = 0.1`, `enemyAttack = 0.1`, その他 = 1.0, `bgmVolume`
- `avalancheDuration`（3d-t の `PlaySoundWithDuration` 相当）
- **`allyCaught` は該当クリップが 3d-t に存在しない** → スロットだけ用意し **null 既定＝無音**（後で差し替え可能に）。

### `Assets/Scripts/Audio/AudioService.cs`（MonoBehaviour、薄いアダプタ ADR-8）

- AudioSource 2系統: `_bgmSource`（loop）＋ `_seSource`（`PlayOneShot`）を `Awake` で自動生成。
- `Configure(AudioConfigSO cfg)` で注入。
- API 例: `PlayBgm()` / `StopBgm()` / `PlaySe(clip, volume)` / `PlaySeWithDuration(clip, duration)`。
- クリップ null 安全（null なら鳴らさない）。

### `Assets/ScriptableObjects/AudioConfig.asset`

- AudioConfigSO インスタンス。7クリップを割り当て、GameManager の SerializeField にセット。

## 既存ファイルの改修（additive イベント追加のみ）

| ファイル | 追加内容 | 発火箇所 |
|---|---|---|
| `Factory/ProjectileFactory.cs` | `public event Action OnCreated;` | `Create()` 内で発火。全敵発射の単一チョークポイント → **EnemyAttack 音** |
| `Gameplay/ChannelController.cs` | `public event Action OnEnemyKilled;` | `ApplyAvalanche()` の **実敵（`else` 分岐）＆変装敵（`DisguisedEnemy` 分岐）** の kill で発火。**味方（`AllyNPC`）分岐では発火しない** → **Monster 音** |
| `Gameplay/AvalancheController.cs` | `public event Action OnAvalancheStarted;` | `IsAvalanching` の **false→true 立ち上がりエッジ**（`Assets/Scripts/Gameplay/AvalancheController.cs:90` の代入箇所）で発火 → **Avalanche 音** |

## GameManager 配線（`Assets/Scripts/Bootstrap/GameManager.cs`）

1. `[SerializeField] private AudioConfigSO _audioConfig;` を追加。
2. `Awake` で `NewChild("AudioService").AddComponent<AudioService>()` → `Configure(_audioConfig)` → `PlayBgm()`。
3. イベント購読:

| 音 | フック | 備考 |
|---|---|---|
| Clear / GameOver | `ShowResult(bool isWin, int)`（`GameManager.cs:157`） | 既存の唯一の勝敗窓口。`isWin` で分岐。任意で `StopBgm()` |
| 砦被弾(Hit) | `Fort.OnHpChanged` | **前HPをキャッシュし「減少 かつ 新HP > 0」の時だけ**再生。`Initialize` の満タン通知・0到達（＝GameOver）を除外 |
| 敵撃破 | `ChannelController.OnEnemyKilled`（新規） | 各 channel を購読 |
| 敵発射 | `ProjectileFactory.OnCreated`（新規） | `_projectileFactory` を購読 |
| 雪崩 | `AvalancheController.OnAvalancheStarted`（新規） | `PlaySeWithDuration(avalanche, avalancheDuration)` |
| 味方巻き込み | `ChannelController.OnAllyCaught`（既存, `GameManager.cs:221` 購読） | 既存 `OnAllyCaughtHandler` に音を1行追加。クリップ null なら無音 |

4. **OnDestroy で全購読を解除**（既存の `OnAllyCaught` 解除 `GameManager.cs:420` と同様に、新規イベントも解除を追加）。

## 注意点（申し送り）

- **asmdef**: `Assets/Scripts/` に asmdef があれば、新設 `Audio` フォルダがその配下に入るか確認。テストアセンブリからの参照要否もチェック。
- **Hit のダブり防止**: `OnHpChanged` は `Fort.Initialize`（リトライ時に満タン）でも発火する。必ず「減少時のみ」ガードを入れる。
- **0到達時**: Hit を鳴らさず GameOver（`ShowResult` 側）に委ねる。
- **テスト**: 追加は純 additive イベントのため既存 EditMode テストへの影響なし。イベント発火を検証したい場合は軽い購読テストを追加可。
- **DontDestroyOnLoad**: 3d-t は未設定だったが、kakigori_demo が単一シーン運用なら不要。シーン遷移設計を確認のうえ判断。

## 音の全体像（トリガー早見表）

| 音 | 系統 | 再生方法 | トリガー |
|---|---|---|---|
| BGM（馬車道） | BGM | loop 再生 | ゲーム開始時 |
| Clear | SE | PlayOneShot | 勝利（`ShowResult(true)`） |
| GameOver | SE | PlayOneShot | 敗北（`ShowResult(false)`／砦陥落） |
| Hit | SE(0.1) | PlayOneShot | 砦被弾（HP 減少 & >0） |
| Monster | SE(0.2) | PlayOneShot | 敵/変装敵の雪崩撃破 |
| EnemyAttack | SE(0.1) | PlayOneShot | 敵の弾発射 |
| Avalanche | SE | PlaySeWithDuration | 雪崩開始（削り開始エッジ） |
| （AllyCaught） | SE | PlayOneShot | 味方巻き込み（クリップ未定・null 既定で無音） |
