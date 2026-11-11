using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using InkPen.Core;
using InkPen.UI;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace InkPen.Tests
{
    /// <summary>
    /// 作业生命周期与分页状态。
    ///
    /// 这里锁的是三个曾经真出过问题的点：
    /// 1) Stop() 只置 _stop=true，Start() 又把它复位 → 未及时退出的旧线程会继续发上一份 GCode。
    ///    现在用作业代号 _generation 兜底，本组测试锁定「重启后只认新作业」这一契约。
    /// 2) 停/启抖动时进度回调会把旧作业的总行数报给新作业，状态栏显示错误的行数。
    /// 3) 多页文稿若不分页过滤，各页笔迹会叠在同一张纸上（画布侧已加 PageIndex，此处锁 PageCount/SetPage）。
    /// </summary>
    [TestClass]
    public class LifecycleTests
    {
        private static bool WaitUntil(Func<bool> cond, int timeoutMs)
        {
            Stopwatch sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                if (cond()) return true;
                Thread.Sleep(15);
            }
            return cond();
        }

        private static List<string> Repeat(int n, string line)
        {
            List<string> l = new List<string>();
            for (int i = 0; i < n; i++) l.Add(line);
            return l;
        }

        // ---------------- StreamController ----------------

        [TestMethod]
        public void Start_AfterStop_ReportsOnlyNewJobTotals()
        {
            GrblClient c = new GrblClient();
            StreamController sc = new StreamController(c);
            sc.AckTimeoutMs = 0;              // 不回 ok 也不中止，模拟真机停在等 ack 的瞬间

            List<string> events = new List<string>();
            sc.OnProgress += delegate(int sent, int total, string state)
            {
                lock (events) events.Add(total + ":" + state);
            };

            List<string> old = Repeat(400, "G1 X1 Y1");
            sc.Start(old, 0);
            WaitUntil(delegate { return sc.Sent >= 5; }, 2000);
            sc.Stop();

            lock (events) events.Clear();      // 丢弃旧作业的 stop 事件，只看新作业

            List<string> fresh = Repeat(5, ";纯注释");
            sc.Start(fresh, 0);
            Assert.IsTrue(WaitUntil(delegate { return !sc.IsRunning; }, 3000), "新作业应很快结束");

            lock (events)
            {
                foreach (string e in events)
                    Assert.IsFalse(e.StartsWith("400:"),
                        "新作业的进度回调里出现了旧作业的总行数 400：" + e);
                Assert.IsTrue(events.Count > 0, "新作业至少要发出一次进度回调");
                Assert.AreEqual("5:finish", events[events.Count - 1], "最后一次应是 finish 且总行数为 5");
            }
            Assert.AreEqual("", sc.LastError);
            Assert.AreEqual(5, sc.Sent);
        }

        [TestMethod]
        public void RapidStopStart_DoesNotCorruptFinalJob()
        {
            GrblClient c = new GrblClient();
            StreamController sc = new StreamController(c);
            sc.AckTimeoutMs = 0;

            for (int k = 0; k < 15; k++)
            {
                sc.Start(Repeat(300, "G1 X1 Y1"), 0);
                Thread.Sleep(4 + (k % 7));
                sc.Stop();
            }

            List<string> final = Repeat(12, ";纯注释");
            sc.Start(final, 0);
            Assert.IsTrue(WaitUntil(delegate { return !sc.IsRunning; }, 3000), "抖动后最后一次作业必须能正常结束");

            Assert.AreEqual("", sc.LastError, "抖动不应让最后一次作业报错");
            Assert.AreEqual(12, sc.Sent, "已处理行数必须只反映最后一次作业");
            Assert.AreEqual(12, sc.Total);
        }

        [TestMethod]
        public void Pause_KeepsIsRunning_SoManualCommandsStayBlocked()
        {
            GrblClient c = new GrblClient();
            StreamController sc = new StreamController(c);
            sc.AckTimeoutMs = 0;

            sc.Start(Repeat(300, "G1 X1 Y1"), 0);
            Assert.IsTrue(WaitUntil(delegate { return sc.Sent > 0; }, 2000));

            sc.Pause();
            Assert.IsTrue(WaitUntil(delegate { return sc.IsPaused; }, 2000), "应进入暂停态");
            Assert.IsTrue(sc.IsRunning, "暂停只是 feed hold，作业仍在跑；手动 jog 必须继续被拦住");

            sc.Stop();
            Assert.IsFalse(sc.IsRunning);
            Assert.IsFalse(sc.IsPaused, "停止后必须清掉暂停标记，否则下次作业一起来就是暂停态");
        }

        [TestMethod]
        public void Stop_WithoutStart_IsSafe()
        {
            GrblClient c = new GrblClient();
            StreamController sc = new StreamController(c);

            sc.Stop();
            sc.Stop();
            Assert.IsFalse(sc.IsRunning);
            Assert.AreEqual(0, sc.Sent);
        }

        [TestMethod]
        public void Start_EmptyList_FinishesWithoutError()
        {
            GrblClient c = new GrblClient();
            StreamController sc = new StreamController(c);
            sc.AckTimeoutMs = 0;

            string last = "";
            sc.OnProgress += delegate(int sent, int total, string state) { last = state; };

            sc.Start(new List<string>(), 0);
            Thread.Sleep(150);

            Assert.AreEqual("finish", last, "空作业应立刻 finish，而不是报错");
            Assert.AreEqual("", sc.LastError);
            Assert.AreEqual(0, sc.Sent);
        }

        [TestMethod]
        public void Start_StartLineBeyondEnd_IsClampedInsteadOfThrowing()
        {
            GrblClient c = new GrblClient();
            StreamController sc = new StreamController(c);
            sc.AckTimeoutMs = 0;

            sc.Start(Repeat(3, ";x"), 999);     // 越界起始行
            Thread.Sleep(150);

            Assert.IsTrue(sc.Sent <= 3, "起始行必须被夹到 [0, 行数]，实际 " + sc.Sent);
            Assert.AreEqual("", sc.LastError);
        }

        // ---------------- 分页 ----------------

        [TestMethod]
        public void Canvas_PageCount_FollowsJobPages()
        {
            Job j = new Job();
            for (int i = 0; i < 3; i++) j.Pages.Add(new Page());

            PaperCanvas cv = new PaperCanvas();
            Assert.AreEqual(1, cv.PageCount, "未装载作业时应显示 1 页，避免出现「页 1/0」");

            cv.SetJob(j);
            Assert.AreEqual(3, cv.PageCount);
            Assert.AreEqual(0, cv.PageIndex, "换作业必须回到第 1 页");
        }

        [TestMethod]
        public void Canvas_SetPage_ClampsToValidRange()
        {
            Job j = new Job();
            for (int i = 0; i < 3; i++) j.Pages.Add(new Page());

            PaperCanvas cv = new PaperCanvas();
            cv.SetJob(j);

            cv.SetPage(99);
            Assert.AreEqual(2, cv.PageIndex, "超出末页应夹到末页");

            cv.SetPage(-5);
            Assert.AreEqual(0, cv.PageIndex, "负数应夹到第 1 页");

            cv.SetPage(1);
            Assert.AreEqual(1, cv.PageIndex);
        }

        [TestMethod]
        public void Paths_CarryPageIndex_SoPagesDoNotOverlap()
        {
            string dir = TestSupport.CreateTempFontDir();
            GlyphRepository fonts = new GlyphRepository();
            try
            {
                fonts.Load(dir, false, "latin.zip");

                Job j = new Job();
                j.Text = new string('A', 50);
                j.Template.Kind = TemplateKind.Ruled;
                j.Template.FontSizeMm = 10;
                j.Template.CharGapMm = 1;
                j.Template.LineGapMm = 6;
                j.Machine.PaperWidthMm = 60;
                j.Machine.PaperHeightMm = 60;
                j.Machine.Seed = 20261110;
                j.Pages = InkPen.Core.LayoutEngine.Layout(j, fonts);
                GCodeBuilder.Build(j, false);

                Assert.IsTrue(j.Pages.Count > 1, "前置条件：这份稿子必须分页");
                Assert.IsTrue(j.Paths.Count > 0, "应产生书写路径");

                int[] perPage = new int[j.Pages.Count];
                foreach (WritePath wp in j.Paths)
                {
                    Assert.IsTrue(wp.PageIndex >= 0 && wp.PageIndex < j.Pages.Count,
                                  "路径页号越界: " + wp.PageIndex);
                    perPage[wp.PageIndex]++;
                }
                for (int i = 0; i < perPage.Length; i++)
                    Assert.IsTrue(perPage[i] > 0, "第 " + (i + 1) + " 页没有任何路径，翻页会是一片空白");

                int sum = 0;
                foreach (int n in perPage) sum += n;
                Assert.AreEqual(j.Paths.Count, sum, "每页路径数之和必须等于总路径数");
            }
            finally
            {
                fonts.Dispose();
                TestSupport.DeleteDir(dir);
            }
        }
    }
}
