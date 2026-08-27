### LiDARプログラムについて

LiDARを動作させるプログラムは `tools/lidar-tracker/valley_tracker.py` です。lidar-tracker2や2.1は検証用のものなので、内覧会では使用しません。

また、`valley_tracker.py` の動作には `tools/lidar-tracker` に適切な設定値が書かれた `config.toml` を配置する必要があります。添付した `config.toml` を使用してください。設定値の詳細は添付した `config.toml` のコメントを見てください。氷検知の精度をよくするために設定値を環境に合わせて変更する必要があります。特に重要なのが `range_max_cm` や `intensity_max` です。

---

## valley_tracker.pyを動かすための環境構築 (tools/lidar-tracker/README.mdより抜粋)

### セットアップ

```bash
brew install cmake swig # SDK のビルドに必要（初回のみ）
cd tools/lidar-tracker
chmod +x ./setup-sdk.sh # 権限追加
./setup-sdk.sh # SDK を vendor/ に取得しパッチを当てる（初回のみ）
uv sync # .venv を作り依存を導入。SDK もここでビルドされる。Python は 3.14

```

* Discordに添付した `config.toml` ファイルを `tools/lidar-tracker` に配置する。
* `uv sync` は `vendor/YDLidar-SDK` を **path 依存** としてビルドする（`pyproject.toml` の `[tool.uv.sources]`）。git 依存にしていないのは、uv がキャッシュ上に clone したものへはパッチを当てられないため。`vendor/` は gitignore 済みで、リポジトリに入るのはパッチだけ。
* **シリアルポートの確認（macOS）:**
```bash
ls /dev/tty.usb* /dev/cu.usb*

```


例: `/dev/tty.usbserial-0001`
2台つなぐと `-0001` と `-0002` のように2つ出る。どちらがどの機体かは、片方だけ挿して確認するのが確実（徳久研LiDARが0002, 小栗研LiDARが0001）。

### 実行

* `uv run python valley_tracker.py`
本番。落氷を谷へ写して Unity へ渡す。
* `uv run python valley_tracker.py --headless`
窓なし（本番で CPU を空けたいとき）。
* `uv run python scan_plot_realtime.py`
可視化のみ。範囲・ノイズの下見用。

どちらもウィンドウを閉じるか Ctrl-C で安全停止する（stop_scan → close）。

ウィンドウは `config.toml` の台数ぶんの段になる（1台なら1段、2台なら上下2段）。
起動時に1台でも開けなければ、先に開いた台を閉じてから終了する。

---

### 最後に注意点

`uv run python valley_tracker.py` **は必ず `tools/lidar-tracker` ディレクトリにいる時に実行してください。**
kakigori_demoのルートなど他ディレクトのパスからだと、uv環境が正しく解決できないので、エラーが出てしまいます。