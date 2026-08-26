# SMSPING — build EXE bằng GitHub

App PC gửi SMS qua SIM7600CE / SIM7600G-H (cổng USB/COM, lệnh AT).
Repo này có sẵn workflow để **GitHub tự build ra file `SMSPING.exe`**.

## Các bước (làm 1 lần)

1. Tạo repo mới trên GitHub (Public hoặc Private đều được).
2. Bấm **Add file → Upload files**, kéo **TẤT CẢ** file/thư mục trong gói này lên,
   gồm cả thư mục ẩn **`.github`** (quan trọng — thiếu nó sẽ không build được).
   - Nếu upload web không thấy thư mục `.github`, xem mục "Cách chắc ăn" bên dưới.
3. Commit. Vào tab **Actions** → workflow **"Build SMSPING EXE"** tự chạy.
   (hoặc bấm **Run workflow**)
4. Chạy xong (~2–3 phút): mở lần chạy đó → kéo xuống **Artifacts** →
   tải **SMSPING-exe** → giải nén được **SMSPING.exe**.

## Cách chắc ăn (dùng Git, không lo mất thư mục ẩn)

```bash
git init
git add .
git commit -m "smsping"
git branch -M main
git remote add origin https://github.com/<tài_khoản>/<tên_repo>.git
git push -u origin main
```

## Nội dung repo
```
smsping_usb.py                     ← code app PC
requirements.txt                   ← thư viện
.github/workflows/build-exe.yml    ← workflow build EXE (BẮT BUỘC)
esp32_passthrough.ino              ← firmware cho bo gộp ESP32+SIM7600CE
HUONG_DAN.md                       ← hướng dẫn nối/chạy
```

## Dùng app
- Cắm USB mạch vào PC → mở SMSPING.exe → **Làm mới** → chọn cổng COM → **Kết nối**.
- **Kiểm tra / Đọc thông tin** để đọc IMEI thật, sóng, nhà mạng.
- Nhập số + nội dung → **GỬI SMS** (hỗ trợ tiếng Việt có dấu).
- Ô IMEI ghi chú để trống, mua được mạch thì điền.
