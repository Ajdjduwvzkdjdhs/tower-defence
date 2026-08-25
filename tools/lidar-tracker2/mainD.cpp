//LiDAR
/*
g++ -O3 mainD.cpp -std=c++11 `pkg-config --cflags --libs opencv4` -framework OpenGL -framework GLUT Connection_information.cpp liburg_cpp.a -Wno-deprecated
*/
#include <iostream>
#include <time.h>
#include <math.h>
#include <csignal>
#include <cstdlib>
#include <opencv2/opencv.hpp>
#include <GLUT/glut.h>
#include "Urg_driver.h"
#include "Connection_information.h"

//#define DATASIZE 1440 //UST-20LX-H01
//#define DATASIZE 513 //UBG-04LX-F01

//三次元ベクトル構造体: Vec_3D
typedef struct _Vec_3D
{
    double x, y, z;
} Vec_3D;

//関数名の宣言
void display();
void reshape(int w, int h);
void timer(int value);
void keyboard(unsigned char key, int x, int y);
void initGL();
void cleanupURG();
void signalHandler(int sig);

//グローバル変数
double fr = 30;
//URG
qrk::Urg_driver urg;
std::vector<long> distData;
//スキャン設定
int scanAngle = 180;  //角度
//double scanAngleStep = 0.35;  //スキャンステップ角（UBG-04LX-F01）
//double scanAngleStep = 0.125;  //スキャンステップ角（UST-20LX-H01）
double scanAreaW = 1080.0, scanAreaH = 560.0, scanAreaB = 0.0;  //領域サイズ（mm）
double scanReso = 1.0;  //1画素あたりのサイズ（mm）
//処理用画像
cv::Mat scanImage0, scanImage;
//投影変換行列
GLdouble model[16], proj[16];
GLint view[4];

double rDisp = 1.0;  //Retina

//メイン関数
int main(int argc, char* argv[])
{
    glutInit(&argc, argv);  //OpenGL初期化
    
    //URG
    qrk::Connection_information information(argc, argv);
    //接続
    if (!urg.open(information.device_or_ip_name(), information.baudrate_or_port_number(),information.connection_type())) {
        printf("URG failed!\n");
        return 1;
    }
    urg.set_scanning_parameter(urg.deg2step(-scanAngle/2), urg.deg2step(scanAngle/2), 0);  //スキャン角度範囲
    urg.start_measurement(qrk::Urg_driver::Distance, qrk::Urg_driver::Infinity_times, 0);  //スキャン開始
    
    //終了時の後始末を登録（詳細は cleanupURG()）
    atexit(cleanupURG);  //ESC・正常終了
    signal(SIGINT, signalHandler);  //Ctrl+C
    signal(SIGTERM, signalHandler);  //kill
    
    //GL初期設定
    initGL();
    
    //スキャン用画像
    scanImage0 = cv::Mat(cv::Size(scanAreaW/scanReso*rDisp, scanAreaH/scanReso*rDisp), CV_8UC3);
    scanImage = cv::Mat(cv::Size(scanAreaW/scanReso, scanAreaH/scanReso), CV_8UC3);
    
    //イベント待ち無限ループ突入
    glutMainLoop();
    
    return 0;
}

//終了時の後始末
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

//GL初期設定
void initGL()
{
    //描画ウィンドウ生成
    glutInitWindowSize(scanAreaW/scanReso, scanAreaH/scanReso);  //ウィンドウサイズ指定
    glutInitWindowPosition(0, 0);
    glutInitDisplayMode(GLUT_RGBA | GLUT_DEPTH);  //ディスプレイモード設定
    glutCreateWindow("CG");  //ウィンドウ生成
    
    //コールバック関数
    glutDisplayFunc(display);  //ディスプレイ
    glutReshapeFunc(reshape);  //リシェイプ
    glutKeyboardFunc(keyboard);  //キーボード
    glutTimerFunc(1000/fr, timer, 0);  //タイマー
    
    //背景色
    glClearColor(0.0, 0.0, 0.0, 1.0);
    //Zバッファの有効化
    glEnable(GL_DEPTH_TEST);
}

void display()
{
    //画面消去
    glClear(GL_COLOR_BUFFER_BIT | GL_DEPTH_BUFFER_BIT);
    
    //行列初期化
    glLoadIdentity();
    
    //----------スキャン映像生成----------
    glColor4d(1.0, 1.0, 1.0, 1.0);
    glPointSize(1.0);
    //glBegin(GL_POINTS);
    for (int i=0; i<distData.size(); i++) {
        if (distData[i]<50) continue;
        
        //スキャン点情報を座標に変換
        double rad = urg.index2rad(i);  //i番目のスキャン点の角度(ラジアン)
        double x = -distData[i]*sin(rad);
        double y = distData[i]*cos(rad);
        double z = 0;
        
        //スキャン点描画
        //glVertex3d(x, y, z);
        
        glPushMatrix();
        glTranslated(x, y, 0);
        glRotated(rad*180.0/M_PI, 0.0, 0.0, 1.0);
        glScaled(distData[i]*0.03, 5.0, 1.0);
        glutSolidSphere(1.0, 18, 9);
        glPopMatrix();
        
    }
    //glEnd();
    
    
    //----------足設置位置検出----------
    std::vector<Vec_3D> footPoints;
    
    //スキャン映像取得
    glReadPixels(0, 0, scanImage0.cols, scanImage0.rows, GL_BGR, GL_UNSIGNED_BYTE, scanImage0.data);
    cv::resize(scanImage0, scanImage, scanImage.size());
    cv::flip(scanImage, scanImage, 0);
    cv::Mat grayImage;
    cv::cvtColor(scanImage, grayImage, cv::COLOR_BGR2GRAY);  //グレースケール化
    cv::erode(grayImage, grayImage, cv::Mat(), cv::Point(-1,-1), 2);
    cv::dilate(grayImage, grayImage, cv::Mat(), cv::Point(-1,-1), 4);
    
    //領域抽出
    std::vector< std::vector<cv::Point> > contours;  //輪郭情報
    cv::findContours(grayImage, contours, cv::RETR_EXTERNAL, cv::CHAIN_APPROX_NONE);  //輪郭検出
    
    for (int i=0; i<contours.size(); i++) {
        double area = cv::contourArea(contours[i]);  //領域iの面積
        if (area>500) {  //ある程度のサイズの領域のみ処理
            //領域中心座標
            cv::Moments m = cv::moments(contours[i]);
            cv::Point p = cv::Point(m.m10/m.m00 , m.m01/m.m00);

            cv::drawContours(scanImage, contours, i, cv::Scalar(0,255,0), -1);  //i番目の領域塗りつぶし
            cv::circle(scanImage, p, 3, cv::Scalar(0,0,255), -1);  //中心点描画

            //表示
            printf("%d, %d\n", p.x, p.y);
        }
    }
    
    printf("----------\n");

    cv::imshow("Scan", scanImage);
        
    glFlush();
}

//リシェイプコールバック関数
void reshape(int w, int h)
{
    glViewport(0, 0, w*rDisp, h*rDisp);
    glMatrixMode(GL_PROJECTION);
    glLoadIdentity();
    glOrtho(-scanAreaW*0.5, scanAreaW*0.5, scanAreaB, scanAreaH+scanAreaB, -100, 100);
    glMatrixMode(GL_MODELVIEW);
    
    glGetDoublev(GL_MODELVIEW_MATRIX, model);  //モデルビュー変換行列を"model[]"に格納
    glGetDoublev(GL_PROJECTION_MATRIX, proj);  //投影変換行列を"proj[]"に格納
    glGetIntegerv(GL_VIEWPORT, view);  //ビューポート設定を"view[]"に格納
    for (int i=0; i<4; i++) {
        view[i] /= rDisp;
    }

}

//タイマーコールバック関数
void timer(int value)
{
    
    //LiDAR
    long time_stamp = 0;
    for (int i=0; i<2; i++) {
        urg.get_distance(distData, &time_stamp);  //センサから値を取得→distData
        //printf("%ld\n", time_stamp);
    }
    
    //ディスプレイイベント発生
    glutPostRedisplay();  //ディスプレイイベント強制発生
    glutTimerFunc(1000/fr, timer, 0);  //タイマー再設定
}

//キーボードコールバック関数(key:キーの種類，x,y:座標)
void keyboard(unsigned char key, int x, int y)
{
    FILE *fp;
    
    switch (key) {
        case 27:
            exit(0);
            break;
            
        default:
            break;
    }
}
