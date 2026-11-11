using System;
using System.Collections.Generic;
using InkPen.Core;
using InkPen.UI;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace InkPen.Tests
{
    /// <summary>
    /// 高度图插值。这里的每条断言都对应一个「错了也不会报错、只会默默写坏纸」的失效模式：
    /// 行列转置、越界外推、脏数据当成有效。
    /// </summary>
    [TestClass]
    public class HeightMapTests
    {
        private static HeightMap Uniform(int cols, int rows, double z)
        {
            HeightMap m = new HeightMap();
            m.Reset(cols, rows);
            m.MinX = 0; m.MinY = 0; m.MaxX = 100; m.MaxY = 100;
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++) m.Set(c, r, z);
            m.Valid = true;
            return m;
        }

        [TestMethod]
        public void At_CornersEqualStoredValues()
        {
            HeightMap m = new HeightMap();
            m.Reset(3, 3);
            m.MinX = 0; m.MinY = 0; m.MaxX = 100; m.MaxY = 200;
            m.Valid = true;
            for (int r = 0; r < 3; r++)
                for (int c = 0; c < 3; c++) m.Set(c, r, c * 10 + r);

            Assert.AreEqual(0.0, m.Get(0, 0), 1e-9, "第 0 行第 0 列应是原点值");
            Assert.AreEqual(0.0, m.At(0, 0), 1e-9, "左下角");
            Assert.AreEqual(20.0, m.At(100, 0), 1e-9, "右下角 (c=2,r=0)");
            Assert.AreEqual(2.0, m.At(0, 200), 1e-9, "左上角 (c=0,r=2)");
            Assert.AreEqual(22.0, m.At(100, 200), 1e-9, "右上角 (c=2,r=2)");
        }

        /// <summary>行方向必须是「r 增大 → 纸面 Y 增大」。转置了这条会红。</summary>
        [TestMethod]
        public void At_BilinearInterpolation_IsNotTransposed()
        {
            // 3×2 且各列值不同：2×2 的对称网格对「行列转置」是不敏感的，抓不住这类缺陷
            HeightMap m = new HeightMap();
            m.Reset(3, 2);
            m.MinX = 0; m.MinY = 0; m.MaxX = 20; m.MaxY = 10;
            m.Valid = true;
            m.Set(0, 0, 0); m.Set(1, 0, 0.2); m.Set(2, 0, 0.4);
            m.Set(0, 1, 1); m.Set(1, 1, 1.2); m.Set(2, 1, 1.4);

            Assert.AreEqual(0.0, m.At(0, 0), 1e-9, "左下角");
            Assert.AreEqual(1.0, m.At(0, 10), 1e-9, "左上角应为 1（转置后会拿到 0.4）");
            Assert.AreEqual(1.4, m.At(20, 10), 1e-9, "右上角");
            Assert.AreEqual(0.5, m.At(0, 5), 1e-9, "沿 Y 的线性插值");
        }

        [TestMethod]
        public void At_OutsideArea_ClampsInsteadOfExtrapolating()
        {
            HeightMap m = Uniform(2, 2, 0.4);
            Assert.AreEqual(0.4, m.At(-500, -500), 1e-9, "区域外必须夹取，不能外推出天文数字");
            Assert.AreEqual(0.4, m.At(5000, 5000), 1e-9);
        }

        [TestMethod]
        public void At_InvalidMapReturnsZero()
        {
            HeightMap m = new HeightMap();
            Assert.AreEqual(0.0, m.At(10, 10), 1e-9, "未探测时应返回 0，不能抛异常或给 NaN");
            m.Reset(3, 3);
            m.Valid = false;
            Assert.AreEqual(0.0, m.At(10, 10), 1e-9);
        }

        /// <summary>
        /// 回归：FromJson 里 Reset() 会把 Valid 清成 false，曾经导致「读回高度图后热力图空白、
        /// 补偿被静默跳过」。往返必须保持 Valid 与数据不变。
        /// </summary>
        [TestMethod]
        public void JsonRoundTrip_PreservesValidityAndData()
        {
            HeightMap src = new HeightMap();
            src.Reset(3, 4);
            src.MinX = 5; src.MinY = 6; src.MaxX = 200; src.MaxY = 280;
            src.PressDepthMm = 0.42;
            src.ProbedAt = "2026-11-11 23:40";
            for (int i = 0; i < src.Z.Length; i++) src.Z[i] = 0.1 * i - 0.3;
            src.Valid = true;
            src.Enabled = true;

            HeightMap back = HeightMap.FromJson(src.ToJson());

            Assert.IsTrue(back.Valid, "往返后 Valid 不能被 Reset 清掉");
            Assert.AreEqual(3, back.Cols);
            Assert.AreEqual(4, back.Rows);
            Assert.AreEqual(5.0, back.MinX, 1e-9);
            Assert.AreEqual(0.42, back.PressDepthMm, 1e-9);
            for (int i = 0; i < src.Z.Length; i++)
                Assert.AreEqual(src.Z[i], back.Z[i], 1e-9, "第 " + i + " 个格子不一致");
            Assert.IsTrue(back.IsUsable);
        }

        [TestMethod]
        public void FromJson_WrongArrayLength_IsRejected()
        {
            HeightMap src = new HeightMap();
            src.Reset(3, 3);
            src.MinX = 0; src.MinY = 0; src.MaxX = 10; src.MaxY = 10;
            src.Valid = true;
            string json = src.ToJson().Replace("\"cols\":3", "\"cols\":4");

            HeightMap back = HeightMap.FromJson(json);
            Assert.IsFalse(back.Valid, "格子数与行列对不上时必须判为脏数据");
            Assert.IsFalse(back.IsUsable);
        }

        [TestMethod]
        public void Smooth_ReducesSinglePointSpike()
        {
            HeightMap m = Uniform(3, 3, 0);
            m.Set(1, 1, 0.9);
            m.Smooth();
            Assert.IsTrue(m.Get(1, 1) < 0.9, "平滑后尖峰应被削弱");
            Assert.IsTrue(m.Get(1, 1) > 0.0, "但不该被抹平到零");
            Assert.IsTrue(m.Get(0, 0) < 0.3, "角落只会被邻域轻微带起，不会被抬到接近尖峰的高度");
        }

        [TestMethod]
        public void Summary_ReportsRangeInMillimetres()
        {
            HeightMap m = Uniform(2, 2, 0);
            m.Set(1, 1, 0.8);
            string s = m.Summary();
            Assert.IsTrue(s.IndexOf("2×2", StringComparison.Ordinal) >= 0, s);
            Assert.IsTrue(s.IndexOf("0.800", StringComparison.Ordinal) >= 0, "起伏值应出现在摘要里: " + s);
        }
    }

    /// <summary>
    /// 机器坐标 ↔ 纸面坐标。ToPaper 写错不会崩，只会让笔位置画到纸外，
    /// 所以用「4 种原点角 × 4 种反转组合」全量往返验证，再加一条反例覆盖运算顺序。
    /// </summary>
    [TestClass]
    public class CoordInverseTests
    {
        [TestMethod]
        public void ToPaper_RoundTrips_AllOriginAndInvertCombinations()
        {
            OriginCorner[] origins = new OriginCorner[]
            {
                OriginCorner.BottomLeft, OriginCorner.TopLeft,
                OriginCorner.BottomRight, OriginCorner.TopRight
            };
            foreach (OriginCorner o in origins)
            {
                foreach (bool ix in new bool[] { false, true })
                {
                    foreach (bool iy in new bool[] { false, true })
                    {
                        MachineConfig m = new MachineConfig();
                        m.PaperWidthMm = 210; m.PaperHeightMm = 297;
                        m.Origin = o; m.InvertX = ix; m.InvertY = iy;

                        Pt p = new Pt(37.5, 241.25);
                        Pt back = CoordTransform.ToPaper(CoordTransform.ToMachine(p, m), m);
                        Assert.AreEqual(p.X, back.X, 1e-9, "X 往返失败 " + o + " ix=" + ix + " iy=" + iy);
                        Assert.AreEqual(p.Y, back.Y, 1e-9, "Y 往返失败 " + o + " ix=" + ix + " iy=" + iy);
                    }
                }
            }
        }

        /// <summary>
        /// 反例：先撤销原点角、再撤销 Invert 会得到 574 而不是 20。
        /// 这条断言专门锁住「逆变换顺序必须与正变换相反」。
        /// </summary>
        [TestMethod]
        public void ToPaper_UndoesInvertBeforeOrigin_NotAfter()
        {
            MachineConfig m = new MachineConfig();
            m.PaperWidthMm = 210; m.PaperHeightMm = 297;
            m.Origin = OriginCorner.TopLeft;
            m.InvertY = true;

            Pt machine = CoordTransform.ToMachine(new Pt(10, 20), m);
            Assert.AreEqual(-277.0, machine.Y, 1e-9);

            Pt paper = CoordTransform.ToPaper(machine, m);
            Assert.AreEqual(20.0, paper.Y, 1e-9,
                "顺序写反会得到 574；纸面 Y 必须回到 20");
        }
    }

    /// <summary>高度补偿落到 GCode 上的行为。核心是「不启用时一个字节都不变」。</summary>
    [TestClass]
    public class HeightCompensationTests
    {
        private static Job JobWithMap(double z)
        {
            Job j = TestSupport.SingleGlyphJob();
            HeightMap m = new HeightMap();
            m.Reset(2, 2);
            m.MinX = 0; m.MinY = 0; m.MaxX = 210; m.MaxY = 297;
            for (int i = 0; i < m.Z.Length; i++) m.Z[i] = z;
            m.Valid = true;
            j.HeightMap = m;
            return j;
        }

        /// <summary>取出所有落笔 Z 行（G1 Z...）。</summary>
        private static List<string> PenDownZ(Job j)
        {
            List<string> zs = new List<string>();
            foreach (GCodeLine g in j.GCode)
            {
                string t = g.Text;
                if (t.StartsWith("G1 Z", StringComparison.Ordinal)) zs.Add(t);
            }
            return zs;
        }

        /// <summary>取出全文所有出现过的 Z 值（含 G1 X.. Y.. Z.. 行尾的补偿值）。</summary>
        private static SortedSet<string> AllZValues(Job j)
        {
            SortedSet<string> zs = new SortedSet<string>();
            foreach (GCodeLine g in j.GCode)
            {
                foreach (string tok in g.Text.Split(' '))
                    if (tok.Length > 1 && tok[0] == 'Z') zs.Add(tok.Substring(1));
            }
            return zs;
        }

        [TestMethod]
        public void Disabled_KeepsWriteZConstant()
        {
            Job j = JobWithMap(0.5);
            j.Machine.UseHeightMap = false;
            GCodeBuilder.Build(j, false);

            List<string> zs = PenDownZ(j);
            Assert.IsTrue(zs.Count > 0, "应至少有一条落笔 Z");
            foreach (string z in zs)
                Assert.AreEqual("G1 Z0 F300", z, "未启用高度图时落笔 Z 必须等于 WriteZ");
        }

        [TestMethod]
        public void Enabled_ShiftsPenDownZByMapValue()
        {
            Job j = JobWithMap(0.5);
            j.Machine.UseHeightMap = true;
            GCodeBuilder.Build(j, false);

            List<string> zs = PenDownZ(j);
            Assert.IsTrue(zs.Count > 0);
            foreach (string z in zs)
                Assert.AreEqual("G1 Z0.5 F300", z, "落笔 Z 应为 WriteZ + 0.5");
        }

        [TestMethod]
        public void Enabled_EmitsExtraZ_WhenPaperIsTilted()
        {
            Job j = TestSupport.SingleGlyphJob();
            HeightMap m = new HeightMap();
            m.Reset(2, 2);
            m.MinX = 0; m.MinY = 0; m.MaxX = 60; m.MaxY = 297;
            m.Set(0, 0, 0); m.Set(1, 0, 1);     // 沿 X 抬高 1 mm
            m.Set(0, 1, 0); m.Set(1, 1, 1);
            m.Valid = true;
            j.HeightMap = m;
            j.Machine.UseHeightMap = true;
            j.Machine.SimplifyEpsilonMm = 0;    // 保留全部点，保证沿 X 有足够采样

            GCodeBuilder.Build(j, false);

            SortedSet<string> uniq = AllZValues(j);
            Assert.IsTrue(uniq.Count >= 2,
                "倾斜纸面应产生不止一种 Z（含落在 XY 行尾的补偿），实际 " + uniq.Count);
        }

        /// <summary>没有有效高度图时勾了开关也不能改变任何一行（防止「以为补偿了其实没补偿」）。</summary>
        [TestMethod]
        public void EnabledButNoData_ProducesIdenticalGCode()
        {
            Job a = TestSupport.SingleGlyphJob();
            a.Machine.UseHeightMap = false;
            GCodeBuilder.Build(a, false);

            Job b = TestSupport.SingleGlyphJob();
            b.Machine.UseHeightMap = true;      // HeightMap 为默认空对象
            GCodeBuilder.Build(b, false);

            Assert.AreEqual(a.GCode.Count, b.GCode.Count, "行数应完全一致");
            for (int i = 0; i < a.GCode.Count; i++)
                Assert.AreEqual(a.GCode[i].Text, b.GCode[i].Text, "第 " + (i + 1) + " 行不一致");
        }

        [TestMethod]
        public void Compensation_KeepsEveryLineWithinGrblBuffer()
        {
            Job j = JobWithMap(0.25);
            j.Machine.UseHeightMap = true;
            GCodeBuilder.Build(j, false);
            GCodeValidator v = new GCodeValidator();
            v.Validate(j);
            foreach (string e in v.Errors)
                Assert.IsFalse(e.IndexOf("80 字节", StringComparison.Ordinal) >= 0,
                               "补偿后行超长: " + e);
        }
    }

    /// <summary>探测指令生成与回报解析。</summary>
    [TestClass]
    public class ProbePlannerTests
    {
        [TestMethod]
        public void BuildPoints_CoversAreaCorners()
        {
            List<ProbePoint> pts = ProbePlanner.BuildPoints(10, 20, 110, 220, 3, 4);
            Assert.AreEqual(12, pts.Count);
            Assert.AreEqual(10.0, pts[0].X, 1e-9);
            Assert.AreEqual(20.0, pts[0].Y, 1e-9);
            Assert.AreEqual(110.0, pts[2].X, 1e-9, "同行第三列应到右边界");
            Assert.AreEqual(220.0, pts[11].Y, 1e-9, "最后一点应到上边界");
            Assert.AreEqual(0, pts[0].Col);
            Assert.AreEqual(3, pts[11].Row);
        }

        /// <summary>
        /// 探测点按纸面坐标算，但 G0 必须发机器坐标。
        /// 这里用「左上原点」做区分：纸面 (0,0) 在机器上是 (0, H)。
        /// </summary>
        [TestMethod]
        public void BuildSteps_ConvertsPaperToMachineCoordinates()
        {
            MachineConfig m = new MachineConfig();
            m.PaperWidthMm = 210; m.PaperHeightMm = 297;
            m.Origin = OriginCorner.TopLeft;
            m.SafeZ = 5;

            List<ProbePoint> pts = ProbePlanner.BuildPoints(0, 0, 0, 0, 2, 2);
            List<ProbeStep> steps = ProbePlanner.BuildSteps(m, pts, 8);

            Assert.AreEqual(4, steps.Count);
            Assert.AreEqual("G0 X0 Y297", steps[0].Lines[1],
                "纸面左下角在左上原点下应发到机器 Y=297");
            Assert.AreEqual("G0 Z5", steps[0].Lines[0], "每块开头必须先抬笔");
            Assert.AreEqual("G38.2 Z-3 F100", steps[0].Lines[2], "下探目标 = SafeZ - travel");
        }

        [TestMethod]
        public void BuildSinglePoint_DoesNotMoveXY()
        {
            MachineConfig m = new MachineConfig();
            m.SafeZ = 5;
            List<string> lines = ProbePlanner.BuildSinglePoint(m, 12.5, 34.5, 4);
            Assert.AreEqual(3, lines.Count);
            Assert.AreEqual("G0 Z5", lines[0]);
            Assert.AreEqual("G0 X12.5 Y34.5", lines[1]);
            Assert.AreEqual("G38.2 Z1 F100", lines[2]);
        }

        [TestMethod]
        public void ParsePrb_SuccessAndFailure()
        {
            double x, y, z; bool ok;

            Assert.IsTrue(ProbePlanner.TryParsePrb("[PRB:12.5,34.25,-1.75:1]", out x, out y, out z, out ok));
            Assert.IsTrue(ok);
            Assert.AreEqual(-1.75, z, 1e-9);

            Assert.IsTrue(ProbePlanner.TryParsePrb("[PRB:0,0,0:0]", out x, out y, out z, out ok));
            Assert.IsFalse(ok, "未接触必须判为失败");
            Assert.AreEqual(0.0, z, 1e-9, "失败时不能把 0 当真实高度");

            Assert.IsFalse(ProbePlanner.TryParsePrb("ok", out x, out y, out z, out ok));
            Assert.IsFalse(ProbePlanner.TryParsePrb(null, out x, out y, out z, out ok));
            Assert.IsFalse(ProbePlanner.TryParsePrb("[PRB:1,2]", out x, out y, out z, out ok));
        }

        [TestMethod]
        public void ApplyResults_FirstSuccessIsZero_AndFailuresFilled()
        {
            // Reset 的行列下限是 2（单行无法做双线性插值），所以用 3×2 的网格。
            HeightMap map = new HeightMap();
            map.Reset(3, 2);
            List<ProbePoint> pts = new List<ProbePoint>();
            for (int r = 0; r < 2; r++)
            {
                for (int c = 0; c < 3; c++)
                {
                    ProbePoint q = new ProbePoint();
                    q.Col = c; q.Row = r; q.X = c * 10; q.Y = r * 10;
                    pts.Add(q);
                }
            }

            pts[0].Ok = true; pts[0].Z = -2.0;    // (0,0)
            pts[1].Ok = false;                     // (1,0) 留空，应被邻点填补
            pts[2].Ok = true; pts[2].Z = -1.5;    // (2,0)
            for (int i = 3; i < 6; i++) { pts[i].Ok = true; pts[i].Z = -2.0; }

            ProbePlanner.ApplyResults(map, pts);

            Assert.IsTrue(map.Valid);
            Assert.AreEqual(-2.0, map.BaseZ, 1e-9, "基准取第一个成功点");
            Assert.AreEqual(0.0, map.Get(0, 0), 1e-9, "第一点归零");
            Assert.AreEqual(0.5, map.Get(2, 0), 1e-9, "第三点应比基准高 0.5");
            Assert.AreEqual(1, map.FailCount);
            // 不写死具体数字：只要求「落在两个相邻真实值之间」，这样实现换加权方式也不会误报
            double filled = map.Get(1, 0);
            Assert.IsFalse(double.IsNaN(filled), "失败点必须被填补，不能留 NaN");
            Assert.IsTrue(filled > 0.0 && filled < 0.5,
                "失败点应由两侧真实值插出，实际 " + filled);
        }

        [TestMethod]
        public void ApplyResults_AllFailed_InvalidatesMap()
        {
            HeightMap map = new HeightMap();
            map.Reset(2, 2);
            List<ProbePoint> pts = ProbePlanner.BuildPoints(0, 0, 10, 10, 2, 2);
            foreach (ProbePoint p in pts) p.Ok = false;

            ProbePlanner.ApplyResults(map, pts);
            Assert.IsFalse(map.Valid, "全部失败时不能产出一张「全是 0」的假高度图");
            Assert.IsFalse(map.IsUsable);
        }
    }

    /// <summary>探测执行器的状态机：不接串口也能验证「下一个点会不会发、失败了会不会卡住」。</summary>
    [TestClass]
    public class ProbeRunnerTests
    {
        private static List<ProbeStep> TwoSteps()
        {
            MachineConfig m = new MachineConfig();
            m.SafeZ = 5;
            // 手搓两个点：BuildPoints 的行列有下限 2，用它无法得到「只有两个点」的场景
            List<ProbePoint> pts = new List<ProbePoint>();
            ProbePoint a = new ProbePoint(); a.Col = 0; a.Row = 0; a.X = 0; a.Y = 0;
            ProbePoint b = new ProbePoint(); b.Col = 1; b.Row = 0; b.X = 10; b.Y = 10;
            pts.Add(a); pts.Add(b);
            return ProbePlanner.BuildSteps(m, pts, 4);
        }

        [TestMethod]
        public void Start_SendsFirstBlockOnly()
        {
            List<string> sent = new List<string>();
            ProbeRunner r = new ProbeRunner();
            r.Send = delegate(string s) { sent.Add(s); };

            r.Start(TwoSteps());

            Assert.IsTrue(r.Running);
            Assert.AreEqual(3, sent.Count, "只应发第一块的三行");
            Assert.AreEqual(0, r.Current);
        }

        [TestMethod]
        public void FeedPrb_AdvancesAndLiftsBeforeNextPoint()
        {
            List<string> sent = new List<string>();
            ProbeRunner r = new ProbeRunner();
            r.Send = delegate(string s) { sent.Add(s); };
            int finished = -1;
            r.OnFinished = delegate(int ok, int total) { finished = ok; };

            r.Start(TwoSteps());
            Assert.AreEqual(3, sent.Count, "起手只发第一块");
            Assert.IsTrue(r.FeedLine("[PRB:0,0,-1.2:1]"));

            Assert.AreEqual(1, r.Current, "应推进到第二个点");
            // 抬起笔 + 下一块的三行 = 7 行。
            // 只比对「第 4 行是 G0 Z5」抓不住漏抬笔——下一块自身的第一行也是 G0 Z5。
            Assert.AreEqual(7, sent.Count, "收到 PRB 后应先补一条抬笔，再发下一块");
            Assert.AreEqual("G0 Z5", sent[3], "第 4 行必须是补发的抬笔");
            Assert.AreEqual("G0 Z5", sent[4], "第 5 行是下一块自带的抬笔");
            Assert.AreEqual("G0 X10 Y10", sent[5], "第 6 行才是下一块的移动");
            Assert.AreEqual(-1, finished, "还没结束");

            Assert.IsTrue(r.FeedLine("[PRB:10,0,-1.0:1]"));
            Assert.IsFalse(r.Running);
            Assert.AreEqual(2, finished, "两个点都成功");
        }

        [TestMethod]
        public void ErrorLine_MarksPointFailed_ButContinues()
        {
            ProbeRunner r = new ProbeRunner();
            List<string> sent = new List<string>();
            r.Send = delegate(string s) { sent.Add(s); };
            int ok = -1, total = -1;
            r.OnFinished = delegate(int a, int b) { ok = a; total = b; };

            r.Start(TwoSteps());
            Assert.IsTrue(r.FeedLine("error:5"));

            Assert.IsTrue(r.Running, "单点失败不能整批中止");
            Assert.AreEqual(1, r.Current);

            r.FeedLine("[PRB:10,0,-1.0:1]");
            Assert.AreEqual(1, ok, "只有一个点成功");
            Assert.AreEqual(2, total);
        }

        [TestMethod]
        public void Poll_TimeoutMarksPointFailed()
        {
            ProbeRunner r = new ProbeRunner();
            r.Send = delegate(string s) { };
            r.TimeoutMs = 1;
            r.Start(TwoSteps());

            System.Threading.Thread.Sleep(5);
            r.Poll(DateTime.UtcNow);

            Assert.AreEqual(1, r.Current, "超时应跳过当前点继续下一个");
        }

        [TestMethod]
        public void Cancel_ReportsPartialProgress()
        {
            ProbeRunner r = new ProbeRunner();
            r.Send = delegate(string s) { };
            int ok = -1;
            r.OnFinished = delegate(int a, int b) { ok = a; };

            r.Start(TwoSteps());
            r.FeedLine("[PRB:0,0,-1:1]");
            r.Cancel();

            Assert.IsFalse(r.Running);
            Assert.IsTrue(r.Cancelled);
            Assert.AreEqual(1, ok, "中止时也要回报已成功的点数");
        }

        [TestMethod]
        public void EmptySteps_FinishesImmediately()
        {
            ProbeRunner r = new ProbeRunner();
            int ok = -1, total = -1;
            r.OnFinished = delegate(int a, int b) { ok = a; total = b; };
            r.Start(new List<ProbeStep>());
            Assert.IsFalse(r.Running);
            Assert.AreEqual(0, ok);
            Assert.AreEqual(0, total);
        }
    }

    /// <summary>实时笔位置：坐标系选错会把笔画到纸外，行号映射错会导致进度乱跳。</summary>
    [TestClass]
    public class PenTrackerTests
    {
        private static Job BuiltJob()
        {
            Job j = TestSupport.SingleGlyphJob();
            GCodeBuilder.Build(j, false);
            return j;
        }

        [TestMethod]
        public void SetStatus_PrefersWorkPosition()
        {
            Job j = BuiltJob();
            PenTracker t = new PenTracker();
            t.SetJob(j);

            // 用真实报文构造：手填字段会绕过 ParseStatus，也就测不到「到底哪段被采信」
            GrblStatus st = GrblClient.ParseStatus("<Idle|MPos:100,200,0|WPos:12,34,0>");
            t.SetStatus(st);

            Assert.IsTrue(t.HasPosition);
            Assert.AreEqual(12.0, t.Paper.X, 1e-9, "有 WPos 时必须用工件坐标（GCode 所在的坐标系）");
            Assert.AreEqual(34.0, t.Paper.Y, 1e-9);
        }

        [TestMethod]
        public void SetStatus_OnlyMposWithoutWco_IsNotTrusted()
        {
            Job j = BuiltJob();
            PenTracker t = new PenTracker();
            t.SetJob(j);

            // 只有 MPos、没有 WCO：不知道工件偏移，机械坐标不能当工件坐标用。
            // 之前的实现会直接拿 MX/MY 顶上，设过零点后笔位置整体偏一个 WCO。
            GrblStatus st = GrblClient.ParseStatus("<Idle|MPos:100,200,0>");
            t.SetStatus(st);

            Assert.IsFalse(st.HasPosition, "没有 WPos 也没有 WCO 时不该认为位置有效");
            Assert.IsFalse(t.HasPosition, "无效位置不能污染笔跟踪状态");
        }

        [TestMethod]
        public void SetStatus_MposPlusWco_IsConvertedToWorkPosition()
        {
            Job j = BuiltJob();
            PenTracker t = new PenTracker();
            t.SetJob(j);

            // $10 掩码关掉 WPos 的固件只给 MPos + WCO，必须自己换算 W = M - WCO
            GrblStatus st = GrblClient.ParseStatus("<Idle|MPos:100,200,0|WCO:90,160,0>");
            Assert.IsTrue(st.HasPosition);
            Assert.AreEqual(10.0, st.WX, 1e-9);
            Assert.AreEqual(40.0, st.WY, 1e-9);

            t.SetStatus(st);
            Assert.AreEqual(10.0, t.Paper.X, 1e-9);
            Assert.AreEqual(40.0, t.Paper.Y, 1e-9);
        }

        [TestMethod]
        public void SetStatus_MapsThroughOriginCorner()
        {
            Job j = BuiltJob();
            j.Machine.Origin = OriginCorner.TopLeft;
            j.Machine.PaperHeightMm = 297;

            PenTracker t = new PenTracker();
            t.SetJob(j);

            GrblStatus st = GrblClient.ParseStatus("<Idle|WPos:10,0,0>");
            t.SetStatus(st);

            Assert.AreEqual(297.0, t.Paper.Y, 1e-9, "左上原点时机器 Y=0 对应纸面顶部");
        }

        [TestMethod]
        public void SetSentLines_MapsToCurrentPath()
        {
            Job j = BuiltJob();
            Assert.AreEqual(2, j.Paths.Count);

            PenTracker t = new PenTracker();
            t.SetJob(j);

            t.SetSentLines(j.Paths[0].FirstGCodeLine + 1);
            Assert.AreEqual(0, t.CurrentPath, "应定位到第 1 笔");

            t.SetSentLines(j.Paths[1].FirstGCodeLine + 1);
            Assert.AreEqual(1, t.CurrentPath, "应推进到第 2 笔");
        }

        [TestMethod]
        public void SetSentLines_BeforeAnyStroke_IsMinusOne()
        {
            Job j = BuiltJob();
            PenTracker t = new PenTracker();
            t.SetJob(j);
            t.SetSentLines(0);
            Assert.AreEqual(-1, t.CurrentPath, "还没发到笔画行时不能假装在第 1 笔");
        }

        [TestMethod]
        public void SetSentLines_RewindDoesNotStick()
        {
            Job j = BuiltJob();
            PenTracker t = new PenTracker();
            t.SetJob(j);

            t.SetSentLines(j.Paths[1].FirstGCodeLine + 1);
            Assert.AreEqual(1, t.CurrentPath);

            t.SetSentLines(j.Paths[0].FirstGCodeLine + 1);
            Assert.AreEqual(0, t.CurrentPath, "重新开始写时必须能回退，不能沿用旧游标");
        }

        [TestMethod]
        public void SetSentLines_DoesNotThrowForNullJob()
        {
            PenTracker t = new PenTracker();
            t.SetSentLines(10);
            Assert.AreEqual(-1, t.CurrentPath);
        }
    }

    /// <summary>自动重连退避。</summary>
    [TestClass]
    public class ReconnectPolicyTests
    {
        [TestMethod]
        public void NextDelay_DoublesAndCaps()
        {
            ReconnectPolicy p = new ReconnectPolicy();
            p.BaseDelaySec = 1; p.MaxDelaySec = 30;

            Assert.AreEqual(1.0, p.NextDelaySec(), 1e-9);
            p.NoteAttempt();
            Assert.AreEqual(2.0, p.NextDelaySec(), 1e-9);
            p.NoteAttempt();
            Assert.AreEqual(4.0, p.NextDelaySec(), 1e-9);
            for (int i = 0; i < 10; i++) p.NoteAttempt();
            Assert.AreEqual(30.0, p.NextDelaySec(), 1e-9, "必须封顶，否则间隔会变成几小时");
        }

        [TestMethod]
        public void NoteAttempt_StopsAtMax()
        {
            ReconnectPolicy p = new ReconnectPolicy();
            p.MaxAttempts = 3;
            Assert.IsTrue(p.NoteAttempt());
            Assert.IsTrue(p.NoteAttempt());
            Assert.IsTrue(p.NoteAttempt());
            Assert.IsFalse(p.NoteAttempt(), "超过上限后必须拒绝，避免无限重连");
            Assert.IsTrue(p.Exhausted);
        }

        [TestMethod]
        public void Disabled_NeverAttempts()
        {
            ReconnectPolicy p = new ReconnectPolicy();
            p.Enabled = false;
            Assert.IsFalse(p.NoteAttempt());
            Assert.AreEqual(0, p.Attempts);
        }
    }

    /// <summary>宏按钮：占位符替换与持久化。</summary>
    [TestClass]
    public class MacroTests
    {
        [TestMethod]
        public void Defaults_HaveEditableButtons()
        {
            MacroSet s = MacroSet.Defaults();
            Assert.AreEqual(4, s.Buttons.Count, "界面固定 4 个宏按钮");
            foreach (MacroButton b in s.Buttons)
            {
                Assert.IsTrue(b.Label.Length > 0, "按钮文字不能为空");
                Assert.IsTrue(b.Command.Length > 0, "指令不能为空");
            }
        }

        [TestMethod]
        public void Expand_ReplacesMachinePlaceholders()
        {
            MachineConfig m = new MachineConfig();
            m.SafeZ = 7.5; m.WriteZ = -0.2; m.XYFeed = 1800; m.ZFeed = 250;

            Assert.AreEqual("G0 Z7.5", MacroSet.Expand("G0 Z{safez}", m));
            Assert.AreEqual("G1 Z-0.2 F250", MacroSet.Expand("G1 Z{writez} F{zfeed}", m));
            Assert.AreEqual("F1800", MacroSet.Expand("F{xyfeed}", m));
            Assert.AreEqual("G0 Z7.5", MacroSet.Expand("G0 Z{safez}", null).Replace("{safez}", "7.5"),
                "null 配置时不能抛异常");
        }

        [TestMethod]
        public void Split_TrimsAndDropsBlankLines()
        {
            List<string> lines = MacroSet.Split("G0 Z5\r\n\r\n  G4 P0.2  \n");
            Assert.AreEqual(2, lines.Count);
            Assert.AreEqual("G0 Z5", lines[0]);
            Assert.AreEqual("G4 P0.2", lines[1]);
        }

        [TestMethod]
        public void JsonRoundTrip_KeepsLabelsAndCommands()
        {
            MacroSet s = new MacroSet();
            s.Buttons.Add(new MacroButton("换纸", "G0 X0 Y0\nG4 P1"));
            s.Buttons.Add(new MacroButton("含引号\"的", "G0 Z{safez}"));

            MacroSet back = MacroSet.FromJson(s.ToJson());
            Assert.AreEqual(2, back.Buttons.Count);
            Assert.AreEqual("换纸", back.Buttons[0].Label);
            Assert.AreEqual("G0 X0 Y0\nG4 P1", back.Buttons[0].Command, "换行必须原样保留");
            Assert.AreEqual("含引号\"的", back.Buttons[1].Label, "引号必须正确转义");
        }

        [TestMethod]
        public void FromJson_GarbageFallsBackToDefaults()
        {
            Assert.AreEqual(4, MacroSet.FromJson("").Buttons.Count);
            Assert.AreEqual(4, MacroSet.FromJson("{not json").Buttons.Count);
            Assert.AreEqual(4, MacroSet.FromJson("{\"buttons\":[]}").Buttons.Count);
        }
    }
}
