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

        // ---- 版面度量 ----
        // 左右两栏同宽（320），卡片 300 宽、内容区 276，全部页面共用这一套坐标，
        // 「标签和输入框对不齐」就是从这里根治的：标题固定在上方一行，输入框固定 26 高，
        // 行距固定 48，不再让 Label 和 NumericUpDown 去抢同一条基线。
        private const int CARD_X = 8;
        private const int CARD_IN = 12;
        private const int CW = 276;          // 卡片内容宽度
        private const int COL2 = 148;        // 双列时第二列的 x
        private const int COLW = 128;        // 双列时每列宽
        private const int ROW_CAP = 13;
        private const int ROW_FIELD = 26;

        private Font _ui;
        private SplitContainer _root, _inner;
        private UiHeader _hdr;
        private UiTabs _leftTabs, _rightTabs;
        private PaperCanvas _canvas;
        private ToolStrip _tools;
        private JobStatusBar _bar;
        private Label _infoLbl, _statusLbl, _droLbl, _fontLbl;

        // 文稿
        private TextBox _txt;
        private ComboBox _tplKind, _direction, _latinFont;
        private NumericUpDown _fontSize, _charGap, _lineGap, _firstIndent;
        private CheckBox _drawGrid, _showGrid, _center, _justify;
        private Button _btnGenerate, _btnExport, _btnSaveCfg, _btnLoadCfg;

        // 字库
        private ListBox _fontList;
        private ComboBox _cjkFont;
        private Button _btnReloadFonts, _btnToggleFont;

        // 机器
        private ComboBox _portCombo, _baudCombo, _stepCombo, _origin;
        private Button _btnRefreshPorts, _btnAutoDetect, _btnConnect;
        private Button _btnHome, _btnUnlock, _btnReset, _btnZero, _btnPenTest, _btnEStop;
        private Button _btnOv100, _btnOvUp, _btnOvDown;
        private Button[] _ovFine;
        private Button _btnJogXp, _btnJogXm, _btnJogYp, _btnJogYm, _btnJogZp, _btnJogZm;
        private TextBox _customCmd;
        private Button _btnSendCmd;
        private Label _detectLbl;
        private CheckBox _autoReconnect;

        // 宏
        private Button[] _macroBtn;
        private Button _btnMacroEdit, _btnMacroReset;

        // 调平（A1 / A5）
        private NumericUpDown _probeMargin, _probeCols, _probeRows, _probeTravel, _probeFeed, _pressDepth;
        private CheckBox _useHeightMap;
        private Button _btnProbeStart, _btnProbeStop, _btnProbeSingle, _btnProbeClear, _btnHmSave, _btnHmLoad;
        private HeatmapView _heat;
        private Label _hmLbl;

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
        private TextBox _gcodeBox;
        private RichTextBox _logBox;
        private ToolStripButton _btnPlay, _btnSimPause, _btnSimStep, _btnSimStop, _btnFit, _btnZoomIn, _btnZoomOut;
        private ToolStripButton _btnPagePrev, _btnPageNext, _btnPenShow, _btnFollow;
        private ToolStripLabel _pageLbl;
        private DataGridView _settingsGrid;
        private TextBox _settingsFilter;
        private Button _btnReadSettings;
        private Button _btnLogClear;
        private CheckBox _chkLogCommOnly;
        private CheckBox _chkDirX, _chkDirY, _chkDirZ;
        private Label _dirLbl, _dirHint;
        private bool _dirSyncing;

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
            Size = new Size(1460, 900);
            MinimumSize = new Size(1180, 700);
            AutoScaleMode = AutoScaleMode.Dpi;
            BackColor = Theme.FormBg;
            Font = _ui;

            // SplitContainer 的 MinSize / SplitterDistance 在构造期宽度尚未确定，
            // 先设会抛 InvalidOperationException（"SplitterDistance 必须在 ... 之间"）。
            // 全部延后到 Shown（布局完成、Width 有效）再设置。
            _root = new SplitContainer();
            _root.Dock = DockStyle.Fill;
            _root.Orientation = Orientation.Vertical;
            _root.FixedPanel = FixedPanel.Panel1;
            _root.BackColor = Theme.FormBg;
            Controls.Add(_root);

            _inner = new SplitContainer();
            _inner.Dock = DockStyle.Fill;
            _inner.Orientation = Orientation.Vertical;
            _inner.FixedPanel = FixedPanel.Panel2;
            _inner.BackColor = Theme.FormBg;
            _root.Panel2.Controls.Add(_inner);
            BuildLeft();
            BuildCenter();
            BuildRight();

            // 顶部工具头最后加入：WinForms 按"后加入者优先"分配 Dock 边，
            // 先加 Fill 再加 Top，Fill 才会让出那一条。
            _hdr = new UiHeader();
            _hdr.Dock = DockStyle.Top;
            Controls.Add(_hdr);

            Shown += delegate(object s, EventArgs e) { ClampToWorkArea(); ApplySplitters(); };
            Resize += delegate(object s, EventArgs e)
            {
                if (!_splittersReady) return;
                try { SafeSplit(_inner, Math.Max(400, _inner.Width - 324)); }
                catch { }
            };
        }

        private bool _splittersReady = false;

        /// <summary>
        /// 窗口默认尺寸在 1366×768 或缩放 150% 的屏上会超出工作区，
        /// 导致底部状态条和右侧面板被屏幕边缘切掉（肉眼像是界面残缺）。
        /// 启动时夹到工作区内，并保证不小于 MinimumSize 所允许的可见区域。
        /// </summary>
        private void ClampToWorkArea()
        {
            Rectangle wa = Screen.FromControl(this).WorkingArea;
            int w = Math.Min(Width, wa.Width - 16);
            int h = Math.Min(Height, wa.Height - 16);
            if (w < MinimumSize.Width) w = Math.Min(MinimumSize.Width, wa.Width);
            if (h < MinimumSize.Height) h = Math.Min(MinimumSize.Height, wa.Height);
            if (w != Width || h != Height) Size = new Size(w, h);
            Location = new Point(wa.X + (wa.Width - w) / 2, wa.Y + (wa.Height - h) / 2);
        }

        private void ApplySplitters()
        {
            try
            {
                _root.Panel1MinSize = 320;
                _root.Panel2MinSize = 760;
                SafeSplit(_root, 320);
                _inner.Panel2MinSize = 320;
                SafeSplit(_inner, Math.Max(400, _inner.Width - 324));
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

        private UiCard Card(Control host, string title, int y, int h)
        {
            UiCard c = new UiCard();
            c.Title = title;
            c.Left = CARD_X;
            c.Top = y;
            c.Height = h;
            c.Width = CW + CARD_IN * 2;
            host.Controls.Add(c);
            return c;
        }

        /// <summary>字段标题：8pt 灰字，独占一行放在输入框上方。</summary>
        private Label Cap(Control p, string text, int x, int y, int w)
        {
            Label l = new Label();
            l.Text = text;
            l.Left = x; l.Top = y; l.Width = w; l.Height = ROW_CAP;
            l.Font = Theme.Font(8f, FontStyle.Regular);
            l.ForeColor = Theme.TextDim;
            l.AutoEllipsis = true;
            p.Controls.Add(l);
            return l;
        }

        private UiField Host(Control p, Control inner, string cap, int x, int y, int w, int h)
        {
            if (cap != null) Cap(p, cap, x, y, w);
            UiField f = new UiField(inner);
            f.Left = x;
            f.Top = y + (cap != null ? ROW_CAP + 2 : 0);
            f.Width = w; f.Height = h;
            p.Controls.Add(f);
            f.LayoutInner();
            return f;
        }

        private NumericUpDown Num(Control p, string cap, double val, double min, double max, int dec,
                                  int x, int y, int w)
        {
            NumericUpDown n = new NumericUpDown();
            n.Font = _ui;
            n.DecimalPlaces = dec;
            n.Minimum = (decimal)min; n.Maximum = (decimal)max;
            n.Value = (decimal)Math.Max(min, Math.Min(max, val));
            n.Increment = dec > 0 ? (decimal)0.1m : 1m;
            n.Width = w;
            Host(p, n, cap, x, y, w, ROW_FIELD);
            return n;
        }

        private ComboBox Cmb(Control p, string cap, string[] items, int idx, int x, int y, int w)
        {
            ComboBox c = new ComboBox();
            c.Font = _ui;
            c.DropDownStyle = ComboBoxStyle.DropDownList;
            c.Items.AddRange(items);
            if (idx >= 0 && idx < c.Items.Count) c.SelectedIndex = idx;
            c.Width = w;
            Host(p, c, cap, x, y, w, ROW_FIELD);
            return c;
        }

        private TextBox Txt(Control p, string cap, string text, int x, int y, int w, int h, bool multi)
        {
            TextBox t = new TextBox();
            t.Text = text;
            t.Font = _ui;
            t.Multiline = multi;
            // 多行输入按宽度自动折行（中文稿件本来就是长句），只留竖向滚动条。
            t.ScrollBars = multi ? ScrollBars.Vertical : ScrollBars.None;
            t.WordWrap = multi; t.AcceptsReturn = multi;
            t.Width = w;
            t.Height = multi ? h : 22;
            Host(p, t, cap, x, y, w, multi ? h + 8 : ROW_FIELD);
            return t;
        }

        private UiSwitch Sw(Control p, string text, bool val, int x, int y, int w)
        {
            UiSwitch k = new UiSwitch();
            k.Text = text; k.Checked = val;
            k.Left = x; k.Top = y; k.Width = w;
            p.Controls.Add(k);
            return k;
        }

        private UiButton Btn(Control p, string text, IconKind icon, int x, int y, int w, int h)
        {
            UiButton b = new UiButton();
            b.Text = text; b.Left = x; b.Top = y; b.Width = w; b.Height = h;
            b.Icon = icon;
            p.Controls.Add(b);
            return b;
        }

        private UiButton PrimaryBtn(UiButton b, UiKind kind)
        {
            b.Kind = kind;
            b.Invalidate();
            return b;
        }

        private TrackBar Slide(Control p, int val, int x, int y, int w)
        {
            TrackBar s = new TrackBar();
            s.Left = x; s.Top = y; s.Width = w; s.Height = 34;
            s.Minimum = 0; s.Maximum = 100; s.Value = Math.Max(0, Math.Min(100, val));
            s.TickFrequency = 10;
            // 刻度线在这套极简版式里纯属噪点，去掉后滑杆和卡片才对得上
            s.TickStyle = TickStyle.None;
            s.BackColor = Theme.Panel;
            p.Controls.Add(s); return s;
        }

        /// <summary>可滚动页容器。卡片宽度跟着可视宽度走，出现滚动条时不会把卡片右边裁掉。</summary>
        private Panel ScrollHost(TabPage page)
        {
            Panel host = new Panel();
            host.Dock = DockStyle.Fill;
            host.AutoScroll = true;
            host.BackColor = Theme.FormBg;
            page.Controls.Add(host);
            host.Resize += delegate { FitCards(host); };
            // 首屏会被"把焦点控件滚进可视区"的逻辑顶下去几十像素，
            // 结果第一张卡的标题被切在页签条下面。页面一露头就把滚动归零。
            page.VisibleChanged += delegate
            {
                if (page.Visible) host.AutoScrollPosition = new Point(0, 0);
            };
            return host;
        }

        /// <summary>内容总高：AutoScroll 靠它决定滚动范围（Padding 在 AutoScroll 下不生效）。</summary>
        private void FinishScroll(Panel host, int contentHeight)
        {
            host.AutoScrollMinSize = new Size(0, contentHeight);
            FitCards(host);
            host.AutoScrollPosition = new Point(0, 0);
        }

        private void FitCards(Panel host)
        {
            if (host == null) return;
            int w = host.ClientSize.Width - CARD_X * 2;
            if (w < 160) return;
            foreach (Control c in host.Controls)
            {
                UiCard card = c as UiCard;
                if (card != null && card.Width != w) card.Width = w;
            }
        }

        // ---------- 左栏 ----------
        private void BuildLeft()
        {
            _leftTabs = new UiTabs();
            _leftTabs.Dock = DockStyle.Fill;
            _leftTabs.Font = _ui;
            _root.Panel1.Controls.Add(_leftTabs);

            BuildDocTab();
            BuildFontTab();
        }

        private TabPage NewPage(string title)
        {
            TabPage p = new TabPage(title);
            p.BackColor = Theme.FormBg;
            p.Font = _ui;
            return p;
        }

        private void BuildDocTab()
        {
            TabPage doc = NewPage("文稿");
            _leftTabs.TabPages.Add(doc);
            Panel host = ScrollHost(doc);
            UiCard src = Card(host, "输入文稿", 8, 226);
            _txt = Txt(src, null, "写字机工作台\n基于开源 GRBL-Plotter 二次开发。\n免登录，完全离线，中文手写文稿一键生成 GCode。\nHello 123，拼音：ā á ǎ à。",
                       CARD_IN, 34, CW, 172, true);
            UiCard lay = Card(host, "版式", 242, 292);
            _tplKind = Cmb(lay, "稿纸模板", TPL_LABELS, 0, CARD_IN, 38, CW);
            _direction = Cmb(lay, "文字方向", new string[] { "横排", "竖排（右起）" }, 0, CARD_IN, 86, CW);
            _fontSize = Num(lay, "字号 mm", 10, 3, 60, 1, CARD_IN, 134, COLW);
            _charGap = Num(lay, "字距 mm", 1, 0, 20, 1, COL2, 134, COLW);
            _lineGap = Num(lay, "行距 mm", 6, 0, 40, 1, CARD_IN, 182, COLW);
            _firstIndent = Num(lay, "首行缩进 mm", 0, 0, 50, 1, COL2, 182, COLW);
            _latinFont = Cmb(lay, "拉丁字体", new string[] { "latin.zip（工整）", "latin_script.zip（连笔）" }, 0, CARD_IN, 230, CW);
            UiCard opt = Card(host, "排版选项", 542, 148);
            _drawGrid = Sw(opt, "把格线写入 GCode（机器画出）", false, CARD_IN, 38, CW);
            _showGrid = Sw(opt, "画布显示格线", true, CARD_IN, 66, 140);
            _center = Sw(opt, "居中", false, CARD_IN, 94, 76);
            _justify = Sw(opt, "两端对齐", false, 96, 94, 96);

            UiCard run = Card(host, "生成与导出", 698, 132);
            _btnGenerate = PrimaryBtn(Btn(run, "生成 GCode / 预览", IconKind.Bolt, CARD_IN, 36, CW, 34), UiKind.Primary);
            _btnExport = Btn(run, "导出 GCode 文件", IconKind.Download, CARD_IN, 80, CW, 32);

            UiCard cfg = Card(host, "配置", 838, 84);
            _btnSaveCfg = Btn(cfg, "保存配置", IconKind.Save, CARD_IN, 34, 130, 30);
            _btnLoadCfg = Btn(cfg, "读取配置", IconKind.Folder, COL2, 34, 128, 30);

            _infoLbl = new Label();
            _infoLbl.Left = CARD_X + 4; _infoLbl.Top = 912;
            _infoLbl.Width = CW; _infoLbl.Height = 44;
            _infoLbl.Font = Theme.Font(8.5f, FontStyle.Regular);
            _infoLbl.ForeColor = Theme.TextDim;
            _infoLbl.Text = "就绪";
            host.Controls.Add(_infoLbl);
            FinishScroll(host, 984);
        }

        private void BuildFontTab()
        {
            TabPage font = NewPage("字库");
            _leftTabs.TabPages.Add(font);
            Panel host = ScrollHost(font);

            UiCard st = Card(host, "字库状态", 8, 96);
            _fontLbl = new Label();
            _fontLbl.Left = CARD_IN; _fontLbl.Top = 36;
            _fontLbl.Width = CW; _fontLbl.Height = 50;
            _fontLbl.Font = Theme.Font(8.5f, FontStyle.Regular);
            _fontLbl.ForeColor = Theme.TextMid;
            _fontLbl.Text = "加载中…";
            st.Controls.Add(_fontLbl);

            UiCard style = Card(host, "中文字形风格", 112, 116);
            _cjkFont = Cmb(style, "换风格后整页重排，已写内容不受影响",
                           new string[] { "加载中…" }, 0, CARD_IN, 36, CW);

            UiCard chain = Card(host, "字库回退链", 236, 216);
            _fontList = new ListBox();
            _fontList.Left = CARD_IN; _fontList.Top = 38;
            _fontList.Width = CW; _fontList.Height = 112;
            _fontList.Font = _ui;
            _fontList.BorderStyle = BorderStyle.FixedSingle;
            _fontList.BackColor = Theme.Panel;
            _fontList.ForeColor = Theme.Text;
            chain.Controls.Add(_fontList);

            Cap(chain, "双击可禁用 / 启用单个字库", CARD_IN, 156, CW);
            _btnToggleFont = Btn(chain, "启用 / 禁用选中", IconKind.None, CARD_IN, 174, 164, 28);
            _btnReloadFonts = Btn(chain, "重新加载", IconKind.Reset, COL2 + 16, 174, 112, 28);

            UiCard note = Card(host, "格式说明", 460, 168);
            Label doc = new Label();
            doc.Left = CARD_IN; doc.Top = 36;
            doc.Width = CW; doc.Height = 122;
            doc.Font = Theme.Font(8.5f, FontStyle.Regular);
            doc.ForeColor = Theme.TextDim;
            doc.Text = "格式：InkPen Glyph v1（ZIP + 码点命名 + 二进制笔画）。\n\n"
                     + "注意：与奎享 / WriterStudio 的 .gfont 同名，兼容关系未实测；"
                     + "本软件只导入本机奎享 .gfont，不产出 .gfont。";
            note.Controls.Add(doc);

            FinishScroll(host, 642);
        }

        // ---------- 中栏 ----------
        private void BuildCenter()
        {
            Panel center = new Panel();
            center.Dock = DockStyle.Fill;
            center.BackColor = Theme.CanvasBgTop;
            _inner.Panel1.Controls.Add(center);

            _canvas = new PaperCanvas();
            _canvas.Dock = DockStyle.Fill;
            center.Controls.Add(_canvas);

            _bar = new JobStatusBar();
            _bar.Dock = DockStyle.Bottom;
            _bar.Height = 76;
            _bar.StateText = "就绪";
            _bar.SubText = "静态校验通过后可先用仿真回放验收，再接真机";
            center.Controls.Add(_bar);

            _tools = new ToolStrip();
            _tools.Dock = DockStyle.Top;
            _tools.AutoSize = false;
            _tools.Height = 40;
            _tools.GripStyle = ToolStripGripStyle.Hidden;
            _tools.Renderer = new BarRenderer();
            _tools.BackColor = Theme.BarBg;
            _tools.Padding = new Padding(8, 4, 8, 4);
            _tools.Font = _ui;
            _tools.ImageScalingSize = new Size(16, 16);
            center.Controls.Add(_tools);

            _tools.Items.Add(new ToolStripLabel("仿真"));
            _btnPlay = Tb(IconKind.Play, "播放", "开始仿真回放");
            _btnSimPause = Tb(IconKind.Pause, "暂停", "暂停仿真");
            _btnSimStep = Tb(IconKind.Step, "单笔", "前进一笔");
            _btnSimStop = Tb(IconKind.Reset, "复位", "停止并复位仿真");
            _tools.Items.Add(_btnPlay); _tools.Items.Add(_btnSimPause);
            _tools.Items.Add(_btnSimStep); _tools.Items.Add(_btnSimStop);
            _tools.Items.Add(new ToolStripSeparator());
            _tools.Items.Add(new ToolStripLabel("速度"));

            ToolStripTextBox speedBox = new ToolStripTextBox();
            speedBox.Text = "12"; speedBox.Width = 36; speedBox.Font = _ui;
            _tools.Items.Add(speedBox);
            _simSpeed = new NumericUpDown();
            _simSpeed.Minimum = 1; _simSpeed.Maximum = 200; _simSpeed.Value = 12;
            _simSpeed.Visible = false;
            speedBox.TextChanged += delegate(object s, EventArgs e)
            {
                int v; if (int.TryParse(speedBox.Text, out v)) _simSpeed.Value = Math.Max(1, Math.Min(200, v));
            };

            _tools.Items.Add(new ToolStripSeparator());
            _btnZoomIn = Tb(IconKind.ZoomIn, "", "放大");
            _btnZoomOut = Tb(IconKind.ZoomOut, "", "缩小");
            _btnFit = Tb(IconKind.Fit, "适应", "适应窗口");
            _tools.Items.Add(_btnZoomIn); _tools.Items.Add(_btnZoomOut); _tools.Items.Add(_btnFit);
            _tools.Items.Add(new ToolStripSeparator());
            _btnPagePrev = Tb(IconKind.Prev, "", "上一页");
            _pageLbl = new ToolStripLabel("页 1/1");
            _pageLbl.Font = _ui;
            _btnPageNext = Tb(IconKind.Next, "", "下一页");
            _tools.Items.Add(_btnPagePrev); _tools.Items.Add(_pageLbl); _tools.Items.Add(_btnPageNext);
            _tools.Items.Add(new ToolStripSeparator());

            ToolStripButton chkUp = new ToolStripButton("空程");
            chkUp.CheckOnClick = true; chkUp.Checked = true;
            chkUp.ToolTipText = "显示抬笔空程虚线";
            chkUp.Font = _ui;
            chkUp.CheckedChanged += delegate(object s, EventArgs e)
            { if (_canvas != null) { _canvas.ShowPenUp = chkUp.Checked; _canvas.Invalidate(); } };
            _tools.Items.Add(chkUp);

            _btnPenShow = Tb(IconKind.Pen, "笔位", "在画布上显示机器实时笔位置（来自 GRBL 状态报文）");
            _btnPenShow.CheckOnClick = true; _btnPenShow.Checked = true;
            _tools.Items.Add(_btnPenShow);

            _btnFollow = Tb(IconKind.Target, "跟随", "笔接近画布边缘时自动平移视图（书写大字幅时有用）");
            _btnFollow.CheckOnClick = true;
            _tools.Items.Add(_btnFollow);

            // 工具条最后加入，Dock=Top 优先拿边；画布 Fill 填剩余。
            _tools.BringToFront();
        }

        private ToolStripButton Tb(IconKind icon, string text, string tip)
        {
            ToolStripButton b = new ToolStripButton(text);
            b.ToolTipText = tip;
            b.Font = _ui;
            b.DisplayStyle = text.Length > 0
                ? ToolStripItemDisplayStyle.ImageAndText
                : ToolStripItemDisplayStyle.Image;
            b.TextImageRelation = TextImageRelation.ImageBeforeText;
            b.Padding = new Padding(4, 0, 4, 0);
            if (icon != IconKind.None) b.Image = Icons.Render(icon, 16, Theme.TextMid);
            return b;
        }

        // ---------- 右栏 ----------
        private void BuildRight()
        {
            _rightTabs = new UiTabs();
            _rightTabs.Dock = DockStyle.Fill;
            _rightTabs.Font = _ui;
            _rightTabs.StripHeight = 30;
            _inner.Panel2.Controls.Add(_rightTabs);

            BuildMachineTab();
            BuildLevelTab();
            BuildSettingsTab();
            BuildGCodeTab();
            BuildSettingsGridTab();
            BuildLogTab();

            // 参数表、日志、GCode 三个数据页要铺满可用宽度。Anchor 在 TabPage 上算的是
            // 「创建时的父尺寸」，页面从未被正确拉伸过，改为显式布局。
            _rightTabs.Resize += delegate { FitRightTab(); };
            _rightTabs.SelectedIndexChanged += delegate { FitRightTab(); };
        }

        /// <summary>让右栏里的「数据展示型」页面铺满可用宽度。</summary>
        private void FitRightTab()
        {
            if (_rightTabs == null || _rightTabs.TabPages.Count == 0) return;
            TabPage p = _rightTabs.SelectedTab;
            if (p == null) return;
            Rectangle r = _rightTabs.DisplayRectangle;
            int w = r.Width, h = r.Height;
            if (w <= 40 || h <= 40) return;

            if (_settingsGrid != null && _settingsGrid.Parent == p)
            {
                _settingsGrid.Width = Math.Max(180, w - 20);
                _settingsGrid.Height = Math.Max(80, h - 78);
                UiField sf = _settingsFilter == null ? null : _settingsFilter.Parent as UiField;
                if (sf != null) sf.Width = Math.Max(90, w - 190);
            }
            if (_logBox != null && _logBox.Parent is UiField)
            {
                UiField f = (UiField)_logBox.Parent;
                f.Width = Math.Max(180, w - 20);
                f.Height = Math.Max(90, h - 84);
                f.LayoutInner();
            }
            if (_gcodeBox != null && _gcodeBox.Parent is UiField)
            {
                UiField f = (UiField)_gcodeBox.Parent;
                f.Width = Math.Max(180, w - 20);
                f.Height = Math.Max(90, h - 86);
                f.LayoutInner();
            }
        }

        private void BuildMachineTab()
        {
            TabPage p = NewPage("机器");
            _rightTabs.TabPages.Add(p);

            Panel host = ScrollHost(p);

            // 书写任务钉在底部：本页唯一的主操作，不该跟着内容滚走。
            UiCard send = new UiCard();
            send.Title = "书写任务";
            send.Dock = DockStyle.Bottom;
            send.Height = 214;
            send.Padding = new Padding(0, 0, 0, 0);
            p.Controls.Add(send);

            _btnStart = PrimaryBtn(Btn(send, "开始书写", IconKind.Play, CARD_IN, 34, CW, 36), UiKind.Primary);
            _btnSendPause = Btn(send, "暂停", IconKind.Pause, CARD_IN, 78, 64, 30);
            _btnSendResume = Btn(send, "继续", IconKind.Play, 80, 78, 60, 30);
            _btnSendStep = Btn(send, "单步", IconKind.Step, 146, 78, 60, 30);
            _btnSendStop = PrimaryBtn(Btn(send, "停止", IconKind.Stop, 212, 78, 64, 30), UiKind.Danger);

            _resumeChar = Num(send, "续写字序号", 0, 0, 100000, 0, CARD_IN, 112, 68);
            _btnResumeFrom = Btn(send, "断点续写", IconKind.Step, 88, 127, 84, 28);
            _customCmd = Txt(send, "自定义指令", "", CARD_IN, 160, 206, 22, false);
            _btnSendCmd = Btn(send, "发送", IconKind.None, 222, 175, 54, 28);

            UiCard conn = Card(host, "连接", 8, 196);
            _portCombo = Cmb(conn, "串口", new string[0], -1, CARD_IN, 34, 148);
            _portCombo.DropDownStyle = ComboBoxStyle.DropDown;
            _btnRefreshPorts = Btn(conn, "刷新", IconKind.Reset, 168, 49, 52, 26);
            _btnAutoDetect = Btn(conn, "探测", IconKind.Target, 226, 49, 62, 26);
            _baudCombo = Cmb(conn, "波特率", new string[] { "115200", "57600", "38400", "9600" }, 0, CARD_IN, 82, 96);
            _baudCombo.DropDownStyle = ComboBoxStyle.DropDown;
            _btnConnect = PrimaryBtn(Btn(conn, "连接", IconKind.Plug, 118, 97, 82, 28), UiKind.Primary);
            _statusLbl = new Label();
            _statusLbl.Left = CARD_IN; _statusLbl.Top = 164; _statusLbl.Width = 110; _statusLbl.Height = 16;
            _statusLbl.Font = Theme.Font(8.5f, FontStyle.Regular);
            _statusLbl.ForeColor = Theme.Danger;
            _statusLbl.Text = "未连接";
            conn.Controls.Add(_statusLbl);
            _autoReconnect = Sw(conn, "断开后自动重连", true, CARD_IN, 132, 170);
            _detectLbl = new Label();
            _detectLbl.Left = 126; _detectLbl.Top = 164; _detectLbl.Width = 162; _detectLbl.Height = 16;
            _detectLbl.Font = Theme.Font(8f, FontStyle.Regular);
            _detectLbl.ForeColor = Theme.TextFaint;
            _detectLbl.Text = "未探测";
            _detectLbl.TextAlign = ContentAlignment.TopRight;
            conn.Controls.Add(_detectLbl);

            UiCard dro = Card(host, "实时位置", 212, 106);
            dro.Badge = "Idle";
            _droLbl = new Label();
            _droLbl.Left = CARD_IN; _droLbl.Top = 36;
            _droLbl.Width = CW; _droLbl.Height = 60;
            _droLbl.Font = Theme.Mono(12f, FontStyle.Regular);
            _droLbl.ForeColor = Theme.Text;
            _droLbl.Text = "X 0.00   Y 0.00   Z 0.00\nIdle | Buf 127 | Ov 100%";
            dro.Controls.Add(_droLbl);

            UiCard jog = Card(host, "手动 jog", 326, 268);
            jog.Hint = "点按走一步";
            _stepCombo = Cmb(jog, "步长 mm", new string[] { "0.1", "1", "5", "10", "50" }, 1, CARD_IN, 34, 86);
            _btnJogZp = Btn(jog, "Z+", IconKind.None, 104, 49, 78, 28);
            _btnJogZm = Btn(jog, "Z−", IconKind.None, 188, 49, 88, 28);

            // 十字方向键：X− / X+ 分列左右，Y+ 在上、Y− 在下。
            // 原来 Y− 和 X− X+ 挤在同一行，看上去像少了一条腿的「＋」，方向直觉是错的。
            _btnJogYp = Btn(jog, "Y+", IconKind.None, 96, 96, 84, 32);
            _btnJogXm = Btn(jog, "X−", IconKind.None, CARD_IN, 134, 84, 32);
            _btnJogXp = Btn(jog, "X+", IconKind.None, 180, 134, 84, 32);
            _btnJogYm = Btn(jog, "Y−", IconKind.None, 96, 172, 84, 32);

            _btnPenTest = Btn(jog, "试笔", IconKind.Pen, CARD_IN, 216, 84, 30);
            _btnZero = Btn(jog, "XY 零点", IconKind.Target, 100, 216, 84, 30);
            _btnUnlock = Btn(jog, "$X 解锁", IconKind.Unlock, 188, 216, 88, 30);

            UiCard safe = Card(host, "回零与复位", 602, 84);
            _btnHome = Btn(safe, "$H 回零", IconKind.Home, CARD_IN, 34, 100, 30);
            _btnReset = Btn(safe, "软复位", IconKind.Reset, 118, 34, 76, 30);
            _btnEStop = PrimaryBtn(Btn(safe, "急停", IconKind.Stop, 200, 34, 76, 30), UiKind.Danger);

            UiCard ov = Card(host, "进给倍率", 694, 124);
            ov.Badge = "Ov 100%";
            _btnOvDown = Btn(ov, "−10%", IconKind.None, CARD_IN, 36, 82, 28);
            _btnOv100 = Btn(ov, "100%", IconKind.None, 100, 36, 82, 28);
            _btnOvUp = Btn(ov, "+10%", IconKind.None, 188, 36, 88, 28);
            _ovFine = new Button[4];
            int[] fine = new int[] { -50, -10, 10, 50 };
            for (int i = 0; i < 4; i++)
            {
                _ovFine[i] = Btn(ov, (fine[i] > 0 ? "+" : "−") + Math.Abs(fine[i]) + "%",
                                 IconKind.None, CARD_IN + i * 70, 74, 64, 26);
                _ovFine[i].Tag = fine[i];
            }

            // 电机方向：直接读写 $3 的位掩码（X=1 / Y=2 / Z=4）。
            // 方向反了正确的做法是改 $3，不是重插电机线：换线之后限位开关、
            // 归零方向、手动 jog 的语义会一起反过来，下一次归零更容易撞。
            UiCard dir = Card(host, "电机方向", 826, 150);
            _chkDirX = Sw(dir, "X 反向", false, CARD_IN, 34, 82);
            _chkDirY = Sw(dir, "Y 反向", false, CARD_IN, 58, 82);
            _chkDirZ = Sw(dir, "Z 反向", false, CARD_IN, 82, 82);
            _dirLbl = new Label();
            _dirLbl.Left = CARD_IN; _dirLbl.Top = 118;
            _dirLbl.Width = CW; _dirLbl.Height = 16;
            _dirLbl.Font = Theme.Mono(8.5f, FontStyle.Bold);
            _dirLbl.ForeColor = Theme.TextMid;
            _dirLbl.Text = "$3 = —";
            dir.Controls.Add(_dirLbl);
            _dirHint = new Label();
            _dirHint.Left = 104; _dirHint.Top = 36;
            _dirHint.Width = 184; _dirHint.Height = 72;
            _dirHint.Font = Theme.Font(8f, FontStyle.Regular);
            _dirHint.ForeColor = Theme.TextDim;
            _dirHint.Text = "勾选立即生效并存 EEPROM。\n坐标读数不会跳变，\n只有之后的移动方向反过来。";
            dir.Controls.Add(_dirHint);

            UiCard mac = Card(host, "宏按钮", 984, 152);
            mac.Hint = "双击编辑";
            _macroBtn = new Button[4];
            for (int i = 0; i < 4; i++)
            {
                int col = i % 2, row = i / 2;
                _macroBtn[i] = Btn(mac, "宏 " + (i + 1), IconKind.Bolt, CARD_IN + col * 142, 34 + row * 34, 136, 30);
                _macroBtn[i].Tag = i;
            }
            _btnMacroEdit = Btn(mac, "编辑当前宏", IconKind.Gear, CARD_IN, 108, 136, 30);
            _btnMacroReset = Btn(mac, "恢复默认", IconKind.Reset, 154, 108, 134, 30);

            FinishScroll(host, 1144);
        }

        private void BuildLevelTab()
        {
            TabPage p = NewPage("调平");
            _rightTabs.TabPages.Add(p);
            Panel host = ScrollHost(p);

            UiCard area = Card(host, "探测参数（纸面 mm）", 8, 200);
            _probeMargin = Num(area, "四周内缩", 12, 0, 100, 1, CARD_IN, 34, 84);
            _probeCols = Num(area, "列数", 3, 2, 16, 0, 106, 34, 80);
            _probeRows = Num(area, "行数", 3, 2, 16, 0, 196, 34, 80);
            _probeTravel = Num(area, "下探距离", 8, 1, 50, 1, CARD_IN, 86, 84);
            _probeFeed = Num(area, "探测进给 mm/min", 100, 1, 3000, 0, 106, 86, 170);
            _pressDepth = Num(area, "压入量", 0.3, 0, 3, 2, CARD_IN, 138, 84);
            _useHeightMap = Sw(area, "启用高度图 Z 补偿", false, 106, 154, 170);

            UiCard act = Card(host, "接触式探测", 216, 126);
            act.Badge = "需接好探针";
            _btnProbeStart = PrimaryBtn(Btn(act, "开始探测", IconKind.Play, CARD_IN, 36, 100, 32), UiKind.Primary);
            _btnProbeStop = PrimaryBtn(Btn(act, "停止", IconKind.Stop, 118, 36, 76, 32), UiKind.Danger);
            _btnProbeSingle = Btn(act, "单点对刀", IconKind.Target, 200, 36, 76, 32);
            _btnHmSave = Btn(act, "保存高度图", IconKind.Save, CARD_IN, 80, 112, 28);
            _btnHmLoad = Btn(act, "读取高度图", IconKind.Folder, 128, 80, 96, 28);
            _btnProbeClear = Btn(act, "清除", IconKind.None, 228, 80, 48, 28);

            UiCard hm = Card(host, "高度图", 350, 264);
            hm.Hint = "蓝=低 红=高";
            _heat = new HeatmapView();
            _heat.Left = CARD_IN; _heat.Top = 36;
            _heat.Width = CW; _heat.Height = 176;
            hm.Controls.Add(_heat);
            _hmLbl = new Label();
            _hmLbl.Left = CARD_IN; _hmLbl.Top = 218;
            _hmLbl.Width = CW; _hmLbl.Height = 36;
            _hmLbl.Font = Theme.Font(8f, FontStyle.Regular);
            _hmLbl.ForeColor = Theme.TextDim;
            _hmLbl.Text = "高度图未启用";
            hm.Controls.Add(_hmLbl);

            UiCard tip = Card(host, "调平要点", 622, 96);
            Label l = new Label();
            l.Left = CARD_IN; l.Top = 36; l.Width = CW; l.Height = 52;
            l.Font = Theme.Font(8f, FontStyle.Regular);
            l.ForeColor = Theme.TextDim;
            l.Text = "弹簧浮动笔架：先用「单点对刀」量出当前纸面接触 Z，压入量取 0.2~0.5 mm，"
                   + "让笔尖靠弹力贴纸；纸面明显翘曲时再跑网格探测做逐点补偿。";
            tip.Controls.Add(l);

            FinishScroll(host, 726);
        }

        private void BuildSettingsTab()
        {
            TabPage p = NewPage("设置");
            _rightTabs.TabPages.Add(p);
            Panel host = ScrollHost(p);

            UiCard paper = Card(host, "纸张与坐标系", 8, 186);
            _paperW = Num(paper, "纸宽 mm", 210, 50, 1000, 1, CARD_IN, 34, COLW);
            _paperH = Num(paper, "纸高 mm", 297, 50, 1000, 1, COL2, 34, COLW);
            _origin = Cmb(paper, "原点角（机器 0,0 落在纸的哪个角）",
                          new string[] { "左下", "左上", "右下", "右上" }, 0, CARD_IN, 86, CW);
            _invertX = Sw(paper, "X 反向", false, CARD_IN, 140, 88);
            _invertY = Sw(paper, "Y 反向", false, 108, 140, 88);

            UiCard z = Card(host, "Z 抬落笔（真机标定）", 202, 250);
            _safeZ = Num(z, "安全 Z", 5, -50, 100, 2, CARD_IN, 34, COLW);
            _writeZ = Num(z, "书写 Z", 0, -50, 100, 2, COL2, 34, COLW);
            _zFeed = Num(z, "Z 进给 mm/min", 300, 1, 10000, 0, CARD_IN, 86, COLW);
            _xyFeed = Num(z, "XY 进给 mm/min", 1500, 1, 20000, 0, COL2, 86, COLW);
            _dwell = Num(z, "落笔延时 s", 0.15, 0, 5, 2, CARD_IN, 138, COLW);
            _connectDist = Num(z, "连笔距离 mm", 0.8, 0, 10, 2, COL2, 138, COLW);
            _eps = Num(z, "路径简化 ε mm", 0.02, 0, 2, 3, CARD_IN, 190, COLW);
            _seed = Num(z, "随机种子", 20261110, 0, 99999999, 0, COL2, 190, COLW);

            UiCard ms = Card(host, "通信与稳定性", 460, 92);
            _ackTimeout = Num(ms, "ack 超时 s（0 表示禁用）", 30, 0, 600, 0, CARD_IN, 34, CW);

            UiCard pert = Card(host, "手写感（拖动实时预览）", 560, 252);
            Cap(pert, "字号抖动", CARD_IN, 36, 70); _jSize = Slide(pert, 0, 84, 30, 180);
            Cap(pert, "位置抖动", CARD_IN, 76, 70); _jPos = Slide(pert, 0, 84, 70, 180);
            Cap(pert, "笔画抖动", CARD_IN, 116, 70); _jStroke = Slide(pert, 0, 84, 110, 180);
            Cap(pert, "基线起伏", CARD_IN, 156, 70); _jWave = Slide(pert, 0, 84, 150, 180);
            Cap(pert, "笔锋强度", CARD_IN, 196, 70); _tail = Slide(pert, 0, 84, 190, 180);

            UiCard gc = Card(host, "GCode 头 / 尾模板", 820, 196);
            _startGc = Txt(gc, "头部（每份任务开头）", "G21\nG90\nG94", CARD_IN, 34, CW, 44, true);
            _endGc = Txt(gc, "尾部（每份任务结尾）", "G0 X0 Y0", CARD_IN, 122, CW, 40, true);

            FinishScroll(host, 1024);
        }

        private void BuildGCodeTab()
        {
            TabPage p = NewPage("代码");
            _rightTabs.TabPages.Add(p);
            Label tip = new Label();
            tip.Left = 12; tip.Top = 8; tip.Width = 300; tip.Height = 16;
            tip.Font = Theme.Font(8f, FontStyle.Regular);
            tip.ForeColor = Theme.TextDim;
            tip.Text = "只读预览；权威数据源是 Job.GCode";
            p.Controls.Add(tip);

            _gcodeBox = Txt(p, null, "", 10, 28, 300, 400, true);
            // 只读：这是预览窗，不是编辑器。发送与导出读的是 Job.GCode，
            // 允许手改会让"显示的"和"发出去的"不是同一份东西。
            _gcodeBox.ReadOnly = true;
            _gcodeBox.Font = Theme.Mono(8.5f, FontStyle.Regular);
            _gcodeBox.BackColor = Theme.Panel;
        }

        private void BuildSettingsGridTab()
        {
            TabPage p = NewPage("参数");
            _rightTabs.TabPages.Add(p);
            _btnReadSettings = Btn(p, "读取 $$", IconKind.Reset, 10, 8, 92, 26);
            _settingsFilter = Txt(p, null, "", 108, 8, 130, 22, false);

            Label tip = new Label();
            tip.Left = 10; tip.Top = 42; tip.Width = 280; tip.Height = 16;
            tip.Font = Theme.Font(8f, FontStyle.Regular);
            tip.ForeColor = Theme.TextFaint;
            tip.Text = "双击「值」列可修改并回写 GRBL";
            p.Controls.Add(tip);

            _settingsGrid = new DataGridView();
            _settingsGrid.Left = 10; _settingsGrid.Top = 62;
            _settingsGrid.Width = 300; _settingsGrid.Height = 520;
            _settingsGrid.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            _settingsGrid.AllowUserToAddRows = false;
            _settingsGrid.AllowUserToDeleteRows = false;
            _settingsGrid.AllowUserToResizeRows = false;
            _settingsGrid.ReadOnly = true;
            // 行头（那个空白列）默认有 43px 宽，把「说明」列整个挤出可视区，
            // 于是 46 项参数里凡是需要文字解释的都看不全——而这一页的全部价值就是那些解释。
            _settingsGrid.RowHeadersVisible = false;
            _settingsGrid.ScrollBars = ScrollBars.Both;
            _settingsGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            _settingsGrid.MultiSelect = false;
            _settingsGrid.ColumnCount = 3;
            _settingsGrid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            _settingsGrid.ColumnHeadersHeight = 28;
            _settingsGrid.RowTemplate.Height = 24;
            _settingsGrid.EnableHeadersVisualStyles = false;
            _settingsGrid.ColumnHeadersDefaultCellStyle.BackColor = Theme.CardBg;
            _settingsGrid.ColumnHeadersDefaultCellStyle.ForeColor = Theme.TextMid;
            _settingsGrid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Theme.CardBg;
            _settingsGrid.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.False;
            _settingsGrid.Columns[0].Name = "参数";
            _settingsGrid.Columns[0].Width = 56;
            _settingsGrid.Columns[0].SortMode = DataGridViewColumnSortMode.NotSortable;
            _settingsGrid.Columns[1].Name = "值";
            _settingsGrid.Columns[1].Width = 80;
            _settingsGrid.Columns[1].SortMode = DataGridViewColumnSortMode.NotSortable;
            _settingsGrid.Columns[2].Name = "说明";
            // 说明列吃掉剩余宽度，窗口拉宽时跟着变宽
            _settingsGrid.Columns[2].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            _settingsGrid.DefaultCellStyle.WrapMode = DataGridViewTriState.False;
            _settingsGrid.DefaultCellStyle.SelectionBackColor = Theme.AccentSoft;
            _settingsGrid.DefaultCellStyle.SelectionForeColor = Theme.Text;
            _settingsGrid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(250, 251, 253);
            _settingsGrid.GridColor = Theme.Border;
            _settingsGrid.Font = _ui;
            _settingsGrid.BackgroundColor = Theme.Panel;
            _settingsGrid.BorderStyle = BorderStyle.None;
            _settingsGrid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            p.Controls.Add(_settingsGrid);
        }

        private void BuildLogTab()
        {
            TabPage p = NewPage("日志");
            _rightTabs.TabPages.Add(p);

            _btnLogClear = Btn(p, "清空", IconKind.None, 10, 8, 56, 26);
            _chkLogCommOnly = Sw(p, "只看通信", false, 74, 10, 96);
            Label tip = new Label();
            tip.Left = 176; tip.Top = 13; tip.Width = 130; tip.Height = 16;
            tip.Font = Theme.Font(8f, FontStyle.Regular);
            tip.ForeColor = Theme.TextFaint;
            tip.Text = "红=错误 绿=ok";
            tip.TextAlign = ContentAlignment.TopRight;
            p.Controls.Add(tip);

            // 日志按来源上色——这不是美化，是让异常一眼可见。
            _logBox = new RichTextBox();
            _logBox.Font = Theme.Mono(8.5f, FontStyle.Regular);
            _logBox.ReadOnly = true;
            _logBox.BackColor = Theme.Panel;
            _logBox.BorderStyle = BorderStyle.None;
            _logBox.DetectUrls = false;
            _logBox.WordWrap = true;
            _logBox.ScrollBars = RichTextBoxScrollBars.Vertical;
            _logBox.ShortcutsEnabled = true;

            UiField f = new UiField(_logBox);
            f.Left = 10; f.Top = 40; f.Width = 300; f.Height = 520;
            f.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            p.Controls.Add(f);
            f.LayoutInner();
        }
    }
}
