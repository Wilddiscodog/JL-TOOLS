using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using JlFlash.Flow;

namespace JlFlashGui
{
    public class MainForm : Form
    {
        // ---- 设备区 ----
        private Button _btnScan;
        private Button _btnKick;
        private Button _btnStop;
        private Label _devInfo;

        // ---- AC695N SRAM 运行区 ----
        private TextBox _ramPayload;
        private TextBox _ramAddr;
        private TextBox _ramMailbox;
        private TextBox _ramArg;
        private ComboBox _ramCipher;
        private Button _btnRamBrowse;
        private Button _btnRunRam;
        private TextBox _fwPath;
        private Button _btnFwBrowse;
        private Button _btnFlash23;

        // ---- wl82 区（原有功能） ----
        private TextBox _path;
        private TextBox _payloadPath;
        private TextBox _payloadAddr;
        private Button _btnFlash;
        private Button _btnRestore;
        private Button _btnReadKey;
        private Button _btnLoadRun;
        private Button _btnWriteRam;
        private Button _btnJump;
        private Button _btnPayloadBrowse;

        private TextBox _log;
        private volatile bool _stop;
        private volatile bool _busy;

        private static readonly string DefaultRamPayload =
            Path.Combine(@"J:\witerpaper\AC695N_Soundbox_SDK-master\SDK\apps\hello_ram", "hello_ram.bin");

        public MainForm()
        {
            Text = "JlFlash 上位机 - AC695N SRAM运行 / wl82 烧录调试";
            Size = new Size(980, 720);
            MinimumSize = new Size(860, 600);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Microsoft YaHei UI", 9f);

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
            };
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            // ================= 设备区 =================
            var grpDev = new GroupBox { Text = "设备", Dock = DockStyle.Top, Height = 62, Padding = new Padding(8) };
            var devRow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };

            _btnScan = MkBtn("扫描设备", 100);
            _btnScan.Click += (s, e) => Run(() =>
            {
                Log("扫描中 ...");
                var devs = FlashFlow.FindDevices();
                Log(devs.Count == 0 ? "未找到 JieLi 设备（板子未接 / 未进下载模式 / 固件 U 盘模式）"
                    : "找到:\n" + string.Join("\n", devs));
            });

            _btnKick = MkBtn("进入下载模式 (kick)", 150);
            _btnKick.Click += (s, e) => Run(() =>
            {
                Log("尝试进入 UBOOT 下载模式（已有 UBOOT 直接确认，否则对固件 U 盘发 0xFB 软升级命令）...");
                var dev = SramRun.EnterDownloadMode(NewFlow(), Log, 15000);
                Log(dev != null ? $"UBOOT 设备就绪: {dev}" : "失败：未出现 UBOOT 设备（试试断电→插数据线→上电，或按住按键复位）");
            });

            _btnStop = MkBtn("停止", 70);
            _btnStop.Enabled = false;
            _btnStop.Click += (s, e) => _stop = true;

            _devInfo = MkLbl("（未扫描）");

            devRow.Controls.Add(_btnScan);
            devRow.Controls.Add(_btnKick);
            devRow.Controls.Add(_btnStop);
            devRow.Controls.Add(MkLbl("  "));
            devRow.Controls.Add(_devInfo);
            grpDev.Controls.Add(devRow);

            // ================= AC695N SRAM 运行区 =================
            var grpRam = new GroupBox
            {
                Text = "AC695N SRAM 运行 / 固件烧录（BR23）",
                Dock = DockStyle.Top,
                Height = 148,
                Padding = new Padding(8),
            };
            var ramRows = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, AutoSize = true };
            var ramRow1 = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Height = 36 };
            var ramRow2 = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Height = 36 };

            _ramPayload = new TextBox { Width = 480, Text = DefaultRamPayload };
            _btnRamBrowse = MkBtn("浏览...", 60);
            _btnRamBrowse.Click += (s, e) =>
            {
                using (var d = new OpenFileDialog())
                {
                    d.Filter = "bin|*.bin|all|*.*";
                    if (File.Exists(_ramPayload.Text)) d.InitialDirectory = Path.GetDirectoryName(_ramPayload.Text);
                    if (d.ShowDialog() == DialogResult.OK) _ramPayload.Text = d.FileName;
                }
            };

            ramRow1.Controls.Add(MkLbl("载荷:"));
            ramRow1.Controls.Add(_ramPayload);
            ramRow1.Controls.Add(_btnRamBrowse);
            ramRow1.Controls.Add(MkLbl("  加载地址:"));
            _ramAddr = new TextBox { Width = 76, Text = "10000" };
            ramRow1.Controls.Add(_ramAddr);

            _ramMailbox = new TextBox { Width = 76, Text = "2FC00" };
            _ramArg = new TextBox { Width = 60, Text = "0" };
            _ramCipher = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 70 };
            _ramCipher.Items.AddRange(new object[] { "自动", "明文", "密文" });
            _ramCipher.SelectedIndex = 0;

            _btnRunRam = MkBtn("▶ SRAM 运行", 110);
            _btnRunRam.BackColor = Color.FromArgb(215, 235, 255);
            _btnRunRam.Click += (s, e) =>
            {
                string file = _ramPayload.Text.Trim();
                if (!File.Exists(file)) { Log("找不到载荷文件: " + file); return; }
                Run(() =>
                {
                    Log($"SRAM 运行: {file} ({new FileInfo(file).Length} B)，流程 = 探测线密 → FB06 写入 → FD07 校验 → FB08 跳转 → 读邮箱");
                    var flow = NewFlow();
                    string dev = SramRun.EnterDownloadMode(flow, Log, 20000);
                    SramRun.RunPayload(dev, File.ReadAllBytes(file), new SramRun.Options
                    {
                        LoadAddr = ParseHex(_ramAddr.Text, SramRun.PayloadAddrDefault),
                        Mailbox = ParseHex(_ramMailbox.Text, SramRun.MailboxAddrDefault),
                        JumpArg = (ushort)ParseHex(_ramArg.Text, 0),
                        ForcedEnc = _ramCipher.SelectedIndex == 1 ? -1 : _ramCipher.SelectedIndex == 2 ? 1 : 0,
                        NoVerify = false,
                    }, Log);
                    Log("完成。FLASH 未被修改。断电重新上电即恢复原固件。");
                });
            };

            ramRow2.Controls.Add(MkLbl("邮箱地址:"));
            ramRow2.Controls.Add(_ramMailbox);
            ramRow2.Controls.Add(MkLbl("  跳转参数(arg):"));
            ramRow2.Controls.Add(_ramArg);
            ramRow2.Controls.Add(MkLbl("  线密模式:"));
            ramRow2.Controls.Add(_ramCipher);
            ramRow2.Controls.Add(MkLbl("  "));
            ramRow2.Controls.Add(_btnRunRam);
            ramRow2.Controls.Add(MkLbl("  提示: 板子断电→插数据线→上电，再点本按钮即可"));

            // ---- 固件烧录（BR23, br23loader@0x12000, 直接擦写 FLASH）----
            var ramRow3 = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Height = 36 };
            _fwPath = new TextBox { Width = 480, Text = @"J:\witerpaper\AC695N_Soundbox_SDK-master\SDK\cpur23	ools\download\standard\jl_isd.bin" };
            _btnFwBrowse = MkBtn("浏览...", 60);
            _btnFwBrowse.Click += (s, e) =>
            {
                using (var d = new OpenFileDialog())
                {
                    d.Filter = "bin|*.bin|all|*.*";
                    if (File.Exists(_fwPath.Text)) d.InitialDirectory = Path.GetDirectoryName(_fwPath.Text);
                    if (d.ShowDialog() == DialogResult.OK) _fwPath.Text = d.FileName;
                }
            };
            _btnFlash23 = MkBtn("烧录到 FLASH", 120);
            _btnFlash23.BackColor = Color.FromArgb(255, 230, 220);
            _btnFlash23.Click += (s, e) =>
            {
                string file = _fwPath.Text.Trim();
                if (!File.Exists(file)) { Log("找不到固件镜像: " + file); return; }
                Run(() =>
                {
                    Log($"烧录 (BR23): {file} —— 将擦写芯片 FLASH，过程中请勿断电！");
                    var flow = NewFlow();
                    flow.Chip = JlChip.Br23;
                    string dev = SramRun.EnterDownloadMode(flow, Log, 20000);
                    flow.FlashImage(dev, File.ReadAllBytes(file), verify: true);
                    Log("烧录完成，设备已复位。");
                });
            };
            ramRow3.Controls.Add(MkLbl("固件:"));
            ramRow3.Controls.Add(_fwPath);
            ramRow3.Controls.Add(_btnFwBrowse);
            ramRow3.Controls.Add(_btnFlash23);
            ramRow3.Controls.Add(MkLbl("  jl_isd.bin（isd_download 打包产物）"));

            ramRows.Controls.Add(ramRow1, 0, 0);
            ramRows.Controls.Add(ramRow2, 0, 1);
            ramRows.Controls.Add(ramRow3, 0, 2);
            grpRam.Controls.Add(ramRows);

            // ================= wl82 区（原有功能） =================
            var grpWl82 = new GroupBox
            {
                Text = "wl82 (AC791N) 烧录 / 调试 —— 原有功能",
                Dock = DockStyle.Top,
                Height = 104,
                Padding = new Padding(8),
            };
            var wlRows = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, AutoSize = true };
            var wlRow1 = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Height = 36 };
            var wlRow2 = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Height = 36 };

            _path = new TextBox { Width = 400, Text = @"D:\fw-AC79_AIoT_SDK\cpu\wl82\tools\jl_isd.bin" };
            var browse = MkBtn("浏览...", 60);
            browse.Click += (s, e) =>
            {
                using (var d = new OpenFileDialog())
                {
                    d.Filter = "bin|*.bin|all|*.*";
                    if (d.ShowDialog() == DialogResult.OK) _path.Text = d.FileName;
                }
            };

            _btnFlash = MkBtn("烧录 jl_isd.bin", 120);
            _btnFlash.Click += (s, e) =>
            {
                string file = _path.Text.Trim();
                if (!File.Exists(file)) { Log("找不到镜像文件: " + file); return; }
                Run(() =>
                {
                    var flow = NewFlow();
                    string dev = flow.WaitForDevice(5000);
                    Log("设备: " + dev);
                    flow.FlashImage(dev, File.ReadAllBytes(file));
                });
            };

            _btnRestore = MkBtn("恢复原厂 dump", 120);
            _btnRestore.Click += (s, e) =>
            {
                string file = PickFile();     // must run on the UI (STA) thread
                if (file == null) return;
                Run(() =>
                {
                    var flow = NewFlow();
                    string dev = flow.WaitForDevice(5000);
                    Log("设备: " + dev);
                    Log("整片恢复，需要几分钟，请勿断电 ...");
                    flow.WriteRaw(dev, File.ReadAllBytes(file), 0, verify: true);
                });
            };

            _btnReadKey = MkBtn("读 ChipKey", 100);
            _btnReadKey.Click += (s, e) => Run(() =>
            {
                var flow = NewFlow();
                string dev = flow.WaitForDevice(5000);
                var r = flow.ReadChipKey(dev);
                Log($"ChipKey (解码): 0x{r.Item1:X4}");
                Log("raw: " + BitConverter.ToString(r.Item2));
            });

            wlRow1.Controls.Add(MkLbl("镜像:"));
            wlRow1.Controls.Add(_path);
            wlRow1.Controls.Add(browse);
            wlRow1.Controls.Add(_btnFlash);
            wlRow1.Controls.Add(_btnRestore);
            wlRow1.Controls.Add(_btnReadKey);

            _payloadPath = new TextBox { Width = 300, Text = "loop.bin" };
            _btnPayloadBrowse = MkBtn("浏览...", 60);
            _btnPayloadBrowse.Click += (s, e) =>
            {
                using (var d = new OpenFileDialog())
                {
                    d.Filter = "bin|*.bin|all|*.*";
                    if (d.ShowDialog() == DialogResult.OK) _payloadPath.Text = d.FileName;
                }
            };
            _payloadAddr = new TextBox { Width = 80, Text = "1C40000" };

            _btnLoadRun = MkBtn("RAM运行 payload", 130);
            _btnLoadRun.Click += (s, e) =>
            {
                string file = _payloadPath.Text.Trim();
                if (!File.Exists(file)) { Log("找不到 payload 文件: " + file); return; }
                Run(() =>
                {
                    var flow = NewFlow();
                    string dev = flow.WaitForDevice(5000);
                    Log("设备: " + dev);
                    LoadAndRun(flow, dev, file, ParseHex(_payloadAddr.Text, 0x1C40000));
                });
            };

            _btnWriteRam = MkBtn("仅写入RAM", 100);
            _btnWriteRam.Click += (s, e) =>
            {
                string file = _payloadPath.Text.Trim();
                if (!File.Exists(file)) { Log("找不到 payload 文件: " + file); return; }
                Run(() =>
                {
                    var flow = NewFlow();
                    string dev = flow.WaitForDevice(5000);
                    Log("设备: " + dev);
                    WritePayloadOnly(flow, dev, file, ParseHex(_payloadAddr.Text, 0x1C40000));
                });
            };

            _btnJump = MkBtn("仅跳转", 80);
            _btnJump.Click += (s, e) =>
            {
                Run(() =>
                {
                    var flow = NewFlow();
                    string dev = flow.WaitForDevice(5000);
                    Log("设备: " + dev);
                    uint addr = ParseHex(_payloadAddr.Text, 0x1C40000);
                    var up = (Tuple<JlFlash.Protocol.LoaderV2, JlFlash.Transport.ScsiTransport>)
                        typeof(FlashFlow).GetMethod("JumpToLoader", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                        .Invoke(flow, new object[] { new JlFlash.Protocol.UbootRom(new JlFlash.Transport.ScsiTransport(dev)), null });
                    Log($"jump to 0x{addr:X8} ...");
                    try { up.Item1.MemJump(addr); Log("jump returned"); }
                    catch (Exception ex) { Log("jump: " + ex.Message); }
                    Thread.Sleep(1500);
                    var still = FlashFlow.FindDevices();
                    Log(still.Count == 0 ? "device GONE -> payload running" :
                        "device still present: " + string.Join(",", still));
                });
            };

            wlRow2.Controls.Add(MkLbl("payload:"));
            wlRow2.Controls.Add(_payloadPath);
            wlRow2.Controls.Add(_btnPayloadBrowse);
            wlRow2.Controls.Add(MkLbl(" 地址:"));
            wlRow2.Controls.Add(_payloadAddr);
            wlRow2.Controls.Add(_btnLoadRun);
            wlRow2.Controls.Add(_btnWriteRam);
            wlRow2.Controls.Add(_btnJump);

            wlRows.Controls.Add(wlRow1, 0, 0);
            wlRows.Controls.Add(wlRow2, 0, 1);
            grpWl82.Controls.Add(wlRows);

            // ================= 日志 =================
            _log = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Font = new Font("Consolas", 9f),
                WordWrap = false,
                BackColor = Color.FromArgb(250, 250, 250),
            };

            layout.Controls.Add(grpDev, 0, 0);
            layout.Controls.Add(grpRam, 0, 1);
            layout.Controls.Add(grpWl82, 0, 2);
            layout.Controls.Add(_log, 0, 3);
            Controls.Add(layout);

            Log("JlFlash GUI 就绪。AC695N SRAM 运行：板子断电→插数据线→上电 → 点「SRAM 运行」（会自动进下载模式）。");
            Log("FLASH 全程不会被擦写；跑完断电重上电即回原固件。");
        }

        private Button MkBtn(string text, int width)
        {
            return new Button { Text = text, Width = width, Height = 30 };
        }

        private Label MkLbl(string text)
        {
            return new Label { Text = text, AutoSize = true, Margin = new Padding(3, 8, 3, 3) };
        }

        private FlashFlow NewFlow()
        {
            return new FlashFlow(Log);
        }

        private string PickFile()
        {
            using (var d = new OpenFileDialog())
            {
                d.Filter = "bin|*.bin|all|*.*";
                d.InitialDirectory = @"J:\xiaolu-r2\device-flash-bin";
                return d.ShowDialog() == DialogResult.OK ? d.FileName : null;
            }
        }

        private void Run(Action action)
        {
            if (_busy) { Log("当前有操作正在进行"); return; }
            _busy = true; _stop = false;
            SetButtons(false);
            Task.Run(() =>
            {
                try { action(); }
                catch (Exception ex) { Log("ERROR: " + ex.Message); }
                finally { _busy = false; BeginInvoke((Action)(() => SetButtons(true))); }
            });
        }

        /// <summary>FB06 write payload into RAM + FD07 verify. No jump. (wl82 loader path)</summary>
        private void WritePayloadOnly(FlashFlow flow, string devPath, string file, uint addr)
        {
            var t = typeof(FlashFlow);
            var dev = new JlFlash.Transport.ScsiTransport(devPath);
            var uboot = new JlFlash.Protocol.UbootRom(dev);
            t.GetMethod("DownloadLoader", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .Invoke(flow, new object[] { uboot });
            var up = (Tuple<JlFlash.Protocol.LoaderV2, JlFlash.Transport.ScsiTransport>)
                t.GetMethod("JumpToLoader", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .Invoke(flow, new object[] { uboot, null });
            var ld = up.Item1;

            byte[] code = File.ReadAllBytes(file);
            Log($"payload {file} ({code.Length} B) -> 0x{addr:X8}");
            for (int off = 0; off < code.Length; off += 256)
            {
                int n = Math.Min(256, code.Length - off);
                var chunk = new byte[n];
                Array.Copy(code, off, chunk, 0, n);
                ld.MemWrite(addr + (uint)off, chunk);
            }
            var back = new byte[code.Length];
            for (int off = 0; off < code.Length; off += 256)
            {
                int n = Math.Min(256, code.Length - off);
                var part = ld.MemRead(addr + (uint)off, n);
                Array.Copy(part, 0, back, off, n);
            }
            bool same = back.SequenceEqual(code);
            Log(same ? "write + verify OK (use jump button to execute)" : "ABORT: verify failed");
        }

        private static uint ParseHex(string s, uint def)
        {
            try { return Convert.ToUInt32(s.Trim(), 16); } catch { return def; }
        }

        /// <summary>
        /// Download a raw payload into SDRAM via the loader (FB06 as-is) and jump (FB08).
        /// After the jump the device is expected to go silent if the payload runs forever.
        /// </summary>
        private void LoadAndRun(FlashFlow flow, string devPath, string file, uint addr)
        {
            var t = typeof(FlashFlow);
            var dev = new JlFlash.Transport.ScsiTransport(devPath);
            var uboot = new JlFlash.Protocol.UbootRom(dev);
            t.GetMethod("DownloadLoader", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .Invoke(flow, new object[] { uboot });
            var up = (Tuple<JlFlash.Protocol.LoaderV2, JlFlash.Transport.ScsiTransport>)
                t.GetMethod("JumpToLoader", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .Invoke(flow, new object[] { uboot, null });
            var ld = up.Item1;

            byte[] code = File.ReadAllBytes(file);
            Log($"payload {file} ({code.Length} B) -> 0x{addr:X8}");
            for (int off = 0; off < code.Length; off += 256)
            {
                int n = Math.Min(256, code.Length - off);
                var chunk = new byte[n];
                Array.Copy(code, off, chunk, 0, n);
                ld.MemWrite(addr + (uint)off, chunk);
            }
            Log("written, verifying via FD07 ...");
            var back = new byte[code.Length];
            for (int off = 0; off < code.Length; off += 256)
            {
                int n = Math.Min(256, code.Length - off);
                var part = ld.MemRead(addr + (uint)off, n);
                Array.Copy(part, 0, back, off, n);
            }
            bool same = back.SequenceEqual(code);
            Log("readback match: " + same);
            if (!same) { Log("ABORT: memory not written correctly"); return; }
            Log($"jumping to 0x{addr:X8} (设备应该会消失) ...");
            try { ld.MemJump(addr); Log("jump command returned"); }
            catch (Exception ex) { Log("jump: " + ex.Message); }
            Thread.Sleep(1500);
            var still = FlashFlow.FindDevices();
            Log(still.Count == 0 ? "设备已消失 -> payload 正在运行!" :
                "设备仍在: " + string.Join(",", still));
        }

        private void SetButtons(bool enabled)
        {
            foreach (var b in new[] { _btnScan, _btnKick, _btnFlash, _btnRestore, _btnReadKey, _btnLoadRun, _btnWriteRam, _btnJump, _btnRunRam, _btnRamBrowse, _btnFlash23, _btnFwBrowse })
                BeginInvoke((Action)(() => b.Enabled = enabled));
            BeginInvoke((Action)(() => _btnStop.Enabled = !enabled));
        }

        private void Log(string s)
        {
            if (_log.InvokeRequired) { BeginInvoke((Action)(() => Log(s))); return; }
            _log.AppendText(DateTime.Now.ToString("HH:mm:ss ") + s.Replace("\n", "\r\n") + Environment.NewLine);
        }
    }
}
