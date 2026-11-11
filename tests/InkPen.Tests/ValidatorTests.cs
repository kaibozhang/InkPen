using System;
using System.Collections.Generic;
using InkPen.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace InkPen.Tests
{
    [TestClass]
    public class ValidatorTests
    {
        private static Job JobWith(params string[] lines)
        {
            Job j = new Job();
            foreach (string l in lines)
            {
                GCodeLine g = new GCodeLine();
                g.Text = l;
                j.GCode.Add(g);
            }
            return j;
        }

        private static bool AnyContains(List<string> list, string part)
        {
            foreach (string s in list) if (s.IndexOf(part, StringComparison.Ordinal) >= 0) return true;
            return false;
        }

        [TestMethod]
        public void EmptyGCode_IsAnError()
        {
            GCodeValidator v = new GCodeValidator();
            v.Validate(new Job());
            Assert.IsTrue(AnyContains(v.Errors, "GCode 为空"));
        }

        [TestMethod]
        public void ValidProgram_HasNoErrors()
        {
            Job j = JobWith("G21", "G90", "F1500", "G0 Z5", "G0 X10 Y10",
                            "G1 Z0 F300", "G1 X20 Y20 F1500", "G0 Z5");
            GCodeValidator v = new GCodeValidator();
            v.Validate(j);
            Assert.AreEqual(0, v.Errors.Count, string.Join(" | ", v.Errors.ToArray()));
        }

        [TestMethod]
        public void MissingZ_IsReported()
        {
            Job j = JobWith("G21", "G0 X10 Y10");
            GCodeValidator v = new GCodeValidator();
            v.Validate(j);
            Assert.IsTrue(AnyContains(v.Errors, "Z 抬落笔"), "没有 Z 指令必须报错");
        }

        [TestMethod]
        public void LineOver80BytesAfterSpaceRemoval_IsAnError()
        {
            // "G1 X" + 79 个数字 = 83 字符，去掉空格后 82 字节 > GRBL 单行缓冲 80
            string longLine = "G1 X" + new string('1', 79);
            Job j = JobWith("G0 Z5", longLine);
            GCodeValidator v = new GCodeValidator();
            v.Validate(j);
            Assert.IsTrue(AnyContains(v.Errors, "80"), "应报单行缓冲超限");
        }

        [TestMethod]
        public void XOutOfPaper_IsReported()
        {
            Job j = JobWith("G0 Z5", "G1 X900 Y10 F1000");
            GCodeValidator v = new GCodeValidator();
            v.Validate(j);
            Assert.IsTrue(AnyContains(v.Errors, "X 越界"));
        }

        [TestMethod]
        public void YOutOfPaper_IsReported()
        {
            Job j = JobWith("G0 Z5", "G1 X10 Y-500 F1000");
            GCodeValidator v = new GCodeValidator();
            v.Validate(j);
            Assert.IsTrue(AnyContains(v.Errors, "Y 越界"));
        }

        [TestMethod]
        public void NegativeCoordinatesWithinTolerance_AreAllowed()
        {
            Job j = JobWith("G0 Z5", "G1 X-0.5 Y-0.5 F1000");
            GCodeValidator v = new GCodeValidator();
            v.Validate(j);
            Assert.AreEqual(0, v.Errors.Count, "1mm 容差内的负坐标应放行");
        }

        [TestMethod]
        public void SafeZNotAboveWriteZ_IsAWarning()
        {
            Job j = JobWith("G0 Z5", "G1 Z0 F300", "G1 X10 Y10 F1000");
            j.Machine.SafeZ = 0;
            j.Machine.WriteZ = 0;
            GCodeValidator v = new GCodeValidator();
            v.Validate(j);
            Assert.AreEqual(0, v.Errors.Count);
            Assert.IsTrue(AnyContains(v.Warnings, "撞笔"), "安全 Z 等于书写 Z 应告警");
        }

        [TestMethod]
        public void NonPositiveFeed_IsAnError()
        {
            Job j = JobWith("G0 Z5", "G1 X10 Y10 F0");
            j.Machine.XYFeed = 0;
            GCodeValidator v = new GCodeValidator();
            v.Validate(j);
            Assert.IsTrue(AnyContains(v.Errors, "进给率"));
        }

        [TestMethod]
        public void Validate_IsIdempotent_AndClearsPreviousState()
        {
            GCodeValidator v = new GCodeValidator();
            v.Validate(JobWith("G21"));
            Assert.IsTrue(v.Errors.Count > 0);

            v.Validate(JobWith("G21", "G90", "F1500", "G0 Z5", "G1 X10 Y10"));
            Assert.AreEqual(0, v.Errors.Count, "二次校验应清空上次结果");
        }
    }
}
