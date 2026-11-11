using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using InkPen.Core;

namespace InkPen.UI
{
    /// <summary>纸张画布：双缓冲绘制笔迹、抬笔空程、格线、笔位置与仿真进度。
    /// 绘制思路参考 GRBL-Plotter GCode2DViewpaths.cs 的 pathPenUp / pathPenDown 分层。</summary>
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
        public int ProgressPath = -1;      // 已写到的路径序号；-1 = 全部
        public int TotalPaths = 0;
        public Pt PenPosition = new Pt(0, 0);
        public bool ShowPen = false;
        /// <summary>当前显示的页（0 基）。多页文稿必须按页过滤，否则各页笔迹会叠在同一张纸上。</summary>
        public int PageIndex = 0;
        public int PenPage = -1;
        private bool _needsFit = true;
        /// <summary>用户手动缩放/平移过。设过之后，窗口尺寸变化不再强行重置视图。</summary>
        private bool _viewByUser = false;

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
            BackColor = Color.FromArgb(240, 242, 246);
        }

        public void SetJob(Job j)
        {
            _job = j;
            TotalPaths = j == null ? 0 : j.Paths.Count;
            ProgressPath = -1;
            ShowPen = false;
            PageIndex = 0;
            PenPage = -1;
            if (_viewByUser) ClampPan(); else Fit();
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
                Invalidate();
            }
        }

        public void Fit()
        {
            if (_job == null || _job.Pages.Count == 0) return;
            if (ClientSize.Width < 80 || ClientSize.Height < 80) { _needsFit = true; return; }
            double w = _job.Machine.PaperWidthMm, h = _job.Machine.PaperHeightMm;
            double zx = (ClientSize.Width - 60) / w, zy = (ClientSize.Height - 60) / h;
            _zoom = Math.Max(0.3, Math.Min(zx, zy));
            _panX = (ClientSize.Width - w * _zoom) / 2.0;
            _panY = (ClientSize.Height - h * _zoom) / 2.0;
            _needsFit = false;
            _viewByUser = false;
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
            g.Clear(BackColor);
            if (_job == null || _job.Pages.Count == 0)
            {
                using (Font f = new Font("Microsoft YaHei", 12f))
                using (Brush b = new SolidBrush(Color.FromArgb(120, 128, 140)))
                    g.DrawString("左侧输入文稿，点击「生成 GCode」后在此预览", f, b, 24, 24);
                return;
            }

            if (_needsFit && ClientSize.Width > 80 && ClientSize.Height > 80) Fit();

            double w = _job.Machine.PaperWidthMm, h = _job.Machine.PaperHeightMm;
            RectangleF paper = new RectangleF(ToScreen(new Pt(0, h)).X, ToScreen(new Pt(0, h)).Y,
                                              (float)(w * _zoom), (float)(h * _zoom));

            using (SolidBrush shadow = new SolidBrush(Color.FromArgb(30, 20, 30, 50)))
                g.FillRectangle(shadow, paper.X + 5, paper.Y + 5, paper.Width, paper.Height);
            g.FillRectangle(Brushes.White, paper);
            using (Pen border = new Pen(Color.FromArgb(190, 198, 210), 1f))
                g.DrawRectangle(border, paper.X, paper.Y, paper.Width, paper.Height);

            g.SetClip(paper);

            if (PageIndex >= _job.Pages.Count) PageIndex = _job.Pages.Count - 1;
            if (PageIndex < 0) PageIndex = 0;

            if (ShowGrid)
            {
                using (Pen gp = new Pen(Color.FromArgb(60, 120, 170, 220), 0.7f))
                    foreach (GridLine gl in _job.Pages[PageIndex].Grid)
                        g.DrawLine(gp, ToScreen(new Pt(gl.X1, gl.Y1)), ToScreen(new Pt(gl.X2, gl.Y2)));
            }

            List<WritePath> paths = _job.Paths;

            // 抬笔空程：只在同一页内连，跨页连线会画出一条穿过纸面的斜线
            if (ShowPenUp)
            {
                using (Pen up = new Pen(Color.FromArgb(70, 160, 170, 180), 0.7f))
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

            using (Pen donePen = new Pen(Color.FromArgb(28, 34, 44), 1.5f))
            using (Pen todoPen = new Pen(Color.FromArgb(90, 190, 200, 205), 1.0f))
            {
                donePen.StartCap = donePen.EndCap = LineCap.Round;
                donePen.LineJoin = LineJoin.Round;
                for (int i = 0; i < paths.Count; i++)
                {
                    WritePath wp = paths[i];
                    if (wp.PageIndex != PageIndex || wp.Points.Count < 2) continue;
                    bool isDone = ProgressPath < 0 || i <= ProgressPath;
                    PointF[] pts = new PointF[wp.Points.Count];
                    for (int k = 0; k < wp.Points.Count; k++) pts[k] = ToScreen(wp.Points[k]);
                    g.DrawLines(isDone ? donePen : todoPen, pts);
                }
            }

            if (ShowPen && PenPage == PageIndex)
            {
                PointF c = ToScreen(PenPosition);
                using (SolidBrush b = new SolidBrush(Color.FromArgb(220, 60, 60)))
                    g.FillEllipse(b, c.X - 4, c.Y - 4, 8, 8);
            }
            g.ResetClip();

            string info = "第 " + (PageIndex + 1) + " / " + _job.Pages.Count + " 页"
                        + " · 笔画 " + paths.Count
                        + " · GCode " + _job.GCode.Count + " 行 · 缩放 " + _zoom.ToString("0.0") + "×";
            using (Font f = new Font("Microsoft YaHei", 9f))
            using (SolidBrush b = new SolidBrush(Color.FromArgb(100, 110, 125)))
                g.DrawString(info, f, b, 10, ClientSize.Height - 24);
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
