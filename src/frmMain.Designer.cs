using System.ComponentModel;
using System.Drawing;
using System.IO.Ports;
using System.Windows.Forms;

namespace SmsPing
{
    partial class frmMain
    {
        private IContainer components = null;

        private ComboBox cmbPort;
        private Button btnConnect;
        private Button btnDisconnect;
        private Label lblStatus;
        private RadioButton rbPing;
        private RadioButton rbAt;
        private Panel panelPing;
        private Panel panelAt;
        private TextBox txtTarget;
        private Button btnSend;
        private TextBox txtAt;
        private CheckBox ckbCr;
        private Button btnSendAt;
        private Button btnCtrlZ;
        private TextBox txtRaw;
        private TextBox txtDecode;
        private Button btnClr;
        private Button btnDecodeSel;
        private Button btn_TK;
        private Button btn_chkHard;
        private Button btnHelp;
        private Button btnAbout;
        private Timer TimerConnect;
        private SerialPort SerialPort1;

        protected override void Dispose(bool disposing)
        {
            if (disposing && components != null) components.Dispose();
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            this.components = new Container();

            this.cmbPort = new ComboBox();
            this.btnConnect = new Button();
            this.btnDisconnect = new Button();
            this.lblStatus = new Label();
            this.rbPing = new RadioButton();
            this.rbAt = new RadioButton();
            this.panelPing = new Panel();
            this.panelAt = new Panel();
            this.txtTarget = new TextBox();
            this.btnSend = new Button();
            this.txtAt = new TextBox();
            this.ckbCr = new CheckBox();
            this.btnSendAt = new Button();
            this.btnCtrlZ = new Button();
            this.txtRaw = new TextBox();
            this.txtDecode = new TextBox();
            this.btnClr = new Button();
            this.btnDecodeSel = new Button();
            this.btn_TK = new Button();
            this.btn_chkHard = new Button();
            this.btnHelp = new Button();
            this.btnAbout = new Button();
            this.TimerConnect = new Timer(this.components);
            this.SerialPort1 = new SerialPort(this.components);

            // cmbPort
            this.cmbPort.DropDownStyle = ComboBoxStyle.DropDownList;
            this.cmbPort.Location = new Point(12, 12);
            this.cmbPort.Size = new Size(110, 24);
            this.cmbPort.SelectedIndexChanged += this.cmbPort_SelectedIndexChanged;

            // btnConnect
            this.btnConnect.Location = new Point(130, 11);
            this.btnConnect.Size = new Size(90, 26);
            this.btnConnect.Text = "Connect";
            this.btnConnect.Click += this.btnConnect_Click;

            // btnDisconnect
            this.btnDisconnect.Location = new Point(226, 11);
            this.btnDisconnect.Size = new Size(90, 26);
            this.btnDisconnect.Text = "Disconnect";
            this.btnDisconnect.Click += this.btnDisconnect_Click;

            // lblStatus
            this.lblStatus.Location = new Point(324, 15);
            this.lblStatus.Size = new Size(160, 20);
            this.lblStatus.Text = "Chưa kết nối";
            this.lblStatus.ForeColor = Color.Red;

            // btn_chkHard
            this.btn_chkHard.Location = new Point(496, 11);
            this.btn_chkHard.Size = new Size(90, 26);
            this.btn_chkHard.Text = "Kiểm tra sóng";
            this.btn_chkHard.Click += this.btn_chkHard_Click;

            // btn_TK
            this.btn_TK.Location = new Point(592, 11);
            this.btn_TK.Size = new Size(90, 26);
            this.btn_TK.Text = "Kiểm tra TK";
            this.btn_TK.Click += this.btn_TK_Click;

            // rbPing
            this.rbPing.Location = new Point(12, 48);
            this.rbPing.Size = new Size(70, 22);
            this.rbPing.Text = "PING";
            this.rbPing.CheckedChanged += this.rbPing_CheckedChanged;

            // rbAt
            this.rbAt.Location = new Point(88, 48);
            this.rbAt.Size = new Size(120, 22);
            this.rbAt.Text = "AT Command";
            this.rbAt.CheckedChanged += this.rbAt_CheckedChanged;

            // panelPing
            this.panelPing.Location = new Point(12, 74);
            this.panelPing.Size = new Size(670, 40);
            this.txtTarget.Location = new Point(0, 8);
            this.txtTarget.Size = new Size(160, 24);
            this.btnSend.Location = new Point(170, 6);
            this.btnSend.Size = new Size(110, 28);
            this.btnSend.Text = "PING SMS";
            this.btnSend.Click += this.btnSend_Click;
            this.panelPing.Controls.Add(this.txtTarget);
            this.panelPing.Controls.Add(this.btnSend);

            // panelAt
            this.panelAt.Location = new Point(12, 74);
            this.panelAt.Size = new Size(670, 40);
            this.panelAt.Visible = false;
            this.txtAt.Location = new Point(0, 8);
            this.txtAt.Size = new Size(300, 24);
            this.ckbCr.Location = new Point(310, 10);
            this.ckbCr.Size = new Size(80, 22);
            this.ckbCr.Text = "Thêm CR";
            this.btnSendAt.Location = new Point(396, 6);
            this.btnSendAt.Size = new Size(80, 28);
            this.btnSendAt.Text = "Gửi AT";
            this.btnSendAt.Click += this.btnSendAt_Click;
            this.btnCtrlZ.Location = new Point(482, 6);
            this.btnCtrlZ.Size = new Size(80, 28);
            this.btnCtrlZ.Text = "Ctrl+Z";
            this.btnCtrlZ.Click += this.btnCtrlZ_Click;
            this.panelAt.Controls.Add(this.txtAt);
            this.panelAt.Controls.Add(this.ckbCr);
            this.panelAt.Controls.Add(this.btnSendAt);
            this.panelAt.Controls.Add(this.btnCtrlZ);

            // txtRaw
            this.txtRaw.Location = new Point(12, 124);
            this.txtRaw.Size = new Size(670, 220);
            this.txtRaw.Multiline = true;
            this.txtRaw.ScrollBars = ScrollBars.Both;
            this.txtRaw.WordWrap = false;
            this.txtRaw.Font = new Font("Consolas", 9F);

            // btnDecodeSel
            this.btnDecodeSel.Location = new Point(12, 350);
            this.btnDecodeSel.Size = new Size(150, 28);
            this.btnDecodeSel.Text = "DECODE vùng chọn";
            this.btnDecodeSel.Click += this.btnDecodeSel_Click;

            // btnClr
            this.btnClr.Location = new Point(168, 350);
            this.btnClr.Size = new Size(80, 28);
            this.btnClr.Text = "Xóa";
            this.btnClr.Click += this.btnClr_Click;

            // btnHelp
            this.btnHelp.Location = new Point(516, 350);
            this.btnHelp.Size = new Size(80, 28);
            this.btnHelp.Text = "Trợ giúp";
            this.btnHelp.Click += this.btnHelp_Click;

            // btnAbout
            this.btnAbout.Location = new Point(602, 350);
            this.btnAbout.Size = new Size(80, 28);
            this.btnAbout.Text = "About";
            this.btnAbout.Click += this.btnAbout_Click;

            // txtDecode
            this.txtDecode.Location = new Point(12, 384);
            this.txtDecode.Size = new Size(670, 150);
            this.txtDecode.Multiline = true;
            this.txtDecode.ScrollBars = ScrollBars.Both;
            this.txtDecode.ReadOnly = true;
            this.txtDecode.Font = new Font("Consolas", 9F);

            // TimerConnect
            this.TimerConnect.Interval = 1000;
            this.TimerConnect.Tick += this.TimerConnect_Tick;

            // SerialPort1
            this.SerialPort1.DataReceived += this.SerialPort1_DataReceived;

            // frmMain
            this.AutoScaleMode = AutoScaleMode.None;
            this.ClientSize = new Size(694, 546);
            this.Controls.Add(this.cmbPort);
            this.Controls.Add(this.btnConnect);
            this.Controls.Add(this.btnDisconnect);
            this.Controls.Add(this.lblStatus);
            this.Controls.Add(this.btn_chkHard);
            this.Controls.Add(this.btn_TK);
            this.Controls.Add(this.rbPing);
            this.Controls.Add(this.rbAt);
            this.Controls.Add(this.panelPing);
            this.Controls.Add(this.panelAt);
            this.Controls.Add(this.txtRaw);
            this.Controls.Add(this.btnDecodeSel);
            this.Controls.Add(this.btnClr);
            this.Controls.Add(this.btnHelp);
            this.Controls.Add(this.btnAbout);
            this.Controls.Add(this.txtDecode);
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.Text = "SmsPing - Ver LTE";
            this.Load += this.frmMain_Load;
        }
    }
}
