using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace BobCoach.Engine
{
    /// <summary>
    /// Season-contract trinket definitions loaded from a generated trinket_registry.json.
    /// Lookups are exact and fail-closed; missing or invalid sources degrade silently so
    /// equipped-trinket resolution keeps its hard-coded audited ruleset.
    /// </summary>
    internal sealed class TrinketDefinitionRegistry
    {
        internal sealed class TrinketDefinition
        {
            internal string CardId;
            internal string NameCn;
            internal string NameEn;
            internal int Cost;
            internal int Score;
            internal string Text;
            internal List<EffectDefinition> Effects = new List<EffectDefinition>();
            internal List<string> Tribes = new List<string>();
        }

        internal sealed class EffectDefinition
        {
            internal string Type;
            internal string Subtype;
            internal double? Value;
        }

        private readonly Dictionary<string, TrinketDefinition> _byCardId =
            new Dictionary<string, TrinketDefinition>(StringComparer.Ordinal);
        private bool _loaded;

        /// <summary>Configured data root this registry was loaded from; null when not set.</summary>
        internal string ConfiguredRoot { get; private set; }

        internal bool IsLoaded { get { return _loaded; } }

        internal int Count { get { return _byCardId.Count; } }

        /// <summary>Loads from the data root only when BOB_COACH_DATA_ROOT is explicitly set.
        /// The APPDATA fallback root is intentionally never touched by this registry.</summary>
        internal void LoadFromConfiguredRoot()
        {
            ConfiguredRoot = null;
            _byCardId.Clear();
            _loaded = false;

            string configuredRoot =
                Environment.GetEnvironmentVariable(BobCoachDataPaths.RootEnvironmentVariable);
            if (string.IsNullOrWhiteSpace(configuredRoot)) return;

            try
            {
                var root = BobCoachDataPaths.ResolveRoot(configuredRoot, null);
                if (root == null) return;
                ConfiguredRoot = root;
                var file = BobCoachDataPaths.Combine(root, "data", "trinket_registry.json");
                if (!File.Exists(file)) return;
                LoadFromJson(File.ReadAllText(file, Encoding.UTF8));
            }
            catch
            {
                _byCardId.Clear();
                _loaded = false;
            }
        }

        /// <summary>Loads registry rows from raw JSON (test injection or file content).</summary>
        internal void LoadFromJson(string json)
        {
            _byCardId.Clear();
            _loaded = false;
            if (string.IsNullOrWhiteSpace(json)) return;

            try
            {
                var parser = new JsonParser(json);
                var root = parser.ParseValue() as Dictionary<string, object>;
                if (root == null) return;

                var lesser = AsArray(root, "lesser");
                var greater = AsArray(root, "greater");
                if (lesser == null || greater == null) return;

                foreach (var section in new[] { lesser, greater })
                {
                    foreach (var item in section)
                    {
                        var row = item as Dictionary<string, object>;
                        if (row == null) continue;
                        string cardId = AsString(row, "cardId");
                        if (string.IsNullOrEmpty(cardId)) continue;
                        if (_byCardId.ContainsKey(cardId)) continue;
                        _byCardId[cardId] = ParseDefinition(cardId, row);
                    }
                }
                _loaded = _byCardId.Count > 0;
            }
            catch
            {
                _byCardId.Clear();
                _loaded = false;
            }
        }

        /// <summary>Exact case-sensitive CardId lookup; false when not loaded or unknown.</summary>
        internal bool TryGet(string cardId, out TrinketDefinition definition)
        {
            definition = null;
            if (!_loaded || string.IsNullOrEmpty(cardId)) return false;
            return _byCardId.TryGetValue(cardId, out definition);
        }

        /// <summary>Returns required CardIds missing from the loaded registry (empty when loaded and complete).</summary>
        internal List<string> VerifyCoverage(IEnumerable<string> requiredCardIds)
        {
            var missing = new List<string>();
            if (requiredCardIds == null) return missing;
            foreach (string cardId in requiredCardIds)
            {
                TrinketDefinition ignored;
                if (string.IsNullOrEmpty(cardId) || !TryGet(cardId, out ignored))
                    missing.Add(cardId);
            }
            return missing;
        }

        /// <summary>Appends a diagnostic line to the configured root's bob_coach.log; no-op otherwise.</summary>
        internal void WriteDiagnostic(string message)
        {
            try
            {
                if (string.IsNullOrEmpty(ConfiguredRoot) || string.IsNullOrEmpty(message)) return;
                var line = string.Format(
                    "[{0:O}] [TrinketDefinitionRegistry] {1}\n", DateTime.UtcNow, message);
                File.AppendAllText(
                    Path.Combine(ConfiguredRoot, "bob_coach.log"), line, Encoding.UTF8);
            }
            catch { }
        }

        private static TrinketDefinition ParseDefinition(
            string cardId, Dictionary<string, object> row)
        {
            var definition = new TrinketDefinition
            {
                CardId = cardId,
                NameCn = AsString(row, "name_cn"),
                NameEn = AsString(row, "name_en"),
                Cost = AsInt(row, "cost"),
                Score = AsInt(row, "score"),
                Text = AsString(row, "text"),
            };

            var effects = AsArray(row, "effects");
            if (effects != null)
            {
                foreach (var raw in effects)
                {
                    var effectRow = raw as Dictionary<string, object>;
                    if (effectRow == null) continue;
                    var effect = new EffectDefinition
                    {
                        Type = AsString(effectRow, "type"),
                        Subtype = AsString(effectRow, "subtype"),
                        Value = AsNullableDouble(effectRow, "value"),
                    };
                    if (string.IsNullOrEmpty(effect.Type)) continue;
                    definition.Effects.Add(effect);
                }
            }

            var tribes = AsArray(row, "tribes");
            if (tribes != null)
            {
                foreach (var raw in tribes)
                {
                    var tribe = raw as string;
                    if (string.IsNullOrEmpty(tribe)) continue;
                    definition.Tribes.Add(tribe);
                }
            }
            return definition;
        }

        private static string AsString(Dictionary<string, object> row, string key)
        {
            object value;
            if (!row.TryGetValue(key, out value) || !(value is string)) return "";
            return (string)value;
        }

        private static List<object> AsArray(Dictionary<string, object> row, string key)
        {
            object value;
            if (!row.TryGetValue(key, out value)) return null;
            return value as List<object>;
        }

        private static int AsInt(Dictionary<string, object> row, string key)
        {
            object value;
            if (!row.TryGetValue(key, out value)) return 0;
            if (value is double) return Convert.ToInt32((double)value);
            var text = value as string;
            int parsed;
            if (text != null && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
                return parsed;
            return 0;
        }

        private static double? AsNullableDouble(Dictionary<string, object> row, string key)
        {
            object value;
            if (!row.TryGetValue(key, out value) || value == null) return null;
            if (value is double) return (double)value;
            var text = value as string;
            double parsed;
            if (text != null
                && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
                return parsed;
            return null;
        }

        /// <summary>Minimal recursive-descent JSON parser for the registry subset
        /// (objects, arrays, strings with escapes, numbers, booleans, null).</summary>
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
