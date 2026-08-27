#include <Arduino.h>
/* ============================================================================
 *  SmsPingBridge — Cầu nối trong suốt USB(PC) <-> UART(SIM7600CE) trên ESP32
 *  ----------------------------------------------------------------------------
 *  ESP32 nhận lệnh AT từ app PC qua cổng USB (COM) rồi chuyển thẳng xuống
 *  SIM7600CE, và trả kết quả ngược lại. App SmsPing.exe trên PC dùng NGUYÊN
 *  như cũ — chỉ cần chọn đúng cổng COM của ESP32.
 *
 *  >>> CHỈNH CHÂN GPIO cho ĐÚNG board của bạn ở phần CẤU HÌNH bên dưới <<<
 *  Mặc định theo board LilyGO T-SIM7600 (ESP32).
 * ==========================================================================*/

// ===================== CẤU HÌNH (sửa theo board của bạn) =====================
#define MODEM_TX_PIN    27      // Chân ESP32 nối tới RX của SIM7600 (ESP32 TX)
#define MODEM_RX_PIN    26      // Chân ESP32 nối tới TX của SIM7600 (ESP32 RX)
#define MODEM_PWRKEY    4       // Chân PWRKEY của SIM7600 (xung để bật modem)
#define MODEM_POWER_ON  25      // Chân cấp nguồn/enable modem (HIGH = bật). -1 nếu board không có
#define MODEM_BAUD      115200  // Baud UART giữa ESP32 và SIM7600 (mặc định SIM7600 = 115200)
#define PC_BAUD         9600    // Baud USB tới PC — GIỮ 9600 để khớp app SmsPing.exe (đang mở COM 9600)

// Board hay gặp:
//  - LilyGO T-SIM7600  : TX=27  RX=26  PWRKEY=4   POWER_ON=25
//  - LilyGO T-A7670/PCIE: TX=26  RX=27  PWRKEY=4   POWER_ON=(tùy rev, thử -1 hoặc 12)
//  - Board tự ráp       : đặt TX/RX = 2 chân UART bạn nối, PWRKEY = chân điều khiển
// ============================================================================

HardwareSerial SerialAT(1);   // UART1 của ESP32 dùng cho SIM7600

void powerOnModem() {
  if (MODEM_POWER_ON >= 0) {
    pinMode(MODEM_POWER_ON, OUTPUT);
    digitalWrite(MODEM_POWER_ON, HIGH);   // cấp nguồn cho modem
  }
  pinMode(MODEM_PWRKEY, OUTPUT);
  // Xung PWRKEY bật modem (theo trình tự chuẩn của SIM7600/LilyGO)
  digitalWrite(MODEM_PWRKEY, LOW);   delay(100);
  digitalWrite(MODEM_PWRKEY, HIGH);  delay(1000);
  digitalWrite(MODEM_PWRKEY, LOW);
}

void setup() {
  Serial.begin(PC_BAUD);                 // cổng USB tới PC
  // Đệm nhận lớn để không mất dữ liệu khi modem trả về nhanh (115200) mà PC đọc chậm (9600)
  SerialAT.setRxBufferSize(4096);
  SerialAT.begin(MODEM_BAUD, SERIAL_8N1, MODEM_RX_PIN, MODEM_TX_PIN);

  delay(500);
  powerOnModem();
  // SIM7600 cần ~10-15s để khởi động và bắt sóng. Cắm board xong hãy chờ rồi mới Connect trên PC.
}

void loop() {
  // Cầu nối trong suốt 2 chiều
  while (Serial.available())   SerialAT.write(Serial.read());   // PC  -> SIM7600
  while (SerialAT.available()) Serial.write(SerialAT.read());   // SIM7600 -> PC
}
