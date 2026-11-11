using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using InkPen.Core;

namespace InkPen.UI
{
    public partial class MainForm : Form
    {
        private Job _job;
        private readonly GlyphRepository _fonts = new GlyphRepository();
        private readonly GrblClient _grbl = new GrblClient();
        private StreamController _stream;
        private SimulationPlayer _sim;
        private TemplateConfig _tpl = new TemplateConfig();
        private MachineConfig _cfg = new MachineConfig();
        private string _dataDir;
        private string _configPath;
        private readonly Timer _debounce = new Timer();
        private readonly Timer _settingsDebounce = new Timer();
        private DateTime _sendStart;
        private bool _legacyJog = false;
        private bool _loading = false;

        public MainForm()
        {
            BuildUi();
            _stream = new StreamController(_grbl);
            _sim = new SimulationPlayer(_canvas);
            _dataDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");
            if (!Directory.Exists(Path.Combine(_dataDir, "glyphs")))
            {
                string alt = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "data"));
                if (Directory.Exists(Path.Combine(alt, "glyphs"))) _dataDir = alt;
            }
            _configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config", "config.json");

            _debounce.Interval = 320;
            _debounce.Tick += delegate(object s, EventArgs e) { _debounce.Stop(); Generate(); };

            // $$ 一次会回来几十行 $n=v，逐行刷表会打爆 UI 线程，合并成一次刷新
            _settingsDebounce.Interval = 300;
            _settingsDebounce.Tick += delegate(object s, EventArgs e) { _settingsDebounce.Stop(); RefreshSettingsGrid(); };

            WireEvents();
            LoadConfig(false);
            ApplyConfigToUi();
            LoadFonts();
            RefreshPorts();
            _loading = false;
            RefreshSettingsGrid();
            Generate();     // 首屏直接出预览，避免打开后中栏空白、用户以为没生效
            Log("InkPen 启动完成。数据目录: " + _dataDir);
            Log("提示：先离线仿真 + GCode 静态校验，Z 参数标定后再接真机。");
        }

        private void WireEvents()
        {
            // 文稿与实时预览
            _btnGenerate.Click += delegate { Generate(); };
            _btnExport.Click += delegate { ExportGCode(); };
            _btnSaveCfg.Click += delegate { SaveConfig(); };
            _btnLoadCfg.Click += delegate { LoadConfig(true); ApplyConfigToUi(); LoadFonts(); Generate(); };
            _tplKind.SelectedIndexChanged += delegate { ScheduleGenerate(); };
            _direction.SelectedIndexChanged += delegate { ScheduleGenerate(); };
            _fontSize.ValueChanged += delegate { ScheduleGenerate(); };
            _charGap.ValueChanged += delegate { ScheduleGenerate(); };
            _lineGap.ValueChanged += delegate { ScheduleGenerate(); };
            _firstIndent.ValueChanged += delegate { ScheduleGenerate(); };
            _drawGrid.CheckedChanged += delegate { ScheduleGenerate(); };
            _showGrid.CheckedChanged += delegate { if (_canvas != null) { _canvas.ShowGrid = _showGrid.Checked; _canvas.Invalidate(); } };
            _center.CheckedChanged += delegate { ScheduleGenerate(); };
            _justify.CheckedChanged += delegate { ScheduleGenerate(); };
            _latinFont.SelectedIndexChanged += delegate { LoadFonts(); ScheduleGenerate(); };
            _preferLocal.CheckedChanged += delegate { LoadFonts(); ScheduleGenerate(); };
            _btnReloadFonts.Click += delegate { LoadFonts(); ScheduleGenerate(); };
            _btnToggleFont.Click += delegate { ToggleFont(); };
            _fontList.DoubleClick += delegate { ToggleFont(); };

            // 设置页参数
            _paperW.ValueChanged += delegate { ScheduleGenerate(); };
            _paperH.ValueChanged += delegate { ScheduleGenerate(); };
            _origin.SelectedIndexChanged += delegate { ScheduleGenerate(); };
            _invertX.CheckedChanged += delegate { ScheduleGenerate(); };
            _invertY.CheckedChanged += delegate { ScheduleGenerate(); };
            _safeZ.ValueChanged += delegate { ScheduleGenerate(); };
            _writeZ.ValueChanged += delegate { ScheduleGenerate(); };
            _zFeed.ValueChanged += delegate { ScheduleGenerate(); };
            _xyFeed.ValueChanged += delegate { ScheduleGenerate(); };
            _dwell.ValueChanged += delegate { ScheduleGenerate(); };
            _connectDist.ValueChanged += delegate { ScheduleGenerate(); };
            _eps.ValueChanged += delegate { ScheduleGenerate(); };
            _seed.ValueChanged += delegate { ScheduleGenerate(); };
            _startGc.TextChanged += delegate { ScheduleGenerate(); };
            _endGc.TextChanged += delegate { ScheduleGenerate(); };
            _jSize.ValueChanged += delegate { ScheduleGenerate(); };
            _jPos.ValueChanged += delegate { ScheduleGenerate(); };
            _jStroke.ValueChanged += delegate { ScheduleGenerate(); };
            _jWave.ValueChanged += delegate { ScheduleGenerate(); };
            _tail.ValueChanged += delegate { ScheduleGenerate(); };

            // 仿真
            _btnPlay.Click += delegate
            {
                if (_job == null) return;
                _sim.Start((int)_simSpeed.Value * 8);
            };
            _btnSimPause.Click += delegate { _sim.Pause(); };
            _btnSimStep.Click += delegate { _sim.Step(); };
            _btnSimStop.Click += delegate { _sim.Stop(); _progress.Value = 0; _progressLbl.Text = "仿真已复位"; };
            _sim.OnProgress += delegate(int cur, int total)
            {
                Ui(delegate
                {
                    _progress.Maximum = Math.Max(1, total);
                    _progress.Value = Math.Min(cur, _progress.Maximum);
                    _progressLbl.Text = "仿真 " + cur + " / " + total + " 笔";
                    UpdatePageLabel();
                });
            };
            _sim.OnFinished += delegate { Ui(delegate { _progressLbl.Text = "仿真完成（" + _job.Paths.Count + " 笔）"; }); };
            _btnZoomIn.Click += delegate { _canvas.Zoom = _canvas.Zoom * 1.25; };
            _btnZoomOut.Click += delegate { _canvas.Zoom = _canvas.Zoom / 1.25; };
            _btnFit.Click += delegate { _canvas.Fit(); };
            _btnPagePrev.Click += delegate { _canvas.SetPage(_canvas.PageIndex - 1); UpdatePageLabel(); };
            _btnPageNext.Click += delegate { _canvas.SetPage(_canvas.PageIndex + 1); UpdatePageLabel(); };

            // 连接
            _btnRefreshPorts.Click += delegate { RefreshPorts(); };
            _btnAutoDetect.Click += delegate
            {
                _detectLbl.Text = "自动探测中…";
                _grbl.AutoDetect(delegate(string port, int baud, string info)
                {
                    Ui(delegate
                    {
                        _portCombo.Text = port;
                        _baudCombo.Text = baud.ToString();
                        _detectLbl.Text = "已识别: " + port + " @ " + baud;
                        Log("自动探测: " + info);
                        _legacyJog = info.IndexOf("0.9") >= 0;
                    });
                }, delegate(string s) { Ui(delegate { _detectLbl.Text = s; }); });
            };
            _btnConnect.Click += delegate { ToggleConnect(); };

            // jog
            _btnJogXp.Click += delegate { Jog("X", 1); };
            _btnJogXm.Click += delegate { Jog("X", -1); };
            _btnJogYp.Click += delegate { Jog("Y", 1); };
            _btnJogYm.Click += delegate { Jog("Y", -1); };
            _btnJogZp.Click += delegate { Jog("Z", 1); };
            _btnJogZm.Click += delegate { Jog("Z", -1); };
            _btnHome.Click += delegate { _grbl.SendCommand("$H"); };
            _btnUnlock.Click += delegate { _grbl.SendCommand("$X"); };
            _btnZero.Click += delegate { _grbl.SendCommand("G10 L20 P1 X0 Y0 Z0"); };
            _btnReset.Click += delegate { _grbl.SendRealtime(0x18); };
            _btnEStop.Click += delegate { _stream.EmergencyStop(); };
            _btnOv100.Click += delegate { _grbl.SendRealtime(0x90); };
            _btnOvUp.Click += delegate { _grbl.SendRealtime(0x91); };
            _btnOvDown.Click += delegate { _grbl.SendRealtime(0x92); };
            _btnPenTest.Click += delegate { PenTest(); };
            _btnSendCmd.Click += delegate
            {
                if (ManualBlocked()) return;
                if (_customCmd.Text.Trim().Length > 0) _grbl.SendCommand(_customCmd.Text.Trim());
            };
            _customCmd.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter && _customCmd.Text.Trim().Length > 0)
                {
                    if (!ManualBlocked()) _grbl.SendCommand(_customCmd.Text.Trim());
                    e.SuppressKeyPress = true;
                }
            };

            // 书写任务
            _btnStart.Click += delegate { StartWriting(0); };
            _btnSendPause.Click += delegate { _stream.Pause(); Log("已请求暂停（等待 IDLE）"); };
            _btnSendResume.Click += delegate { _stream.Resume(); Log("继续书写"); };
            _btnSendStep.Click += delegate { _stream.Step(); };
            _btnSendStop.Click += delegate
            {
                _stream.Stop();
                _progressLbl.ForeColor = SystemColors.ControlText;
                Log("已停止");
            };
            _btnResumeFrom.Click += delegate
            {
                if (_job == null) { Log("请先生成 GCode"); return; }
                int ci = (int)_resumeChar.Value;
                int line = GCodeBuilder.FindResumeLine(_job, ci);
                if (line >= _job.GCode.Count)
                {
                    Log("断点续写：字符序号 " + ci + " 已超出本文稿范围（共 "
                        + CountPlacedChars(_job) + " 字），无内容可写");
                    return;
                }
                Log("断点续写：字符序号 " + ci + " → GCode 行 " + (line + 1));
                StartWriting(line);
            };

            // $ 参数
            _btnReadSettings.Click += delegate { _grbl.SendCommand("$$"); };
            _settingsGrid.CellDoubleClick += delegate(object s, DataGridViewCellEventArgs e)
            {
                if (e.RowIndex < 0) return;
                object id = _settingsGrid.Rows[e.RowIndex].Cells[0].Value;
                object val = _settingsGrid.Rows[e.RowIndex].Cells[1].Value;
                if (id == null) return;
                string input = val == null ? "" : val.ToString();
                if (ShowInput("$" + id + " =", ref input) == DialogResult.OK)
                {
                    _grbl.SendCommand("$" + id + "=" + input.Replace(",", "."));
                    Log("写入 $" + id + "=" + input);
                }
            };

            // GRBL 事件
            _grbl.OnLog += delegate(string s) { Log(s); };
            _grbl.OnError += delegate(string s) { Log("!! " + s); };
            _grbl.OnConnectionChanged += delegate(bool open)
            {
                Ui(delegate
                {
                    _btnConnect.Text = open ? "断开" : "连接";
                    _statusLbl.Text = "状态：" + (open ? "已连接" : "未连接");
                    _statusLbl.ForeColor = open ? Color.FromArgb(40, 130, 80) : Color.FromArgb(160, 70, 60);
                    if (!open) { _settingsGrid.Rows.Clear(); _grbl.Settings.Values.Clear(); }
                });
            };
            _grbl.OnSettingsChanged += delegate
            {
                Ui(delegate
                {
                    if (IsDisposed) return;
                    if (!_settingsDebounce.Enabled) _settingsDebounce.Start();
                });
            };
            _grbl.OnStatus += delegate(GrblStatus st)
            {
                Ui(delegate
                {
                    _droLbl.Text = "X " + st.WX.ToString("F2") + "   Y " + st.WY.ToString("F2")
                                 + "   Z " + st.WZ.ToString("F2") + "\n" + st.State
                                 + " | MPos " + st.MX.ToString("F1") + "," + st.MY.ToString("F1")
                                 + " | Bf " + st.BufferFree + " | Ov " + st.FeedOverride + "%";
                });
            };
            _stream.OnProgress += delegate(int sent, int total, string state)
            {
                Ui(delegate
                {
                    _progress.Maximum = Math.Max(1, total);
                    _progress.Value = Math.Min(sent, _progress.Maximum);
                    double elapsed = (DateTime.Now - _sendStart).TotalSeconds;
                    double eta = sent > 0 ? elapsed / sent * (total - sent) : 0;
                    if (state == "error")
                    {
                        _progressLbl.ForeColor = Color.FromArgb(170, 50, 45);
                        _progressLbl.Text = "作业已中止 · " + _stream.LastError
                            + " · 可从 GCode 第 " + _stream.FailedLineNumber + " 行续写";
                    }
                    else
                    {
                        _progressLbl.Text = state + "  " + sent + " / " + total
                            + " 行  ·  已用 " + elapsed.ToString("F0") + "s"
                            + "  ·  预计剩余 " + eta.ToString("F0") + "s";
                    }
                    if (state == "finish") Log("书写完成");
                    if (state == "stop") Log("书写停止");
                });
            };
            _stream.OnLog += delegate(string s) { Log(s); };
            _stream.OnError += delegate(string s) { Log("!! " + s); };

            FormClosing += delegate { try { _stream.Stop(); _grbl.Dispose(); _fonts.Dispose(); } catch { } };
        }

        private void Ui(Action a)
        {
            if (IsDisposed) return;
            if (InvokeRequired) { try { BeginInvoke(a); } catch { } }
            else a();
        }

        private void Log(string s)
        {
            Ui(delegate
            {
                if (_logBox.TextLength > 120000) _logBox.Text = _logBox.Text.Substring(_logBox.TextLength - 60000);
                _logBox.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  " + s + "\n");
            });
        }

        private void UpdatePageLabel()
        {
            if (_pageLbl == null || _canvas == null) return;
            _pageLbl.Text = "页 " + (_canvas.PageIndex + 1) + "/" + _canvas.PageCount;
        }

        private void ScheduleGenerate()
        {
            if (_loading) return;
            _debounce.Stop(); _debounce.Start();
        }

        // ---------- 配置 ----------
        private void ApplyUiToConfig()
        {
            _tpl.Kind = TemplateKindFromIndex(_tplKind.SelectedIndex);
            _tpl.Direction = (TextDirection)_direction.SelectedIndex;
            _tpl.FontSizeMm = (double)_fontSize.Value;
            _tpl.CharGapMm = (double)_charGap.Value;
            _tpl.LineGapMm = (double)_lineGap.Value;
            _tpl.FirstLineIndentMm = (double)_firstIndent.Value;
            _tpl.DrawGrid = _drawGrid.Checked;
            _tpl.ShowGrid = _showGrid.Checked;
            _tpl.CenterVertically = _center.Checked;
            _tpl.Justify = _justify.Checked;
            _tpl.LatinFont = _latinFont.SelectedIndex == 1 ? "latin_script.zip" : "latin.zip";

            _cfg.PaperWidthMm = (double)_paperW.Value;
            _cfg.PaperHeightMm = (double)_paperH.Value;
            _cfg.Origin = (OriginCorner)_origin.SelectedIndex;
            _cfg.InvertX = _invertX.Checked;
            _cfg.InvertY = _invertY.Checked;
            _cfg.SafeZ = (double)_safeZ.Value;
            _cfg.WriteZ = (double)_writeZ.Value;
            _cfg.ZFeed = (double)_zFeed.Value;
            _cfg.XYFeed = (double)_xyFeed.Value;
            _cfg.DwellSec = (double)_dwell.Value;
            _cfg.ConnectDistanceMm = (double)_connectDist.Value;
            _cfg.SimplifyEpsilonMm = (double)_eps.Value;
            _cfg.Seed = (int)_seed.Value;
            _cfg.StartGCode = _startGc.Text;
            _cfg.EndGCode = _endGc.Text;
            _cfg.PreferLocalFont = _preferLocal.Checked;
            _cfg.JitterSize = _jSize.Value / 1000.0;
            _cfg.JitterPosMm = _jPos.Value / 100.0;
            _cfg.JitterStrokeMm = _jStroke.Value / 100.0;
            _cfg.BaselineWaveMm = _jWave.Value / 100.0;
            _cfg.TailStrength = _tail.Value / 500.0;
            _cfg.AckTimeoutMs = (int)_ackTimeout.Value * 1000;
        }

        private void ApplyConfigToUi()
        {
            _loading = true;
            _tplKind.SelectedIndex = TemplateToIndex(_tpl.Kind);
            _direction.SelectedIndex = (int)_tpl.Direction;
            _fontSize.Value = (decimal)Math.Max(3, Math.Min(60, _tpl.FontSizeMm));
            _charGap.Value = (decimal)Math.Max(0, Math.Min(20, _tpl.CharGapMm));
            _lineGap.Value = (decimal)Math.Max(0, Math.Min(40, _tpl.LineGapMm));
            _firstIndent.Value = (decimal)Math.Max(0, Math.Min(50, _tpl.FirstLineIndentMm));
            _drawGrid.Checked = _tpl.DrawGrid;
            _showGrid.Checked = _tpl.ShowGrid;
            _center.Checked = _tpl.CenterVertically;
            _justify.Checked = _tpl.Justify;
            _latinFont.SelectedIndex = _tpl.LatinFont == "latin_script.zip" ? 1 : 0;
            _preferLocal.Checked = _cfg.PreferLocalFont;

            _paperW.Value = (decimal)Clamp(_cfg.PaperWidthMm, 50, 1000);
            _paperH.Value = (decimal)Clamp(_cfg.PaperHeightMm, 50, 1000);
            _origin.SelectedIndex = (int)_cfg.Origin;
            _invertX.Checked = _cfg.InvertX;
            _invertY.Checked = _cfg.InvertY;
            _safeZ.Value = (decimal)Clamp(_cfg.SafeZ, -50, 100);
            _writeZ.Value = (decimal)Clamp(_cfg.WriteZ, -50, 100);
            _zFeed.Value = (decimal)Clamp(_cfg.ZFeed, 1, 10000);
            _xyFeed.Value = (decimal)Clamp(_cfg.XYFeed, 1, 20000);
            _dwell.Value = (decimal)Clamp(_cfg.DwellSec, 0, 5);
            _connectDist.Value = (decimal)Clamp(_cfg.ConnectDistanceMm, 0, 10);
            _eps.Value = (decimal)Clamp(_cfg.SimplifyEpsilonMm, 0, 2);
            _seed.Value = (decimal)Clamp(_cfg.Seed, 0, 99999999);
            _startGc.Text = _cfg.StartGCode;
            _endGc.Text = _cfg.EndGCode;
            _jSize.Value = (int)Clamp(_cfg.JitterSize * 1000, 0, 100);
            _jPos.Value = (int)Clamp(_cfg.JitterPosMm * 100, 0, 100);
            _jStroke.Value = (int)Clamp(_cfg.JitterStrokeMm * 100, 0, 100);
            _jWave.Value = (int)Clamp(_cfg.BaselineWaveMm * 100, 0, 100);
            _tail.Value = (int)Clamp(_cfg.TailStrength * 500, 0, 100);
            _ackTimeout.Value = (decimal)Clamp(_cfg.AckTimeoutMs / 1000, 0, 600);
            _loading = false;
        }

        private static double Clamp(double v, double a, double b) { return Math.Max(a, Math.Min(b, v)); }

        /// <summary>写 JSON 时用无 BOM 的 UTF-8：带 BOM 的文件会让很多外部工具解析失败。</summary>
        private static readonly System.Text.UTF8Encoding Utf8NoBom = new System.Text.UTF8Encoding(false);

        private void SaveConfig()
        {
            try
            {
                ApplyUiToConfig();
                Directory.CreateDirectory(Path.GetDirectoryName(_configPath));
                File.WriteAllText(_configPath,
                    ConfigBinder.Serialize(_tpl, _cfg, _txt.Text), Utf8NoBom);
                Log("配置已保存: " + _configPath);
            }
            catch (Exception ex) { Log("保存配置失败: " + ex.Message); }
        }

        private void LoadConfig(bool showMsg)
        {
            try
            {
                if (!File.Exists(_configPath)) return;
                string text = ConfigBinder.Deserialize(
                    File.ReadAllText(_configPath, System.Text.Encoding.UTF8), _tpl, _cfg);
                if (text != null) _txt.Text = text;
                if (showMsg) Log("配置已读取");
            }
            catch (Exception ex) { Log("读取配置失败: " + ex.Message); }
        }

        // ---------- 字库 ----------
        private void LoadFonts()
        {
            try
            {
                _fonts.Load(Path.Combine(_dataDir, "glyphs"), _preferLocal.Checked,
                            _latinFont.SelectedIndex == 1 ? "latin_script.zip" : "latin.zip");
                // Load() 会重建 pack 并把 Enabled 复位，需重新应用用户的禁用选择
                foreach (string n in _disabledFonts) _fonts.SetEnabled(n, false);
                _fontLbl.Text = "有效覆盖 " + _fonts.Count + " 个码点\n" + _fonts.PackInfo();
                _fontList.Items.Clear();
                foreach (string n in _fonts.ChainNames) _fontList.Items.Add(n);
                foreach (string e in _fonts.LoadErrors) Log("字库: " + e);
            }
            catch (Exception ex) { Log("字库加载失败: " + ex.Message); }
        }

        private readonly HashSet<string> _disabledFonts = new HashSet<string>();

        private void ToggleFont()
        {
            if (_fontList.SelectedItem == null) return;
            string n = _fontList.SelectedItem.ToString();
            bool enable = _disabledFonts.Contains(n);   // 当前禁用 -> 这次启用
            _fonts.SetEnabled(n, enable);
            if (enable) _disabledFonts.Remove(n); else _disabledFonts.Add(n);
            Log("字库 " + n + (enable ? " 已启用" : " 已禁用"));
            _fontLbl.Text = "有效覆盖 " + _fonts.Count + " 个码点\n" + _fonts.PackInfo();
            Generate();
        }

        private bool IsFontEnabled(string n) { return !_disabledFonts.Contains(n); }

        // ---------- 生成 ----------
        private void Generate()
        {
            ApplyUiToConfig();
            try
            {
                Job j = new Job();
                j.Text = _txt.Text;
                j.Template = _tpl;
                j.Machine = _cfg;
                j.Pages = InkPen.Core.LayoutEngine.Layout(j, _fonts);
                GCodeBuilder.Build(j, true);

                GCodeValidator v = new GCodeValidator();
                v.Validate(j);
                _job = j;
                _canvas.ShowGrid = _tpl.ShowGrid;
                _canvas.SetJob(j);
                _sim.SetJob(j);
                UpdatePageLabel();
                _gcodeBox.Text = GCodeBuilder.ToText(j, 0);

                string miss = j.MissingChars.Count == 0 ? "无缺字" : "缺字 " + j.MissingChars.Count + " 个: " + string.Join("", j.MissingChars.ToArray());
                _infoLbl.Text = "页 " + j.Pages.Count + " · 笔画 " + j.Paths.Count
                              + " · GCode " + j.GCode.Count + " 行 · " + miss;
                foreach (string e in v.Errors) Log("校验错误: " + e);
                foreach (string w in v.Warnings) Log("校验警告: " + w);
                _progressLbl.Text = v.Errors.Count == 0
                    ? "静态校验通过（" + v.Warnings.Count + " 条警告）· 可先仿真再真机"
                    : "静态校验发现 " + v.Errors.Count + " 个错误，请检查日志";
            }
            catch (Exception ex)
            {
                Log("生成失败: " + ex);
                _infoLbl.Text = "生成失败，见日志";
            }
        }

        private void ExportGCode()
        {
            if (_job == null) { Log("请先生成 GCode"); return; }
            SaveFileDialog d = new SaveFileDialog();
            d.Filter = "GCode (*.nc;*.gcode)|*.nc;*.gcode|所有文件 (*.*)|*.*";
            d.FileName = "inkpen_job.nc";
            if (d.ShowDialog() != DialogResult.OK) return;
            try
            {
                File.WriteAllText(d.FileName, _gcodeBox.Text, System.Text.Encoding.ASCII);
                string meta = Path.ChangeExtension(d.FileName, ".job.json");
                Dictionary<string, object> o = new Dictionary<string, object>();
                o["text"] = _job.Text;
                o["seed"] = (double)_cfg.Seed;
                o["template"] = ConfigBinder.ToDict(_tpl);
                o["machine"] = ConfigBinder.ToDict(_cfg);
                o["pages"] = (double)_job.Pages.Count;
                o["gcodeLines"] = (double)_job.GCode.Count;
                File.WriteAllText(meta, Json.Stringify(o), Utf8NoBom);
                Log("已导出 " + d.FileName + " 与 " + meta);
            }
            catch (Exception ex) { Log("导出失败: " + ex.Message); }
        }

        // ---------- 机器 ----------
        private void RefreshPorts()
        {
            string cur = _portCombo.Text;
            _portCombo.Items.Clear();
            foreach (string p in GrblClient.GetPorts()) _portCombo.Items.Add(p);
            if (cur.Length > 0) _portCombo.Text = cur;
            else if (_portCombo.Items.Count > 0) _portCombo.SelectedIndex = 0;
            if (_portCombo.Items.Count == 0) _detectLbl.Text = "未发现串口设备";
        }

        private void ToggleConnect()
        {
            if (_grbl.IsOpen) { _grbl.Disconnect(); Log("已断开"); return; }
            string port = _portCombo.Text.Trim();
            int baud;
            if (!int.TryParse(_baudCombo.Text.Trim(), out baud)) baud = 115200;
            if (port.Length == 0) { MessageBox.Show("请选择串口", "提示"); return; }
            _cfg.PortName = port; _cfg.BaudRate = baud;
            if (_grbl.Connect(port, baud))
            {
                Log("连接成功，读取固件信息…");
                _grbl.SendCommand("$I");
                _grbl.SendCommand("$$");
            }
        }

        /// <summary>
        /// 书写过程中禁止手动 jog / 试笔 / 自定义指令：这些命令会插进字符计数流控之间，
        /// 让 ok 与在途行错位，进而多发导致 GRBL 缓冲溢出。
        /// </summary>
        private bool ManualBlocked()
        {
            if (_stream != null && _stream.IsRunning)
            {
                Log("书写进行中，已忽略该手动指令（会打乱字符计数流控）。如需干预请先「暂停」。");
                return true;
            }
            return false;
        }

        private void Jog(string axis, int sign)
        {
            if (!_grbl.IsOpen) { Log("未连接，无法 jog"); return; }
            if (ManualBlocked()) return;
            double d;
            if (!double.TryParse(_stepCombo.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out d))
                d = 1;
            string v = (sign * d).ToString("0.###", CultureInfo.InvariantCulture);
            if (_legacyJog)
            {
                _grbl.SendCommand("G91 G0 " + axis + v + " F" + GCodeBuilder.F(_cfg.XYFeed));
                _grbl.SendCommand("G90");
            }
            else
            {
                _grbl.SendCommand("$J=G91 G21 " + axis + v + " F" + GCodeBuilder.F(_cfg.XYFeed));
            }
        }

        private void PenTest()
        {
            if (!_grbl.IsOpen) { Log("未连接，无法试笔"); return; }
            if (ManualBlocked()) return;
            _grbl.SendCommand("G0 Z" + GCodeBuilder.F(_cfg.SafeZ));
            _grbl.SendCommand("G1 Z" + GCodeBuilder.F(_cfg.WriteZ) + " F" + GCodeBuilder.F(_cfg.ZFeed));
            _grbl.SendCommand("G4 P0.5");
            _grbl.SendCommand("G0 Z" + GCodeBuilder.F(_cfg.SafeZ));
            Log("试笔：抬→落→停 0.5s→抬。请确认笔尖接触与 Z 方向。");
        }

        /// <summary>统计实际排上纸的字数（缺字不计），用于「无内容可写」判定与状态栏显示。</summary>
        public static int CountPlacedChars(Job j)
        {
            if (j == null || j.Pages == null) return 0;
            int n = 0;
            foreach (Page p in j.Pages) n += p.Glyphs.Count;
            return n;
        }

        private void StartWriting(int fromLine)
        {
            if (_job == null || _job.GCode.Count == 0) { Log("请先生成 GCode"); return; }
            if (fromLine >= _job.GCode.Count) { Log("起始行超出 GCode 范围，未发送"); return; }
            if (!_grbl.IsOpen) { MessageBox.Show("串口未连接。请先连接机器，或先用仿真回放验收。", "未连接"); return; }
            DialogResult r = MessageBox.Show(
                "即将开始真机书写。\n\n请确认：\n1) 已完成离线仿真与 GCode 静态校验\n2) Z 安全高度 / 书写高度 / 轴方向已标定\n3) 纸张已固定、笔已装好、原点已设定\n\n继续？",
                "真机书写确认", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning);
            if (r != DialogResult.OK) return;

            List<string> lines = new List<string>();
            string src = fromLine > 0 ? GCodeBuilder.ToText(_job, fromLine) : _gcodeBox.Text;
            string[] arr = src.Replace("\r\n", "\n").Split('\n');
            foreach (string l in arr) if (l.Trim().Length > 0) lines.Add(l.Trim());
            _sendStart = DateTime.Now;
            _stream.AckTimeoutMs = _cfg.AckTimeoutMs;
            _progressLbl.ForeColor = SystemColors.ControlText;
            _stream.Start(lines, 0);
            Log("开始书写：" + lines.Count + " 行，起始行 " + (fromLine + 1)
              + "，ack 超时 " + (_cfg.AckTimeoutMs / 1000) + " s");
        }

        /// <summary>GRBL 1.1 常用 $ 参数中文说明（未列出的显示空，不影响读写）。</summary>
        public static string SettingName(int id)
        {
            switch (id)
            {
                case 0: return "步进脉冲宽度 µs";
                case 1: return "步进空闲延时 ms";
                case 2: return "脉冲电平取反";
                case 3: return "方向取反";
                case 4: return "使能引脚取反";
                case 5: return "限位引脚取反";
                case 6: return "探针引脚取反";
                case 10: return "状态报告选项";
                case 11: return "结点偏差 mm";
                case 12: return "圆弧容差 mm";
                case 13: return "按英寸报告";
                case 20: return "软限位使能";
                case 21: return "硬限位使能";
                case 22: return "回零循环使能";
                case 23: return "回零方向取反";
                case 24: return "回零定位进给";
                case 25: return "回零搜索进给";
                case 26: return "回零开关去抖 ms";
                case 27: return "回零脱离距离 mm";
                case 30: return "主轴最高转速";
                case 31: return "主轴最低转速";
                case 32: return "激光模式";
                case 100: return "X 轴 steps/mm";
                case 101: return "Y 轴 steps/mm";
                case 102: return "Z 轴 steps/mm";
                case 110: return "X 最大速率 mm/min";
                case 111: return "Y 最大速率 mm/min";
                case 112: return "Z 最大速率 mm/min";
                case 120: return "X 加速度 mm/s²";
                case 121: return "Y 加速度 mm/s²";
                case 122: return "Z 加速度 mm/s²";
                case 130: return "X 行程上限 mm";
                case 131: return "Y 行程上限 mm";
                case 132: return "Z 行程上限 mm";
                default: return "";
            }
        }

        private void RefreshSettingsGrid()
        {
            if (IsDisposed || _settingsGrid == null) return;
            try
            {
                List<int> ids = new List<int>(_grbl.Settings.Values.Keys);
                if (ids.Count == 0) return;
                ids.Sort();
                _settingsGrid.Rows.Clear();
                foreach (int id in ids)
                {
                    _settingsGrid.Rows.Add("$" + id,
                        GCodeBuilder.F(_grbl.Settings.Values[id]), SettingName(id));
                }
                Log("$ 参数已刷新：" + ids.Count + " 项（双击「值」可修改并回写）");
            }
            catch (Exception ex) { Log("刷新 $ 参数失败: " + ex.Message); }
        }

        private DialogResult ShowInput(string label, ref string value)
        {
            Form f = new Form();
            f.Text = "修改 GRBL 参数"; f.Font = _ui;
            f.FormBorderStyle = FormBorderStyle.FixedDialog;
            f.StartPosition = FormStartPosition.CenterParent;
            f.ClientSize = new Size(300, 110);
            f.MinimizeBox = f.MaximizeBox = false;
            Label l = new Label(); l.Text = label; l.Left = 12; l.Top = 14; l.Width = 270; f.Controls.Add(l);
            TextBox t = new TextBox(); t.Text = value; t.Left = 12; t.Top = 38; t.Width = 270; f.Controls.Add(t);
            Button ok = new Button(); ok.Text = "确定"; ok.DialogResult = DialogResult.OK;
            ok.Left = 140; ok.Top = 70; ok.Width = 66; f.Controls.Add(ok);
            Button cancel = new Button(); cancel.Text = "取消"; cancel.DialogResult = DialogResult.Cancel;
            cancel.Left = 216; cancel.Top = 70; cancel.Width = 66; f.Controls.Add(cancel);
            f.AcceptButton = ok; f.CancelButton = cancel;
            DialogResult r = f.ShowDialog(this);
            value = t.Text;
            return r;
        }
    }
}
