using System;
using System.Collections.Generic;
using InkPen.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace InkPen.Tests
{
    [TestClass]
    public class JsonTests
    {
        [TestMethod]
        public void Parse_Primitives()
        {
            Assert.AreEqual(42.0, (double)Json.Parse("42"), 1e-9);
            Assert.AreEqual(-1.5, (double)Json.Parse("-1.5"), 1e-9);
            Assert.AreEqual(1000.0, (double)Json.Parse("1e3"), 1e-9);
            Assert.AreEqual("hi", Json.Parse("\"hi\""));
            Assert.AreEqual(true, Json.Parse("true"));
            Assert.AreEqual(false, Json.Parse("false"));
            Assert.IsNull(Json.Parse("null"));
        }

        [TestMethod]
        public void Parse_EscapedString_AndUnicodeEscape()
        {
            Assert.AreEqual("a\nb\t\"c\"", Json.Parse("\"a\\nb\\t\\\"c\\\"\""));
            Assert.AreEqual("\u4e2d", Json.Parse("\"\\u4e2d\""));
            Assert.AreEqual("C:\\tmp\\x", Json.Parse("\"C:\\\\tmp\\\\x\""));
        }

        [TestMethod]
        public void Parse_NestedObjectAndArray()
        {
            object o = Json.Parse("{\"a\":1,\"b\":[1,2,3],\"c\":{\"d\":true},\"e\":null}");
            Dictionary<string, object> d = Json.Obj(o);
            Assert.IsNotNull(d);
            Assert.AreEqual(1.0, Json.Num(d, "a", -1), 1e-9);

            List<object> arr = d["b"] as List<object>;
            Assert.IsNotNull(arr);
            Assert.AreEqual(3, arr.Count);
            Assert.AreEqual(3.0, (double)arr[2], 1e-9);

            Assert.IsTrue(Json.Bool(Json.Obj(d["c"]), "d", false));
            Assert.IsNull(d["e"]);
        }

        [TestMethod]
        public void Stringify_RoundTripsThroughParse()
        {
            Dictionary<string, object> src = new Dictionary<string, object>();
            src["name"] = "\u5199\u5b57\u673a \"InkPen\"";
            src["size"] = 10.5;
            src["flag"] = true;
            src["nested"] = new Dictionary<string, object> { { "x", 1.0 } };

            string text = Json.Stringify(src);
            Dictionary<string, object> back = Json.Obj(Json.Parse(text));

            Assert.AreEqual(src["name"], Json.Str(back, "name", ""));
            Assert.AreEqual(10.5, Json.Num(back, "size", 0), 1e-9);
            Assert.IsTrue(Json.Bool(back, "flag", false));
            Assert.AreEqual(1.0, Json.Num(Json.Obj(back["nested"]), "x", 0), 1e-9);
        }

        [TestMethod]
        public void Stringify_EscapesControlChars()
        {
            string s = Json.Stringify("a\nb");
            Assert.AreEqual("\"a\\nb\"", s);
        }

        [TestMethod]
        public void Helpers_ReturnDefaults_OnWrongTypeOrMissing()
        {
            Dictionary<string, object> d = new Dictionary<string, object>();
            d["n"] = 5.0;
            Assert.AreEqual("def", Json.Str(d, "n", "def"), "值不是 string 时应回退默认");
            Assert.AreEqual(7.0, Json.Num(d, "missing", 7.0), 1e-9);
            Assert.IsFalse(Json.Bool(d, "missing", false));
            Assert.AreEqual("x", Json.Str(null, "k", "x"));
        }

        [TestMethod]
        public void Parse_ToleratesUtf8Bom()
        {
            object o = Json.Parse("\uFEFF{\"a\":1}");
            Dictionary<string, object> d = Json.Obj(o);
            Assert.IsNotNull(d, "带 BOM 的 JSON 也必须能解析");
            Assert.AreEqual(1.0, Json.Num(d, "a", -1), 1e-9);
        }

        [TestMethod]
        public void Parse_EmptyOrGarbage_DoesNotThrow()
        {
            Assert.IsNull(Json.Parse(""));
            Assert.IsNull(Json.Parse(null));
            Dictionary<string, object> d = Json.Obj(Json.Parse("{\"a\":}"));
            Assert.IsNotNull(d);
        }
    }
}
