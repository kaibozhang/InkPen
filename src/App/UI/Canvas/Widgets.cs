using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;
using InkPen.Core;

namespace InkPen.UI
{
    /// <summary>
    /// 中栏底部状态条：圆环进度 + 作业文案 + 实时坐标胶囊。
    /// 全部自绘，一是不用为深色底去逐个改系统控件，二是 X/Y/Z 能做成等宽大字号，
    /// 一眼能看清笔在哪（GRBL-Plotter 的 DRO 也是这个思路）。
    /// </summary>
    public class JobStatusBar : Control
    {
        public double Progress = 0, Total = 0;
        public string StateText = "就绪";
        public string SubText = "";
        public bool Error = false;
        public bool Connected = false;
        public bool HasPos = false;
        public double PosX, PosY, PosZ;
        public bool PenDown = false;
        public string GrblState = "";
        public int BufferFree = -1;
        public int Override = 100;

        public JobStatusBar()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                     | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.CanvasBgTop;
            Height = 78;
        }

        public void SetPos(double x, double y, double z, bool has)
        {
            PosX = x; PosY = y; PosZ = z; HasPos = has;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle r = ClientRectangle;
            using (SolidBrush bg = new SolidBrush(Color.FromArgb(20, 26, 36)))
                g.FillRectangle(bg, r);
            using (Pen top = new Pen(Color.FromArgb(48, 58, 74), 1f))
                g.DrawLine(top, 0, 0, r.Width, 0);

            // ---- 圆环进度 ----
            float d = 54f;
            RectangleF ring = new RectangleF(16, (r.Height - d) / 2f, d, d);
            Color ringCol = Error ? Theme.Danger : Theme.Accent;
            double pct = Total > 0 ? Math.Max(0, Math.Min(1, Progress / Total)) : 0;

            using (Pen track = new Pen(Color.FromArgb(46, 56, 72), 5f))
                g.DrawEllipse(track, ring);
            if (pct > 0.0005)
            {
                using (Pen arc = new Pen(ringCol, 5f))
                {
                    arc.StartCap = LineCap.Round;
                    arc.EndCap = LineCap.Round;
                    g.DrawArc(arc, ring, -90f, (float)(360.0 * pct));
                }
            }
            string ptxt = (pct * 100).ToString("0", CultureInfo.InvariantCulture) + "%";
            using (Font f = Theme.Mono(9.5f, FontStyle.Bold))
            using (Brush b = new SolidBrush(Error ? Theme.Danger : Color.FromArgb(226, 236, 248)))
            {
                SizeF sz = g.MeasureString(ptxt, f);
                g.DrawString(ptxt, f, b, ring.X + (ring.Width - sz.Width) / 2,
                             ring.Y + (ring.Height - sz.Height) / 2);
            }

            // ---- 文案 ----
            float tx = 84;
            using (Font f1 = Theme.Font(9.2f, FontStyle.Bold))
            using (Brush b1 = new SolidBrush(Error ? Theme.Danger : Color.FromArgb(228, 236, 247)))
                g.DrawString(Trim(StateText, 46), f1, b1, tx, 16);
            using (Font f2 = Theme.Font(8f, FontStyle.Regular))
            using (Brush b2 = new SolidBrush(Color.FromArgb(138, 154, 176)))
                g.DrawString(Trim(SubText, 60), f2, b2, tx, 38);

            // ---- 坐标胶囊 ----
            // 宽度自适应：先保住「3 个胶囊 + 状态徽标」，缩胶囊；实在放不下才丢徽标，
            // 最后连胶囊都不画。否则 3 个 96px 的胶囊会顶出右边界，看起来像界面被裁了一半。
            float gap = 8;
            float chipW = 96, chipH = 46;
            float badgeW = 104;
            float textEnd = tx + 300;
            float avail = r.Width - 16 - textEnd;
            if (avail >= 58 * 3 + gap * 2)
            {
                float withBadge = avail - gap * 2 - gap - badgeW;
                if (withBadge / 3f >= 58f)
                {
                    chipW = Math.Min(96f, withBadge / 3f);
                }
                else
                {
                    chipW = Math.Min(96f, (avail - gap * 2) / 3f);
                    badgeW = 0;
                }

                float blockW = chipW * 3 + gap * 2 + (badgeW > 0 ? gap + badgeW : 0);
                float cx = r.Width - 16 - blockW;
                float cy = (r.Height - chipH) / 2f;

                DrawChip(g, cx, cy, chipW, chipH, "X", PosX);
                DrawChip(g, cx + chipW + gap, cy, chipW, chipH, "Y", PosY);
                DrawChip(g, cx + (chipW + gap) * 2, cy, chipW, chipH, "Z", PosZ);

                // 状态徽标：颜色直接对应机器状态，不用读文字也能判断
                if (badgeW > 0)
                {
                    RectangleF badge = new RectangleF(cx + (chipW + gap) * 3 + gap, cy, badgeW, chipH);
                    Color bc = StateColor();
                    Theme.FillRound(g, badge, 8, Color.FromArgb(28, bc));
                    Theme.DrawRound(g, badge, 8, Color.FromArgb(120, bc), 1f);
                    using (Font f = Theme.Font(8.6f, FontStyle.Bold))
                    using (Brush b = new SolidBrush(bc))
                    using (StringFormat sf = new StringFormat())
                    {
                        sf.Alignment = StringAlignment.Center;
                        sf.LineAlignment = StringAlignment.Center;
                        g.DrawString(Connected ? Label() : "未连接", f, b, badge, sf);
                    }
                    if (Connected)
                    {
                        using (Font f = Theme.Mono(7.2f, FontStyle.Regular))
                        using (Brush b = new SolidBrush(Color.FromArgb(130, 148, 170)))
                        using (StringFormat sf = new StringFormat())
                        {
                            sf.Alignment = StringAlignment.Center;
                            g.DrawString("Bf " + BufferFree + "  Ov " + Override + "%", f, b,
                                         new RectangleF(badge.X, badge.Bottom + 1, badge.Width, 12), sf);
                        }
                    }
                }
            }
        }

        private string Trim(string s, int n)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Length <= n ? s : s.Substring(0, n - 1) + "…";
        }

        private Color StateColor()
        {
            if (!Connected) return Color.FromArgb(140, 150, 165);
            if (Error) return Theme.Danger;
            if (GrblState == "Run" || GrblState == "Jog") return Theme.Accent;
            if (GrblState == "Hold") return Theme.Warm;
            if (GrblState == "Alarm") return Theme.Danger;
            if (GrblState == "Idle") return Theme.Ok;
            return Color.FromArgb(150, 165, 185);
        }

        private string Label()
        {
            if (GrblState == "Run") return "书写中";
            if (GrblState == "Hold") return "已暂停";
            if (GrblState == "Jog") return "Jog";
            if (GrblState == "Alarm") return "ALARM";
            if (GrblState == "Idle") return "待机";
            if (GrblState == "Home") return "回零";
            if (GrblState == "Door") return "门开";
            return "已连接";
        }

        private void DrawChip(Graphics g, float x, float y, float w, float h, string axis, double v)
        {
            RectangleF box = new RectangleF(x, y, w, h);
            Theme.FillRound(g, box, 8, Color.FromArgb(30, 38, 50));
            Theme.DrawRound(g, box, 8, Color.FromArgb(52, 64, 82), 1f);

            using (Font fa = Theme.Font(7.5f, FontStyle.Bold))
            using (Brush ba = new SolidBrush(Color.FromArgb(120, Theme.Accent)))
                g.DrawString(axis, fa, ba, box.X + 8, box.Y + 5);

            string txt = HasPos ? v.ToString("0.00", CultureInfo.InvariantCulture) : "—";
            using (Font fv = Theme.Mono(13f, FontStyle.Bold))
            using (Brush bv = new SolidBrush(Color.FromArgb(232, 240, 250)))
                g.DrawString(txt, fv, bv, box.X + 8, box.Y + 18);
        }
    }

    /// <summary>
    /// 高度图热力图：把 3×3 ~ 16×16 的探测网格画成色块。
    /// 冷暖色映射（蓝=低 白=基准 红=高），比表格更容易看出纸是不是翘的。
    /// </summary>
    public class HeatmapView : Control
    {
        private HeightMap _map;

        public HeatmapView()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                     | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Color.White;
        }

        public void SetMap(HeightMap m)
        {
            _map = m;
            Invalidate();
        }

        private static Color Ramp(double t)
        {
            // t ∈ [-1,1]，-1 蓝 → 0 近白 → +1 红
            if (t < 0)
            {
                double k = -t;
                return Blend(Color.FromArgb(238, 242, 248), Color.FromArgb(64, 126, 208), k);
            }
            return Blend(Color.FromArgb(238, 242, 248), Color.FromArgb(226, 96, 74), t);
        }

        private static Color Blend(Color a, Color b, double k)
        {
            if (k < 0) k = 0; else if (k > 1) k = 1;
            return Color.FromArgb(
                (int)(a.R + (b.R - a.R) * k),
                (int)(a.G + (b.G - a.G) * k),
                (int)(a.B + (b.B - a.B) * k));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);

            Rectangle pad = Rectangle.Inflate(ClientRectangle, -12, -12);
            if (pad.Width < 60 || pad.Height < 60) return;

            if (_map == null || !_map.IsUsable)
            {
                using (Font f = Theme.Font(9f, FontStyle.Regular))
                using (Brush b = new SolidBrush(Theme.TextDim))
                using (StringFormat sf = new StringFormat())
                {
                    sf.Alignment = StringAlignment.Center;
                    sf.LineAlignment = StringAlignment.Center;
                    g.DrawString("尚无探测数据\n点「开始探测」或读取已保存的高度图",
                                 f, b, ClientRectangle, sf);
                }
                return;
            }

            // 底部留一条图例；其余全部给色块。
            // 不再按纸张长宽比缩格子——那是给「看纸」用的，热力图要的是把数据摊开看清。
            RectangleF legend = new RectangleF(pad.X, pad.Bottom - 22, pad.Width, 22);
            RectangleF grid = new RectangleF(pad.X, pad.Y, pad.Width, pad.Height - 26);

            double absMax = Math.Max(Math.Abs(_map.MinZ()), Math.Abs(_map.MaxZ()));
            if (absMax < 1e-6) absMax = 0.001;

            float cw = grid.Width / (_map.Cols - 1);
            float ch = grid.Height / (_map.Rows - 1);

            for (int r = 0; r < _map.Rows - 1; r++)
            {
                for (int c = 0; c < _map.Cols - 1; c++)
                {
                    // 单元格颜色取四角均值，行 0 在底部（纸面 Y 向上）
                    double v = (_map.Get(c, r) + _map.Get(c + 1, r)
                              + _map.Get(c, r + 1) + _map.Get(c + 1, r + 1)) / 4.0;
                    RectangleF cell = new RectangleF(grid.X + c * cw,
                                                     grid.Bottom - (r + 1) * ch, cw, ch);
                    using (SolidBrush b = new SolidBrush(Ramp(v / absMax)))
                        g.FillRectangle(b, cell);
                }
            }

            using (Pen gp = new Pen(Color.FromArgb(70, 90, 105, 125), 1f))
            {
                for (int c = 0; c < _map.Cols; c++)
                    g.DrawLine(gp, grid.X + c * cw, grid.Y, grid.X + c * cw, grid.Bottom);
                for (int r = 0; r < _map.Rows; r++)
                    g.DrawLine(gp, grid.X, grid.Bottom - r * ch, grid.Right, grid.Bottom - r * ch);
            }

            // 数值只在格子放得下时标注，否则每个点都写字会糊成一团
            bool showNum = cw >= 34 && ch >= 22;
            for (int r = 0; r < _map.Rows; r++)
            {
                for (int c = 0; c < _map.Cols; c++)
                {
                    PointF p = new PointF(grid.X + c * cw, grid.Bottom - r * ch);
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(200, 245, 248, 252)))
                        g.FillEllipse(b, p.X - 2.4f, p.Y - 2.4f, 4.8f, 4.8f);
                    using (Pen pen = new Pen(Color.FromArgb(150, 60, 80, 105), 1f))
                        g.DrawEllipse(pen, p.X - 2.4f, p.Y - 2.4f, 4.8f, 4.8f);

                    if (!showNum) continue;
                    string t = _map.Get(c, r).ToString("+0.00;-0.00", CultureInfo.InvariantCulture);
                    // 靠边的点把标注往内推，避免压到相邻格子或越出边框
                    float tx = c == 0 ? p.X + 5 : (c == _map.Cols - 1 ? p.X - 34 : p.X + 4);
                    float ty = r == _map.Rows - 1 ? p.Y + 4 : p.Y - 12;
                    using (Font f = Theme.Mono(6.8f, FontStyle.Regular))
                    using (Brush b = new SolidBrush(Color.FromArgb(190, 55, 70, 90)))
                        g.DrawString(t, f, b, tx, ty);
                }
            }

            using (Pen border = new Pen(Color.FromArgb(150, 120, 134, 155), 1f))
                g.DrawRectangle(border, grid.X, grid.Y, grid.Width, grid.Height);

            // 横向图例：左低右高，下沿标数值
            float barW = Math.Min(150, legend.Width - 4);
            using (LinearGradientBrush lb = new LinearGradientBrush(
                       new RectangleF(legend.X, legend.Y, barW, 9),
                       Ramp(-1), Ramp(1), LinearGradientMode.Horizontal))
                g.FillRectangle(lb, legend.X, legend.Y + 2, barW, 9);
            using (Pen edge = new Pen(Color.FromArgb(120, 130, 145), 1f))
                g.DrawRectangle(edge, legend.X, legend.Y + 2, barW, 9);

            using (Font f = Theme.Mono(6.8f, FontStyle.Regular))
            using (Brush b = new SolidBrush(Theme.TextDim))
            {
                g.DrawString("-" + absMax.ToString("0.00", CultureInfo.InvariantCulture), f, b,
                             legend.X, legend.Bottom - 10);
                string hi = "+" + absMax.ToString("0.00", CultureInfo.InvariantCulture) + " mm";
                SizeF hs = g.MeasureString(hi, f);
                g.DrawString(hi, f, b, legend.X + barW - hs.Width, legend.Bottom - 10);
            }
        }
    }
}
