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
        private static void Main()
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
            try { Application.Run(new MainForm()); }
            catch (Exception ex)
            {
                MessageBox.Show("启动失败：\n" + ex, "InkPen", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
