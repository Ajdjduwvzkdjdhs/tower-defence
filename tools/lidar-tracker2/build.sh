#!/usr/bin/env bash
#
# lidar-tracker2 (Hokuyo UBG-04LX-F01) のビルド。
#
#   ./build.sh        ソースが a.out より新しいときだけビルド
#   ./build.sh -f     常にビルドし直す
#   ./a.out           実行（ビルドは変更時だけでよい）
#
# mainD.cpp 冒頭のコメントにあるビルドコマンドは使わないこと。
# あれは `pkg-config --libs opencv4` で OpenCV の全モジュール（gapi / sfm /
# viz / dnn …）をリンクするが、Homebrew が openvino・gflags・jsoncpp・protobuf
# を上げた結果それらが古い dylib を掴めなくなり、起動時に dyld が abort する。
# mainD.cpp が実際に使うのは core / imgproc / highgui の 3 つだけで、
# この 3 つは壊れた依存を持たないので、明示的にそれだけをリンクする。
#
# liburg_cpp.a は URG ライブラリの静的ライブラリ（arm64）。
# 元は LiDAR 一式と一緒に受け取ったもので、このフォルダに置いてある。

set -euo pipefail

cd "$(dirname "$0")"

SOURCES=(mainD.cpp Connection_information.cpp)
OUT=a.out
CHECK_SOURCES=(urg_check.cpp Connection_information.cpp)
CHECK_OUT=urg_check
OPENCV_PREFIX=/opt/homebrew/opt/opencv

force=0
[ "${1:-}" = "-f" ] && force=1

if [ ! -f liburg_cpp.a ]; then
    echo "liburg_cpp.a がありません。URG ライブラリの静的ライブラリをこのフォルダに置いてください。" >&2
    exit 1
fi

needs_build() {  # $1=出力, 残り=ソース
    local out="$1"; shift
    [ "$force" -eq 1 ] && return 0
    [ -x "$out" ] || return 0
    local src
    for src in "$@" *.h liburg_cpp.a; do
        [ "$src" -nt "$out" ] && return 0
    done
    return 1
}

built=0

if needs_build "$CHECK_OUT" "${CHECK_SOURCES[@]}"; then
    g++ -O2 "${CHECK_SOURCES[@]}" -std=c++11 -I. liburg_cpp.a -o "$CHECK_OUT" -Wno-deprecated
    echo "ビルド完了: $CHECK_OUT"
    built=1
fi

if ! needs_build "$OUT" "${SOURCES[@]}"; then
    [ "$built" -eq 0 ] && echo "変更なし。$OUT / $CHECK_OUT は最新です（強制ビルドは ./build.sh -f）。"
    exit 0
fi

g++ -O3 "${SOURCES[@]}" -std=c++11 \
    -I"$OPENCV_PREFIX/include/opencv4" -L"$OPENCV_PREFIX/lib" \
    -lopencv_core -lopencv_imgproc -lopencv_highgui \
    -framework OpenGL -framework GLUT \
    liburg_cpp.a -Wno-deprecated \
    -o "$OUT"

echo "ビルド完了: $OUT"
echo
echo "疎通確認（GUI なし）:  ./urg_check"
echo "実行:                  ./$OUT   （-s /dev/tty.usbmodemXXX でポート上書き可）"
echo "ハングしたら:          ./urg_reset.sh   → もう一度 ./$OUT"
