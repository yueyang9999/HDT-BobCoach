using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace BobCoach.Engine
{
    internal sealed class HeroStrategyRuleEvaluator : IHeroStrategyRuleEvaluator
    {
        public bool TryEvaluate(HeroPowerFact fact, out HeroStrategy strategy)
        {
            strategy = null;
            if (!IsValid(fact)) return false;

            try
            {
                string text = NormalizeAndExpand(fact.TextZhCn, fact.ScriptData);
                if (string.IsNullOrEmpty(text)) return false;

                var candidate = BuildStrategy(
                    fact.HeroCardId, "", text, fact.PowerCost, fact.HeroArmor);
                ApplySpecialRules(candidate);
                ApplyDataDrivenCalibration(candidate, fact.HeroCardId);  // R225 数据驱动英雄校准(最终覆盖规则)

                candidate.PowerType = DerivePowerType(
                    fact.PowerCost,
                    fact.HideCost,
                    fact.BaconHeroPowerActivated,
                    text);
                candidate.PowerCost = candidate.PowerType == HeroPowerType.Passive
                    ? 0
                    : fact.PowerCost;
                candidate.UnlockTurn = DeriveUnlockTurn(text);
                candidate.UnlockTier = DeriveUnlockTier(text);
                candidate.HasDiscover = text.Contains("发现");
                candidate.UsePurpose = InferUsePurpose(candidate.PowerType, text);
                candidate.SynergyTags = InferSynergyTags(text);
                candidate.HeroName = "";
                candidate.PowerHint = "";
                strategy = candidate;
                return true;
            }
            catch
            {
                strategy = null;
                return false;
            }
        }

        private static bool IsValid(HeroPowerFact fact)
        {
            return fact != null
                && !string.IsNullOrEmpty(fact.RequestedCardId)
                && !string.IsNullOrEmpty(fact.HeroCardId)
                && !string.IsNullOrEmpty(fact.PowerCardId)
                && fact.HeroArmor >= 0 && fact.HeroArmor <= 100
                && fact.PowerCost >= 0 && fact.PowerCost <= 20
                && !string.IsNullOrWhiteSpace(fact.TextZhCn)
                && (fact.ScriptData == null || fact.ScriptData.Length == 6);
        }

        private static string NormalizeAndExpand(string value, int[] scriptData)
        {
            string result = Regex.Replace(value ?? "", "<[^>]+>", "");
            if (scriptData != null)
            {
                for (int index = 0; index < scriptData.Length; index++)
                {
                    result = result.Replace(
                        "{" + index + "}",
                        scriptData[index].ToString(System.Globalization.CultureInfo.InvariantCulture));
                }
            }
            result = result.Replace('\u3000', ' ');
            return Regex.Replace(result, @"\s+", " ").Trim();
        }

        private static HeroPowerType DerivePowerType(
            int cost,
            bool hideCost,
            bool activated,
            string text)
        {
            if (hideCost || activated) return HeroPowerType.Passive;
            if (cost > 0) return HeroPowerType.Active;
            return string.IsNullOrEmpty(PassiveTrigger(text))
                ? HeroPowerType.Active
                : HeroPowerType.Passive;
        }

        private static string PassiveTrigger(string text)
        {
            string[] triggers =
            {
                "每回合一次", "战斗开始时", "在每一个回合开始时", "对战开始时",
                "每一个回合", "当你", "在友方", "在你的回合开始时",
            };
            string exact = triggers.FirstOrDefault(text.StartsWith) ?? "";
            if (!string.IsNullOrEmpty(exact)) return exact;
            if (text.StartsWith("每局", StringComparison.Ordinal)
                || text.StartsWith("在每局", StringComparison.Ordinal)) return "";
            if ((text.StartsWith("每", StringComparison.Ordinal)
                || text.StartsWith("在每", StringComparison.Ordinal))
                && text.Contains("时")) return "recurring-time-trigger";
            // R236 描述式被动识别(对抗性审查修正 R236b/R236final):
            // 顺序关键——先判明确主动动作词(优先), 再判明确被动描述词。
            // 宽泛词(获得/铸币/在你)不单独判被动, 防误伤主动buff(使一个随从获得)。
            string[] activeVerbs =
            {
                "选择一个", "选择2个", "使一个随从", "投一个", "获取一个",
                "发现一个", "替换为", "偷取", "施放", "消灭一个", "购买一张",
                "选择一条", "使一个友方随从", "触发一个", "投一",
            };
            // 文本以"被动"开头: 直接判被动(托里姆"被动 对战开始时,发现一个…")
            if (text.StartsWith("被动", StringComparison.Ordinal))
                return "passive-header";
            foreach (var av in activeVerbs)
                if (text.Contains(av)) return "";
            string[] passiveMarkers =
            {
                "随从消耗", "购买随从和刷新", "购买随从", "出售一个随从",
                "升级酒馆后", "回合结束时", "回合开始时", "每4个回合",
                "每回合开始时", "在你使用3张", "在你购买一个", "在你升级",
                "你的下一个", "被动", "开局时拥有", "每局对战限一次",
                "在你出售一个", "当你购买", "战斗开始时", "在每个回合开始时",
            };
            foreach (var marker in passiveMarkers)
                if (text.Contains(marker)) return "descriptive-passive";
            return "";
        }

        private static int DeriveUnlockTurn(string text)
        {
            var match = Regex.Match(text ?? "", @"第\s*(\d+)\s*回合解锁");
            int turn;
            if (match.Success && int.TryParse(match.Groups[1].Value, out turn)
                && turn >= 1 && turn <= 20)
                return turn;
            return (text ?? "").Contains("跳过你的前两个回合") ? 3 : 1;
        }

        private static int DeriveUnlockTier(string text)
        {
            var match = Regex.Match(text ?? "", @"等级\s*(\d+)\s*时解锁");
            int tier;
            return match.Success
                && int.TryParse(match.Groups[1].Value, out tier)
                && tier >= 1 && tier <= 7
                    ? tier
                    : 1;
        }

        private HeroStrategy BuildStrategy(string heroId, string name, string hint, int cost, int armor)
        {
            var strat = new HeroStrategy
            {
                HeroCardId = heroId,
                HeroName = name ?? "",
                PowerCost = cost,
                PowerHint = hint ?? "",
            };

            if (cost == 0 && string.IsNullOrEmpty(hint))
                strat.PowerType = HeroPowerType.Passive;
            else if (cost > 0 && !string.IsNullOrEmpty(hint) && hint.Contains("被动"))
                strat.PowerType = HeroPowerType.Conditional;
            else if (cost > 0)
                strat.PowerType = HeroPowerType.Active;
            else
                strat.PowerType = HeroPowerType.Passive;

            if (!string.IsNullOrEmpty(hint))
            {
                if (hint.Contains("铸币") || hint.Contains("金币") || hint.Contains("刷新") && hint.Contains("消耗"))
                    strat.Archetype = HeroArchetype.Econ;
                else if (hint.Contains("发现") && (hint.Contains("随从") || hint.Contains("牌")))
                    strat.Archetype = HeroArchetype.Greed;
                else if (hint.Contains("攻击力") || hint.Contains("生命值") || hint.Contains("+"))
                    strat.Archetype = HeroArchetype.Tempo;
                else if (hint.Contains("战斗开始时") || hint.Contains("亡语"))
                    strat.Archetype = HeroArchetype.Scaling;
                else
                    strat.Archetype = HeroArchetype.General;
            }

            if (cost == 0 && strat.PowerType == HeroPowerType.Passive && armor <= 10)
                strat.LevelAggression = 1.05f;
            else if (cost >= 2 && armor >= 15)
                strat.LevelAggression = 0.95f;
            else if (cost >= 3)
                strat.LevelAggression = 0.90f;

            return strat;
        }

        private void ApplySpecialRules(HeroStrategy strat)
        {
            var hint = strat.PowerHint ?? "";
            if (string.IsNullOrEmpty(hint)) return;

            if (hint.StartsWith("被动")) strat.PowerType = HeroPowerType.Passive;
            else if (hint.Length > 2 && hint[1] == '费' && hint[0] >= '0' && hint[0] <= '9')
                strat.PowerType = HeroPowerType.Active;

            if ((hint.Contains("买") && hint.Contains("2费")) || hint.Contains("买怪2费"))
            {
                strat.SpecialRule = "MILLHOUSE";
                strat.LevelAggression = 0.80f;
                strat.BuyValueBias = 0.05f;
            }
            if (hint.Contains("空过") || hint.Contains("跳过"))
            {
                strat.SpecialRule = "AFK";
                strat.LevelAggression = 0.70f;
            }
            if (hint.Contains("升本后得") || hint.Contains("升本获得"))
            {
                strat.SpecialRule = "OMU";
                strat.LevelAggression = 1.30f;
                strat.UpgradeValueBias = 0.08f;
            }
            if (hint.Contains("铸币上限"))
            {
                strat.SpecialRule = "CENARIUS";
                strat.LevelAggression = 0.90f;
                strat.PowerValueBias = 0.12f;
            }
            // R253: cost-decay hero (Togwaggle: next hero power costs 1 less each turn)
            if (hint.Contains("铸币减少") || (hint.Contains("减少") && hint.Contains("英雄技能")))
            {
                strat.SpecialRule = "COST_DECAY";
                strat.LevelAggression = 0.87f;
            }
            if (hint.Contains("升级") && hint.Contains("发现"))
            {
                strat.SpecialRule = "ELISE";
                strat.LevelAggression = 1.20f;
                strat.UpgradeValueBias = 0.05f;
            }
            if (hint.Contains("禁锢") || hint.Contains("锁入"))
            {
                strat.SpecialRule = "MAIEV";
                strat.LevelAggression = 0.90f;
                strat.BuyValueBias = -0.05f;
            }
            if ((hint.Contains("刷新") && hint.Contains("添加")) || hint.Contains("战列巡航舰"))
            {
                strat.SpecialRule = "LIFTOFF";
                strat.LevelAggression = 0.95f;
            }

            if (hint.Contains("刷新酒馆") && hint.Contains("高一级"))
            {
                strat.SpecialRule = "TOCKI";
                strat.LevelAggression = 1.10f;
                strat.PowerValueBias = 0.14f;
            }
            if (hint.Contains("出售") && (hint.Contains("铸币") || hint.Contains("金币")))
            {
                strat.SpecialRule = "GALLYWIX";
                strat.LevelAggression = 1.15f;
                strat.BuyValueBias = 0.04f;
            }
            if (hint.Contains("刷新") && hint.Contains("法术牌"))
            {
                strat.SpecialRule = "CHROMIE";
                strat.PowerValueBias = 0.04f;
                strat.BuyValueBias = 0.06f;
                strat.LevelAggression = 0.75f;  // R224 数据驱动: 前期不升本(0/21 局 T1-T3)
            }
            if (hint.Contains("制造") && hint.Contains("亡灵") && hint.Contains("作品"))
            {
                strat.SpecialRule = "PUTRICIDE";
                strat.PowerValueBias = 0.10f;
                strat.LevelAggression = 0.85f;
                strat.TribeAffinity["UNDEAD"] = 0.35f;
            }
            if (hint.Contains("消灭") && hint.Contains("复活") || hint.Contains("重新召唤") && hint.Contains("复制"))
            {
                strat.SpecialRule = "TALON";
                strat.PowerValueBias = 0.10f;
            }
            if (hint.Contains("发现两项") || hint.Contains("替换本技能"))
            {
                strat.SpecialRule = "GENN";
                strat.LevelAggression = 0.65f;
                strat.BuyValueBias = 0.10f;
            }
            if (hint.Contains("从两个任务中选择") || hint.Contains("完成后获得"))
            {
                strat.SpecialRule = "DENATHRIUS";
                strat.LevelAggression = 1.05f;
            }
            if (hint.Contains("发现一个英雄技能") || hint.Contains("冒险出发"))
            {
                strat.SpecialRule = "FINLEY";
                strat.LevelAggression = 1.00f;
            }
            if (hint.Contains("上一场战斗") && hint.Contains("死亡") && hint.Contains("发现"))
            {
                strat.SpecialRule = "SYLVANAS";
                strat.PowerValueBias = 0.05f;
            }
            if ((hint.Contains("等级4") || hint.Contains("等级 4")) && hint.Contains("解锁"))
            {
                strat.SpecialRule = "ALEXSTRASZA";
                strat.PowerValueBias = 0.03f;
                strat.LevelAggression = 1.25f;
                strat.UpgradeValueBias = 0.06f;
            }
            if (hint.Contains("骰") || hint.Contains("投一枚"))
            {
                strat.SpecialRule = "SNAKE_EYES";
                strat.PowerValueBias = 0.07f;
                strat.LevelAggression = 0.92f;
            }
            if (hint.Contains("投入") && hint.Contains("随从") && hint.Contains("锅中"))
            {
                strat.SpecialRule = "COOKIE";
                strat.PowerValueBias = 0.06f;
            }
            if (hint.Contains("酒馆法术") && hint.Contains("额外获得"))
            {
                strat.SpecialRule = "LAKANISHU";
                strat.LevelAggression = 0.92f;
                strat.BuyValueBias = 0.03f;
            }
            if (hint.Contains("复仇") && (hint.Contains("雏龙") || hint.Contains("奥妮克希亚")))
            {
                strat.SpecialRule = "ONYXIA";
                strat.LevelAggression = 0.85f;
            }
            if (hint.Contains("攻击力最低") && hint.Contains("亡语"))
            {
                strat.SpecialRule = "TAMSIN";
                strat.LevelAggression = 0.90f;
            }

            if (hint.Contains("龙")) strat.TribeAffinity["DRAGON"] = 0.20f;
            if (hint.Contains("机械") || hint.Contains("磁力"))
                strat.TribeAffinity["MECHANICAL"] = 0.20f;
            if (hint.Contains("海盗")) strat.TribeAffinity["PIRATE"] = 0.20f;
            if (hint.Contains("鱼人")) strat.TribeAffinity["MURLOC"] = 0.20f;
            if (hint.Contains("野兽")) strat.TribeAffinity["BEAST"] = 0.20f;
            if (hint.Contains("恶魔")) strat.TribeAffinity["DEMON"] = 0.20f;
            if (hint.Contains("元素")) strat.TribeAffinity["ELEMENTAL"] = 0.20f;
            if (hint.Contains("亡灵") || hint.Contains("亡语"))
                strat.TribeAffinity["UNDEAD"] = 0.20f;
            if (hint.Contains("野猪")) strat.TribeAffinity["QUILBOAR"] = 0.20f;
            if (hint.Contains("纳迦")) strat.TribeAffinity["NAGA"] = 0.20f;

            if (strat.PowerType == HeroPowerType.Active && strat.PowerValueBias == 0f)
            {
                if (strat.PowerCost == 0)
                    strat.PowerValueBias = 0.12f;
                else if (hint.Contains("发现"))
                    strat.PowerValueBias = 0.10f;
                else if (hint.Contains("铸币") || hint.Contains("金币") || hint.Contains("刷新"))
                    strat.PowerValueBias = 0.08f;
                else if (hint.Contains("获取"))
                    strat.PowerValueBias = 0.06f;
                else if (hint.Contains("+") || hint.Contains("buff") || hint.Contains("身材"))
                    strat.PowerValueBias = 0.05f;
            }
        }

        private static HeroUsePurpose InferUsePurpose(HeroPowerType powerType, string hint)
        {
            if (powerType == HeroPowerType.Passive || string.IsNullOrEmpty(hint))
                return HeroUsePurpose.None;
            if (hint.Contains("铸币") || (hint.Contains("刷新") && hint.Contains("费"))
                || hint.Contains("省钱"))
                return HeroUsePurpose.Economy;
            if (hint.Contains("buff") || hint.Contains("BUFF") || hint.Contains("+")
                || hint.Contains("成长") || hint.Contains("身材"))
                return HeroUsePurpose.Buff;
            if (hint.Contains("发现") || hint.Contains("获取") || hint.Contains("给")
                || hint.Contains("锁"))
                return HeroUsePurpose.Resource;
            if (hint.Contains("伤害") || hint.Contains("攻击") || hint.Contains("战斗"))
                return HeroUsePurpose.Combat;
            return HeroUsePurpose.Generic;
        }

        private static HashSet<string> InferSynergyTags(string hint)
        {
            hint = hint ?? "";
            var tags = new HashSet<string>(StringComparer.Ordinal);
            if (hint.Contains("龙")) tags.Add("DRAGON");
            if (hint.Contains("亡语") || hint.Contains("死亡")) tags.Add("DEATH");
            if (hint.Contains("机械")) tags.Add("MECHANICAL");
            if (hint.Contains("战吼")) tags.Add("BATTLECRY");
            if (hint.Contains("鱼人")) tags.Add("MURLOC");
            if (hint.Contains("海盗")) tags.Add("PIRATE");
            if (hint.Contains("元素")) tags.Add("ELEMENTAL");
            if (hint.Contains("恶魔")) tags.Add("DEMON");
            if (hint.Contains("野兽")) tags.Add("BEAST");
            if (hint.Contains("野猪")) tags.Add("QUILBOAR");
            if (hint.Contains("纳迦")) tags.Add("NAGA");
            if (hint.Contains("亡灵")) tags.Add("UNDEAD");
            if (hint.Contains("发现")) tags.Add("DISCOVER");
            return tags;
        }

                // R225/R226 数据驱动英雄校准(1281局): [levelAgg, earlySuppressTurn, buyBias, powerBias, refBias, earlyBuyBias, earlyRefBias]
        private static readonly Dictionary<string, float[]> DataDrivenHeroParams = new Dictionary<string, float[]>(StringComparer.Ordinal)
        {
            { "BG24_HERO_100", new float[] { 0.9f, 0f, 0.039f, 0f, 0.012f, -0.17f, -0.03f } },
            { "BG25_HERO_105", new float[] { 0.91f, 0f, 0.028f, 0f, 0.016f, -0.32f, 0.02f } },
            { "BG20_HERO_202", new float[] { 0.93f, 0f, 0.039f, 0f, 0.02f, 0.08f, 0.04f } },
            { "TB_BaconShop_HERO_27", new float[] { 0.95f, 0f, 0.041f, 0f, -0.034f, -0.04f, -0.61f } },
            { "TB_BaconShop_HERO_50", new float[] { 0.86f, 0f, 0.018f, 0.02f, 0f, -0.17f, -0.18f } },
            { "BG30_HERO_304", new float[] { 0.86f, 0f, 0.017f, 0f, 0f, -0.25f, -0.21f } },
            { "BG31_HERO_811", new float[] { 0.86f, 0f, 0.014f, 0f, -0.01f, -0.23f, -0.31f } },
            { "TB_BaconShop_HERO_10", new float[] { 0.83f, 0f, 0.071f, 0.02f, 0.006f, -0.03f, -0.11f } },
            { "BG32_HERO_001", new float[] { 0.88f, 0f, 0.05f, 0f, 0.01f, -0.38f, -0.06f } },
            { "BG23_HERO_305", new float[] { 0.87f, 0f, 0.035f, 0f, 0.002f, -0.3f, -0.16f } },
            { "TB_BaconShop_HERO_16", new float[] { 0.62f, 4f, 0.006f, 0.05f, 0.022f, 0.68f, 0.09f } },
            { "TB_BaconShop_HERO_34", new float[] { 0.89f, 0f, 0.031f, 0f, 0.01f, -0.27f, -0.08f } },
            { "BG20_HERO_242", new float[] { 0.79f, 4f, 0.038f, 0f, -0.006f, -0.15f, -0.23f } },
            { "BG21_HERO_010", new float[] { 0.8f, 0f, 0.045f, 0f, 0f, 0.45f, -0.18f } },
            { "TB_BaconShop_HERO_62", new float[] { 0.9f, 0f, 0.039f, 0f, 0.012f, 0.05f, -0.01f } },
            { "BG20_HERO_283", new float[] { 0.96f, 0f, 0.036f, 0f, 0.004f, 0f, -0.13f } },
            { "TB_BaconShop_HERO_43", new float[] { 0.86f, 0f, 0.085f, 0.05f, 0.026f, 0.11f, 0.14f } },
            { "TB_BaconShop_HERO_40", new float[] { 0.95f, 0f, 0.054f, 0f, 0.014f, -0.08f, -0.01f } },
            { "BG34_HERO_001", new float[] { 0.95f, 0f, 0.026f, 0.05f, 0.018f, 0.16f, 0.04f } },
            { "BG31_HERO_003", new float[] { 0.82f, 0f, 0.035f, 0.02f, 0.022f, 0.05f, 0.09f } },
            { "BG20_HERO_280", new float[] { 0.92f, 0f, 0.036f, 0f, 0.028f, -0.05f, 0.17f } },
            { "BG34_HERO_000", new float[] { 1.12f, 0f, 0.022f, 0f, 0.016f, -0.19f, -0.01f } },
            { "TB_BaconShop_HERO_67", new float[] { 0.74f, 4f, 0.064f, 0.02f, 0.13f, 2.37f, 1.42f } },
            { "TB_BaconShop_HERO_41", new float[] { 0.85f, 0f, 0.024f, 0f, 0.012f, -0.32f, -0.03f } },
            { "BG32_HERO_002", new float[] { 0.87f, 0f, 0.032f, 0f, 0.012f, -0.02f, -0.03f } },
            { "BG34_HERO_004", new float[] { 1.13f, 0f, 0.027f, 0f, 0.006f, -0.21f, -0.11f } },
            { "BG27_HERO_801", new float[] { 0.91f, 0f, 0.033f, 0f, -0.004f, 0f, -0.23f } },
            { "BG22_HERO_201", new float[] { 0.83f, 0f, 0.025f, 0f, 0.044f, 0.83f, 0.34f } },
            { "TB_BaconShop_HERO_90", new float[] { 0.94f, 0f, 0.031f, 0f, 0.016f, 0.08f, -0.01f } },
            { "BG21_HERO_020", new float[] { 0.82f, 0f, 0.025f, 0f, 0.044f, 0.4f, 0.37f } },
            { "TB_BaconShop_HERO_57", new float[] { 0.76f, 4f, 0.022f, 0f, 0.092f, 0.49f, 0.99f } },
            { "BG25_HERO_103", new float[] { 0.92f, 0f, 0.007f, 0f, 0.012f, -0.09f, -0.03f } },
            { "BG31_HERO_005", new float[] { 0.89f, 0f, 0.023f, 0f, 0.024f, -0.17f, 0.14f } },
            { "TB_BaconShop_HERO_49", new float[] { 0.8f, 0f, 0.071f, 0f, 0.01f, -0.02f, -0.06f } },
            { "BG22_HERO_000", new float[] { 0.91f, 0f, 0.015f, 0f, 0.01f, -0.21f, -0.06f } },
            { "TB_BaconShop_HERO_68", new float[] { 0.78f, 4f, 0.068f, 0f, 0.032f, 0.15f, 0.22f } },
            { "TB_BaconShop_HERO_02", new float[] { 0.86f, 0f, 0.012f, 0f, 0.06f, -0.19f, 0.57f } },
            { "BG35_HERO_001", new float[] { 0.76f, 4f, -0.001f, 0f, 0.026f, -0.11f, 0.14f } },
            { "BG26_HERO_104", new float[] { 0.86f, 0f, 0.034f, 0f, 0.022f, -0.17f, 0.12f } },
            { "BG28_HERO_400", new float[] { 0.83f, 0f, 0.012f, 0f, 0.028f, -0.21f, 0.19f } },
            { "TB_BaconShop_HERO_702", new float[] { 0.99f, 0f, 0.008f, 0f, 0.028f, 0.12f, 0.17f } },
            { "TB_BaconShop_HERO_44", new float[] { 0.9f, 0f, 0.02f, 0f, 0.062f, 0.59f, 0.59f } },
            { "TB_BaconShop_HERO_35", new float[] { 0.95f, 0f, 0.028f, 0f, 0.008f, -0.1f, -0.11f } },
            { "BG26_HERO_101", new float[] { 0.81f, 0f, 0.061f, 0f, 0.01f, -0.28f, -0.03f } },
            { "TB_BaconShop_HERO_64", new float[] { 0.86f, 0f, 0.024f, 0.05f, 0.012f, 0f, -0.03f } },
            { "BG28_HERO_800", new float[] { 0.76f, 4f, 0.03f, 0f, 0.014f, 0.14f, -0.01f } },
            { "TB_BaconShop_HERO_74", new float[] { 0.87f, 0f, 0.031f, 0f, 0.036f, 0.22f, 0.27f } },
            { "BG20_HERO_101", new float[] { 0.93f, 0f, 0.021f, 0f, -0.012f, -0.41f, -0.33f } },
            { "TB_BaconShop_HERO_55", new float[] { 0.9f, 0f, 0.033f, 0f, 0.01f, -0.26f, -0.06f } },
            { "BG21_HERO_000", new float[] { 0.93f, 0f, 0.025f, 0f, 0.01f, -0.26f, -0.06f } },
            { "TB_BaconShop_HERO_22", new float[] { 0.92f, 0f, -0.002f, 0f, 0.002f, 0f, 0f } },
            { "TB_BaconShop_HERO_23", new float[] { 0.88f, 0f, 0.024f, 0f, 0.002f, 0f, 0f } },
            { "TB_BaconShop_HERO_94", new float[] { 0.82f, 0f, 0.045f, 0f, 0.056f, 0f, 0f } },
            { "TB_BaconShop_HERO_75", new float[] { 0.94f, 0f, -0.007f, 0f, 0f, 0f, 0f } },
            { "TB_BaconShop_HERO_21", new float[] { 0.88f, 0f, 0.033f, 0f, 0.014f, 0f, 0f } },
            { "TB_BaconShop_HERO_93", new float[] { 0.88f, 0f, 0f, 0.05f, 0.026f, 0f, 0f } },
            { "BG31_HERO_006", new float[] { 0.84f, 0f, 0.057f, 0f, 0.032f, 0f, 0f } },
            { "TB_BaconShop_HERO_60", new float[] { 0.68f, 0f, 0.04f, 0f, -0.002f, 0f, 0f } },
            { "TB_BaconShop_HERO_28", new float[] { 0.84f, 0f, 0.019f, 0f, 0.046f, 0f, 0f } },
            { "BG23_HERO_303", new float[] { 0.84f, 0f, 0.016f, 0f, 0.012f, 0f, 0f } },
            { "TB_BaconShop_HERO_12", new float[] { 0.8f, 4f, 0.026f, 0f, 0.086f, 0f, 0f } },
            { "TB_BaconShop_HERO_56", new float[] { 0.81f, 0f, 0.015f, 0f, -0.004f, 0f, 0f } },
            { "TB_BaconShop_HERO_42", new float[] { 0.95f, 0f, 0.014f, 0f, 0.034f, 0f, 0f } },
            { "BG28_HERO_801", new float[] { 0.76f, 4f, 0.021f, 0f, 0.016f, 0f, 0f } },
            { "BG34_HERO_002", new float[] { 0.76f, 4f, -0.001f, 0f, -0.024f, 0f, 0f } },
            { "BG36_HERO_105", new float[] { 0.76f, 4f, 0.025f, 0.05f, -0.01f, 0f, 0f } },
            { "BG20_HERO_282", new float[] { 0.93f, 0f, -0.011f, 0f, 0.012f, 0f, 0f } },
            { "TB_BaconShop_HERO_11", new float[] { 0.76f, 4f, -0.004f, 0.05f, -0.004f, 0f, 0f } },
            { "TB_BaconShop_HERO_18", new float[] { 0.82f, 0f, 0.048f, 0f, 0.02f, 0f, 0f } },
            { "BG20_HERO_103", new float[] { 0.82f, 0f, 0.022f, 0f, -0.02f, 0f, 0f } },
            { "TB_BaconShop_HERO_17", new float[] { 0.82f, 0f, 0.006f, 0f, 0.036f, 0f, 0f } },
            { "TB_BaconShop_HERO_70", new float[] { 0.82f, 0f, 0.057f, 0f, 0.008f, 0f, 0f } },
            { "TB_BaconShop_HERO_14", new float[] { 0.96f, 0f, -0.005f, 0f, 0.016f, 0f, 0f } },
            { "BG22_HERO_007", new float[] { 0.87f, 0f, 0.002f, 0.05f, 0.008f, 0f, 0f } },

            { "BG20_HERO_201", new float[] { 1.08f, 0f, 0.02f, 0f, 0f, 0f, 0f } },
            { "BG20_HERO_301", new float[] { 0.96f, 0f, 0.015f, 0f, 0f, 0f, 0f } },
            { "BG21_HERO_030", new float[] { 0.9f, 0f, 0.038f, 0f, 0f, 0f, 0f } },
            { "BG22_HERO_003", new float[] { 0.72f, 0f, 0.025f, 0f, 0f, 0f, 0f } },
            { "BG22_HERO_305", new float[] { 0.84f, 0f, 0.017f, 0f, 0f, 0f, 0f } },
            { "BG23_HERO_201", new float[] { 0.72f, 0f, 0.028f, 0f, 0f, 0f, 0f } },
            { "BG26_HERO_102", new float[] { 0.72f, 0f, 0.011f, 0f, 0f, 0f, 0f } },
            { "BG31_HERO_801", new float[] { 0.66f, 0f, 0.013f, 0f, 0f, 0f, 0f } },
            { "BG31_HERO_802", new float[] { 0.72f, 0f, 0.017f, 0f, 0f, 0f, 0f } },
            { "BG33_HERO_001", new float[] { 0.81f, 0f, 0.017f, 0f, 0f, 0f, 0f } },
            { "TB_BaconShop_HERO_08", new float[] { 0.9f, 0f, 0.015f, 0f, 0f, 0f, 0f } },
            { "TB_BaconShop_HERO_25", new float[] { 0.84f, 0f, 0.049f, 0f, 0f, 0f, 0f } },
            { "TB_BaconShop_HERO_36", new float[] { 1.08f, 0f, 0.046f, 0f, 0f, 0f, 0f } },
            { "TB_BaconShop_HERO_38", new float[] { 0.72f, 0f, 0.019f, 0f, 0f, 0f, 0f } },
            { "TB_BaconShop_HERO_39", new float[] { 0.72f, 0f, 0.021f, 0f, 0f, 0f, 0f } },
            { "TB_BaconShop_HERO_45", new float[] { 0.72f, 0f, 0.012f, 0f, 0f, 0f, 0f } },
            { "TB_BaconShop_HERO_53", new float[] { 0.72f, 0f, 0.005f, 0f, 0f, 0f, 0f } },
            { "TB_BaconShop_HERO_58", new float[] { 0.81f, 0f, 0.026f, 0f, 0f, 0f, 0f } },
            { "TB_BaconShop_HERO_59", new float[] { 0.81f, 0f, 0.027f, 0f, 0f, 0f, 0f } },
            { "TB_BaconShop_HERO_71", new float[] { 0.72f, 0f, 0.018f, 0f, 0f, 0f, 0f } },
            { "TB_BaconShop_HERO_72", new float[] { 0.72f, 0f, 0.014f, 0f, 0f, 0f, 0f } },
            { "TB_BaconShop_HERO_76", new float[] { 0.72f, 0f, 0.014f, 0f, 0f, 0f, 0f } },
            { "TB_BaconShop_HERO_78", new float[] { 0.72f, 0f, 0.014f, 0f, 0f, 0f, 0f } },
            // R236 数据块已删除(2026-09-02): 63条与 R225 权威表(hero_strategy_params.json 97英雄)重复47键+
            // 含16条 Buddy/非英雄实体, 导致 Dictionary 重复键→类型初始化异常→HeroPowerEngine 静默降级。
            // 修复: 保留与权威数据源一致的 R225 97条; R236 "HP频率" 数据如需校准走独立通道。
        };

        // R225: 数据驱动英雄校准——按英雄应用核心参数
        private static void ApplyDataDrivenCalibration(HeroStrategy strategy, string heroCardId)
        {
            if (strategy == null || string.IsNullOrEmpty(heroCardId)) return;
            float[] prm;
            if (!DataDrivenHeroParams.TryGetValue(heroCardId, out prm)) return;
            strategy.LevelAggression = prm[0];
            strategy.EarlyUpgradeSuppressTurn = (int)prm[1];
            strategy.BuyValueBias = prm[2];
            strategy.PowerValueBias = Math.Max(strategy.PowerValueBias, prm[3]);
            strategy.RefreshValueBias = prm[4];
            strategy.EarlyBuyBias = prm[5];
            strategy.EarlyRefBias = prm[6];
        }
    }


}
