using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO.Ports;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace SmsPing
{
    public partial class frmMain : Form
    {
        private string _myImei = "";       // buffer nhận dữ liệu từ modem
        private bool _flagCheckImei = false;
        private string _smscPduPrefix = "00"; // SMSC nhét vào đầu PDU khi PING ("00" = dùng SIM)

        // ================== BẢNG SMSC THEO NHÀ MẠNG (MCC = 452) ==================
        // 01 Mobifone | 02 Vinaphone | 04 Viettel | 05 Vietnamobile | 07 Gmobile
        private static readonly Dictionary<string, string> SmscByNetwork =
            new Dictionary<string, string>
        {
            { "01", "+84900000023" }, // Mobifone (miền Nam). Bắc: +84900000011 | Trung: +84900000017
            { "02", "+8491020005"  }, // Vinaphone
            { "04", "+84980200030" }, // Viettel
            { "05", "+84925252525" }, // Vietnamobile
            { "07", "+84995252525" }, // Gmobile
        };

        // Danh sách IMEI thiết bị hợp lệ. Thêm IMEI modem mới vào đây nếu muốn dùng máy khác.
        private static readonly string[] AllowedImei = new string[]
        {
            "862636051970828","862636051979746","862636054171572","862636054064009",
            "862636054182835","862636054166416","866506050985885","862636056523887",
            "862636057265306"
        };

        public frmMain()
        {
            InitializeComponent();
        }

        private void frmMain_Load(object sender, EventArgs e)
        {
            try
            {
                string[] ports = SerialPort.GetPortNames();
                cmbPort.Items.AddRange(ports);
                if (ports.Length != 0)
                    cmbPort.SelectedIndex = ports.Length - 1;

                rbPing.Checked = true;
                ckbCr.Checked = true;
                btnDisconnect.Enabled = false;
                SetConnected(false);

                // Dòng thông báo kết quả lớn (tạo bằng code) — chèn ngay trên khung KẾT QUẢ
                int h = 36;
                lblLatest = new Label
                {
                    Text = "Chưa có kết quả PING",
                    Font = new Font(this.Font.FontFamily, 15F, FontStyle.Bold),
                    TextAlign = ContentAlignment.MiddleCenter,
                    BackColor = Color.FromArgb(238, 238, 238),
                    ForeColor = Color.FromArgb(85, 85, 85),
                    BorderStyle = BorderStyle.FixedSingle,
                    Location = new Point(txtDecode.Left, txtDecode.Top),
                    Size = new Size(txtDecode.Width, h),
                    Anchor = txtDecode.Anchor
                };
                txtDecode.Top = txtDecode.Top + h + 2;
                txtDecode.Height = txtDecode.Height - h - 2;
                this.Controls.Add(lblLatest);
                lblLatest.BringToFront();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Không liệt kê được cổng COM: " + ex.Message, "Lỗi",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ============================ KẾT NỐI ============================
        private void btnConnect_Click(object sender, EventArgs e)
        {
            if (cmbPort.SelectedItem == null)
            {
                MessageBox.Show("Chọn cổng COM trước.", "Thông báo",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            try
            {
                lblStatus.Text = "Đang kết nối...";
                lblStatus.ForeColor = Color.DarkOrange;
                TimerConnect.Enabled = false;

                SerialPort1.PortName = cmbPort.Text;
                SerialPort1.BaudRate = 115200; // khớp firmware cầu nối ESP32
                SerialPort1.Parity = Parity.None;
                SerialPort1.StopBits = StopBits.One;
                SerialPort1.DataBits = 8;
                SerialPort1.Open();

                btnConnect.Enabled = false;
                btnDisconnect.Enabled = true;

                _myImei = "";
                _flagCheckImei = true;

                SerialPort1.Write("AT\r\n");
                for (int i = 0; i < 10 && !_myImei.Contains("OK"); i++)
                {
                    SerialPort1.Write("AT\r\n");
                    Thread.Sleep(1500);
                    Application.DoEvents();
                }

                SerialPort1.Write("AT+CGSN\r\n");
                for (int i = 0; i < 20 && !Regex.IsMatch(_myImei, "\\d{15}"); i++)
                {
                    Thread.Sleep(500);
                    Application.DoEvents();
                }

                CheckImei();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Lỗi kết nối: " + ex.Message);
            }
        }

        private void btnDisconnect_Click(object sender, EventArgs e)
        {
            try { SerialPort1.Close(); }
            catch (Exception ex) { MessageBox.Show(ex.Message); }
            finally { SetConnected(false); }
        }

        // =================== KIỂM TRA IMEI + TỰ GẮN SMSC ===================
        private void CheckImei()
        {
            Match m = Regex.Match(_myImei, "\\d{15}");
            if (!m.Success)
            {
                MessageBox.Show("Không đọc được IMEI thiết bị:\r\n" + _myImei, "Lỗi",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                SerialPort1.Close();
                SetConnected(false);
                _flagCheckImei = false;
                return;
            }

            string imei = m.Value;
            if (!AllowedImei.Contains(imei))
            {
                MessageBox.Show("Thiết bị không hợp lệ.", "Lỗi",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                SerialPort1.Close();
                SetConnected(false);
                _flagCheckImei = false;
                return;
            }

            // Chế độ mạng TỰ ĐỘNG (cho phép 2G/3G) -> ping SMS vẫn chạy, đồng thời USSD dùng được
            SerialPort1.Write("AT+CNMP=2\r\n"); Thread.Sleep(300); Application.DoEvents();

            // ---- Dọn bộ nhớ SMS (tránh "+CMS ERROR: Memory full") ----
            // Xóa sạch mọi vùng nhớ: SIM (SM), modem (ME), report (SR)
            foreach (string mem in new[] { "SM", "ME", "SR" })
            {
                try
                {
                    SerialPort1.Write("AT+CPMS=\"" + mem + "\",\"" + mem + "\",\"" + mem + "\"\r\n");
                    Thread.Sleep(250); Application.DoEvents();
                    SerialPort1.Write("AT+CMGD=1,4\r\n");
                    Thread.Sleep(500); Application.DoEvents();
                }
                catch { }
            }
            // Ưu tiên lưu tin đến vào bộ nhớ modem cho các lần sau
            SerialPort1.Write("AT+CPMS=\"ME\",\"ME\",\"ME\"\r\n"); Thread.Sleep(250); Application.DoEvents();

            // ---- Nhận diện nhà mạng của SIM theo IMSI, lưu SMSC để nhét vào PDU ----
            // IMSI đọc được kể cả khi SMSC trên SIM trống -> đây là cách chắc chắn nhất.
            string smsc = DetectSmscByNetwork(Query("AT+CIMI\r\n", 2500, "\\d{15}"));
            if (smsc == null) smsc = DetectSmscByNetwork(Query("AT+CPSI?\r\n", 2500, "452")); // dự phòng
            if (smsc == null) smsc = DetectSmscByNetwork(Query("AT+COPS?\r\n", 2500, "452")); // dự phòng

            if (!string.IsNullOrEmpty(smsc))
            {
                // Nhét SMSC thẳng vào PDU -> PING không bao giờ dính "SMSC address unknown"
                _smscPduPrefix = PduCodec.EncodeSmscPrefix(smsc);
                // set thêm CSCA cho chắc (không bắt buộc vì PDU đã tự mang SMSC)
                Query("AT+CSCA=\"" + smsc + "\",145\r\n", 800, "OK");
            }
            else
            {
                _smscPduPrefix = "00";
                MessageBox.Show("Chưa dò được nhà mạng của SIM qua IMSI.\r\n" +
                                "Kiểm tra SIM đã nhận đúng chưa rồi Connect lại.",
                                "SMSC", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            SerialPort1.Write("AT+CNMI=1,0,0,1,0\r\n"); Thread.Sleep(200);
            SerialPort1.Write("AT+CLIP=1\r\n");

            txtTarget.Focus();
            _flagCheckImei = false;
            SetConnected(true);
        }

        // Gửi 1 lệnh và đọc phản hồi tới khi khớp mustMatch hoặc hết timeout (mustMatch có thể null).
        private string Query(string cmd, int timeoutMs, string mustMatch)
        {
            _myImei = "";
            SerialPort1.Write(cmd);
            int waited = 0;
            while (waited < timeoutMs)
            {
                Thread.Sleep(100);
                Application.DoEvents();
                waited += 100;
                if (mustMatch != null && Regex.IsMatch(_myImei, mustMatch)) break;
            }
            return _myImei;
        }

        // Dò SMSC theo nhà mạng từ IMSI/CPSI/COPS. Bắt "452" + MNC "0X" ở mọi định dạng:
        // IMSI "45201...", CPSI "452-01", COPS "\"45201\"".
        private static string DetectSmscByNetwork(string data)
        {
            data = data ?? "";
            Match m = Regex.Match(data, "452[\\s\\-]*(0[1-9])");
            if (m.Success && SmscByNetwork.TryGetValue(m.Groups[1].Value, out string smsc))
                return smsc;
            return null;
        }

        // ============================ NHẬN DỮ LIỆU ============================
        private void SerialPort1_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            try
            {
                string data = SerialPort1.ReadExisting();
                if (txtRaw.InvokeRequired)
                    txtRaw.Invoke(new Action<string>(ReceivedText), new object[] { data });
                else
                    ReceivedText(data);
            }
            catch (Exception ex) { MessageBox.Show(ex.Message); }
        }

        private string _rxBuffer = "";
        private Label lblLatest;
        private readonly System.Collections.Generic.HashSet<string> _decodedPdus =
            new System.Collections.Generic.HashSet<string>();

        private void ReceivedText(string data)
        {
            txtRaw.AppendText(data);
            if (_flagCheckImei) _myImei = _myImei + data;

            // Tự động giải mã report giao hàng (+CDS) ngay khi về -> hiện online/offline
            _rxBuffer += data;
            if (_rxBuffer.Length > 8000) _rxBuffer = _rxBuffer.Substring(_rxBuffer.Length - 8000);

            foreach (Match m in Regex.Matches(_rxBuffer, "\\+CDS:\\s*\\d+\\s*([0-9A-Fa-f]{40,})"))
            {
                string pdu = m.Groups[1].Value;
                if (_decodedPdus.Contains(pdu)) continue;
                KETQUA k = PduCodec.Decode(pdu);
                if (!k.ER) continue;
                _decodedPdus.Add(pdu);
                ShowResult(k);
            }
        }

        // Hiển thị 1 kết quả: dòng lớn ONLINE/OFFLINE + đưa mới nhất lên ĐẦU danh sách
        private void ShowResult(KETQUA k)
        {
            bool online = k.kq_sms == "SDT PING ONLINE.";
            lblLatest.Text = k.sdt_dcping + "   :   " + (online ? "ONLINE" : "OFFLINE");
            lblLatest.ForeColor = online ? Color.FromArgb(0, 150, 0) : Color.Red;

            string line = k.sdt_dcping + " : " + (online ? "ONLINE" : "OFFLINE") + "  (" + k.kq + ")" +
                "\r\n   PING " + k.MR + " | nhận " + k.t_ping + " | phát " + k.t_report + "\r\n\r\n";
            txtDecode.Text = line + txtDecode.Text;
            txtDecode.SelectionStart = 0;
            txtDecode.ScrollToCaret();
        }

        // ============================ PING ============================
        private static bool ValidPhone(string sdt)
        {
            return sdt.Length >= 10 && sdt.All(char.IsDigit) && sdt[0] == '0';
        }

        private void btnSend_Click(object sender, EventArgs e)
        {
            if (!SerialPort1.IsOpen)
            {
                MessageBox.Show("Connect COM port trước khi sử dụng lệnh.", "Thông báo",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            string sdt = txtTarget.Text;
            if (!ValidPhone(sdt))
            {
                MessageBox.Show("Kiểm tra lại định dạng SĐT cần PING (10 số, bắt đầu bằng 0).",
                    "Thông báo", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            SendPdu(PduCodec.BuildPingPdu(sdt, _smscPduPrefix));
        }

        private void SendPdu(string pdu)
        {
            SerialPort1.Write("AT+CMGF=0\r\n"); Thread.Sleep(200);
            SerialPort1.Write("AT+CMGS=19\r\n"); Thread.Sleep(300);
            SerialPort1.Write(pdu); Thread.Sleep(200);
            SerialPort1.Write("\u001a"); // Ctrl+Z
        }

        // ============================ AT COMMAND ============================
        private void btnSendAt_Click(object sender, EventArgs e)
        {
            if (!SerialPort1.IsOpen)
            {
                MessageBox.Show("Connect COM port trước khi sử dụng lệnh.", "Thông báo",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            SerialPort1.Write(ckbCr.Checked ? txtAt.Text + "\r" : txtAt.Text);
        }

        private void btnCtrlZ_Click(object sender, EventArgs e)
        {
            if (!SerialPort1.IsOpen)
            {
                MessageBox.Show("Connect COM port trước khi sử dụng lệnh.", "Thông báo",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            SerialPort1.Write("\u001a");
        }

        private void btn_TK_Click(object sender, EventArgs e)
        {
            if (!SerialPort1.IsOpen)
            {
                MessageBox.Show("Connect COM port trước khi sử dụng lệnh.", "Thông báo",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            bool prevFlag = _flagCheckImei;
            _flagCheckImei = true; // để đọc được phản hồi modem trong lúc kiểm tra TK
            try
            {
                // Nhận diện nhà mạng của SIM qua IMSI (452 + MNC)
                string imsi = Query("AT+CIMI\r\n", 2500, "\\d{15}");
                Match mm = Regex.Match(imsi, "452(0[1-9])");
                string mnc = mm.Success ? mm.Groups[1].Value : "";

                // Đảm bảo đã đăng ký dịch vụ mạng (CS) cho USSD
                Query("AT+CREG=1\r\n", 500, "OK");
                for (int i = 0; i < 12; i++)
                    if (Regex.IsMatch(Query("AT+CREG?\r\n", 500, "\\+CREG:"), "\\+CREG:\\s*\\d,\\s*[15]")) break;

                // Đầu số tra cước chính theo nhà mạng (hiện dùng chung *101#)
                string ussd = "*101#"; // 01 Mobifone / 02 Vinaphone / 04 Viettel / 05 Vietnamobile / 07 Gmobile

                // Thử USSD (giải phóng phiên cũ + thử lại khi mạng báo bận/retry)
                Query("AT+CMGF=1\r\n", 500, "OK");
                Query("AT+CSCS=\"GSM\"\r\n", 500, "OK");
                Query("AT+CUSD=2\r\n", 800, null);

                string r = "";
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    r = Query("AT+CUSD=1,\"" + ussd + "\"\r\n", 12000, "\\+CUSD:|ERROR");
                    if (Regex.IsMatch(r, "\\+CUSD:")) break;
                    Query("AT+CUSD=2\r\n", 800, null);
                    Thread.Sleep(2000); Application.DoEvents();
                }

                if (Regex.IsMatch(r, "\\+CUSD:")) // lấy được tài khoản qua USSD
                {
                    Query("AT+CMGF=0\r\n", 400, "OK");
                    return;
                }

                // USSD không được -> Viettel đã NGỪNG USSD (từ 13/05/2026): dùng SMS "TK" gửi 191
                if (mnc == "04")
                {
                    Query("AT+CNMI=2,2,0,0,0\r\n", 500, "OK"); // cho tin đến hiện thẳng ra
                    Query("AT+CSCS=\"GSM\"\r\n", 500, "OK");
                    SerialPort1.Write("AT+CMGS=\"191\"\r\n"); Thread.Sleep(600); Application.DoEvents();
                    SerialPort1.Write("TK"); Thread.Sleep(200);
                    SerialPort1.Write("\u001a"); // gửi
                    string rr = Query("", 12000, "\\+CMT:");
                    Query("AT+CNMI=1,0,0,1,0\r\n", 400, "OK"); // khôi phục cấu hình của tool
                    Query("AT+CMGF=0\r\n", 400, "OK");
                    if (!Regex.IsMatch(rr, "\\+CMT:"))
                        MessageBox.Show("Viettel đã ngừng *101# (từ 13/05/2026). Đã gửi 'TK' đến 191,\r\n" +
                            "chờ vài giây, tin trả lời số dư sẽ hiện ở khung RAW CODE.",
                            "Kiểm tra TK", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                Query("AT+CMGF=0\r\n", 400, "OK");
                MessageBox.Show("Chưa lấy được tài khoản sau vài lần thử (mạng báo bận / retry).\r\n" +
                    "Đợi ~10 giây rồi bấm lại. Kết quả (nếu có) hiện ở khung RAW CODE.",
                    "Kiểm tra TK", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally { _flagCheckImei = prevFlag; }
        }

        private void btn_chkHard_Click(object sender, EventArgs e)
        {
            if (!SerialPort1.IsOpen)
            {
                MessageBox.Show("Connect COM port trước khi sử dụng lệnh.", "Thông báo",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            SerialPort1.Write("AT\r\n"); Thread.Sleep(200);
            SerialPort1.Write("AT+CSQ\r\n");
        }

        // ============================ DECODE ============================
        private void btnClr_Click(object sender, EventArgs e)
        {
            txtRaw.Clear();
            txtDecode.Clear();
            if (lblLatest != null)
            {
                lblLatest.Text = "Chưa có kết quả PING";
                lblLatest.ForeColor = Color.FromArgb(85, 85, 85);
            }
            _decodedPdus.Clear();
        }

        private void btnDecodeSel_Click(object sender, EventArgs e)
        {
            string sel = txtRaw.SelectedText;
            if (string.IsNullOrWhiteSpace(sel))
            {
                MessageBox.Show("Bạn phải chọn (bôi đen) phần REPORT cần decode.", "Thông báo",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            // Tách lấy chuỗi PDU hex dài nhất trong vùng chọn (bỏ "+CDS: 25", xuống dòng, khoảng trắng...)
            string clean = sel.Replace("\r", "").Replace("\n", "").Replace(" ", "");
            Match hx = Regex.Match(clean, "[0-9A-Fa-f]{40,}");
            string pdu = hx.Success ? hx.Value : clean;
            KETQUA kq = PduCodec.Decode(pdu);
            if (!kq.ER)
            {
                MessageBox.Show("Chỉ chọn phần kết quả REPORT của lệnh PING để DECODE.", "Thông báo",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            txtDecode.Focus();
            ShowResult(kq);
        }

        // ============================ MISC ============================
        private void btnHelp_Click(object sender, EventArgs e)
        {
            MessageBox.Show(
                "- Bạn có thể PING cho một hoặc nhiều số ĐT đang tắt máy.\r\n" +
                "Thiết bị sẽ SMS báo cho bạn khi SĐT đó online trở lại.\r\n" +
                "- Bôi đen phần REPORT trong RAW CODE rồi bấm DECODE để đọc kết quả.",
                "Trợ giúp");
        }

        private void btnAbout_Click(object sender, EventArgs e)
        {
            MessageBox.Show(
                "Thiết bị do tác giả và PTH thực hiện.\r\nPhiên bản viết lại gọn từ mã gốc.",
                "About");
        }

        private void cmbPort_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (SerialPort1.IsOpen)
            {
                MessageBox.Show("Disconnect trước khi chọn cổng COM.", "Thông báo",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            SerialPort1.PortName = cmbPort.Text;
        }

        private void rbPing_CheckedChanged(object sender, EventArgs e)
        {
            panelPing.Visible = rbPing.Checked;
            panelAt.Visible = !rbPing.Checked;
            if (rbPing.Checked) txtTarget.Focus();
        }

        private void rbAt_CheckedChanged(object sender, EventArgs e)
        {
            panelAt.Visible = rbAt.Checked;
            panelPing.Visible = !rbAt.Checked;
            if (rbAt.Checked) txtAt.Focus();
        }

        private void SetConnected(bool connected)
        {
            btnConnect.Enabled = !connected;
            btnDisconnect.Enabled = connected;
            TimerConnect.Enabled = connected;
            lblStatus.Text = connected ? "Đã kết nối" : "Chưa kết nối";
            lblStatus.ForeColor = connected ? Color.Green : Color.Red;
        }

        private void TimerConnect_Tick(object sender, EventArgs e)
        {
            bool open = SerialPort1.IsOpen;
            lblStatus.Text = open ? "Đã kết nối" : "Chưa kết nối";
            lblStatus.ForeColor = open ? Color.Green : Color.Red;
        }
    }
}
