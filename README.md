# かき氷タワーディフェンス (Kakigori TD)

雪山の神となり、頂上からかき氷を削って雪崩を起こし、麓から砦を遠隔攻撃する敵を殲滅する、**体験型タワーディフェンス**。
展示用インスタレーションとして制作した、実機入力（かき氷機ハンドル・LiDAR）対応のゲームです。

<!-- TODO: スクリーンショット / プレイ動画を追加 -->
<!-- ![screenshot](docs/images/screenshot.png) -->

## ゲーム概要

- **ルール**: 目標撃破数（既定 100 体）を先に達成すればクリア、砦の HP が 0 になればゲームオーバー（撃破数レース）
- **操作**: かき氷機のハンドルを回して氷を削り、落とした位置から雪崩を起こして敵を巻き込む
- **ギミック**: 爆発兵の連鎖、盾兵、コンボ、ゲージが溜まると撃てる必殺ビーム砲
- **スコア**: クリアタイムをローカルランキングに記録（上位 10 件表示）

## 特徴

- **物理入力に対応**: かき氷機ハンドルの回転（シリアル通信）と、氷の位置検知（カメラ / LiDAR）から操作できる。キーボードは開発用のフォールバック
- **LiDAR による落氷検知**: 複数台のセンサーで冗長化し、検知結果を Unity が読み取る（`tools/`）
- **ロジックと演出の分離**: ゲームロジックは UnityEngine 非依存の純 C#（`Assets/Scripts/Core/`）に分離し、EditMode テスト 254 本で検証

## 技術スタック

| 項目 | 内容 |
|---|---|
| エンジン | Unity 6000.3.16f1（Unity 6.3 / URP 17.3） |
| 言語 | C#（Unity）、Python / C++（`tools/` の入力トラッカー） |
| パッケージ | Cinemachine 3.1 / Input System 1.19 / Test Framework 1.6 |
| テスト | Unity Test Framework（EditMode） |

## 動かし方

1. Unity Hub で **Unity 6000.3.16f1** を用意する
2. このリポジトリを clone し、Unity Hub から開く
3. `Assets/Scenes/TitleScene.unity` を開いて Play する

### 操作（開発用。展示では LiDAR とかき氷機ハンドルで操作）

| 操作 | 動作 |
|---|---|
| マウス左ボタン長押し | 画面上の位置に雪を削り落とし、雪崩を起こす |
| 矢印キー | 雪を落とす位置を移動 |
| `Space` 長押し | その位置で雪を削る |
| `Enter` | タイトル・リザルトからの決定 / シーン遷移 |
| `D` 5 秒長押し（タイトル / リザルト） | ランキングを消去 |

## シーン構成

`TitleScene` → `TH`（ゲーム本体）→ `ResultScene`（クリア）/ `GameOverScene`（敗北）→ `TitleScene`

## ディレクトリ構成

```
Assets/Scripts/
  Core/       純C#ロジック（コンボ・ゲージ・爆発連鎖・湧き・ランキング）
  Data/       ScriptableObject 定義（調整値）
  Input/      入力抽象化（キーボード / シリアル / Vision・LiDAR）
  Gameplay/   敵・雪崩・ビーム砲・砦など MonoBehaviour
  Audio/ UI/ Persistence/ Bootstrap/ Scenes/
Assets/Tests/EditMode/   単体テスト
docs/                    仕様・設計判断（ADR）・フェーズ計画
tools/                   位置検知トラッカー（Python / C++）
```

設計の詳細は [docs/architecture.md](docs/architecture.md) と [docs/spec-summary.md](docs/spec-summary.md) を参照してください。
