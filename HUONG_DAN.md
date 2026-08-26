# SMSPING (USB/COM) — Bo gộp ESP32 + SIM7600CE

App PC nói chuyện với SIM7600CE **qua cổng COM (USB)** bằng lệnh AT — chạy
**giống hệt** app dùng SIM7600G-H cắm USB. Chỉ khác: bo gộp cần nạp firmware
passthrough để USB dẫn thẳng vào modem.

---

## A. NẠP FIRMWARE PASSTHROUGH CHO ESP32 (chỉ làm 1 lần)

1. Arduino IDE → board **"ESP32 Dev Module"**.
2. Mở `esp32_passthrough/esp32_passthrough.ino`.
3. **Sửa chân cho đúng bo của bạn** (4 hằng số ở đầu file). Mặc định đang set
   theo LILYGO T-SIM7600. Bo khác thì tra sơ đồ chân của bo đó:
   ```
   MODEM_TX_PIN, MODEM_RX_PIN, MODEM_PWRKEY, MODEM_POWER_ON
   ```
4. Upload. Xong, ESP32 trở thành "cầu nối trong suốt" USB ⇄ modem.

> Nếu bo của bạn có sẵn cổng USB đi thẳng vào SIM7600CE (một số bo có 2 cổng
> USB, một cho ESP32 một cho modem) thì **không cần** firmware này — cắm đúng
> cổng modem là chạy luôn như SIM7600G-H.

---

## B. CHẠY APP (bản Python)

```
pip install PyQt5 pyserial
python pc_app/smsping_usb.py
```

- Cắm USB bo vào PC → bấm **Làm mới** → chọn **cổng COM** của mạch.
- Bấm **Kết nối** → **Kiểm tra / Đọc thông tin** (đọc IMEI thật, sóng, nhà mạng).
- Nhập số + nội dung → **GỬI SMS** (hỗ trợ tiếng Việt có dấu).
- Ô **IMEI (ghi chú)**: để trống, mua được mạch thì điền.

---

## C. BUILD RA FILE .EXE

### Cách 1 — GitHub Actions (khuyên dùng, không cần cài gì trên máy)
1. Đẩy cả thư mục này lên 1 repo GitHub.
2. Vào tab **Actions** → workflow **"Build SMSPING EXE"** chạy tự động
   (hoặc bấm **Run workflow**).
3. Chạy xong tải mục **Artifacts → SMSPING-exe** → được `SMSPING.exe`.

### Cách 2 — Tự build trên máy Windows
```
pip install PyQt5 pyserial pyinstaller
pyinstaller --onefile --noconsole --name SMSPING pc_app/smsping_usb.py
```
File ở `dist/SMSPING.exe` — copy chạy trên máy khác không cần cài Python.

---

## D. LƯU Ý

- Driver: SIM7600G-H/CE thường cần driver USB (SimTech). Nếu PC không thấy cổng
  COM, cài driver của hãng module.
- App đọc IMEI thật bằng `AT+CGSN` để quản lý — **không** đổi/giả IMEI.
- Gửi lỗi thì kiểm tra: SIM còn tiền & bật SMS, sóng CSQ ≥ 10, ăng-ten, nguồn 5V/2A.
- Số nên nhập dạng `84…` cho chắc.
