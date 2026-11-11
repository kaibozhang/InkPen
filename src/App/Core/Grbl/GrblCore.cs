using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO.Ports;
using System.Text;
using System.Threading;

namespace InkPen.Core
{
    /* 部分算法提取自 GRBL-Plotter (C) Sven Hasemann, GPL-3.0
     * 参考文件：MachineControl/ControlSerialFormStreaming.cs、GrblRelated.cs、
     *           ControlSerialFormConnect.cs、GrblSetupForm.cs
     * 修改：剥离 WinForms / Properties.Settings / NLog 依赖，重写为独立类。
     */

    public enum GrblState { Unknown, Idle, Run, Hold, Jog, Alarm, Door, Check, Home, Sleep, NotConnected }

    public class GrblStatus
    {
        public GrblState State = GrblState.Unknown;
        public double WX, WY, WZ;
        public double MX, MY, MZ;
        public int FeedOverride = 100;
        public int BufferFree = 127;
        public string Raw = "";
    }

    public class GrblSettings
    {
        public Dictionary<int, double> Values = new Dictionary<int, double>();
        public void Parse(string line)
        {
            // "$100=250.000"
            if (!line.StartsWith("$")) return;
            int eq = line.IndexOf('=');
            if (eq < 2) return;
            int id; double v;
            if (int.TryParse(line.Substring(1, eq - 1), out id)
                && double.TryParse(line.Substring(eq + 1), NumberStyles.Float,
                                   CultureInfo.InvariantCulture, out v))
                Values[id] = v;
        }
    }

    /// <summary>串口连接、GRBL 状态解析、实时命令、$ 参数。</summary>
    public class GrblClient : IDisposable
    {
        private SerialPort _port;
        private readonly StringBuilder _rx = new StringBuilder();
        private readonly object _lock = new object();
        private Thread _detectThread;

        public Action<string> OnLog;
        public Action<GrblStatus> OnStatus;
        public Action<string> OnError;
        public Action<bool> OnConnectionChanged;
        /// <summary>协议级致命错误：error: / ALARM: / &lt;Alarm&gt;。由 StreamController 订阅以中止作业。</summary>
        public Action<string> OnStreamFault;
        /// <summary>$n=v 参数行解析完成（用于刷新 $ 参数表）。</summary>
        public Action OnSettingsChanged;

        public bool VerboseLog = true;   // 流控发送时关闭逐行日志，避免上万行刷屏
        public GrblStatus Status = new GrblStatus();
        public GrblSettings Settings = new GrblSettings();
        public string DetectedInfo = "";
        public bool IsOpen { get { return _port != null && _port.IsOpen; } }

        public static string[] GetPorts()
        {
            try { return SerialPort.GetPortNames(); }
            catch { return new string[0]; }
        }

        public static string PortDescription(string port)
        {
            // 注册表 SERIALCOMM / Win32_SerialPort 在本机为空，用 PnP 描述做友好提示
            try
            {
                using (System.Management.ManagementObjectSearcher s =
                    new System.Management.ManagementObjectSearcher(
                        "SELECT Name, DeviceID FROM Win32_PnPEntity WHERE Name LIKE '%(COM%'"))
                {
                    foreach (System.Management.ManagementObject o in s.Get())
                    {
                        string n = Convert.ToString(o["Name"]);
                        if (n != null && n.IndexOf("(" + port + ")", StringComparison.OrdinalIgnoreCase) >= 0)
                            return n;
                    }
                }
            }
            catch { }
            return port;
        }

        public bool Connect(string port, int baud)
        {
            Disconnect();
            try
            {
                _port = new SerialPort(port, baud, Parity.None, 8, StopBits.One);
                _port.Handshake = Handshake.None;
                _port.DtrEnable = true;
                _port.RtsEnable = true;
                _port.ReadTimeout = 500;
                _port.WriteTimeout = 500;
                _port.NewLine = "\n";
                _port.DataReceived += DataReceived;
                _port.Open();
                Thread.Sleep(200);
                SendRealtime(0x18); // soft-reset，等待欢迎信息
                Status.State = GrblState.Unknown;
                if (OnConnectionChanged != null) OnConnectionChanged(true);
                Log("已连接 " + port + " @ " + baud);
                return true;
            }
            catch (Exception ex)
            {
                Log("连接失败: " + ex.Message);
                Disconnect();
                return false;
            }
        }

        public void Disconnect()
        {
            if (_port != null)
            {
                try { _port.DataReceived -= DataReceived; } catch { }
                try { if (_port.IsOpen) _port.Close(); } catch { }
                try { _port.Dispose(); } catch { }
                _port = null;
            }
            Status.State = GrblState.NotConnected;
            if (OnConnectionChanged != null) OnConnectionChanged(false);
        }

        private void DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            try
            {
                string s = _port.ReadExisting();
                lock (_lock) _rx.Append(s);
                ProcessRx();
            }
            catch (Exception ex) { Log("RX error: " + ex.Message); }
        }

        private void ProcessRx()
        {
            List<string> lines = new List<string>();
            lock (_lock)
            {
                // 关键：只取「完整」的行，未收齐的半行必须留在 _rx。
                // 原先一次 ToString()+清空会把 "ok\r\n" 拆成 "o" / "k" 两次事件，
                // ok 永远匹配不上 -> 在途行不释放 -> 缓冲耗尽 -> 真机书写永久卡死。
                while (_rx.Length > 0)
                {
                    string buf = _rx.ToString();
                    if (buf[0] == '<')
                    {
                        // GRBL 状态报告以 '>' 结束，部分固件不跟换行，单独成帧
                        int gt = buf.IndexOf('>');
                        if (gt < 0) break;
                        lines.Add(buf.Substring(0, gt + 1));
                        _rx.Remove(0, gt + 1);
                        while (_rx.Length > 0 && (_rx[0] == '\r' || _rx[0] == '\n')) _rx.Remove(0, 1);
                    }
                    else
                    {
                        int nl = buf.IndexOf('\n');
                        if (nl < 0) break;
                        lines.Add(buf.Substring(0, nl));
                        _rx.Remove(0, nl + 1);
                    }
                }
            }
            foreach (string raw in lines)
            {
                string line = raw.Replace("\r", "").Trim();
                if (line.Length == 0) continue;
                if (VerboseLog && !line.StartsWith("<") && line != "ok") Log("RX " + line);
                if (line.StartsWith("<"))
                {
                    Status = ParseStatus(line);
                    if (OnStatus != null) OnStatus(Status);
                    if (Status.State == GrblState.Alarm)
                        RaiseFault("机器进入 Alarm 状态: " + line);
                }
                else if (line.StartsWith("error:"))
                {
                    RaiseFault(line);
                    if (OnError != null) OnError(line);
                }
                else if (line.StartsWith("ALARM:"))
                {
                    Status.State = GrblState.Alarm;
                    RaiseFault(line);
                    if (OnError != null) OnError(line);
                }
                else if (line.StartsWith("$"))
                {
                    Settings.Parse(line);
                    if (OnSettingsChanged != null) OnSettingsChanged();
                }
                else if (line.IndexOf("Grbl", StringComparison.OrdinalIgnoreCase) >= 0
                         || line.StartsWith("[VER:") || line.StartsWith("[OPT:"))
                {
                    DetectedInfo += line + " ";
                }
                else if (line == "ok")
                {
                    if (OnOk != null) OnOk();
                }
            }
        }

        private void RaiseFault(string reason)
        {
            if (OnStreamFault != null) OnStreamFault(reason);
        }

        public static GrblStatus ParseStatus(string line)
        {
            GrblStatus s = new GrblStatus();
            s.Raw = line;
            string[] f = line.Trim('<', '>').Split('|');
            if (f.Length == 0) return s;
            s.State = ParseState(f[0]);
            for (int i = 1; i < f.Length; i++)
            {
                string seg = f[i];
                if (seg.StartsWith("MPos:")) ParsePos(seg.Substring(5), true, s);
                else if (seg.StartsWith("WPos:")) ParsePos(seg.Substring(5), false, s);
                else if (seg.StartsWith("Ov:")) { string[] o = seg.Substring(3).Split(','); if (o.Length > 0) int.TryParse(o[0], out s.FeedOverride); }
                else if (seg.StartsWith("Bf:")) { string[] b = seg.Substring(3).Split(','); int v; if (b.Length > 0 && int.TryParse(b[0], out v)) s.BufferFree = v; }
            }
            return s;
        }

        private static void ParsePos(string csv, bool machine, GrblStatus s)
        {
            string[] p = csv.Split(',');
            double x, y, z;
            if (p.Length > 0 && double.TryParse(p[0], NumberStyles.Float, CultureInfo.InvariantCulture, out x))
            { if (machine) s.MX = x; else s.WX = x; }
            if (p.Length > 1 && double.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out y))
            { if (machine) s.MY = y; else s.WY = y; }
            if (p.Length > 2 && double.TryParse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture, out z))
            { if (machine) s.MZ = z; else s.WZ = z; }
        }

        private static GrblState ParseState(string s)
        {
            if (s.StartsWith("Idle")) return GrblState.Idle;
            if (s.StartsWith("Run")) return GrblState.Run;
            if (s.StartsWith("Hold")) return GrblState.Hold;
            if (s.StartsWith("Jog")) return GrblState.Jog;
            if (s.StartsWith("Alarm")) return GrblState.Alarm;
            if (s.StartsWith("Door")) return GrblState.Door;
            if (s.StartsWith("Check")) return GrblState.Check;
            if (s.StartsWith("Home")) return GrblState.Home;
            if (s.StartsWith("Sleep")) return GrblState.Sleep;
            return GrblState.Unknown;
        }

        public void SendCommand(string cmd, bool log)
        {
            if (!IsOpen) { Log("未连接，无法发送: " + cmd); return; }
            try { _port.Write(cmd + "\n"); if (log && VerboseLog) Log("TX " + cmd); }
            catch (Exception ex) { Log("TX error: " + ex.Message); }
        }

        public void SendCommand(string cmd) { SendCommand(cmd, true); }

        public void SendRealtime(int b)
        {
            if (!IsOpen) return;
            try { _port.Write(new byte[] { (byte)b }, 0, 1); }
            catch { }
        }

        public event Action OnOk;
        private void RaiseOk() { if (OnOk != null) OnOk(); }

        /// <summary>自动探测：遍历端口 × 常见波特率，读取 $I / 欢迎信息。</summary>
        public void AutoDetect(Action<string, int, string> found, Action<string> progress)
        {
            if (_detectThread != null && _detectThread.IsAlive) return;
            _detectThread = new Thread(delegate()
            {
                int[] bauds = new int[] { 115200, 57600, 9600 };
                string[] ports = GetPorts();
                foreach (string p in ports)
                {
                    foreach (int b in bauds)
                    {
                        if (progress != null) progress("探测 " + p + " @ " + b + " ...");
                        try
                        {
                            using (SerialPort sp = new SerialPort(p, b, Parity.None, 8, StopBits.One))
                            {
                                sp.ReadTimeout = 300; sp.WriteTimeout = 300;
                                sp.DtrEnable = true;
                                sp.Open();
                                Thread.Sleep(250);
                                sp.Write("$I\n");
                                StringBuilder sb = new StringBuilder();
                                DateTime end = DateTime.Now.AddMilliseconds(900);
                                while (DateTime.Now < end)
                                {
                                    try { sb.Append(sp.ReadExisting()); } catch { }
                                    if (sb.ToString().IndexOf("Grbl", StringComparison.OrdinalIgnoreCase) >= 0
                                        || sb.ToString().IndexOf("[VER:", StringComparison.OrdinalIgnoreCase) >= 0) break;
                                    Thread.Sleep(40);
                                }
                                string resp = sb.ToString();
                                sp.Close();
                                if (resp.IndexOf("Grbl", StringComparison.OrdinalIgnoreCase) >= 0
                                    || resp.IndexOf("[VER:", StringComparison.OrdinalIgnoreCase) >= 0
                                    || resp.IndexOf("[OPT:", StringComparison.OrdinalIgnoreCase) >= 0)
                                {
                                    if (found != null) found(p, b, resp.Replace("\r", " ").Replace("\n", " ").Trim());
                                    return;
                                }
                            }
                        }
                        catch { }
                    }
                }
                if (progress != null) progress("未自动识别到 GRBL，请手动选择端口");
            });
            _detectThread.IsBackground = true;
            _detectThread.Start();
        }

        private void Log(string s) { if (OnLog != null) OnLog(s); }

        public void Dispose() { Disconnect(); }
    }

    /// <summary>字符计数流控发送器（GRBL RX buffer 默认 127 字节）。</summary>
    public class StreamController
    {
        private readonly GrblClient _client;
        private Thread _worker;
        private volatile bool _stop = false;
        private volatile bool _pauseReq = false;
        private volatile bool _stepMode = false;
        private volatile bool _stepGo = false;
        private int _bufferFree = 127;
        private List<string> _lines = new List<string>();
        private int _startLine = 0;

        public Action<int, int, string> OnProgress;   // sent, total, state
        public Action<string> OnLog;
        public Action<string> OnError;
        public int BufferSize = 127;
        /// <summary>GRBL 1.1 单行缓冲（LINE_BUFFER_SIZE）默认 80 字节，超长会被静默截断。</summary>
        public int MaxLineBytes = 80;
        /// <summary>等待 ok 的超时（ms）；长 G4 延时/慢速长行程需调大，0 表示禁用。</summary>
        public int AckTimeoutMs = 30000;
        public string LastError = "";
        public int FailedLineNumber = -1;
        public string FailedLine = "";
        public int Sent { get; private set; }
        public int Total { get { return _lines.Count; } }
        public bool IsRunning { get { return _worker != null && _worker.IsAlive; } }
        public bool IsPaused { get; private set; }

        private readonly Queue<int> _sentLengths = new Queue<int>();
        private readonly Queue<string> _pendingLines = new Queue<string>();
        private DateTime _lastAck = DateTime.UtcNow;
        private bool _faulted = false;
        /// <summary>
        /// 作业代号。Stop()/Start() 都会 +1，旧线程发现代号不符就立即退出。
        /// 否则「停止后马上重新开始」时，旧线程会因为 _stop 被复位而继续发上一份 GCode。
        /// </summary>
        private int _generation = 0;

        public StreamController(GrblClient client)
        {
            _client = client;
            _client.OnOk += HandleOk;
            _client.OnStreamFault += HandleFault;
        }

        private void HandleOk()
        {
            // 每条 ok 释放一行占用；字符计数按已发送行长度回补
            lock (this)
            {
                if (_sentLengths.Count > 0)
                    _bufferFree = Math.Min(BufferSize, _bufferFree + _sentLengths.Dequeue());
                if (_pendingLines.Count > 0) _pendingLines.Dequeue();
                _lastAck = DateTime.UtcNow;
            }
        }

        /// <summary>
        /// GRBL 返回 error:/ALARM:/&lt;Alarm&gt; 时中止作业。
        /// 刻意不重发：字符计数流控下在途可能有多行，盲目重发会写出重复笔画。
        /// 只记录「最早未确认行」，由用户从该行续写。
        /// </summary>
        private void HandleFault(string reason)
        {
            if (!IsRunning) return;   // 手动命令报错不属于作业失败
            Abort(reason);
        }

        private void Abort(string reason)
        {
            int inflight;
            lock (this)
            {
                if (_faulted) return;
                _faulted = true;
                _stop = true;
                inflight = _pendingLines.Count;
                FailedLineNumber = Sent - inflight + 1;
                FailedLine = inflight > 0 ? _pendingLines.Peek() : "";
                LastError = reason;
                _sentLengths.Clear();
                _pendingLines.Clear();
                _bufferFree = BufferSize;
            }
            string msg = "流控中止（不重发，避免重复笔画）：" + reason
                       + " | 在途 " + inflight + " 行 | 最早未确认行 #" + FailedLineNumber
                       + (FailedLine.Length > 0 ? ": " + FailedLine : "");
            if (OnError != null) OnError(msg);
            Progress(_lines.Count, "error");
        }

        public void Start(List<string> lines, int startLine)
        {
            Stop();
            _lines = new List<string>(lines);
            _startLine = Math.Max(0, Math.Min(startLine, _lines.Count));
            _stop = false; _pauseReq = false; IsPaused = false;
            _stepMode = false; _stepGo = false;
            _client.VerboseLog = false;   // 上万行 GCode 逐行日志会拖垮 UI
            lock (this)
            {
                _faulted = false;
                _bufferFree = BufferSize;
                _sentLengths.Clear();
                _pendingLines.Clear();
                _lastAck = DateTime.UtcNow;
                LastError = "";
                FailedLineNumber = -1;
                FailedLine = "";
            }
            Sent = _startLine;
            int gen = ++_generation;
            // 快照：旧线程只能看到自己那份列表。否则 Start() 换掉 _lines 后，
            // 尚未退出的旧线程会按旧下标去索引新作业的行，发出乱七八糟的笔画。
            List<string> snapshot = _lines;
            _worker = new Thread(delegate() { Run(gen, snapshot); });
            _worker.IsBackground = true;
            _worker.Start();
        }

        private void Run(int gen, List<string> lines)
        {
            int total = lines.Count;
            int i = _startLine;
            Progress(total, "start");
            while (gen == _generation && !_stop && i < total)
            {
                if (_pauseReq)
                {
                    if (!IsPaused)
                    {
                        IsPaused = true;
                        _client.SendRealtime(0x21); // feed hold
                        Progress(total, "pause");
                    }
                    // 暂停时 GRBL 不回 ok，不能计为 ack 超时
                    lock (this) _lastAck = DateTime.UtcNow;
                    Thread.Sleep(60);
                    continue;
                }
                if (_stepMode && !_stepGo) { Thread.Sleep(30); continue; }

                string raw = lines[i];
                if (string.IsNullOrEmpty(raw) || raw.Trim().Length == 0 || raw.TrimStart().StartsWith(";"))
                { i++; Sent = i; continue; }

                // GRBL 忽略空格；发送压缩行以节省 RX 缓冲，字符计数按实际发送长度。
                // 括号注释内的分号/空格不受影响（GRBL 整段忽略）。
                string wire = raw.Replace(" ", "");
                int need = wire.Length + 1;
                if (MaxLineBytes > 0 && wire.Length > MaxLineBytes)
                {
                    Abort("GCode 第 " + (i + 1) + " 行 " + wire.Length
                        + " 字节超过 GRBL 单行缓冲 " + MaxLineBytes
                        + " 字节（LINE_BUFFER_SIZE），该行会被静默截断写出错字");
                    break;
                }
                if (need > BufferSize)
                {
                    Abort("GCode 第 " + (i + 1) + " 行长度 " + wire.Length
                              + " 超过 RX 缓冲 " + BufferSize + " 字节，GRBL 会丢弃该行");
                    break;
                }
                bool sentNow = false;
                lock (this)
                {
                    if (_bufferFree >= need)
                    {
                        _bufferFree -= need;
                        _sentLengths.Enqueue(need);
                        _pendingLines.Enqueue(wire);
                        _client.SendCommand(wire, false);
                        if (_stepMode) { _stepGo = false; }
                        i++;
                        Sent = i;
                        sentNow = true;
                        if (i % 20 == 0) Progress(total, "run");
                    }
                }
                if (sentNow) continue;

                // 缓冲满：等待 ok。若长时间无任何 ok，判定为失联并中止（不重发）。
                if (AckTimeoutMs > 0)
                {
                    bool timedOut = false;
                    int inflight = 0;
                    lock (this)
                    {
                        inflight = _sentLengths.Count;
                        if (inflight > 0 &&
                            (DateTime.UtcNow - _lastAck).TotalMilliseconds > AckTimeoutMs)
                            timedOut = true;
                    }
                    if (timedOut)
                    {
                        Abort("等待 ok 超时（" + AckTimeoutMs + " ms，在途 " + inflight + " 行）");
                        break;
                    }
                }
                Thread.Sleep(4);
            }
            _client.VerboseLog = true;
            // 已被新作业取代：静默退出，不要发 finish/stop 覆盖新作业的进度
            if (gen != _generation) return;
            // Abort() 已经发过 "error" 进度，这里再发一次会让状态栏重复刷新
            lock (this) { if (_faulted) return; }
            if (!_stop) { IsPaused = false; Progress(total, "finish"); }
            else Progress(total, "stop");
        }

        private void Progress(int total, string state)
        {
            if (OnProgress != null) OnProgress(Sent, total, state);
        }

        public void Pause()
        {
            if (!IsRunning) return;
            _pauseReq = true;
        }

        public void Resume()
        {
            if (!IsRunning) return;
            _pauseReq = false;
            IsPaused = false;
            _client.SendRealtime(0x7E); // cycle start
            Progress(_lines.Count, "resume");
        }

        public void SetStepMode(bool on)
        {
            _stepMode = on;
            _stepGo = false;
        }

        public void Step()
        {
            _stepMode = true;
            _stepGo = true;
            if (IsPaused) Resume();
        }

        public void Stop()
        {
            _stop = true;
            _generation++;          // 让仍在跑的旧线程立刻失效
            if (_worker != null && _worker.IsAlive)
            {
                try { _worker.Join(400); } catch { }
            }
            _worker = null;
            IsPaused = false;
        }

        public void EmergencyStop()
        {
            Stop();
            _client.SendRealtime(0x21);   // feed hold
            Thread.Sleep(60);
            _client.SendRealtime(0x18);   // soft reset
            if (OnLog != null) OnLog("急停：feed hold + soft reset；请重新设原点后再续写");
        }

        private void Log(string s) { if (OnLog != null) OnLog(s); }
    }
}
