#!/usr/bin/env bash
#
# 公式 SDK（YDLidar-SDK）を vendor/ に取得し、macOS 用パッチを当てる。
#
#   ./setup-sdk.sh && uv sync
#
# uv sync は vendor/YDLidar-SDK を path 依存としてビルドする（pyproject.toml）。
# git 依存にしないのは、uv のキャッシュ上の clone にはパッチを当てられないため。
#
# パッチの中身は patches/ydlidar-sdk-macos.patch と README を参照。
# SDK 本体は Linux / Windows 向けで、素の macOS ではビルドが通らない。

set -euo pipefail

REPO="https://github.com/YDLIDAR/YDLidar-SDK.git"
# 検証済みのコミットに固定する。上げるときはパッチが当たるか確認すること
COMMIT="01cdda4f2b36dff2a706d0535c64228d863c7411"

cd "$(dirname "$0")"
DEST="vendor/YDLidar-SDK"
PATCH="$PWD/patches/ydlidar-sdk-macos.patch"

for cmd in git cmake swig; do
    if ! command -v "$cmd" >/dev/null 2>&1; then
        echo "$cmd が必要です（macOS: brew install cmake swig）" >&2
        exit 1
    fi
done

if [ -d "$DEST" ]; then
    echo "$DEST は既にあります。作り直すには rm -rf $DEST してから再実行してください。"
    exit 0
fi

mkdir -p vendor
git clone --filter=blob:none "$REPO" "$DEST"
git -C "$DEST" checkout --quiet "$COMMIT"
git -C "$DEST" apply "$PATCH"

echo
echo "SDK を $DEST に用意しました（$COMMIT + macOS パッチ）。"
echo "続けて 'uv sync' でビルド・インストールされます。"
