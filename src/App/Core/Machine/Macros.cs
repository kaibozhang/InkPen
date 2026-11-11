using System;
using System.Collections.Generic;
using System.Globalization;

namespace InkPen.Core
{
    /// <summary>一个用户自定义指令按钮（A4）。</summary>
    public class MacroButton
    {
        public string Label = "";
        public string Command = "";

        public MacroButton() { }
        public MacroButton(string label, string command) { Label = label; Command = command; }
    }

    /// <summary>
    /// 宏按钮集合。存在 config\macros.json，与 config.json 分开——按钮表是「个人习惯」，
    /// 换文稿 / 换机器时不该被一起覆盖。
    ///
    /// 指令里支持占位符，改 Z 参数后按钮自动跟着变，不用手工再改一遍：
    ///   {safez} {writez} {xyfeed} {zfeed}
    /// </summary>
    public class MacroSet
    {
        public List<MacroButton> Buttons = new List<MacroButton>();

        public static MacroSet Defaults()
        {
            MacroSet s = new MacroSet();
            s.Buttons.Add(new MacroButton("抬笔", "G0 Z{safez}"));
            s.Buttons.Add(new MacroButton("试笔", "G0 Z{writez} F{zfeed}\nG4 P0.5\nG0 Z{safez}"));
            s.Buttons.Add(new MacroButton("回纸角", "G0 X0 Y0"));
            s.Buttons.Add(new MacroButton("清报警", "$X"));
            return s;
        }

        public static MacroSet FromJson(string json)
        {
            MacroSet s = new MacroSet();
            if (string.IsNullOrEmpty(json)) return Defaults();

            Dictionary<string, object> root = Json.Obj(Json.Parse(json));
            if (root == null) return Defaults();

            object v;
            List<object> arr = root.TryGetValue("buttons", out v) ? v as List<object> : null;
            if (arr == null) return Defaults();

            foreach (object o in arr)
            {
                Dictionary<string, object> d = Json.Obj(o);
                if (d == null) continue;
                MacroButton b = new MacroButton(Json.Str(d, "label", ""), Json.Str(d, "command", ""));
                if (b.Label.Trim().Length == 0 && b.Command.Trim().Length == 0) continue;
                s.Buttons.Add(b);
            }
            return s.Buttons.Count > 0 ? s : Defaults();
        }

        public string ToJson()
        {
            List<object> arr = new List<object>();
            foreach (MacroButton b in Buttons)
            {
                Dictionary<string, object> d = new Dictionary<string, object>();
                d["label"] = b.Label ?? "";
                d["command"] = b.Command ?? "";
                arr.Add(d);
            }
            Dictionary<string, object> root = new Dictionary<string, object>();
            root["buttons"] = arr;
            return Json.Stringify(root);
        }

        /// <summary>把 {safez} 之类的占位符换成当前配置值。</summary>
        public static string Expand(string cmd, MachineConfig m)
        {
            if (string.IsNullOrEmpty(cmd)) return "";
            string s = cmd;
            if (m != null)
            {
                s = s.Replace("{safez}", GCodeBuilder.F(m.SafeZ));
                s = s.Replace("{writez}", GCodeBuilder.F(m.WriteZ));
                s = s.Replace("{xyfeed}", GCodeBuilder.F(m.XYFeed));
                s = s.Replace("{zfeed}", GCodeBuilder.F(m.ZFeed));
            }
            return s;
        }

        /// <summary>按 \n 拆成多条指令（一个按钮可以发一串）。</summary>
        public static List<string> Split(string expanded)
        {
            List<string> lines = new List<string>();
            if (string.IsNullOrEmpty(expanded)) return lines;
            string[] arr = expanded.Replace("\r\n", "\n").Split('\n');
            foreach (string l in arr)
            {
                string t = l.Trim();
                if (t.Length > 0) lines.Add(t);
            }
            return lines;
        }
    }
}
