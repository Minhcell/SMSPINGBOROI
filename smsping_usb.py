#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
SMSPING (USB/COM) - Gửi SMS qua SIM7600CE/SIM7600G-H bằng cổng COM (lệnh AT).
Dùng được cho:
  - Bo gộp ESP32 + SIM7600CE (đã nạp firmware passthrough) cắm USB vào PC
  - Module SIM7600G-H cắm USB trực tiếp
Cần: pip install PyQt5 pyserial
"""

import sys, time
import serial
import serial.tools.list_ports
from PyQt5 import QtWidgets, QtCore


# ================= Lớp giao tiếp AT qua Serial =================
class Modem:
    def __init__(self):
        self.ser = None

    def open(self, port, baud=115200):
        self.close()
        self.ser = serial.Serial(port, baud, timeout=0.2)
        time.sleep(0.3)
        self.ser.reset_input_buffer()

    def close(self):
        if self.ser and self.ser.is_open:
            try: self.ser.close()
            except Exception: pass
        self.ser = None

    def is_open(self):
        return self.ser is not None and self.ser.is_open

    def _read_until(self, ends, timeout):
        buf = ""
        t0 = time.time()
        while time.time() - t0 < timeout:
            n = self.ser.in_waiting
            if n:
                buf += self.ser.read(n).decode("latin-1", "ignore")
                t0 = time.time()
                for e in ends:
                    if e in buf:
                        return buf
            else:
                time.sleep(0.01)
        return buf

    def at(self, cmd, timeout=4.0, ends=("OK", "ERROR")):
        self.ser.reset_input_buffer()
        self.ser.write((cmd + "\r\n").encode())
        return self._read_until(ends, timeout)

    # ---- mã hoá UTF-8 -> UCS2 hex (gửi tiếng Việt) ----
    @staticmethod
    def ucs2(text):
        return text.encode("utf-16-be").hex().upper()

    @staticmethod
    def phone_ucs2(num):
        # mỗi ký tự số -> 00 + hex ASCII
        return "".join("00%02X" % ord(c) for c in num)

    def init(self):
        self.at("AT")
        self.at("ATE0")
        self.at("AT+CMEE=2")
        self.at("AT+CMGF=1")            # SMS text mode
        self.at('AT+CSCS="UCS2"')       # bảng mã UCS2
        self.at("AT+CSMP=17,167,0,8")   # DCS=8 cho Unicode
        self.at("AT+CNMI=2,1,0,0,0")

    def imei(self):
        r = self.at("AT+CGSN")
        digits = "".join(c for c in r if c.isdigit())
        return digits[:15] if len(digits) >= 15 else digits

    def status(self):
        imei = self.imei()
        csq = self.at("AT+CSQ")
        cops = self.at("AT+COPS?")
        def line(resp, tag):
            i = resp.find(tag)
            if i < 0: return ""
            i += len(tag)
            j = resp.find("\r", i)
            return resp[i:(j if j > 0 else len(resp))].strip()
        return {
            "imei": imei,
            "signal": line(csq, "+CSQ:"),
            "operator": line(cops, "+COPS:"),
        }

    def send_sms(self, number, text):
        self.at("AT+CMGF=1")
        self.at('AT+CSCS="UCS2"')
        self.at("AT+CSMP=17,167,0,8")
        self.ser.reset_input_buffer()
        self.ser.write(('AT+CMGS="%s"\r' % self.phone_ucs2(number)).encode())
        # chờ dấu nhắc '>'
        got = self._read_until((">",), 5.0)
        if ">" not in got:
            return False, "Khong thay dau nhac '>' (kiem tra module/nguon)"
        self.ser.write(self.ucs2(text).encode())
        self.ser.write(bytes([26]))   # Ctrl+Z
        r = self._read_until(("+CMGS:", "ERROR"), 20.0)
        if "+CMGS:" in r:
            ref = r[r.find("+CMGS:") + 6:].strip().splitlines()[0]
            return True, "Da gui. Ref=" + ref
        return False, "Gui that bai: " + r.strip()

    def inbox(self):
        self.at("AT+CMGF=1")
        self.at('AT+CSCS="UCS2"')
        return self.at('AT+CMGL="ALL"', timeout=8.0)


# ================= Luồng nền =================
class Worker(QtCore.QThread):
    done = QtCore.pyqtSignal(bool, object)

    def __init__(self, fn):
        super().__init__()
        self.fn = fn

    def run(self):
        try:
            ok, res = self.fn()
            self.done.emit(ok, res)
        except Exception as e:
            self.done.emit(False, str(e))


# ================= Giao diện =================
class App(QtWidgets.QWidget):
    def __init__(self):
        super().__init__()
        self.modem = Modem()
        self.workers = []
        self.setWindowTitle("SMSPING  —  SIM7600 (USB/COM)")
        self.resize(700, 600)
        self._build()
        self.refresh_ports()

    def _build(self):
        root = QtWidgets.QVBoxLayout(self)
        t = QtWidgets.QLabel("📡  SMSPING  (USB / COM)")
        t.setStyleSheet("font-size:20px;font-weight:bold;color:#0b5;padding:6px;")
        root.addWidget(t)

        # --- kết nối ---
        gc = QtWidgets.QGroupBox("Kết nối cổng COM")
        g = QtWidgets.QGridLayout(gc)
        self.cbo = QtWidgets.QComboBox()
        self.baud = QtWidgets.QComboBox(); self.baud.addItems(["115200","9600","57600","230400"])
        b_ref = QtWidgets.QPushButton("Làm mới"); b_ref.clicked.connect(self.refresh_ports)
        self.b_conn = QtWidgets.QPushButton("Kết nối"); self.b_conn.clicked.connect(self.toggle)
        g.addWidget(QtWidgets.QLabel("Cổng:"),0,0); g.addWidget(self.cbo,0,1)
        g.addWidget(QtWidgets.QLabel("Baud:"),0,2); g.addWidget(self.baud,0,3)
        g.addWidget(b_ref,0,4); g.addWidget(self.b_conn,0,5)
        root.addWidget(gc)

        # --- thiết bị ---
        gd = QtWidgets.QGroupBox("Thông tin thiết bị")
        d = QtWidgets.QGridLayout(gd)
        self.imei_live = QtWidgets.QLineEdit(); self.imei_live.setReadOnly(True)
        self.imei_live.setPlaceholderText("(bấm Kiểm tra để đọc IMEI thật từ mạch)")
        self.imei_note = QtWidgets.QLineEdit()
        self.imei_note.setPlaceholderText("Để trống — điền khi mua được mạch")
        self.sig = QtWidgets.QLineEdit(); self.sig.setReadOnly(True)
        self.op  = QtWidgets.QLineEdit(); self.op.setReadOnly(True)
        b_stat = QtWidgets.QPushButton("Kiểm tra / Đọc thông tin"); b_stat.clicked.connect(self.get_status)
        d.addWidget(QtWidgets.QLabel("IMEI (đọc từ mạch):"),0,0); d.addWidget(self.imei_live,0,1)
        d.addWidget(QtWidgets.QLabel("IMEI (ghi chú):"),1,0); d.addWidget(self.imei_note,1,1)
        d.addWidget(QtWidgets.QLabel("Sóng (CSQ):"),2,0); d.addWidget(self.sig,2,1)
        d.addWidget(QtWidgets.QLabel("Nhà mạng:"),3,0); d.addWidget(self.op,3,1)
        d.addWidget(b_stat,4,1)
        root.addWidget(gd)

        # --- gửi ---
        gs = QtWidgets.QGroupBox("Gửi tin nhắn (SMS Ping)")
        s = QtWidgets.QGridLayout(gs)
        self.num = QtWidgets.QLineEdit(); self.num.setPlaceholderText("VD 84912345678 hoặc 0912345678")
        self.msg = QtWidgets.QPlainTextEdit(); self.msg.setFixedHeight(80)
        self.msg.setPlaceholderText("Nội dung (hỗ trợ tiếng Việt có dấu)")
        b_send = QtWidgets.QPushButton("📤  GỬI SMS")
        b_send.setStyleSheet("font-weight:bold;padding:8px;background:#0b5;color:white;")
        b_send.clicked.connect(self.send)
        b_in = QtWidgets.QPushButton("📥  Đọc hộp thư"); b_in.clicked.connect(self.get_inbox)
        s.addWidget(QtWidgets.QLabel("Gửi tới:"),0,0); s.addWidget(self.num,0,1)
        s.addWidget(QtWidgets.QLabel("Nội dung:"),1,0); s.addWidget(self.msg,1,1)
        row = QtWidgets.QHBoxLayout(); row.addWidget(b_send); row.addWidget(b_in)
        s.addLayout(row,2,1)
        root.addWidget(gs)

        root.addWidget(QtWidgets.QLabel("Nhật ký:"))
        self.log = QtWidgets.QPlainTextEdit(); self.log.setReadOnly(True)
        root.addWidget(self.log, 1)

    # ---------- tiện ích ----------
    def logmsg(self, s): self.log.appendPlainText(s)

    def refresh_ports(self):
        self.cbo.clear()
        for p in serial.tools.list_ports.comports():
            self.cbo.addItem("%s  —  %s" % (p.device, p.description), p.device)

    def run_bg(self, fn, cb):
        w = Worker(fn); w.done.connect(cb)
        w.finished.connect(lambda: self.workers.remove(w) if w in self.workers else None)
        self.workers.append(w); w.start()

    # ---------- hành động ----------
    def toggle(self):
        if self.modem.is_open():
            self.modem.close(); self.b_conn.setText("Kết nối"); self.logmsg("✓ Đã ngắt.")
            return
        if self.cbo.count() == 0:
            QtWidgets.QMessageBox.warning(self,"Chưa có cổng","Không thấy cổng COM. Cắm USB rồi bấm Làm mới.")
            return
        port = self.cbo.currentData()
        baud = int(self.baud.currentText())
        try:
            self.modem.open(port, baud); self.modem.init()
            self.b_conn.setText("Ngắt"); self.logmsg("✓ Đã mở %s @ %d" % (port, baud))
        except Exception as e:
            self.logmsg("✗ Mở cổng lỗi: " + str(e))

    def _need(self):
        if not self.modem.is_open():
            QtWidgets.QMessageBox.warning(self,"Chưa kết nối","Bấm Kết nối trước.")
            return False
        return True

    def get_status(self):
        if not self._need(): return
        self.logmsg("→ Đọc thông tin…")
        self.run_bg(lambda: (True, self.modem.status()), self._on_status)

    def _on_status(self, ok, r):
        if ok and isinstance(r, dict):
            self.imei_live.setText(r["imei"]); self.sig.setText(r["signal"]); self.op.setText(r["operator"])
            self.logmsg("✓ IMEI: %s | Sóng: %s" % (r["imei"], r["signal"]))
        else:
            self.logmsg("✗ " + str(r))

    def send(self):
        if not self._need(): return
        num = self.num.text().strip(); msg = self.msg.toPlainText().strip()
        if not num or not msg:
            QtWidgets.QMessageBox.warning(self,"Thiếu","Nhập số và nội dung."); return
        self.logmsg("→ Gửi tới %s…" % num)
        self.run_bg(lambda: self.modem.send_sms(num, msg), self._on_send)

    def _on_send(self, ok, r):
        self.logmsg(("✓ " if ok else "✗ ") + str(r))

    def get_inbox(self):
        if not self._need(): return
        self.logmsg("→ Đọc hộp thư…")
        self.run_bg(lambda: (True, self.modem.inbox()), lambda ok,r: self.logmsg("📥\n"+str(r)))

    def closeEvent(self, e):
        self.modem.close(); e.accept()


if __name__ == "__main__":
    app = QtWidgets.QApplication(sys.argv)
    w = App(); w.show()
    sys.exit(app.exec_())
