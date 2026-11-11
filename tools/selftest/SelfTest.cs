using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using InkPen.Core;

// GUI-free, offline end-to-end verification of the REAL production Core.
// No WinForms, no serial port, no external DLL.
// The source is intentionally 100% ASCII: non-ASCII characters are built from
// codepoints via (char)0x... and newlines via (char)10. This avoids any
// csc.exe source-codepage ambiguity (GBK vs UTF-8) on Chinese Windows.

namespace InkPen.SelfTest
{
    internal static class Program
    {
        const int Seed = 20261110;

        static string _outDir;
        static GlyphRepository _fonts;
        static int _checks = 0;
        static int _failed = 0;
        static readonly List<string> _ncFiles = new List<string>();
        static readonly HashSet<char> _probeChars = new HashSet<char>();

        private class JobSpec
        {
            public string Name;
            public string Text;
            public TemplateKind Kind;
            public TextDirection Direction;
            public bool DrawGrid;
            public bool RequireNoMissing;
            public bool RequireMultiPage;
            public bool RequireGridInGcode;
        }

        static int Main()
        {
            Console.WriteLine("=== InkPen offline end-to-end self-test (seed=" + Seed + ") ===");
            try
            {
                return Run();
            }
            catch (Exception ex)
            {
                Console.WriteLine("[FATAL] Unhandled exception, stack trace follows:");
                Console.WriteLine(ex.ToString());
                return 1;
            }
        }

        // Build a string from Unicode codepoints without embedding non-ASCII bytes.
        static string Chars(params int[] cps)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < cps.Length; i++) sb.Append((char)cps[i]);
            return sb.ToString();
        }

        static string Join(List<string> xs)
        {
            return string.Join(" ", xs.ToArray());
        }

        // (a) Hanzi + digits + Latin + toned pinyin + Chinese punctuation, mixed.
        static string MixedText()
        {
            return Chars(0x6C49, 0x5B57, 0x6570, 0x5B57) + "123 Latin "
                + "m" + Chars(0x0101) + " m" + Chars(0x00E1) + " m" + Chars(0x01CE) + " m" + Chars(0x00E0) + " "
                + "n" + Chars(0x01D0) + " h" + Chars(0x01CE) + "o "
                + Chars(0x01D6) + " " + Chars(0x01D8) + " " + Chars(0x01DA) + " " + Chars(0x01DC) + " "
                + Chars(0xFF0C, 0x3002, 0xFF01, 0xFF1F, 0xFF1B, 0xFF1A, 0x300A, 0x300B, 0x3001);
        }

        static string ShortText()
        {
            return "InkPen " + Chars(0x6C49, 0x5B57) + "123 abc " + Chars(0xFF0C, 0x3002, 0xFF01);
        }

        // (d) long text with explicit line breaks that forces multiple pages.
        static string MultiPageText()
        {
            string phrase = Chars(0x6C49, 0x5B57, 0x6570, 0x5B57) + "123abc" + Chars(0xFF0C);
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < 60; i++)
            {
                if (i > 0) sb.Append((char)10);
                sb.Append(phrase);
            }
            return sb.ToString();
        }

        static int Run()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string root = Path.GetFullPath(Path.Combine(baseDir, ".."));
            string distGlyphs = Path.Combine(baseDir, "data", "glyphs");
            string srcGlyphs = Path.Combine(root, "data", "glyphs");
            string glyphDir = File.Exists(Path.Combine(distGlyphs, "mmah.zip")) ? distGlyphs : srcGlyphs;

            _outDir = Path.Combine(baseDir, "selftest");
            Directory.CreateDirectory(_outDir);

            // ---- main pass: preferLocal=false so results are reproducible ----
            _fonts = new GlyphRepository();
            _fonts.Load(glyphDir, false, "latin.zip");
            Console.WriteLine("Glyph dir   : " + glyphDir);
            Console.WriteLine("preferLocal : false  (main pass, reproducible on any machine)");
            Console.WriteLine("Glyph Count : " + _fonts.Count);
            Console.WriteLine("Packs       : " + _fonts.PackInfo());
            Console.WriteLine("LoadErrors  : " + _fonts.LoadErrors.Count);
            foreach (string e in _fonts.LoadErrors) Console.WriteLine("    " + e);
            if (_fonts.Count <= 0)
            {
                Console.WriteLine("[FATAL] No glyphs loaded - cannot continue.");
                _fonts.Dispose();
                return 1;
            }
            Console.WriteLine();

            List<JobSpec> specs = MakeSpecs();
            Console.WriteLine("--- jobs ---");
            foreach (JobSpec sp in specs) RunJob(sp);
            Console.WriteLine();

            // ---- determinism: rebuild the same job twice, compare bytes ----
            DeterminismTest();

            // ---- resume: mid-job charIndex -> FindResumeLine / ToText ----
            ResumeTest();
            Console.WriteLine();

            // ---- optional local.zip coverage comparison ----
            LocalComparison(glyphDir);
            Console.WriteLine();

            Console.WriteLine("=== SUMMARY ===");
            Console.WriteLine("jobs=" + specs.Count + " nc_files=" + _ncFiles.Count
                + " checks=" + _checks + " failed=" + _failed);
            foreach (string f in _ncFiles) Console.WriteLine("  " + f);
            Console.WriteLine(_failed == 0 ? "SELFTEST RESULT: PASS" : "SELFTEST RESULT: FAIL");

            _fonts.Dispose();
            return _failed == 0 ? 0 : 1;
        }

        static List<JobSpec> MakeSpecs()
        {
            List<JobSpec> s = new List<JobSpec>();
            s.Add(new JobSpec { Name = "a_mixed",     Text = MixedText(),     Kind = TemplateKind.Free,    Direction = TextDirection.Horizontal, DrawGrid = false, RequireNoMissing = true });
            s.Add(new JobSpec { Name = "b_free",      Text = ShortText(),     Kind = TemplateKind.Free,    Direction = TextDirection.Horizontal, DrawGrid = false });
            s.Add(new JobSpec { Name = "b_ruled",     Text = ShortText(),     Kind = TemplateKind.Ruled,   Direction = TextDirection.Horizontal, DrawGrid = false });
            s.Add(new JobSpec { Name = "b_grid",      Text = ShortText(),     Kind = TemplateKind.Grid,    Direction = TextDirection.Horizontal, DrawGrid = false });
            s.Add(new JobSpec { Name = "b_tianzi",    Text = ShortText(),     Kind = TemplateKind.Tianzi,  Direction = TextDirection.Horizontal, DrawGrid = false });
            s.Add(new JobSpec { Name = "b_pinyin4",   Text = ShortText(),     Kind = TemplateKind.Pinyin4, Direction = TextDirection.Horizontal, DrawGrid = false });
            s.Add(new JobSpec { Name = "c_vertical",  Text = MixedText(),     Kind = TemplateKind.Ruled,   Direction = TextDirection.Vertical,   DrawGrid = false });
            s.Add(new JobSpec { Name = "d_multipage", Text = MultiPageText(), Kind = TemplateKind.Ruled,   Direction = TextDirection.Horizontal, DrawGrid = false, RequireMultiPage = true });
            s.Add(new JobSpec { Name = "e_drawgrid",  Text = ShortText(),     Kind = TemplateKind.Grid,    Direction = TextDirection.Horizontal, DrawGrid = true, RequireGridInGcode = true });
            return s;
        }

        static Job MakeJob(JobSpec sp)
        {
            Job job = new Job();
            job.Text = sp.Text;
            job.Template.Kind = sp.Kind;
            job.Template.Direction = sp.Direction;
            job.Template.DrawGrid = sp.DrawGrid;
            job.Template.LatinFont = "latin.zip";
            job.Machine.Seed = Seed;
            return job;
        }

        static void BuildJob(Job job)
        {
            job.Pages = InkPen.Core.LayoutEngine.Layout(job, _fonts);
            GCodeBuilder.Build(job, true);
        }

        static void RunJob(JobSpec sp)
        {
            Job job = MakeJob(sp);
            BuildJob(job);
            GCodeValidator v = new GCodeValidator();
            v.Validate(job);

            string text = GCodeBuilder.ToText(job, 0);
            string path = Path.Combine(_outDir, sp.Name + ".nc");
            File.WriteAllText(path, text, new ASCIIEncoding());
            _ncFiles.Add(path);

            foreach (char c in sp.Text)
                if (!char.IsWhiteSpace(c)) _probeChars.Add(c);

            int zLines = 0, metaLines = 0, gridLines = 0;
            foreach (GCodeLine g in job.GCode)
            {
                if (g.Text.IndexOf("Z") >= 0 && (g.Text.IndexOf("G0") >= 0 || g.Text.IndexOf("G1") >= 0)) zLines++;
                if (g.Text.StartsWith("(CH:")) metaLines++;
                if (g.PageIndex >= 0 && g.LineIndex == -1 && g.CharIndex == -1 && g.StrokeIndex == -1) gridLines++;
            }

            List<string> fails = new List<string>();
            int nChecks = 6;
            if (v.Errors.Count != 0) fails.Add("validator Errors=" + v.Errors.Count + " [" + Join(v.Errors) + "]");
            if (job.GCode.Count <= 0) fails.Add("GCode.Count==0");
            if (job.Paths.Count <= 0) fails.Add("Paths.Count==0");
            if (zLines <= 0) fails.Add("no line containing Z");
            if (metaLines <= 0) fails.Add("no (CH: metadata line");
            if (sp.RequireNoMissing)
            {
                nChecks++;
                if (job.MissingChars.Count != 0) fails.Add("MissingChars=" + job.MissingChars.Count);
            }
            if (sp.RequireMultiPage)
            {
                nChecks++;
                if (job.Pages.Count < 2) fails.Add("Pages.Count<2 (no pagination)");
            }
            if (sp.RequireGridInGcode)
            {
                nChecks += 2;
                if (job.Pages[0].Grid.Count <= 0) fails.Add("no grid geometry generated");
                if (gridLines <= 0) fails.Add("no grid lines emitted into GCode");
            }
            _checks += nChecks;
            _failed += fails.Count;

            Console.WriteLine("  [" + (fails.Count == 0 ? "PASS" : "FAIL") + "] "
                + sp.Name.PadRight(13)
                + " kind=" + sp.Kind
                + " dir=" + (sp.Direction == TextDirection.Vertical ? "V" : "H")
                + " grid=" + (sp.DrawGrid ? "Y" : "N")
                + " | pages=" + job.Pages.Count
                + " paths=" + job.Paths.Count
                + " gcode=" + job.GCode.Count
                + " Z=" + zLines + " meta=" + metaLines + " gridLines=" + gridLines
                + " | missing=" + job.MissingChars.Count
                + " warn=" + v.Warnings.Count + " err=" + v.Errors.Count);
            foreach (string f in fails) Console.WriteLine("        -> " + f);
            foreach (string w in v.Warnings) Console.WriteLine("        [WARN] " + w);
            if (job.MissingChars.Count > 0)
                Console.WriteLine("        [" + (sp.RequireNoMissing ? "MISSING CHARS (asserted)" : "note: missing chars, reported only")
                    + "] " + Join(job.MissingChars));
        }

        static JobSpec DeterminismSpec()
        {
            return new JobSpec { Name = "determinism", Text = MixedText(), Kind = TemplateKind.Free, Direction = TextDirection.Horizontal, DrawGrid = false };
        }

        static void DeterminismTest()
        {
            JobSpec ds = DeterminismSpec();
            Job j1 = MakeJob(ds); BuildJob(j1);
            Job j2 = MakeJob(ds); BuildJob(j2);
            byte[] b1 = new ASCIIEncoding().GetBytes(GCodeBuilder.ToText(j1, 0));
            byte[] b2 = new ASCIIEncoding().GetBytes(GCodeBuilder.ToText(j2, 0));
            bool det = b1.Length == b2.Length;
            if (det)
                for (int i = 0; i < b1.Length; i++) if (b1[i] != b2[i]) { det = false; break; }
            _checks++;
            if (!det) _failed++;
            Console.WriteLine("  [" + (det ? "PASS" : "FAIL") + "] determinism : rebuild twice -> "
                + (det ? "byte-identical (" + b1.Length + " bytes)" : "DIFFERENT"));
        }

        static void ResumeTest()
        {
            JobSpec ds = DeterminismSpec();
            Job job = MakeJob(ds); BuildJob(job);
            string full = GCodeBuilder.ToText(job, 0);

            int maxCI = 0;
            foreach (GCodeLine g in job.GCode) if (g.CharIndex > maxCI) maxCI = g.CharIndex;
            int ci = maxCI / 2;
            int line = GCodeBuilder.FindResumeLine(job, ci);
            string resumed = GCodeBuilder.ToText(job, line);

            string path = Path.Combine(_outDir, "resume.nc");
            File.WriteAllText(path, resumed, new ASCIIEncoding());
            _ncFiles.Add(path);

            // prefix = first 6 emitted lines of a resumed stream
            StringBuilder pfx = new StringBuilder();
            string[] rl = resumed.Split((char)10);
            int taken = 0;
            for (int k = 0; k < rl.Length && taken < 6; k++)
            {
                pfx.Append(rl[k]).Append((char)10);
                taken++;
            }
            string prefix = pfx.ToString();

            bool hasG21 = prefix.IndexOf("G21") >= 0;
            bool hasG90 = prefix.IndexOf("G90") >= 0;
            bool hasG94 = prefix.IndexOf("G94") >= 0;
            bool hasG0Z = prefix.IndexOf("G0 Z") >= 0;
            bool hasF = false;
            for (int i = 0; i + 1 < prefix.Length; i++)
                if (prefix[i] == 'F' && char.IsDigit(prefix[i + 1])) { hasF = true; break; }
            bool shorter = resumed.Length < full.Length;
            bool linePositive = line > 0;

            List<string> fails = new List<string>();
            if (!hasG21) fails.Add("resume prefix missing G21");
            if (!hasG90) fails.Add("resume prefix missing G90");
            if (!hasG94) fails.Add("resume prefix missing G94");
            if (!hasF) fails.Add("resume prefix missing F feed");
            if (!hasG0Z) fails.Add("resume prefix missing G0 Z");
            if (!shorter) fails.Add("resumed text not shorter than full text");
            if (!linePositive) fails.Add("FindResumeLine returned 0");
            _checks += 7;
            _failed += fails.Count;

            Console.WriteLine("  [" + (fails.Count == 0 ? "PASS" : "FAIL") + "] resume : targetCI=" + ci
                + " line=" + line + " fullLen=" + full.Length + " resumeLen=" + resumed.Length
                + " shorter=" + shorter);
            foreach (string f in fails) Console.WriteLine("        -> " + f);
        }

        static void LocalComparison(string glyphDir)
        {
            Console.WriteLine("--- local.zip coverage comparison ---");
            string localPath = Path.Combine(glyphDir, "local.zip");
            if (!File.Exists(localPath))
            {
                Console.WriteLine("  local.zip : ABSENT (skipped, not a failure)");
                return;
            }
            Console.WriteLine("  local.zip : present");
            GlyphRepository fl = new GlyphRepository();
            fl.Load(glyphDir, true, "latin.zip");
            Console.WriteLine("  preferLocal=false : Count=" + _fonts.Count + "  packs=[" + string.Join(" ", _fonts.ChainNames) + "]");
            Console.WriteLine("  preferLocal=true  : Count=" + fl.Count + "  packs=[" + string.Join(" ", fl.ChainNames) + "]");
            Console.WriteLine("  note: both modes load the same set of packs (only priority differs), so Count is equal by design.");

            int servedFalse = 0, servedTrue = 0, sourceDiff = 0;
            foreach (char c in _probeChars)
            {
                string s1, s2;
                StrokeGlyph g1 = _fonts.Get(c, out s1);
                StrokeGlyph g2 = fl.Get(c, out s2);
                if (s1 == "local.zip") servedFalse++;
                if (s2 == "local.zip") servedTrue++;
                if (g1 != null && g2 != null && s1 != s2) sourceDiff++;
            }
            Console.WriteLine("  probe chars=" + _probeChars.Count
                + " | served by local.zip: preferLocal=false=" + servedFalse
                + " preferLocal=true=" + servedTrue
                + " | source-changed=" + sourceDiff);
            fl.Dispose();
        }
    }
}
