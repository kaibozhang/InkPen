using System;
using System.Collections.Generic;
using System.IO;
using InkPen.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace InkPen.Tests
{
    /// <summary>
    /// 2026-11-12 全量测试（五路并行审计）发现并修复的缺陷的回归用例。
    ///
    /// 每条用例都对应一个真实缺陷，断言的设计原则是：
    /// **把修复改回坏实现，这条断言必须变红。** 只写「不出异常」不算回归。
    ///
    /// 注意：默认测试字库只有「A」「我」两个字。涉及标点避头尾的用例必须自己造字库，
    /// 否则标点全都落进缺字分支、只推进 x 不产生字形，断言遍历 Glyphs 时为真空转——
    /// 看上去通过，实际什么都没验（第一版就是这么漏的，注入验证才发现）。
    /// </summary>
    [TestClass]
    public class AuditRegressionTests
    {
        // ---------- B1：抬笔不参与高度补偿 ----------

        private static Job JobWithFlatMap(double z)
        {
            Job j = TestSupport.SingleGlyphJob();
            HeightMap m = new HeightMap();
            m.Reset(2, 2);
            m.MinX = 0; m.MinY = 0; m.MaxX = 210; m.MaxY = 297;
            for (int i = 0; i < m.Z.Length; i++) m.Z[i] = z;
            m.Valid = true;
            j.HeightMap = m;
            j.Machine.SafeZ = 2;
            j.Machine.WriteZ = 0;
            return j;
        }

        [TestMethod]
        public void PenUp_TracksTiltedPaper_NotRawSafeZ()
        {
            // 纸面整体比基准高 0.8 mm。抬笔若仍写 SafeZ=2，在纸面最高处只有 1.2 mm 间隙，
            // 叠上任何一点跳动就是在纸上拖着走。正确做法是抬到 SafeZ + MaxZ。
            Job j = JobWithFlatMap(0.8);
            j.Machine.UseHeightMap = true;
            GCodeBuilder.Build(j, false);

            List<string> lifts = new List<string>();
            foreach (GCodeLine g in j.GCode)
                if (g.Text.StartsWith("G0 Z", StringComparison.Ordinal)) lifts.Add(g.Text);

            Assert.IsTrue(lifts.Count > 0, "至少要有一次抬笔");
            foreach (string t in lifts)
                Assert.AreEqual("G0 Z2.8", t, "抬笔高度应为 SafeZ + 纸面最大正偏差");
        }

        [TestMethod]
        public void PenUp_UnchangedWhenHeightMapDisabled()
        {
            Job j = JobWithFlatMap(0.8);
            j.Machine.UseHeightMap = false;
            GCodeBuilder.Build(j, false);

            foreach (GCodeLine g in j.GCode)
                if (g.Text.StartsWith("G0 Z", StringComparison.Ordinal))
                    Assert.AreEqual("G0 Z2", g.Text, "未启用高度图时抬笔必须是原始 SafeZ");
        }

        [TestMethod]
        public void PenUp_NegativeMapDoesNotLowerLift()
        {
            // 纸面全都比基准低时不需要抬高，更不能反过来压低
            Job j = JobWithFlatMap(-0.5);
            j.Machine.UseHeightMap = true;
            GCodeBuilder.Build(j, false);

            foreach (GCodeLine g in j.GCode)
                if (g.Text.StartsWith("G0 Z", StringComparison.Ordinal))
                    Assert.AreEqual("G0 Z2", g.Text, "纸面偏低时抬笔不应改变");
        }

        // ---------- B5：续写前缀顺序 ----------

        [TestMethod]
        public void ResumePrefix_DeclaresUnitsBeforeAnyZMove()
        {
            Job j = TestSupport.SingleGlyphJob();
            GCodeBuilder.Build(j, false);
            Assert.IsTrue(j.GCode.Count > 3, "样例作业要够长才谈得上续写");

            int from = Math.Min(3, j.GCode.Count - 1);
            string s = GCodeBuilder.ToText(j, from);
            int g21 = s.IndexOf("G21", StringComparison.Ordinal);
            int g90 = s.IndexOf("G90", StringComparison.Ordinal);
            int g94 = s.IndexOf("G94", StringComparison.Ordinal);
            int z = s.IndexOf("G0 Z", StringComparison.Ordinal);

            Assert.IsTrue(g21 >= 0 && g90 >= 0 && g94 >= 0, "续写前缀必须恢复单位/距离/进给模式");
            Assert.IsTrue(z > g21 && z > g90 && z > g94,
                "G0 Z 必须排在 G21/G90/G94 之后：否则在 G91/G20 下这一行会变成增量移动或英寸");
        }

        [TestMethod]
        public void ResumePrefix_UsesCompensatedLift()
        {
            Job j = JobWithFlatMap(0.6);
            j.Machine.UseHeightMap = true;
            GCodeBuilder.Build(j, false);

            string s = GCodeBuilder.ToText(j, Math.Min(2, j.GCode.Count - 1));
            Assert.IsTrue(s.Contains("G0 Z2.6"), "续写前缀的抬笔也要跟纸面高度走");
        }

        [TestMethod]
        public void ResumeIntoConnectedStroke_ReAppliesPenDownZ()
        {
            // 续写起点落在「连笔」中间：该行是 G1 X.. Y..，此前笔是落着的。
            // 前缀会先 G0 Z 抬笔，若不再补一条落笔 Z，从这一行到下一条 Z 指令之间
            // 的笔画全部空走——用户看到的是「续写了，但少了一截」。
            Job j = new Job();
            j.Machine.SafeZ = 2; j.Machine.ZFeed = 300; j.Machine.XYFeed = 2000;
            string[] lines = new string[]
            {
                "G21 G90 G94",
                "G0 X10 Y10",
                "G1 Z0.3 F300",
                "G1 X20 Y10 Z0.3",     // ← 续写起点（连笔中）
                "G1 X30 Y10 Z0.3",
            };
            foreach (string t in lines) { GCodeLine g = new GCodeLine(); g.Text = t; j.GCode.Add(g); }

            string s = GCodeBuilder.ToText(j, 3);
            StringAssert.Contains(s, "G1 Z0.3 F300", "续写进连笔段时必须补回落笔 Z");
        }

        [TestMethod]
        public void ResumeAtLiftedPosition_DoesNotForcePenDown()
        {
            // 起点前最近一条 Z 是抬笔：不能凭空落笔，否则会在纸上划一道空程线
            Job j = new Job();
            j.Machine.SafeZ = 2; j.Machine.ZFeed = 300; j.Machine.XYFeed = 2000;
            string[] lines = new string[]
            {
                "G21 G90 G94",
                "G0 Z2",
                "G1 Z0.3 F300",
                "G1 X20 Y10 Z0.3",
                "G0 Z2",                // ← 抬笔
                "G0 X40 Y10",           // ← 续写起点（空程中）
            };
            foreach (string t in lines) { GCodeLine g = new GCodeLine(); g.Text = t; j.GCode.Add(g); }

            string s = GCodeBuilder.ToText(j, 5);
            Assert.IsFalse(s.Contains("G1 Z0.3"), "抬笔状态的续写起点不应被强行落笔");
        }

        // ---------- A1 / A2 / A6：排版越界与跨页错位 ----------

        /// <summary>造一个只含指定字符的临时字库目录（标点必须真的在库里，否则用例空转）。</summary>
        private static string TempDirWith(params char[] chars)
        {
            string dir = Path.Combine(Path.GetTempPath(), "inkpen_audit_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            List<StrokeGlyph> gs = new List<StrokeGlyph>();
            foreach (char c in chars)
                gs.Add(TestSupport.Glyph(c, 1024, new double[] { 0, 0, 512, 1024 }));
            TestSupport.WritePack(Path.Combine(dir, "mmah.zip"), gs);
            return dir;
        }

        private static void WithFonts(string dir, Action<GlyphRepository> body)
        {
            GlyphRepository fonts = new GlyphRepository();
            try
            {
                fonts.Load(dir, false, "latin.zip");
                body(fonts);
            }
            finally
            {
                fonts.Dispose();
            }
        }

        private static Job LayoutJob(string text, double paperW, double paperH, double fontSize)
        {
            Job j = new Job();
            j.Text = text;
            j.Template.Kind = TemplateKind.Ruled;
            j.Template.FontSizeMm = fontSize;
            j.Template.CharGapMm = 0;
            j.Template.LineGapMm = 4;
            j.Template.MarginLeft = 10;
            j.Template.MarginRight = 10;
            j.Template.MarginTop = 10;
            j.Template.MarginBottom = 10;
            j.Machine.PaperWidthMm = paperW;
            j.Machine.PaperHeightMm = paperH;
            j.Machine.Seed = 20261112;
            return j;
        }

        [TestMethod]
        public void HangingPunctuation_CannotWalkOffThePaper()
        {
            // 一行排满（可用宽 40mm / 字号 8mm = 5 字）后再来一串行首禁则字符。
            // 「允许挤在行尾」原本没有上限，连续标点会一直往右挤，最终墨迹落在纸外。
            Job j = LayoutJob("AAAAA" + new string('。', 10), 60, 297, 8);
            string dir = TempDirWith('A', '。');
            try
            {
                WithFonts(dir, fonts =>
                {
                    j.Pages = InkPen.Core.LayoutEngine.Layout(j, fonts);
                    double right = j.Machine.PaperWidthMm - j.Template.MarginRight;

                    int count = 0;
                    foreach (Page p in j.Pages)
                        foreach (PlacedGlyph g in p.Glyphs)
                        {
                            count++;
                            Assert.IsTrue(g.X <= right + j.Template.FontSizeMm * 1.05 + 1e-6,
                                "字形 " + g.Ch + " 的 X=" + g.X.ToString("F2")
                              + " 越过了右边界 " + right.ToString("F2")
                              + "（悬挂标点必须有上限）");
                        }
                    Assert.AreEqual(15, count, "标点必须在字库里，否则这条用例是空转");
                });
            }
            finally { TestSupport.DeleteDir(dir); }
        }

        [TestMethod]
        public void SpacesAtLineStart_CannotPushTextOffThePaper()
        {
            Job j = LayoutJob(new string(' ', 40) + "AB", 60, 297, 8);
            string dir = TempDirWith('A', 'B');
            try
            {
                WithFonts(dir, fonts =>
                {
                    j.Pages = InkPen.Core.LayoutEngine.Layout(j, fonts);
                    double right = j.Machine.PaperWidthMm - j.Template.MarginRight;
                    int count = 0;
                    foreach (Page p in j.Pages)
                        foreach (PlacedGlyph g in p.Glyphs)
                        {
                            count++;
                            Assert.IsTrue(g.X <= right + 1e-6,
                                "行首空格把字推到了纸外：X=" + g.X.ToString("F2"));
                        }
                    Assert.AreEqual(2, count);
                });
            }
            finally { TestSupport.DeleteDir(dir); }
        }

        [TestMethod]
        public void EveryGlyphLiesInsideItsOwnPage()
        {
            // 通用不变量：每个字形的 Y 必须落在它所属页面的版面区域内。
            // 「行尾禁则字符跟着换行」的旧实现先减 y、后判换页、再放字形，
            // 结果那个字被放在上一页页底之下，PageIndex 却还指向上一页。
            string body = "";
            for (int i = 0; i < 200; i++) body += "（A";
            Job j = LayoutJob(body, 60, 60, 8);
            string dir = TempDirWith('A', '（');
            try
            {
                WithFonts(dir, fonts =>
                {
                    j.Pages = InkPen.Core.LayoutEngine.Layout(j, fonts);
                    Assert.IsTrue(j.Pages.Count > 1, "这个用例需要触发换页");

                    int count = 0;
                    foreach (Page p in j.Pages)
                        foreach (PlacedGlyph g in p.Glyphs)
                        {
                            count++;
                            Assert.AreEqual(p.Index, g.PageIndex, "字形的页号必须与它所在的页一致");
                            Assert.IsTrue(g.Y >= -0.001 && g.Y <= p.HeightMm + 0.001,
                                "第 " + p.Index + " 页出现 Y=" + g.Y.ToString("F2")
                              + " 的字形（页高 " + p.HeightMm.ToString("F1") + "）");
                        }
                    Assert.AreEqual(400, count);
                });
            }
            finally { TestSupport.DeleteDir(dir); }
        }

        // ---------- A3 / A4 / A5 / A7：排版几何与码点 ----------

        [TestMethod]
        public void JustifyWithFirstLineIndent_DoesNotOvershootRightMargin()
        {
            // 两端对齐的补量原本按 MarginLeft 算，等于假装本行从左边距起排。
            // 首行有缩进时起点是 MarginLeft+indent，于是整行右端稳定多顶出一个缩进量的距离。
            Job j = LayoutJob(new string('A', 15), 60, 400, 8);
            j.Template.Kind = TemplateKind.Free;
            j.Template.Justify = true;
            j.Template.FirstLineIndentMm = 8;
            string dir = TempDirWith('A');
            try
            {
                WithFonts(dir, fonts =>
                {
                    j.Pages = InkPen.Core.LayoutEngine.Layout(j, fonts);
                    double right = j.Template.MarginLeft
                                 + (j.Machine.PaperWidthMm - j.Template.MarginLeft - j.Template.MarginRight);

                    int firstLine = 0;
                    foreach (PlacedGlyph g in j.Pages[0].Glyphs)
                    {
                        if (g.LineIndex != 0) continue;
                        firstLine++;
                        double glyphRight = g.X + g.AdvanceMm;
                        Assert.IsTrue(glyphRight <= right + 1e-6,
                            "首行两端对齐后字身右缘 " + glyphRight.ToString("F2")
                          + " 越过右边界 " + right.ToString("F2") + "（溢出量应恒为 0）");
                    }
                    Assert.AreEqual(4, firstLine,
                        "首行应有 4 个字（可用宽 40，缩进占掉 8 后余 32 / 字号 8）");
                    Assert.IsTrue(j.Pages[0].Glyphs[0].X >= j.Template.MarginLeft + 8 - 1e-6,
                        "首行第一个字必须带缩进");
                });
            }
            finally { TestSupport.DeleteDir(dir); }
        }

        [TestMethod]
        public void PageBottom_DoesNotWasteAnExtraFontHeight()
        {
            // y 是字身底边，字形占 [y, y+FontSize]。页底判定原本要求 y >= MarginBottom + FontSize，
            // 等于每页底部白扔一整行；横排还因此比竖排少排一行。
            // 尺寸选得刚好卡住边界：修正后可排 17 行，旧实现只能排 16 行 → 多出一页。
            Job j = LayoutJob(string.Join("\n", Repeat("AAAAA", 17)), 60, 220, 8);
            string dir = TempDirWith('A');
            try
            {
                WithFonts(dir, fonts =>
                {
                    j.Pages = InkPen.Core.LayoutEngine.Layout(j, fonts);
                    Assert.AreEqual(1, j.Pages.Count,
                        "17 行必须排进一页（页底不该多扣一个字号）");
                    Assert.AreEqual(85, j.Pages[0].Glyphs.Count);
                    double last = j.Pages[0].Glyphs[j.Pages[0].Glyphs.Count - 1].Y;
                    Assert.IsTrue(last >= j.Template.MarginBottom - 1e-6,
                        "最后一行字底 Y=" + last.ToString("F2") + " 不得压进下边距");
                });
            }
            finally { TestSupport.DeleteDir(dir); }
        }

        [TestMethod]
        public void GridIndent_SnapsToCellBoundary()
        {
            // 方格稿纸的契约是「一字一格」。缩进改变了字形起点，格线却仍从 MarginLeft 起排，
            // 于是首行每个字都跨在格线上。修法是把缩进吸附到整格。
            Job j = LayoutJob(new string('A', 6), 100, 200, 10);
            j.Template.Kind = TemplateKind.Grid;
            j.Template.CharGapMm = 1;
            j.Template.FirstLineIndentMm = 8;          // cell=11 → 应吸附成 11
            j.Machine.JitterPosMm = 0;
            j.Machine.JitterSize = 0;
            j.Machine.BaselineWaveMm = 0;
            string dir = TempDirWith('A');
            try
            {
                WithFonts(dir, fonts =>
                {
                    j.Pages = InkPen.Core.LayoutEngine.Layout(j, fonts);
                    double cell = j.Template.FontSizeMm + j.Template.CharGapMm;
                    int firstLine = 0;
                    foreach (PlacedGlyph g in j.Pages[0].Glyphs)
                    {
                        if (g.LineIndex != 0) continue;
                        firstLine++;
                        double off = g.X - j.Template.MarginLeft;
                        double mod = off - Math.Round(off / cell) * cell;
                        Assert.IsTrue(Math.Abs(mod) < 0.01,
                            "首行字形 X=" + g.X.ToString("F2") + " 偏离格线 " + mod.ToString("F3")
                          + "mm（缩进必须落在整格上）");
                    }
                    Assert.AreEqual(6, firstLine);
                });
            }
            finally { TestSupport.DeleteDir(dir); }
        }

        [TestMethod]
        public void SurrogatePair_IsOneCharNotTwo()
        {
            // 非 BMP 字符在 UTF-16 里占两个 char。按代码单元迭代会把它当两个字：
            // 缺字记两条孤立代理、宽度多占一格。合并成码点后应只记一条、只推进一格。
            const int CP = 0x20000;                 // 扩展 B 区汉字
            Job j = LayoutJob(char.ConvertFromUtf32(CP) + "A", 60, 200, 8);
            j.Template.Kind = TemplateKind.Free;
            j.Machine.JitterPosMm = 0;
            j.Machine.JitterSize = 0;
            j.Machine.BaselineWaveMm = 0;
            string dir = TempDirWith('A');
            try
            {
                WithFonts(dir, fonts =>
                {
                    j.Pages = InkPen.Core.LayoutEngine.Layout(j, fonts);
                    Assert.AreEqual(1, j.MissingChars.Count,
                        "一个码点只该记一条缺字，实际: " + string.Join(",", j.MissingChars));
                    Assert.AreEqual(char.ConvertFromUtf32(CP), j.MissingChars[0],
                        "缺字表里应是完整码点，不应是孤立代理");

                    Assert.AreEqual(1, j.Pages[0].Glyphs.Count);
                    double expect = j.Template.MarginLeft + j.Template.FontSizeMm;   // 缺字只推进一格
                    Assert.AreEqual(expect, j.Pages[0].Glyphs[0].X, 1e-6,
                        "缺字后面那个字的位置说明宽度被多占了一格");
                });
            }
            finally { TestSupport.DeleteDir(dir); }
        }

        private static string Repeat(string s, int n)
        {
            string r = "";
            for (int i = 0; i < n; i++) r += s;
            return r;
        }

        // ---------- B4：小网格被平滑削峰 ----------

        [TestMethod]
        public void Smooth_KeepsSingleAxisTiltOnSmallGrid()
        {
            // 3×2 网格、Y 向真实倾斜 1mm。原来 3×3 均值（边界按实际格数取平均）
            // 会把每个点都变成全图平均 → 起伏 0.000mm，用户以为纸是平的。
            HeightMap hm = new HeightMap();
            hm.Cols = 3; hm.Rows = 2;
            hm.Z = new double[] { 0, 0, 0, 1, 1, 1 };
            hm.Valid = true;
            hm.MinX = 0; hm.MaxX = 100; hm.MinY = 0; hm.MaxY = 100;

            hm.Smooth();

            Assert.AreEqual(0.0, hm.At(0, 0), 1e-9, "低边不应被抬高");
            Assert.AreEqual(1.0, hm.At(0, 100), 1e-9, "高边不应被压低");
            Assert.AreEqual(1.0, hm.MaxZ() - hm.MinZ(), 1e-9, "单向倾斜必须完整保留");
        }

        [TestMethod]
        public void Smooth_StillKillsSingleSpike()
        {
            // 修正不能把平滑本身废掉：孤立毛刺仍要被削下去。
            HeightMap hm = new HeightMap();
            hm.Cols = 3; hm.Rows = 3;
            hm.Z = new double[] { 0, 0, 0, 0, 2.0, 0, 0, 0, 0 };
            hm.Valid = true;
            hm.MinX = 0; hm.MaxX = 100; hm.MinY = 0; hm.MaxY = 100;

            hm.Smooth();

            Assert.AreEqual(2.0 / 9.0, hm.Z[4], 1e-9, "中心毛刺应被邻域均掉");
            Assert.AreEqual(0.0, hm.Z[0], 1e-9, "边界点保持实测值");
        }

        // ---------- C8：迟到的 [PRB:] 被记到下一个点 ----------

        [TestMethod]
        public void LatePrb_IsNotCreditedToTheNextPoint()
        {
            // 第 k 点超时结案后，它那块 G38.2 还在 GRBL 里跑，迟到的 PRB 必然会来。
            // 原来「收到任意 PRB 就当前点结案」，整条链错位一格，最后一点的真实 Z 永远丢失。
            MachineConfig m = new MachineConfig();
            m.SafeZ = 5;
            List<ProbePoint> pts = new List<ProbePoint>();
            ProbePoint a = new ProbePoint(); a.Col = 0; a.Row = 0; a.X = 0; a.Y = 0;
            ProbePoint b = new ProbePoint(); b.Col = 1; b.Row = 0; b.X = 10; b.Y = 10;
            pts.Add(a); pts.Add(b);

            ProbeRunner r = new ProbeRunner();
            r.Send = delegate(string s) { };
            r.TimeoutMs = 1;
            bool finished = false;
            int doneOk = -1;
            List<double> zs = new List<double>();
            r.OnResult = delegate(ProbePoint p, bool ok, double z) { zs.Add(z); };
            r.OnFinished = delegate(int ok, int total) { finished = true; doneOk = ok; };

            r.Start(ProbePlanner.BuildSteps(m, pts, 4));
            System.Threading.Thread.Sleep(5);
            r.Poll(DateTime.UtcNow);                 // 第 0 点超时
            Assert.AreEqual(1, r.Current);
            Assert.IsFalse(finished);

            Assert.IsTrue(r.FeedLine("[PRB:0,0,-1.2:1]"));
            Assert.AreEqual(1, r.Current, "迟到的 PRB 不能把状态再往前推一格");
            Assert.IsFalse(finished, "收到迟到回报就宣布结束，说明最后一个点被吞了");
            Assert.AreEqual(1, zs.Count, "迟到回报不该产生任何新结果");

            Assert.IsTrue(r.FeedLine("[PRB:10.2,9.9,-1.0:1]"));
            Assert.IsFalse(r.Running);
            Assert.IsTrue(finished);
            Assert.AreEqual(2, zs.Count);
            Assert.AreEqual(-1.0, zs[1], 1e-9, "第二个点必须收下自己的真实 Z");
            Assert.AreEqual(1, doneOk, "只有第二个点算成功");
        }

        // ---------- A8：缺字错误重复累积 ----------

        [TestMethod]
        public void Load_DoesNotAccumulateErrorsAcrossReloads()
        {
            string dir = TestSupport.CreateTempFontDir();
            try
            {
                using (GlyphRepository fonts = new GlyphRepository())
                {
                    fonts.Load(dir, false, "latin.zip");
                    int first = fonts.LoadErrors.Count;

                    // 再 Load 两次（切换字库 / 改配置都会触发），错误数不能变多
                    fonts.Load(dir, false, "latin.zip");
                    fonts.Load(dir, false, "latin.zip");
                    Assert.AreEqual(first, fonts.LoadErrors.Count,
                        "重复 Load 不能把同一批错误反复累加");
                }
            }
            finally { TestSupport.DeleteDir(dir); }
        }

        [TestMethod]
        public void Load_MissingPack_ReportedOncePerLoad()
        {
            string empty = TestSupport.CreateTempFontDir();
            try
            {
                using (GlyphRepository fonts = new GlyphRepository())
                {
                    fonts.Load(empty, false, "does-not-exist.zip");
                    int first = fonts.LoadErrors.Count;
                    Assert.IsTrue(first > 0, "整目录缺字库时必须报错");
                    fonts.Load(empty, false, "does-not-exist.zip");
                    Assert.AreEqual(first, fonts.LoadErrors.Count, "第二次 Load 不应叠加同样的条目");
                }
            }
            finally { TestSupport.DeleteDir(empty); }
        }
    }
}
