using System;
using System.Collections.Generic;
using System.Globalization;

namespace InkPen.Core
{
    /// <summary>一个探测点及其结果。Col/Row 是网格下标，X/Y 是纸面 mm。</summary>
    public class ProbePoint
    {
        public int Col, Row;
        public double X, Y;
        public bool Ok = false;
        public double Z;
    }

    /// <summary>一个探测点的完整指令块：抬笔 → 移动到点 → 下探。</summary>
    public class ProbeStep
    {
        public ProbePoint Point;
        public List<string> Lines = new List<string>();
    }

    /// <summary>
    /// 接触式探测（G38.2）的指令生成与结果解析。
    /// 纯逻辑、不碰串口：便于单元测试，也便于上层决定「一个点一条 ok 地发」还是「整块发」。
    ///
    /// 弹簧浮动笔架场景下的用法：
    ///   1) 逐点 G38.2 下探，记录各点纸面 Z（相对第一点）；
    ///   2) 落笔 Z = 探测基准 + 压入量 + 该点高度差。
    /// 注意 G38.2 需要机器已解锁（$X）且已回零/设过原点，否则直接 ALARM。
    /// </summary>
    public static class ProbePlanner
    {
        public static string F(double v)
        {
            return v.ToString("0.###", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// 在区域内生成规则网格点，行优先、逐行同向扫描（蛇形会让结果顺序与显示顺序不一致，不采用）。
        /// 区域非法时退化为「纸面留边 10%」的默认区域。
        /// </summary>
        public static List<ProbePoint> BuildPoints(double minX, double minY, double maxX, double maxY,
                                                   int cols, int rows)
        {
            cols = Math.Max(2, Math.Min(64, cols));
            rows = Math.Max(2, Math.Min(64, rows));

            if (maxX <= minX) { minX = 0; maxX = 100; }
            if (maxY <= minY) { minY = 0; maxY = 100; }

            List<ProbePoint> pts = new List<ProbePoint>();
            for (int r = 0; r < rows; r++)
            {
                double y = minY + (maxY - minY) * r / (rows - 1);
                for (int c = 0; c < cols; c++)
                {
                    double x = minX + (maxX - minX) * c / (cols - 1);
                    ProbePoint p = new ProbePoint();
                    p.Col = c; p.Row = r; p.X = x; p.Y = y;
                    pts.Add(p);
                }
            }
            return pts;
        }

        /// <summary>默认探测区域：纸面四周各留 5%，避免在纸边/夹子上探测。</summary>
        public static void DefaultArea(MachineConfig m, out double minX, out double minY,
                                       out double maxX, out double maxY)
        {
            double w = m == null ? 210 : m.PaperWidthMm;
            double h = m == null ? 297 : m.PaperHeightMm;
            minX = w * 0.05; maxX = w * 0.95;
            minY = h * 0.05; maxY = h * 0.95;
        }

        /// <summary>
        /// 生成逐点探测指令。每个块以「抬笔 + 快速移动」开头，下探到 SafeZ - travel（mm）。
        /// 上层收到 [PRB:] 或超时后，应发送 G0 Z&lt;SafeZ&gt; 再进入下一块。
        ///
        /// ProbePoint.X/Y 是**纸面** mm（高度图与画布都用这套坐标），
        /// 但 G0 必须发机器坐标，所以这里统一走 CoordTransform.ToMachine。
        /// 换原点角 / 反转轴之后，探测点仍然落在纸上同一位置。
        /// </summary>
        public static List<ProbeStep> BuildSteps(MachineConfig m, List<ProbePoint> pts, double travelMm)
        {
            List<ProbeStep> steps = new List<ProbeStep>();
            if (m == null || pts == null) return steps;

            double travel = travelMm > 0 ? travelMm : 8.0;
            double target = m.SafeZ - travel;
            string lift = "G0 Z" + F(m.SafeZ);

            foreach (ProbePoint p in pts)
            {
                Pt mc = CoordTransform.ToMachine(new Pt(p.X, p.Y), m);
                ProbeStep s = new ProbeStep();
                s.Point = p;
                s.Lines.Add(lift);
                s.Lines.Add("G0 X" + F(mc.X) + " Y" + F(mc.Y));
                s.Lines.Add("G38.2 Z" + F(target) + " F" + F(m.ProbeFeedMmPerMin));
                steps.Add(s);
            }
            return steps;
        }

        /// <summary>
        /// 单点对刀（A5）：只探一个点，用于测当前纸厚/笔尖接触位。
        /// x/y 是**机器（工件）坐标**，通常直接传当前 WPos——就地探测，不移动 XY。
        /// </summary>
        public static List<string> BuildSinglePoint(MachineConfig m, double x, double y, double travelMm)
        {
            List<string> lines = new List<string>();
            if (m == null) return lines;
            double travel = travelMm > 0 ? travelMm : 8.0;
            lines.Add("G0 Z" + F(m.SafeZ));
            lines.Add("G0 X" + F(x) + " Y" + F(y));
            lines.Add("G38.2 Z" + F(m.SafeZ - travel) + " F" + F(m.ProbeFeedMmPerMin));
            return lines;
        }

        /// <summary>解析 GRBL 的探测回报：[PRB:x,y,z:1]（成功）/ [PRB:0,0,0:0]（未接触）。</summary>
        public static bool TryParsePrb(string line, out double x, out double y, out double z, out bool ok)
        {
            x = y = z = 0;
            ok = false;
            if (line == null) return false;

            string s = line.Trim();
            int lb = s.IndexOf("[PRB:", StringComparison.OrdinalIgnoreCase);
            if (lb < 0) return false;
            int rb = s.IndexOf(']', lb);
            if (rb < 0) return false;

            string body = s.Substring(lb + 5, rb - lb - 5);
            string[] parts = body.Split(':');
            if (parts.Length < 1) return false;

            string[] xyz = parts[0].Split(',');
            if (xyz.Length < 3) return false;

            if (!double.TryParse(xyz[0], NumberStyles.Float, CultureInfo.InvariantCulture, out x)) return false;
            if (!double.TryParse(xyz[1], NumberStyles.Float, CultureInfo.InvariantCulture, out y)) return false;
            if (!double.TryParse(xyz[2], NumberStyles.Float, CultureInfo.InvariantCulture, out z)) return false;

            if (parts.Length >= 2)
            {
                int flag;
                ok = int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out flag) && flag == 1;
            }
            // 明确回报 :0 时坐标是无效值，清掉，避免把 (0,0,0) 当真实高度
            if (!ok) { x = y = z = 0; }
            return true;
        }

        /// <summary>
        /// 把探测结果折算成高度表：以第一个成功点为基准（记 0），其余存相对差。
        /// 失败点用邻近成功点的均值填补；全失败则整张图判为无效。
        /// </summary>
        public static void ApplyResults(HeightMap map, List<ProbePoint> pts)
        {
            if (map == null || pts == null) return;

            double baseZ = 0;
            bool hasBase = false;
            int fails = 0;

            foreach (ProbePoint p in pts)
            {
                if (!p.Ok) { fails++; continue; }
                if (!hasBase) { baseZ = p.Z; hasBase = true; }
            }

            map.BaseZ = baseZ;
            map.FailCount = fails;
            map.Valid = hasBase && fails < pts.Count;
            if (!hasBase) return;

            foreach (ProbePoint p in pts)
            {
                double v = p.Ok ? p.Z - baseZ : double.NaN;
                map.Set(p.Col, p.Row, v);
            }

            // 失败点用 4 邻域均值补，没有邻居则填 0
            if (fails > 0)
            {
                for (int r = 0; r < map.Rows; r++)
                {
                    for (int c = 0; c < map.Cols; c++)
                    {
                        if (!double.IsNaN(map.Get(c, r))) continue;
                        double sum = 0; int n = 0;
                        int[][] d = new int[][] { new int[] { -1, 0 }, new int[] { 1, 0 },
                                                  new int[] { 0, -1 }, new int[] { 0, 1 } };
                        foreach (int[] o in d)
                        {
                            int rr = r + o[1], cc = c + o[0];
                            if (rr < 0 || rr >= map.Rows || cc < 0 || cc >= map.Cols) continue;
                            double v = map.Get(cc, rr);
                            if (double.IsNaN(v)) continue;
                            sum += v; n++;
                        }
                        map.Set(c, r, n > 0 ? sum / n : 0);
                    }
                }
            }
        }
    }
}
