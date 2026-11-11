using System;
using InkPen.Core;
using InkPen.UI;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace InkPen.Tests
{
    /// <summary>
    /// UI 契约测试：下拉框序号与枚举语义的映射。
    /// 早期用 (TemplateKind)SelectedIndex 直接强转，枚举顺序是 Free/Ruled/Grid/Tianzi/Pinyin4，
    /// 而界面顺序是 横线/方格/田字/拼音/自由 → 选「横线稿纸」实际生成「自由排版」。
    /// </summary>
    [TestClass]
    public class UiContractTests
    {
        [TestMethod]
        public void TemplateCombo_MapsToCorrectKind()
        {
            Assert.AreEqual(TemplateKind.Ruled, MainForm.TemplateKindFromIndex(0), "第 1 项应是横线稿纸");
            Assert.AreEqual(TemplateKind.Grid, MainForm.TemplateKindFromIndex(1), "第 2 项应是方格稿纸");
            Assert.AreEqual(TemplateKind.Tianzi, MainForm.TemplateKindFromIndex(2), "第 3 项应是田字格");
            Assert.AreEqual(TemplateKind.Pinyin4, MainForm.TemplateKindFromIndex(3), "第 4 项应是拼音四线三格");
            Assert.AreEqual(TemplateKind.Free, MainForm.TemplateKindFromIndex(4), "第 5 项应是自由排版");
        }

        [TestMethod]
        public void TemplateCombo_RoundTrips()
        {
            foreach (TemplateKind k in new TemplateKind[]
                     { TemplateKind.Free, TemplateKind.Ruled, TemplateKind.Grid,
                       TemplateKind.Tianzi, TemplateKind.Pinyin4 })
            {
                int idx = MainForm.IndexFromTemplateKind(k);
                Assert.AreEqual(k, MainForm.TemplateKindFromIndex(idx),
                                "往返映射必须一致: " + k);
            }
        }

        [TestMethod]
        public void TemplateCombo_OutOfRange_FallsBackSafely()
        {
            Assert.AreEqual(TemplateKind.Ruled, MainForm.TemplateKindFromIndex(-1));
            Assert.AreEqual(TemplateKind.Ruled, MainForm.TemplateKindFromIndex(99));
        }

        /// <summary>$ 参数表是给人看的：标定 Z 方向要看 $102，标定行程要看 $130-132 与 $110-112。</summary>
        [TestMethod]
        public void SettingName_CoversCalibrationCriticalIds()
        {
            string[] ids = new string[] { "$100", "$101", "$102", "$110", "$111", "$112",
                                          "$120", "$121", "$122", "$130", "$131", "$132" };
            foreach (string s in ids)
            {
                int id = int.Parse(s.Substring(1), System.Globalization.CultureInfo.InvariantCulture);
                Assert.AreNotEqual("", MainForm.SettingName(id), s + " 必须有中文说明，否则标定时看不懂");
            }
            Assert.AreEqual("Z 轴 steps/mm", MainForm.SettingName(102), "Z 轴标定最关键的参数不能标错");
            Assert.AreEqual("", MainForm.SettingName(9988), "未知参数应返回空串而不是猜");
            Assert.AreEqual("", MainForm.SettingName(-1));
        }

        /// <summary>「无内容可写」判定：缺字不能算已排版，null 不能抛异常。</summary>
        [TestMethod]
        public void CountPlacedChars_CountsOnlyLaidOutGlyphs()
        {
            Assert.AreEqual(0, MainForm.CountPlacedChars(null), "null 作业必须安全返回 0");

            Job empty = new Job();
            Assert.AreEqual(0, MainForm.CountPlacedChars(empty), "无页时应为 0");

            Job one = TestSupport.SingleGlyphJob();
            Assert.AreEqual(1, MainForm.CountPlacedChars(one));

            one.Pages.Add(new Page());
            Assert.AreEqual(1, MainForm.CountPlacedChars(one), "空页不应虚增字数");
        }
    }
}
