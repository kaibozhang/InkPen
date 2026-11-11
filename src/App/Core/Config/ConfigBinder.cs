using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;

namespace InkPen.Core
{
    /// <summary>
    /// 配置对象的字段级 JSON 绑定（不引第三方序列化库）。
    /// 从 MainForm 抽出，便于单元测试覆盖「保存 → 读取」的往返。
    /// 只处理 public instance 字段：double / int / bool / string / enum。
    /// </summary>
    public static class ConfigBinder
    {
        public static Dictionary<string, object> ToDict(object o)
        {
            Dictionary<string, object> d = new Dictionary<string, object>();
            if (o == null) return d;
            foreach (FieldInfo f in o.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                object v = f.GetValue(o);
                if (v == null) { d[f.Name] = null; continue; }
                if (v is Enum) d[f.Name] = v.ToString();
                else if (v is double) d[f.Name] = (double)v;
                else if (v is float) d[f.Name] = (double)(float)v;
                else if (v is int) d[f.Name] = (double)(int)v;
                else if (v is long) d[f.Name] = (double)(long)v;
                else if (v is bool) d[f.Name] = (bool)v;
                else d[f.Name] = v.ToString();
            }
            return d;
        }

        /// <summary>把字典写回对象的字段；类型不符或键缺失时保持原值，不抛异常。</summary>
        public static void FromDict(object o, Dictionary<string, object> d)
        {
            if (o == null || d == null) return;
            foreach (FieldInfo f in o.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                object v;
                if (!d.TryGetValue(f.Name, out v) || v == null) continue;
                try
                {
                    if (f.FieldType == typeof(double)) f.SetValue(o, Convert.ToDouble(v, CultureInfo.InvariantCulture));
                    else if (f.FieldType == typeof(float)) f.SetValue(o, (float)Convert.ToDouble(v, CultureInfo.InvariantCulture));
                    else if (f.FieldType == typeof(int)) f.SetValue(o, Convert.ToInt32(v, CultureInfo.InvariantCulture));
                    else if (f.FieldType == typeof(long)) f.SetValue(o, Convert.ToInt64(v, CultureInfo.InvariantCulture));
                    else if (f.FieldType == typeof(bool)) f.SetValue(o, Convert.ToBoolean(v, CultureInfo.InvariantCulture));
                    else if (f.FieldType == typeof(string)) f.SetValue(o, Convert.ToString(v, CultureInfo.InvariantCulture));
                    else if (f.FieldType.IsEnum) f.SetValue(o, Enum.Parse(f.FieldType, Convert.ToString(v), true));
                }
                catch
                {
                    // 单个字段脏数据不应让整个配置读取失败
                }
            }
        }

        /// <summary>序列化整份配置（模板 + 机器 + 文稿）。</summary>
        public static string Serialize(TemplateConfig template, MachineConfig machine, string text)
        {
            Dictionary<string, object> root = new Dictionary<string, object>();
            root["template"] = ToDict(template);
            root["machine"] = ToDict(machine);
            root["text"] = text ?? "";
            return Json.Stringify(root);
        }

        /// <summary>反序列化整份配置；返回文稿内容，模板与机器配置就地写回。</summary>
        public static string Deserialize(string json, TemplateConfig template, MachineConfig machine)
        {
            Dictionary<string, object> root = Json.Obj(Json.Parse(json));
            if (root == null) return null;

            object t;
            if (root.TryGetValue("template", out t)) FromDict(template, Json.Obj(t));
            if (root.TryGetValue("machine", out t)) FromDict(machine, Json.Obj(t));

            object txt;
            if (root.TryGetValue("text", out txt) && txt is string) return (string)txt;
            return null;
        }
    }
}
