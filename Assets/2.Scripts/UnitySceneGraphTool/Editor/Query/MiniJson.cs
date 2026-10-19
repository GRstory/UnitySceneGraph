using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SceneGraph.Editor
{
    /// <summary>쿼리 JSON 파싱용 최소 파서. object → Dictionary, array → List, number → double.</summary>
    public static class MiniJson
    {
        public static object Parse(string json)
        {
            var p = new Parser(json ?? "");
            var v = p.Value();
            p.SkipWs();
            if (!p.End) throw p.Error("Trailing characters");
            return v;
        }

        public static string Quote(string s)
        {
            if (s == null) return "null";
            var sb = new StringBuilder("\"");
            foreach (var c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.Append('"').ToString();
        }

        sealed class Parser
        {
            readonly string s;
            int i;
            public Parser(string s) => this.s = s;
            public bool End => i >= s.Length;
            public FormatException Error(string msg) => new FormatException($"{msg} at {i}");

            public void SkipWs()
            {
                while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
            }

            public object Value()
            {
                SkipWs();
                if (End) throw Error("Unexpected end");
                var c = s[i];
                if (c == '{') return Obj();
                if (c == '[') return Arr();
                if (c == '"') return Str();
                if (c == '-' || char.IsDigit(c)) return Num();
                if (Match("true")) return true;
                if (Match("false")) return false;
                if (Match("null")) return null;
                throw Error($"Unexpected '{c}'");
            }

            bool Match(string word)
            {
                if (string.CompareOrdinal(s, i, word, 0, word.Length) != 0) return false;
                i += word.Length;
                return true;
            }

            Dictionary<string, object> Obj()
            {
                var d = new Dictionary<string, object>();
                i++;
                SkipWs();
                if (!End && s[i] == '}') { i++; return d; }
                while (true)
                {
                    SkipWs();
                    if (End || s[i] != '"') throw Error("Expected key");
                    var key = Str();
                    SkipWs();
                    if (End || s[i] != ':') throw Error("Expected ':'");
                    i++;
                    d[key] = Value();
                    SkipWs();
                    if (End) throw Error("Unterminated object");
                    if (s[i] == ',') { i++; continue; }
                    if (s[i] == '}') { i++; return d; }
                    throw Error("Expected ',' or '}'");
                }
            }

            List<object> Arr()
            {
                var l = new List<object>();
                i++;
                SkipWs();
                if (!End && s[i] == ']') { i++; return l; }
                while (true)
                {
                    l.Add(Value());
                    SkipWs();
                    if (End) throw Error("Unterminated array");
                    if (s[i] == ',') { i++; continue; }
                    if (s[i] == ']') { i++; return l; }
                    throw Error("Expected ',' or ']'");
                }
            }

            string Str()
            {
                var sb = new StringBuilder();
                i++;
                while (true)
                {
                    if (End) throw Error("Unterminated string");
                    var c = s[i++];
                    if (c == '"') return sb.ToString();
                    if (c != '\\') { sb.Append(c); continue; }
                    if (End) throw Error("Bad escape");
                    var e = s[i++];
                    switch (e)
                    {
                        case 'n': sb.Append('\n'); break;
                        case 't': sb.Append('\t'); break;
                        case 'r': sb.Append('\r'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'u':
                            if (i + 4 > s.Length) throw Error("Bad unicode escape");
                            sb.Append((char)Convert.ToInt32(s.Substring(i, 4), 16));
                            i += 4;
                            break;
                        default: sb.Append(e); break;
                    }
                }
            }

            double Num()
            {
                var start = i;
                if (s[i] == '-') i++;
                while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '.' || s[i] == 'e' || s[i] == 'E' || s[i] == '+' || s[i] == '-')) i++;
                return double.Parse(s.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture);
            }
        }
    }
}
