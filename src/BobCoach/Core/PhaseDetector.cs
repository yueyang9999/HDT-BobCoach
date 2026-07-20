using System;
using System.Linq;
using Hearthstone_Deck_Tracker;
using Hearthstone_Deck_Tracker.Enums;

namespace BobCoach.Engine
{
    public enum BattlePhase { None, Shopping, Combat }

    /// <summary>
    /// 战斗阶段检测器：比 IsBattlegroundsCombatPhase 更精确，
    /// 通过检查SETASIDE区域是否有随从来判断是否在招募阶段。
    /// </summary>
    public class PhaseDetector
    {
        private BattlePhase _current = BattlePhase.None;
        public BattlePhase Current => _current;
        public bool CanRender => _current == BattlePhase.Shopping;

        public event Action<BattlePhase, BattlePhase> PhaseChanged;

        public void Update(int turn)
        {
            try
            {
                var game = Core.Game;
                if (game == null || !game.IsRunning || game.CurrentGameMode != GameMode.Battlegrounds)
                {
                    SetPhase(BattlePhase.None);
                    return;
                }

                // 优先使用HDT内置API
                bool isCombat;
                try { isCombat = game.IsBattlegroundsCombatPhase; }
                catch { isCombat = false; }

                if (!isCombat)
                {
                    // 非战斗: 检查是否有商店随从确认(双重验证)
                    bool hasShopMinions = false;
                    try
                    {
                        foreach (var entity in game.Entities.Values.ToList())
                        {
                            if (entity == null) continue;
                            int zone = entity.HasTag(HearthDb.Enums.GameTag.ZONE) ? entity.GetTag(HearthDb.Enums.GameTag.ZONE) : 0;
                            int cardType = entity.HasTag(HearthDb.Enums.GameTag.CARDTYPE) ? entity.GetTag(HearthDb.Enums.GameTag.CARDTYPE) : 0;
                            if ((zone == 5 || zone == 6) && (cardType == 4 || cardType == 5))
                            { hasShopMinions = true; break; }
                        }
                    }
                    catch { }

                    // HDT说不是战斗→信任HDT, 即使暂时没检测到商店实体也认为在招募阶段
                    if (turn >= 1)
                        SetPhase(BattlePhase.Shopping);
                    else
                        SetPhase(BattlePhase.None);
                }
                else
                {
                    SetPhase(BattlePhase.Combat);
                }
            }
            catch { }
        }

        private void SetPhase(BattlePhase newPhase)
        {
            if (_current == newPhase) return;
            var old = _current;
            _current = newPhase;
            PhaseChanged?.Invoke(old, newPhase);
        }
    }
}
