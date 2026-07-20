using System;
using System.Collections.Generic;

namespace BobCoach.Engine
{
    /// <summary>
    /// P1 GEV 金币等价值 — 效果表 + 估值计算(fable5 审查修正版)。
    /// GEV = StatTempo + EffectValue + SynergyValue(caller) + TripleValue(caller)。
    /// 效果与身材基准只从用户HDT提供的本机HearthDb事实派生。
    /// 关键词价值走 MinionData 实时标志(比 mechanics 静态标签更准, 含 buff 获得的关键词)。
    /// </summary>
    public class EffectValueTable
    {
        private static readonly IReadOnlyList<double> EmptyBaseline
            = Array.AsReadOnly(new double[0]);
        private readonly ICardEffectSource _effects;
        private readonly IReadOnlyList<double> _baseline;
        public bool Loaded { get; private set; }

        // fable5 修正参数
        private const double STAT_CAP = 6.0;      // M1: StatTempo 上限
        private const double EFFECT_CAP = 6.0;    // M2: EffectValue 总上限
        private const double SYNERGY_CAP = 3.0;
        private const double TRIPLE_CAP = 4.5;
        private const double GAMMA = 0.7;         // S1: 折现率
        private const double GOLD_EFFECT_MULT = 1.75; // S4: 金卡效果

        public EffectValueTable()
            : this(
                new CachedCardEffectSource(
                    new HearthDbCardEffectFactSource(),
                    new CardEffectRuleEvaluator(),
                    new HearthDbCardEffectCardIdNormalizer()),
                new CardEffectBaselineProvider(
                    new HearthDbCardPoolMembershipSource(),
                    new HearthDbCardEffectFactSource()))
        {
        }

        internal EffectValueTable(
            ICardEffectSource effects,
            ICardEffectBaselineProvider baseline)
        {
            _effects = effects ?? throw new ArgumentNullException(nameof(effects));
            IReadOnlyList<double> values = EmptyBaseline;
            Loaded = baseline != null && baseline.TryGet(out values);
            _baseline = Loaded && values != null ? values : EmptyBaseline;
        }

        /// <summary>玩家当前酒馆等级的身材基准(M1: 分母用 tavernTier 不是卡自身tier)。</summary>
        public double StatBaseline(int tavernTier)
        {
            int t = tavernTier < 1 ? 1 : (tavernTier > 7 ? 7 : tavernTier);
            if (_baseline.Count <= t) return 0;
            double b = _baseline[t];
            return b > 0 ? b : 0;
        }

        public double ComputeStatTempo(MinionData card, int tavernTier)
        {
            if (card == null) return 0;
            double b = StatBaseline(tavernTier);
            if (b <= 0) return 0;
            double v = 3.0 * (card.Attack + card.Health) / b;
            return v > STAT_CAP ? STAT_CAP : v;
        }

        /// <summary>关键词价值: 用 MinionData 实时标志(fable5 S4 数值)。</summary>
        private static double KeywordValue(MinionData c)
        {
            double v = 0;
            if (c.DivineShield) v += 0.5;
            if (c.Poisonous || c.Venomous) v += 0.5;
            if (c.Taunt) v += 0.3;
            if (c.Reborn) v += 0.4;
            if (c.Windfury || c.MegaWindfury) v += 0.3;
            return v;
        }

        /// <summary>每回合效果的折现乘数 Σγ^i, i=0..H-1(S1: γ=0.7, H=clamp(12−turn,1,5), 血≤10 减半)。</summary>
        private static double PerTurnDiscount(GameState state)
        {
            int H = 12 - state.Turn;
            if (H < 1) H = 1;
            if (H > 5) H = 5;
            if (state.Health <= 10) { H = H / 2; if (H < 1) H = 1; }
            double disc = 0, g = 1.0;
            for (int i = 0; i < H; i++) { disc += g; g *= GAMMA; }
            return disc;
        }

        public double ComputeEffectValue(MinionData card, GameState state, string dominantTribe = null)
        {
            if (card == null || state == null) return 0;
            double total = KeywordValue(card); // 关键词走实时标志
            double goldMult = card.Golden ? GOLD_EFFECT_MULT : 1.0;
            IReadOnlyList<CardEffectDefinition> definitions;
            if (_effects.TryGet(card.CardId, out definitions) && definitions != null)
            {
                foreach (var effect in definitions)
                {
                    if (effect == null || effect.Type == "keyword") continue;
                    string type = effect.Type;
                    double val = effect.ValueGold * goldMult;
                    if (effect.Per == "turn")
                        val *= PerTurnDiscount(state);
                    string effTribe = effect.Tribe;
                    if (!string.IsNullOrEmpty(effTribe))
                    {
                        if (type == "tribe_buff")
                        {
                            if (!BoardHasTribe(state, effTribe)) val = 0;
                        }
                        else if (!string.IsNullOrEmpty(dominantTribe) && effTribe != dominantTribe)
                        {
                            val *= 0.6;
                        }
                    }
                    total += val;
                }
            }
            return total > EFFECT_CAP ? EFFECT_CAP : total;
        }

        /// <summary>场上是否有指定部落的随从(tribe_buff 门控用)。</summary>
        private static bool BoardHasTribe(GameState state, string tribe)
        {
            if (state.BoardMinions == null || string.IsNullOrEmpty(tribe)) return false;
            foreach (var m in state.BoardMinions)
                if (m != null && !string.IsNullOrEmpty(m.Tribe) && MinionData.TribeMatches(m.Tribe, tribe))
                    return true;
            return false;
        }

        /// <summary>GEV = StatTempo + EffectValue + Synergy(≤3, caller供) + Triple(≤4.5, caller供)。</summary>
        public double ComputeGEV(MinionData card, GameState state, double synergyValue, double tripleValue, string dominantTribe = null)
        {
            if (card == null || state == null) return 0;
            double st = ComputeStatTempo(card, state.TavernTier);
            double ev = ComputeEffectValue(card, state, dominantTribe);
            double sy = synergyValue < 0 ? 0 : (synergyValue > SYNERGY_CAP ? SYNERGY_CAP : synergyValue);
            double tr = tripleValue < 0 ? 0 : (tripleValue > TRIPLE_CAP ? TRIPLE_CAP : tripleValue);
            return st + ev + sy + tr;
        }
    }
}
