using System;
using System.Collections.Generic;

namespace InkPen.Core
{
    /// <summary>
    /// 网格探测的执行器：按「抬笔 → 移到点 → G38.2 下探」一块一块发，等 [PRB:] 回报。
    ///
    /// 刻意做成纯状态机、不持有串口：发包与时间都由外部注入，
    /// 这样「探测到一半机器报错 / 超时 / 用户取消」这些分支可以离线测。
    ///
    /// 调用时序：
    ///   Start(steps) → 外部把 Send 接到串口 → 每收到一行调 FeedLine
    ///   → 每个点回调 OnResult → 全部完成回调 OnFinished
    /// </summary>
    public class ProbeRunner
    {
        private List<ProbeStep> _steps = new List<ProbeStep>();
        private int _index = -1;
        private DateTime _stepStart;

        // 已发出 G38.2、但回报还没到的点的序号（FIFO）。
        // GRBL 的 [PRB:] 不带序号，唯一可靠的顺序信息就是「谁先下探谁先回报」。
        // 超时的点虽然已经按失败结案，它那块 G38.2 仍留在 GRBL 队列里，迟到的 PRB 一定会来；
        // 靠这个队列把它认领掉，才不会把它的回报记到下一个点头上（整条链错位一格）。
        private readonly Queue<int> _prbWait = new Queue<int>();

        public bool Running { get; private set; }
        public bool Cancelled { get; private set; }
        public int OkCount { get; private set; }
        public int Total { get { return _steps.Count; } }
        public int Current { get { return _index; } }

        /// <summary>单点超时（ms）。探测距离不长，8~20s 足够；超时按失败处理并继续下一个点。</summary>
        public int TimeoutMs = 20000;

        public Action<string> Send;                       // 发一行 GCode
        public Action<ProbeStep> OnStepStarted;
        public Action<ProbePoint, bool, double> OnResult; // 点、是否成功、PRBZ
        public Action<int, int> OnFinished;               // 成功数、总数

        public void Start(List<ProbeStep> steps)
        {
            _steps = steps ?? new List<ProbeStep>();
            _index = -1;
            OkCount = 0;
            Cancelled = false;
            _prbWait.Clear();
            Running = _steps.Count > 0;
            if (!Running)
            {
                if (OnFinished != null) OnFinished(0, 0);
                return;
            }
            Advance();
        }

        public void Cancel()
        {
            if (!Running) return;
            Cancelled = true;
            Running = false;
            if (OnFinished != null) OnFinished(OkCount, _steps.Count);
        }

        private void Advance()
        {
            _index++;
            if (_index >= _steps.Count)
            {
                Running = false;
                if (OnFinished != null) OnFinished(OkCount, _steps.Count);
                return;
            }
            ProbeStep s = _steps[_index];
            _stepStart = DateTime.UtcNow;
            if (OnStepStarted != null) OnStepStarted(s);
            if (Send != null)
            {
                foreach (string l in s.Lines) Send(l);
                _prbWait.Enqueue(_index);     // 这块 G38.2 必定回报一条 [PRB:]
            }
        }

        /// <summary>喂一行原始回报。返回 true 表示这一行被探测流程消费掉了。</summary>
        public bool FeedLine(string line)
        {
            if (!Running) return false;

            double x, y, z;
            bool ok;
            if (ProbePlanner.TryParsePrb(line, out x, out y, out z, out ok))
            {
                int owner = _prbWait.Count > 0 ? _prbWait.Dequeue() : _index;
                if (owner != _index)
                {
                    // 迟到的回报：它属于已经按超时结案的那个点。吞掉它，不推进——
                    // 当前点的真实回报随后就到，不能提前把它当成已完成。
                    return true;
                }
                ProbeStep s = _steps[_index];
                s.Point.Ok = ok;
                s.Point.Z = z;
                if (ok) OkCount++;
                if (OnResult != null) OnResult(s.Point, ok, z);
                // 探测完必须立刻抬笔，否则下一块会以探测速度贴着纸面横移
                if (Send != null && s.Lines.Count > 0) Send(s.Lines[0]);
                Advance();
                return true;
            }

            // 探测中的 error/ALRM 只影响当前点，不能整批中止——
            // 常见原因是该点纸边悬空导致下探行程不够，其余点仍然有意义。
            if (line != null
                && (line.StartsWith("error:") || line.StartsWith("ALARM:")
                    || line.IndexOf("<Alarm", StringComparison.Ordinal) >= 0))
            {
                ProbeStep s = _steps[_index];
                // 报错的那块已经被 GRBL 丢弃，不会再有 PRB 回来：把它的待回收名额销掉。
                if (_prbWait.Count > 0 && _prbWait.Peek() == _index) _prbWait.Dequeue();
                s.Point.Ok = false;
                s.Point.Z = 0;
                if (OnResult != null) OnResult(s.Point, false, 0);
                if (Send != null && s.Lines.Count > 0) Send(s.Lines[0]);
                Advance();
                return true;
            }
            return false;
        }

        /// <summary>超时检查。由 UI 定时器周期调用。</summary>
        public void Poll(DateTime now)
        {
            if (!Running || TimeoutMs <= 0) return;
            if ((now - _stepStart).TotalMilliseconds < TimeoutMs) return;

            ProbeStep s = _steps[_index];
            // 注意：这里**不**把 _index 从 _prbWait 里摘掉。
            // 那块 G38.2 还在 GRBL 里跑（探针没接触就一直下行），等下真接触了照样回一条 PRB，
            // 留着名额才能把它认领成「已超时的点」而不是下一点的成绩。
            s.Point.Ok = false;
            s.Point.Z = 0;
            if (OnResult != null) OnResult(s.Point, false, 0);
            if (Send != null && s.Lines.Count > 0) Send(s.Lines[0]);
            Advance();
        }
    }
}
