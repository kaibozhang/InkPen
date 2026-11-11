using System;
using System.Collections.Generic;

namespace InkPen.Core
{
    /// <summary>
    /// 实时笔位置跟踪：把 GRBL 状态报文里的坐标换算成纸面 mm，并按已发送的 GCode 行号
    /// 推算「当前写到第几笔」。
    ///
    /// 两个坐标系的坑必须说清楚：
    ///  · GRBL 的 MPos 是机床绝对坐标（回零后原点在机床角上），WPos 才是加了工件偏移的坐标。
    ///    GCode 是在「工件坐标」里发的，所以画布只能跟着 WPos 走；只收到 MPos 时才退回 MPos
    ///    （此时说明用户没设 G10 偏移，两者等价）。
    ///  · 纸面坐标 Y 向上、机器坐标 Y 方向取决于原点角与 InvertY，必须走 CoordTransform.ToPaper，
    ///    不能直接把 MPos 当纸面坐标画——那就是之前整包字倒置同一类错误的变种。
    /// </summary>
    public class PenTracker
    {
        private Job _job;
        private int _pathCursor = 0;
        private int _lastUpper = -1;
        private bool _sawWPos = false;

        /// <summary>笔尖纸面坐标（mm）。</summary>
        public Pt Paper = new Pt(0, 0);

        /// <summary>是否已经收到过有效坐标（未连接 / 未上报时为 false）。</summary>
        public bool HasPosition = false;

        /// <summary>当前是否处于落笔状态（由最后一条已发送的 GCode 行推断）。</summary>
        public bool PenDown = false;

        /// <summary>正在写的笔画下标；-1 表示还没开始。</summary>
        public int CurrentPath = -1;

        /// <summary>正在写的页；-1 表示未知。</summary>
        public int PageIndex = -1;

        public int LastSentLine = -1;

        public int PathCount { get { return _job == null ? 0 : _job.Paths.Count; } }

        public void SetJob(Job j)
        {
            _job = j;
            Reset();
        }

        public void Reset()
        {
            _pathCursor = 0;
            _lastUpper = -1;
            CurrentPath = -1;
            PageIndex = -1;
            PenDown = false;
            HasPosition = false;
            LastSentLine = -1;
        }

        /// <summary>喂一条 GRBL 状态。优先 WPos，缺失时退回 MPos。</summary>
        public void SetStatus(GrblStatus st)
        {
            if (st == null || _job == null) return;

            double x, y;
            if (st.Raw != null && st.Raw.IndexOf("WPos:", StringComparison.Ordinal) >= 0) _sawWPos = true;
            if (_sawWPos) { x = st.WX; y = st.WY; }
            else { x = st.MX; y = st.MY; }

            // 有些固件在未回零时全报 0；全 0 且连接正常也可能是真的原点，不做丢弃，
            // 只是标记有位置，让画布能画出「笔在原点」的真实情况。
            Paper = CoordTransform.ToPaper(new Pt(x, y), _job.Machine);
            HasPosition = true;
        }

        /// <summary>
        /// 喂「已发送多少行 GCode」（不含因空行/注释被跳过的部分，按 job.GCode 下标语义传总数）。
        /// 用增量游标推进，避免每次 O(n) 全扫。
        /// </summary>
        public void SetSentLines(int sentCount)
        {
            if (_job == null || _job.Paths.Count == 0) { CurrentPath = -1; return; }

            int upper = Math.Min(sentCount, _job.GCode.Count) - 1;
            if (upper < 0) { CurrentPath = -1; PenDown = false; return; }

            // 换作业 / 续写回退时重新定位，不能沿用旧游标
            if (upper < _lastUpper) _pathCursor = 0;
            _lastUpper = upper;
            if (_pathCursor >= _job.Paths.Count) _pathCursor = _job.Paths.Count - 1;

            while (_pathCursor + 1 < _job.Paths.Count
                   && _job.Paths[_pathCursor + 1].FirstGCodeLine <= upper)
                _pathCursor++;

            CurrentPath = _job.Paths[0].FirstGCodeLine <= upper ? _pathCursor : -1;
            if (CurrentPath >= 0) PageIndex = _job.Paths[CurrentPath].PageIndex;
            LastSentLine = upper;
            PenDown = _job.GCode[upper].IsPenDown;
        }

        /// <summary>已写完的笔画数（用于进度显示）。</summary>
        public int CompletedPaths
        {
            get
            {
                if (CurrentPath < 0) return 0;
                return PenDown ? CurrentPath : CurrentPath + 1;
            }
        }

        /// <summary>笔尖到当前笔画起点的距离（mm），用于判断「是否真的落在线上」。</summary>
        public double DistanceToCurrentStart()
        {
            if (_job == null || CurrentPath < 0 || CurrentPath >= _job.Paths.Count) return 0;
            List<Pt> pts = _job.Paths[CurrentPath].Points;
            if (pts.Count == 0) return 0;
            double dx = Paper.X - pts[0].X, dy = Paper.Y - pts[0].Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }
    }

    /// <summary>
    /// 掉线自动重连的退避策略（A3）。纯状态机，不碰串口，便于测试。
    /// 指数退避：1s → 2s → 4s → 8s → 16s → 30s 封顶，默认最多 8 次。
    /// </summary>
    public class ReconnectPolicy
    {
        public bool Enabled = true;
        public int MaxAttempts = 8;
        public double BaseDelaySec = 1.0;
        public double MaxDelaySec = 30.0;

        public int Attempts { get; private set; }
        public string Port = "";
        public int Baud = 115200;

        public void Reset()
        {
            Attempts = 0;
        }

        public bool Exhausted { get { return Attempts >= MaxAttempts; } }

        /// <summary>第 Attempts 次尝试前应等待的秒数（未开始计时前调用）。</summary>
        public double NextDelaySec()
        {
            double d = BaseDelaySec * Math.Pow(2, Math.Max(0, Attempts));
            if (d > MaxDelaySec) d = MaxDelaySec;
            return d;
        }

        /// <summary>记一次尝试；返回 true 表示这次仍允许尝试。</summary>
        public bool NoteAttempt()
        {
            if (!Enabled || Exhausted) return false;
            Attempts++;
            return true;
        }

        public string Describe()
        {
            if (!Enabled) return "自动重连已关闭";
            return "自动重连第 " + Attempts + "/" + MaxAttempts + " 次，间隔 "
                 + NextDelaySec().ToString("0.#") + " s";
        }
    }
}
