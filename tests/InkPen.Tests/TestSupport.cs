using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using InkPen.Core;

namespace InkPen.Tests
{
    /// <summary>测试夹具：构造内存字形、临时字库包、最小 Job。</summary>
    internal static class TestSupport
    {
        public const char ChA = 'A';
        public const char ChWo = '\u6211';   // 我
        public const double Grid = 1024.0;

        /// <summary>从测试输出目录向上找仓库根（含 data\glyphs 的那一级）。</summary>
        public static string ProjectRoot()
        {
            string dir = AppDomain.CurrentDomain.BaseDirectory;
            for (int i = 0; i < 8; i++)
            {
                if (dir == null) return null;
                if (Directory.Exists(Path.Combine(dir, "data", "glyphs"))) return dir;
                DirectoryInfo p = Directory.GetParent(dir);
                if (p == null) return null;
                dir = p.FullName;
            }
            return null;
        }

        /// <summary>用扁平数组构造字形：strokes 每个元素是 x0,y0,x1,y1,... 的笔画。</summary>
        public static StrokeGlyph Glyph(char ch, double advance, params double[][] strokes)
        {
            StrokeGlyph g = new StrokeGlyph();
            g.Ch = ch;
            g.CodePoint = (int)ch;
            g.Advance = advance;
            g.Source = "test";
            if (strokes != null)
            {
                foreach (double[] flat in strokes)
                {
                    Stroke st = new Stroke();
                    for (int i = 0; i + 1 < flat.Length; i += 2)
                        st.Points.Add(new Pt(flat[i], flat[i + 1]));
                    g.Strokes.Add(st);
                }
            }
            return g;
        }

        /// <summary>标准测试字形：一竖一横，笔顺明确。</summary>
        public static StrokeGlyph GlyphA()
        {
            return Glyph(ChA, Grid,
                new double[] { 0, 0, 512, 1024 },
                new double[] { 0, 512, 1024, 512 });
        }

        public static StrokeGlyph GlyphWo()
        {
            return Glyph(ChWo, Grid,
                new double[] { 100, 900, 100, 100 },
                new double[] { 100, 600, 900, 600 },
                new double[] { 100, 100, 900, 100 });
        }

        /// <summary>按 InkPen Glyph v1 二进制格式写一个 ZIP 字库包。</summary>
        public static void WritePack(string path, IEnumerable<StrokeGlyph> glyphs)
        {
            if (File.Exists(path)) File.Delete(path);
            using (ZipArchive zip = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                foreach (StrokeGlyph g in glyphs)
                {
                    ZipArchiveEntry e = zip.CreateEntry(g.CodePoint.ToString());
                    using (Stream s = e.Open())
                    using (BinaryWriter w = new BinaryWriter(s))
                    {
                        w.Write((ushort)g.Strokes.Count);
                        w.Write((float)g.Advance);
                        foreach (Stroke st in g.Strokes)
                        {
                            w.Write((ushort)st.Points.Count);
                            foreach (Pt p in st.Points)
                            {
                                w.Write((float)p.X);
                                w.Write((float)p.Y);
                            }
                        }
                    }
                }
            }
        }

        /// <summary>建一个只含 A 与我 的临时字库目录，测试用（不依赖 data\glyphs）。</summary>
        public static string CreateTempFontDir()
        {
            string dir = Path.Combine(Path.GetTempPath(), "inkpen_test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            WritePack(Path.Combine(dir, "mmah.zip"), new StrokeGlyph[] { GlyphA(), GlyphWo() });
            return dir;
        }

        public static void DeleteDir(string dir)
        {
            try { if (dir != null && Directory.Exists(dir)) Directory.Delete(dir, true); }
            catch { }
        }

        /// <summary>含一个已排版字形的 Job（不走排版引擎，直接给定坐标）。</summary>
        public static Job SingleGlyphJob()
        {
            Job j = new Job();
            j.Text = "A";
            Page p = new Page();
            p.Index = 0;
            p.WidthMm = j.Machine.PaperWidthMm;
            p.HeightMm = j.Machine.PaperHeightMm;

            PlacedGlyph pg = new PlacedGlyph();
            pg.Glyph = GlyphA();
            pg.Ch = ChA;
            pg.X = 20;
            pg.Y = 200;
            pg.SizeMm = 10;
            pg.AdvanceMm = 11;
            pg.PageIndex = 0;
            pg.LineIndex = 0;
            pg.CharIndex = 0;
            p.Glyphs.Add(pg);
            j.Pages.Add(p);

            return j;
        }
    }
}
