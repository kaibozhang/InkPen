using System;
using System.Collections.Generic;
using InkPen.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace InkPen.Tests
{
    [TestClass]
    public class ConfigBinderTests
    {
        [TestMethod]
        public void SerializeDeserialize_RoundTripsAllFieldTypes()
        {
            TemplateConfig t = new TemplateConfig();
            t.Kind = TemplateKind.Tianzi;
            t.Direction = TextDirection.Vertical;
            t.FontSizeMm = 12.5;
            t.CharGapMm = 2;
            t.DrawGrid = true;
            t.CenterVertically = true;
            t.LatinFont = "latin_script.zip";

            MachineConfig m = new MachineConfig();
            m.Origin = OriginCorner.TopRight;
            m.InvertX = true;
            m.SafeZ = 7.5;
            m.WriteZ = -0.3;
            m.XYFeed = 2400;
            m.Seed = 987654;
            m.PortName = "COM7";
            m.AckTimeoutMs = 45000;

            string text = "\u5199\u5b57\u673a\nHello 123\u3002";
            string json = ConfigBinder.Serialize(t, m, text);

            TemplateConfig t2 = new TemplateConfig();
            MachineConfig m2 = new MachineConfig();
            string text2 = ConfigBinder.Deserialize(json, t2, m2);

            Assert.AreEqual(text, text2);
            Assert.AreEqual(TemplateKind.Tianzi, t2.Kind);
            Assert.AreEqual(TextDirection.Vertical, t2.Direction);
            Assert.AreEqual(12.5, t2.FontSizeMm, 1e-9);
            Assert.AreEqual(2.0, t2.CharGapMm, 1e-9);
            Assert.IsTrue(t2.DrawGrid);
            Assert.IsTrue(t2.CenterVertically);
            Assert.AreEqual("latin_script.zip", t2.LatinFont);

            Assert.AreEqual(OriginCorner.TopRight, m2.Origin);
            Assert.IsTrue(m2.InvertX);
            Assert.AreEqual(7.5, m2.SafeZ, 1e-9);
            Assert.AreEqual(-0.3, m2.WriteZ, 1e-9);
            Assert.AreEqual(2400.0, m2.XYFeed, 1e-9);
            Assert.AreEqual(987654, m2.Seed);
            Assert.AreEqual("COM7", m2.PortName);
            Assert.AreEqual(45000, m2.AckTimeoutMs);
        }

        [TestMethod]
        public void Deserialize_MissingKeys_KeepDefaults()
        {
            TemplateConfig t = new TemplateConfig();
            t.FontSizeMm = 10;
            MachineConfig m = new MachineConfig();

            string text = ConfigBinder.Deserialize("{\"text\":\"abc\"}", t, m);

            Assert.AreEqual("abc", text);
            Assert.AreEqual(10.0, t.FontSizeMm, 1e-9, "未出现的键必须保持原值");
            Assert.AreEqual(210.0, m.PaperWidthMm, 1e-9);
        }

        [TestMethod]
        public void Deserialize_BadValue_DoesNotThrowAndKeepsOldValue()
        {
            TemplateConfig t = new TemplateConfig();
            t.FontSizeMm = 10;
            string json = "{\"template\":{\"FontSizeMm\":\"not a number\",\"Kind\":\"NoSuchKind\"},\"text\":\"x\"}";

            string text = ConfigBinder.Deserialize(json, t, new MachineConfig());

            Assert.AreEqual("x", text);
            Assert.AreEqual(10.0, t.FontSizeMm, 1e-9, "脏数据不应覆盖原值");
            Assert.AreEqual(TemplateKind.Ruled, t.Kind, "非法枚举不应改值");
        }

        [TestMethod]
        public void Deserialize_NullOrBrokenJson_ReturnsNullSafely()
        {
            Assert.IsNull(ConfigBinder.Deserialize("", new TemplateConfig(), new MachineConfig()));
            Assert.IsNull(ConfigBinder.Deserialize("null", new TemplateConfig(), new MachineConfig()));
        }

        [TestMethod]
        public void ToDict_ConvertsIntsToDoubles_ForJsonWriter()
        {
            Dictionary<string, object> d = ConfigBinder.ToDict(new MachineConfig());
            Assert.IsInstanceOfType(d["AckTimeoutMs"], typeof(double));
            Assert.IsInstanceOfType(d["PaperWidthMm"], typeof(double));
            Assert.IsInstanceOfType(d["Origin"], typeof(string), "枚举应存名字而不是序号");
        }

        [TestMethod]
        public void FromDict_NullInput_IsNoOp()
        {
            MachineConfig m = new MachineConfig();
            ConfigBinder.FromDict(m, null);
            ConfigBinder.FromDict(null, new Dictionary<string, object>());
            Assert.AreEqual(210.0, m.PaperWidthMm, 1e-9);
        }
    }
}
