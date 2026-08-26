#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
SMSPING (USB/COM) - Gửi SMS + BÁO CÁO KẾT QUẢ PING qua SIM7600CE/SIM7600G-H.
Dùng cho: SIM7600G-H cắm USB trực tiếp, hoặc bo gộp ESP32+SIM7600CE (passthrough).
Cần: pip install PyQt5 pyserial
"""

import sys, time, csv
from datetime import datetime
import serial
import serial.tools.list_ports
from PyQt5 import QtWidgets, QtCore, QtGui


# ================= Giao tiếp AT qua Serial =================
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

    @staticmethod
    def ucs2(text):
        return text.encode("utf-16-be").hex().upper()

    @staticmethod
    def phone_ucs2(num):
        return "".join("00%02X" % ord(c) for c in num)

    def init(self, want_report=False):
        self.at("AT")
        self.at("ATE0")
        self.at("AT+CMEE=2")
        self.at("AT+CMGF=1")
        self.at('AT+CSCS="UCS2"')
        # octet đầu: 17 = bình thường, 49 = có yêu cầu báo nhận (delivery report)
        self.at("AT+CSMP=%d,167,0,8" % (49 if want_report else 17))
        # định tuyến báo tin đến + báo nhận (+CDS)
        self.at("AT+CNMI=2,1,2,1,0" if want_report else "AT+CNMI=2,1,0,0,0")

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
            i += len(tag); j = resp.find("\r", i)
            return resp[i:(j if j > 0 else len(resp))].strip()
        return {"imei": imei, "signal": line(csq, "+CSQ:"), "operator": line(cops, "+COPS:")}

    def send_sms(self, number, text):
        """Trả (ok, ref, detail). ref = mã tham chiếu để đối chiếu báo nhận."""
        self.ser.reset_input_buffer()
        self.ser.write(('AT+CMGS="%s"\r' % self.phone_ucs2(number)).encode())
        got = self._read_until((">",), 5.0)
        if ">" not in got:
            return False, None, "Không thấy dấu nhắc '>' (kiểm tra cổng/nguồn)"
        self.ser.write(self.ucs2(text).encode())
        self.ser.write(bytes([26]))  # Ctrl+Z
        r = self._read_until(("+CMGS:", "ERROR"), 20.0)
        if "+CMGS:" in r:
            ref = "".join(c for c in r[r.find("+CMGS:") + 6:].split("\n")[0] if c.isdigit())
            return True, ref, "Đã gửi vào mạng"
        return False, None, "Thất bại: " + r.strip().replace("\r", " ").replace("\n", " ")

    def poll_cds(self):
        """Đọc mọi dữ liệu chờ, trả text (có thể chứa +CDS báo nhận)."""
        out = ""
        if self.ser and self.ser.in_waiting:
            out += self.ser.read(self.ser.in_waiting).decode("latin-1", "ignore")
        return out


# ================= Luồng nền =================
class OneShot(QtCore.QThread):
    done = QtCore.pyqtSignal(bool, object)
    def __init__(self, fn): super().__init__(); self.fn = fn
    def run(self):
        try: ok, res = self.fn(); self.done.emit(ok, res)
        except Exception as e: self.done.emit(False, str(e))


class PingBatch(QtCore.QThread):
    """Gửi lần lượt danh sách số, báo kết quả từng dòng."""
    row = QtCore.pyqtSignal(dict)
    finished_all = QtCore.pyqtSignal(dict)

    def __init__(self, modem, numbers, text, gap=1.0):
        super().__init__()
        self.modem, self.numbers, self.text, self.gap = modem, numbers, text, gap

    def run(self):
        total = len(self.numbers); ok_n = 0
        for i, num in enumerate(self.numbers, 1):
            try:
                ok, ref, detail = self.modem.send_sms(num, self.text)
            except Exception as e:
                ok, ref, detail = False, None, str(e)
            if ok: ok_n += 1
            self.row.emit({
                "no": i,
                "time": datetime.now().strftime("%H:%M:%S"),
                "number": num,
                "ok": ok,
                "status": "Đã gửi" if ok else "Thất bại",
                "ref": ref or "",
                "detail": detail,
            })
            time.sleep(self.gap)
        self.finished_all.emit({"total": total, "success": ok_n, "fail": total - ok_n})


# ================= Giao diện =================
class App(QtWidgets.QWidget):
    def __init__(self):
        super().__init__()
        self.modem = Modem()
        self.threads = []
        self.ref_row = {}
        self.setWindowTitle("SMSPING  —  Báo cáo kết quả Ping (USB/COM)")
        self.resize(860, 720)
        self._build()
        self.refresh_ports()
        self.timer = QtCore.QTimer(self); self.timer.timeout.connect(self._poll_reports)
        self.timer.start(1500)

    def _build(self):
        root = QtWidgets.QVBoxLayout(self)
        t = QtWidgets.QLabel("📡  SMSPING  —  Báo cáo kết quả Ping")
        t.setStyleSheet("font-size:20px;font-weight:bold;color:#0b5;padding:6px;")
        root.addWidget(t)

        gc = QtWidgets.QGroupBox("Kết nối cổng COM")
        g = QtWidgets.QGridLayout(gc)
        self.cbo = QtWidgets.QComboBox()
        self.baud = QtWidgets.QComboBox(); self.baud.addItems(["115200","9600","57600","230400"])
        self.chk_report = QtWidgets.QCheckBox("Yêu cầu báo nhận (delivery report)")
        b_ref = QtWidgets.QPushButton("Làm mới"); b_ref.clicked.connect(self.refresh_ports)
        self.b_conn = QtWidgets.QPushButton("Kết nối"); self.b_conn.clicked.connect(self.toggle)
        g.addWidget(QtWidgets.QLabel("Cổng:"),0,0); g.addWidget(self.cbo,0,1)
        g.addWidget(QtWidgets.QLabel("Baud:"),0,2); g.addWidget(self.baud,0,3)
        g.addWidget(b_ref,0,4); g.addWidget(self.b_conn,0,5)
        g.addWidget(self.chk_report,1,1,1,3)
        root.addWidget(gc)

        gd = QtWidgets.QGroupBox("Thông tin thiết bị")
        d = QtWidgets.QGridLayout(gd)
        self.imei_live = QtWidgets.QLineEdit(); self.imei_live.setReadOnly(True)
        self.imei_live.setPlaceholderText("(bấm Kiểm tra để đọc IMEI thật)")
        self.imei_note = QtWidgets.QLineEdit(); self.imei_note.setPlaceholderText("Để trống — điền khi mua được mạch")
        self.sig = QtWidgets.QLineEdit(); self.sig.setReadOnly(True)
        self.op  = QtWidgets.QLineEdit(); self.op.setReadOnly(True)
        b_stat = QtWidgets.QPushButton("Kiểm tra / Đọc thông tin"); b_stat.clicked.connect(self.get_status)
        d.addWidget(QtWidgets.QLabel("IMEI (đọc từ mạch):"),0,0); d.addWidget(self.imei_live,0,1)
        d.addWidget(QtWidgets.QLabel("IMEI (ghi chú):"),1,0); d.addWidget(self.imei_note,1,1)
        d.addWidget(QtWidgets.QLabel("Sóng (CSQ):"),2,0); d.addWidget(self.sig,2,1)
        d.addWidget(QtWidgets.QLabel("Nhà mạng:"),3,0); d.addWidget(self.op,3,1)
        d.addWidget(b_stat,4,1)
        root.addWidget(gd)

        gs = QtWidgets.QGroupBox("Gửi Ping")
        s = QtWidgets.QGridLayout(gs)
        self.nums = QtWidgets.QPlainTextEdit(); self.nums.setFixedHeight(70)
        self.nums.setPlaceholderText("Nhập số điện thoại — MỖI SỐ MỘT DÒNG\nVD:\n84912345678\n84987654321")
        self.msg = QtWidgets.QPlainTextEdit(); self.msg.setFixedHeight(60)
        self.msg.setPlaceholderText("Nội dung (hỗ trợ tiếng Việt có dấu)")
        self.b_send = QtWidgets.QPushButton("📤  PING (GỬI)")
        self.b_send.setStyleSheet("font-weight:bold;padding:8px;background:#0b5;color:white;")
        self.b_send.clicked.connect(self.do_ping)
        s.addWidget(QtWidgets.QLabel("Số (mỗi dòng 1 số):"),0,0); s.addWidget(self.nums,0,1)
        s.addWidget(QtWidgets.QLabel("Nội dung:"),1,0); s.addWidget(self.msg,1,1)
        s.addWidget(self.b_send,2,1)
        root.addWidget(gs)

        gr = QtWidgets.QGroupBox("Báo cáo kết quả Ping")
        rl = QtWidgets.QVBoxLayout(gr)
        self.tbl = QtWidgets.QTableWidget(0, 6)
        self.tbl.setHorizontalHeaderLabels(["#", "Thời gian", "Số điện thoại", "Trạng thái", "Ref", "Chi tiết"])
        self.tbl.horizontalHeader().setSectionResizeMode(5, QtWidgets.QHeaderView.Stretch)
        self.tbl.setColumnWidth(0, 36); self.tbl.setColumnWidth(1, 80)
        self.tbl.setColumnWidth(2, 130); self.tbl.setColumnWidth(3, 90); self.tbl.setColumnWidth(4, 60)
        self.tbl.setEditTriggers(QtWidgets.QAbstractItemView.NoEditTriggers)
        rl.addWidget(self.tbl)
        bar = QtWidgets.QHBoxLayout()
        self.lbl_sum = QtWidgets.QLabel("Tổng: 0  |  Thành công: 0  |  Thất bại: 0")
        self.lbl_sum.setStyleSheet("font-weight:bold;")
        b_csv = QtWidgets.QPushButton("💾  Xuất báo cáo CSV"); b_csv.clicked.connect(self.export_csv)
        b_clr = QtWidgets.QPushButton("Xóa bảng"); b_clr.clicked.connect(self.clear_table)
        bar.addWidget(self.lbl_sum); bar.addStretch(1); bar.addWidget(b_csv); bar.addWidget(b_clr)
        rl.addLayout(bar)
        root.addWidget(gr, 1)

    def refresh_ports(self):
        self.cbo.clear()
        for p in serial.tools.list_ports.comports():
            self.cbo.addItem("%s  —  %s" % (p.device, p.description), p.device)

    def run_bg(self, fn, cb):
        w = OneShot(fn); w.done.connect(cb)
        w.finished.connect(lambda: self.threads.remove(w) if w in self.threads else None)
        self.threads.append(w); w.start()

    def _need(self):
        if not self.modem.is_open():
            QtWidgets.QMessageBox.warning(self, "Chưa kết nối", "Bấm Kết nối trước."); return False
        return True

    def toggle(self):
        if self.modem.is_open():
            self.modem.close(); self.b_conn.setText("Kết nối"); return
        if self.cbo.count() == 0:
            QtWidgets.QMessageBox.warning(self,"Chưa có cổng","Cắm USB rồi bấm Làm mới."); return
        try:
            self.modem.open(self.cbo.currentData(), int(self.baud.currentText()))
            self.modem.init(want_report=self.chk_report.isChecked())
            self.b_conn.setText("Ngắt")
        except Exception as e:
            QtWidgets.QMessageBox.critical(self, "Lỗi", "Mở cổng lỗi: " + str(e))

    def get_status(self):
        if not self._need(): return
        self.run_bg(lambda: (True, self.modem.status()), self._on_status)

    def _on_status(self, ok, r):
        if ok and isinstance(r, dict):
            self.imei_live.setText(r["imei"]); self.sig.setText(r["signal"]); self.op.setText(r["operator"])

    def do_ping(self):
        if not self._need(): return
        nums = [x.strip() for x in self.nums.toPlainText().splitlines() if x.strip()]
        text = self.msg.toPlainText().strip()
        if not nums or not text:
            QtWidgets.QMessageBox.warning(self, "Thiếu", "Nhập ít nhất 1 số và nội dung."); return
        self.b_send.setEnabled(False); self.b_send.setText("Đang gửi…")
        w = PingBatch(self.modem, nums, text)
        w.row.connect(self._add_row)
        w.finished_all.connect(self._batch_done)
        w.finished.connect(lambda: self.threads.remove(w) if w in self.threads else None)
        self.threads.append(w); w.start()

    def _add_row(self, r):
        row = self.tbl.rowCount(); self.tbl.insertRow(row)
        vals = [str(r["no"]), r["time"], r["number"], r["status"], r["ref"], r["detail"]]
        for c, v in enumerate(vals):
            it = QtWidgets.QTableWidgetItem(v)
            it.setForeground(QtGui.QColor("#0a7a2f") if r["ok"] else QtGui.QColor("#c0392b"))
            self.tbl.setItem(row, c, it)
        if r["ok"] and r["ref"]:
            self.ref_row[r["ref"]] = row
        self.tbl.scrollToBottom()

    def _batch_done(self, s):
        self.b_send.setEnabled(True); self.b_send.setText("📤  PING (GỬI)")
        self.lbl_sum.setText("Tổng: %d  |  Thành công: %d  |  Thất bại: %d"
                             % (s["total"], s["success"], s["fail"]))

    def _poll_reports(self):
        if not self.modem.is_open(): return
        try:
            data = self.modem.poll_cds()
        except Exception:
            return
        if "+CDS" in data:
            for token in data.replace("\r", " ").split():
                if token.isdigit() and token in self.ref_row:
                    row = self.ref_row[token]
                    cell = self.tbl.item(row, 3)
                    if cell:
                        cell.setText("Đã nhận ✓")
                        for c in range(self.tbl.columnCount()):
                            self.tbl.item(row, c).setForeground(QtGui.QColor("#0a7a2f"))

    def export_csv(self):
        if self.tbl.rowCount() == 0:
            QtWidgets.QMessageBox.information(self, "Trống", "Chưa có kết quả."); return
        path, _ = QtWidgets.QFileDialog.getSaveFileName(
            self, "Lưu báo cáo", "bao_cao_ping.csv", "CSV (*.csv)")
        if not path: return
        with open(path, "w", newline="", encoding="utf-8-sig") as f:
            w = csv.writer(f)
            w.writerow(["#", "Thời gian", "Số điện thoại", "Trạng thái", "Ref", "Chi tiết"])
            for r in range(self.tbl.rowCount()):
                w.writerow([self.tbl.item(r, c).text() if self.tbl.item(r, c) else "" for c in range(6)])
        QtWidgets.QMessageBox.information(self, "Xong", "Đã lưu: " + path)

    def clear_table(self):
        self.tbl.setRowCount(0); self.ref_row.clear()
        self.lbl_sum.setText("Tổng: 0  |  Thành công: 0  |  Thất bại: 0")

    def closeEvent(self, e):
        self.modem.close(); e.accept()


if __name__ == "__main__":
    app = QtWidgets.QApplication(sys.argv)
    w = App(); w.show()
    sys.exit(app.exec_())
