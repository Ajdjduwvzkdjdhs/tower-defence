# position-tracker（かき氷機の位置検知 / Phase 5）

MediaPipe で手の位置を追い、放射4セクター（`0:N 1:W 2:S 3:E` / 不感帯・未検出 `-1`）を
判定して、共有テキストファイルに `seq,channel` を原子的に書き出す。Unity 側はこのファイルを
ポーリングして照準（`ActiveChannel`）に使う。設計は `docs/phase5.md`（正本）を参照。

`Assets/` の外（`tools/`）に置き、uv 環境で隔離している（Unity のインポート対象外）。

## セットアップ

```sh
cd tools/position-tracker
uv sync            # .venv を作り依存（opencv/mediapipe/numpy）を導入。Python は 3.12 固定
```

## 実行

```sh
# 本番：カメラ + MediaPipe。ウィンドウ上でマウスドラッグして四隅(quad)を台に合わせる。ESC で終了
uv run python tracker.py

# 検証：カメラ無し。合成座標で書き込み契約（seq 単調増加・channel 0-3/-1）を確認。Ctrl-C で終了
uv run python tracker.py --sim
```

出力先の解決順（Python・Unity 共通・`docs/phase5.md` §8）:

1. 環境変数 `KAKIGORI_POSITION_FILE`（通常未設定・手動運用の上書き保険）
2. `config.json` の `positionFile`（配布物同梱の単一真実。`config.example.json` をコピーして作る）
3. スクリプト相対 `out/position.txt`（Editor 開発の既定・無設定で動く）

## ファイル契約

- 1 行 `seq,channel`（例 `1837,2`）。`seq` は long 単調増加、`channel`∈{-1,0,1,2,3}。
- `position.txt.tmp` に書き切って `os.replace()` で原子的 rename（partial read を排除）。
- 手未検出・quad 外・不感帯でも `-1` を書き続ける（seq を進める）。Unity は seq 停止で「ロスト」と判定。
