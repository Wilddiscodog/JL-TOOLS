using System;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using JlFlash.Flow;

namespace JlFlash
{
    internal sealed class MainForm : Form
    {
        private readonly TextBox _imagePath = new TextBox();
        private readonly Button _browse = new Button();
        private readonly Button _flash = new Button();
        private readonly Button _scan = new Button();
        private readonly CheckBox _verify = new CheckBox();
        private readonly TextBox _log = new TextBox();

        private const string DefaultImage = @"D:\fw-AC79_AIoT_SDK\cpu\wl82\tools\jl_isd.bin";

        public MainForm()
        {
            Text = "JlFlash";
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(760, 520);
            MinimumSize = new Size(640, 420);
            Font = new Font("Microsoft YaHei UI", 9f);

            var top = new Panel { Dock = DockStyle.Top, Height = 78, Padding = new Padding(12, 10, 12, 8) };
            var pathLabel = new Label { Text = "固件", AutoSize = true, Location = new Point(12, 14) };
            _imagePath.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
            _imagePath.Location = new Point(52, 10);
            _imagePath.Width = 560;
            _browse.Text = "浏览";
            _browse.Size = new Size(72, 28);
            _browse.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _browse.Location = new Point(620, 8);
            _browse.Click += BrowseClick;

            _flash.Text = "烧录";
            _flash.Size = new Size(88, 30);
            _flash.Location = new Point(52, 42);
            _flash.Click += FlashClick;

            _scan.Text = "检测设备";
            _scan.Size = new Size(88, 30);
            _scan.Location = new Point(148, 42);
            _scan.Click += ScanClick;

            _verify.Text = "写完后校验";
            _verify.AutoSize = true;
            _verify.Checked = true;
            _verify.Location = new Point(250, 48);

            top.Controls.Add(pathLabel);
            top.Controls.Add(_imagePath);
            top.Controls.Add(_browse);
            top.Controls.Add(_flash);
            top.Controls.Add(_scan);
            top.Controls.Add(_verify);
            top.Resize += delegate
            {
                _browse.Left = top.ClientSize.Width - _browse.Width - 12;
                _imagePath.Width = _browse.Left - _imagePath.Left - 8;
            };

            _log.Dock = DockStyle.Fill;
            _log.Multiline = true;
            _log.ReadOnly = true;
            _log.ScrollBars = ScrollBars.Both;
            _log.WordWrap = false;
            _log.Font = new Font("Consolas", 9f);
            _log.BackColor = Color.White;

            Controls.Add(_log);
            Controls.Add(top);
            Load += delegate
            {
                _browse.Left = top.ClientSize.Width - _browse.Width - 12;
                _imagePath.Width = Math.Max(80, _browse.Left - _imagePath.Left - 8);
            };

            _imagePath.Text = File.Exists(LoadLastPath()) ? LoadLastPath()
                : (File.Exists(DefaultImage) ? DefaultImage : "");
        }

        private static string LastPathFile
        {
            get { return Path.Combine(AppContext.BaseDirectory, "JlFlash.last"); }
        }

        private static string LoadLastPath()
        {
            try { return File.Exists(LastPathFile) ? File.ReadAllText(LastPathFile).Trim() : ""; }
            catch { return ""; }
        }

        private void SaveLastPath(string path)
        {
            try { File.WriteAllText(LastPathFile, path); }
            catch { }
        }

        private void BrowseClick(object sender, EventArgs e)
        {
            using (var dlg = new OpenFileDialog())
            {
                dlg.Filter = "固件镜像 (*.bin;*.fw)|*.bin;*.fw|所有文件 (*.*)|*.*";
                dlg.Title = "选择要烧录的镜像";
                if (!string.IsNullOrWhiteSpace(_imagePath.Text))
                {
                    dlg.InitialDirectory = Path.GetDirectoryName(_imagePath.Text);
                    dlg.FileName = Path.GetFileName(_imagePath.Text);
                }
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    _imagePath.Text = dlg.FileName;
                    SaveLastPath(dlg.FileName);
                }
            }
        }

        private void AppendLog(string line)
        {
            if (IsDisposed) return;
            if (InvokeRequired)
            {
                BeginInvoke(new Action<string>(AppendLog), line);
                return;
            }
            _log.AppendText(line + Environment.NewLine);
        }

        private void SetBusy(bool busy)
        {
            _flash.Enabled = !busy;
            _scan.Enabled = !busy;
            _browse.Enabled = !busy;
            _imagePath.Enabled = !busy;
            _verify.Enabled = !busy;
            _flash.Text = busy ? "烧录中" : "烧录";
        }

        private async void FlashClick(object sender, EventArgs e)
        {
            string path = _imagePath.Text.Trim();
            if (path.Length == 0 || !File.Exists(path))
            {
                AppendLog("找不到固件文件");
                return;
            }
            bool verify = _verify.Checked;
            SaveLastPath(path);
            SetBusy(true);
            AppendLog("---- 开始烧录 ----");
            try
            {
                await Task.Run(delegate
                {
                    byte[] bin = File.ReadAllBytes(path);
                    AppendLog("Image: " + path + " (" + bin.Length + " bytes)");
                    var flow = new FlashFlow(AppendLog);
                    string dev = flow.WaitForDevice(8000);
                    AppendLog("Device: " + dev);
                    flow.FlashImage(dev, bin, verify);
                });
                AppendLog("---- 烧录完成 ----");
            }
            catch (Exception ex)
            {
                AppendLog("ERROR: " + ex.Message);
                AppendLog("---- 烧录失败 ----");
            }
            finally
            {
                SetBusy(false);
            }
        }

        private async void ScanClick(object sender, EventArgs e)
        {
            SetBusy(true);
            AppendLog("正在检测下载设备 ...");
            try
            {
                var devs = await Task.Run(delegate { return FlashFlow.FindDevices(); });
                if (devs.Count == 0)
                    AppendLog("没有找到设备。请插上 USB 数据线，让芯片进入下载模式。");
                else
                    foreach (var d in devs) AppendLog("  " + d);
            }
            catch (Exception ex)
            {
                AppendLog("ERROR: " + ex.Message);
            }
            finally
            {
                SetBusy(false);
            }
        }
    }
}
