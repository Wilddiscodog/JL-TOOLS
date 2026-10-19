using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Diagnostics;
using JlFlash.Flow;
using JlFlashGui;

namespace JlFlashGui
{
    public class MainForm : Form
    {
        // ---- 顶部设备区 ----
        private Button _btnScan;
        private Button _btnKick;
        private Label _devInfo;

        // ---- Tab 页控件 ----
        // AC695N / BR23
        private TextBox _brFwPath;
        private TextBox _brDumpPath;
        // AC791N / WL82
        private TextBox _wlFwPath;
        private TextBox _wlDumpPath;
        private TextBox _wlPayload;
        private TextBox _wlPayloadAddr;
        // Key 工具
        private TextBox _keyChipKey;
        private TextBox _keyOutDir;
        private TextBox _recryptToolsDir;
        private TextBox _recryptKeyFile;
        private CheckBox _ckRecrypt;

        // ---- 日志 ----
        private TextBox _log;

        private readonly List<Button> _allButtons = new List<Button>();
        private volatile bool _busy;

        private static readonly string DefaultBrFw =
            @"J:\witerpaper\QQ-SDK\AC695N_SDK\SDK\cpu\br23\tools\download\standard\jl_isd.bin";
        private static readonly string DefaultWlFw =
            @"D:\fw-AC79_AIoT_SDK\cpu\wl82\tools\jl_isd.bin";
        private static readonly string DefaultWlTools =
            @"D:\fw-AC79_AIoT_SDK\cpu\wl82\tools";
        private static readonly string DefaultDumpDir = @"J:\xiaolu-r2\device-flash-bin";

        public MainForm()
        {
            Text = "JL-TOOLS 烧录上位机";
            Size = new Size(1020, 780);
            MinimumSize = new Size(920, 680);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Microsoft YaHei UI", 9f);

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(10, 8, 10, 8) };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));   // 头部
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // 设备区
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));   // Tab
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 42));    // 日志

            root.Controls.Add(Header(), 0, 0);
            root.Controls.Add(DeviceBar(), 0, 1);
            root.Controls.Add(Tabs(), 0, 2);
            _log = LogBox();
            root.Controls.Add(_log, 0, 3);
            Controls.Add(root);

            Log("JL-TOOLS 上位机就绪。设备接 USB 后：UDISK 固件模式点「进入下载模式」，救砖/新固件直接短接 flash CS 插入（ROM UBOOT 会被自动识别）。");
            Log("提示：AC7911 的芯片已烧 key（0x7DF1），固件必须 recrypt 加密烧录（AC791N 页「加密烧录」），默认流镜像无法启动。");
        }

        // ============================================================ UI 构造
        private Control Header()
        {
            var p = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(21, 38, 77),
                Padding = new Padding(16, 10, 16, 8),
            };
            p.Paint += (s, e) =>
            {
                using (var b = new System.Drawing.Drawing2D.LinearGradientBrush(p.ClientRectangle,
                    Color.FromArgb(20, 38, 77), Color.FromArgb(36, 86, 214), 20f))
                    e.Graphics.FillRectangle(b, p.ClientRectangle);
            };
            p.Controls.Add(new Label
            {
                Text = "JL-TOOLS  杰理芯片烧录上位机      AC695N (BR23) · AC791N (WL82)",
                ForeColor = Color.White,
                Font = new Font("Microsoft YaHei UI", 12f, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(16, 12),
            });
            return p;
        }

        private Control DeviceBar()
        {
            var grp = NewGroup("设备", 56);
            var row = Row(grp);

            _btnScan = MkBtn("扫描设备", 96);
            _btnScan.Click += (s, e) => Run(() =>
            {
                Log("扫描中 ...");
                var devs = FlashFlow.FindDevices();
                Log(devs.Count == 0 ? "未找到 JieLi 设备（板子未接 / 固件无 U 盘功能时请用 CS 短接进 ROM 模式）"
                    : "找到:\n" + string.Join("\n", devs));
            });

            _btnKick = MkBtn("进入下载模式", 120);
            _btnKick.Click += (s, e) => Run(() =>
            {
                Log("尝试进入 UBOOT 下载模式（UDISK 固件发 0xFB；否则请用 CS 短接救砖）...");
                var dev = SramRun.EnterDownloadMode(NewFlow(), Log, 20000);
                Log(dev != null ? $"UBOOT 设备就绪: {dev}" : "失败：未出现 UBOOT 设备");
            });

            row.Controls.Add(_btnScan);
            row.Controls.Add(_btnKick);
            row.Controls.Add(MkLbl("状态:"));
            _devInfo = MkLbl("（未扫描）");
            _devInfo.ForeColor = Color.FromArgb(30, 90, 190);
            row.Controls.Add(_devInfo);
            return grp;
        }

        private Control Tabs()
        {
            var tabs = new TabControl { Dock = DockStyle.Fill };
            tabs.TabPages.Add(TabBr23());
            tabs.TabPages.Add(TabWl82());
            tabs.TabPages.Add(TabKeyTools());
            return tabs;
        }

        // ---------------------------------------------------------- AC695N
        private TabPage TabBr23()
        {
            var page = new TabPage(" AC695N · BR23 (词易帮 Y20) ") { Padding = new Padding(10) };
            var col = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, AutoSize = true };
            col.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            col.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            col.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            // -- 固件烧录（raw 默认流） --
            var g1 = NewGroup("固件烧录（JlFlash 裸写，默认流镜像）", 62);
            var r1 = Row(g1);
            _brFwPath = new TextBox { Width = 520, Text = File.Exists(DefaultBrFw) ? DefaultBrFw : "" };
            r1.Controls.Add(MkLbl("镜像:"));
            r1.Controls.Add(_brFwPath);
            var bBrowse = MkBtn("浏览...", 70);
            bBrowse.Click += (s, e) => _brFwPath.Text = PickFile(_brFwPath.Text) ?? _brFwPath.Text;
            r1.Controls.Add(bBrowse);
            var bFlash = PrimaryBtn("烧录到 FLASH", 130);
            bFlash.Click += (s, e) =>
            {
                string file = _brFwPath.Text.Trim();
                if (!File.Exists(file)) { Log("找不到固件镜像: " + file); return; }
                Run(() =>
                {
                    Log($"烧录 (BR23 默认流): {file} —— 将擦写芯片 FLASH，请勿断电！");
                    var flow = NewFlow();
                    flow.Chip = JlChip.Br23;
                    string dev = SramRun.EnterDownloadMode(flow, Log, 20000);
                    flow.FlashImage(dev, File.ReadAllBytes(file), verify: true);
                    Log("烧录完成，设备已复位。");
                    Log("注意：Y20 (AC6956C) 实测默认流可启动；AC7911 则必须走 recrypt 加密烧录。");
                });
            };
            r1.Controls.Add(bFlash);
            col.Controls.Add(g1, 0, 0);

            // -- 整片恢复 --
            var g2 = NewGroup("整片恢复（写入完整 dump）", 62);
            var r2 = Row(g2);
            _brDumpPath = new TextBox { Width = 520 };
            r2.Controls.Add(MkLbl("dump:"));
            r2.Controls.Add(_brDumpPath);
            var bDump = MkBtn("浏览...", 70);
            bDump.Click += (s, e) => _brDumpPath.Text = PickFile(_brDumpPath.Text, DefaultDumpDir) ?? _brDumpPath.Text;
            r2.Controls.Add(bDump);
            var bRestore = MkBtn("整片恢复", 130);
            bRestore.Click += (s, e) =>
            {
                string file = _brDumpPath.Text.Trim();
                if (!File.Exists(file)) { Log("找不到 dump 文件: " + file); return; }
                Run(() =>
                {
                    Log($"整片恢复: {file} —— 需要几分钟，请勿断电！");
                    var flow = NewFlow();
                    flow.Chip = JlChip.Br23;
                    string dev = SramRun.EnterDownloadMode(flow, Log, 20000);
                    flow.WriteRaw(dev, File.ReadAllBytes(file), 0, verify: true);
                    Log("恢复完成，设备已复位。");
                });
            };
            r2.Controls.Add(bRestore);
            col.Controls.Add(g2, 0, 1);

            // -- ChipKey --
            var g3 = NewGroup("eFuse ChipKey", 62);
            var r3 = Row(g3);
            var bKey = MkBtn("读取 ChipKey", 120);
            bKey.Click += (s, e) => Run(() =>
            {
                Log("读取 ChipKey（FC09，raw[6..7] 解码）...");
                var flow = NewFlow();
                flow.Chip = JlChip.Br23;
                string dev = SramRun.EnterDownloadMode(flow, Log, 20000);
                var r = flow.ReadChipKeyAndReset(dev);
                Log($"ChipKey (解码): 0x{r.Item1:X4}");
                Log("raw: " + BitConverter.ToString(r.Item2));
                Log("可用 Key 工具页生成对应的 0xXXXX.key 文件。");
            });
            r3.Controls.Add(bKey);
            r3.Controls.Add(MkLbl("Y20 实测 = 0xB165；AC7911 = 0x7DF1"));
            col.Controls.Add(g3, 0, 2);

            page.Controls.Add(col);
            return page;
        }

        // ---------------------------------------------------------- AC791N
        private TabPage TabWl82()
        {
            var page = new TabPage(" AC791N · WL82 (xiaolu-r2) ") { Padding = new Padding(10) };
            var col = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, AutoSize = true };
            col.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            col.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            col.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            col.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            // -- 固件烧录（raw 默认流——仅适用于不含 recrypt 需求的芯片/场景） --
            var g1 = NewGroup("固件烧录（JlFlash 裸写，默认流镜像——本芯片须 recrypt 才能启动，见「加密烧录」页）", 62);
            var r1 = Row(g1);
            _wlFwPath = new TextBox { Width = 520, Text = File.Exists(DefaultWlFw) ? DefaultWlFw : "" };
            r1.Controls.Add(MkLbl("镜像:"));
            r1.Controls.Add(_wlFwPath);
            var bBrowse = MkBtn("浏览...", 70);
            bBrowse.Click += (s, e) => _wlFwPath.Text = PickFile(_wlFwPath.Text) ?? _wlFwPath.Text;
            r1.Controls.Add(bBrowse);
            var bFlash = MkBtn("烧录到 FLASH", 130);
            bFlash.Click += (s, e) =>
            {
                string file = _wlFwPath.Text.Trim();
                if (!File.Exists(file)) { Log("找不到镜像文件: " + file); return; }
                Run(() =>
                {
                    Log($"烧录 (WL82 裸写默认流): {file} —— 请勿断电！");
                    Log("警告：本芯片已烧 key（0x7DF1），默认流镜像烧入后无法启动；正常烧录请用「加密烧录」页。");
                    var flow = NewFlow();
                    string dev = SramRun.EnterDownloadMode(flow, Log, 20000);
                    flow.FlashImage(dev, File.ReadAllBytes(file), verify: true);
                    Log("烧录完成，设备已复位。");
                });
            };
            r1.Controls.Add(bFlash);
            col.Controls.Add(g1, 0, 0);

            // -- 整片恢复 --
            var g2 = NewGroup("整片恢复（写入完整 dump，救砖）", 62);
            var r2 = Row(g2);
            _wlDumpPath = new TextBox { Width = 520 };
            r2.Controls.Add(MkLbl("dump:"));
            r2.Controls.Add(_wlDumpPath);
            var bDump = MkBtn("浏览...", 70);
            bDump.Click += (s, e) => _wlDumpPath.Text = PickFile(_wlDumpPath.Text, DefaultDumpDir) ?? _wlDumpPath.Text;
            r2.Controls.Add(bDump);
            var bRestore = MkBtn("整片恢复", 130);
            bRestore.Click += (s, e) =>
            {
                string file = _wlDumpPath.Text.Trim();
                if (!File.Exists(file)) { Log("找不到 dump 文件: " + file); return; }
                Run(() =>
                {
                    var flow = NewFlow();
                    string dev = flow.WaitForDevice(5000);
                    Log("设备: " + dev);
                    Log("整片恢复，需要几分钟，请勿断电 ...");
                    flow.WriteRaw(dev, File.ReadAllBytes(file), 0, verify: true);
                    Log("恢复完成，设备已复位。");
                });
            };
            r2.Controls.Add(bRestore);
            col.Controls.Add(g2, 0, 1);

            // -- SRAM 运行 --
            var g3 = NewGroup("SRAM 运行（写 RAM 并跳转，FLASH 不受影响）", 96);
            var rows = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, AutoSize = true };
            var rr1 = Row(rows);
            _wlPayload = new TextBox { Width = 430, Text = "loop.bin" };
            rr1.Controls.Add(MkLbl("载荷:"));
            rr1.Controls.Add(_wlPayload);
            var bpb = MkBtn("浏览...", 70);
            bpb.Click += (s, e) => _wlPayload.Text = PickFile(_wlPayload.Text) ?? _wlPayload.Text;
            rr1.Controls.Add(bpb);
            rr1.Controls.Add(MkLbl("  地址:"));
            _wlPayloadAddr = new TextBox { Width = 84, Text = "1C40000" };
            rr1.Controls.Add(_wlPayloadAddr);
            var bLoad = PrimaryBtn("载入并运行", 110);
            bLoad.Click += (s, e) =>
            {
                string file = _wlPayload.Text.Trim();
                if (!File.Exists(file)) { Log("找不到 payload 文件: " + file); return; }
                Run(() =>
                {
                    var flow = NewFlow();
                    string dev = flow.WaitForDevice(5000);
                    Log("设备: " + dev);
                    LoadAndRun(flow, dev, file, ParseHex(_wlPayloadAddr.Text, 0x1C40000));
                });
            };
            rr1.Controls.Add(bLoad);
            var rr2 = Row(rows);
            var bWrite = MkBtn("仅写入 RAM", 110);
            bWrite.Click += (s, e) =>
            {
                string file = _wlPayload.Text.Trim();
                if (!File.Exists(file)) { Log("找不到 payload 文件: " + file); return; }
                Run(() =>
                {
                    var flow = NewFlow();
                    string dev = flow.WaitForDevice(5000);
                    WritePayloadOnly(flow, dev, file, ParseHex(_wlPayloadAddr.Text, 0x1C40000));
                });
            };
            rr2.Controls.Add(bWrite);
            var bJump = MkBtn("仅跳转", 90);
            bJump.Click += (s, e) =>
            {
                Run(() =>
                {
                    uint addr = ParseHex(_wlPayloadAddr.Text, 0x1C40000);
                    var up = flow_EnterLoader();
                    Log($"jump to 0x{addr:X8} ...");
                    try { up.Item1.MemJump(addr); Log("jump returned"); }
                    catch (Exception ex) { Log("jump: " + ex.Message); }
                    Thread.Sleep(1500);
                    var still = FlashFlow.FindDevices();
                    Log(still.Count == 0 ? "设备已消失 -> payload 正在运行!" : "设备仍在: " + string.Join(",", still));
                });
            };
            rr2.Controls.Add(bJump);
            rows.Controls.Add(rr1, 0, 0);
            rows.Controls.Add(rr2, 0, 1);
            g3.Controls.Add(rows);
            col.Controls.Add(g3, 0, 2);

            // -- 读 ChipKey --
            var g4 = NewGroup("eFuse ChipKey", 62);
            var r4 = Row(g4);
            var bKey = MkBtn("读取 ChipKey", 120);
            bKey.Click += (s, e) => Run(() =>
            {
                var flow = NewFlow();
                string dev = SramRun.EnterDownloadMode(flow, Log, 20000);
                var r = flow.ReadChipKeyAndReset(dev);
                Log($"ChipKey (解码): 0x{r.Item1:X4}");
                Log("raw: " + BitConverter.ToString(r.Item2));
                Log("本机实测 = 0x7DF1；可用 Key 工具页生成 0x7DF1.key 做 recrypt 烧录。");
            });
            r4.Controls.Add(bKey);
            r4.Controls.Add(MkLbl("AC7911 实测 = 0x7DF1（ROM 强制解密，默认流镜像无法启动）"));
            col.Controls.Add(g4, 0, 3);

            page.Controls.Add(col);
            return page;
        }

        // ---------------------------------------------------------- Key 工具
        private TabPage TabKeyTools()
        {
            var page = new TabPage(" 加密与 Key 工具 ") { Padding = new Padding(10) };
            var col = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, AutoSize = true };
            col.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            col.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            col.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            // -- Key 文件生成 --
            var g1 = NewGroup("Key 文件生成（格式已完全破解，可绕过 -use-chip-key 授权）", 96);
            var rows = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, AutoSize = true };
            var r1 = Row(rows);
            r1.Controls.Add(MkLbl("ChipKey (4位hex):"));
            _keyChipKey = new TextBox { Width = 110, Text = "7DF1" };
            r1.Controls.Add(_keyChipKey);
            r1.Controls.Add(MkLbl("  保存目录:"));
            _keyOutDir = new TextBox { Width = 430, Text = @"D:\fw-AC79_AIoT_SDK\cpu\wl82\tools" };
            r1.Controls.Add(_keyOutDir);
            var bGen = PrimaryBtn("生成 key 文件", 130);
            bGen.Click += (s, e) =>
            {
                try
                {
                    string key = _keyChipKey.Text.Trim();
                    string path = KeyGen.Save(_keyOutDir.Text.Trim(), key);
                    Log($"已生成 {path}");
                    Log("内容: " + File.ReadAllText(path));
                    Log("用法: isd_download ... -key " + Path.GetFileName(path) + " （对 chipkey 匹配的芯片做 recrypt 烧录）");
                }
                catch (Exception ex) { Log("生成失败: " + ex.Message); }
            };
            r1.Controls.Add(bGen);
            rows.Controls.Add(r1, 0, 0);

            var r2 = Row(rows);
            r2.Controls.Add(MkLbl("已验证: 生成器复现 0xB165.key 逐字节一致；0x7DF1.key 已在 AC7911 实测通过 recrypt 烧录并启动。"));
            rows.Controls.Add(r2, 0, 1);
            g1.Controls.Add(rows);
            col.Controls.Add(g1, 0, 0);

            // -- recrypt 加密烧录（wl82） --
            var g2 = NewGroup("recrypt 加密烧录（AC7911 专用：isd_download -key，固件写入时重加密为 chipkey 流）", 96);
            var rows2 = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, AutoSize = true };
            var s1 = Row(rows2);
            s1.Controls.Add(MkLbl("工具目录(含 isd_config.ini/uboot.boot/app.bin):"));
            s1.Controls.Add(MkLbl(""));
            rows2.Controls.Add(s1, 0, 0);
            var s2 = Row(rows2);
            _recryptToolsDir = new TextBox { Width = 480, Text = File.Exists(DefaultWlTools + "\\isd_config.ini") ? DefaultWlTools : "" };
            s2.Controls.Add(_recryptToolsDir);
            var bTools = MkBtn("浏览...", 70);
            bTools.Click += (s, e) =>
            {
                using (var d = new FolderBrowserDialog())
                {
                    if (Directory.Exists(_recryptToolsDir.Text)) d.SelectedPath = _recryptToolsDir.Text;
                    if (d.ShowDialog() == DialogResult.OK) _recryptToolsDir.Text = d.SelectedPath;
                }
            };
            s2.Controls.Add(bTools);
            rows2.Controls.Add(s2, 0, 1);
            var s3 = Row(rows2);
            s3.Controls.Add(MkLbl("key 文件:"));
            _recryptKeyFile = new TextBox { Width = 300, Text = @"D:\fw-AC79_AIoT_SDK\cpu\wl82\tools\0x7DF1.key" };
            s3.Controls.Add(_recryptKeyFile);
            var bKeyF = MkBtn("浏览...", 70);
            bKeyF.Click += (s, e) =>
            {
                using (var d = new OpenFileDialog())
                {
                    d.Filter = "key|*.key|all|*.*";
                    if (File.Exists(_recryptKeyFile.Text)) d.InitialDirectory = Path.GetDirectoryName(_recryptKeyFile.Text);
                    if (d.ShowDialog() == DialogResult.OK) _recryptKeyFile.Text = d.FileName;
                }
            };
            s3.Controls.Add(bKeyF);
            var bGo = PrimaryBtn("加密烧录 (recrypt)", 170);
            bGo.Click += (s, e) => Run(() => RecryptFlash());
            s3.Controls.Add(bGo);
            rows2.Controls.Add(s3, 0, 2);
            g2.Controls.Add(rows2);
            col.Controls.Add(g2, 0, 1);

            // -- 说明 --
            var g3 = NewGroup("说明", 100);
            g3.Controls.Add(new Label
            {
                Text = "① recrypt 原理：打包产物为默认流（key=0xFFFF）加密；烧录时 UBOOT 用 key 文件把固件重加密为 chipkey 流写入。\n" +
                       "② 本机 (AC7911) ROM 强制 eFuse 解密：默认流镜像无法启动，必须走本页 recrypt 烧录（已实测闭环）。\n" +
                       "③ Y20 (AC6956C) 无此强制，默认流 JlFlash 裸写即可启动（两平台 ROM 行为不同）。\n" +
                       "④ 前置条件：设备进 UBOOT 下载模式（UDISK 点「进入下载模式」；变砖/无 U 盘时 CS 短接救砖）。",
                Dock = DockStyle.Fill,
                Font = new Font("Microsoft YaHei UI", 9f),
                ForeColor = Color.FromArgb(60, 70, 90),
            });
            col.Controls.Add(g3, 0, 2);

            page.Controls.Add(col);
            return page;
        }

        // ---------------------------------------------------------- recrypt 烧录
        private void RecryptFlash()
        {
            string tools = _recryptToolsDir.Text.Trim();
            string key = _recryptKeyFile.Text.Trim();
            var exe = Path.Combine(tools, "isd_download.exe");
            var ini = Path.Combine(tools, "isd_config.ini");
            var uboot = Path.Combine(tools, "uboot.boot");
            var app = Path.Combine(tools, "app.bin");

            if (!File.Exists(exe)) { Log("找不到 " + exe); return; }
            if (!File.Exists(ini)) { Log("找不到 " + ini); return; }
            if (!File.Exists(uboot)) { Log("找不到 " + uboot); return; }
            if (!File.Exists(app)) { Log("找不到 " + app + "（先在 SDK 编译出 app.bin 并放入工具目录）"); return; }
            if (!File.Exists(key)) { Log("找不到 key 文件: " + key); return; }

            Log($"recrypt 加密烧录: tools={tools}");
            Log($"key={key} —— 将擦写芯片 FLASH，请勿断电！");

            var psi = new ProcessStartInfo
            {
                FileName = exe,
                WorkingDirectory = tools,
                Arguments = $"isd_config.ini -tonorflash -dev wl82 -boot 0x1c02000 -div1 -wait 3000 " +
                            $"-uboot uboot.boot -app app.bin cfg_tool.bin -format all -key {Path.GetFileName(key)} -reboot 500",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.GetEncoding("GBK"),
                StandardErrorEncoding = Encoding.GetEncoding("GBK"),
            };

            using (var proc = Process.Start(psi))
            {
                proc.OutputDataReceived += (s, e) => { if (e.Data != null) Log(e.Data); };
                proc.ErrorDataReceived += (s, e) => { if (e.Data != null) Log("[err] " + e.Data); };
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();
                proc.WaitForExit();
                Log($"isd_download 退出码: {proc.ExitCode}");
            }
            Log("完成。若中途报 ERR_KEY/ERR_NODEVICE：确认设备已进 UBOOT 且 key 文件 chipkey 与芯片 eFuse 匹配。");
        }

        private Tuple<JlFlash.Protocol.LoaderV2, JlFlash.Transport.ScsiTransport> flow_EnterLoader()
        {
            var flow = NewFlow();
            string dev = flow.WaitForDevice(5000);
            return flow.EnterLoader(dev);
        }

        // ---------------------------------------------------------- 日志
        private TextBox LogBox()
        {
            return new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Font = new Font("Consolas", 9f),
                WordWrap = false,
                BackColor = Color.FromArgb(15, 23, 41),
                ForeColor = Color.FromArgb(219, 228, 245),
                BorderStyle = BorderStyle.FixedSingle,
            };
        }

        // ---------------------------------------------------------- 小组件
        private GroupBox NewGroup(string title, int height)
        {
            return new GroupBox { Text = title, Dock = DockStyle.Top, Height = height, Padding = new Padding(10, 6, 10, 6) };
        }

        private FlowLayoutPanel Row(Control parent)
        {
            var row = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                AutoSize = true,
                Margin = new Padding(0, 3, 0, 3),
            };
            parent.Controls.Add(row);
            return row;
        }

        private Button MkBtn(string text, int width)
        {
            var b = new Button { Text = text, Width = width, Height = 30 };
            _allButtons.Add(b);
            return b;
        }

        private Button PrimaryBtn(string text, int width)
        {
            var b = MkBtn(text, width);
            b.BackColor = Color.FromArgb(36, 86, 214);
            b.ForeColor = Color.White;
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderColor = Color.FromArgb(26, 64, 176);
            return b;
        }

        private Label MkLbl(string text)
        {
            return new Label { Text = text, AutoSize = true, Margin = new Padding(4, 8, 4, 0) };
        }

        private FlashFlow NewFlow()
        {
            return new FlashFlow(Log);
        }

        private string PickFile(string cur, string initDir = null)
        {
            using (var d = new OpenFileDialog())
            {
                d.Filter = "bin|*.bin|key|*.key|all|*.*";
                if (File.Exists(cur)) d.InitialDirectory = Path.GetDirectoryName(cur);
                else if (initDir != null && Directory.Exists(initDir)) d.InitialDirectory = initDir;
                return d.ShowDialog() == DialogResult.OK ? d.FileName : null;
            }
        }

        private void Run(Action action)
        {
            if (_busy) { Log("当前有操作正在进行"); return; }
            _busy = true;
            SetButtons(false);
            Task.Run(() =>
            {
                try { action(); }
                catch (Exception ex) { Log("ERROR: " + ex.Message); }
                finally { _busy = false; BeginInvoke((Action)(() => SetButtons(true))); }
            });
        }

        private void SetButtons(bool enabled)
        {
            foreach (var b in _allButtons)
            {
                var bb = b;
                BeginInvoke((Action)(() => bb.Enabled = enabled));
            }
            BeginInvoke((Action)(() => _devInfo.Text = enabled ? _devInfo.Text : "操作中..."));
        }

        private void Log(string s)
        {
            if (_log.InvokeRequired) { BeginInvoke((Action)(() => Log(s))); return; }
            _log.AppendText(DateTime.Now.ToString("HH:mm:ss ") + s.Replace("\n", "\r\n") + Environment.NewLine);
        }

        private static uint ParseHex(string s, uint def)
        {
            try { return Convert.ToUInt32(s.Trim(), 16); } catch { return def; }
        }

        /// <summary>FB06 写入 RAM + FD07 校验，不跳转。（wl82 loader 路径）</summary>
        private void WritePayloadOnly(FlashFlow flow, string devPath, string file, uint addr)
        {
            var up = flow.EnterLoader(devPath);
            var ld = up.Item1;
            byte[] code = File.ReadAllBytes(file);
            Log($"payload {file} ({code.Length} B) -> 0x{addr:X8}");
            for (int off = 0; off < code.Length; off += 256)
            {
                int n = Math.Min(256, code.Length - off);
                ld.MemWrite(addr + (uint)off, Slice(code, off, n));
            }
            var back = new byte[code.Length];
            for (int off = 0; off < code.Length; off += 256)
            {
                int n = Math.Min(256, code.Length - off);
                Array.Copy(ld.MemRead(addr + (uint)off, n), 0, back, off, n);
            }
            Log(back.SequenceEqual(code) ? "写入 + 校验 OK（点「仅跳转」执行）" : "ABORT: 校验失败");
        }

        /// <summary>FB06 写入 SDRAM + 校验 + FB08 跳转。跳转后设备预期消失（载荷持续运行）。</summary>
        private void LoadAndRun(FlashFlow flow, string devPath, string file, uint addr)
        {
            var up = flow.EnterLoader(devPath);
            var ld = up.Item1;
            byte[] code = File.ReadAllBytes(file);
            Log($"payload {file} ({code.Length} B) -> 0x{addr:X8}");
            for (int off = 0; off < code.Length; off += 256)
            {
                int n = Math.Min(256, code.Length - off);
                ld.MemWrite(addr + (uint)off, Slice(code, off, n));
            }
            Log("written, verifying via FD07 ...");
            var back = new byte[code.Length];
            for (int off = 0; off < code.Length; off += 256)
            {
                int n = Math.Min(256, code.Length - off);
                Array.Copy(ld.MemRead(addr + (uint)off, n), 0, back, off, n);
            }
            if (!back.SequenceEqual(code)) { Log("ABORT: memory not written correctly"); return; }
            Log("readback match: True");
            Log($"jumping to 0x{addr:X8} (设备应该会消失) ...");
            try { ld.MemJump(addr); Log("jump command returned"); }
            catch (Exception ex) { Log("jump: " + ex.Message); }
            Thread.Sleep(1500);
            var still = FlashFlow.FindDevices();
            Log(still.Count == 0 ? "设备已消失 -> payload 正在运行!" : "设备仍在: " + string.Join(",", still));
        }

        private static byte[] Slice(byte[] src, int off, int len)
        {
            var r = new byte[len];
            Array.Copy(src, off, r, 0, len);
            return r;
        }
    }
}
