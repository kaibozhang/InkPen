using System;
using System.Collections.Generic;
using System.IO;
using InkPen.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace InkPen.Tests
{
    /// <summary>
    /// 字库方向契约（X 与 Y 双向）。
    ///
    /// 背景：tools/import_gfont.py 曾漏掉 Y 镜像，把整包 local.zip 生成成上下颠倒的，
    /// 现象是"每个字都转了 180°"。这类缺陷不会让任何结构型测试失败——字形数据结构完好、
    /// 笔画数正确、坐标都在 1024 网格内，只是整体镜像了。
    ///
    /// 所以本文件只做一件事：用**与字体风格无关的几何不变量**判方向。
    /// 判据设计原则（踩过坑，见 docs/testing_pitfalls.md）：
    ///   1. 不能用"某个字看起来对不对"——高度依赖字体风格，连笔写法会让判据直接失效；
    ///   2. 不能用低分辨率截图肉眼判断——同一个误判会反复发生；
    ///   3. 必须可量化：重心、比例、相关系数，且镜像后数值必须显著反转。
    ///
    /// 每一条判据都要能回答一个问题："如果整包被镜像了，这个数会变成多少？"
    /// 答不上来的判据不算判据。
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

        /// <summary>
        /// local.zip 是专有字库（NOTICE.md §4：本机奎享 .gfont 导入，授权不允许再分发），
        /// 已被 .gitignore 排除，公开检出后通常不存在。
        /// 相关用例必须**跳过**而不是失败——否则每个克隆者都会看到一条永远红的测试，
        /// 久了就没人看测试结果，那比没有测试更糟。
        /// </summary>
        private static void RequireLocalPack()
        {
            if (!File.Exists(Path.Combine(GlyphDir(), "local.zip")))
                Assert.Inconclusive(
                    "跳过：未找到 data/glyphs/local.zip。该包为专有字库，不随仓库分发；"
                    + "本机如需运行此用例，用 tools/import_gfont.py 从自己的 .gfont 生成。");
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

        /// <summary>字形水平重心相对位置：0 = 字形左缘，1 = 字形右缘。</summary>
        internal static double CenterX(StrokeGlyph g)
        {
            if (g == null || g.Strokes.Count == 0) return double.NaN;
            double lo = double.MaxValue, hi = double.MinValue, sum = 0;
            int n = 0;
            foreach (Stroke s in g.Strokes)
                foreach (Pt p in s.Points)
                {
                    if (p.X < lo) lo = p.X;
                    if (p.X > hi) hi = p.X;
                    sum += p.X;
                    n++;
                }
            if (n == 0 || hi - lo < 1e-6) return double.NaN;
            return (sum / n - lo) / (hi - lo);
        }

        /// <summary>
        /// 计算 local.zip 与 mmah.zip 在某一维重心上的相关系数。
        /// 两个同源中文字库对同一批字的重心分布应当正相关；整包镜像会让相关系数变负。
        /// </summary>
        private static double CorrWithMmah(Func<StrokeGlyph, double> metric, out int samples)
        {
            GlyphRepository mmah = OpenOnly("mmah.zip");
            GlyphRepository local = OpenOnly("local.zip");
            try
            {
                List<double> a = new List<double>();
                List<double> b = new List<double>();
                string src;
                foreach (char ch in CommonChars)
                {
                    double va = metric(mmah.Get(ch, out src));
                    double vb = metric(local.Get(ch, out src));
                    if (double.IsNaN(va) || double.IsNaN(vb)) continue;
                    a.Add(va); b.Add(vb);
                }
                samples = a.Count;
                if (samples < 30) return double.NaN;

                double ma = 0, mb = 0;
                for (int i = 0; i < samples; i++) { ma += a[i]; mb += b[i]; }
                ma /= samples; mb /= samples;

                double num = 0, da = 0, db = 0;
                for (int i = 0; i < samples; i++)
                {
                    num += (a[i] - ma) * (b[i] - mb);
                    da += (a[i] - ma) * (a[i] - ma);
                    db += (b[i] - mb) * (b[i] - mb);
                }
                if (da <= 0 || db <= 0) return double.NaN;
                return num / Math.Sqrt(da * db);
            }
            finally
            {
                mmah.Dispose();
                local.Dispose();
            }
        }

        // ---------------------------------------------------------------
        // Y 方向（上下颠倒）
        // ---------------------------------------------------------------

        [TestMethod]
        public void LocalPack_SharesVerticalDirectionWithMmah()
        {
            RequireLocalPack();
            int n;
            double corr = CorrWithMmah(CenterY, out n);
            Assert.IsFalse(double.IsNaN(corr), "共同字形太少，无法判定垂直方向: " + n);

            // 同向应显著正相关（实测 +0.73）；整包上下镜像会变成 -0.73。
            Assert.IsTrue(corr > 0.3,
                "local.zip 与 mmah.zip 的**垂直**重心相关系数 = " + corr.ToString("F3")
                + "，说明 local.zip 整包上下翻转了（Y 未镜像）。"
                + " 检查 tools/import_gfont.py 的坐标变换。");
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

        // ---------------------------------------------------------------
        // X 方向（左右镜像）
        //
        // 这一组是复盘时补的：原先只守了 Y，X 方向完全没人看。
        // 导入脚本若把 x' = W - x 写错（或漏写），字形会左右翻转——
        // 汉字里"我""以""这"这类不对称字会明显错，但所有结构断言照样通过。
        // ---------------------------------------------------------------

        [TestMethod]
        public void LocalPack_SharesHorizontalDirectionWithMmah()
        {
            RequireLocalPack();
            int n;
            double corr = CorrWithMmah(CenterX, out n);
            Assert.IsFalse(double.IsNaN(corr), "共同字形太少，无法判定水平方向: " + n);

            // 实测 +0.43：汉字的水平重心比垂直重心分布更集中，所以相关系数天然低于 Y，
            // 阈值取 0.2 留余量；整包左右镜像会变成 -0.43，仍能被明确区分。
            Assert.IsTrue(corr > 0.2,
                "local.zip 与 mmah.zip 的**水平**重心相关系数 = " + corr.ToString("F3")
                + "，说明 local.zip 整包左右镜像了（X 未正确处理）。"
                + " 检查 tools/import_gfont.py 的坐标变换。");
        }

        [TestMethod]
        public void Latin_L_VerticalBarSitsOnLeft()
        {
            // 「L」的竖笔在左缘、底横向右延伸。取字形顶部 30% 的点（这一段只有竖笔），
            // 其 x 中心应贴近左缘。实测 0.000；X 镜像后为 1.000，区分度极大。
            GlyphRepository repo = OpenOnly("latin.zip");
            try
            {
                string src;
                StrokeGlyph g = repo.Get('L', out src);
                Assert.IsNotNull(g, "latin.zip 缺少「L」");

                double xlo = double.MaxValue, xhi = double.MinValue;
                double ylo = double.MaxValue, yhi = double.MinValue;
                foreach (Stroke s in g.Strokes)
                    foreach (Pt p in s.Points)
                    {
                        if (p.X < xlo) xlo = p.X;
                        if (p.X > xhi) xhi = p.X;
                        if (p.Y < ylo) ylo = p.Y;
                        if (p.Y > yhi) yhi = p.Y;
                    }
                Assert.IsTrue(xhi - xlo > 1e-6 && yhi - ylo > 1e-6, "「L」包围盒退化，无法判定");

                double cut = yhi - 0.3 * (yhi - ylo);
                double sum = 0; int n = 0;
                foreach (Stroke s in g.Strokes)
                    foreach (Pt p in s.Points)
                        if (p.Y >= cut) { sum += p.X; n++; }

                Assert.IsTrue(n > 0, "「L」顶部 30% 没有点，无法判定");
                double cx = (sum / n - xlo) / (xhi - xlo);

                Assert.IsTrue(cx < 0.25,
                    "「L」顶部竖笔应在字形左缘，实测 x 中心 " + cx.ToString("F2")
                    + "（大于 0.25 说明字形左右镜像了）");
            }
            finally
            {
                repo.Dispose();
            }
        }

        [TestMethod]
        public void Latin_F_LeftHalfCarriesMoreInk()
        {
            // 「F」竖笔贯穿全高且在最左，右侧只有两条短横 → 左半点数显著多于右半。
            // 实测左半占比 0.667；X 镜像后 0.333。这条测的是整体墨量分布，
            // 与上一条（局部位置）互补：万一某个字体把 L 的写法改成带衬线，还有这条兜底。
            GlyphRepository repo = OpenOnly("latin.zip");
            try
            {
                string src;
                StrokeGlyph g = repo.Get('F', out src);
                Assert.IsNotNull(g, "latin.zip 缺少「F」");

                double xlo = double.MaxValue, xhi = double.MinValue;
                int total = 0;
                foreach (Stroke s in g.Strokes)
                    foreach (Pt p in s.Points)
                    {
                        if (p.X < xlo) xlo = p.X;
                        if (p.X > xhi) xhi = p.X;
                        total++;
                    }
                Assert.IsTrue(total > 0 && xhi - xlo > 1e-6, "「F」没有可用点，无法判定");

                double xmid = (xlo + xhi) / 2.0;
                int left = 0;
                foreach (Stroke s in g.Strokes)
                    foreach (Pt p in s.Points)
                        if (p.X < xmid) left++;

                double ratio = (double)left / total;
                Assert.IsTrue(ratio > 0.55,
                    "「F」左半点数占比应在 0.55 以上（实测原样 0.667 / 镜像 0.333），"
                    + "当前 " + ratio.ToString("F3") + "，说明字形左右镜像了");
            }
            finally
            {
                repo.Dispose();
            }
        }
    }
}
