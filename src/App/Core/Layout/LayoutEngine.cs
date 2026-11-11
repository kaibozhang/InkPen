using System;
using System.Collections.Generic;

namespace InkPen.Core
{
    /// <summary>文稿排版：横排/竖排、自动换行分页、标点避头尾、四类稿纸模板。</summary>
    public static class LayoutEngine
    {
        private const string NO_LINE_START = "，。！？；：、）】》」』〕〉…·％‰.,!?;:)]}>";
        private const string NO_LINE_END = "（【《「『〔〈([{<";

        public static List<Page> Layout(Job job, GlyphRepository fonts)
        {
            TemplateConfig t = job.Template;
            MachineConfig m = job.Machine;
            List<Page> pages = new List<Page>();
            Rng rng = new Rng((uint)m.Seed);
            job.MissingChars.Clear();

            double usableW = m.PaperWidthMm - t.MarginLeft - t.MarginRight;
            double usableH = m.PaperHeightMm - t.MarginTop - t.MarginBottom;
            double lineH = t.FontSizeMm + t.LineGapMm;
            if (lineH < 1) lineH = t.FontSizeMm + 1;

            Page page = NewPage(pages.Count, m);
            pages.Add(page);

            if (t.Direction == TextDirection.Vertical)
            {
                LayoutVertical(job, fonts, page, pages, rng, usableW, usableH, lineH);
            }
            else
            {
                LayoutHorizontal(job, fonts, page, pages, rng, usableW, usableH, lineH);
            }

            foreach (Page pg in pages) BuildGrid(pg, t, m);
            return pages;
        }

        private static Page NewPage(int index, MachineConfig m)
        {
            Page p = new Page();
            p.Index = index;
            p.WidthMm = m.PaperWidthMm;
            p.HeightMm = m.PaperHeightMm;
            return p;
        }

        private static void LayoutHorizontal(Job job, GlyphRepository fonts, Page page,
            List<Page> pages, Rng rng, double usableW, double usableH, double lineH)
        {
            TemplateConfig t = job.Template;
            // 方格/田字格是「一字一格」：首行缩进必须落在格线上，否则字形从 MarginLeft+indent
            // 起排、格线却从 MarginLeft 起按 cell 排列，整行字都压在格边上。
            double indent = (pages.Count == 1 ? t.FirstLineIndentMm : 0.0);
            if (indent > 0 && (t.Kind == TemplateKind.Grid || t.Kind == TemplateKind.Tianzi))
            {
                double cell = t.FontSizeMm + t.CharGapMm;
                if (cell > 0.5) indent = Math.Round(indent / cell) * cell;
            }
            double x = t.MarginLeft + indent;
            double lineStartX = x;                       // 本行排版的起点，两端对齐要按它算补量
            double y = job.Machine.PaperHeightMm - t.MarginTop - t.FontSizeMm;
            // y 是字身底边（字形占 [y, y+FontSize]），所以页底判定就该是 MarginBottom。
            // 原来多扣了一个字号，每页白扔一整行，横排还因此比竖排少排一行。
            double bottom = t.MarginBottom;
            int lineIndex = 0, charIndex = 0;
            List<PlacedGlyph> line = new List<PlacedGlyph>();

            string text = (job.Text ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Replace('\t', ' ');
            for (int i = 0; i < text.Length; i++)
            {
                char ch = text[i];
                // 非 BMP 字符（emoji、扩展区汉字）在 UTF-16 里是两个 char。按代码单元迭代会把
                // 一个码点当两个字：缺字记两条孤立代理、宽度多占一格。合并成一个码点再查字形。
                int cp = ch;
                if (char.IsHighSurrogate(ch) && i + 1 < text.Length
                    && char.IsLowSurrogate(text[i + 1]))
                {
                    cp = char.ConvertToUtf32(ch, text[i + 1]);
                    i++;                                  // 吃掉低代理，循环再 +1 正好跳过整对
                }
                if (ch == '\n')
                {
                    FlushLine(page, line, lineIndex, t, usableW, lineStartX);
                    line.Clear();
                    lineIndex++;
                    x = t.MarginLeft;
                    lineStartX = x;
                    y -= lineH;
                    if (y < bottom)
                    {
                        page = NewPage(pages.Count, job.Machine);
                        pages.Add(page);
                        y = job.Machine.PaperHeightMm - t.MarginTop - t.FontSizeMm;
                        lineIndex = 0;
                    }
                    continue;
                }
                if (ch == ' ')
                {
                    double sp = t.FontSizeMm * 0.5;
                    // 空格也要受行宽约束。之前它无条件推进 x，于是「行首连打十几个空格」
                    // 就能把后面的字一路推出纸外，而排版阶段没有任何提示。
                    if (x + sp > t.MarginLeft + usableW)
                    {
                        if (line.Count > 0)
                        {
                            FlushLine(page, line, lineIndex, t, usableW, lineStartX);
                            line.Clear();
                            lineIndex++;
                            x = t.MarginLeft;
                            lineStartX = x;
                            y -= lineH;
                            if (y < bottom)
                            {
                                page = NewPage(pages.Count, job.Machine);
                                pages.Add(page);
                                y = job.Machine.PaperHeightMm - t.MarginTop - t.FontSizeMm;
                                lineIndex = 0;
                            }
                            continue;   // 换行后这个空格当行首空格丢弃，避免行首缩进越积越多
                        }
                        // 整行都是空格：把游标夹回右边界，不换行（否则会空白页刷屏）
                        x = t.MarginLeft + usableW - sp;
                    }
                    x += sp;
                    continue;
                }

                string src;
                StrokeGlyph g = fonts.Get(cp, out src);
                if (g == null)
                {
                    string miss = cp > 0xFFFF ? char.ConvertFromUtf32(cp) : ch.ToString();
                    if (!job.MissingChars.Contains(miss)) job.MissingChars.Add(miss);
                    x += t.FontSizeMm;
                    continue;
                }
                double adv = Math.Max(g.Advance, 1) / 1024.0 * t.FontSizeMm + t.CharGapMm;
                if (x + adv > t.MarginLeft + usableW && line.Count > 0)
                {
                    // 避头尾：
                    //  · 行首禁则字符（。，」等）允许挤在行尾，但**只能挤一个字宽**。
                    //    原来这里是空语句，连续标点（“……”“！！！”）会一路往右挤，
                    //    最终整行墨迹落到纸外，而画布和校验器都不会报错。
                    //  · 行尾禁则字符（“（等）下移到下一行时，必须先换页再放置，
                    //    否则字形会留在上一页的页底，与它所在的行隔着一整页。
                    bool hangOk = NO_LINE_START.IndexOf(ch) >= 0
                               && (x + adv) - (t.MarginLeft + usableW) <= t.FontSizeMm * 1.05;

                    if (!hangOk && line.Count > 0 && NO_LINE_END.IndexOf(line[line.Count - 1].Ch) >= 0)
                    {
                        PlacedGlyph moved = line[line.Count - 1];
                        line.RemoveAt(line.Count - 1);
                        FlushLine(page, line, lineIndex, t, usableW, lineStartX);
                        line.Clear();
                        lineIndex++;
                        y -= lineH;
                        // 先决定落在哪一页，再算坐标：换页判断必须在 moved 定位之前
                        if (y < bottom)
                        {
                            page = NewPage(pages.Count, job.Machine);
                            pages.Add(page);
                            y = job.Machine.PaperHeightMm - t.MarginTop - t.FontSizeMm;
                            lineIndex = 0;
                        }
                        moved.X = t.MarginLeft;
                        moved.Y = y;
                        moved.LineIndex = lineIndex;
                        moved.PageIndex = page.Index;
                        line.Add(moved);
                        x = t.MarginLeft + moved.AdvanceMm;
                        lineStartX = t.MarginLeft;
                    }
                    else if (hangOk)
                    {
                        // 允许悬挂：什么都不做，继续按原 x 放这个标点
                    }
                    else
                    {
                        FlushLine(page, line, lineIndex, t, usableW, lineStartX);
                        line.Clear();
                        lineIndex++;
                        x = t.MarginLeft;
                        lineStartX = x;
                        y -= lineH;
                        if (y < bottom)
                        {
                            page = NewPage(pages.Count, job.Machine);
                            pages.Add(page);
                            y = job.Machine.PaperHeightMm - t.MarginTop - t.FontSizeMm;
                            lineIndex = 0;
                        }
                    }
                }

                PlacedGlyph pg = new PlacedGlyph();
                pg.Glyph = g; pg.Ch = ch;
                pg.X = x + Jitter(rng, job.Machine.JitterPosMm);
                pg.Y = y + Jitter(rng, job.Machine.JitterPosMm)
                         + Math.Sin(lineIndex * 0.9 + charIndex * 0.13) * job.Machine.BaselineWaveMm;
                pg.SizeMm = t.FontSizeMm * (1.0 + Jitter(rng, job.Machine.JitterSize));
                pg.AdvanceMm = adv;
                pg.PageIndex = pages.Count - 1;
                pg.LineIndex = lineIndex;
                pg.CharIndex = charIndex++;
                line.Add(pg);
                x += adv;
            }
            FlushLine(page, line, lineIndex, t, usableW, lineStartX);
            foreach (PlacedGlyph pg in page.Glyphs) pg.PageIndex = page.Index;
        }

        private static void LayoutVertical(Job job, GlyphRepository fonts, Page page,
            List<Page> pages, Rng rng, double usableW, double usableH, double lineH)
        {
            TemplateConfig t = job.Template;
            double x = job.Machine.PaperWidthMm - t.MarginRight - t.FontSizeMm;
            double y = job.Machine.PaperHeightMm - t.MarginTop - t.FontSizeMm;
            double left = t.MarginLeft;
            int lineIndex = 0, charIndex = 0;

            string text = (job.Text ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Replace('\t', ' ');
            for (int i = 0; i < text.Length; i++)
            {
                char ch = text[i];
                if (ch == '\n')
                {
                    x -= lineH;
                    y = job.Machine.PaperHeightMm - t.MarginTop - t.FontSizeMm;
                    lineIndex++;
                    if (x < left)
                    {
                        page = NewPage(pages.Count, job.Machine);
                        pages.Add(page);
                        x = job.Machine.PaperWidthMm - t.MarginRight - t.FontSizeMm;
                        lineIndex = 0;
                    }
                    continue;
                }
                if (ch == ' ') { y -= t.FontSizeMm * 0.5; continue; }
                string src;
                StrokeGlyph g = fonts.Get(ch, out src);
                if (g == null)
                {
                    if (!job.MissingChars.Contains(ch.ToString())) job.MissingChars.Add(ch.ToString());
                    y -= t.FontSizeMm;
                    continue;
                }
                if (y < t.MarginBottom)
                {
                    x -= lineH;
                    y = job.Machine.PaperHeightMm - t.MarginTop - t.FontSizeMm;
                    lineIndex++;
                    if (x < left)
                    {
                        page = NewPage(pages.Count, job.Machine);
                        pages.Add(page);
                        x = job.Machine.PaperWidthMm - t.MarginRight - t.FontSizeMm;
                        lineIndex = 0;
                    }
                }
                PlacedGlyph pg = new PlacedGlyph();
                pg.Glyph = g; pg.Ch = ch;
                pg.X = x + Jitter(rng, job.Machine.JitterPosMm);
                pg.Y = y + Jitter(rng, job.Machine.JitterPosMm);
                pg.SizeMm = t.FontSizeMm * (1.0 + Jitter(rng, job.Machine.JitterSize));
                pg.AdvanceMm = t.FontSizeMm + t.CharGapMm;
                pg.PageIndex = pages.Count - 1;
                pg.LineIndex = lineIndex;
                pg.CharIndex = charIndex++;
                page.Glyphs.Add(pg);
                y -= pg.AdvanceMm;
            }
            foreach (PlacedGlyph pg in page.Glyphs) pg.PageIndex = page.Index;
        }

        private static void FlushLine(Page page, List<PlacedGlyph> line, int lineIndex,
                                      TemplateConfig t, double usableW, double lineStartX)
        {
            if (line.Count == 0) return;
            if (t.Justify || t.CenterVertically)
            {
                double used = 0;
                foreach (PlacedGlyph p in line) used += p.AdvanceMm;
                // 补量必须相对本行的**实际起点**算。首行有缩进时起点是 MarginLeft+indent，
                // 按 MarginLeft 算会稳定多补一个 indent，整行右端顶出右边距。
                double extra = (t.MarginLeft + usableW) - (lineStartX + used);
                if (t.CenterVertically && extra > 0)
                    foreach (PlacedGlyph p in line) p.X += extra / 2.0;
                else if (t.Justify && extra > 0 && line.Count > 1)
                {
                    double step = extra / (line.Count - 1);
                    for (int i = 1; i < line.Count; i++) line[i].X += step * i;
                }
            }
            foreach (PlacedGlyph p in line)
            {
                p.LineIndex = lineIndex;
                p.PageIndex = page.Index;
                page.Glyphs.Add(p);
            }
        }

        private static double Jitter(Rng rng, double amount)
        {
            if (amount <= 0) return 0;
            return (rng.NextDouble() * 2.0 - 1.0) * amount;
        }

        private static void BuildGrid(Page page, TemplateConfig t, MachineConfig m)
        {
            page.Grid.Clear();
            if (t.Kind == TemplateKind.Free) return;
            double mL = t.MarginLeft, mR = m.PaperWidthMm - t.MarginRight;
            double lineH = t.FontSizeMm + t.LineGapMm;
            double top = m.PaperHeightMm - t.MarginTop;

            if (t.Direction == TextDirection.Vertical)
            {
                double x = m.PaperWidthMm - t.MarginRight - t.FontSizeMm;
                while (x > t.MarginLeft)
                {
                    if (t.Kind == TemplateKind.Grid || t.Kind == TemplateKind.Tianzi)
                        page.Grid.Add(new GridLine(x, t.MarginBottom, x, top));
                    x -= lineH;
                }
                return;
            }

            // 找出本页出现的所有基线
            List<double> baselines = new List<double>();
            foreach (PlacedGlyph g in page.Glyphs)
            {
                double b = g.Y;
                bool found = false;
                foreach (double v in baselines) if (Math.Abs(v - b) < lineH * 0.4) { found = true; break; }
                if (!found) baselines.Add(b);
            }
            baselines.Sort();
            baselines.Reverse();
            for (int li = 0; li < baselines.Count; li++)
            {
                double baseY = baselines[li];
                if (t.Kind == TemplateKind.Ruled)
                {
                    page.Grid.Add(new GridLine(mL, baseY, mR, baseY));
                }
                else if (t.Kind == TemplateKind.Pinyin4)
                {
                    double q = t.FontSizeMm / 3.0;
                    for (int k = 0; k <= 3; k++)
                        page.Grid.Add(new GridLine(mL, baseY + k * q, mR, baseY + k * q));
                }
                else // Grid / Tianzi：逐字格
                {
                    double cell = t.FontSizeMm + t.CharGapMm;
                    for (double x = mL; x + cell <= mR + 0.01; x += cell)
                    {
                        page.Grid.Add(new GridLine(x, baseY, x + cell, baseY));
                        page.Grid.Add(new GridLine(x, baseY + t.FontSizeMm, x + cell, baseY + t.FontSizeMm));
                        page.Grid.Add(new GridLine(x, baseY, x, baseY + t.FontSizeMm));
                        page.Grid.Add(new GridLine(x + cell, baseY, x + cell, baseY + t.FontSizeMm));
                        if (t.Kind == TemplateKind.Tianzi)
                        {
                            // 虚线十字（用短线段模拟）
                            for (int k = 0; k < 5; k++)
                            {
                                double a = baseY + k * t.FontSizeMm / 5.0;
                                page.Grid.Add(new GridLine(x + cell / 2, a, x + cell / 2, a + t.FontSizeMm / 10.0));
                                double bx = x + k * cell / 5.0;
                                page.Grid.Add(new GridLine(bx, baseY + t.FontSizeMm / 2, bx + cell / 10.0, baseY + t.FontSizeMm / 2));
                            }
                        }
                    }
                }
            }
        }
    }

    /// <summary>确定性 xorshift32，保证同种子同参数可复现。</summary>
    public class Rng
    {
        private uint _s;
        public Rng(uint seed) { _s = seed == 0 ? 0x9E3779B9u : seed; }
        public uint Next()
        {
            _s ^= _s << 13; _s ^= _s >> 17; _s ^= _s << 5;
            return _s;
        }
        public double NextDouble() { return (Next() & 0xFFFFFF) / 16777216.0; }
    }
}
