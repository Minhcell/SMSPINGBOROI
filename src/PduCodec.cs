using System;
using System.Collections.Generic;
using System.Text;

namespace SmsPing
{
    // Kết quả giải mã 1 bản tin REPORT của lệnh PING
    public struct KETQUA
    {
        public bool ER;          // true = giải mã được (đúng là status-report)
        public string MR;        // message reference
        public string sdt_dcping;// số ĐT đã ping
        public string t_ping;    // thời điểm SMSC nhận (SCTS)
        public string t_report;  // thời điểm phát report (DT)
        public string kq;        // kết quả (đã ánh xạ mô tả)
        public string kq_sms;    // mã kết quả gốc
    }

    public static class PduCodec
    {
        // 84 = mã VN; SwapDigits hoán vị nửa-octet 9 số thuê bao (bỏ số 0 đầu)
        public static string SwapDigits(string sdt)
        {
            return string.Concat(new string[]
            {
                sdt[2].ToString(), sdt[1].ToString(),
                sdt[4].ToString(), sdt[3].ToString(),
                sdt[6].ToString(), sdt[5].ToString(),
                sdt[8].ToString(), sdt[7].ToString(),
                "F",               sdt[9].ToString()
            });
        }

        // PDU "ping thầm" tới 1 số VN. smscPrefix nhét SMSC vào đầu PDU.
        // "00" = dùng SMSC của SIM. AT+CMGS=19 GIỮ NGUYÊN (chỉ đếm phần TPDU).
        public static string BuildPingPdu(string sdt, string smscPrefix = "00")
        {
            if (string.IsNullOrEmpty(smscPrefix)) smscPrefix = "00";
            return smscPrefix + "71000B9148" + SwapDigits(sdt) + "000800050401020000";
        }

        // Mã hoá SMSC vào đầu PDU: [độ dài octet][91][số hoán vị]. "+84900000023" -> "07914809000020F3"
        public static string EncodeSmscPrefix(string intlNumber)
        {
            if (string.IsNullOrEmpty(intlNumber)) return "00";
            string d = intlNumber.StartsWith("+") ? intlNumber.Substring(1) : intlNumber;
            StringBuilder only = new StringBuilder();
            foreach (char c in d) if (char.IsDigit(c)) only.Append(c);
            d = only.ToString();
            if (d.Length == 0) return "00";
            string padded = (d.Length % 2 == 0) ? d : d + "F";
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < padded.Length; i += 2) { sb.Append(padded[i + 1]); sb.Append(padded[i]); }
            string swapped = sb.ToString();
            int octets = 1 + swapped.Length / 2;
            return octets.ToString("X2") + "91" + swapped;
        }

        // ================= GIẢI MÃ STATUS-REPORT (tổng quát) =================
        // Tự nhận PDU có/không kèm địa chỉ SMSC ở đầu.
        public static KETQUA Decode(string input)
        {
            KETQUA r = new KETQUA
            { ER = false, MR = "", sdt_dcping = "", t_ping = "", t_report = "", kq = "", kq_sms = "" };

            string s = (input ?? "").Trim();
            StringBuilder hx = new StringBuilder();
            foreach (char c in s) if (Uri.IsHexDigit(c)) hx.Append(char.ToUpper(c));
            s = hx.ToString();

            try
            {
                int p = 0;
                int b0 = Convert.ToInt32(s.Substring(0, 2), 16);

                // Nếu octet đầu là TPDU status-report (MTI=10) thì KHÔNG có SMSC;
                // ngược lại octet đầu là ĐỘ DÀI địa chỉ SMSC -> bỏ qua phần SMSC.
                if ((b0 & 0x03) != 0x02)
                {
                    int smscLen = b0;
                    p = 2 + smscLen * 2;
                }

                int firstOctet = Convert.ToInt32(s.Substring(p, 2), 16); p += 2;
                if ((firstOctet & 0x03) != 0x02) { return r; } // không phải STATUS-REPORT

                // TP-MR
                r.MR = Convert.ToInt32(s.Substring(p, 2), 16).ToString(); p += 2;

                // TP-RA (số ĐT được ping)
                int raDigits = Convert.ToInt32(s.Substring(p, 2), 16); p += 2;
                p += 2; // type-of-address (91)
                int raOctets = (raDigits + 1) / 2;
                string raSwapped = s.Substring(p, raOctets * 2); p += raOctets * 2;
                string ra = DecodeSemiOctet(raSwapped);
                if (ra.StartsWith("84")) ra = "0" + ra.Substring(2);
                else if (!ra.StartsWith("0")) ra = "0" + ra;
                r.sdt_dcping = ra;

                // TP-SCTS (SMSC nhận lúc) + TP-DT (phát report lúc)
                r.t_ping = DecodeTimestamp(s.Substring(p, 14)); p += 14;
                r.t_report = DecodeTimestamp(s.Substring(p, 14)); p += 14;

                // TP-STATUS
                r.kq = s.Substring(p, 2);
                r.kq_sms = r.kq;
                r.ER = true;
            }
            catch
            {
                r.ER = false;
                return r;
            }

            if (!string.IsNullOrEmpty(r.kq) && KqTable.TryGetValue(r.kq, out KqEntry e))
            {
                r.kq = e.Vi;
                r.kq_sms = e.Sms;
            }
            return r;
        }

        private static string DecodeSemiOctet(string swapped)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i + 1 < swapped.Length; i += 2) { sb.Append(swapped[i + 1]); sb.Append(swapped[i]); }
            return sb.ToString().Replace("F", "").Replace("f", "");
        }

        // 7 octet: YY MM DD HH MM SS TZ (mỗi octet đảo nửa-byte) -> "HH:MM:SS, ngay DD/MM/20YY"
        private static string DecodeTimestamp(string t)
        {
            string yy = Rev(t, 0), mo = Rev(t, 2), dd = Rev(t, 4), hh = Rev(t, 6), mi = Rev(t, 8), ss = Rev(t, 10);
            return hh + ":" + mi + ":" + ss + ", ngay " + dd + "/" + mo + "/20" + yy;
        }
        private static string Rev(string t, int i) { return "" + t[i + 1] + t[i]; }

        private struct KqEntry
        {
            public string Vi;
            public string Sms;
            public KqEntry(string vi, string sms) { Vi = vi; Sms = sms; }
        }

        // Bảng mô tả mã kết quả (TP-Status của STATUS-REPORT)
        private static readonly Dictionary<string, KqEntry> KqTable =
            new Dictionary<string, KqEntry>(StringComparer.OrdinalIgnoreCase)
        {
            { "00", new KqEntry("SDT PING ONLINE.", "SDT PING ONLINE.") },
            { "01", new KqEntry("SMSC can not send.", "SMSC can not send.") },
            { "02", new KqEntry("SMS replace SMSC.", "SMS replace SMSC.") },
            { "03", new KqEntry("Lower End of the Reserved Values in This Sector.", "Lower End of the Reserved Values in This Sector.") },
            { "0F", new KqEntry("High End of the Reserved Values in This Sector.", "High End of the Reserved Values in This Sector.") },
            { "10", new KqEntry("Lower End of Values Specific to each SMSC.", "Lower End of Values Specific to each SMSC.") },
            { "1F", new KqEntry("High End of Values Specific to each SMSC in This Sector.", "High End of Values Specific to each SMSC in This Sector.") },
            { "20", new KqEntry("Congestion.", "Congestion.") },
            { "60", new KqEntry("Congestion.", "Congestion.") },
            { "21", new KqEntry("SDT ban.", "SDT ban.") },
            { "61", new KqEntry("SDT ban.", "SDT ban.") },
            { "22", new KqEntry("SDT Khong hoi dap.", "SDT Khong hoi dap.") },
            { "62", new KqEntry("SDT Khong hoi dap.", "SDT Khong hoi dap.") },
            { "23", new KqEntry("Service rejected.", "Service rejected.") },
            { "63", new KqEntry("Service rejected.", "Service rejected.") },
            { "24", new KqEntry("service not available.", "service not available.") },
            { "64", new KqEntry("service not available.", "service not available.") },
            { "25", new KqEntry("Loi o DT dich.", "Loi o DT dich.") },
            { "65", new KqEntry("Loi o DT dich.", "Loi o DT dich.") },
            { "26", new KqEntry("Lower End of the Reserved Values in This Sector.", "Lower End of the Reserved Values in This Sector.") },
            { "66", new KqEntry("Lower End of the Reserved Values in This Sector.", "Lower End of the Reserved Values in This Sector.") },
            { "2F", new KqEntry("High End of the Reserved Values in This Sector.", "High End of the Reserved Values in This Sector.") },
            { "6F", new KqEntry("High End of the Reserved Values in This Sector.", "High End of the Reserved Values in This Sector.") },
            { "30", new KqEntry("Lower End of Values Specific to each SMSC.", "Lower End of Values Specific to each SMSC.") },
            { "70", new KqEntry("Lower End of Values Specific to each SMSC.", "Lower End of Values Specific to each SMSC.") },
            { "3F", new KqEntry("High End of Values Specific to each SMSC in This Sector.", "High End of Values Specific to each SMSC in This Sector.") },
            { "7F", new KqEntry("High End of Values Specific to each SMSC in This Sector.", "High End of Values Specific to each SMSC in This Sector.") },
            { "40", new KqEntry("Remote procedure error.", "Remote procedure error.") },
            { "41", new KqEntry("Incompatible destination.", "Incompatible destination.") },
            { "42", new KqEntry("Connection rejected by DT dich.", "Connection rejected by DT dich.") },
            { "43", new KqEntry("Not obtainable.", "Not obtainable.") },
            { "44", new KqEntry("Quality of service not available.", "Quality of service not available.") },
            { "45", new KqEntry("SDT PING KHONG CO THUC.", "SDT PING KHONG CO THUC.") },
            { "46", new KqEntry("Het han. SMS xoa TN", "Het han. SMS xoa TN") },
            { "47", new KqEntry("SMS Deleted by originating DT dich.", "SMS Deleted by originating DT dich.") },
            { "48", new KqEntry("SMS Deleted by SMSC Administration.", "SMS Deleted by SMSC Administration.") },
            { "49", new KqEntry("SMS does not exist.", "SMS does not exist.") },
            { "4A", new KqEntry("Lower End of the Reserved Values in This Sector.", "Lower End of the Reserved Values in This Sector.") },
            { "4F", new KqEntry("High End of the Reserved Values in This Sector.", "High End of the Reserved Values in This Sector.") },
            { "50", new KqEntry("Lower End of Values Specific to each SMSC.", "Lower End of Values Specific to each SMSC.") },
            { "5F", new KqEntry("High End of Values Specific to each SMSC in This Sector.", "High End of Values Specific to each SMSC in This Sector.") },
        };
    }
}
