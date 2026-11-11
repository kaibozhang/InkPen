using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace InkPen.Core
{
    /// <summary>
    /// 字库仓储：ZIP 容器 + 码点命名 + 紧凑二进制笔画。
    /// 回退链：local(本机手写) → mmah(笔顺主库) → pinyin → latin → latin_script → fallback。
    /// </summary>
    public class GlyphRepository : IDisposable
    {
        private class FontPack
        {
            public string Name;
            public string Path;
            public ZipArchive Zip;
            public Dictionary<int, string> Index = new Dictionary<int, string>();
            public Dictionary<int, StrokeGlyph> Cache = new Dictionary<int, StrokeGlyph>();
            public bool Enabled = true;
        }

        private readonly List<FontPack> _packs = new List<FontPack>();
        public readonly List<string> LoadErrors = new List<string>();

        public int Count
        {
            get
            {
                HashSet<int> all = new HashSet<int>();
                foreach (FontPack p in _packs)
                {
                    if (!p.Enabled) continue;   // 禁用后覆盖数应同步下降
                    foreach (int cp in p.Index.Keys) all.Add(cp);
                }
                return all.Count;
            }
        }

        public string[] ChainNames
        {
            get
            {
                List<string> n = new List<string>();
                foreach (FontPack p in _packs) n.Add(p.Name);
                return n.ToArray();
            }
        }

        public string PackInfo()
        {
            List<string> n = new List<string>();
            foreach (FontPack p in _packs)
                n.Add(p.Name + "=" + p.Index.Count + (p.Enabled ? "" : "(禁用)"));
            return string.Join("  ", n.ToArray());
        }

        public void Load(string dir, bool preferLocal, string latinFont)
        {
            Dispose();
            List<string> order = new List<string>();
            if (preferLocal) order.Add("local.zip");
            order.Add("mmah.zip");
            order.Add("pinyin.zip");
            order.Add(latinFont == "latin_script.zip" ? "latin_script.zip" : "latin.zip");
            if (latinFont == "latin_script.zip") order.Add("latin.zip");
            order.Add("fallback.zip");
            if (!preferLocal) order.Add("local.zip");

            foreach (string name in order)
            {
                string path = Path.Combine(dir, name);
                if (!File.Exists(path))
                {
                    if (name != "local.zip") LoadErrors.Add("缺少字库: " + name);
                    continue;
                }
                try
                {
                    FontPack p = new FontPack();
                    p.Name = name;
                    p.Path = path;
                    p.Zip = ZipFile.OpenRead(path);
                    foreach (ZipArchiveEntry e in p.Zip.Entries)
                    {
                        int cp;
                        if (int.TryParse(e.Name, out cp)) p.Index[cp] = e.FullName;
                    }
                    _packs.Add(p);
                }
                catch (Exception ex)
                {
                    LoadErrors.Add(name + ": " + ex.Message);
                }
            }
        }

        public bool SetEnabled(string name, bool enabled)
        {
            foreach (FontPack p in _packs)
                if (p.Name == name) { p.Enabled = enabled; return true; }
            return false;
        }

        public StrokeGlyph Get(char ch, out string source)
        {
            int cp = (int)ch;
            foreach (FontPack p in _packs)
            {
                if (!p.Enabled || !p.Index.ContainsKey(cp)) continue;
                StrokeGlyph g;
                if (p.Cache.TryGetValue(cp, out g))
                {
                    source = p.Name;
                    return g;
                }
                try
                {
                    ZipArchiveEntry e = p.Zip.GetEntry(p.Index[cp]);
                    if (e == null) continue;
                    g = Read(e, cp, ch, p.Name);
                    p.Cache[cp] = g;
                    source = p.Name;
                    return g;
                }
                catch (Exception ex)
                {
                    LoadErrors.Add(p.Name + "/" + cp + ": " + ex.Message);
                }
            }
            source = "";
            return null;
        }

        private static StrokeGlyph Read(ZipArchiveEntry e, int cp, char ch, string source)
        {
            StrokeGlyph g = new StrokeGlyph();
            g.CodePoint = cp;
            g.Ch = ch;
            g.Source = source;
            using (Stream s = e.Open())
            using (BinaryReader r = new BinaryReader(s))
            {
                int nStroke = r.ReadUInt16();
                g.Advance = r.ReadSingle();
                for (int i = 0; i < nStroke; i++)
                {
                    int nPts = r.ReadUInt16();
                    Stroke st = new Stroke();
                    for (int j = 0; j < nPts; j++)
                    {
                        float x = r.ReadSingle();
                        float y = r.ReadSingle();
                        st.Points.Add(new Pt(x, y));
                    }
                    if (st.Points.Count >= 2) g.Strokes.Add(st);
                }
            }
            return g;
        }

        public void Dispose()
        {
            foreach (FontPack p in _packs)
            {
                try { if (p.Zip != null) p.Zip.Dispose(); }
                catch { }
            }
            _packs.Clear();
        }
    }
}
