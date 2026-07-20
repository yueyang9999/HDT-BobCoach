using System.Collections.Generic;

namespace BobCoach.Engine
{
    /// <summary>
    /// 情境策略调整器。根据ContextDetector产出的情境类型，
    /// 调整价值函数权重、规则阈值、动作偏好。
    /// </summary>
    public class StrategyAdjuster
    {
        private ContextThresholds _thresholds;

        public StrategyAdjuster()
        {
            _thresholds = ContextThresholds.Default;
        }

        /// <summary>从JSON加载校准数据</summary>
        public void LoadThresholds(string json)
        {
            _thresholds = ContextThresholds.FromJson(json);
        }

        /// <summary>根据情境产出调整后的策略参数</summary>
        public AdjustedStrategy GetAdjustedStrategy(
            ContextDetector.ContextResult context,
            float[] baseWeights,
            HeroStrategy heroStrat)
        {
            var result = new AdjustedStrategy
            {
                Context = context.Type,
                Weights = (float[])baseWeights.Clone(),
                DangerHp = _thresholds.GetDangerHp(context.Type),
                BlockLevelUp = false,
                LevelUpBias = 0f,
                SellBias = 0f,
                DesperatePowerRatio = 0.5f,
                TechCardMultiplier = 1.0f
            };

            // ── 加载情境权重增量 ──
            var deltas = _thresholds.GetWeightDeltas(context.Type);
            if (deltas != null)
            {
                foreach (var kv in deltas)
                {
                    int idx = kv.Key;
                    if (idx >= 0 && idx < result.Weights.Length)
                        result.Weights[idx] += kv.Value;
                }
            }

            // ── 加载动作偏好 ──
            var biases = _thresholds.GetActionBiases(context.Type);
            if (biases != null)
            {
                if (biases.TryGetValue("levelBias", out float lb)) result.LevelUpBias = lb;
                if (biases.TryGetValue("sellBias", out float sb)) result.SellBias = sb;
            }

            // ── 加载HP阈值 ──
            result.DangerHp = _thresholds.GetDangerHp(context.Type);

            // ── 情境特定逻辑 ──
            switch (context.Type)
            {
                case SituationType.DESPERATE:
                    result.BlockLevelUp = true;
                    result.DesperatePowerRatio = 0.4f;
                    result.TechCardMultiplier = 3.5f;
                    break;
                case SituationType.UNDER_PRESSURE:
                    result.DesperatePowerRatio = 0.45f;
                    result.TechCardMultiplier = 2.0f;
                    break;
                case SituationType.POWER_CURVE:
                    result.DangerHp = 18;
                    result.TechCardMultiplier = 0.8f; // 降低tech卡权重，贪核心
                    break;
                case SituationType.ECON_TURN:
                    result.TechCardMultiplier = 1.0f;
                    break;
            }

            // ── 英雄修正: 覆盖默认阈值 ──
            if (heroStrat.Archetype == HeroArchetype.Econ)
            {
                // 经济型英雄更激进
                result.LevelUpBias += 0.02f;
                if (context.Type == SituationType.STANDARD)
                    result.DangerHp -= 2;
            }
            else if (heroStrat.Archetype == HeroArchetype.Survival)
            {
                result.DangerHp += 3; // 生存型英雄更保守
            }

            return result;
        }
    }

    /// <summary>调整后的策略参数包</summary>
    public class AdjustedStrategy
    {
        public SituationType Context;
        public float[] Weights;
        public int DangerHp;
        public bool BlockLevelUp;
        public float LevelUpBias;
        public float SellBias;
        public float DesperatePowerRatio;
        public float TechCardMultiplier;
    }

    /// <summary>情境阈值数据(从JSON加载)</summary>
    internal class ContextThresholds
    {
        private Dictionary<string, Dictionary<int, float>> _weightDeltas;
        private Dictionary<string, Dictionary<string, float>> _actionBiases;
        private Dictionary<string, int> _hpThresholds;
        private Dictionary<string, bool> _blockLevel;

        public static ContextThresholds Default => new ContextThresholds
        {
            _weightDeltas = new Dictionary<string, Dictionary<int, float>>
            {
                ["POWER_CURVE"] = new Dictionary<int, float> { [0] = 0.08f, [9] = 0.12f, [15] = -0.10f },
                ["STANDARD"] = new Dictionary<int, float>(),
                ["UNDER_PRESSURE"] = new Dictionary<int, float> { [4] = 0.10f, [15] = 0.08f, [9] = -0.06f },
                ["DESPERATE"] = new Dictionary<int, float> { [15] = 0.20f, [4] = 0.12f, [9] = -0.18f, [1] = 0.08f },
                ["ECON_TURN"] = new Dictionary<int, float> { [17] = 0.08f, [7] = 0.06f, [16] = 0.05f }
            },
            _actionBiases = new Dictionary<string, Dictionary<string, float>>
            {
                ["POWER_CURVE"] = new Dictionary<string, float> { ["levelBias"] = 0.08f, ["sellBias"] = -0.03f },
                ["STANDARD"] = new Dictionary<string, float> { ["levelBias"] = 0f, ["sellBias"] = 0f },
                ["UNDER_PRESSURE"] = new Dictionary<string, float> { ["levelBias"] = -0.04f, ["sellBias"] = 0.02f },
                ["DESPERATE"] = new Dictionary<string, float> { ["levelBias"] = -0.20f, ["sellBias"] = 0.04f },
                ["ECON_TURN"] = new Dictionary<string, float> { ["levelBias"] = 0.02f, ["sellBias"] = -0.02f }
            },
            _hpThresholds = new Dictionary<string, int>
            {
                ["POWER_CURVE"] = 18, ["STANDARD"] = 12, ["UNDER_PRESSURE"] = 8, ["DESPERATE"] = 5, ["ECON_TURN"] = 14
            },
            _blockLevel = new Dictionary<string, bool>
            {
                ["POWER_CURVE"] = false, ["STANDARD"] = false, ["UNDER_PRESSURE"] = false, ["DESPERATE"] = true, ["ECON_TURN"] = false
            }
        };

        public static ContextThresholds FromJson(string json)
        {
            // 简单JSON解析(不依赖Newtonsoft.Json)
            var result = Default;
            try
            {
                var obj = TinyJson.Parse(json);
                if (obj is Dictionary<string, object> root)
                {
                    // 解析hpThresholds
                    if (root.TryGetValue("hpThresholds", out var hpObj) && hpObj is Dictionary<string, object> hpDict)
                    {
                        foreach (var kv in hpDict)
                        {
                            if (kv.Value is Dictionary<string, object> inner)
                            {
                                if (inner.TryGetValue("dangerHp", out var dhp) && dhp is long l)
                                    result._hpThresholds[kv.Key] = (int)l;
                                if (inner.TryGetValue("blockLevel", out var bl) && bl is bool b)
                                    result._blockLevel[kv.Key] = b;
                            }
                        }
                    }
                }
            }
            catch { /* 解析失败时使用默认值 */ }
            return result;
        }

        public Dictionary<int, float> GetWeightDeltas(SituationType ctx)
        {
            var key = ctx.ToString();
            return _weightDeltas.TryGetValue(key, out var d) ? d : _weightDeltas["STANDARD"];
        }

        public Dictionary<string, float> GetActionBiases(SituationType ctx)
        {
            var key = ctx.ToString();
            return _actionBiases.TryGetValue(key, out var d) ? d : _actionBiases["STANDARD"];
        }

        public int GetDangerHp(SituationType ctx)
        {
            var key = ctx.ToString();
            return _hpThresholds.TryGetValue(key, out var v) ? v : 12;
        }

        public bool GetBlockLevel(SituationType ctx)
        {
            var key = ctx.ToString();
            return _blockLevel.TryGetValue(key, out var v) ? v : false;
        }
    }

    /// <summary>迷你JSON解析器(仅解析嵌套字典/数字/布尔值)</summary>
    internal static class TinyJson
    {
        public static object Parse(string json)
        {
            json = json.Trim();
            if (json.StartsWith("{")) return ParseObject(json, 0, out _);
            return null;
        }

        private static Dictionary<string, object> ParseObject(string json, int start, out int end)
        {
            var result = new Dictionary<string, object>();
            int i = start + 1;
            while (i < json.Length)
            {
                while (i < json.Length && char.IsWhiteSpace(json[i])) i++;
                if (json[i] == '}') { end = i + 1; return result; }
                if (json[i] == ',') { i++; continue; }

                // Parse key
                int keyStart = json.IndexOf('"', i) + 1;
                int keyEnd = json.IndexOf('"', keyStart);
                string key = json.Substring(keyStart, keyEnd - keyStart);
                i = json.IndexOf(':', keyEnd) + 1;

                // Parse value
                while (i < json.Length && char.IsWhiteSpace(json[i])) i++;
                if (json[i] == '"') { i++; int ve = json.IndexOf('"', i); result[key] = json.Substring(i, ve - i); i = ve + 1; }
                else if (json[i] == '{') { result[key] = ParseObject(json, i, out i); }
                else if (json[i] == 't' || json[i] == 'f') { result[key] = json[i] == 't'; i += json[i] == 't' ? 4 : 5; }
                else { int ve = i; while (ve < json.Length && (char.IsDigit(json[ve]) || json[ve] == '.' || json[ve] == '-')) ve++; if (ve > i) { var ns = json.Substring(i, ve - i); if (long.TryParse(ns, out long lv)) result[key] = lv; else if (float.TryParse(ns, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float fv)) result[key] = fv; } i = ve; }
            }
            end = i;
            return result;
        }
    }
}
