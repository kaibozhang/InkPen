using System;
using System.Collections.Generic;
using System.Globalization;

namespace InkPen.Core
{
    /// <summary>纸面 mm 坐标 ↔ 机器坐标（原点角 / 轴反转）。</summary>
    public static class CoordTransform
    {
        public static Pt ToMachine(Pt p, MachineConfig m)
        {
            double x = p.X, y = p.Y;
            switch (m.Origin)
            {
                case OriginCorner.TopLeft: y = m.PaperHeightMm - y; break;
                case OriginCorner.BottomRight: x = m.PaperWidthMm - x; break;
                case OriginCorner.TopRight:
                    x = m.PaperWidthMm - x; y = m.PaperHeightMm - y; break;
            }
            if (m.InvertX) x = -x;
            if (m.InvertY) y = -y;
            return new Pt(x, y);
        }

        /// <summary>
        /// 机器坐标 → 纸面 mm。ToMachine 的严格逆运算。
        /// 顺序必须与正变换相反：先撤销 Invert，再撤销原点角。
        /// 两者不可交换（x→W-x 与 x→-x 复合的结果不同），写反了笔位置会跑偏一整个纸宽。
        /// </summary>
        public static Pt ToPaper(Pt p, MachineConfig m)
        {
            double x = p.X, y = p.Y;
            if (m.InvertX) x = -x;
            if (m.InvertY) y = -y;
            switch (m.Origin)
            {
                case OriginCorner.TopLeft: y = m.PaperHeightMm - y; break;
                case OriginCorner.BottomRight: x = m.PaperWidthMm - x; break;
                case OriginCorner.TopRight:
                    x = m.PaperWidthMm - x; y = m.PaperHeightMm - y; break;
            }
            return new Pt(x, y);
        }
    }

    public static class Rdp
    {
        public static List<Pt> Simplify(List<Pt> pts, double eps)
        {
            // 注意：null 不能走 new List<Pt>(pts)——构造函数会抛 ArgumentNullException
            if (pts == null) return new List<Pt>();
            if (pts.Count < 3 || eps <= 0) return new List<Pt>(pts);
            bool[] keep = new bool[pts.Count];
            keep[0] = keep[pts.Count - 1] = true;
            SimplifyPart(pts, 0, pts.Count - 1, eps, keep);
            List<Pt> outp = new List<Pt>();
            for (int i = 0; i < pts.Count; i++) if (keep[i]) outp.Add(pts[i]);
            return outp;
        }

        private static void SimplifyPart(List<Pt> p, int a, int b, double eps, bool[] keep)
        {
            if (b <= a + 1) return;
            double maxD = -1; int idx = -1;
            for (int i = a + 1; i < b; i++)
            {
                double d = DistToSegment(p[i], p[a], p[b]);
                if (d > maxD) { maxD = d; idx = i; }
            }
            if (maxD > eps && idx > 0)
            {
                keep[idx] = true;
                SimplifyPart(p, a, idx, eps, keep);
                SimplifyPart(p, idx, b, eps, keep);
            }
        }

        private static double DistToSegment(Pt p, Pt a, Pt b)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y;
            double len2 = dx * dx + dy * dy;
            if (len2 < 1e-12) return Math.Sqrt((p.X - a.X) * (p.X - a.X) + (p.Y - a.Y) * (p.Y - a.Y));
            double t = ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len2;
            if (t < 0) t = 0; else if (t > 1) t = 1;
            double qx = a.X + t * dx, qy = a.Y + t * dy;
            return Math.Sqrt((p.X - qx) * (p.X - qx) + (p.Y - qy) * (p.Y - qy));
        }
    }

    public static class HandwritingPerturb
    {
        /// <summary>基于索引哈希的确定性笔画点扰动，不污染字库缓存。</summary>
        public static double Hash(int a, int b, int c, int seed)
        {
            uint h = (uint)(seed ^ (a * 73856093) ^ (b * 19349663) ^ (c * 83492791));
            h ^= h >> 16; h *= 0x7feb352d; h ^= h >> 15; h *= 0x846ca68b; h ^= h >> 16;
            return ((h & 0xFFFFFF) / 16777216.0) * 2.0 - 1.0;
        }

        /// <summary>笔锋：在起笔/收笔处追加一小段回描，模拟真人顿笔。</summary>
        public static List<Pt> EnhanceTail(List<Pt> pts, double strength)
        {
            if (pts == null || pts.Count < 2 || strength <= 0) return pts;
            List<Pt> r = new List<Pt>(pts);
            Pt a = pts[0], b = pts[1];
            double len = Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
            if (len > 1e-6)
            {
                double k = Math.Min(strength, 0.2) * Math.Min(len, 2.0) / len;
                r.Insert(0, new Pt(a.X + (a.X - b.X) * k, a.Y + (a.Y - b.Y) * k));
            }
            Pt c = pts[pts.Count - 2], d = pts[pts.Count - 1];
            double len2 = Math.Sqrt((d.X - c.X) * (d.X - c.X) + (d.Y - c.Y) * (d.Y - c.Y));
            if (len2 > 1e-6)
            {
                double k = Math.Min(strength, 0.2) * Math.Min(len2, 2.0) / len2;
                r.Add(new Pt(d.X + (d.X - c.X) * k, d.Y + (d.Y - c.Y) * k));
            }
            return r;
        }
    }

    public class GCodeValidator
    {
        public readonly List<string> Errors = new List<string>();
        public readonly List<string> Warnings = new List<string>();

        public void Validate(Job job)
        {
            Errors.Clear(); Warnings.Clear();
            MachineConfig m = job.Machine;
            if (job.GCode.Count == 0) { Errors.Add("GCode 为空"); return; }
            bool hasZ = false, hasFeed = false;
            for (int i = 0; i < job.GCode.Count; i++)
            {
                string s = job.GCode[i].Text;
                // 与 StreamController 一致：发送前去空格，GRBL 1.1 单行缓冲 80 字节。
                // 离线校验阶段就报错，避免真机书写时才中止。
                int wireBytes = s.Replace(" ", "").Length;
                if (wireBytes > 80)
                    Errors.Add("行 " + (i + 1) + " 压缩后 " + wireBytes
                             + " 字节超过 GRBL 单行缓冲 80 字节，会被静默截断");
                if (s.IndexOf("Z") >= 0) hasZ = true;
                if (s.IndexOf("F") >= 0) hasFeed = true;
                double x, y;
                ExtractXY(s, out x, out y);
                if (!double.IsNaN(x) && (x < -m.PaperWidthMm - 1 || x > m.PaperWidthMm + 1))
                    Errors.Add("行 " + (i + 1) + " X 越界: " + x.ToString("F1"));
                if (!double.IsNaN(y) && (y < -m.PaperHeightMm - 1 || y > m.PaperHeightMm + 1))
                    Errors.Add("行 " + (i + 1) + " Y 越界: " + y.ToString("F1"));
            }
            if (!hasZ) Errors.Add("未发现任何 Z 抬落笔指令");
            if (!hasFeed) Warnings.Add("未发现 F 进给率");
            if (m.SafeZ <= m.WriteZ) Warnings.Add("安全 Z 不大于书写 Z，可能撞笔；请在真机联调前校准");
            if (m.XYFeed <= 0 || m.ZFeed <= 0) Errors.Add("进给率必须大于 0");
        }

        private static void ExtractXY(string s, out double x, out double y)
        {
            x = y = double.NaN;
            string[] t = s.Split(' ');
            foreach (string p in t)
            {
                if (p.Length < 2) continue;
                double v;
                if (!double.TryParse(p.Substring(1), NumberStyles.Float, CultureInfo.InvariantCulture, out v)) continue;
                if (p[0] == 'X') x = v;
                else if (p[0] == 'Y') y = v;
            }
        }
    }

    /// <summary>把排版结果转成 Z 轴抬落笔 GCode，并生成仿真路径与断点续写索引。</summary>
    public static class GCodeBuilder
    {
        public static void Build(Job job, bool includeGrid)
        {
            job.GCode.Clear();
            job.Paths.Clear();
            MachineConfig m = job.Machine;
            AddLines(job, m.StartGCode, -1, -1, -1, -1, 0);
            Emit(job, "(INKPEN JOB START)", null);
            Emit(job, "G21 G90 G94", null);
            Emit(job, "F" + F(m.XYFeed), null);

            bool penDown = false;
            double lastX = 0, lastY = 0;
            // 上一次真正写给 GRBL 的落笔 Z。高度补偿是「相对上一次的变化量」触发，
            // 所以必须跨笔画、跨页保留，否则每次换笔都会多吐一条无意义的 Z 行。
            double lastZ = m.WriteZ;
            foreach (Page page in job.Pages)
            {
                if (includeGrid && job.Template.DrawGrid)
                {
                    foreach (GridLine gl in page.Grid)
                    {
                        List<Pt> line = new List<Pt>();
                        line.Add(new Pt(gl.X1, gl.Y1));
                        line.Add(new Pt(gl.X2, gl.Y2));
                        EmitPath(job, line, ref penDown, ref lastX, ref lastY, ref lastZ,
                                 page.Index, -1, -1, -1, 0);
                    }
                }

                foreach (PlacedGlyph pg in page.Glyphs)
                {
                    for (int si = 0; si < pg.Glyph.Strokes.Count; si++)
                    {
                        Stroke st = pg.Glyph.Strokes[si];
                        List<Pt> raw = new List<Pt>();
                        for (int pi = 0; pi < st.Points.Count; pi++)
                        {
                            Pt g = st.Points[pi];
                            double jx = HandwritingPerturb.Hash(pg.CharIndex, si, pi, m.Seed) * m.JitterStrokeMm;
                            double jy = HandwritingPerturb.Hash(si, pi, pg.CharIndex + 7, m.Seed + 1) * m.JitterStrokeMm;
                            raw.Add(new Pt(
                                pg.X + g.X / 1024.0 * pg.SizeMm + jx,
                                pg.Y + g.Y / 1024.0 * pg.SizeMm + jy));
                        }
                        List<Pt> pts = Rdp.Simplify(HandwritingPerturb.EnhanceTail(raw, m.TailStrength),
                                                   m.SimplifyEpsilonMm);
                        if (pts.Count < 2) continue;

                        // 连笔优化：相邻笔画端点足够近时不抬笔
                        Pt first = CoordTransform.ToMachine(pts[0], m);
                        double gap = Math.Sqrt((first.X - lastX) * (first.X - lastX)
                                             + (first.Y - lastY) * (first.Y - lastY));
                        if (penDown && gap > m.ConnectDistanceMm)
                        {
                            Emit(job, "G0 Z" + F(m.SafeZ), null);
                            penDown = false;
                        }
                        EmitPath(job, pts, ref penDown, ref lastX, ref lastY, ref lastZ,
                                 page.Index, pg.LineIndex, pg.CharIndex, si, pg.Ch);
                    }
                }
            }
            if (penDown) Emit(job, "G0 Z" + F(m.SafeZ), null);
            Emit(job, "(INKPEN JOB END)", null);
            AddLines(job, m.EndGCode, -1, -1, -1, -1, 0);
        }

        /// <summary>高度图补偿阈值：小于此值不单独写 Z，避免行数暴涨而精度无收益。</summary>
        public const double ZCompEpsilon = 0.005;

        /// <summary>某个纸面点应有的落笔 Z（机器坐标）。未启用高度图时恒为 WriteZ。</summary>
        public static double CompensatedZ(Job job, double paperX, double paperY)
        {
            MachineConfig m = job.Machine;
            if (m == null) return 0;
            if (!m.UseHeightMap) return m.WriteZ;
            HeightMap map = job.HeightMap;
            if (map == null || !map.IsUsable) return m.WriteZ;
            return m.WriteZ + map.At(paperX, paperY);
        }

        private static void EmitPath(Job job, List<Pt> paperPts, ref bool penDown,
            ref double lastX, ref double lastY, ref double lastZ, int pageIdx, int lineIdx,
            int charIdx, int strokeIdx, int cp)
        {
            MachineConfig m = job.Machine;
            List<Pt> machine = new List<Pt>();
            foreach (Pt p in paperPts) machine.Add(CoordTransform.ToMachine(p, m));
            Pt start = machine[0];

            bool comp = m.UseHeightMap && job.HeightMap != null && job.HeightMap.IsUsable;
            double z0 = CompensatedZ(job, paperPts[0].X, paperPts[0].Y);

            WritePath wp = new WritePath();
            wp.PageIndex = pageIdx; wp.LineIndex = lineIdx; wp.CharIndex = charIdx;
            wp.StrokeIndex = strokeIdx; wp.CodePoint = cp; wp.PenDown = true;
            wp.FirstGCodeLine = job.GCode.Count;
            // 仿真画布按纸面排版显示，故存纸面坐标（机器坐标仅用于 GCode）
            foreach (Pt p in paperPts) wp.Points.Add(p);
            job.Paths.Add(wp);

            string meta = "(CH:" + cp + ";PG:" + pageIdx + ";LN:" + lineIdx
                        + ";CI:" + charIdx + ";SI:" + strokeIdx + ")";
            Emit(job, meta, MakeMeta(pageIdx, lineIdx, charIdx, strokeIdx, cp, false));
            if (!penDown)
            {
                Emit(job, "G0 X" + F(start.X) + " Y" + F(start.Y), MakeMeta(pageIdx, lineIdx, charIdx, strokeIdx, cp, false));
                Emit(job, "G1 Z" + F(z0) + " F" + F(m.ZFeed), MakeMeta(pageIdx, lineIdx, charIdx, strokeIdx, cp, false));
                if (m.DwellSec > 0) Emit(job, "G4 P" + F(m.DwellSec), null);
                penDown = true;
                lastZ = z0;
            }
            else if (comp && Math.Abs(z0 - lastZ) >= ZCompEpsilon)
            {
                // 连笔（不抬笔）换笔画时高度变了：把 Z 并进这一笔的第一条 XY 行
                Emit(job, "G1 Z" + F(z0) + " F" + F(m.ZFeed),
                     MakeMeta(pageIdx, lineIdx, charIdx, strokeIdx, cp, false));
                lastZ = z0;
            }

            for (int i = 1; i < machine.Count; i++)
            {
                Pt p = machine[i];
                string zseg = "";
                if (comp)
                {
                    double pz = CompensatedZ(job, paperPts[i].X, paperPts[i].Y);
                    if (Math.Abs(pz - lastZ) >= ZCompEpsilon)
                    {
                        zseg = " Z" + F(pz);
                        lastZ = pz;
                    }
                }
                if (i == 1)
                    Emit(job, "G1 X" + F(p.X) + " Y" + F(p.Y) + zseg + " F" + F(m.XYFeed),
                         MakeMeta(pageIdx, lineIdx, charIdx, strokeIdx, cp, true));
                else
                    Emit(job, "G1 X" + F(p.X) + " Y" + F(p.Y) + zseg,
                         MakeMeta(pageIdx, lineIdx, charIdx, strokeIdx, cp, true));
            }
            lastX = machine[machine.Count - 1].X;
            lastY = machine[machine.Count - 1].Y;
        }

        private static GCodeLine MakeMeta(int pg, int ln, int ci, int si, int cp, bool down)
        {
            GCodeLine g = new GCodeLine();
            g.PageIndex = pg; g.LineIndex = ln; g.CharIndex = ci;
            g.StrokeIndex = si; g.CodePoint = cp; g.IsPenDown = down;
            return g;
        }

        private static void Emit(Job job, string text, GCodeLine meta)
        {
            GCodeLine g = meta ?? new GCodeLine();
            g.Text = text;
            job.GCode.Add(g);
        }

        private static void AddLines(Job job, string block, int pg, int ln, int ci, int si, int cp)
        {
            if (string.IsNullOrEmpty(block)) return;
            string[] lines = block.Replace("\r\n", "\n").Split('\n');
            foreach (string l in lines)
            {
                string s = l.Trim();
                if (s.Length == 0) continue;
                Emit(job, s, MakeMeta(pg, ln, ci, si, cp, false));
            }
        }

        public static string F(double v)
        {
            return v.ToString("0.###", CultureInfo.InvariantCulture);
        }

        /// <summary>按字符序号找到续写起始 GCode 行（含前置抬笔）。</summary>
        public static int FindResumeLine(Job job, int charIndex)
        {
            if (charIndex <= 0) return 0;
            for (int i = 0; i < job.GCode.Count; i++)
            {
                GCodeLine g = job.GCode[i];
                if (g.CharIndex >= charIndex && g.StrokeIndex >= 0) return i;
            }
            // 序号超出文档长度：返回末尾而不是 0。
            // 返回 0 会让机器把整篇重写一遍，白白废掉已写好的纸。
            return job.GCode.Count;
        }

        public static string ToText(Job job, int fromLine)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            if (fromLine > 0)
            {
                // 对齐 GRBL-Plotter 的 StreamSection 做法：续写前恢复模态状态与抬笔，
                // 避免从中途开始时沿用未知的 G90/G91、G20/G21、F 状态。
                sb.Append("(RESUME FROM LINE " + (fromLine + 1) + ")\n");
                sb.Append("G0 Z" + F(job.Machine.SafeZ) + "\n");
                sb.Append("G21\nG90\nG94\n");
                sb.Append("F" + F(job.Machine.XYFeed) + "\n");
            }
            for (int i = fromLine; i < job.GCode.Count; i++)
                sb.Append(job.GCode[i].Text).Append('\n');
            return sb.ToString();
        }
    }
}
