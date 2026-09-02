using System;
using System.Collections.Generic;

namespace BobCoach.Engine
{
    public class HeroPowerEngine
    {
        private readonly IHeroStrategySource _source;

        public HeroPowerEngine()
            : this(new CachedHeroStrategySource(
                new HearthDbHeroPowerFactSource(),
                new HeroStrategyRuleEvaluator()))
        {
        }

        internal HeroPowerEngine(IHeroStrategySource source)
        {
            _source = source ?? throw new ArgumentNullException("source");
        }

        public HeroStrategy GetStrategy(string heroCardId)
        {
            HeroStrategy strategy;
            return TryGet(heroCardId, out strategy)
                ? strategy
                : CreateDefaultStrategy();
        }

        public HeroStrategy GetStrategyForPower(string heroCardId, string heroPowerCardId)
        {
            HeroStrategy strategy;
            if (TryGet(heroPowerCardId, out strategy))
                return strategy;
            return GetStrategy(heroCardId);
        }

        public string GetUseSuggestion(
            string heroCardId,
            int gold,
            int turn,
            int boardSize,
            bool boardFull)
        {
            return GetUseSuggestionFromStrategy(
                GetStrategy(heroCardId), gold, turn, boardSize, boardFull);
        }

        public string GetUseSuggestion(
            string heroCardId,
            string heroPowerCardId,
            int gold,
            int turn,
            int boardSize,
            bool boardFull)
        {
            return GetUseSuggestionFromStrategy(
                GetStrategyForPower(heroCardId, heroPowerCardId),
                gold,
                turn,
                boardSize,
                boardFull);
        }

        public string GetUseSuggestion(
            string heroCardId,
            int gold,
            int turn,
            int boardSize,
            bool boardFull,
            int heroPowerCost,
            bool heroPowerExhausted,
            int heroPowerUnlockTurn,
            int heroPowerUnlockTier,
            int tavernTier)
        {
            return GetUseSuggestionFromStrategy(
                GetStrategy(heroCardId),
                gold,
                turn,
                boardSize,
                boardFull,
                heroPowerCost,
                heroPowerExhausted,
                heroPowerUnlockTurn,
                heroPowerUnlockTier,
                tavernTier);
        }

        private string GetUseSuggestionFromStrategy(
            HeroStrategy strategy,
            int gold,
            int turn,
            int boardSize,
            bool boardFull,
            int heroPowerCost = -1,
            bool heroPowerExhausted = false,
            int heroPowerUnlockTurn = 1,
            int heroPowerUnlockTier = 1,
            int tavernTier = 1)
        {
            if (strategy == null || strategy.PowerType == HeroPowerType.Passive)
                return "";

            int cost = heroPowerCost >= 0 ? heroPowerCost : strategy.PowerCost;
            if (cost < 0) cost = strategy.PowerCost;
            if (gold < cost)
                return "";
            if (heroPowerExhausted)
                return "";
            if (turn < heroPowerUnlockTurn)
                return "";
            if (tavernTier < heroPowerUnlockTier)
                return "";

            if (strategy.SpecialRule == "DIG_GOLDEN")
                return string.Format("挖金 · {0}费", cost);
            if (strategy.SpecialRule == "CHROMIE")
                return string.Format("法术酒馆 · {0}费", cost);
            if (strategy.UsePurpose == HeroUsePurpose.Economy)
                return string.Format("赚铸币 · {0}费换经济优势", cost);
            if (strategy.UsePurpose == HeroUsePurpose.Buff)
                return boardSize > 0
                    ? string.Format("加buff · 场面{0}个随从收益高", boardSize)
                    : "";
            if (strategy.UsePurpose == HeroUsePurpose.Resource)
                return string.Format("拿资源 · 补强场面", boardSize);
            if (strategy.UsePurpose == HeroUsePurpose.Combat)
                return boardSize > 0
                    ? string.Format("加战力 · 提升战斗胜率", boardSize)
                    : "";
            if (strategy.UsePurpose == HeroUsePurpose.Generic && !boardFull)
                return string.Format("使用技能 · {0}费", cost);
            return "";
        }

        public float GetLevelAggression(string heroCardId)
        {
            return GetStrategy(heroCardId).LevelAggression;
        }

        public float GetTribeAffinity(string heroCardId, string tribe)
        {
            if (string.IsNullOrEmpty(tribe)) return 0f;
            float value;
            return GetStrategy(heroCardId).TribeAffinity.TryGetValue(tribe, out value)
                ? value
                : 0f;
        }

        public bool HasSpecialRule(string heroCardId, string rule)
        {
            return GetStrategy(heroCardId).SpecialRule == (rule ?? "");
        }

        private bool TryGet(string cardId, out HeroStrategy strategy)
        {
            strategy = null;
            if (string.IsNullOrEmpty(cardId)) return false;
            try
            {
                HeroStrategy resolved;
                if (!_source.TryGet(cardId, out resolved) || resolved == null)
                    return false;
                strategy = CachedHeroStrategySource.Copy(resolved);
                return strategy != null;
            }
            catch
            {
                strategy = null;
                return false;
            }
        }

        private static HeroStrategy CreateDefaultStrategy()
        {
            return new HeroStrategy
            {
                HeroCardId = "",
                HeroName = "",
                PowerType = HeroPowerType.Passive,
                Archetype = HeroArchetype.General,
                PowerCost = 0,
                UnlockTurn = 1,
                UnlockTier = 1,
                HasDiscover = false,
                PowerHint = "",
                LevelAggression = 1.0f,
                UsePurpose = HeroUsePurpose.None,
                SpecialRule = "",
                TribeAffinity = new Dictionary<string, float>(StringComparer.Ordinal),
                SynergyTags = new HashSet<string>(StringComparer.Ordinal),
            };
        }
    }
}
