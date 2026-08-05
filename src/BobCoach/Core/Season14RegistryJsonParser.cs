using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace BobCoach.Engine
{
    /// <summary>Minimal JSON parser and row helpers for generated season14 registry files.</summary>
    internal static class Season14RegistryJsonParser
    {
        internal static Dictionary<string, object> ParseRootObject(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try
            {
                return new JsonParser(json).ParseValue() as Dictionary<string, object>;
            }
            catch
            {
                return null;
            }
        }

        internal static Dictionary<string, object> AsObject(
            Dictionary<string, object> row, string key)
        {
            object value;
            if (row == null || !row.TryGetValue(key, out value)) return null;
            return value as Dictionary<string, object>;
        }

        internal static string AsString(Dictionary<string, object> row, string key)
        {
            object value;
            if (row == null || !row.TryGetValue(key, out value) || !(value is string))
                return "";
            return (string)value;
        }

        internal static List<object> AsArray(Dictionary<string, object> row, string key)
        {
            object value;
            if (row == null || !row.TryGetValue(key, out value)) return null;
            return value as List<object>;
        }

        internal static int AsInt(Dictionary<string, object> row, string key)
        {
            object value;
            if (row == null || !row.TryGetValue(key, out value) || value == null) return 0;
            if (value is double) return Convert.ToInt32((double)value);
            var text = value as string;
            int parsed;
            if (text != null
                && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
                return parsed;
            return 0;
        }

        internal static int? AsNullableInt(Dictionary<string, object> row, string key)
        {
            object value;
            if (row == null || !row.TryGetValue(key, out value) || value == null) return null;
            if (value is double) return Convert.ToInt32((double)value);
            var text = value as string;
            int parsed;
            if (text != null
                && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
                return parsed;
            return null;
        }

        internal static bool AsBool(Dictionary<string, object> row, string key)
        {
            object value;
            if (row == null || !row.TryGetValue(key, out value) || value == null) return false;
            if (value is bool) return (bool)value;
            var text = value as string;
            bool parsed;
            if (text != null
                && bool.TryParse(text, out parsed))
                return parsed;
            return false;
        }

        internal static List<string> AsStringList(Dictionary<string, object> row, string key)
        {
            var result = new List<string>();
            var values = AsArray(row, key);
            if (values == null) return result;
            foreach (var value in values)
            {
                var text = value as string;
                if (string.IsNullOrEmpty(text)) continue;
                result.Add(text);
            }
            return result;
        }

        internal static bool IsBattlegroundsDuoCardId(string cardId)
        {
            return !string.IsNullOrEmpty(cardId)
                && cardId.StartsWith("BGDUO", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Recursive-descent parser for the generated registry JSON subset.</summary>
        private sealed class JsonParser
        {
            private readonly string _text;
            private int _pos;

            internal JsonParser(string text)
            {
                _text = text ?? "";
            }

            internal object ParseValue()
            {
                SkipWhitespace();
                if (_pos >= _text.Length) throw new FormatException("unexpected end of JSON");
                char c = _text[_pos];
                switch (c)
                {
                    case '{': return ParseObject();
                    case '[': return ParseArray();
                    case '"': return ParseString();
                    case 't': Expect("true"); return true;
                    case 'f': Expect("false"); return false;
                    case 'n': Expect("null"); return null;
                    default: return ParseNumber();
                }
            }

            private Dictionary<string, object> ParseObject()
            {
                _pos++;
                var result = new Dictionary<string, object>(StringComparer.Ordinal);
                SkipWhitespace();
                if (Peek() == '}') { _pos++; return result; }
                while (true)
                {
                    SkipWhitespace();
                    if (Peek() != '"') throw new FormatException("expected string key");
                    string key = ParseString();
                    SkipWhitespace();
                    if (Peek() != ':') throw new FormatException("expected ':'");
                    _pos++;
                    result[key] = ParseValue();
                    SkipWhitespace();
                    char separator = Peek();
                    if (separator == ',') { _pos++; continue; }
                    if (separator == '}') { _pos++; return result; }
                    throw new FormatException("expected ',' or '}'");
                }
            }

            private List<object> ParseArray()
            {
                _pos++;
                var result = new List<object>();
                SkipWhitespace();
                if (Peek() == ']') { _pos++; return result; }
                while (true)
                {
                    result.Add(ParseValue());
                    SkipWhitespace();
                    char separator = Peek();
                    if (separator == ',') { _pos++; continue; }
                    if (separator == ']') { _pos++; return result; }
                    throw new FormatException("expected ',' or ']'");
                }
            }

            private string ParseString()
            {
                _pos++;
                var builder = new StringBuilder();
                while (true)
                {
                    if (_pos >= _text.Length) throw new FormatException("unterminated string");
                    char c = _text[_pos++];
                    if (c == '"') return builder.ToString();
                    if (c != '\\')
                    {
                        if (c < 0x20) throw new FormatException("unescaped control character");
                        builder.Append(c);
                        continue;
                    }
                    if (_pos >= _text.Length) throw new FormatException("unterminated escape");
                    char escaped = _text[_pos++];
                    switch (escaped)
                    {
                        case '"': builder.Append('"'); break;
                        case '\\': builder.Append('\\'); break;
                        case '/': builder.Append('/'); break;
                        case 'b': builder.Append('\b'); break;
                        case 'f': builder.Append('\f'); break;
                        case 'n': builder.Append('\n'); break;
                        case 'r': builder.Append('\r'); break;
                        case 't': builder.Append('\t'); break;
                        case 'u':
                            if (_pos + 4 > _text.Length) throw new FormatException("invalid unicode escape");
                            builder.Append((char)Convert.ToInt32(_text.Substring(_pos, 4), 16));
                            _pos += 4;
                            break;
                        default: throw new FormatException("invalid escape: " + escaped);
                    }
                }
            }

            private object ParseNumber()
            {
                int start = _pos;
                while (_pos < _text.Length
                    && "-+0123456789.eE".IndexOf(_text[_pos]) >= 0)
                    _pos++;
                if (_pos == start) throw new FormatException("invalid number");
                string token = _text.Substring(start, _pos - start);
                double value;
                if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                    throw new FormatException("invalid number: " + token);
                return value;
            }

            private void Expect(string literal)
            {
                if (_pos + literal.Length > _text.Length
                    || string.CompareOrdinal(_text, _pos, literal, 0, literal.Length) != 0)
                    throw new FormatException("expected literal: " + literal);
                _pos += literal.Length;
            }

            private char Peek()
            {
                if (_pos >= _text.Length) throw new FormatException("unexpected end of JSON");
                return _text[_pos];
            }

            private void SkipWhitespace()
            {
                while (_pos < _text.Length
                    && (_text[_pos] == ' ' || _text[_pos] == '\t'
                        || _text[_pos] == '\r' || _text[_pos] == '\n'))
                    _pos++;
            }
        }
    }
}
