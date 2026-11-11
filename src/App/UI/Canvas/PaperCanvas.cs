using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;
using InkPen.Core;

namespace InkPen.UI
{
    /// <summary>
    /// 纸张画布：深色工作台上的一张纸。
    /// 分层顺序（底 → 顶）：点阵背景 → 纸张阴影/纸面 → 格线 → 抬笔空程 → 已写/待写笔迹
    ///                     → 当前笔画高亮 → 十字准星 → 笔尖光晕 → HUD / 比例尺。
    ///
    /// 绘制思路参考 GRBL-Plotter GCode2DViewpaths.cs 的 pathPenUp / pathPenDown 分层；
    /// 实时笔位置来自 GRBL 状态报文（WPos），由 PenTracker 换算成纸面坐标后喂进来。
    /// </summary>
    public class PaperCanvas : Control
    {
        private Job _job;
        private double _zoom = 2.2;
        private double _panX = 20, _panY = 20;
        private bool _dragging = false;
        private int _dragSX, _dragSY;
        private double _dragOX, _dragOY;

        public bool ShowGrid = true;
        public bool ShowPenUp = true;
        public int ProgressPath = -1;      // 仿真进度；-1 = 全部画出
        public int TotalPaths = 0;
        public Pt PenPosition = new Pt(0, 0);
        public bool ShowPen = false;
        /// <summary>当前显示的页（0 基）。多页文稿必须按页过滤，否则各页笔迹会叠在同一张纸上。</summary>
        public int PageIndex = 0;
        public int PenPage = -1;
        private bool _needsFit = true;
        /// <summary>用户手动缩放/平移过。设过之后，窗口尺寸变化不再强行重置视图。</summary>
        private bool _viewByUser = false;

        // ---- 实时笔（真机）----
        /// <summary>真机模式：笔迹按 RealtimePath 分层着色，而不是整个画出来。</summary>
        public bool Realtime = false;
        public int RealtimePath = -1;
        public bool PenDownNow = false;
        public bool Follow = false;
        /// <summary>笔尖平滑动画：GRBL 状态约 5Hz，直接跳会让笔一格一格地闪。</summary>
        private readonly Timer _anim = new Timer();
        private PointF _penSmooth;
        private bool _penSmoothInit = false;

        public int PageCount { get { return _job == null || _job.Pages.Count == 0 ? 1 : _job.Pages.Count; } }

        public void SetPage(int idx)
        {
            int max = PageCount - 1;
            PageIndex = Math.Max(0, Math.Min(max, idx));
            Invalidate();
        }

        public PaperCanvas()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                     | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.CanvasBgTop;
            DoubleBuffered = true;
            _anim.Interval = 33;
            _anim.Tick += delegate { AnimTick(); };
        }

        public void SetJob(Job j)
        {
            _job = j;
            TotalPaths = j == null ? 0 : j.Paths.Count;
            ProgressPath = -1;
            RealtimePath = -1;
            ShowPen = false;
            Realtime = false;
            _penSmoothInit = false;
            PageIndex = 0;
            PenPage = -1;
            if (_viewByUser) ClampPan(); else Fit();
            Invalidate();
        }

        /// <summary>真机实时更新：笔的纸面坐标 + 当前笔画 + 页。</summary>
        public void SetRealtime(Pt paper, bool penDown, int currentPath, int pageIndex)
        {
            Realtime = true;
            PenPosition = paper;
            PenDownNow = penDown;
            RealtimePath = currentPath;
            PenPage = pageIndex;
            ShowPen = true;
            if (pageIndex >= 0 && pageIndex < PageCount && pageIndex != PageIndex)
            {
                PageIndex = pageIndex;
                if (Follow) CenterOn(paper);
            }

            if (!_penSmoothInit)
            {
                PointF s = ToScreen(paper);
                _penSmooth = s;
                _penSmoothInit = true;
            }
            if (Follow) EnsureVisible(paper);
            if (!_anim.Enabled) _anim.Start();
            Invalidate();
        }

        public void ClearRealtime()
        {
            Realtime = false;
            RealtimePath = -1;
            ShowPen = false;
            _penSmoothInit = false;
            _anim.Stop();
            Invalidate();
        }

        private void AnimTick()
        {
            if (!ShowPen || !_penSmoothInit) { _anim.Stop(); return; }
            PointF t = ToScreen(PenPosition);
            float k = 0.38f;
            float nx = _penSmooth.X + (t.X - _penSmooth.X) * k;
            float ny = _penSmooth.Y + (t.Y - _penSmooth.Y) * k;
            if (Math.Abs(t.X - nx) < 0.6f && Math.Abs(t.Y - ny) < 0.6f)
            {
                nx = t.X; ny = t.Y;
                _penSmooth = new PointF(nx, ny);
                _anim.Stop();
                Invalidate();
                return;
            }
            _penSmooth = new PointF(nx, ny);
            Invalidate();
        }

        public double Zoom
        {
            get { return _zoom; }
            set
            {
                double nz = Math.Max(0.3, Math.Min(20, value));
                if (Math.Abs(nz - _zoom) < 1e-9) return;
                // 以画布中心为锚点缩放，否则连点几次放大，纸张就跑到画面外了
                double cx = ClientSize.Width / 2.0, cy = ClientSize.Height / 2.0;
                double k = nz / _zoom;
                _panX = cx - (cx - _panX) * k;
                _panY = cy - (cy - _panY) * k;
                _zoom = nz;
                _viewByUser = true;
                _penSmoothInit = false;
                Invalidate();
            }
        }

        public void Fit()
        {
            if (_job == null || _job.Pages.Count == 0) return;
            if (ClientSize.Width < 80 || ClientSize.Height < 80) { _needsFit = true; return; }
            double w = _job.Machine.PaperWidthMm, h = _job.Machine.PaperHeightMm;

            // 上边留得多一些：HUD 信息框画在 (14,14) 起、高 46，纸张从 y=36 开始会被它压住一角。
            // 纸张在「扣掉 HUD 的剩余区域」里居中，两者永不重叠。
            const double padX = 36, padTop = 72, padBot = 26;
            double zx = (ClientSize.Width - padX * 2) / w;
            double zy = (ClientSize.Height - padTop - padBot) / h;
            _zoom = Math.Max(0.3, Math.Min(zx, zy));
            _panX = (ClientSize.Width - w * _zoom) / 2.0;
            _panY = padTop + (ClientSize.Height - padTop - padBot - h * _zoom) / 2.0;
            _needsFit = false;
            _viewByUser = false;
            _penSmoothInit = false;
            Invalidate();
        }

        /// <summary>
        /// 窗口/分栏尺寸变化时必须重新定位。
        /// 原先只在 SetJob 时 Fit 一次，而那时窗体还没完成布局（画布比最终宽），
        /// 算出的 panX 偏大；窗口定下来后纸张就跑到右边、被画布右缘裁掉一半。
        /// </summary>
        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (_job == null || _job.Pages.Count == 0) return;
            if (_viewByUser) { ClampPan(); Invalidate(); }
            else Fit();
        }

        /// <summary>手动视图下至少留住一角在画面内，别让用户把纸拖丢再也找不回来。</summary>
        private void ClampPan()
        {
            if (_job == null || _job.Pages.Count == 0) return;
            const double keep = 60;
            double pw = _job.Machine.PaperWidthMm * _zoom;
            double ph = _job.Machine.PaperHeightMm * _zoom;
            _panX = Math.Max(keep - pw, Math.Min(ClientSize.Width - keep, _panX));
            _panY = Math.Max(keep - ph, Math.Min(ClientSize.Height - keep, _panY));
        }

        private void CenterOn(Pt paper)
        {
            double w = _job.Machine.PaperWidthMm, h = _job.Machine.PaperHeightMm;
            _panX = ClientSize.Width / 2.0 - paper.X * _zoom;
            _panY = ClientSize.Height / 2.0 - (h - paper.Y) * _zoom;
            _viewByUser = true;
            _penSmoothInit = false;
            ClampPan();
        }

        /// <summary>跟随模式：笔接近画布边缘时把视图推过去，而不是每帧硬居中（会晕）。</summary>
        private void EnsureVisible(Pt paper)
        {
            PointF s = ToScreen(paper);
            double mx = ClientSize.Width * 0.22;
            double my = ClientSize.Height * 0.22;
            if (s.X < mx || s.X > ClientSize.Width - mx || s.Y < my || s.Y > ClientSize.Height - my)
                CenterOn(paper);
        }

        private PointF ToScreen(Pt p)
        {
            // 纸面 Y 向上，屏幕 Y 向下
            double h = _job == null ? 297 : _job.Machine.PaperHeightMm;
            return new PointF((float)(_panX + p.X * _zoom),
                              (float)(_panY + (h - p.Y) * _zoom));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            PaintBackdrop(g);

            if (_job == null || _job.Pages.Count == 0)
            {
                using (Font f = Theme.Font(11f, FontStyle.Regular))
                using (Brush b = new SolidBrush(Color.FromArgb(130, 145, 165)))
                using (StringFormat sf = new StringFormat())
                {
                    sf.Alignment = StringAlignment.Center;
                    g.DrawString("左侧输入文稿，点击「生成 GCode / 预览」", f, b,
                                 new RectangleF(0, ClientSize.Height / 2f - 20, ClientSize.Width, 24), sf);
                    using (Font f2 = Theme.Font(8.5f, FontStyle.Regular))
                    using (Brush b2 = new SolidBrush(Color.FromArgb(90, 105, 125)))
                        g.DrawString("滚轮缩放 · 左键拖动平移 · 空格跟随笔尖", f2, b2,
                                     new RectangleF(0, ClientSize.Height / 2f + 4, ClientSize.Width, 20), sf);
                }
                return;
            }

            if (_needsFit && ClientSize.Width > 80 && ClientSize.Height > 80) Fit();

            double w = _job.Machine.PaperWidthMm, h = _job.Machine.PaperHeightMm;
            RectangleF paper = new RectangleF((float)_panX, (float)_panY, (float)(w * _zoom), (float)(h * _zoom));

            PaintPaper(g, paper);

            g.SetClip(paper);

            if (PageIndex >= _job.Pages.Count) PageIndex = _job.Pages.Count - 1;
            if (PageIndex < 0) PageIndex = 0;

            if (ShowGrid)
            {
                using (Pen gp = new Pen(Theme.GridLine, 0.7f))
                    foreach (GridLine gl in _job.Pages[PageIndex].Grid)
                        g.DrawLine(gp, ToScreen(new Pt(gl.X1, gl.Y1)), ToScreen(new Pt(gl.X2, gl.Y2)));
            }

            List<WritePath> paths = _job.Paths;
            int prog = Realtime ? RealtimePath : ProgressPath;

            // 抬笔空程：只在同一页内连，跨页连线会画出一条穿过纸面的斜线
            if (ShowPenUp)
            {
                using (Pen up = new Pen(Theme.Travel, 0.8f))
                {
                    up.DashStyle = DashStyle.Dot;
                    WritePath prev = null;
                    foreach (WritePath wp in paths)
                    {
                        if (wp.PageIndex != PageIndex) continue;
                        if (prev != null && prev.Points.Count > 0 && wp.Points.Count > 0)
                            g.DrawLine(up, ToScreen(prev.Points[prev.Points.Count - 1]),
                                           ToScreen(wp.Points[0]));
                        prev = wp;
                    }
                }
            }

            using (Pen donePen = new Pen(Theme.InkDone, 1.6f))
            using (Pen todoPen = new Pen(Theme.InkTodo, 1.0f))
            using (Pen curPen = new Pen(Theme.Accent, 2.2f))
            {
                donePen.StartCap = donePen.EndCap = LineCap.Round;
                donePen.LineJoin = LineJoin.Round;
                todoPen.StartCap = todoPen.EndCap = LineCap.Round;
                todoPen.LineJoin = LineJoin.Round;
                curPen.StartCap = curPen.EndCap = LineCap.Round;
                curPen.LineJoin = LineJoin.Round;

                for (int i = 0; i < paths.Count; i++)
                {
                    WritePath wp = paths[i];
                    if (wp.PageIndex != PageIndex || wp.Points.Count < 2) continue;

                    Pen pen;
                    if (prog < 0) pen = donePen;
                    else if (i < prog) pen = donePen;
                    else if (i == prog) pen = curPen;
                    else pen = todoPen;

                    PointF[] pts = new PointF[wp.Points.Count];
                    for (int k = 0; k < wp.Points.Count; k++) pts[k] = ToScreen(wp.Points[k]);
                    if (pen == curPen)
                    {
                        // 当前笔画加一层淡光，让人一眼看到「机器正在写这一笔」
                        using (Pen halo = new Pen(Color.FromArgb(70, Theme.Accent), 5.5f))
                        {
                            halo.StartCap = halo.EndCap = LineCap.Round;
                            halo.LineJoin = LineJoin.Round;
                            g.DrawLines(halo, pts);
                        }
                    }
                    g.DrawLines(pen, pts);
                }
            }

            g.ResetClip();

            bool penOnThisPage = ShowPen && (PenPage < 0 || PenPage == PageIndex);
            if (penOnThisPage)
            {
                PointF c = _penSmoothInit ? _penSmooth : ToScreen(PenPosition);
                PaintPen(g, c, paper);
            }

            PaintHud(g, paper);
            PaintScaleBar(g);
        }

        private void PaintBackdrop(Graphics g)
        {
            Rectangle r = ClientRectangle;
            if (r.Width <= 0 || r.Height <= 0) return;
            using (LinearGradientBrush b = new LinearGradientBrush(
                       new Rectangle(0, 0, Math.Max(1, r.Width), Math.Max(1, r.Height)),
                       Theme.CanvasBgTop, Theme.CanvasBgBottom, 90f))
                g.FillRectangle(b, r);

            // 点阵：给深色底一点「工程台面」的质感，也让纸张边界更明确。
            // 图案是静态的，用 26×26 的贴图 + TextureBrush 一次填满，别再每帧画上千个小方块。
            TextureBrush dot = DotBrush();
            if (dot != null) g.FillRectangle(dot, r);
        }

        // 静态点阵贴图（进程级复用；只在首次绘制时创建一次）
        private static TextureBrush _dotBrush;

        private static TextureBrush DotBrush()
        {
            if (_dotBrush != null) return _dotBrush;
            try
            {
                Bitmap tile = new Bitmap(26, 26);
                using (Graphics tg = Graphics.FromImage(tile))
                {
                    tg.Clear(Color.Transparent);
                    tg.SmoothingMode = SmoothingMode.AntiAlias;
                    using (SolidBrush b = new SolidBrush(Theme.CanvasDot))
                        tg.FillRectangle(b, 12f, 12f, 1.6f, 1.6f);
                }
                TextureBrush tb = new TextureBrush(tile, WrapMode.Tile);
                _dotBrush = tb;
                return tb;
            }
            catch
            {
                return null;   // 贴图建不出来就不画点阵，背景已由渐变铺满
            }
        }

        private void PaintPaper(Graphics g, RectangleF paper)
        {
            // 三层阴影模拟纸张离开台面的高度
            for (int i = 3; i >= 1; i--)
            {
                int a = 26 - i * 5;
                using (SolidBrush sb = new SolidBrush(Color.FromArgb(a, 0, 0, 0)))
                    g.FillRectangle(sb, paper.X + i * 2.2f, paper.Y + i * 2.6f, paper.Width, paper.Height);
            }
            using (SolidBrush paperFill = new SolidBrush(Theme.Paper))
                g.FillRectangle(paperFill, paper);
            using (Pen edge = new Pen(Color.FromArgb(60, 120, 132, 150), 1f))
                g.DrawRectangle(edge, paper.X, paper.Y, paper.Width, paper.Height);

            // 纸面极淡的对角纹理，避免大片纯白显得空
            using (LinearGradientBrush sheen = new LinearGradientBrush(
                       Rectangle.Round(paper), Color.FromArgb(16, 255, 255, 255),
                       Color.FromArgb(0, 255, 255, 255), 45f))
                g.FillRectangle(sheen, paper);
        }

        private void PaintPen(Graphics g, PointF c, RectangleF paper)
        {
            Color col = PenDownNow ? Theme.Accent : Theme.Warm;

            // 十字准星只在纸内画，避免溢出到台面上显得脏
            RectangleF clip = paper;
            g.SetClip(clip);
            using (Pen cross = new Pen(Color.FromArgb(46, col), 1f))
            {
                cross.DashStyle = DashStyle.Dash;
                g.DrawLine(cross, paper.X, c.Y, paper.Right, c.Y);
                g.DrawLine(cross, c.X, paper.Y, c.X, paper.Bottom);
            }
            g.ResetClip();

            Theme.Glow(g, c, 18f, Color.FromArgb(120, col), 4);
            using (SolidBrush b = new SolidBrush(col))
                g.FillEllipse(b, c.X - 4.5f, c.Y - 4.5f, 9f, 9f);
            using (Pen ring = new Pen(Color.FromArgb(210, 255, 255, 255), 1.4f))
                g.DrawEllipse(ring, c.X - 7.5f, c.Y - 7.5f, 15f, 15f);
            using (Pen ring2 = new Pen(Color.FromArgb(90, col), 1f))
                g.DrawEllipse(ring2, c.X - 12.5f, c.Y - 12.5f, 25f, 25f);
        }

        private void PaintHud(Graphics g, RectangleF paper)
        {
            string line1 = "第 " + (PageIndex + 1) + " / " + _job.Pages.Count + " 页"
                         + "   ·   笔画 " + _job.Paths.Count
                         + "   ·   GCode " + _job.GCode.Count + " 行";
            string line2 = "缩放 " + _zoom.ToString("0.0", CultureInfo.InvariantCulture) + "×"
                         + (Realtime ? "   ·   实时跟随" : "   ·   预览")
                         + (Realtime && RealtimePath >= 0
                             ? "   ·   第 " + (RealtimePath + 1) + " 笔" : "");

            float w = 250, h = 46;
            using (Font f1 = Theme.Font(8.8f, FontStyle.Bold))
            using (Font f2 = Theme.Font(7.8f, FontStyle.Regular))
            {
                // 宽度按最长一行实测：写死 250 时「GCode 12000 行」会溢出到框外。
                // 上限再压到画布宽度的 62%，给右上角的比例尺留出位置，两者不打架。
                float need = Math.Max(g.MeasureString(line1, f1).Width,
                                      g.MeasureString(line2, f2).Width) + 32;
                float cap = Math.Min(ClientSize.Width - 40,
                                     Math.Max(220, ClientSize.Width * 0.62f));
                w = Math.Max(220, Math.Min(cap, need));
                RectangleF box = new RectangleF(14, 14, w, h);
                using (SolidBrush bg = new SolidBrush(Color.FromArgb(150, 12, 17, 26)))
                    using (GraphicsPath p = Theme.RoundRect(box, 9))
                        g.FillPath(bg, p);
                Theme.DrawRound(g, box, 9, Color.FromArgb(60, Theme.Accent), 1f);

                using (SolidBrush accentBar = new SolidBrush(Theme.Accent))
                    using (GraphicsPath p = Theme.RoundRect(new RectangleF(box.X + 10, box.Y + 12, 3, 22), 1.5f))
                        g.FillPath(accentBar, p);

                using (Brush b1 = new SolidBrush(Color.FromArgb(232, 240, 250)))
                    g.DrawString(line1, f1, b1, box.X + 20, box.Y + 7);
                using (Brush b2 = new SolidBrush(Color.FromArgb(140, 158, 180)))
                    g.DrawString(line2, f2, b2, box.X + 20, box.Y + 25);
            }
        }

        /// <summary>右下角比例尺：画出 10mm 的实际长度，缩放后仍有参照。</summary>
        private void PaintScaleBar(Graphics g)
        {
            double mm = 10;
            // 太长就退到 5mm / 1mm，保证比例尺不会横穿整个画布
            while (mm * _zoom > 160 && mm > 1) mm /= 2;
            double px = mm * _zoom;
            if (px < 8) return;

            float x = ClientSize.Width - (float)px - 22;
            // 放右上角：纸张现在从 y=72 起铺满到画布底，比例尺原来画在 Height-26 会压在纸上。
            // 右上角是空的（HUD 在左上，宽度有上限），两块信息互不遮挡。
            float y = 32;
            if (x < 8) return;

            using (Pen p = new Pen(Color.FromArgb(150, 205, 218, 232), 1.4f))
            {
                g.DrawLine(p, x, y, x + (float)px, y);
                g.DrawLine(p, x, y - 4, x, y + 4);
                g.DrawLine(p, x + (float)px, y - 4, x + (float)px, y + 4);
            }
            using (Font f = Theme.Font(7.5f, FontStyle.Regular))
            using (Brush b = new SolidBrush(Color.FromArgb(140, 170, 190, 212)))
            {
                string t = mm.ToString("0.#", CultureInfo.InvariantCulture) + " mm";
                SizeF sz = g.MeasureString(t, f);
                g.DrawString(t, f, b, x + (float)px - sz.Width, y - 18);
            }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            double f = e.Delta > 0 ? 1.15 : 1.0 / 1.15;
            double nx = (e.X - _panX) / _zoom, ny = (e.Y - _panY) / _zoom;
            _zoom = Math.Max(0.3, Math.Min(20, _zoom * f));
            _panX = e.X - nx * _zoom;
            _panY = e.Y - ny * _zoom;
            _viewByUser = true;
            _penSmoothInit = false;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left)
            {
                _dragging = true; _dragSX = e.X; _dragSY = e.Y;
                _dragOX = _panX; _dragOY = _panY;
                Cursor = Cursors.Hand;
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_dragging)
            {
                _panX = _dragOX + (e.X - _dragSX);
                _panY = _dragOY + (e.Y - _dragSY);
                _viewByUser = true;
                Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _dragging = false;
            Cursor = Cursors.Default;
        }
    }

    /// <summary>仿真回放：按笔画推进 ProgressPath，可在任意速度/单步下运行。</summary>
    public class SimulationPlayer
    {
        private readonly Timer _timer = new Timer();
        private readonly PaperCanvas _canvas;
        private Job _job;
        public Action<int, int> OnProgress;
        public Action OnFinished;
        public bool IsPlaying { get; private set; }

        public SimulationPlayer(PaperCanvas canvas)
        {
            _canvas = canvas;
            _timer.Interval = 40;
            _timer.Tick += Tick;
        }

        public void SetJob(Job j) { _job = j; Stop(); }

        public void Start(int pathsPerSecond)
        {
            if (_job == null || _job.Paths.Count == 0) return;
            _canvas.Realtime = false;
            _timer.Interval = Math.Max(10, Math.Min(1000, 1000 / Math.Max(1, pathsPerSecond / 3)));
            IsPlaying = true;
            _timer.Start();
        }

        private void Tick(object sender, EventArgs e)
        {
            if (_job == null) { Stop(); return; }
            // 从 -1 起播时应从第 0 笔开始（原先 +2 会跳过首笔）
            int next = _canvas.ProgressPath < 0 ? 0 : _canvas.ProgressPath + 1;
            if (next >= _job.Paths.Count)
            {
                _canvas.ProgressPath = _job.Paths.Count - 1;
                _canvas.ShowPen = false;
                _canvas.Invalidate();
                Stop();
                if (OnFinished != null) OnFinished();
                return;
            }
            _canvas.ProgressPath = next;
            WritePath wp = _job.Paths[next];
            _canvas.PageIndex = wp.PageIndex;      // 跨页时自动翻页
            _canvas.PenPage = wp.PageIndex;
            if (wp.Points.Count > 0)
            {
                _canvas.PenPosition = wp.Points[wp.Points.Count - 1];
                _canvas.ShowPen = true;
            }
            _canvas.Invalidate();
            if (OnProgress != null) OnProgress(next, _job.Paths.Count);
        }

        public void Step()
        {
            if (_job == null || _job.Paths.Count == 0) return;
            int next = _canvas.ProgressPath < 0 ? 0 : Math.Min(_job.Paths.Count - 1, _canvas.ProgressPath + 1);
            _canvas.ProgressPath = next;
            WritePath wp = _job.Paths[next];
            _canvas.PageIndex = wp.PageIndex;
            _canvas.PenPage = wp.PageIndex;
            if (wp.Points.Count > 0)
            {
                _canvas.PenPosition = wp.Points[wp.Points.Count - 1];
                _canvas.ShowPen = true;
            }
            _canvas.Invalidate();
            if (OnProgress != null) OnProgress(next, _job.Paths.Count);
        }

        public void Pause() { _timer.Stop(); IsPlaying = false; }

        public void Stop()
        {
            _timer.Stop(); IsPlaying = false;
            _canvas.ProgressPath = -1;
            _canvas.ShowPen = false;
            _canvas.Invalidate();
        }
    }
}
