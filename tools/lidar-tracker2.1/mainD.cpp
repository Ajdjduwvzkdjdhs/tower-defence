//LiDAR 落氷検知（Hokuyo URG 版）
/*
 ビルドは ./build.sh（mainD.cpp 冒頭の g++ を直接叩かないこと。理由は build.sh 参照）。

 これは tools/lidar-tracker2/mainD.cpp を、tools/lidar-tracker（YDLidar T-mini Plus +
 Python の落氷検知）と同じ数え方に置き換えたもの。センサと言語はそのまま
 （Hokuyo URG + C++）で、検出アルゴリズムだけを移植してある。

 ----------------------------------------------------------------------------
 何が変わったか（lidar-tracker2 → lidar-tracker2.1）
 ----------------------------------------------------------------------------
 lidar-tracker2 は「点群を OpenGL で描いてラスタ画像にし、erode/dilate と
 findContours で塊を見つけ、その重心を足の設置位置として出す」画像処理ベースの
 ツールだった。落氷を数えるにはこれでは足りない:

   - 塊の位置は出るが、時間方向の処理が無いので「何回落ちたか」を数えられない。
   - 静止物（壁・機体・氷の山）が毎フレーム同じ塊として出続けるので、
     落氷とそれ以外を区別できない。

 そこで検出部を丸ごと差し替え、lidar-tracker と同じ「角度ビン＋セクタ＋
 立ち上がりエッジ」方式にした。OpenGL / GLUT は検出にも描画にも要らなくなった
 ので落とし、表示は OpenCV だけで描いている。

 ----------------------------------------------------------------------------
 数え方（lidar-tracker の SectorCounter / RisingEdgeCounter と同じ規則）
 ----------------------------------------------------------------------------
 1. 全周を BIN_RESOLUTION_DEG 刻みの角度ビンに分け、1 スキャンぶんの点を
    「表示角（0 度＝画面上、時計回り）」でビンに入れる。
 2. RANGE_MIN..RANGE_MAX の同心円の外と、センサのエラー値は捨てる（_is_valid）。
 3. 計測セクタ（COUNT_SECTOR_*）に入った有効点だけを数の対象にする。
 4. 「有り／無し」を判定する。モードは 2 つ:
      simple     : セクタ内に有効点があれば「有り」。セクタに静止物が写らない
                   前提の、いちばん素直な数え方（既定）。
      background : 起動直後にビンごとの静止背景（距離の中位値）を学習し、
                   そこから COUNT_FOREGROUND_MARGIN_MM 以上手前を通った点だけを
                   「有り」とする。セクタに壁や機体が写っていて simple では
                   hits が 1 で張り付くときに使う。
 5. 「無し → 有り」の立ち上がりだけを 1 回と数える。氷が数スキャンにわたって
    映り続けても 1 回。点滅で 1 回が 2 回に割れないよう、COUNT_RELEASE_SEC の
    間ずっと「無し」が続くまでは同じ検出が続いているとみなす。

 lidar-tracker との違いは、センサの性質からくる 3 点だけ:

   - URG は 360 度回らず ±SCAN_ANGLE_DEG/2 の往復スキャンなので、
     「1 回転」がそのまま「1 スキャン」になる。数え方の規則は変わらない。
     見えない後方は図に灰色で塗ってある。
   - 猶予・学習の長さは回転数ではなく秒で持ち、起動時に urg.scan_usec() から
     スキャン数へ直す（URG は機種でスキャン周期が違うため。UBG-04LX-F01 は
     約 28ms/スキャン、UST-20LX-H01 は約 25ms/スキャン）。
   - Intensity（反射強度）と干渉フラグ 0x03（太陽光ノイズ）は無い。これは
     YDLidar SDK 固有の値で URG の Distance モードには存在しないので、
     色分けとフラグのカウンタは移植していない。

 ----------------------------------------------------------------------------
 操作
 ----------------------------------------------------------------------------
   r    カウンタを 0 に戻す（1 回の計測の区切り。background では背景も学習し直す）
   m    数え方を simple / background で切り替える（切り替え時に 0 に戻る）
   ESC  終了（q でも同じ）

 起動した時点から数え始める。r はリセット専用で、「押すまで数えない」には
 していない（押し忘れが「カウントが動かない」に見えるため）。
*/

#include <cstdio>
#include <cmath>
#include <csignal>
#include <cstdlib>
#include <algorithm>
#include <limits>
#include <string>
#include <vector>
#include <opencv2/opencv.hpp>
#include "Urg_driver.h"
#include "Connection_information.h"

//=============================
// 設定
//=============================

//--- スキャン -----------------------------------------------------------
// URG のスキャン角度範囲[度]。センサ正面を中心に ±SCAN_ANGLE_DEG/2 を測る。
const int SCAN_ANGLE_DEG = 180;

//--- 検出範囲（関心領域）-----------------------------------------------
// lidar-tracker の config.toml [detection] に相当する現物合わせの値。
// 外側 RANGE_MAX は「関心円」として赤線で描かれ、壁や周囲の人を落として
// かき氷機まわりの落氷だけを見るための主要なノブ。内側 RANGE_MIN は、
// センサ自身の筐体・治具・支柱など常に至近距離で返る反射を切るためのもの
// （0 なら切らない。0 以外にすると橙色の円で描画される）。
const double RANGE_MIN_CM = 0.0;
const double RANGE_MAX_CM = 80.0;
const double RANGE_MIN_MM = RANGE_MIN_CM * 10.0;
const double RANGE_MAX_MM = RANGE_MAX_CM * 10.0;

// 表示範囲 = 関心円 × 倍率。円外のコンテキストも少し見えるようにする。
const double DISPLAY_SCALE = 1.2;
const double DISPLAY_MAX_MM = RANGE_MAX_MM * DISPLAY_SCALE;

//--- 角度 ---------------------------------------------------------------
// 現物合わせ用の角度オフセット[度]。センサー正面を画面上のどこに向けるか。
// 0 度＝画面上・時計回りを基準に、この値だけ表示全体を回転させる。
const double ANGLE_OFFSET_DEG = 0.0;

// 角度ビンの解像度[度]。1.0° → 360 ビン。
// UBG-04LX-F01 のステップ角は約 0.36° なので、1 ビンに 2〜3 点入る。
// 同じビンに複数入ったときは最後の点で上書きする（lidar-tracker と同じ）。
const double BIN_RESOLUTION_DEG = 1.0;
const int NUM_BINS = 360;

//--- 計測セクタ（数える範囲）-------------------------------------------
// 図に見えているとおりの角度[度]で指定する（ANGLE_OFFSET_DEG 適用後の表示角）。
// 0 度をまたぐ指定（例 315 → 45）もできる。
// lidar-tracker の既定は 225-315 だが、あちらは 360 度スキャンで後方も見える。
// URG は前方 180 度しか見えないので、既定は正面 ±45 度にしてある。
const double COUNT_SECTOR_START_DEG = 315.0;
const double COUNT_SECTOR_END_DEG = 45.0;

//--- 時間の定数 ---------------------------------------------------------
// lidar-tracker は「回転数」で持っているが、URG は機種でスキャン周期が違うので
// 秒で持ち、起動時に urg.scan_usec() からスキャン数へ直す。
// 括弧内は lidar-tracker の既定値（10Hz 換算）で、同じ時間になるようにしてある。
const double COUNT_RELEASE_SEC = 0.2;      // 2 回転ぶんの猶予
const double COUNT_BACKGROUND_SEC = 2.0;   // 20 回転ぶんの背景学習

// background モードのしきい。「背景のばらつき」と「拾いたい氷の手前具合」の
// 折り合い。大きくすると背景の揺れを拾わなくなるが、背景ぎりぎりを通る氷も
// 落とす。小さくするとその逆。
const double COUNT_FOREGROUND_MARGIN_MM = 100.0;

//--- フィルタの ON/OFF（現物合わせで切り替える）-------------------------
// URG は無反射・近すぎ・強すぎのとき、距離ではなくエラー値（min_distance 未満の
// 小さな値）を返す。lidar-tracker の FILTER_ZERO_DISTANCE に相当。
const bool FILTER_ERROR_DISTANCE = true;
const bool FILTER_BY_RANGE = true;         // RANGE_MIN..RANGE_MAX の外を除外

//--- スキャン周期の実測 -------------------------------------------------
// 猶予・学習の長さ（秒）をスキャン数へ直すのに使う周期は、計測開始後に実測する。
// urg.scan_usec() はセンサが PP で申告する公称値で、カタログ値や実際の回転数と
// 食い違うことがあるため（UBG-04LX-F01 はカタログ 28ms に対し 25ms を申告する）。
// 実測にかかる時間ぶん（warmup + sample スキャン）だけ数え始めが遅れる。
const int MEASURE_WARMUP_SCANS = 5;    //回り始めの不安定なぶんを捨てる
const int MEASURE_SAMPLE_SCANS = 20;   //この本数の所要時間から周期を出す

//--- 描画 ---------------------------------------------------------------
// 描画は読み取りと独立。1 スキャンごとに描くとセンサより速く回せないので、
// この間隔でだけ描く（数えるのは毎スキャン）。
const int DRAW_INTERVAL_MS = 33;

const int IMAGE_WIDTH = 960;
const int IMAGE_HEIGHT = 660;
const int LEFT_COLUMN_WIDTH = 320;   // プロットの左に空ける、カウンタ用の幅
const int TOP_MARGIN = 56;           // 2 行のタイトルが収まるだけの厚み
const int BOTTOM_MARGIN = 34;        // 下端の操作説明

// 左のカウンタ欄。プロットの縦中心からの高さ[px]で置く。
const int COUNT_TEXT_LEFT = 24;
const int COUNT_HEAD_OFFSET = -170;
const int COUNT_SUB_OFFSET = -140;
const int COUNT_TUNING_OFFSET = -115;
const int COUNT_HITS_OFFSET = -30;
const int COUNT_PTS_OFFSET = 22;
const int COUNT_NOW_OFFSET = 88;

// 色（BGR）。matplotlib の tab 系に合わせてある。
const cv::Scalar COLOR_BG(255, 255, 255);
const cv::Scalar COLOR_GRID(224, 224, 224);
const cv::Scalar COLOR_TEXT(40, 40, 40);
const cv::Scalar COLOR_POINT(180, 119, 31);      // tab:blue
const cv::Scalar COLOR_RANGE_MAX(40, 39, 214);   // tab:red
const cv::Scalar COLOR_RANGE_MIN(14, 127, 255);  // tab:orange
const cv::Scalar COLOR_THRESHOLD(127, 127, 127); // tab:gray
const cv::Scalar COLOR_SECTOR(180, 119, 31);     // 通常時のセクタ塗り
const cv::Scalar COLOR_SECTOR_HIT(40, 39, 214);  // 検出中のセクタ塗り
const cv::Scalar COLOR_BLIND(200, 200, 200);     // センサが見ていない範囲
const double SECTOR_ALPHA = 0.10;
const double BLIND_ALPHA = 0.35;

const char* WINDOW_NAME = "Ice counter";

//=============================
// 小物
//=============================

static const double kNaN = std::numeric_limits<double>::quiet_NaN();

//センサ角[度] → 表示角[度]（0 度＝画面上、時計回り）
//URG の角度は正面 0 度・反時計回りが正なので、符号を反転すると時計回りになる。
//lidar-tracker と違い、ビン分けにもこの表示角を使う（オフセットは定数なので
//結果は変わらず、背景のしきい線と点が必ず同じ角度に乗る）。
static double sensor_deg_to_display(double sensorDeg)
{
    double d = std::fmod(-sensorDeg + ANGLE_OFFSET_DEG, 360.0);

    return (d < 0.0) ? d + 360.0 : d;
}

static int display_deg_to_bin(double displayDeg)
{
    int idx = (int)std::floor(displayDeg / BIN_RESOLUTION_DEG);

    return ((idx % NUM_BINS) + NUM_BINS) % NUM_BINS;
}

static double bin_center_deg(int bin)
{
    return (bin + 0.5) * BIN_RESOLUTION_DEG;
}

//計測セクタを [(開始角, 終了角), ...] で返す（表示角[度]）。
//0 度をまたぐ指定は 2 つに割れる。描画にも判定にも同じ定義を使う。
struct Span
{
    double lo, hi;
};

static std::vector<Span> sector_spans()
{
    double start = std::fmod(COUNT_SECTOR_START_DEG, 360.0);
    double end = std::fmod(COUNT_SECTOR_END_DEG, 360.0);
    if (start < 0.0) start += 360.0;
    if (end < 0.0) end += 360.0;

    std::vector<Span> spans;
    if (start <= end) {
        Span s = {start, end};
        spans.push_back(s);
    } else {
        Span a = {start, 360.0};
        Span b = {0.0, end};
        spans.push_back(a);
        spans.push_back(b);
    }

    return spans;
}

static bool in_count_sector(double displayDeg)
{
    std::vector<Span> spans = sector_spans();

    for (size_t i = 0; i < spans.size(); i++) {
        if (spans[i].lo <= displayDeg && displayDeg <= spans[i].hi) return true;
    }

    return false;
}

//フィルタ設定に照らして、その点を「有効な物体点」とみなすか。
//無効な点はビンをクリアするので、「関心円の外に壁がある方向」は点が出ない＝
//その方向の関心領域内には何も無い、と読める。
static bool is_valid(long distance, long minDistance)
{
    if (FILTER_ERROR_DISTANCE && distance < minDistance) return false;
    if (FILTER_BY_RANGE && !(RANGE_MIN_MM <= distance && distance <= RANGE_MAX_MM)) return false;

    return true;
}

//セクタ内にあった有効点（ビン番号と距離）
struct SectorPoint
{
    int bin;
    double distance;
};

//=============================
// 数え方
//=============================

//1 スキャンごとの点数を受け取り、「無し → 有り」の変わり目を数える。
//
//  count  : 無し → 有り に変わった瞬間だけ 1 回。氷が 1 個落ちて数スキャン
//           映り続けても 1 回になる。点滅で 1 回が 2 回に割れないよう、
//           releaseScans ぶん連続で無くなるまでは同じ検出が続いているとみなす。
//  points : 「有り」と数えた点の累計。1 回の検出がどれだけ濃かったかを見る値
//           なので、count と違って映っている間ずっと積み上がる。
//  last   : 最後の 1 スキャンで「有り」と数えた点の数。累計と違って増減するので、
//           「そもそも点が入ってきているのか」がその場で分かる。
class RisingEdgeCounter
{
public:
    long count;
    long points;
    int last;
    bool present;   //いま検出中とみなしているか

    RisingEdgeCounter(void) : releaseScans_(1) { clear(); }

    void set_release_scans(int scans) { releaseScans_ = std::max(1, scans); }

    void clear(void)
    {
        count = 0;
        points = 0;
        last = 0;
        present = false;
        absent_ = 0;
    }

    //1 スキャンぶんの点数を渡す。立ち上がったスキャンだけ true を返す。
    bool feed(int pointCount)
    {
        points += pointCount;
        last = pointCount;
        bool started = false;

        if (pointCount > 0) {
            if (!present) {
                count++;
                started = true;
            }
            present = true;
            absent_ = 0;
        } else if (present) {
            absent_++;
            if (absent_ >= releaseScans_) present = false;
        }

        return started;
    }

private:
    int releaseScans_;
    int absent_;
};

//計測セクタの検出回数と点数を数える。
//lidar-tracker の SectorCounter の移植。あちらは読み取りスレッドと描画スレッドで
//共有するのでロックを持っているが、こちらは 1 スレッドなので持たない。
class SectorCounter
{
public:
    enum Mode { MODE_SIMPLE, MODE_BACKGROUND };

    SectorCounter(void)
        : learnScans_(1), mode_(MODE_SIMPLE)
    {
        clear();
    }

    void configure(int releaseScans, int learnScans)
    {
        hits_.set_release_scans(releaseScans);
        learnScans_ = std::max(1, learnScans);
        clear();
    }

    //1 スキャンぶんのセクタ内の点を渡す。立ち上がったスキャンだけ true を返す。
    //background の学習中は数えないので、必ず false。
    bool feed(const std::vector<SectorPoint>& points)
    {
        if (is_learning()) {
            learn(points);

            return false;
        }

        return hits_.feed(hit_count(points));
    }

    //0 に戻して数え直す（1 回の計測の区切り）。背景も学習し直す。
    //背景も引き直すのは、置き直し・氷の山の変化のあとに古い背景で数え続けると、
    //あるはずの検出が出ない／無いはずの検出が出るのに気づけないため。
    void reset(void) { clear(); }

    //数え方を切り替える。前のモードの数字と混ざって読めなくなるので 0 に戻す。
    void toggle_mode(void)
    {
        mode_ = (mode_ == MODE_SIMPLE) ? MODE_BACKGROUND : MODE_SIMPLE;
        clear();
    }

    const RisingEdgeCounter& hits(void) const { return hits_; }

    const char* mode_name(void) const
    {
        return (mode_ == MODE_SIMPLE) ? "simple" : "background";
    }

    //学習の残りスキャン数（学習中でなければ 0）
    int learning_remaining(void) const
    {
        return is_learning() ? (learnScans_ - learned_) : 0;
    }

    //ビンごとのしきい距離[mm]（背景 - margin）。描画用。
    //simple のとき・学習中・背景が無かったビンは NaN なので線は描かれない。
    double threshold(int bin) const
    {
        if (mode_ == MODE_SIMPLE) return kNaN;

        return background_[bin] - COUNT_FOREGROUND_MARGIN_MM;
    }

private:
    RisingEdgeCounter hits_;
    int learnScans_;
    Mode mode_;
    int learned_;
    std::vector<double> background_;
    std::vector< std::vector<double> > observed_;

    void clear(void)
    {
        hits_.clear();
        background_.assign(NUM_BINS, kNaN);
        observed_.assign(NUM_BINS, std::vector<double>());
        learned_ = 0;
    }

    bool is_learning(void) const
    {
        return mode_ == MODE_BACKGROUND && learned_ < learnScans_;
    }

    //この 1 スキャンで「有り」と数える点の数（モードで判定が変わる）
    int hit_count(const std::vector<SectorPoint>& points) const
    {
        if (mode_ == MODE_SIMPLE) return (int)points.size();

        int n = 0;
        for (size_t i = 0; i < points.size(); i++) {
            if (is_foreground(points[i].bin, points[i].distance)) n++;
        }

        return n;
    }

    //背景の学習に 1 スキャンぶん取り込み、規定スキャン数に達したら背景を確定する。
    //背景はビンごとの距離の中位値なので、学習中に氷が 1 個落ちても引っぱられにくい。
    void learn(const std::vector<SectorPoint>& points)
    {
        for (size_t i = 0; i < points.size(); i++) {
            observed_[points[i].bin].push_back(points[i].distance);
        }

        learned_++;

        if (learned_ < learnScans_) return;

        for (int bin = 0; bin < NUM_BINS; bin++) {
            std::vector<double>& d = observed_[bin];
            if (d.empty()) continue;

            std::sort(d.begin(), d.end());
            size_t mid = d.size() / 2;
            background_[bin] = (d.size() % 2 == 1)
                ? d[mid]
                : (d[mid - 1] + d[mid]) * 0.5;
        }

        //学習後は溜めない（スキャンごとに増え続けてしまうので）
        observed_.assign(NUM_BINS, std::vector<double>());
    }

    //その点が静止背景より手前か。背景が無いビンに来た点はすべて手前扱い
    //（何も無い方向に来たものは、それ自体が検出）。
    bool is_foreground(int bin, double distance) const
    {
        double background = background_[bin];

        if (std::isnan(background)) return true;

        return distance < background - COUNT_FOREGROUND_MARGIN_MM;
    }
};

//=============================
// グローバル変数
//=============================

qrk::Urg_driver urg;
std::vector<long> distData;

//全周 NUM_BINS ビンの最新値。無反射・エラー・範囲外のビンは NaN。
//lidar-tracker は 360 度回るセンサなので「ビームが通ったのに物が無ければ
//そのビンをクリアする」という書き方だが、URG は毎スキャン全角度が更新されるので
//スキャンごとに一度 NaN で埋め直せば同じことになる。
std::vector<double> binDistance(NUM_BINS, kNaN);
int validCount = 0;      //直近 1 スキャンで有効だった点の数（ビンではなく点の数）
long minDistance = 20;   //センサの最短計測距離[mm]。これ未満はエラー値（open 後に入れる）

SectorCounter counter;

//描画レイアウト（起動時に一度決める）
cv::Point plotCenter;
double plotRadiusPx = 1.0;

//タイトルに出す固定文言
std::string rangeLabel;
std::string sectorLabel;
std::string deviceLabel;
std::string scanLabel;
std::string tuningLabel;

//=============================
// 関数の宣言
//=============================

void cleanupURG();
void signalHandler(int sig);
double measure_scan_period_sec();
std::vector<SectorPoint> updateScan();
void render(cv::Mat& image);

//=============================
// メイン
//=============================

int main(int argc, char* argv[])
{
    setvbuf(stdout, NULL, _IOLBF, 0);

    //URG に接続
    qrk::Connection_information information(argc, argv);
    if (!urg.open(information.device_or_ip_name(),
                  information.baudrate_or_port_number(),
                  information.connection_type())) {
        printf("URG failed! (%s)\n", urg.what());

        return 1;
    }

    urg.set_scanning_parameter(urg.deg2step(-SCAN_ANGLE_DEG / 2),
                               urg.deg2step(SCAN_ANGLE_DEG / 2), 0);

    //センサへの問い合わせは、計測を始める前に済ませてしまう。
    //product_type() は「VV」コマンドを送ってその応答を読むので、計測データが
    //流れている最中に呼ぶと応答がストリームと混ざる。product_type() 自体が
    //"receive error." を返すだけでなく、ストリームの同期が崩れて以後の
    //get_distance() が "no response" で失敗し続ける。
    //（min_distance() / scan_usec() は open 時に読んだ値を返すだけなので安全。）
    minDistance = urg.min_distance();
    std::string productType = urg.product_type();

    double nominalSec = urg.scan_usec() / 1e6;

    //タイトル用の文言（スキャン周期に関わるものは実測してから組み立てる）
    char buf[256];
    if (FILTER_BY_RANGE) {
        snprintf(buf, sizeof(buf), "range = %g-%g cm", RANGE_MIN_CM, RANGE_MAX_CM);
    } else {
        snprintf(buf, sizeof(buf), "range filter OFF, circle = %g cm", RANGE_MAX_CM);
    }
    rangeLabel = buf;
    snprintf(buf, sizeof(buf), "%g-%g", COUNT_SECTOR_START_DEG, COUNT_SECTOR_END_DEG);
    sectorLabel = buf;
    snprintf(buf, sizeof(buf), "%s   %s   (%s)",
             productType.c_str(), information.device_or_ip_name(), rangeLabel.c_str());
    deviceLabel = buf;
    printf("%s\n", deviceLabel.c_str());

    //ここから計測開始。以後センサへ問い合わせを送らないこと（上のコメント参照）
    if (!urg.start_measurement(qrk::Urg_driver::Distance,
                               qrk::Urg_driver::Infinity_times, 0)) {
        printf("start_measurement 失敗: %s\n", urg.what());

        return 1;
    }

    //終了時の後始末を登録（詳細は cleanupURG）
    atexit(cleanupURG);
    signal(SIGINT, signalHandler);
    signal(SIGTERM, signalHandler);

    //猶予と学習の長さは秒で持っているので、スキャン数へ直すために周期が要る。
    //公称値は当てにせず実測する（MEASURE_* のコメント参照）。
    double scanSec = measure_scan_period_sec();
    if (scanSec > 0.001 && scanSec < 1.0) {
        printf("scan period: %.1f ms 実測 (%.1f Hz)   センサ申告 %.1f ms\n",
               scanSec * 1000.0, 1.0 / scanSec, nominalSec * 1000.0);
    } else {
        //実測に失敗したら公称値で見積もる。それも取れなければ 10Hz 相当。
        scanSec = (nominalSec > 0.0) ? nominalSec : 0.1;
        printf("scan period: 実測に失敗したので %.1f ms（センサ申告）で見積もります\n",
               scanSec * 1000.0);
    }

    int releaseScans = std::max(1, (int)std::ceil(COUNT_RELEASE_SEC / scanSec));
    int learnScans = std::max(1, (int)std::ceil(COUNT_BACKGROUND_SEC / scanSec));
    counter.configure(releaseScans, learnScans);

    snprintf(buf, sizeof(buf), "%.1f ms/scan (%.1f Hz, measured)   scan +-%d deg",
             scanSec * 1000.0, 1.0 / scanSec, SCAN_ANGLE_DEG / 2);
    scanLabel = buf;
    snprintf(buf, sizeof(buf), "release %d / learn %d scans", releaseScans, learnScans);
    tuningLabel = buf;

    printf("%s   %s\n", scanLabel.c_str(), tuningLabel.c_str());
    printf("sector %s deg  /  mode %s\n", sectorLabel.c_str(), counter.mode_name());
    printf("r: reset counts   m: toggle mode   ESC: quit\n");

    //セクタがセンサの視野から外れていないか（URG は前方 180 度しか見えない）
    int outside = 0;
    for (int deg = 0; deg < 360; deg++) {
        double displayDeg = deg + 0.5;
        if (!in_count_sector(displayDeg)) continue;
        //センサの視野を表示角に直すと [360-SCAN/2, 360) と [0, SCAN/2]
        double half = SCAN_ANGLE_DEG / 2.0;
        if (displayDeg > half && displayDeg < 360.0 - half) outside++;
    }
    if (outside > 0) {
        printf("警告: 計測セクタのうち %d 度ぶんがセンサの視野（正面 +-%d 度）の外です。"
               "COUNT_SECTOR_* を見直してください。\n", outside, SCAN_ANGLE_DEG / 2);
    }

    //描画レイアウト
    int plotW = IMAGE_WIDTH - LEFT_COLUMN_WIDTH;
    int plotH = IMAGE_HEIGHT - TOP_MARGIN - BOTTOM_MARGIN;
    int plotSize = std::min(plotW, plotH);
    plotCenter = cv::Point(LEFT_COLUMN_WIDTH + plotW / 2, TOP_MARGIN + plotH / 2);
    plotRadiusPx = plotSize / 2.0 - 14.0;

    cv::Mat image(IMAGE_HEIGHT, IMAGE_WIDTH, CV_8UC3);
    cv::namedWindow(WINDOW_NAME, cv::WINDOW_AUTOSIZE);

    int failures = 0;
    int64 lastDraw = 0;
    double tickMs = 1000.0 / cv::getTickFrequency();

    //読み取りは 1 スキャンずつ止めずに回し（数え落としが出ないように）、
    //描画だけを DRAW_INTERVAL_MS に間引く。
    while (true) {
        long timeStamp = 0;

        if (!urg.get_distance(distData, &timeStamp)) {
            //一時的な取りこぼしは次のスキャンで戻るので続ける。
            //続けて失敗するならセンサが落ちているので抜ける。
            if (++failures >= 10) {
                printf("get_distance が続けて失敗しました: %s\n", urg.what());
                break;
            }
            continue;
        }
        failures = 0;

        std::vector<SectorPoint> points = updateScan();

        if (counter.feed(points)) {
            const RisingEdgeCounter& h = counter.hits();
            printf("hit %ld  (%d pts, total %ld pts)\n", h.count, h.last, h.points);
        }

        int64 now = cv::getTickCount();
        if ((now - lastDraw) * tickMs < DRAW_INTERVAL_MS) continue;
        lastDraw = now;

        render(image);
        cv::imshow(WINDOW_NAME, image);

        int key = cv::waitKey(1);
        if (key == 27 || key == 'q') break;
        if (key == 'r') {
            counter.reset();
            printf("counts reset  (mode %s)\n", counter.mode_name());
        }
        if (key == 'm') {
            counter.toggle_mode();
            printf("mode -> %s  (counts reset)\n", counter.mode_name());
        }
    }

    return 0;
}

//スキャン 1 本あたりの実測所要時間[秒]。計測開始後に呼ぶこと。
//読み落としがあったときは負の値を返す（呼び出し側が公称値へ退避する）。
double measure_scan_period_sec()
{
    std::vector<long> data;
    long timeStamp = 0;

    for (int i = 0; i < MEASURE_WARMUP_SCANS; i++) {
        if (!urg.get_distance(data, &timeStamp)) return -1.0;
    }

    int64 t0 = cv::getTickCount();
    for (int i = 0; i < MEASURE_SAMPLE_SCANS; i++) {
        if (!urg.get_distance(data, &timeStamp)) return -1.0;
    }

    return (cv::getTickCount() - t0) / cv::getTickFrequency() / MEASURE_SAMPLE_SCANS;
}

//1 スキャンぶんの距離データをビンへ入れ、セクタ内の有効点を返す。
std::vector<SectorPoint> updateScan()
{
    std::vector<SectorPoint> points;

    binDistance.assign(NUM_BINS, kNaN);
    validCount = 0;

    for (size_t i = 0; i < distData.size(); i++) {
        if (!is_valid(distData[i], minDistance)) continue;

        double displayDeg = sensor_deg_to_display(urg.index2deg((int)i));
        int bin = display_deg_to_bin(displayDeg);
        double distance = (double)distData[i];

        //同じビンに複数の点が入ったときは最後の点で上書きする
        binDistance[bin] = distance;
        validCount++;

        if (in_count_sector(displayDeg)) {
            SectorPoint p = {bin, distance};
            points.push_back(p);
        }
    }

    return points;
}

//=============================
// 描画
//=============================

//表示角[度]と距離[mm]を画面座標へ（0 度＝画面上、時計回り）
static cv::Point polar2px(double displayDeg, double mm)
{
    double th = displayDeg * M_PI / 180.0;
    double r = mm / DISPLAY_MAX_MM * plotRadiusPx;

    return cv::Point(cvRound(plotCenter.x + r * std::sin(th)),
                     cvRound(plotCenter.y - r * std::cos(th)));
}

//中心から広がる扇形の頂点列
static std::vector<cv::Point> wedge(double loDeg, double hiDeg, double outerMm)
{
    std::vector<cv::Point> poly;
    poly.push_back(plotCenter);

    for (double a = loDeg; a < hiDeg; a += 1.0) poly.push_back(polar2px(a, outerMm));
    poly.push_back(polar2px(hiDeg, outerMm));

    return poly;
}

static void fill_wedge(cv::Mat& image, double loDeg, double hiDeg,
                       const cv::Scalar& color, double alpha)
{
    cv::Mat overlay = image.clone();
    std::vector<cv::Point> poly = wedge(loDeg, hiDeg, DISPLAY_MAX_MM);
    std::vector< std::vector<cv::Point> > polys;
    polys.push_back(poly);

    cv::fillPoly(overlay, polys, color, cv::LINE_AA);
    cv::addWeighted(overlay, alpha, image, 1.0 - alpha, 0.0, image);
}

static void put(cv::Mat& image, const std::string& text, cv::Point org,
                double scale, const cv::Scalar& color, int thickness = 1,
                int font = cv::FONT_HERSHEY_SIMPLEX)
{
    cv::putText(image, text, org, font, scale, color, thickness, cv::LINE_AA);
}

void render(cv::Mat& image)
{
    char buf[256];

    image.setTo(COLOR_BG);

    //センサが見ていない範囲（URG は前方 SCAN_ANGLE_DEG しか測らない）
    double half = SCAN_ANGLE_DEG / 2.0;
    if (half < 180.0) fill_wedge(image, half, 360.0 - half, COLOR_BLIND, BLIND_ALPHA);

    //計測セクタ。検出中は色を変えて、離れた所からでも当たったのが分かるようにする
    bool detecting = counter.hits().present && counter.learning_remaining() == 0;
    std::vector<Span> spans = sector_spans();
    for (size_t i = 0; i < spans.size(); i++) {
        fill_wedge(image, spans[i].lo, spans[i].hi,
                   detecting ? COLOR_SECTOR_HIT : COLOR_SECTOR, SECTOR_ALPHA);
    }

    //目盛りの円と放射線
    for (int i = 1; i <= 4; i++) {
        double mm = DISPLAY_MAX_MM * i / 4.0;
        cv::circle(image, plotCenter, cvRound(mm / DISPLAY_MAX_MM * plotRadiusPx),
                   COLOR_GRID, 1, cv::LINE_AA);
    }
    for (int deg = 0; deg < 360; deg += 30) {
        cv::line(image, plotCenter, polar2px(deg, DISPLAY_MAX_MM), COLOR_GRID, 1, cv::LINE_AA);
    }
    for (int deg = 0; deg < 360; deg += 90) {
        snprintf(buf, sizeof(buf), "%d", deg);
        cv::Point p = polar2px(deg, DISPLAY_MAX_MM * 1.04);
        put(image, buf, cv::Point(p.x - 8, p.y + 4), 0.4, COLOR_GRID, 1);
    }

    //検出範囲の境界。外側＝赤（関心円）、内側＝橙（0 のときは描かない）
    cv::circle(image, plotCenter, cvRound(RANGE_MAX_MM / DISPLAY_MAX_MM * plotRadiusPx),
               COLOR_RANGE_MAX, 1, cv::LINE_AA);
    if (RANGE_MIN_MM > 0.0) {
        cv::circle(image, plotCenter, cvRound(RANGE_MIN_MM / DISPLAY_MAX_MM * plotRadiusPx),
                   COLOR_RANGE_MIN, 1, cv::LINE_AA);
    }

    //background モードで学習した静止背景のしきい線（背景 - margin）。
    //この線より手前（内側）を通った点だけを数える。点を並べて破線に見せる。
    for (int bin = 0; bin < NUM_BINS; bin++) {
        double th = counter.threshold(bin);
        if (std::isnan(th) || th <= 0.0 || th > DISPLAY_MAX_MM) continue;

        cv::circle(image, polar2px(bin_center_deg(bin), th), 1,
                   COLOR_THRESHOLD, cv::FILLED, cv::LINE_AA);
    }

    //物体点
    for (int bin = 0; bin < NUM_BINS; bin++) {
        if (std::isnan(binDistance[bin]) || binDistance[bin] > DISPLAY_MAX_MM) continue;

        cv::circle(image, polar2px(bin_center_deg(bin), binDistance[bin]), 2,
                   COLOR_POINT, cv::FILLED, cv::LINE_AA);
    }

    //タイトル（2 行）
    put(image, deviceLabel, cv::Point(LEFT_COLUMN_WIDTH, 22), 0.45, COLOR_TEXT, 1);
    snprintf(buf, sizeof(buf), "%s   %zu pts/scan   valid %4d pts",
             scanLabel.c_str(), distData.size(), validCount);
    put(image, buf, cv::Point(LEFT_COLUMN_WIDTH, 42), 0.42, COLOR_TEXT, 1);

    //左のカウンタ欄。計測中に離れた所から読むので、タイトルではなくここに大きく出す
    const RisingEdgeCounter& h = counter.hits();
    int cy = plotCenter.y;
    int left = COUNT_TEXT_LEFT;

    put(image, "URG", cv::Point(left, cy + COUNT_HEAD_OFFSET), 0.6, COLOR_TEXT, 1);
    snprintf(buf, sizeof(buf), "%s deg  /  %s", sectorLabel.c_str(), counter.mode_name());
    put(image, buf, cv::Point(left, cy + COUNT_SUB_OFFSET), 0.5, COLOR_TEXT, 1);
    put(image, tuningLabel, cv::Point(left, cy + COUNT_TUNING_OFFSET), 0.42, COLOR_TEXT, 1);

    int remaining = counter.learning_remaining();
    if (remaining > 0) {
        //学習中はまだ数えていない。0 のままの数字を出すと「動いていない」に
        //見えるので、数字は伏せて、学習中であることと残りスキャン数を出す。
        put(image, "  -- hits", cv::Point(left, cy + COUNT_HITS_OFFSET), 1.1, COLOR_TEXT, 2);
        put(image, "  -- pts", cv::Point(left, cy + COUNT_PTS_OFFSET), 1.1, COLOR_TEXT, 2);
        snprintf(buf, sizeof(buf), "learning background ... %3d scans left", remaining);
        put(image, buf, cv::Point(left, cy + COUNT_NOW_OFFSET), 0.42, COLOR_TEXT, 1);
    } else {
        snprintf(buf, sizeof(buf), "%4ld hits", h.count);
        put(image, buf, cv::Point(left, cy + COUNT_HITS_OFFSET), 1.1, COLOR_TEXT, 2);
        snprintf(buf, sizeof(buf), "%4ld pts", h.points);
        put(image, buf, cv::Point(left, cy + COUNT_PTS_OFFSET), 1.1, COLOR_TEXT, 2);

        //now は直近 1 スキャンで「有り」と数えた点数。hits が増えないときに、
        //点が入っていないのか（now も 0）、入っているのに立ち上がらないのか
        //（now > 0）を、その場で切り分けるために出している。
        snprintf(buf, sizeof(buf), "now %3d%s", h.last, h.present ? "   DETECTING" : "");
        put(image, buf, cv::Point(left, cy + COUNT_NOW_OFFSET), 0.5,
            h.present ? COLOR_SECTOR_HIT : COLOR_TEXT, 1);
    }

    //下端の操作説明
    put(image, "r: reset counts    m: mode (simple / background)    ESC: quit",
        cv::Point(left, IMAGE_HEIGHT - 12), 0.45, COLOR_TEXT, 1);
}

//=============================
// 後始末
//=============================

//計測を止めずに終了するとセンサはデータを流し続け、次回起動時の urg.open() が
//応答を掴めずハングする（Dockにアイコンは出るがウィンドウが出ない）。
//そうなった場合は urg_reset.sh で復帰できる。
void cleanupURG()
{
    static bool done = false;
    if (done) return;  //二重に呼ばれても一度だけ
    done = true;

    urg.stop_measurement();
    urg.close();
}

//シグナルハンドラ(Ctrl+C，kill)
void signalHandler(int sig)
{
    exit(0);  //atexitで登録した cleanupURG() が走る
}
