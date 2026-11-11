using System;
using System.Collections.Generic;
using InkPen.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace InkPen.Tests
{
    [TestClass]
    public class GCodeTests
    {
        private static bool HasLine(Job j, string exact)
        {
            foreach (GCodeLine g in j.GCode) if (g.Text == exact) return true;
            return false;
        }

        private static bool AnyContains(Job j, string part)
        {
            foreach (GCodeLine g in j.GCode) if (g.Text.IndexOf(part, StringComparison.Ordinal) >= 0) return true;
            return false;
        }

        [TestMethod]
        public void Build_EmitsStartEndMarkers_AndZPenMoves()
        {
            Job j = TestSupport.SingleGlyphJob();
            GCodeBuilder.Build(j, false);

            Assert.IsTrue(j.GCode.Count > 10);
            Assert.IsTrue(HasLine(j, "(INKPEN JOB START)"));
            Assert.IsTrue(HasLine(j, "(INKPEN JOB END)"));
            Assert.IsTrue(HasLine(j, "G21"), "应输出公制设置");
            Assert.IsTrue(HasLine(j, "G90"), "应输出绝对坐标设置");
            Assert.IsTrue(AnyContains(j, "G1 Z0 F300"), "落笔指令缺失");
            Assert.IsTrue(AnyContains(j, "G0 Z5"), "抬笔指令缺失");
            Assert.IsTrue(AnyContains(j, "G4 P0.15"), "落笔延时缺失");
        }

        [TestMethod]
        public void Build_WritesResumeMetadata()
        {
            Job j = TestSupport.SingleGlyphJob();
            GCodeBuilder.Build(j, false);

            Assert.IsTrue(AnyContains(j, "(CH:65;PG:0;LN:0;CI:0;SI:0)"),
                          "第一笔必须带 (CH;PG;LN;CI;SI) 续写元数据");
            Assert.IsTrue(AnyContains(j, "(CH:65;PG:0;LN:0;CI:0;SI:1)"),
                          "第二笔的笔画序号应为 1");
        }

        [TestMethod]
        public void Build_ProducesOnePathPerStroke()
        {
            Job j = TestSupport.SingleGlyphJob();
            GCodeBuilder.Build(j, false);
            Assert.AreEqual(2, j.Paths.Count, "字形两笔应对应两条仿真路径");
            foreach (WritePath p in j.Paths) Assert.IsTrue(p.Points.Count >= 2);
        }

        [TestMethod]
        public void Build_AppliesOriginCornerToMachineCoordinates()
        {
            Job j = TestSupport.SingleGlyphJob();
            j.Machine.Origin = OriginCorner.TopLeft;
            GCodeBuilder.Build(j, false);

            // 纸面 (20,200) → 机器 (20, 297-200=97)
            Assert.IsTrue(HasLine(j, "G0 X20 Y97"),
                          "左上原点时 Y 必须翻转，实际首条快移缺失");
        }

        [TestMethod]
        public void Build_IncludesGridOnlyWhenRequested()
        {
            Job j = TestSupport.SingleGlyphJob();
            j.Template.DrawGrid = true;
            j.Pages[0].Grid.Add(new GridLine(15, 100, 195, 100));

            GCodeBuilder.Build(j, false);
            int withoutGrid = j.Paths.Count;

            GCodeBuilder.Build(j, true);
            int withGrid = j.Paths.Count;

            Assert.AreEqual(withoutGrid + 1, withGrid, "includeGrid=true 时应多出一条格线路径");
        }

        [TestMethod]
        public void Build_IsDeterministic()
        {
            Job a = TestSupport.SingleGlyphJob();
            a.Machine.JitterStrokeMm = 0.3;
            GCodeBuilder.Build(a, false);
            string ta = GCodeBuilder.ToText(a, 0);

            Job b = TestSupport.SingleGlyphJob();
            b.Machine.JitterStrokeMm = 0.3;
            GCodeBuilder.Build(b, false);
            string tb = GCodeBuilder.ToText(b, 0);

            Assert.AreEqual(ta, tb, "同种子同参数必须逐字节一致");
        }

        [TestMethod]
        public void ToText_ResumePrefix_RestoresModalState()
        {
            Job j = TestSupport.SingleGlyphJob();
            GCodeBuilder.Build(j, false);

            int line = GCodeBuilder.FindResumeLine(j, 1);
            Assert.IsTrue(line > 0, "字符序号 1 应能找到续写行");

            string text = GCodeBuilder.ToText(j, line);
            Assert.IsTrue(text.StartsWith("(RESUME FROM LINE"), "续写应带来源标记");
            Assert.IsTrue(text.IndexOf("G21") >= 0, "续写必须恢复公制");
            Assert.IsTrue(text.IndexOf("G90") >= 0, "续写必须恢复绝对坐标");
            Assert.IsTrue(text.IndexOf("G94") >= 0, "续写必须恢复进给模式");
            Assert.IsTrue(text.IndexOf("G0 Z5") >= 0, "续写前必须先抬笔");
            Assert.IsTrue(text.Length < GCodeBuilder.ToText(j, 0).Length, "续写文本应短于全文");
        }

        [TestMethod]
        public void FindResumeLine_ZeroReturnsHead_BeyondEndReturnsTail()
        {
            Job j = TestSupport.SingleGlyphJob();
            GCodeBuilder.Build(j, false);

            Assert.AreEqual(0, GCodeBuilder.FindResumeLine(j, 0));
            Assert.AreEqual(j.GCode.Count, GCodeBuilder.FindResumeLine(j, 9999),
                            "序号越界应返回末尾，而不是从头重写整篇");
        }

        [TestMethod]
        public void Build_ResultPassesValidator()
        {
            Job j = TestSupport.SingleGlyphJob();
            GCodeBuilder.Build(j, false);

            GCodeValidator v = new GCodeValidator();
            v.Validate(j);

            Assert.AreEqual(0, v.Errors.Count,
                "生成的 GCode 不应有错误：" + string.Join(" | ", v.Errors.ToArray()));
        }

        [TestMethod]
        public void Build_EmptyJob_StillProducesHeaderAndEnd()
        {
            Job j = new Job();
            j.Text = "";
            GCodeBuilder.Build(j, false);
            Assert.IsTrue(j.GCode.Count > 0);
            Assert.IsTrue(HasLine(j, "(INKPEN JOB END)"));
        }

        [TestMethod]
        public void Build_StartEndTemplates_AreHonoured()
        {
            Job j = TestSupport.SingleGlyphJob();
            j.Machine.StartGCode = "G21\nG90";
            j.Machine.EndGCode = "M5\nG0 X0 Y0";
            GCodeBuilder.Build(j, false);

            Assert.IsTrue(HasLine(j, "M5"), "自定义尾模板应写入");
            Assert.AreEqual("G0 X0 Y0", j.GCode[j.GCode.Count - 1].Text);
        }
    }
}
