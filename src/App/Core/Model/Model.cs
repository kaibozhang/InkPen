using System;
using System.Collections.Generic;

namespace InkPen.Core
{
    public struct Pt
    {
        public double X;
        public double Y;
        public Pt(double x, double y) { X = x; Y = y; }
    }

    /// <summary>一条连续落笔笔画。边界即抬笔点。</summary>
    public class Stroke
    {
        public List<Pt> Points = new List<Pt>();
    }

    public class StrokeGlyph
    {
        public int CodePoint;
        public char Ch;
        public double Advance = 1024.0;
        public List<Stroke> Strokes = new List<Stroke>();
        public string Source = "";
    }

    public class PlacedGlyph
    {
        public StrokeGlyph Glyph;
        public char Ch;
        public double X;        // 纸面 mm，字身左下
        public double Y;        // 纸面 mm，字身左下
        public double SizeMm;   // 字身高度 mm
        public double AdvanceMm;
        public int PageIndex;
        public int LineIndex;
        public int CharIndex;
    }

    public class GridLine
    {
        public double X1, Y1, X2, Y2;
        public GridLine(double x1, double y1, double x2, double y2)
        { X1 = x1; Y1 = y1; X2 = x2; Y2 = y2; }
    }

    public class Page
    {
        public int Index;
        public double WidthMm, HeightMm;
        public List<PlacedGlyph> Glyphs = new List<PlacedGlyph>();
        public List<GridLine> Grid = new List<GridLine>();
    }

    public enum TemplateKind { Free, Ruled, Grid, Tianzi, Pinyin4 }
    public enum TextDirection { Horizontal, Vertical }
    public enum OriginCorner { BottomLeft, TopLeft, BottomRight, TopRight }

    public class TemplateConfig
    {
        public string Name = "横线稿纸";
        public TemplateKind Kind = TemplateKind.Ruled;
        public TextDirection Direction = TextDirection.Horizontal;
        public double MarginLeft = 15, MarginRight = 15, MarginTop = 15, MarginBottom = 15;
        public double FontSizeMm = 10.0;
        public double CharGapMm = 1.0;
        public double LineGapMm = 6.0;
        public double FirstLineIndentMm = 0.0;
        public bool DrawGrid = false;       // 格线是否写入 GCode
        public bool ShowGrid = true;        // 画布是否显示格线
        public bool CenterVertically = false;
        public bool Justify = false;

        public string LatinFont = "latin.zip";  // latin.zip / latin_script.zip
    }

    public class MachineConfig
    {
        public double PaperWidthMm = 210.0;
        public double PaperHeightMm = 297.0;
        public OriginCorner Origin = OriginCorner.BottomLeft;
        public bool InvertX = false;
        public bool InvertY = false;

        // Z 轴步进抬落笔（留空/默认需真机标定）
        public double SafeZ = 5.0;
        public double WriteZ = 0.0;
        public double ZFeed = 300.0;
        public double XYFeed = 1500.0;
        public double RapidFeed = 3000.0;
        public double DwellSec = 0.15;
        public double ConnectDistanceMm = 0.8;  // 相邻笔画端点距离小于此值则不抬笔（连笔）

        public string StartGCode = "G21\nG90\nG94";
        public string EndGCode = "G0 X0 Y0";
        public double SimplifyEpsilonMm = 0.02;

        public string PortName = "";
        public int BaudRate = 115200;
        public bool AutoDetect = true;
        public int AckTimeoutMs = 30000;    // 等待 GRBL ok 的超时；0 禁用
        public bool AutoReconnect = true;   // 掉线后按退避自动重连（A3）

        // 高度图 / 接触式探测（A1 / A5）
        public bool UseHeightMap = false;   // 逐点 Z 补偿开关（数据在 config\heightmap.json）
        public double ProbeTravelMm = 8.0;  // 单次下探最大行程
        public double ProbeFeedMmPerMin = 100.0;
        public int ProbeCols = 3;
        public int ProbeRows = 3;
        public double ProbeMarginMm = 10.0; // 探测区域相对纸边内缩
        public double PressDepthMm = 0.3;   // 弹簧笔架压入量：接触后再压这么多

        // 手写感（0..1 或具体量纲）
        public int Seed = 20261110;
        public double JitterSize = 0.0;
        public double JitterPosMm = 0.0;
        public double JitterStrokeMm = 0.0;
        public double BaselineWaveMm = 0.0;
        public double TailStrength = 0.0;   // 笔锋：起收笔重复描画长度比例
        public bool PreferLocalFont = true; // 本机手写风格优先
    }

    /// <summary>一行 GCode 及其续写元数据。</summary>
    public class GCodeLine
    {
        public string Text;
        public int PageIndex = -1;
        public int LineIndex = -1;
        public int CharIndex = -1;
        public int StrokeIndex = -1;
        public int CodePoint = 0;
        public bool IsPenDown = false;
    }

    /// <summary>仿真用路径（纸面 mm 坐标）。</summary>
    public class WritePath
    {
        public List<Pt> Points = new List<Pt>();
        public bool PenDown;
        public int PageIndex;
        public int LineIndex;
        public int CharIndex;
        public int StrokeIndex;
        public int CodePoint;
        public int FirstGCodeLine;
    }

    public class Job
    {
        public string Text = "";
        public List<Page> Pages = new List<Page>();
        public List<GCodeLine> GCode = new List<GCodeLine>();
        public List<WritePath> Paths = new List<WritePath>();
        public TemplateConfig Template = new TemplateConfig();
        public MachineConfig Machine = new MachineConfig();
        public List<string> MissingChars = new List<string>();
        public int ResumeCharIndex = 0;
        /// <summary>纸面高度补偿表。为空或未启用时 GCode 与不带补偿时完全一致。</summary>
        public HeightMap HeightMap = new HeightMap();
    }
}
