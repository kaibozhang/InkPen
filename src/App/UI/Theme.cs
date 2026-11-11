using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace InkPen.UI
{
    /// <summary>
    /// 统一视觉主题。集中放颜色与绘制工具，避免每个控件各写一套灰阶导致界面花掉。
    ///
    /// 配色思路：
    ///   · 画布用深色（视觉焦点留给纸和笔迹，发光笔尖才看得清）
    ///   · 面板用浅色（长时间看表格/日志不刺眼，也不会出现深色主题常见的「某些控件白底黑字」）
    ///   · 强调色只有两个：青（运行/正常）与琥珀（暂停/待确认），红色只留给急停与错误
    /// </summary>
    public static class Theme
    {
        // 画布（深）
        public static readonly Color CanvasBgTop = Color.FromArgb(18, 23, 32);
        public static readonly Color CanvasBgBottom = Color.FromArgb(27, 34, 46);
        public static readonly Color CanvasDot = Color.FromArgb(38, 47, 62);

        // 纸张
        public static readonly Color Paper = Color.FromArgb(253, 252, 248);
        public static readonly Color PaperShadow = Color.FromArgb(70, 0, 0, 0);
        public static readonly Color PaperEdge = Color.FromArgb(120, 130, 146);
        public static readonly Color GridLine = Color.FromArgb(70, 118, 168, 214);

        // 笔迹
        public static readonly Color InkDone = Color.FromArgb(30, 36, 46);
        public static readonly Color InkTodo = Color.FromArgb(105, 190, 200, 208);
        public static readonly Color Travel = Color.FromArgb(70, 150, 165, 185);

        // 强调
        public static readonly Color Accent = Color.FromArgb(38, 196, 176);
        public static readonly Color AccentDim = Color.FromArgb(90, 38, 196, 176);
        public static readonly Color Warm = Color.FromArgb(238, 168, 72);
        public static readonly Color Danger = Color.FromArgb(214, 74, 66);
        public static readonly Color Ok = Color.FromArgb(46, 158, 110);

        // 面板（浅）
        public static readonly Color Panel = Color.White;
        public static readonly Color FormBg = Color.FromArgb(238, 241, 246);
        public static readonly Color CardBg = Color.FromArgb(249, 250, 253);
        public static readonly Color Border = Color.FromArgb(214, 221, 232);
        public static readonly Color Text = Color.FromArgb(52, 60, 74);
        public static readonly Color TextDim = Color.FromArgb(112, 122, 138);
        public static readonly Color BarBg = Color.FromArgb(250, 251, 253);

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

        /// <summary>给按钮套上统一的扁平外观 + 悬停/按下反馈。</summary>
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

        public static Color Blend(Color a, Color b, double k)
        {
            if (k < 0) k = 0; else if (k > 1) k = 1;
            return Color.FromArgb(
                (int)(a.A + (b.A - a.A) * k),
                (int)(a.R + (b.R - a.R) * k),
                (int)(a.G + (b.G - a.G) * k),
                (int)(a.B + (b.B - a.B) * k));
        }
    }

    /// <summary>
    /// 工具栏渲染器。
    ///
    /// 默认的 <see cref="ToolStripProfessionalRenderer"/> 在浅色工具栏上会把「按下」画成
    /// 系统蓝框、把「选中」画成灰色渐变，与我们自己的青/琥珀配色打架——截图里最明显的症状是
    /// 「空程」「笔位」两个 CheckOnClick 按钮看起来像被蓝色框圈住，而旁边的「播放」只是纯文字。
    /// 这里统一成：悬停 = 极淡青底，选中 = 淡青底 + 细边，标签类文字压暗一档做层次。
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

            RectangleF r = new RectangleF(1, 2, b.Width - 2, b.Height - 4);
            double k = b.Checked ? 0.80 : (b.Pressed ? 0.70 : 0.90);
            Theme.FillRound(e.Graphics, r, 5, Theme.Blend(Theme.Accent, Theme.BarBg, k));
            Theme.DrawRound(e.Graphics, r, 5,
                Theme.Blend(Theme.Accent, Theme.BarBg, b.Checked ? 0.45 : 0.72), 1f);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            Rectangle r = e.Item.Bounds;
            int x = r.Left + r.Width / 2;
            using (Pen p = new Pen(Theme.Border))
                e.Graphics.DrawLine(p, x, r.Top + 5, x, r.Bottom - 5);
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = (!e.Item.Enabled || e.Item is ToolStripLabel) ? Theme.TextDim : Theme.Text;
            base.OnRenderItemText(e);
        }
    }
}
