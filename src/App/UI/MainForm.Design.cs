using System;
using System.Drawing;
using System.Windows.Forms;
using InkPen.Core;

namespace InkPen.UI
{
    public partial class MainForm : Form
    {
        /// <summary>
        /// 下拉框顺序必须与 TemplateKind 语义显式对应。
        /// 早期直接 (TemplateKind)SelectedIndex 转换，因枚举顺序是
        /// Free/Ruled/Grid/Tianzi/Pinyin4 而界面是「横线/方格/田字/拼音/自由」，
        /// 导致选「横线稿纸」实际生成「自由排版」。
        /// </summary>
        private static readonly TemplateKind[] TPL_KINDS = new TemplateKind[]
        {
            TemplateKind.Ruled, TemplateKind.Grid, TemplateKind.Tianzi,
            TemplateKind.Pinyin4, TemplateKind.Free
        };

        private static readonly string[] TPL_LABELS = new string[]
        {
            "横线稿纸 / 信纸", "方格稿纸", "田字格", "拼音四线三格", "自由排版"
        };

        private static int TemplateToIndex(TemplateKind k)
        {
            for (int i = 0; i < TPL_KINDS.Length; i++) if (TPL_KINDS[i] == k) return i;
            return 0;
        }

        /// <summary>下拉框序号 → 模板类型（供单元测试锁定，避免再次错位）。</summary>
        public static TemplateKind TemplateKindFromIndex(int index)
        {
            if (index < 0 || index >= TPL_KINDS.Length) return TemplateKind.Ruled;
            return TPL_KINDS[index];
        }

        /// <summary>模板类型 → 下拉框序号。</summary>
        public static int IndexFromTemplateKind(TemplateKind kind)
        {
            return TemplateToIndex(kind);
        }

        private Font _ui;
        private SplitContainer _root, _inner;
        private TabControl _leftTabs, _rightTabs;
        private PaperCanvas _canvas;
        private ToolStrip _tools;
        private ProgressBar _progress;
        private Label _infoLbl, _statusLbl, _droLbl, _fontLbl, _progressLbl;

        // 文稿
        private TextBox _txt;
        private ComboBox _tplKind, _direction, _latinFont;
        private NumericUpDown _fontSize, _charGap, _lineGap, _firstIndent;
        private CheckBox _drawGrid, _showGrid, _center, _justify;
        private Button _btnGenerate, _btnExport, _btnSaveCfg, _btnLoadCfg;

        // 字库
        private ListBox _fontList;
        private CheckBox _preferLocal;
        private Button _btnReloadFonts, _btnToggleFont;

        // 机器
        private ComboBox _portCombo, _baudCombo, _stepCombo, _origin;
        private Button _btnRefreshPorts, _btnAutoDetect, _btnConnect;
        private Button _btnHome, _btnUnlock, _btnReset, _btnZero, _btnPenTest, _btnEStop;
        private Button _btnOv100, _btnOvUp, _btnOvDown;
        private Button _btnJogXp, _btnJogXm, _btnJogYp, _btnJogYm, _btnJogZp, _btnJogZm;
        private TextBox _customCmd;
        private Button _btnSendCmd;
        private Label _detectLbl;

        // 作业控制
        private Button _btnStart, _btnSendPause, _btnSendResume, _btnSendStep, _btnSendStop;
        private NumericUpDown _resumeChar;
        private Button _btnResumeFrom;

        // 设置
        private NumericUpDown _paperW, _paperH;
        private CheckBox _invertX, _invertY;
        private NumericUpDown _safeZ, _writeZ, _zFeed, _xyFeed, _dwell, _connectDist, _eps, _seed, _ackTimeout;
        private TextBox _startGc, _endGc;
        private TrackBar _jSize, _jPos, _jStroke, _jWave, _tail;
        private NumericUpDown _simSpeed;

        // GCode / 日志 / $ 参数
        private TextBox _gcodeBox, _logBox;
        private ToolStripButton _btnPlay, _btnSimPause, _btnSimStep, _btnSimStop, _btnFit, _btnZoomIn, _btnZoomOut;
        private ToolStripButton _btnPagePrev, _btnPageNext;
        private ToolStripLabel _pageLbl;
        private DataGridView _settingsGrid;
        private Button _btnReadSettings;

        private Font UiFont()
        {
            // 避免 GRBL-Plotter Issue #477 类崩溃：不假设特定字体族存在
            try { return new Font("Microsoft YaHei", 9f); }
            catch { }
            try { return new Font("SimSun", 9f); }
            catch { }
            return SystemFonts.DefaultFont;
        }

        private void BuildUi()
        {
            _ui = UiFont();
            Text = "写字机工作台 InkPen";
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(1400, 880);
            MinimumSize = new Size(1100, 680);
            AutoScaleMode = AutoScaleMode.Dpi;
            BackColor = Color.FromArgb(246, 248, 251);
            Font = _ui;

            // SplitContainer 的 MinSize / SplitterDistance 在构造期宽度尚未确定，
            // 先设会抛 InvalidOperationException（"SplitterDistance 必须在 ... 之间"）。
            // 全部延后到 Shown（布局完成、Width 有效）再设置。
            _root = new SplitContainer();
            _root.Dock = DockStyle.Fill;
            _root.Orientation = Orientation.Vertical;
            _root.FixedPanel = FixedPanel.Panel1;
            _root.BackColor = Color.FromArgb(226, 231, 238);
            Controls.Add(_root);

            _inner = new SplitContainer();
            _inner.Dock = DockStyle.Fill;
            _inner.Orientation = Orientation.Vertical;
            _inner.FixedPanel = FixedPanel.Panel2;
            _inner.BackColor = Color.FromArgb(226, 231, 238);
            _root.Panel2.Controls.Add(_inner);

            BuildLeft();
            BuildCenter();
            BuildRight();

            Shown += delegate(object s, EventArgs e) { ApplySplitters(); };
            Resize += delegate(object s, EventArgs e)
            {
                if (!_splittersReady) return;
                try { SafeSplit(_inner, Math.Max(400, _inner.Width - 350)); }
                catch { }
            };
        }

        private bool _splittersReady = false;

        private void ApplySplitters()
        {
            try
            {
                _root.Panel1MinSize = 260;
                _root.Panel2MinSize = 700;
                SafeSplit(_root, 302);
                _inner.Panel2MinSize = 320;
                SafeSplit(_inner, Math.Max(400, _inner.Width - 350));
                _splittersReady = true;
            }
            catch (Exception ex)
            {
                Log("三栏布局初始化失败（已保留默认分隔）：" + ex.Message);
            }
        }

        /// <summary>把目标分隔位置夹取到 [Panel1MinSize, Width-Panel2MinSize] 合法区间。</summary>
        private static void SafeSplit(SplitContainer sc, int desired)
        {
            int min = sc.Panel1MinSize;
            int max = sc.Width - sc.Panel2MinSize;
            if (max < min) return;
            sc.SplitterDistance = Math.Max(min, Math.Min(desired, max));
        }

        // ---------- 通用控件工厂 ----------
        private Label L(Control p, string text, int x, int y, int w)
        {
            Label l = new Label();
            l.Text = text; l.Left = x; l.Top = y + 3; l.Width = w;
            l.Font = _ui; l.ForeColor = Color.FromArgb(55, 63, 75);
            p.Controls.Add(l); return l;
        }

        private TextBox T(Control p, string text, int x, int y, int w, int h, bool multi)
        {
            TextBox t = new TextBox();
            t.Text = text; t.Left = x; t.Top = y; t.Width = w; t.Height = h;
            t.Font = _ui; t.Multiline = multi;
            t.ScrollBars = multi ? ScrollBars.Both : ScrollBars.None;
            t.WordWrap = false; t.AcceptsReturn = multi;
            t.BorderStyle = BorderStyle.FixedSingle;
            p.Controls.Add(t); return t;
        }

        private NumericUpDown N(Control p, double val, double min, double max, int dec, int x, int y, int w)
        {
            NumericUpDown n = new NumericUpDown();
            n.Left = x; n.Top = y; n.Width = w; n.Font = _ui;
            n.DecimalPlaces = dec;
            n.Minimum = (decimal)min; n.Maximum = (decimal)max;
            n.Value = (decimal)Math.Max(min, Math.Min(max, val));
            n.Increment = dec > 0 ? (decimal)0.1 : 1;
            p.Controls.Add(n); return n;
        }

        private ComboBox C(Control p, string[] items, int idx, int x, int y, int w)
        {
            ComboBox c = new ComboBox();
            c.Left = x; c.Top = y; c.Width = w; c.Font = _ui;
            c.DropDownStyle = ComboBoxStyle.DropDownList;
            c.Items.AddRange(items);
            if (idx >= 0 && idx < c.Items.Count) c.SelectedIndex = idx;
            p.Controls.Add(c); return c;
        }

        private CheckBox K(Control p, string text, bool val, int x, int y, int w)
        {
            CheckBox k = new CheckBox();
            k.Text = text; k.Checked = val; k.Left = x; k.Top = y;
            k.Width = w; k.Font = _ui; k.ForeColor = Color.FromArgb(55, 63, 75);
            p.Controls.Add(k); return k;
        }

        private Button B(Control p, string text, int x, int y, int w, int h)
        {
            Button b = new Button();
            b.Text = text; b.Left = x; b.Top = y; b.Width = w; b.Height = h;
            b.Font = _ui; b.FlatStyle = FlatStyle.Flat;
            b.BackColor = Color.White; b.ForeColor = Color.FromArgb(40, 48, 60);
            b.FlatAppearance.BorderColor = Color.FromArgb(200, 208, 218);
            p.Controls.Add(b); return b;
        }

        private GroupBox G(Control p, string text, int x, int y, int w, int h)
        {
            GroupBox g = new GroupBox();
            g.Text = text; g.Left = x; g.Top = y; g.Width = w; g.Height = h;
            g.Font = _ui; g.ForeColor = Color.FromArgb(70, 80, 95);
            p.Controls.Add(g); return g;
        }

        private TrackBar S(Control p, int val, int x, int y, int w)
        {
            TrackBar s = new TrackBar();
            s.Left = x; s.Top = y; s.Width = w; s.Height = 40;
            s.Minimum = 0; s.Maximum = 100; s.Value = Math.Max(0, Math.Min(100, val));
            s.TickFrequency = 10;
            p.Controls.Add(s); return s;
        }

        // ---------- 左栏 ----------
        private void BuildLeft()
        {
            _leftTabs = new TabControl();
            _leftTabs.Dock = DockStyle.Fill; _leftTabs.Font = _ui;
            _root.Panel1.Controls.Add(_leftTabs);

            TabPage doc = new TabPage("文稿");
            doc.AutoScroll = true; doc.BackColor = Color.White; doc.Font = _ui;
            _leftTabs.TabPages.Add(doc);

            L(doc, "输入文稿", 10, 8, 260);
            _txt = T(doc, "写字机工作台\n基于开源 GRBL-Plotter 二次开发。\n免登录，完全离线，中文手写文稿一键生成 GCode。\nHello 123，拼音：ā á ǎ à。", 10, 28, 262, 190, true);

            L(doc, "稿纸模板", 10, 228, 70);
            _tplKind = C(doc, TPL_LABELS, 0, 85, 225, 180);
            L(doc, "文字方向", 10, 258, 70);
            _direction = C(doc, new string[] { "横排", "竖排（右起）" }, 0, 85, 255, 180);

            L(doc, "字号 mm", 10, 290, 70);
            _fontSize = N(doc, 10, 3, 60, 1, 85, 287, 70);
            L(doc, "字距 mm", 160, 290, 60);
            _charGap = N(doc, 1, 0, 20, 1, 220, 287, 52);
            L(doc, "行距 mm", 10, 320, 70);
            _lineGap = N(doc, 6, 0, 40, 1, 85, 317, 70);
            L(doc, "首行缩进", 160, 320, 60);
            _firstIndent = N(doc, 0, 0, 50, 1, 220, 317, 52);

            _drawGrid = K(doc, "把格线写入 GCode（机器画出）", false, 10, 350, 262);
            _showGrid = K(doc, "画布显示格线", true, 10, 374, 120);
            _center = K(doc, "居中", false, 140, 374, 70);
            _justify = K(doc, "两端对齐", false, 210, 374, 62);

            L(doc, "拉丁字体", 10, 404, 70);
            _latinFont = C(doc, new string[] { "latin.zip（工整）", "latin_script.zip（连笔）" }, 0, 85, 401, 180);

            _btnGenerate = B(doc, "生成 GCode / 预览", 10, 438, 262, 34);
            _btnGenerate.BackColor = Color.FromArgb(40, 110, 190);
            _btnGenerate.ForeColor = Color.White;
            _btnGenerate.FlatAppearance.BorderColor = Color.FromArgb(40, 110, 190);
            _btnExport = B(doc, "导出 GCode 文件", 10, 478, 126, 30);
            _btnSaveCfg = B(doc, "保存配置", 146, 478, 60, 30);
            _btnLoadCfg = B(doc, "读取配置", 212, 478, 60, 30);

            _infoLbl = L(doc, "就绪", 10, 518, 262);
            _infoLbl.ForeColor = Color.FromArgb(90, 100, 115);

            TabPage font = new TabPage("字库");
            font.AutoScroll = true; font.BackColor = Color.White; font.Font = _ui;
            _leftTabs.TabPages.Add(font);

            _fontLbl = L(font, "加载中…", 10, 10, 262);
            _fontLbl.Height = 46;
            _preferLocal = K(font, "本机手写库优先（真人风格）", true, 10, 62, 262);
            L(font, "字库回退链（双击可禁用/启用）", 10, 90, 262);
            _fontList = new ListBox();
            _fontList.Left = 10; _fontList.Top = 112; _fontList.Width = 262;
            _fontList.Height = 130; _fontList.Font = _ui;
            font.Controls.Add(_fontList);
            _btnToggleFont = B(font, "启用 / 禁用选中字库", 10, 250, 150, 28);
            _btnReloadFonts = B(font, "重新加载", 170, 250, 102, 28);
            L(font,
              "格式：InkPen Glyph v1（ZIP + 码点命名 + 二进制笔画）。\n"
              + "注意：与奎享 / WriterStudio 的 .gfont 同名，兼容关系未实测；\n"
              + "本软件只导入本机奎享 .gfont，不产出 .gfont。", 10, 290, 262).Height = 76;
        }

        // ---------- 中栏 ----------
        private void BuildCenter()
        {
            Panel center = new Panel();
            center.Dock = DockStyle.Fill;
            center.BackColor = Color.FromArgb(240, 242, 246);
            _inner.Panel1.Controls.Add(center);

            _tools = new ToolStrip();
            _tools.Dock = DockStyle.Top;
            _tools.GripStyle = ToolStripGripStyle.Hidden;
            _tools.BackColor = Color.FromArgb(250, 251, 253);
            _tools.Font = _ui;
            _tools.Items.Add(new ToolStripLabel("仿真 "));
            _btnPlay = ToolButton("▶ 播放", "开始仿真回放");
            _btnSimPause = ToolButton("⏸ 暂停", "暂停仿真");
            _btnSimStep = ToolButton("⏭ 单笔", "前进一笔");
            _btnSimStop = ToolButton("⏹ 复位", "停止并复位仿真");
            _tools.Items.Add(_btnPlay); _tools.Items.Add(_btnSimPause);
            _tools.Items.Add(_btnSimStep); _tools.Items.Add(_btnSimStop);
            _tools.Items.Add(new ToolStripSeparator());
            _tools.Items.Add(new ToolStripLabel("速度 "));
            ToolStripTextBox speedBox = new ToolStripTextBox();
            speedBox.Text = "12"; speedBox.Width = 42; speedBox.Font = _ui;
            _tools.Items.Add(speedBox);
            _simSpeed = new NumericUpDown();
            _simSpeed.Minimum = 1; _simSpeed.Maximum = 200; _simSpeed.Value = 12;
            _simSpeed.Visible = false;
            speedBox.TextChanged += delegate(object s, EventArgs e)
            {
                int v; if (int.TryParse(speedBox.Text, out v)) _simSpeed.Value = Math.Max(1, Math.Min(200, v));
            };
            _tools.Items.Add(new ToolStripSeparator());
            _btnZoomIn = ToolButton("＋", "放大");
            _btnZoomOut = ToolButton("－", "缩小");
            _btnFit = ToolButton("适应", "适应窗口");
            _tools.Items.Add(_btnZoomIn); _tools.Items.Add(_btnZoomOut); _tools.Items.Add(_btnFit);
            _tools.Items.Add(new ToolStripSeparator());
            _btnPagePrev = ToolButton("◀ 上页", "上一页");
            _pageLbl = new ToolStripLabel("页 1/1");
            _pageLbl.Font = _ui;
            _btnPageNext = ToolButton("下页 ▶", "下一页");
            _tools.Items.Add(_btnPagePrev); _tools.Items.Add(_pageLbl); _tools.Items.Add(_btnPageNext);
            _tools.Items.Add(new ToolStripSeparator());
            ToolStripButton chkUp = new ToolStripButton("抬笔空程");
            chkUp.CheckOnClick = true; chkUp.Checked = true;
            chkUp.CheckedChanged += delegate(object s, EventArgs e)
            { if (_canvas != null) { _canvas.ShowPenUp = chkUp.Checked; _canvas.Invalidate(); } };
            _tools.Items.Add(chkUp);
            center.Controls.Add(_tools);

            _canvas = new PaperCanvas();
            _canvas.Dock = DockStyle.Fill;
            center.Controls.Add(_canvas);
            _canvas.BringToFront();

            Panel bottom = new Panel();
            bottom.Dock = DockStyle.Bottom; bottom.Height = 34;
            bottom.BackColor = Color.FromArgb(250, 251, 253);
            center.Controls.Add(bottom);
            bottom.BringToFront();
            _progress = new ProgressBar();
            _progress.Left = 10; _progress.Top = 8; _progress.Width = 300; _progress.Height = 18;
            bottom.Controls.Add(_progress);
            _progressLbl = new Label();
            _progressLbl.Left = 320; _progressLbl.Top = 9; _progressLbl.Width = 500;
            _progressLbl.Font = _ui; _progressLbl.ForeColor = Color.FromArgb(80, 90, 105);
            _progressLbl.Text = "就绪";
            bottom.Controls.Add(_progressLbl);
        }

        private ToolStripButton ToolButton(string text, string tip)
        {
            ToolStripButton b = new ToolStripButton(text);
            b.ToolTipText = tip; b.Font = _ui;
            b.DisplayStyle = ToolStripItemDisplayStyle.Text;
            return b;
        }

        // ---------- 右栏 ----------
        private void BuildRight()
        {
            _rightTabs = new TabControl();
            _rightTabs.Dock = DockStyle.Fill; _rightTabs.Font = _ui;
            _inner.Panel2.Controls.Add(_rightTabs);

            BuildMachineTab();
            BuildSettingsTab();
            BuildGCodeTab();
            BuildSettingsGridTab();
            BuildLogTab();
        }

        private void BuildMachineTab()
        {
            TabPage p = new TabPage("机器");
            p.AutoScroll = true; p.BackColor = Color.White; p.Font = _ui;
            _rightTabs.TabPages.Add(p);

            GroupBox conn = G(p, "连接", 8, 8, 306, 138);
            L(conn, "端口", 8, 18, 40);
            _portCombo = C(conn, new string[0], -1, 52, 15, 140);
            _portCombo.DropDownStyle = ComboBoxStyle.DropDown;
            _btnRefreshPorts = B(conn, "刷新", 196, 14, 46, 26);
            _btnAutoDetect = B(conn, "自动探测", 246, 14, 52, 26);
            L(conn, "波特率", 8, 48, 50);
            _baudCombo = C(conn, new string[] { "115200", "57600", "38400", "9600" }, 0, 62, 45, 80);
            _baudCombo.DropDownStyle = ComboBoxStyle.DropDown;
            _btnConnect = B(conn, "连接", 150, 44, 70, 28);
            _detectLbl = L(conn, "未探测", 8, 78, 290);
            _detectLbl.Height = 30; _detectLbl.ForeColor = Color.FromArgb(100, 110, 125);
            _statusLbl = L(conn, "状态：未连接", 8, 108, 290);
            _statusLbl.ForeColor = Color.FromArgb(160, 70, 60);

            GroupBox dro = G(p, "实时状态 DRO", 8, 152, 306, 76);
            _droLbl = L(dro, "X 0.00   Y 0.00   Z 0.00\nIdle | Buf 127 | Ov 100%", 8, 18, 290);
            _droLbl.Height = 46;
            _droLbl.Font = new Font("Consolas", 9f);

            GroupBox jog = G(p, "手动 jog / 回零", 8, 234, 306, 168);
            L(jog, "步长", 8, 18, 36);
            _stepCombo = C(jog, new string[] { "0.1", "1", "5", "10", "50" }, 1, 46, 15, 70);
            _btnJogYp = B(jog, "Y+", 122, 14, 42, 26);
            _btnJogXm = B(jog, "X−", 76, 46, 42, 26);
            _btnJogXp = B(jog, "X+", 168, 46, 42, 26);
            _btnJogYm = B(jog, "Y−", 122, 46, 42, 26);
            _btnJogZp = B(jog, "Z+", 216, 32, 38, 26);
            _btnJogZm = B(jog, "Z−", 258, 32, 38, 26);
            _btnHome = B(jog, "$H 回零", 8, 82, 68, 28);
            _btnUnlock = B(jog, "$X 解锁", 80, 82, 68, 28);
            _btnZero = B(jog, "设原点", 152, 82, 62, 28);
            _btnPenTest = B(jog, "试笔", 218, 82, 78, 28);
            _btnReset = B(jog, "软复位", 8, 116, 68, 28);
            _btnEStop = B(jog, "急停", 80, 116, 68, 28);
            _btnEStop.BackColor = Color.FromArgb(205, 60, 55);
            _btnEStop.ForeColor = Color.White;
            _btnOvDown = B(jog, "倍率 −", 152, 116, 62, 28);
            _btnOv100 = B(jog, "100%", 218, 116, 38, 28);
            _btnOvUp = B(jog, "+", 258, 116, 38, 28);

            GroupBox send = G(p, "书写任务", 8, 408, 306, 150);
            _btnStart = B(send, "开始书写", 8, 18, 90, 30);
            _btnStart.BackColor = Color.FromArgb(40, 140, 90);
            _btnStart.ForeColor = Color.White;
            _btnSendPause = B(send, "暂停", 102, 18, 60, 30);
            _btnSendResume = B(send, "继续", 166, 18, 60, 30);
            _btnSendStep = B(send, "单步", 230, 18, 66, 30);
            _btnSendStop = B(send, "停止", 8, 54, 90, 28);
            L(send, "从第几个字续写", 104, 58, 100);
            _resumeChar = N(send, 0, 0, 100000, 0, 206, 55, 60);
            _btnResumeFrom = B(send, "断点续写", 8, 90, 120, 30);
            _btnSendCmd = B(send, "发送", 240, 90, 56, 30);
            _customCmd = T(send, "", 8, 124, 288, 22, false);
            L(send, "自定义指令", 136, 94, 96);
        }

        private void BuildSettingsTab()
        {
            TabPage p = new TabPage("设置");
            p.AutoScroll = true; p.BackColor = Color.White; p.Font = _ui;
            _rightTabs.TabPages.Add(p);

            GroupBox paper = G(p, "纸张与坐标系", 8, 8, 306, 138);
            L(paper, "宽 mm", 8, 20, 50);
            _paperW = N(paper, 210, 50, 1000, 1, 62, 17, 70);
            L(paper, "高 mm", 140, 20, 50);
            _paperH = N(paper, 297, 50, 1000, 1, 190, 17, 70);
            L(paper, "原点角", 8, 50, 50);
            _origin = C(paper, new string[] { "左下", "左上", "右下", "右上" }, 0, 62, 47, 120);
            _invertX = K(paper, "X 反向", false, 190, 48, 60);
            _invertY = K(paper, "Y 反向", false, 250, 48, 52);
            L(paper, "首尾 GCode 模板可在 GCode 页之前设置；Z 参数需真机标定", 8, 82, 290).Height = 40;

            GroupBox z = G(p, "Z 轴步进抬落笔（真机标定）", 8, 152, 306, 166);
            L(z, "安全 Z", 8, 20, 60); _safeZ = N(z, 5, -50, 100, 2, 76, 17, 62);
            L(z, "书写 Z", 148, 20, 60); _writeZ = N(z, 0, -50, 100, 2, 210, 17, 62);
            L(z, "Z 进给", 8, 48, 60); _zFeed = N(z, 300, 1, 10000, 0, 76, 45, 62);
            L(z, "XY 进给", 148, 48, 60); _xyFeed = N(z, 1500, 1, 20000, 0, 210, 45, 62);
            L(z, "落笔延时 s", 8, 76, 76); _dwell = N(z, 0.15, 0, 5, 2, 88, 73, 62);
            L(z, "连笔距离", 158, 76, 66); _connectDist = N(z, 0.8, 0, 10, 2, 224, 73, 62);
            L(z, "路径简化 ε", 8, 104, 76); _eps = N(z, 0.02, 0, 2, 3, 88, 101, 62);
            L(z, "随机种子", 158, 104, 66); _seed = N(z, 20261110, 0, 99999999, 0, 224, 101, 62);
            L(z, "ack 超时 s（0 禁用）", 8, 132, 150); _ackTimeout = N(z, 30, 0, 600, 0, 158, 129, 62);

            GroupBox pert = G(p, "手写感（拖动实时预览）", 8, 324, 306, 196);
            L(pert, "字号抖动", 8, 20, 70); _jSize = S(pert, 0, 78, 14, 210);
            L(pert, "位置抖动", 8, 54, 70); _jPos = S(pert, 0, 78, 48, 210);
            L(pert, "笔画抖动", 8, 88, 70); _jStroke = S(pert, 0, 78, 82, 210);
            L(pert, "基线起伏", 8, 122, 70); _jWave = S(pert, 0, 78, 116, 210);
            L(pert, "笔锋强度", 8, 156, 70); _tail = S(pert, 0, 78, 150, 210);

            GroupBox gc = G(p, "GCode 头 / 尾模板", 8, 526, 306, 150);
            _startGc = T(gc, "G21\nG90\nG94", 8, 18, 286, 50, true);
            _endGc = T(gc, "G0 X0 Y0", 8, 76, 286, 50, true);
        }

        private void BuildGCodeTab()
        {
            TabPage p = new TabPage("GCode");
            p.BackColor = Color.White; p.Font = _ui;
            _rightTabs.TabPages.Add(p);
            L(p, "可直接编辑后从「机器」页开始书写；编辑内容优先于生成结果", 8, 8, 310).Height = 32;
            _gcodeBox = T(p, "", 8, 42, 306, 640, true);
            _gcodeBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            _gcodeBox.Font = new Font("Consolas", 8.5f);
        }

        private void BuildSettingsGridTab()
        {
            TabPage p = new TabPage("$ 参数");
            p.BackColor = Color.White; p.Font = _ui;
            _rightTabs.TabPages.Add(p);
            _btnReadSettings = B(p, "读取 $$ 参数", 8, 8, 120, 28);
            L(p, "双击数值可修改并回写 $n=value", 136, 12, 180);
            _settingsGrid = new DataGridView();
            _settingsGrid.Left = 8; _settingsGrid.Top = 42;
            _settingsGrid.Width = 306; _settingsGrid.Height = 640;
            _settingsGrid.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            _settingsGrid.AllowUserToAddRows = false;
            _settingsGrid.AllowUserToDeleteRows = false;
            _settingsGrid.ReadOnly = true;
            _settingsGrid.ColumnCount = 3;
            _settingsGrid.Columns[0].Name = "参数";
            _settingsGrid.Columns[0].Width = 62;
            _settingsGrid.Columns[1].Name = "值";
            _settingsGrid.Columns[1].Width = 86;
            _settingsGrid.Columns[2].Name = "说明";
            _settingsGrid.Columns[2].Width = 150;
            _settingsGrid.Font = _ui;
            p.Controls.Add(_settingsGrid);
        }

        private void BuildLogTab()
        {
            TabPage p = new TabPage("日志");
            p.BackColor = Color.White; p.Font = _ui;
            _rightTabs.TabPages.Add(p);
            _logBox = T(p, "", 8, 8, 306, 674, true);
            _logBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            _logBox.Font = new Font("Consolas", 8.5f);
            _logBox.ReadOnly = true;
            _logBox.BackColor = Color.FromArgb(250, 251, 253);
        }
    }
}
