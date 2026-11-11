using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using InkPen.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace InkPen.Tests
{
    /// <summary>
    /// 流控中止逻辑测试。不发真数据：GrblClient 未连接时 SendCommand 只记日志，
    /// 因此这里验证的是「什么情况下应当中止、且不重发」。
    /// </summary>
    [TestClass]
    public class StreamControllerTests
    {
        private static bool WaitUntil(Func<bool> cond, int timeoutMs)
        {
            Stopwatch sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                if (cond()) return true;
                Thread.Sleep(20);
            }
            return cond();
        }

        [TestMethod]
        public void Start_RejectsLineOverSingleLineBuffer()
        {
            GrblClient c = new GrblClient();
            StreamController sc = new StreamController(c);
            sc.AckTimeoutMs = 0;

            string reported = null;
            sc.OnError += delegate(string s) { reported = s; };

            List<string> lines = new List<string>();
            lines.Add("G1 X" + new string('1', 120));   // 压缩后远超 80 字节

            sc.Start(lines, 0);

            Assert.IsTrue(WaitUntil(delegate { return !sc.IsRunning; }, 3000), "作业应中止并退出线程");
            Assert.IsTrue(sc.LastError.IndexOf("单行缓冲", StringComparison.Ordinal) >= 0,
                          "应报告单行缓冲超限，实际：" + sc.LastError);
            Assert.IsNotNull(reported, "OnError 必须通知 UI");
            Assert.IsTrue(sc.FailedLineNumber >= 1, "应记录失败行号供续写");
            Assert.AreEqual(0, sc.Sent, "超长行不应被发送");
        }

        [TestMethod]
        public void Start_AbortsOnAckTimeout_InsteadOfHangingForever()
        {
            GrblClient c = new GrblClient();
            StreamController sc = new StreamController(c);
            sc.AckTimeoutMs = 250;

            List<string> lines = new List<string>();
            for (int i = 0; i < 40; i++) lines.Add("G1 X1 Y1");   // 足够塞满 127 字节缓冲

            sc.Start(lines, 0);

            Assert.IsTrue(WaitUntil(delegate { return !sc.IsRunning; }, 6000),
                          "无 ok 返回时必须超时退出，否则真机会永久卡死");
            Assert.IsTrue(sc.LastError.IndexOf("超时", StringComparison.Ordinal) >= 0,
                          "应报告 ack 超时，实际：" + sc.LastError);
            Assert.IsTrue(sc.FailedLineNumber >= 1);
            Assert.AreEqual("G1X1Y1", sc.FailedLine, "失败行应为最早未确认行，且已去掉空格");
        }

        [TestMethod]
        public void Start_SkipsBlankAndCommentOnlyLines()
        {
            GrblClient c = new GrblClient();
            StreamController sc = new StreamController(c);
            sc.AckTimeoutMs = 0;

            List<string> lines = new List<string>();
            lines.Add(";纯注释");
            lines.Add("");
            lines.Add("    ");

            sc.Start(lines, 0);

            Assert.IsTrue(WaitUntil(delegate { return !sc.IsRunning; }, 3000));
            Assert.AreEqual("", sc.LastError, "跳过注释不应报错");
            Assert.AreEqual(3, sc.Sent, "三行都应被计为已处理");
        }

        [TestMethod]
        public void Stop_TerminatesRunningJob()
        {
            GrblClient c = new GrblClient();
            StreamController sc = new StreamController(c);
            sc.AckTimeoutMs = 0;

            List<string> lines = new List<string>();
            for (int i = 0; i < 200; i++) lines.Add("G1 X1 Y1");

            sc.Start(lines, 0);
            Thread.Sleep(80);
            sc.Stop();

            Assert.IsFalse(sc.IsRunning, "Stop 后工作线程必须结束");
        }
    }
}
