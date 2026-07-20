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
    }
}
