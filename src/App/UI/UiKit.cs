using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace InkPen.UI
{
    // =====================================================================
    //  自绘控件集
    //
    //  为什么不用系统控件：WinForms 的 Button / GroupBox / CheckBox 是 1990 年代
    //  的 3D 边框风格，配合 DPI 缩放在 1080p 上会显得糊且碎片化（一个页面里同时出现
    //  凸起按钮、凹陷输入框、灰色分组框线），而 GRBL 系工具（Candle / UGS / LaserGRBL）
    //  早就统一成了扁平卡片式。
    //
    //  这里所有控件都遵守同一套令牌（颜色/圆角/字号全部来自 Theme），
    //  所以"顺手改一个按钮"不会再破坏整体一致性。
    // =====================================================================

    public enum UiKind
    {
        /// <summary>白底细边，次级操作</summary>
        Ghost,
        /// <summary>深蓝实心，一个区域里只该有一个</summary>
        Primary,
        /// <summary>淡红底红字，停止/急停</summary>
        Danger,
        /// <summary>无边框，悬停才出现底色</summary>
        Quiet
    }

    public enum IconKind
    {
        None, Play, Pause, Step, Stop, Reset, Bolt, Download, Save, Folder,
        Gear, Plug, Target, Home, Unlock, Pen, ZoomIn, ZoomOut, Fit, Prev, Next, Warn
    }

    internal static class UiPaint
    {
        /// <summary>
        /// UserPaint + AllPaintingInWmPaint 下系统不再自动擦背景。
        /// 自绘控件如果只画"有新东西"的部分，上一帧的字和色块会一层层叠上去
        /// （表现为文字重影、蓝块越画越大）。所以每个 OnPaint 的第一件事都是擦这块。
        /// </summary>
        public static void Clear(Control c, Graphics g)
        {
            g.ResetTransform();
            Color back = c.Parent != null ? c.Parent.BackColor : c.BackColor;
            if (back.A == 0) back = Color.White;
            using (SolidBrush b = new SolidBrush(back))
                g.FillRectangle(b, c.ClientRectangle);
        }
    }

    /// <summary>矢量图标。全部用 1.6px 线宽手绘，随 DPI 缩放不失真，也不依赖图标字体。</summary>
    public static class Icons
    {
        public static Bitmap Render(IconKind kind, int size, Color color)
        {
            Bitmap bmp = new Bitmap(size, size);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                float s = size / 16f;
                g.ScaleTransform(s, s);
                Draw(g, kind, new RectangleF(1, 1, 14, 14), color, 1.5f);
            }
            return bmp;
        }

        /// <summary>在指定矩形里画图标。坐标按 16×16 设计，内部等比缩放到目标矩形。</summary>
        public static void Draw(Graphics g, IconKind kind, RectangleF r, Color color, float w)
        {
            if (kind == IconKind.None || r.Width <= 1 || r.Height <= 1) return;
            GraphicsState st = g.Save();
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TranslateTransform(r.X, r.Y);
            g.ScaleTransform(r.Width / 16f, r.Height / 16f);
            using (Pen p = new Pen(color, w))
            using (SolidBrush b = new SolidBrush(color))
            {
                p.StartCap = LineCap.Round;
                p.EndCap = LineCap.Round;
                p.LineJoin = LineJoin.Round;
                switch (kind)
                {
                    case IconKind.Play:
                        g.FillPolygon(b, new PointF[] {
                            new PointF(5.5f, 3.5f), new PointF(12.5f, 8f), new PointF(5.5f, 12.5f) });
                        break;
                    case IconKind.Pause:
                        g.FillRectangle(b, new RectangleF(5f, 3.5f, 2.2f, 9f));
                        g.FillRectangle(b, new RectangleF(9.4f, 3.5f, 2.2f, 9f));
                        break;
                    case IconKind.Step:
                        g.FillPolygon(b, new PointF[] {
                            new PointF(4f, 3.5f), new PointF(9.5f, 8f), new PointF(4f, 12.5f) });
                        g.FillRectangle(b, new RectangleF(10.6f, 3.5f, 1.8f, 9f));
                        break;
                    case IconKind.Stop:
                        g.FillRectangle(b, new RectangleF(4.5f, 4.5f, 7f, 7f));
                        break;
                    case IconKind.Reset:
                        g.DrawArc(p, 3.5f, 3.5f, 9f, 9f, 40, 280);
                        g.FillPolygon(b, new PointF[] {
                            new PointF(12.6f, 3.2f), new PointF(12.8f, 6.4f), new PointF(9.6f, 5.6f) });
                        break;
                    case IconKind.Bolt:
                        g.FillPolygon(b, new PointF[] {
                            new PointF(9.2f, 1.6f), new PointF(4.2f, 9f), new PointF(7.6f, 9f),
                            new PointF(6.8f, 14.4f), new PointF(11.8f, 6.8f), new PointF(8.4f, 6.8f) });
                        break;
                    case IconKind.Download:
                        g.DrawLine(p, 8f, 2.8f, 8f, 9.6f);
                        g.DrawLine(p, 5.2f, 7f, 8f, 9.8f);
                        g.DrawLine(p, 10.8f, 7f, 8f, 9.8f);
                        g.DrawLine(p, 3.6f, 12.8f, 12.4f, 12.8f);
                        break;
                    case IconKind.Save:
                        g.DrawRectangle(p, 3f, 3f, 10f, 10f);
                        g.FillRectangle(b, new RectangleF(5.6f, 3f, 4.8f, 3.4f));
                        g.DrawRectangle(p, 5.4f, 9f, 5.2f, 4f);
                        break;
                    case IconKind.Folder:
                        g.DrawLines(p, new PointF[] {
                            new PointF(2.4f, 12.4f), new PointF(2.4f, 4.4f), new PointF(6.4f, 4.4f),
                            new PointF(7.8f, 6.2f), new PointF(13.6f, 6.2f), new PointF(13.6f, 12.4f),
                            new PointF(2.4f, 12.4f) });
                        break;
                    case IconKind.Gear:
                        g.DrawEllipse(p, 5.6f, 5.6f, 4.8f, 4.8f);
                        for (int i = 0; i < 6; i++)
                        {
                            double a = i * Math.PI / 3.0;
                            float x1 = 8f + (float)Math.Cos(a) * 4.2f, y1 = 8f + (float)Math.Sin(a) * 4.2f;
                            float x2 = 8f + (float)Math.Cos(a) * 6.4f, y2 = 8f + (float)Math.Sin(a) * 6.4f;
                            g.DrawLine(p, x1, y1, x2, y2);
                        }
                        break;
                    case IconKind.Plug:
                        g.DrawLine(p, 8f, 8f, 8f, 12.6f);
                        g.DrawLine(p, 5f, 5.2f, 5f, 8.4f);
                        g.DrawLine(p, 11f, 5.2f, 11f, 8.4f);
                        g.DrawArc(p, 4.4f, 6.4f, 7.2f, 4.4f, 0, 180);
                        g.DrawLine(p, 4f, 13.4f, 12f, 13.4f);
                        break;
                    case IconKind.Target:
                        g.DrawEllipse(p, 2.8f, 2.8f, 10.4f, 10.4f);
                        g.DrawEllipse(p, 6.2f, 6.2f, 3.6f, 3.6f);
                        g.DrawLine(p, 8f, 1f, 8f, 3.4f);
                        g.DrawLine(p, 8f, 12.6f, 8f, 15f);
                        g.DrawLine(p, 1f, 8f, 3.4f, 8f);
                        g.DrawLine(p, 12.6f, 8f, 15f, 8f);
                        break;
                    case IconKind.Home:
                        g.DrawLines(p, new PointF[] {
                            new PointF(2.6f, 7.4f), new PointF(8f, 2.6f), new PointF(13.4f, 7.4f) });
                        g.DrawLines(p, new PointF[] {
                            new PointF(4.4f, 7.4f), new PointF(4.4f, 13.4f), new PointF(11.6f, 13.4f),
                            new PointF(11.6f, 7.4f) });
                        break;
                    case IconKind.Unlock:
                        g.DrawRectangle(p, 4f, 7.4f, 8f, 6f);
                        g.DrawArc(p, 6.2f, 3f, 5.2f, 5.2f, 180, 160);
                        break;
                    case IconKind.Pen:
                        g.DrawLine(p, 3.2f, 12.8f, 12.2f, 3.8f);
                        g.DrawLine(p, 10.4f, 2.6f, 13.4f, 5.6f);
                        g.FillPolygon(b, new PointF[] {
                            new PointF(3.2f, 12.8f), new PointF(5.4f, 12.2f), new PointF(3.8f, 10.6f) });
                        break;
                    case IconKind.ZoomIn:
                        g.DrawEllipse(p, 3f, 3f, 8f, 8f);
                        g.DrawLine(p, 10.4f, 10.4f, 13.6f, 13.6f);
                        g.DrawLine(p, 5.2f, 7f, 8.8f, 7f);
                        g.DrawLine(p, 7f, 5.2f, 7f, 8.8f);
                        break;
                    case IconKind.ZoomOut:
                        g.DrawEllipse(p, 3f, 3f, 8f, 8f);
                        g.DrawLine(p, 10.4f, 10.4f, 13.6f, 13.6f);
                        g.DrawLine(p, 5.2f, 7f, 8.8f, 7f);
                        break;
                    case IconKind.Fit:
                        g.DrawRectangle(p, 2.6f, 3.6f, 10.8f, 8.8f);
                        g.DrawLine(p, 5.4f, 6.2f, 5.4f, 9.8f);
                        g.DrawLine(p, 5.4f, 6.2f, 10.6f, 6.2f);
                        break;
                    case IconKind.Prev:
                        g.FillPolygon(b, new PointF[] {
                            new PointF(10.4f, 3.6f), new PointF(5.4f, 8f), new PointF(10.4f, 12.4f) });
                        break;
                    case IconKind.Next:
                        g.FillPolygon(b, new PointF[] {
                            new PointF(5.6f, 3.6f), new PointF(10.6f, 8f), new PointF(5.6f, 12.4f) });
                        break;
                    case IconKind.Warn:
                        g.DrawLines(p, new PointF[] {
                            new PointF(8f, 2.8f), new PointF(13.6f, 12.8f), new PointF(2.4f, 12.8f),
                            new PointF(8f, 2.8f) });
                        g.DrawLine(p, 8f, 6.2f, 8f, 9.4f);
                        g.FillEllipse(b, 7.2f, 10.4f, 1.6f, 1.6f);
                        break;
                }
            }
            g.Restore(st);
        }
    }

    /// <summary>自绘按钮：白底细边 / 深蓝实心 / 淡红。带图标与悬停反馈，尺寸由调用方手量。</summary>
    public class UiButton : Button
    {
        public UiKind Kind = UiKind.Ghost;
        public IconKind Icon = IconKind.None;
        private bool _hover, _down;

        public UiButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                   | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            BackColor = Theme.Panel;
            Font = Theme.Font(9f, FontStyle.Regular);
            Padding = new Padding(0);
            Cursor = Cursors.Hand;
            TabStop = false;
            UseVisualStyleBackColor = false;
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; _down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { _down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { _down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            UiPaint.Clear(this, g);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            RectangleF r = new RectangleF(0, 0, Width - 1, Height - 1);

            Color fill, edge, fore;
            switch (Kind)
            {
                case UiKind.Primary:
                    fill = _down ? Theme.AccentDark : (_hover ? Theme.Blend(Theme.Accent, Color.Black, 0.12) : Theme.Accent);
                    edge = fill; fore = Color.White; break;
                case UiKind.Danger:
                    fill = _down ? Color.FromArgb(244, 214, 214) : (_hover ? Color.FromArgb(250, 224, 224) : Theme.DangerSoft);
                    edge = Color.FromArgb(240, 201, 201); fore = Theme.Danger; break;
                case UiKind.Quiet:
                    fill = _hover ? Theme.AccentSoft : Theme.Panel;
                    edge = fill; fore = Theme.TextMid; break;
                default:
                    fill = _down ? Color.FromArgb(236, 241, 247) : (_hover ? Color.FromArgb(245, 248, 252) : Theme.Panel);
                    edge = _hover ? Theme.BorderStrong : Theme.Border; fore = Theme.TextMid; break;
            }
            if (!Enabled) { fill = Color.FromArgb(243, 245, 248); edge = Color.FromArgb(228, 233, 239); fore = Theme.TextFaint; }

            Theme.FillRound(g, r, Theme.RControl, fill);
            if (Kind != UiKind.Quiet || Enabled)
                Theme.DrawRound(g, r, Theme.RControl, edge, 1f);

            string text = Text == null ? "" : Text;
            int icon = (Icon != IconKind.None) ? 15 : 0;
            int gap = (icon > 0 && text.Length > 0) ? 5 : 0;
            Size ts = text.Length > 0
                ? TextRenderer.MeasureText(text, Font, new Size(1000, Height), TextFormatFlags.NoPadding)
                : Size.Empty;
            int total = icon + gap + ts.Width;
            float x = (Width - total) / 2f;
            float cy = Height / 2f;

            if (icon > 0)
            {
                Icons.Draw(g, Icon, new RectangleF(x, cy - icon / 2f, icon, icon), fore, 1.5f);
                x += icon + gap;
            }
            if (text.Length > 0)
            {
                Rectangle tr = new Rectangle((int)Math.Round(x), 0, ts.Width + 4, Height);
                TextRenderer.DrawText(g, text, Font, tr, fore,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.Left);
            }

            if (Focused && ShowFocusCues)
                Theme.DrawRound(g, new RectangleF(1.5f, 1.5f, Width - 4, Height - 4), Theme.RControl - 1, Theme.AccentDim, 1f);
        }
    }

    /// <summary>
    /// 卡片容器。替代 GroupBox：圆角白底 + 标题行，标题右侧可挂一个状态徽标。
    /// BackColor 与页面同色，圆角外的四个角自然融掉（比开透明背景便宜且不闪）。
    /// </summary>
    public class UiCard : Panel
    {
        public string Title = "";
        public string Badge = "";
        public Color BadgeFore = Theme.TextDim;
        public Color BadgeBack = Color.Empty;
        public int HeaderH = 32;
        /// <summary>标题右侧的说明文字（灰色，不占徽标位）。</summary>
        public string Hint = "";

        public UiCard()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                   | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.FormBg;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            using (SolidBrush b = new SolidBrush(Parent != null ? Parent.BackColor : Theme.FormBg))
                e.Graphics.FillRectangle(b, ClientRectangle);
        }

        /// <summary>
        /// Label 默认继承父容器 BackColor，而卡片的 BackColor 是页面底色（圆角要靠它融边），
        /// 结果白卡上每条标题都拖一条灰底。加进来就统一改成白底。
        /// </summary>
        protected override void OnControlAdded(ControlEventArgs e)
        {
            base.OnControlAdded(e);
            if (e.Control is Label) e.Control.BackColor = Theme.Panel;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            RectangleF r = new RectangleF(0, 0, Width - 1, Height - 1);
            Theme.FillRound(g, r, Theme.RCard, Theme.Panel);
            Theme.DrawRound(g, r, Theme.RCard, Theme.Border, 1f);

            if (!string.IsNullOrEmpty(Title))
            {
                using (Font f = Theme.Font(9.5f, FontStyle.Bold))
                using (SolidBrush b = new SolidBrush(Theme.TextMid))
                    g.DrawString(Title, f, b, 13, 9);
            }

            float right = Width - 13;
            if (!string.IsNullOrEmpty(Badge))
            {
                using (Font f = Theme.Font(8f, FontStyle.Regular))
                {
                    SizeF sz = g.MeasureString(Badge, f);
                    float bw = sz.Width + 6, bh = 17;
                    RectangleF br = new RectangleF(right - bw, 8, bw, bh);
                    Color back = BadgeBack.IsEmpty ? Theme.CardBg : BadgeBack;
                    Theme.FillRound(g, br, 4, back);
                    using (SolidBrush b = new SolidBrush(BadgeFore))
                    using (StringFormat sf = new StringFormat())
                    {
                        sf.Alignment = StringAlignment.Center;
                        sf.LineAlignment = StringAlignment.Center;
                        g.DrawString(Badge, f, b, br, sf);
                    }
                    right = br.X - 8;
                }
            }
            if (!string.IsNullOrEmpty(Hint))
            {
                using (Font f = Theme.Font(8f, FontStyle.Regular))
                using (SolidBrush b = new SolidBrush(Theme.TextFaint))
                using (StringFormat sf = new StringFormat())
                {
                    sf.Alignment = StringAlignment.Far;
                    sf.LineAlignment = StringAlignment.Center;
                    g.DrawString(Hint, f, b, new RectangleF(60, 8, right - 60, 18), sf);
                }
            }
        }
    }

    /// <summary>开关。替代 CheckBox 的系统勾选框：34×18 胶囊 + 滑块，勾选才染色。</summary>
    public class UiSwitch : CheckBox
    {
        public UiSwitch()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                   | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            AutoSize = false;
            Height = 22;
            Font = Theme.Font(9f, FontStyle.Regular);
            ForeColor = Theme.TextMid;
            Cursor = Cursors.Hand;
            BackColor = Color.Transparent;
        }

        protected override void OnCheckedChanged(EventArgs e) { Invalidate(); base.OnCheckedChanged(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnMouseEnter(EventArgs e) { Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            UiPaint.Clear(this, g);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int tw = 34, th = 18;
            float ty = (Height - th) / 2f;
            Color track = !Enabled ? Color.FromArgb(226, 231, 238)
                        : (Checked ? Theme.Accent : Color.FromArgb(203, 212, 223));
            Theme.FillRound(g, new RectangleF(0, ty, tw, th), th / 2f, track);
            float kx = Checked ? tw - th + 3 : 3;
            using (SolidBrush b = new SolidBrush(Color.White))
                g.FillEllipse(b, kx, ty + 3, th - 6, th - 6);

            if (!string.IsNullOrEmpty(Text))
            {
                Size ts = TextRenderer.MeasureText(Text, Font, new Size(1000, Height), TextFormatFlags.NoPadding);
                Rectangle tr = new Rectangle(tw + 8, 0, Math.Min(ts.Width + 4, Width - tw - 8), Height);
                TextRenderer.DrawText(g, Text, Font, tr,
                    Enabled ? ForeColor : Theme.TextFaint,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.Left
                    | TextFormatFlags.EndEllipsis);
            }
        }
    }

    /// <summary>
    /// 输入框外壳：圆角细边 + 内嵌真实控件（NumericUpDown / ComboBox / TextBox）。
    ///
    /// 关键点：外壳负责画边框，内层控件的 BorderStyle 一律设成 None。
    /// 这样 DPI 放大时边框不会跟着变成 2px 的"凸起"框，也对齐了同一列的左边界。
    /// </summary>
    public class UiField : Panel
    {
        public readonly Control Inner;
        public int InsetX = 6;
        public int InsetY = 4;

        public UiField(Control inner)
        {
            Inner = inner;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                   | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Panel;
            Height = 28;

            // 注意用 TextBoxBase：RichTextBox 继承的是 TextBoxBase 而不是 TextBox，
            // 写成 `as TextBox` 会让日志框永远走不到多行分支（高度停在 20px）。
            TextBoxBase tb = inner as TextBoxBase;
            if (tb != null) { tb.BorderStyle = BorderStyle.None; }
            ComboBox cb = inner as ComboBox;
            if (cb != null)
            {
                cb.FlatStyle = FlatStyle.Flat;
                cb.DrawMode = DrawMode.OwnerDrawFixed;
                cb.ItemHeight = 20;
                cb.DrawItem += ComboDrawItem;
            }
            NumericUpDown nu = inner as NumericUpDown;
            if (nu != null) { nu.BorderStyle = BorderStyle.None; }

            Controls.Add(inner);
        }

        /// <summary>下拉项自绘：系统的选中项是深蓝底白字，跟主色冲突，且高度不可控。</summary>
        private void ComboDrawItem(object sender, DrawItemEventArgs e)
        {
            ComboBox cb = (ComboBox)sender;
            e.DrawBackground();
            bool sel = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            using (SolidBrush b = new SolidBrush(sel ? Theme.AccentSoft : Theme.Panel))
                e.Graphics.FillRectangle(b, e.Bounds);
            if (e.Index >= 0 && e.Index < cb.Items.Count)
            {
                using (SolidBrush t = new SolidBrush(sel ? Theme.AccentDark : Theme.Text))
                using (StringFormat sf = new StringFormat())
                {
                    sf.LineAlignment = StringAlignment.Center;
                    e.Graphics.DrawString(Convert.ToString(cb.Items[e.Index]), cb.Font, t,
                        new RectangleF(e.Bounds.X + 6, e.Bounds.Y, e.Bounds.Width - 8, e.Bounds.Height), sf);
                }
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutInner();
        }

        public void LayoutInner()
        {
            if (Inner == null) return;
            int h = Height - InsetY * 2;
            int w = Width - InsetX * 2;
            TextBoxBase tb = Inner as TextBoxBase;
            if (tb != null && tb.Multiline)
            {
                // 多行文本框必须铺满：否则 RichTextBox 只有 22px 高的一条，
                // 被垂直居中之后日志会浮在页面中间（第一版就踩了这个坑）。
                Inner.SetBounds(InsetX, InsetY, Math.Max(10, w), Math.Max(16, h));
                return;
            }
            int ih = Math.Min(h, Math.Max(16, Inner.Height));
            Inner.SetBounds(InsetX, (Height - ih) / 2, Math.Max(10, w), ih);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (SolidBrush b = new SolidBrush(Theme.Panel))
                e.Graphics.FillRectangle(b, ClientRectangle);
            Theme.DrawRound(e.Graphics, new RectangleF(0, 0, Width - 1, Height - 1),
                Theme.RControl, Theme.BorderStrong, 1f);
        }
    }

    /// <summary>
    /// 分段式选项卡。代替系统 TabControl 的凸起标签页：
    /// 每个标签页等宽铺满，选中项白底 + 深蓝字 + 底部 2px 主色，未选中只留灰字。
    /// 仍继承 TabControl，所以 TabPages / SelectedIndex 的既有调用全部不动。
    /// </summary>
    public class UiTabs : TabControl
    {
        public int StripHeight = 30;

        public UiTabs()
        {
            // 注意：TabControl 不能开 UserPaint。开了之后原生标签条与页体都不再绘制，
            // 只剩两枚滚动箭头（本文件第一版就是踩了这个坑）。只保留双缓冲。
            SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
            DrawMode = TabDrawMode.OwnerDrawFixed;
            SizeMode = TabSizeMode.Fixed;
            Appearance = TabAppearance.Normal;
            ItemSize = new Size(72, StripHeight);
            Font = Theme.Font(9f, FontStyle.Regular);
            Padding = new Point(0, 0);
        }

        private bool _resizing;

        private void ResizeTabs()
        {
            if (_resizing) return;
            int n = TabPages.Count;
            if (n <= 0) return;
            // 留 6px 给 TabControl 自己的边框/内边距，否则 n 个标签总宽会超出，
            // 右侧会冒出「◄ ►」滚动箭头，而且最后一个页签被挤到看不见。
            int w = Math.Max(24, (ClientSize.Width - 6) / n);
            if (w == ItemSize.Width && ItemSize.Height == StripHeight) return;
            _resizing = true;
            try { ItemSize = new Size(w, StripHeight); }
            finally { _resizing = false; }
        }

        protected override void OnResize(EventArgs e) { base.OnResize(e); ResizeTabs(); }
        protected override void OnControlAdded(ControlEventArgs e) { base.OnControlAdded(e); ResizeTabs(); }

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            Rectangle r = GetTabRect(e.Index);
            bool sel = e.Index == SelectedIndex;
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            using (SolidBrush b = new SolidBrush(Theme.FormBg))
                g.FillRectangle(b, r);
            if (sel)
            {
                RectangleF body = new RectangleF(r.X, r.Y + 2, r.Width, r.Height);
                using (GraphicsPath p = Theme.RoundRect(new RectangleF(body.X, body.Y, body.Width, body.Height + 8), 7))
                using (SolidBrush b = new SolidBrush(Theme.Panel))
                    g.FillPath(b, p);
                using (SolidBrush b = new SolidBrush(Theme.Accent))
                    g.FillRectangle(b, r.X + 8, r.Bottom - 2, r.Width - 16, 2);
            }
            string text = TabPages[e.Index].Text;
            TextRenderer.DrawText(g, text, Font, r,
                sel ? Theme.AccentDark : Theme.TextDim,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    /// <summary>
    /// 顶部标题栏：品牌标识 + 机型 + 连接状态胶囊。
    /// 不做窗口自绘标题栏——那会连带接管拖动、双击最大化、系统菜单，
    /// 为一个视觉收益去重写窗口非客户区不值得。这里是在原生标题栏下方加一条工具头。
    /// </summary>
    public class UiHeader : Control
    {
        private string _conn = "未连接";
        private bool _open;
        private string _machine = "";

        public UiHeader()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                   | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Height = 46;
            BackColor = Theme.Panel;
        }

        public void SetConn(bool open, string desc)
        {
            _open = open; _conn = desc;
            Invalidate();
        }

        public void SetMachine(string name)
        {
            _machine = name == null ? "" : name;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (SolidBrush b = new SolidBrush(Theme.Panel))
                g.FillRectangle(b, ClientRectangle);
            using (Pen p = new Pen(Theme.Border))
                g.DrawLine(p, 0, Height - 1, Width, Height - 1);

            // 品牌标识
            RectangleF mark = new RectangleF(14, (Height - 26) / 2f, 26, 26);
            Theme.FillRound(g, mark, 7, Theme.Accent);
            Icons.Draw(g, IconKind.Pen, new RectangleF(mark.X + 6, mark.Y + 6, 14, 14), Color.White, 1.7f);

            using (Font f = Theme.Font(10.5f, FontStyle.Bold))
            using (SolidBrush b = new SolidBrush(Theme.Text))
                g.DrawString("InkPen", f, b, 48, (Height - 20) / 2f - 1);
            using (Font f = Theme.Font(8.5f, FontStyle.Regular))
            using (SolidBrush b = new SolidBrush(Theme.TextDim))
                g.DrawString("写字机工作台", f, b, 100, (Height - 16) / 2f);

            float right = Width - 14;
            // 连接胶囊
            using (Font f = Theme.Font(8.5f, FontStyle.Regular))
            {
                string txt = _conn;
                SizeF sz = g.MeasureString(txt, f);
                float w = sz.Width + 30, h = 22;
                RectangleF r = new RectangleF(right - w, (Height - h) / 2f, w, h);
                Theme.FillRound(g, r, 11, _open ? Theme.OkSoft : Theme.CardBg);
                using (SolidBrush b = new SolidBrush(_open ? Theme.Ok : Theme.TextDim))
                    g.FillEllipse(b, r.X + 10, r.Y + h / 2f - 3, 6, 6);
                using (SolidBrush b = new SolidBrush(_open ? Theme.Ok : Theme.TextMid))
                    g.DrawString(txt, f, b, r.X + 21, r.Y + (h - sz.Height) / 2f);
                right = r.X - 8;
            }
            // 机型胶囊
            if (_machine.Length > 0)
            {
                using (Font f = Theme.Font(8.5f, FontStyle.Regular))
                {
                    SizeF sz = g.MeasureString(_machine, f);
                    float w = sz.Width + 16, h = 22;
                    RectangleF r = new RectangleF(right - w, (Height - h) / 2f, w, h);
                    Theme.FillRound(g, r, 5, Theme.CardBg);
                    using (SolidBrush b = new SolidBrush(Theme.TextMid))
                        g.DrawString(_machine, f, b, r.X + 8, r.Y + (h - sz.Height) / 2f);
                }
            }
        }
    }
}
