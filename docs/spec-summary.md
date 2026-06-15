# 仕様要約 (Spec Summary)

> 各サブエージェントが単独で読んで実装できる要約。**完全版の正本は v2 仕様書**（`docs/spec-full.md` に配置推奨）。本要約と完全版が食い違う場合は完全版を正とし、本ファイルを更新する。

## コンセプト
雪山の神となり、頂上からかき氷を削って雪崩を起こし、麓4方向から砦を**遠隔攻撃**する敵を、**殲滅レース**で退ける。

## 遊びの核
- ジャンル: 殲滅レース型タワーディフェンス。
- 勝利: 3 Wave 生存。 / 敗北: 砦HPが0。 / スコア: 終了時の砦HP（Phase2以降は −味方巻き込みペナルティ）。
- 圧力(i): 氷が有限（1プレイ固定・補充なし・溶けない）。
- 圧力(ii): 一度に1方向の谷にしか雪崩を流せない。

## マップ・座標系
- 砦＝中央/山頂。谷（雪崩の流路）は**放射状4本**: `North/West/South/East`（チャンネルID `0:N 1:W 2:S 3:E`）。
- 敵は各谷の**麓（外周端）**にマトリックスで位置。
- 敵の遠隔攻撃＝**麓→山頂（外→中）**に飛ぶ弾。雪崩＝**山頂→麓（中→外）**に流れる。両者は逆向きに交差。

## 入力
- `W/A/S/D`: 照準する谷を選択（ラッチ式・同時1本のみ）。`Space` 長押し: アクティブ谷を削る。
- 入力は `IInputSource` 抽象（`ActiveChannel:int(-1で未選択)`, `IsShaving:bool`）。実装は `KeyboardInputSource`。

## コアメカニクス
- **削り→雪崩ストリーム**: `Space` 長押し中、アクティブ谷に雪崩が流れ、`iceDrainPerSec` で氷ストック消費。0で削れない。
- **撃破解決（深さ制御）**: 削り開始から `avalancheDelay` 秒後に判定開始（Inspector可変・初期値0＝即時）。以降 `killInterval` 秒ごとにその谷の**先頭行（最大 `rowWidth`=3 体）を消す**（1ヒット1キル）。先頭行が消えると次行が先頭に＝削り続けると奥へ進む。削りを止める/谷を変えると深さを制御できる。
- **氷ストック**: 有限の氷の数値表現（削れる秒数）。`iceTotal` を全3Wave通算で消費、補充なし・溶けない。

## 敵と遠隔攻撃（プロジェクタイル）
- 各谷の麓に **3体×N行**のマトリックス。新規湧きは**最奥**に追加。
- 各敵は `EnemyTypeSO.fireInterval` 秒ごとに弾を生成し砦へ発射。弾は `ProjectileTypeSO.speed` で麓→山頂へ飛び、着弾で `ProjectileTypeSO.damage` を砦に与える。
- 弾は **ObjectPool** で使い回し（着弾でプール返却）。生成は **ProjectileFactory**（`ProjectileTypeSO` を渡す）。
- 敵を倒すと**以後の発射**が停止（飛来中の弾は着弾する）。雪崩は弾を防がない（スコープ外）。

## 砦HP ＝ ゲームの時計
- 砦HP（`fortHpMax`）は着弾ごとに離散減算。減る速さ＝生存敵数 × 発射頻度 × 弾ダメージ。敵を放置するほど速く減る。
- HP0 で `Fort.OnDestroyed` → 敗北。`Fort.OnHpChanged` を UI が購読。

## 味方NPC（Phase2）
- 青（敵は赤）。**一時出現・自走退場**（`allyLifetime` 秒）。マトリックスの1行を占有。
- 雪崩が味方の行に達すると巻き込み＝スコア減（`allyHitPenalty`）。
- 味方の手前の敵は手前で削りを止めればノーペナルティで撃破可。奥の敵は味方退場後に撃破可（恒久ロックされない）。

## 変装（Phase3）
- 変装敵は青（味方色）＋**微妙な見た目差（tell）**。見逃すと退場せず攻撃継続／真の味方を撃つとスコア減。tellの具体は TBD。
- 突撃隊: 高 `moveSpeed`／短 `fireInterval` の高プレッシャー敵（パラメータで表現）。

## Wave
- ウェーブごとに**制限時間を個別指定**（`WaveConfigSO.duration`）。Wave終了時、残った敵は消える。Wave間に `waveLull` 秒の小休止。

| Wave | 名称 | 内容 | Phase |
|---|---|---|---|
| 1 | 快感波 | 味方なし。資源/注意の判断 | 1 |
| 2 | 本隊侵攻 | 味方混在（深さ制御ジレンマ） | 2 |
| 3 | 突撃隊＋変装波 | 速い敵＋青に化けた変装敵 | 3 |

## 主要パラメータ初期値（プレイテストで調整）
| パラメータ | 初期値 | 置き場所 |
|---|---|---|
| `channelCount` | 4 | 固定 |
| `avalancheDelay` | 0 秒 | Inspector |
| `killInterval` | 0.4 秒/行 | Inspector |
| `rowWidth` | 3 | Inspector |
| `iceTotal` | 50 削り秒 | Inspector |
| `iceDrainPerSec` | 1.0 | Inspector |
| `fortHpMax` | 1000 | Inspector |
| `projectile.damage` | 5 | ProjectileTypeSO |
| `projectile.speed` | 6.0 | ProjectileTypeSO |
| `enemy.fireInterval` | 2.0 秒 | EnemyTypeSO |
| `wave[1..3].duration` | 30/40/45 秒 | WaveConfigSO |
| `wave[1..3].spawnInterval` | 1.5/1.0/0.6 秒 | WaveConfigSO |
| `waveLull` | 5 秒 | Inspector |
| `allyLifetime`（P2） | 6 秒 | WaveConfigSO/EnemyTypeSO |
| `allyHitPenalty`（P2） | 50 | Inspector |
