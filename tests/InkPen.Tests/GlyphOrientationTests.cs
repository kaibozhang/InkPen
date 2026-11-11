using System;
using System.Collections.Generic;
using System.IO;
using InkPen.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace InkPen.Tests
{
    /// <summary>
    /// 字库垂直方向契约。
    ///
    /// 背景：tools/import_gfont.py 曾漏掉 Y 镜像，把整包 local.zip 生成成上下颠倒的，
    /// 现象是"每个字都转了 180°"。这类缺陷不会让任何单元测试失败——字形数据结构完好、
    /// 笔画数正确、坐标都在 1024 网格内，只是整体镜像了。所以必须用几何不变量来判：
    /// 同类字库对同一批字的"垂直重心"应当正相关，镜像会让相关系数变负。
    /// </summary>
    [TestClass]
    public class GlyphOrientationTests
    {
        private const string CommonChars =
            "的一是在不了有和人这中大为上个国我以要他时来用们生到作地于出就分对成会可主发年动同工" +
            "也能下过子说产种面而方后多定行学法所民得经十三之进着等部度家电力里如水化高自二理起小";

        private static string GlyphDir()
        {
            string root = TestSupport.ProjectRoot();
            Assert.IsNotNull(root, "找不到项目根目录（应含 data/glyphs）");
            string dir = Path.Combine(root, "data");
            dir = Path.Combine(dir, "glyphs");
            Assert.IsTrue(Directory.Exists(dir), "字库目录不存在: " + dir);
            return dir;
        }

        /// <summary>只启用指定字库，避免回退链把别的包的字混进来。</summary>
        private static GlyphRepository OpenOnly(string pack)
        {
            GlyphRepository repo = new GlyphRepository();
            repo.Load(GlyphDir(), true, "latin.zip");
            foreach (string n in repo.ChainNames) repo.SetEnabled(n, n == pack);
            return repo;
        }

        /// <summary>字形垂直重心相对位置：0 = 字形底部，1 = 字形顶部。</summary>
        internal static double CenterY(StrokeGlyph g)
        {
            if (g == null || g.Strokes.Count == 0) return double.NaN;
            double lo = double.MaxValue, hi = double.MinValue, sum = 0;
            int n = 0;
            foreach (Stroke s in g.Strokes)
                foreach (Pt p in s.Points)
                {
                    if (p.Y < lo) lo = p.Y;
                    if (p.Y > hi) hi = p.Y;
                    sum += p.Y;
                    n++;
                }
            if (n == 0 || hi - lo < 1e-6) return double.NaN;
            return (sum / n - lo) / (hi - lo);
        }

        [TestMethod]
        public void LocalPack_SharesVerticalDirectionWithMmah()
        {
            // local.zip 是专有字库（见 NOTICE.md §4：本机奎享 .gfont 导入，授权不允许再分发），
            // 因此它被 .gitignore 排除，公开检出后通常不存在。
            // 这里必须跳过而不是失败——否则任何克隆本仓库的人都看到一条永远红的测试，
            // 久而久之就没人看测试结果了。跳过时会显式说明原因。
            if (!File.Exists(Path.Combine(GlyphDir(), "local.zip")))
                Assert.Inconclusive(
                    "跳过：未找到 data/glyphs/local.zip。该包为专有字库，不随仓库分发；"
                    + "本机如需运行此用例，用 tools/import_gfont.py 从自己的 .gfont 生成。");

            GlyphRepository mmah = OpenOnly("mmah.zip");
            GlyphRepository local = OpenOnly("local.zip");
            try
            {
                List<double> a = new List<double>();
                List<double> b = new List<double>();
                foreach (char ch in CommonChars)
                {
                    string src;
                    double ya = CenterY(mmah.Get(ch, out src));
                    double yb = CenterY(local.Get(ch, out src));
                    if (double.IsNaN(ya) || double.IsNaN(yb)) continue;
                    a.Add(ya); b.Add(yb);
                }

                Assert.IsTrue(a.Count >= 30, "共同字形太少，无法判定方向: " + a.Count);

                double ma = 0, mb = 0;
                for (int i = 0; i < a.Count; i++) { ma += a[i]; mb += b[i]; }
                ma /= a.Count; mb /= b.Count;

                double num = 0, da = 0, db = 0;
                for (int i = 0; i < a.Count; i++)
                {
                    num += (a[i] - ma) * (b[i] - mb);
                    da += (a[i] - ma) * (a[i] - ma);
                    db += (b[i] - mb) * (b[i] - mb);
                }
                double corr = num / Math.Sqrt(da * db);

                // 同向应显著正相关（实测 +0.71）；整包镜像会变成 -0.71。
                Assert.IsTrue(corr > 0.3,
                    "local.zip 与 mmah.zip 的垂直重心相关系数 = " + corr.ToString("F3")
                    + "，说明 local.zip 整包上下翻转了（Y 未镜像）。"
                    + " 检查 tools/import_gfont.py 的坐标变换。");
            }
            finally
            {
                mmah.Dispose();
                local.Dispose();
            }
        }

        [TestMethod]
        public void Mmah_Three_UpperStrokeIsShorterThanLower()
        {
            // "三" 是天然的上下判据：上横短、下横最长，镜像后长度关系直接反过来。
            GlyphRepository repo = OpenOnly("mmah.zip");
            try
            {
                string src;
                StrokeGlyph g = repo.Get('三', out src);
                Assert.IsNotNull(g, "mmah.zip 缺少「三」");
                Assert.IsTrue(g.Strokes.Count >= 3, "「三」应有至少 3 笔，实际 " + g.Strokes.Count);

                double topY = double.MinValue, botY = double.MaxValue;
                double topLen = 0, botLen = 0;
                foreach (Stroke s in g.Strokes)
                {
                    double lo = double.MaxValue, hi = double.MinValue;
                    double xlo = double.MaxValue, xhi = double.MinValue;
                    foreach (Pt p in s.Points)
                    {
                        if (p.Y < lo) lo = p.Y;
                        if (p.Y > hi) hi = p.Y;
                        if (p.X < xlo) xlo = p.X;
                        if (p.X > xhi) xhi = p.X;
                    }
                    double mid = (lo + hi) / 2.0, len = xhi - xlo;
                    if (mid > topY) { topY = mid; topLen = len; }
                    if (mid < botY) { botY = mid; botLen = len; }
                }

                Assert.IsTrue(botLen > topLen,
                    "「三」下横应比上横长（下 " + botLen.ToString("F0")
                    + " vs 上 " + topLen.ToString("F0") + "），否则字库上下颠倒");
            }
            finally
            {
                repo.Dispose();
            }
        }

        [TestMethod]
        public void Latin_P_LoopSitsAtTop()
        {
            // "P" 的圆环在上半部；镜像后会掉到下半部。X 不变，只测 Y。
            GlyphRepository repo = OpenOnly("latin.zip");
            try
            {
                string src;
                StrokeGlyph g = repo.Get('P', out src);
                Assert.IsNotNull(g, "latin.zip 缺少「P」");

                double lo = double.MaxValue, hi = double.MinValue, xlo = double.MaxValue, xhi = double.MinValue;
                foreach (Stroke s in g.Strokes)
                    foreach (Pt p in s.Points)
                    {
                        if (p.Y < lo) lo = p.Y;
                        if (p.Y > hi) hi = p.Y;
                        if (p.X < xlo) xlo = p.X;
                        if (p.X > xhi) xhi = p.X;
                    }
                double xmid = (xlo + xhi) / 2.0;

                double sum = 0; int n = 0;
                foreach (Stroke s in g.Strokes)
                    foreach (Pt p in s.Points)
                        if (p.X > xmid) { sum += p.Y; n++; }

                Assert.IsTrue(n > 0, "「P」没有右半部分点，无法判定");
                double rel = (sum / n - lo) / (hi - lo);
                Assert.IsTrue(rel > 0.5,
                    "「P」的圆环应在字形上半部，实测相对位置 " + rel.ToString("F2")
                    + "（小于 0.5 说明字形上下颠倒）");
            }
            finally
            {
                repo.Dispose();
            }
        }
    }
}
