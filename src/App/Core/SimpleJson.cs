using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace InkPen.Core
{
    /// <summary>零依赖极简 JSON 解析/序列化（配置持久化用，不引第三方库）。</summary>
    public static class Json
    {
        public static object Parse(string s)
        {
            if (s == null) return null;
            // 容忍 UTF-8 BOM：其它工具写出的 JSON 常带 BOM，不应因此解析失败
            if (s.Length > 0 && s[0] == '\uFEFF') s = s.Substring(1);
            int i = 0;
            object v = ParseValue(s, ref i);
            return v;
        }

        private static object ParseValue(string s, ref int i)
        {
            Skip(s, ref i);
            if (i >= s.Length) return null;
            char c = s[i];
            if (c == '{') return ParseObject(s, ref i);
            if (c == '[') return ParseArray(s, ref i);
            if (c == '"') return ParseString(s, ref i);
            if (c == 't' && s.Substring(i).StartsWith("true")) { i += 4; return true; }
            if (c == 'f' && s.Substring(i).StartsWith("false")) { i += 5; return false; }
            if (c == 'n' && s.Substring(i).StartsWith("null")) { i += 4; return null; }
            return ParseNumber(s, ref i);
        }

        private static Dictionary<string, object> ParseObject(string s, ref int i)
        {
            Dictionary<string, object> d = new Dictionary<string, object>();
            i++; // {
            Skip(s, ref i);
            while (i < s.Length && s[i] != '}')
            {
                Skip(s, ref i);
                string k = ParseString(s, ref i);
                Skip(s, ref i);
                if (i < s.Length && s[i] == ':') i++;
                d[k] = ParseValue(s, ref i);
                Skip(s, ref i);
                if (i < s.Length && s[i] == ',') { i++; continue; }
                break;
            }
            if (i < s.Length && s[i] == '}') i++;
            return d;
        }

        private static List<object> ParseArray(string s, ref int i)
        {
            List<object> a = new List<object>();
            i++; // [
            Skip(s, ref i);
            while (i < s.Length && s[i] != ']')
            {
                a.Add(ParseValue(s, ref i));
                Skip(s, ref i);
                if (i < s.Length && s[i] == ',') { i++; continue; }
                break;
            }
            if (i < s.Length && s[i] == ']') i++;
            return a;
        }

        private static string ParseString(string s, ref int i)
        {
            StringBuilder sb = new StringBuilder();
            if (s[i] == '"') i++;
            while (i < s.Length && s[i] != '"')
            {
                char c = s[i++];
                if (c != '\\') { sb.Append(c); continue; }
                if (i >= s.Length) break;
                char e = s[i++];
                switch (e)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        if (i + 4 <= s.Length)
                        {
                            int code;
                            if (int.TryParse(s.Substring(i, 4), NumberStyles.HexNumber,
                                             CultureInfo.InvariantCulture, out code))
                                sb.Append((char)code);
                            i += 4;
                        }
                        break;
                    default: sb.Append(e); break;
                }
            }
            if (i < s.Length && s[i] == '"') i++;
            return sb.ToString();
        }

        private static double ParseNumber(string s, ref int i)
        {
            int start = i;
            while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '-' || s[i] == '+'
                                    || s[i] == '.' || s[i] == 'e' || s[i] == 'E')) i++;
            double v;
            double.TryParse(s.Substring(start, i - start), NumberStyles.Float,
                            CultureInfo.InvariantCulture, out v);
            return v;
        }

        private static void Skip(string s, ref int i)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        }

        public static string Stringify(object o)
        {
            StringBuilder sb = new StringBuilder();
            Write(sb, o);
            return sb.ToString();
        }

        private static void Write(StringBuilder sb, object o)
        {
            if (o == null) { sb.Append("null"); return; }
            if (o is string) { WriteString(sb, (string)o); return; }
            if (o is bool) { sb.Append(((bool)o) ? "true" : "false"); return; }
            if (o is double || o is float || o is int || o is long || o is decimal)
            {
                sb.Append(Convert.ToDouble(o).ToString("R", CultureInfo.InvariantCulture));
                return;
            }
            Dictionary<string, object> d = o as Dictionary<string, object>;
            if (d != null)
            {
                sb.Append('{');
                bool first = true;
                foreach (KeyValuePair<string, object> kv in d)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    WriteString(sb, kv.Key);
                    sb.Append(':');
                    Write(sb, kv.Value);
                }
                sb.Append('}');
                return;
            }
            System.Collections.IEnumerable e = o as System.Collections.IEnumerable;
            if (e != null)
            {
                sb.Append('[');
                bool first = true;
                foreach (object item in e)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    Write(sb, item);
                }
                sb.Append(']');
                return;
            }
            WriteString(sb, o.ToString());
        }

        private static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u" + ((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        public static Dictionary<string, object> Obj(object o)
        {
            return o as Dictionary<string, object>;
        }

        public static string Str(Dictionary<string, object> d, string k, string def)
        {
            object v;
            if (d != null && d.TryGetValue(k, out v) && v is string) return (string)v;
            return def;
        }

        public static double Num(Dictionary<string, object> d, string k, double def)
        {
            object v;
            if (d != null && d.TryGetValue(k, out v) && v is double) return (double)v;
            return def;
        }

        public static bool Bool(Dictionary<string, object> d, string k, bool def)
        {
            object v;
            if (d != null && d.TryGetValue(k, out v) && v is bool) return (bool)v;
            return def;
        }
    }
}
