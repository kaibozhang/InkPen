using System;
using System.Collections.Generic;
using System.IO;
using InkPen.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace InkPen.Tests
{
    [TestClass]
    public class LayoutTests
    {
        private string _dir;
        private GlyphRepository _fonts;

        [TestInitialize]
        public void Setup()
        {
            _dir = TestSupport.CreateTempFontDir();
            _fonts = new GlyphRepository();
            _fonts.Load(_dir, false, "latin.zip");
        }

        [TestCleanup]
        public void Teardown()
        {
            if (_fonts != null) _fonts.Dispose();
            TestSupport.DeleteDir(_dir);
        }

        private static Job NewJob(string text, TemplateKind kind)
        {
            Job j = new Job();
            j.Text = text;
            j.Template.Kind = kind;
            j.Template.FontSizeMm = 10;
            j.Template.CharGapMm = 1;
            j.Template.LineGapMm = 6;
            j.Machine.Seed = 20261110;
            return j;
        }

        [TestMethod]
        public void Horizontal_LaysGlyphsLeftToRight_WithMonotonicCharIndex()
        {
            Job j = NewJob("AAAA", TemplateKind.Ruled);
            j.Pages = InkPen.Core.LayoutEngine.Layout(j, _fonts);

            Assert.AreEqual(1, j.Pages.Count);
            Assert.AreEqual(4, j.Pages[0].Glyphs.Count);
            Assert.AreEqual(0, j.MissingChars.Count);

            double lastX = double.MinValue;
            for (int i = 0; i < j.Pages[0].Glyphs.Count; i++)
            {
                PlacedGlyph g = j.Pages[0].Glyphs[i];
                Assert.AreEqual(i, g.CharIndex, "字符序号必须连续递增（断点续写依赖它）");
                Assert.IsTrue(g.X > lastX, "横排应逐字右移");
                lastX = g.X;
            }
        }

        [TestMethod]
        public void Vertical_LaysGlyphsTopToBottom()
        {
            Job j = NewJob("AAA", TemplateKind.Ruled);
            j.Template.Direction = TextDirection.Vertical;
            j.Pages = InkPen.Core.LayoutEngine.Layout(j, _fonts);

            Assert.AreEqual(1, j.Pages.Count);
            Assert.AreEqual(3, j.Pages[0].Glyphs.Count);

            double prevY = double.MaxValue;
            foreach (PlacedGlyph g in j.Pages[0].Glyphs)
            {
                Assert.IsTrue(g.Y < prevY, "竖排应逐字下移");
                prevY = g.Y;
            }
        }

        [TestMethod]
        public void MissingChar_IsReported_AndDoesNotThrow()
        {
            Job j = NewJob("AZ", TemplateKind.Ruled);
            j.Pages = InkPen.Core.LayoutEngine.Layout(j, _fonts);

            Assert.AreEqual(1, j.Pages[0].Glyphs.Count, "只有 A 有字形");
            CollectionAssert.Contains(j.MissingChars, "Z");
        }

        [TestMethod]
        public void LongText_Paginates_AndEachGlyphKnowsItsPage()
        {
            Job j = NewJob(new string('A', 50), TemplateKind.Ruled);
            j.Machine.PaperWidthMm = 60;
            j.Machine.PaperHeightMm = 60;
            j.Template.MarginLeft = 5; j.Template.MarginRight = 5;
            j.Template.MarginTop = 5; j.Template.MarginBottom = 5;
            j.Pages = InkPen.Core.LayoutEngine.Layout(j, _fonts);

            Assert.IsTrue(j.Pages.Count > 1, "小纸张长文本必须分页，实际 " + j.Pages.Count + " 页");

            int total = 0;
            for (int pi = 0; pi < j.Pages.Count; pi++)
            {
                Page p = j.Pages[pi];
                Assert.AreEqual(pi, p.Index);
                foreach (PlacedGlyph g in p.Glyphs)
                {
                    Assert.AreEqual(pi, g.PageIndex, "字形的页号必须与所在页一致");
                    Assert.IsTrue(g.X >= 0 && g.X <= j.Machine.PaperWidthMm + 1e-6,
                                  "字形 X 越界: " + g.X);
                    Assert.IsTrue(g.Y >= -1e-6 && g.Y <= j.Machine.PaperHeightMm + 1e-6,
                                  "字形 Y 越界: " + g.Y);
                    total++;
                }
            }
            Assert.AreEqual(50, total, "分页不能丢字");
        }

        [TestMethod]
        public void Grid_RuledHasBaselines_FreeHasNone()
        {
            Job ruled = NewJob("AAA", TemplateKind.Ruled);
            ruled.Pages = InkPen.Core.LayoutEngine.Layout(ruled, _fonts);
            Assert.IsTrue(ruled.Pages[0].Grid.Count >= 1, "横线稿纸应有基线");

            Job free = NewJob("AAA", TemplateKind.Free);
            free.Pages = InkPen.Core.LayoutEngine.Layout(free, _fonts);
            Assert.AreEqual(0, free.Pages[0].Grid.Count, "自由排版不应产生格线");
        }

        [TestMethod]
        public void Grid_TianziHasMoreLinesThanSquare()
        {
            Job tian = NewJob("A", TemplateKind.Tianzi);
            tian.Pages = InkPen.Core.LayoutEngine.Layout(tian, _fonts);

            Job grid = NewJob("A", TemplateKind.Grid);
            grid.Pages = InkPen.Core.LayoutEngine.Layout(grid, _fonts);

            Assert.IsTrue(tian.Pages[0].Grid.Count > grid.Pages[0].Grid.Count,
                          "田字格应比方格多出十字辅助线");
        }

        [TestMethod]
        public void SameSeedSameSettings_ProducesIdenticalPositions()
        {
            Job a = NewJob("AAAA", TemplateKind.Ruled);
            a.Machine.JitterPosMm = 0.5;
            a.Machine.JitterSize = 0.05;
            a.Pages = InkPen.Core.LayoutEngine.Layout(a, _fonts);

            Job b = NewJob("AAAA", TemplateKind.Ruled);
            b.Machine.JitterPosMm = 0.5;
            b.Machine.JitterSize = 0.05;
            b.Pages = InkPen.Core.LayoutEngine.Layout(b, _fonts);

            for (int i = 0; i < a.Pages[0].Glyphs.Count; i++)
            {
                Assert.AreEqual(a.Pages[0].Glyphs[i].X, b.Pages[0].Glyphs[i].X, 1e-12);
                Assert.AreEqual(a.Pages[0].Glyphs[i].Y, b.Pages[0].Glyphs[i].Y, 1e-12);
                Assert.AreEqual(a.Pages[0].Glyphs[i].SizeMm, b.Pages[0].Glyphs[i].SizeMm, 1e-12);
            }
        }

        [TestMethod]
        public void FirstLineIndent_ShiftsOnlyFirstLine()
        {
            Job j = NewJob("AA\nAA", TemplateKind.Free);
            j.Template.FirstLineIndentMm = 8;
            j.Pages = InkPen.Core.LayoutEngine.Layout(j, _fonts);

            List<PlacedGlyph> gs = j.Pages[0].Glyphs;
            Assert.AreEqual(4, gs.Count);
            Assert.AreEqual(j.Template.MarginLeft + 8, gs[0].X, 1e-6);
            Assert.AreEqual(j.Template.MarginLeft, gs[2].X, 1e-6, "第二行不应缩进");
        }

        [TestMethod]
        public void CenterMode_MovesLineRight()
        {
            Job plain = NewJob("AAAA", TemplateKind.Free);
            plain.Pages = InkPen.Core.LayoutEngine.Layout(plain, _fonts);

            Job centered = NewJob("AAAA", TemplateKind.Free);
            centered.Template.CenterVertically = true;
            centered.Pages = InkPen.Core.LayoutEngine.Layout(centered, _fonts);

            Assert.IsTrue(centered.Pages[0].Glyphs[0].X > plain.Pages[0].Glyphs[0].X,
                          "居中模式首字应比默认更靠右");
        }

        [TestMethod]
        public void EmptyText_ProducesOneEmptyPage_WithoutThrowing()
        {
            Job j = NewJob("", TemplateKind.Ruled);
            j.Pages = InkPen.Core.LayoutEngine.Layout(j, _fonts);
            Assert.AreEqual(1, j.Pages.Count);
            Assert.AreEqual(0, j.Pages[0].Glyphs.Count);
        }

        [TestMethod]
        public void GlyphSize_ScalesWithFontSize()
        {
            Job j = NewJob("A", TemplateKind.Free);
            j.Template.FontSizeMm = 20;
            j.Pages = InkPen.Core.LayoutEngine.Layout(j, _fonts);
            Assert.AreEqual(20.0, j.Pages[0].Glyphs[0].SizeMm, 1e-6);
        }
    }
}
