using System;
using System.Collections.Generic;
using UnityEngine;

namespace UnityAITools.Editor.Services
{
    /// <summary>
    /// Helper for extracting typed parameters from a JSON parameter string.
    /// Provides type-safe accessors with fallbacks.
    /// </summary>
    public class ToolParams
    {
        private readonly Dictionary<string, object> _params;

        public ToolParams(string paramsJson)
        {
            _params = MiniJson.Deserialize(paramsJson) as Dictionary<string, object>
                      ?? new Dictionary<string, object>();
        }

        public string GetString(string key, string fallback = null)
        {
            return _params.TryGetValue(key, out var val) && val is string s ? s : fallback;
        }

        public string RequireString(string key)
        {
            var val = GetString(key);
            if (val == null)
                throw new ArgumentException($"Required parameter '{key}' is missing");
            return val;
        }

        public int? GetInt(string key)
        {
            if (!_params.TryGetValue(key, out var val)) return null;
            if (val is long l) return (int)l;
            if (val is double d) return (int)d;
            if (val is int i) return i;
            if (val is string s && int.TryParse(s, out var parsed)) return parsed;
            return null;
        }

        public float? GetFloat(string key)
        {
            if (!_params.TryGetValue(key, out var val)) return null;
            if (val is double d) return (float)d;
            if (val is long l) return l;
            if (val is float f) return f;
            if (val is string s && float.TryParse(s, out var parsed)) return parsed;
            return null;
        }

        public bool? GetBool(string key)
        {
            if (!_params.TryGetValue(key, out var val)) return null;
            if (val is bool b) return b;
            if (val is string s)
            {
                if (s.Equals("true", StringComparison.OrdinalIgnoreCase)) return true;
                if (s.Equals("false", StringComparison.OrdinalIgnoreCase)) return false;
            }
            return null;
        }

        public float[] GetFloatArray(string key)
        {
            if (!_params.TryGetValue(key, out var val)) return null;
            if (val is List<object> list && list.Count > 0)
            {
                var arr = new float[list.Count];
                for (int i = 0; i < list.Count; i++)
                    arr[i] = Convert.ToSingle(list[i]);
                return arr;
            }
            return null;
        }

        public float[] GetVector3(string key)
        {
            var arr = GetFloatArray(key);
            return arr != null && arr.Length >= 3 ? arr : null;
        }

        public Vector3? GetUnityVector3(string key)
        {
            var arr = GetVector3(key);
            if (arr == null) return null;
            return new Vector3(arr[0], arr[1], arr[2]);
        }

        public List<string> GetStringList(string key)
        {
            if (!_params.TryGetValue(key, out var val)) return null;
            if (val is List<object> list)
            {
                var result = new List<string>();
                foreach (var item in list)
                    result.Add(item?.ToString());
                return result;
            }
            return null;
        }

        public bool HasKey(string key) => _params.ContainsKey(key);

        public object GetRaw(string key)
        {
            return _params.TryGetValue(key, out var val) ? val : null;
        }
    }

    /// <summary>
    /// Minimal JSON parser for Unity (no external dependencies).
    /// Handles basic JSON objects, arrays, strings, numbers, booleans, and null.
    /// </summary>
    public static class MiniJson
    {
        public static object Deserialize(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            var index = 0;
            return ParseValue(json, ref index);
        }

        public static string Serialize(object obj)
        {
            if (obj == null) return "null";
            if (obj is string s) return $"\"{EscapeString(s)}\"";
            if (obj is bool b) return b ? "true" : "false";
            if (obj is int || obj is long || obj is float || obj is double)
                return Convert.ToString(obj, System.Globalization.CultureInfo.InvariantCulture);

            if (obj is Dictionary<string, object> dict)
            {
                var parts = new List<string>();
                foreach (var kvp in dict)
                    parts.Add($"\"{EscapeString(kvp.Key)}\":{Serialize(kvp.Value)}");
                return "{" + string.Join(",", parts) + "}";
            }

            if (obj is System.Collections.IList ilist)
            {
                var parts = new List<string>();
                foreach (var item in ilist)
                    parts.Add(Serialize(item));
                return "[" + string.Join(",", parts) + "]";
            }

            return $"\"{EscapeString(obj.ToString())}\"";
        }

        private static object ParseValue(string json, ref int index)
        {
            SkipWhitespace(json, ref index);
            if (index >= json.Length) return null;

            var c = json[index];
            if (c == '{') return ParseObject(json, ref index);
            if (c == '[') return ParseArray(json, ref index);
            if (c == '"') return ParseString(json, ref index);
            if (c == 't' || c == 'f') return ParseBool(json, ref index);
            if (c == 'n') { index += 4; return null; }
            return ParseNumber(json, ref index);
        }

        private static Dictionary<string, object> ParseObject(string json, ref int index)
        {
            var dict = new Dictionary<string, object>();
            index++; // skip '{'
            SkipWhitespace(json, ref index);

            while (index < json.Length && json[index] != '}')
            {
                SkipWhitespace(json, ref index);
                var key = ParseString(json, ref index);
                SkipWhitespace(json, ref index);
                index++; // skip ':'
                var value = ParseValue(json, ref index);
                dict[key] = value;
                SkipWhitespace(json, ref index);
                if (index < json.Length && json[index] == ',') index++;
            }

            if (index < json.Length) index++; // skip '}'
            return dict;
        }

        private static List<object> ParseArray(string json, ref int index)
        {
            var list = new List<object>();
            index++; // skip '['
            SkipWhitespace(json, ref index);

            while (index < json.Length && json[index] != ']')
            {
                list.Add(ParseValue(json, ref index));
                SkipWhitespace(json, ref index);
                if (index < json.Length && json[index] == ',') index++;
            }

            if (index < json.Length) index++; // skip ']'
            return list;
        }

        private static string ParseString(string json, ref int index)
        {
            index++; // skip opening '"'
            var start = index;
            var sb = new System.Text.StringBuilder();

            while (index < json.Length && json[index] != '"')
            {
                if (json[index] == '\\')
                {
                    sb.Append(json, start, index - start);
                    index++;
                    if (index < json.Length)
                    {
                        switch (json[index])
                        {
                            case '"': sb.Append('"'); break;
                            case '\\': sb.Append('\\'); break;
                            case '/': sb.Append('/'); break;
                            case 'n': sb.Append('\n'); break;
                            case 'r': sb.Append('\r'); break;
                            case 't': sb.Append('\t'); break;
                            default: sb.Append(json[index]); break;
                        }
                        index++;
                        start = index;
                    }
                }
                else
                {
                    index++;
                }
            }

            sb.Append(json, start, index - start);
            if (index < json.Length) index++; // skip closing '"'
            return sb.ToString();
        }

        private static object ParseNumber(string json, ref int index)
        {
            var start = index;
            var isFloat = false;

            if (index < json.Length && json[index] == '-') index++;
            while (index < json.Length && char.IsDigit(json[index])) index++;
            if (index < json.Length && json[index] == '.') { isFloat = true; index++; }
            while (index < json.Length && char.IsDigit(json[index])) index++;
            if (index < json.Length && (json[index] == 'e' || json[index] == 'E'))
            {
                isFloat = true;
                index++;
                if (index < json.Length && (json[index] == '+' || json[index] == '-')) index++;
                while (index < json.Length && char.IsDigit(json[index])) index++;
            }

            var numStr = json.Substring(start, index - start);
            if (isFloat && double.TryParse(numStr, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var d))
                return d;
            if (long.TryParse(numStr, out var l))
                return l;
            return 0;
        }

        private static bool ParseBool(string json, ref int index)
        {
            if (json[index] == 't') { index += 4; return true; }
            index += 5;
            return false;
        }

        private static void SkipWhitespace(string json, ref int index)
        {
            while (index < json.Length && char.IsWhiteSpace(json[index])) index++;
        }

        private static string EscapeString(string s)
        {
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"")
                    .Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
        }
    }
}
