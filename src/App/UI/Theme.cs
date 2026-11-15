using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace InkPen.UI
{
    /// <summary>
    /// 统一视觉主题（深蓝工业仪表风）。
    ///
    /// 分两层，互不干扰：
    ///   · 画布层（深）—— 中栏纸面区，视觉焦点留在纸和笔迹上，笔尖光晕才看得清
    ///   · 面板层（浅）—— 左右两栏，长时间看表格/日志不刺眼
    ///
    /// 强调色只有一个：深蓝 #185FA5（主操作/选中）。
    /// 语义色只在有语义时出现：绿=连接正常/完成，琥珀=待确认，红=停止/错误。
    /// 早期版本每个控件各写一套灰阶，结果同一页里出现 6 种不同的浅灰边框，
    /// 界面看起来"花"其实不是配色多，而是没有统一令牌。改这里一处就够。
    /// </summary>
    public static class Theme
    {
        // ---------- 画布（深） ----------
        public static readonly Color CanvasBgTop = Color.FromArgb(14, 20, 29);
        public static readonly Color CanvasBgBottom = Color.FromArgb(21, 29, 40);
        public static readonly Color CanvasDot = Color.FromArgb(34, 44, 58);

        // 纸张
        public static readonly Color Paper = Color.FromArgb(253, 252, 248);
        public static readonly Color PaperShadow = Color.FromArgb(80, 0, 0, 0);
        public static readonly Color PaperEdge = Color.FromArgb(120, 130, 146);
        public static readonly Color GridLine = Color.FromArgb(70, 118, 168, 214);

        // 笔迹
        public static readonly Color InkDone = Color.FromArgb(28, 34, 44);
        public static readonly Color InkTodo = Color.FromArgb(105, 190, 200, 208);
        public static readonly Color Travel = Color.FromArgb(70, 150, 165, 185);

        // ---------- 强调 ----------
        public static readonly Color Accent = Color.FromArgb(24, 95, 165);       // #185FA5
        public static readonly Color AccentDark = Color.FromArgb(12, 68, 124);   // #0C447C
        public static readonly Color AccentSoft = Color.FromArgb(230, 241, 251); // #E6F1FB
        public static readonly Color AccentDim = Color.FromArgb(90, 24, 95, 165);

        public static readonly Color Ok = Color.FromArgb(15, 110, 86);           // #0F6E56
        public static readonly Color OkSoft = Color.FromArgb(225, 245, 238);     // #E1F5EE
        public static readonly Color Warm = Color.FromArgb(186, 117, 23);        // #BA7517
        public static readonly Color WarmSoft = Color.FromArgb(250, 238, 218);   // #FAEEDA
        public static readonly Color Danger = Color.FromArgb(163, 45, 45);       // #A32D2D
        public static readonly Color DangerSoft = Color.FromArgb(252, 235, 235); // #FCEBEB

        // ---------- 面板（浅） ----------
        public static readonly Color Panel = Color.White;
        public static readonly Color FormBg = Color.FromArgb(234, 238, 244);     // #EAEEF4
        public static readonly Color CardBg = Color.FromArgb(247, 249, 252);     // #F7F9FC
        public static readonly Color Border = Color.FromArgb(216, 224, 234);     // #D8E0EA
        public static readonly Color BorderStrong = Color.FromArgb(194, 204, 218);
        public static readonly Color Text = Color.FromArgb(23, 32, 44);          // #17202C
        public static readonly Color TextMid = Color.FromArgb(74, 85, 99);       // #4A5563
        public static readonly Color TextDim = Color.FromArgb(124, 136, 153);    // #7C8899
        public static readonly Color TextFaint = Color.FromArgb(160, 171, 185);
        public static readonly Color BarBg = Color.White;

        // 圆角令牌：控件 6，卡片 10
        public const int RControl = 6;
        public const int RCard = 10;

        /// <summary>正文字体。不假设字体族存在，逐个降级（GRBL-Plotter #477 类崩溃的成因）。</summary>
        public static Font Font(float size, FontStyle style)
        {
            try { return new Font("Microsoft YaHei", size, style); }
            catch { }
            try { return new Font("SimSun", size, style); }
            catch { }
            return new Font(SystemFonts.DefaultFont.FontFamily, size, style);
        }

        public static Font Mono(float size, FontStyle style)
        {
            try { return new Font("Consolas", size, style); }
            catch { }
            return Font(size, style);
        }

        /// <summary>圆角矩形路径。radius 会被夹取到不超过短边一半。</summary>
        public static GraphicsPath RoundRect(RectangleF r, float radius)
        {
            GraphicsPath p = new GraphicsPath();
            float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            if (d <= 0.5f)
            {
                p.AddRectangle(r);
                return p;
            }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void FillRound(Graphics g, RectangleF r, float radius, Color c)
        {
            using (GraphicsPath p = RoundRect(r, radius))
            using (SolidBrush b = new SolidBrush(c))
                g.FillPath(b, p);
        }

        public static void DrawRound(Graphics g, RectangleF r, float radius, Color c, float w)
        {
            using (GraphicsPath p = RoundRect(r, radius))
            using (Pen pen = new Pen(c, w))
                g.DrawPath(pen, p);
        }

        /// <summary>柔和光晕：若干层同心圆，外淡内浓。比高斯模糊便宜，GDI+ 下够用。</summary>
        public static void Glow(Graphics g, PointF c, float radius, Color color, int layers)
        {
            for (int i = layers; i >= 1; i--)
            {
                float rr = radius * i / layers;
                int a = (int)(color.A * 0.55 * (1.0 - (double)(i - 1) / layers));
                if (a <= 2) continue;
                using (SolidBrush b = new SolidBrush(Color.FromArgb(a, color)))
                    g.FillEllipse(b, c.X - rr, c.Y - rr, rr * 2, rr * 2);
            }
        }

        public static Color Blend(Color a, Color b, double k)
        {
            if (k < 0) k = 0; else if (k > 1) k = 1;
            return Color.FromArgb(
                (int)(a.A + (b.A - a.A) * k),
                (int)(a.R + (b.R - a.R) * k),
                (int)(a.G + (b.G - a.G) * k),
                (int)(a.B + (b.B - a.B) * k));
        }

        /// <summary>
        /// 扁平按钮外观（保留给旧调用点）。主界面统一用 UiButton 自绘，
        /// 这里仍在是因为 ProbeDialog 等小窗还挂系统 Button。
        /// </summary>
        public static void StyleButton(Button b, Color back, Color fore, Color border)
        {
            b.FlatStyle = FlatStyle.Flat;
            b.BackColor = back;
            b.ForeColor = fore;
            b.FlatAppearance.BorderColor = border;
            b.FlatAppearance.BorderSize = 1;
            b.Cursor = Cursors.Hand;
            Color hover = Blend(back, Color.White, 0.14);
            Color press = Blend(back, Color.Black, 0.10);
            b.MouseEnter += delegate { b.BackColor = hover; };
            b.MouseLeave += delegate { b.BackColor = back; };
            b.MouseDown += delegate { b.BackColor = press; };
            b.MouseUp += delegate { b.BackColor = hover; };
        }
    }

    /// <summary>
    /// 工具栏渲染器。
    ///
    /// 默认的 ToolStripProfessionalRenderer 会画系统蓝框和灰色渐变，跟深蓝主色打架。
    /// 这里统一成：悬停 = 极淡底，选中 = 淡蓝底 + 深蓝字。
    /// </summary>
    public class BarRenderer : ToolStripProfessionalRenderer
    {
        public BarRenderer() { RoundedEdges = false; }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            using (SolidBrush b = new SolidBrush(Theme.BarBg))
                e.Graphics.FillRectangle(b, e.AffectedBounds);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            using (Pen p = new Pen(Theme.Border))
                e.Graphics.DrawLine(p, 0, e.ToolStrip.Height - 1, e.ToolStrip.Width, e.ToolStrip.Height - 1);
        }

        protected override void OnRenderButtonBackground(ToolStripItemRenderEventArgs e)
        {
            ToolStripButton b = e.Item as ToolStripButton;
            if (b == null) return;
            if (!b.Checked && !b.Pressed && !b.Selected) return;

            RectangleF r = new RectangleF(1, 3, b.Width - 2, b.Height - 6);
            if (b.Checked)
                Theme.FillRound(e.Graphics, r, Theme.RControl, Theme.AccentSoft);
            else if (b.Pressed)
                Theme.FillRound(e.Graphics, r, Theme.RControl, Theme.Blend(Theme.AccentSoft, Theme.Border, 0.45));
            else
                Theme.FillRound(e.Graphics, r, Theme.RControl, Color.FromArgb(243, 246, 250));
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            Rectangle r = e.Item.Bounds;
            int x = r.Left + r.Width / 2;
            using (Pen p = new Pen(Theme.Border))
                e.Graphics.DrawLine(p, x, r.Top + 8, x, r.Bottom - 8);
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            if (e.Item is ToolStripLabel)
                e.TextColor = Theme.TextDim;
            else if (e.Item is ToolStripButton)
                e.TextColor = ((ToolStripButton)e.Item).Checked ? Theme.AccentDark : Theme.TextMid;
            else
                e.TextColor = e.Item.Enabled ? Theme.Text : Theme.TextFaint;
            base.OnRenderItemText(e);
        }
    }
}
