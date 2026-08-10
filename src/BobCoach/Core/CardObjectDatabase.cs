using System;
using System.Collections.Generic;

namespace BobCoach.Engine
{
    public sealed class CardObjectFact
    {
        public string CardId = "";
        public string NameCn = "";
        public string NameEn = "";
        public string Type = "";
        public int TechLevel;
        public int Cost;
        public int Attack;
        public int Health;
        public List<string> Races = new List<string>();
        public List<string> Mechanics = new List<string>();
        public string TextCn = "";
        public bool CanActivate;
        public int ActivateCost;
    }

    /// <summary>
    /// 运行时只读卡牌对象库。由现有 season14 registry 聚合而成，
    /// 未知 CardId 一律返回 false / "unknown"，不泄漏裸 ID。
    /// </summary>
    public sealed class CardObjectDatabase
    {
        private readonly Season14MinionRegistry _minions = new Season14MinionRegistry();
        private readonly Season14TavernSpellRegistry _spells = new Season14TavernSpellRegistry();
        private readonly ActivateRegistry _activate = new ActivateRegistry();
        private readonly TrinketDefinitionRegistry _trinkets = new TrinketDefinitionRegistry();
        private bool _loaded;

        public bool IsLoaded { get { return _loaded; } }

        public void LoadFromConfiguredRoot()
        {
            _minions.LoadFromConfiguredRoot();
            _spells.LoadFromConfiguredRoot();
            _activate.LoadFromConfiguredRoot();
            _trinkets.LoadFromConfiguredRoot();
            _loaded = _minions.IsLoaded || _spells.IsLoaded
                || _activate.IsLoaded || _trinkets.IsLoaded;
        }

        public bool TryGet(string cardId, out CardObjectFact fact)
        {
            fact = null;
            if (!IsLoaded || string.IsNullOrEmpty(cardId)) return false;

            Season14MechanicCardFact cardFact;
            if (_minions.TryGet(cardId, out cardFact))
            {
                fact = ToFact(cardFact);
                TryApplyActivate(cardId, fact);
                return true;
            }
            if (_spells.TryGet(cardId, out cardFact))
            {
                fact = ToFact(cardFact);
                TryApplyActivate(cardId, fact);
                return true;
            }
            Season14MechanicCardFact activate;
            if (_activate.TryGet(cardId, out activate))
            {
                fact = new CardObjectFact
                {
                    CardId = activate.CardId,
                    NameCn = activate.NameZhCn,
                    NameEn = activate.NameEnUs,
                    Type = activate.Type,
                    TechLevel = activate.TechLevel,
                    Cost = activate.Cost,
                    Attack = activate.Attack,
                    Health = activate.Health,
                    TextCn = activate.TextZhCn,
                    CanActivate = true,
                    ActivateCost = activate.Cost,
                };
                return true;
            }

            TrinketDefinitionRegistry.TrinketDefinition trinket;
            if (_trinkets.TryGet(cardId, out trinket))
            {
                fact = new CardObjectFact
                {
                    CardId = trinket.CardId,
                    NameCn = trinket.NameCn,
                    NameEn = trinket.NameEn,
                    Type = "TRINKET",
                    Cost = trinket.Cost,
                    TextCn = trinket.TextCn,
                };
                return true;
            }
            return false;
        }

        private void TryApplyActivate(string cardId, CardObjectFact fact)
        {
            Season14MechanicCardFact activate;
            if (_activate.TryGet(cardId, out activate))
            {
                fact.CanActivate = true;
                fact.ActivateCost = ActivateFactSource.ParseActivateCost(
                    activate.TextEnUs, activate.TextZhCn);
            }
        }

        public string ResolveName(string cardId)
        {
            if (string.IsNullOrEmpty(cardId)) return "";
            CardObjectFact fact;
            return TryGet(cardId, out fact)
                ? (!string.IsNullOrEmpty(fact.NameCn) ? fact.NameCn : "unknown")
                : "unknown";
        }

        private static CardObjectFact ToFact(Season14MechanicCardFact source)
        {
            return new CardObjectFact
            {
                CardId = source.CardId,
                NameCn = source.NameZhCn,
                NameEn = source.NameEnUs,
                Type = source.Type,
                TechLevel = source.TechLevel,
                Cost = source.Cost,
                Attack = source.Attack,
                Health = source.Health,
                Races = new List<string>(source.Races ?? new List<string>()),
                Mechanics = new List<string>(source.BattlegroundsAssociatedRaces
                    ?? new List<string>()),
                TextCn = source.TextZhCn,
            };
        }
    }
}
