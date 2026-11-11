using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using InkPen.UI;

namespace InkPen
{
    internal static class Program
    {
        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        [STAThread]
        private static void Main(string[] args)
        {
            // 应对 GRBL-Plotter Issues 中提到的 HiDPI 缩放模糊问题
            try { SetProcessDPIAware(); }
            catch { }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException += delegate(object s, System.Threading.ThreadExceptionEventArgs e)
            {
                MessageBox.Show("未处理异常：\n" + e.Exception, "InkPen", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };
            // 隐藏参数 --tab=level|machine|settings|gcode 供界面自动化截图用，
            // 不改变任何默认行为（不带参数时与以前完全一致）。
            foreach (string a in args)
            {
                if (a != null && a.StartsWith("--tab=", StringComparison.Ordinal))
                    MainForm.StartupTab = a.Substring(6);
            }
            try { Application.Run(new MainForm()); }
            catch (Exception ex)
            {
                MessageBox.Show("启动失败：\n" + ex, "InkPen", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
