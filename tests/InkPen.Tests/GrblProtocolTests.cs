using System;
using InkPen.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace InkPen.Tests
{
    [TestClass]
    public class GrblProtocolTests
    {
        [TestMethod]
        public void ParseStatus_IdleWithMachineAndWorkPosition()
        {
            GrblStatus s = GrblClient.ParseStatus(
                "<Idle|MPos:1.500,2.250,3.000|FS:0,0|WCO:10.000,20.000,0.000>");

            Assert.AreEqual(GrblState.Idle, s.State);
            Assert.AreEqual(1.5, s.MX, 1e-6);
            Assert.AreEqual(2.25, s.MY, 1e-6);
            Assert.AreEqual(3.0, s.MZ, 1e-6);
        }

        [TestMethod]
        public void ParseStatus_RunWithBufferAndOverride()
        {
            GrblStatus s = GrblClient.ParseStatus("<Run|MPos:1,2,3|Bf:15,128|Ov:120,100,100>");

            Assert.AreEqual(GrblState.Run, s.State);
            Assert.AreEqual(15, s.BufferFree);
            Assert.AreEqual(120, s.FeedOverride);
        }

        [TestMethod]
        public void ParseStatus_WorkPositionSegment()
        {
            GrblStatus s = GrblClient.ParseStatus("<Idle|WPos:5.000,6.000,7.000>");
            Assert.AreEqual(5.0, s.WX, 1e-6);
            Assert.AreEqual(6.0, s.WY, 1e-6);
            Assert.AreEqual(7.0, s.WZ, 1e-6);
            Assert.AreEqual(0.0, s.MX, 1e-6, "没有 MPos 时机器坐标保持默认");
        }

        [TestMethod]
        public void ParseStatus_AllStates()
        {
            Assert.AreEqual(GrblState.Hold, GrblClient.ParseStatus("<Hold:0|MPos:0,0,0>").State);
            Assert.AreEqual(GrblState.Jog, GrblClient.ParseStatus("<Jog|MPos:0,0,0>").State);
            Assert.AreEqual(GrblState.Alarm, GrblClient.ParseStatus("<Alarm|MPos:0,0,0>").State);
            Assert.AreEqual(GrblState.Door, GrblClient.ParseStatus("<Door:1|MPos:0,0,0>").State);
            Assert.AreEqual(GrblState.Home, GrblClient.ParseStatus("<Home|MPos:0,0,0>").State);
            Assert.AreEqual(GrblState.Sleep, GrblClient.ParseStatus("<Sleep|MPos:0,0,0>").State);
            Assert.AreEqual(GrblState.Check, GrblClient.ParseStatus("<Check|MPos:0,0,0>").State);
        }

        [TestMethod]
        public void ParseStatus_Garbage_DoesNotThrow()
        {
            GrblStatus s = GrblClient.ParseStatus("<");
            Assert.IsNotNull(s);
            Assert.AreEqual(GrblState.Unknown, s.State);

            GrblStatus s2 = GrblClient.ParseStatus("<Idle|MPos:bad,bad,bad>");
            Assert.AreEqual(GrblState.Idle, s2.State);
            Assert.AreEqual(0.0, s2.MX, 1e-9, "非法数值不应污染坐标");
        }

        [TestMethod]
        public void Settings_ParsesKeyValue()
        {
            GrblSettings st = new GrblSettings();
            st.Parse("$100=250.000");
            st.Parse("$102=80.5");
            st.Parse("not a setting");
            st.Parse("$");

            Assert.AreEqual(2, st.Values.Count);
            Assert.AreEqual(250.0, st.Values[100], 1e-6);
            Assert.AreEqual(80.5, st.Values[102], 1e-6);
        }

        [TestMethod]
        public void GetPorts_ReturnsArrayNotNull()
        {
            string[] ports = GrblClient.GetPorts();
            Assert.IsNotNull(ports);
        }

        [TestMethod]
        public void Client_NotConnected_RefusesSafely()
        {
            GrblClient c = new GrblClient();
            int logs = 0;
            c.OnLog += delegate(string s) { logs++; };

            Assert.IsFalse(c.IsOpen);
            c.SendCommand("$I");          // 未连接应只记日志、不抛异常
            c.SendRealtime(0x18);
            c.Disconnect();

            Assert.IsTrue(logs >= 1, "未连接发送应留下日志");
            Assert.AreEqual(GrblState.NotConnected, c.Status.State);
            c.Dispose();
        }
    }
}
