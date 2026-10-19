using System;
using System.Text;
using System.Windows.Forms;

namespace JlFlashGui
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            // isd_download 输出为 GBK：.NET 8 需注册 CodePages 提供程序才能用 GetEncoding("GBK")
            Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            int tab = 0;
            if (args.Length > 0) int.TryParse(args[0], out tab);
            Application.Run(new MainForm(tab));
        }
    }
}
