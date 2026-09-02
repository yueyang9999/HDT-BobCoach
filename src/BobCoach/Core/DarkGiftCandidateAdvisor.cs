using System;
using System.Collections.Generic;
using System.Linq;

namespace BobCoach.Engine
{
    /// <summary>
    /// Scores dark gift discover candidates from the same board/tribe state used by
    /// the shop recommender. Unknown facts remain unrated instead of becoming fake
    /// top picks.
    /// </summary>
    public static class DarkGiftCandidateAdvisor
    {
        public static List<TrinketHint> Evaluate(
            IList<DiscoverCandidate> candidates,
            IList<MinionData> boardMinions,
            string compDir,
            Func<string, string> nameLookup,
            Func<string, IList<string>> tribeLookup,
            Func<string, int> tierLookup,
            Func<string, bool> isResourceCard = null,
            Func<string, bool> isCoreCard = null)
        {
            var hints = new List<TrinketHint>();
            if (candidates == null) return hints;

            var boardIds = new HashSet<string>(StringComparer.Ordinal);
            if (boardMinions != null)
            {
                foreach (var minion in boardMinions)
                {
                    if (minion != null && !string.IsNullOrEmpty(minion.CardId))
                        boardIds.Add(minion.CardId);
                }
            }

            string target = NormalizeTribe(compDir);
            for (int i = 0; i < candidates.Count; i++)
            {
                var candidate = candidates[i];
                if (candidate == null) continue;

                string cardId = candidate.CardId ?? "";
                string cardName = ResolveName(candidate.CardName, cardId, nameLookup);
                var tribes = tribeLookup != null && !string.IsNullOrEmpty(cardId)
                    ? tribeLookup(cardId) ?? new List<string>()
                    : new List<string>();
                int tier = tierLookup != null && !string.IsNullOrEmpty(cardId)
                    ? tierLookup(cardId)
                    : 0;

                bool matched = tribes.Any(t =>
                    !string.IsNullOrEmpty(t) && string.Equals(t, target, StringComparison.Ordinal));
                bool offTribe = IsOffTribe(tribes, target);
                bool hasBoardCopy = boardIds.Contains(cardId);
                bool isResource = isResourceCard != null && isResourceCard(cardId);
                bool isCore = isCoreCard != null && isCoreCard(cardId);

                double score = 1.0;
                string reason = "候选";
                if (isResource)
                {
                    score += 1.4;
                    reason = "资源牌";
                }
                if (isCore)
                {
                    score += 1.6;
                    reason = "核心牌";
                }
                if (tier >= 5)
                {
                    score += 0.8;
                    if (reason == "候选") reason = "高星";
                }
                else if (tier >= 4)
                {
                    score += 0.5;
                    if (reason == "候选") reason = "优质";
                }
                else if (tier >= 3)
                {
                    score += 0.2;
                }

                if (matched)
                {
                    score += 0.8;
                    reason = "匹配流派";
                }
                if (offTribe)
                {
                    score -= 0.7;
                    if (reason == "候选" || reason == "高星" || reason == "优质")
                        reason = "异族";
                }
                if (hasBoardCopy)
                {
                    score += 0.3;
                    if (reason == "候选") reason = "同牌对子";
                }

                bool rated = !string.IsNullOrEmpty(target)
                    && (matched || offTribe || tier > 0);
                hints.Add(new TrinketHint
                {
                    Index = i,
                    Name = cardName,
                    Score = score,
                    Reason = reason,
                    IsUnrated = !rated,
                });
            }

            hints.Sort((a, b) =>
            {
                int scoreCmp = b.Score.CompareTo(a.Score);
                if (scoreCmp != 0) return scoreCmp;
                return a.Index.CompareTo(b.Index);
            });
            for (int i = 0; i < hints.Count; i++)
                hints[i].IsTopPick = i == 0;
            return hints;
        }

        private static string ResolveName(
            string knownName, string cardId, Func<string, string> nameLookup)
        {
            string resolved = nameLookup != null && !string.IsNullOrEmpty(cardId)
                ? nameLookup(cardId)
                : "";
            if (!string.IsNullOrEmpty(resolved) && resolved != "unknown")
                return resolved;
            if (!string.IsNullOrEmpty(knownName)
                && knownName != "unknown"
                && knownName != cardId)
                return knownName;
            return "unknown";
        }

        private static bool IsOffTribe(IList<string> tribes, string target)
        {
            if (string.IsNullOrEmpty(target) || tribes == null || tribes.Count == 0)
                return false;
            if (target == "全" || target == "全部")
                return false;
            foreach (var tribe in tribes)
            {
                if (string.IsNullOrEmpty(tribe)) continue;
                if (tribe == target || tribe == "全" || tribe == "全部")
                    return false;
            }
            return true;
        }

        private static string NormalizeTribe(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            string code = value.Trim().TrimEnd('?');
            switch (code.ToUpperInvariant())
            {
                case "BEAST": return "野兽";
                case "MECHANICAL": return "机械";
                case "DEMON": return "恶魔";
                case "DRAGON": return "龙";
                case "ELEMENTAL": return "元素";
                case "UNDEAD": return "亡灵";
                case "PIRATE": return "海盗";
                case "QUILBOAR": return "野猪人";
                case "NAGA": return "纳迦";
                case "MURLOC": return "鱼人";
                case "TOTEM": return "图腾";
                case "ALL": return "全";
                default: return code;
            }
        }
    }
}
