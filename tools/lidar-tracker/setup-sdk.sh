#!/usr/bin/env bash
#
# 公式 SDK（YDLidar-SDK）を vendor/ に取得し、macOS 用パッチを当てる。
#
#   ./setup-sdk.sh && uv sync
#
# uv sync は vendor/YDLidar-SDK を path 依存としてビルドする（pyproject.toml）。
# git 依存にしないのは、uv のキャッシュ上の clone にはパッチを当てられないため。
#
# パッチは 2 本ある（中身は各ファイルの先頭と README を参照）:
#   ydlidar-sdk-macos.patch      SDK 本体は Linux / Windows 向けで、素の macOS
#                                ではビルドが通らないのを直す
#   ydlidar-sdk-expose-flag.patch LaserPoint に干渉フラグ（阳光/玻璃ノイズ）を
#                                通す。本家は判定に使ったあと捨てていて、
#                                呼び出し側から「どの点が疑わしいと判定されたか」
#                                が分からないため

set -euo pipefail

REPO="https://github.com/YDLIDAR/YDLidar-SDK.git"
# 検証済みのコミットに固定する。上げるときはパッチが当たるか確認すること
COMMIT="01cdda4f2b36dff2a706d0535c64228d863c7411"

cd "$(dirname "$0")"
DEST="vendor/YDLidar-SDK"
PATCHES=(
    "$PWD/patches/ydlidar-sdk-macos.patch"
    "$PWD/patches/ydlidar-sdk-expose-flag.patch"
)

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
for patch in "${PATCHES[@]}"; do
    git -C "$DEST" apply "$patch"
done

echo
echo "SDK を $DEST に用意しました（$COMMIT + パッチ ${#PATCHES[@]} 本）。"
echo "続けて 'uv sync' でビルド・インストールされます。"
