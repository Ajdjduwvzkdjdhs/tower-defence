#!/usr/bin/env bash
#
# 走りっぱなしになった計測を止めてセンサを受付可能な状態に戻す。
#
#   ./urg_reset.sh [ポート]      既定: /dev/cu.usbmodem101
#
# a.out は Infinity_times で計測を開始するが、Ctrl-C や強制終了で落ちると
# 計測停止コマンド(QT)を送らずに死ぬ。センサはデータを流し続けたままなので、
# 次に起動したとき urg.open() が応答を掴めずハングする
# （＝Dock にアイコンは出るがウィンドウが出ない）。
# そのときはこれを実行してから a.out を起動し直す。USB 抜き差しでも直る。

set -euo pipefail
DEV="${1:-/dev/cu.usbmodem101}"

if [ ! -e "$DEV" ]; then
    echo "$DEV がありません。ls /dev/tty.usb* /dev/cu.usb* で確認してください。" >&2
    exit 1
fi

if lsof "$DEV" >/dev/null 2>&1; then
    echo "$DEV を使用中のプロセスがあります。先に終了させてください:" >&2
    lsof "$DEV" >&2
    exit 1
fi

stty -f "$DEV" 115200 raw -echo

perl -e '
use strict; use warnings; use Fcntl;
my $dev = shift;
sysopen(my $fh, $dev, O_RDWR | O_NONBLOCK) or die "open $dev: $!\n";
my $drain = sub { my ($b, $t) = ("", 0); while (defined(my $n = sysread($fh, $b, 65536))) { last if $n == 0; $t += $n; } $t };
for my $i (1 .. 8) {
    syswrite($fh, "\nQT\n");
    select undef, undef, undef, 0.3;
    $drain->();
}
select undef, undef, undef, 0.5; $drain->();
select undef, undef, undef, 1.0;
my $still = $drain->();
close $fh;
if ($still > 0) { print "まだ $still バイト流れています。USB を抜き差ししてください。\n"; exit 1; }
print "計測を停止しました。a.out を起動できます。\n";
' "$DEV"
