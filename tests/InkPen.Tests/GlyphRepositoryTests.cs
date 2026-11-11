using System;
using System.Collections.Generic;
using System.IO;
using InkPen.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace InkPen.Tests
{
    [TestClass]
    public class GlyphRepositoryTests
    {
        private string _dir;
        private GlyphRepository _repo;

        [TestInitialize]
        public void Setup()
        {
            _dir = TestSupport.CreateTempFontDir();
            _repo = new GlyphRepository();
            _repo.Load(_dir, false, "latin.zip");
        }

        [TestCleanup]
        public void Teardown()
        {
            if (_repo != null) _repo.Dispose();
            TestSupport.DeleteDir(_dir);
        }

        [TestMethod]
        public void Load_ReadsCodepointNamedEntries()
        {
            Assert.AreEqual(2, _repo.Count, "临时字库含 A 与我 两个字形");
            CollectionAssert.Contains(_repo.ChainNames, "mmah.zip");
            Assert.IsTrue(_repo.PackInfo().IndexOf("mmah.zip=2", StringComparison.Ordinal) >= 0,
                          "PackInfo 应显示包内字形数");
        }

        [TestMethod]
        public void Get_DecodesBinaryStrokes()
        {
            string src;
            StrokeGlyph g = _repo.Get(TestSupport.ChA, out src);

            Assert.IsNotNull(g, "A 应能取出");
            Assert.AreEqual("mmah.zip", src);
            Assert.AreEqual(2, g.Strokes.Count);
            Assert.AreEqual(1024.0, g.Advance, 1e-6);
            Assert.AreEqual(2, g.Strokes[0].Points.Count);
            Assert.AreEqual(0.0, g.Strokes[0].Points[0].X, 1e-6);
            Assert.AreEqual(1024.0, g.Strokes[0].Points[1].Y, 1e-6);
        }

        [TestMethod]
        public void Get_CachesSameInstance()
        {
            string s1, s2;
            StrokeGlyph a = _repo.Get(TestSupport.ChA, out s1);
            StrokeGlyph b = _repo.Get(TestSupport.ChA, out s2);
            Assert.AreSame(a, b, "同一字形应走缓存，避免重复解压");
        }

        [TestMethod]
        public void Get_UnknownChar_ReturnsNullAndEmptySource()
        {
            string src;
            StrokeGlyph g = _repo.Get('Z', out src);
            Assert.IsNull(g);
            Assert.AreEqual("", src);
        }

        [TestMethod]
        public void SetEnabled_ChangesCoverageAndLookup()
        {
            Assert.IsTrue(_repo.SetEnabled("mmah.zip", false));

            string src;
            Assert.AreEqual(0, _repo.Count, "禁用后有效覆盖数必须同步下降");
            Assert.IsNull(_repo.Get(TestSupport.ChA, out src));
            Assert.IsTrue(_repo.PackInfo().IndexOf("(禁用)", StringComparison.Ordinal) >= 0);

            Assert.IsTrue(_repo.SetEnabled("mmah.zip", true));
            Assert.AreEqual(2, _repo.Count, "重新启用后应恢复");
        }

        [TestMethod]
        public void SetEnabled_UnknownPack_ReturnsFalse()
        {
            Assert.IsFalse(_repo.SetEnabled("nope.zip", false));
        }

        [TestMethod]
        public void MissingPacks_AreReportedButDoNotThrow()
        {
            // 临时目录只放了 mmah.zip，其余应在 LoadErrors 里点名
            Assert.IsTrue(_repo.LoadErrors.Count > 0);
            bool mentionsPinyin = false;
            foreach (string e in _repo.LoadErrors)
                if (e.IndexOf("pinyin.zip", StringComparison.Ordinal) >= 0) mentionsPinyin = true;
            Assert.IsTrue(mentionsPinyin, "缺失的 pinyin.zip 应被记录");
        }

        [TestMethod]
        public void Load_Twice_DoesNotLeakOldPacks()
        {
            _repo.Load(_dir, false, "latin.zip");
            Assert.AreEqual(2, _repo.Count, "重复 Load 不应叠加字库");
        }

        [TestMethod]
        public void RealFontData_CoversChineseAndLatin()
        {
            string root = TestSupport.ProjectRoot();
            if (root == null) Assert.Inconclusive("未找到 data\\glyphs，跳过真实字库测试");

            GlyphRepository fonts = new GlyphRepository();
            try
            {
                fonts.Load(Path.Combine(root, "data", "glyphs"), false, "latin.zip");
                Assert.IsTrue(fonts.Count > 20000, "开放字库并集应超过两万码点，实际 " + fonts.Count);

                string src;
                Assert.IsNotNull(fonts.Get('\u6211', out src), "汉字「我」必须存在");
                Assert.IsNotNull(fonts.Get('A', out src), "拉丁 A 必须存在");
                Assert.IsNotNull(fonts.Get('1', out src), "数字 1 必须存在");
                Assert.IsNotNull(fonts.Get('\u0101', out src), "带调拼音 ā 必须存在");
                Assert.IsNotNull(fonts.Get('\u3002', out src), "中文句号必须存在");
            }
            finally { fonts.Dispose(); }
        }
    }
}
