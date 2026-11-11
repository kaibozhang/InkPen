using System;
using System.Drawing;
using System.Windows.Forms;

namespace InkPen.UI
{
    /// <summary>
    /// 探测进度对话框。只负责显示与「中止」，探测本身由 ProbeRunner 驱动，
    /// 两者通过 MainForm 的回调连起来——对话框不碰串口，避免关窗后还在发指令。
    /// </summary>
    public class ProbeDialog : Form
    {
        private readonly Label _title;
        private readonly Label _big;
        private readonly Label _sub;
        private readonly ListBox _log;
        private readonly Button _cancel;
        private readonly ProgressBar _bar;
        private bool _finished;

        public bool CancelRequested { get; private set; }

        public ProbeDialog(string title, string hint)
        {
            Text = "接触式探测";
            Font = Theme.Font(9f, FontStyle.Regular);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(520, 330);
            MaximizeBox = false;
            MinimizeBox = false;
            BackColor = Theme.Panel;

            _title = new Label();
            _title.Text = title;
            _title.Font = Theme.Font(12f, FontStyle.Bold);
            _title.ForeColor = Theme.Text;
            _title.Left = 18; _title.Top = 16; _title.Width = 480; _title.Height = 26;
            Controls.Add(_title);

            _big = new Label();
            _big.Text = "准备中…";
            _big.Font = Theme.Mono(20f, FontStyle.Bold);
            _big.ForeColor = Theme.Accent;
            _big.Left = 18; _big.Top = 46; _big.Width = 480; _big.Height = 34;
            Controls.Add(_big);

            _sub = new Label();
            _sub.Text = hint;
            _sub.ForeColor = Theme.TextDim;
            _sub.Left = 18; _sub.Top = 80; _sub.Width = 480; _sub.Height = 20;
            Controls.Add(_sub);

            _bar = new ProgressBar();
            _bar.Left = 18; _bar.Top = 104; _bar.Width = 482; _bar.Height = 8;
            Controls.Add(_bar);

            _log = new ListBox();
            _log.Left = 18; _log.Top = 122; _log.Width = 482; _log.Height = 148;
            _log.Font = Theme.Mono(8.5f, FontStyle.Regular);
            _log.BorderStyle = BorderStyle.FixedSingle;
            _log.HorizontalScrollbar = true;
            Controls.Add(_log);

            _cancel = new Button();
            _cancel.Text = "中止";
            _cancel.Left = 414; _cancel.Top = 282; _cancel.Width = 86; _cancel.Height = 32;
            Theme.StyleButton(_cancel, Color.White, Theme.Text, Theme.Border);
            _cancel.Click += delegate
            {
                CancelRequested = true;
                _cancel.Enabled = false;
                _big.ForeColor = Theme.Warm;
                _big.Text = "正在中止…";
            };
            Controls.Add(_cancel);
        }

        public void SetProgress(int done, int total, string msg)
        {
            _bar.Maximum = Math.Max(1, total);
            _bar.Value = Math.Max(0, Math.Min(done, _bar.Maximum));
            _big.Text = done + " / " + total;
            _sub.Text = msg;
            Application.DoEvents();
        }

        public void AddLog(string s)
        {
            _log.Items.Add(s);
            if (_log.Items.Count > 200) _log.Items.RemoveAt(0);
            _log.TopIndex = Math.Max(0, _log.Items.Count - 1);
        }

        public void Finish(string summary, bool ok)
        {
            if (_finished) return;
            _finished = true;
            _big.Text = ok ? "完成" : "未完成";
            _big.ForeColor = ok ? Theme.Ok : Theme.Danger;
            _sub.Text = summary;
            _cancel.Text = "关闭";
            _cancel.Enabled = true;
            _cancel.Click += delegate { };
            Application.DoEvents();
            // 留 600ms 给用户看到结果，再自动关；点「关闭」也能立刻关
            Timer t = new Timer();
            t.Interval = 600;
            t.Tick += delegate
            {
                t.Stop();
                if (!IsDisposed) Close();
            };
            t.Start();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // 用户直接点 X 也当作中止，避免 MainForm 一直等一个不再有人看的对话框
            if (!_finished) CancelRequested = true;
            base.OnFormClosing(e);
        }
    }
}
