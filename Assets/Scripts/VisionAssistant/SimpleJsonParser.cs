using System.Collections.Generic;
using System.Text;

/// <summary>
/// Minimal recursive-descent JSON parser. Unity's built-in JsonUtility can't
/// deserialize into Dictionary/variable-shaped objects, which is exactly what
/// the LLM's extraction response looks like (fields differ per document).
/// Parses into: Dictionary&lt;string,object&gt;, List&lt;object&gt;, string, double, bool, or null.
/// </summary>
public static class SimpleJsonParser
{
    public static object Parse(string json)
    {
        int i = 0;
        SkipWhitespace(json, ref i);
        return ParseValue(json, ref i);
    }

    private static object ParseValue(string s, ref int i)
    {
        SkipWhitespace(s, ref i);
        char c = s[i];
        if (c == '{') return ParseObject(s, ref i);
        if (c == '[') return ParseArray(s, ref i);
        if (c == '"') return ParseString(s, ref i);
        if (c == 't') { i += 4; return true; }
        if (c == 'f') { i += 5; return false; }
        if (c == 'n') { i += 4; return null; }
        return ParseNumber(s, ref i);
    }

    private static Dictionary<string, object> ParseObject(string s, ref int i)
    {
        var dict = new Dictionary<string, object>();
        i++; // skip '{'
        SkipWhitespace(s, ref i);
        if (s[i] == '}') { i++; return dict; }

        while (true)
        {
            SkipWhitespace(s, ref i);
            string key = ParseString(s, ref i);
            SkipWhitespace(s, ref i);
            i++; // skip ':'
            object value = ParseValue(s, ref i);
            dict[key] = value;
            SkipWhitespace(s, ref i);
            if (s[i] == ',') { i++; continue; }
            if (s[i] == '}') { i++; break; }
        }
        return dict;
    }

    private static List<object> ParseArray(string s, ref int i)
    {
        var list = new List<object>();
        i++; // skip '['
        SkipWhitespace(s, ref i);
        if (s[i] == ']') { i++; return list; }

        while (true)
        {
            object value = ParseValue(s, ref i);
            list.Add(value);
            SkipWhitespace(s, ref i);
            if (s[i] == ',') { i++; continue; }
            if (s[i] == ']') { i++; break; }
        }
        return list;
    }

    private static string ParseString(string s, ref int i)
    {
        var sb = new StringBuilder();
        i++; // skip opening quote
        while (s[i] != '"')
        {
            if (s[i] == '\\')
            {
                i++;
                switch (s[i])
                {
                    case 'n': sb.Append('\n'); break;
                    case 't': sb.Append('\t'); break;
                    case 'r': sb.Append('\r'); break;
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'u':
                        string hex = s.Substring(i + 1, 4);
                        sb.Append((char)System.Convert.ToInt32(hex, 16));
                        i += 4;
                        break;
                    default: sb.Append(s[i]); break;
                }
            }
            else
            {
                sb.Append(s[i]);
            }
            i++;
        }
        i++; // skip closing quote
        return sb.ToString();
    }

    private static double ParseNumber(string s, ref int i)
    {
        int start = i;
        while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '-' || s[i] == '+' || s[i] == '.' || s[i] == 'e' || s[i] == 'E'))
            i++;
        return double.Parse(s.Substring(start, i - start), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static void SkipWhitespace(string s, ref int i)
    {
        while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
    }
}
