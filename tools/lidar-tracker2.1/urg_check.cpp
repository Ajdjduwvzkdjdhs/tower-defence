// LiDAR 単体の疎通確認（GUI なし）。
#include <cstdio>
#include <vector>
#include "Urg_driver.h"
#include "Connection_information.h"

int main(int argc, char* argv[])
{
    setvbuf(stdout, NULL, _IOLBF, 0);
    qrk::Connection_information info(argc, argv);
    printf("接続先: %s (%ld)\n", info.device_or_ip_name(), info.baudrate_or_port_number());

    qrk::Urg_driver urg;
    if (!urg.open(info.device_or_ip_name(), info.baudrate_or_port_number(), info.connection_type())) {
        printf("open 失敗: %s\n", urg.what());
        return 1;
    }
    printf("open OK\n");
    printf("  製品: %s / FW: %s / SN: %s\n", urg.product_type(), urg.firmware_version(), urg.serial_id());
    printf("  ステップ: %d..%d / 距離: %ld..%ld mm / 1スキャン: %ld us / 最大点数: %d\n",
           urg.min_step(), urg.max_step(), urg.min_distance(), urg.max_distance(),
           urg.scan_usec(), urg.max_data_size());

    urg.set_scanning_parameter(urg.deg2step(-90), urg.deg2step(90), 0);
    if (!urg.start_measurement(qrk::Urg_driver::Distance, 10, 0)) {
        printf("start_measurement 失敗: %s\n", urg.what());
        return 1;
    }
    for (int i = 0; i < 10; i++) {
        std::vector<long> data; long ts = 0;
        if (!urg.get_distance(data, &ts)) { printf("get_distance 失敗: %s\n", urg.what()); return 1; }
        long front = data.empty() ? -1 : data[data.size()/2];
        long mn = 999999, mx = 0; int valid = 0;
        for (size_t j = 0; j < data.size(); j++) {
            if (data[j] < 20) continue;
            valid++; if (data[j] < mn) mn = data[j]; if (data[j] > mx) mx = data[j];
        }
        printf("frame %2d: %zu点 (有効 %d) ts=%ld 正面=%ld mm 最短=%ld 最長=%ld\n",
               i, data.size(), valid, ts, front, valid ? mn : -1, mx);
    }
    urg.stop_measurement(); urg.close();
    printf("OK: LiDAR は正常に応答しています。\n");
    return 0;
}
