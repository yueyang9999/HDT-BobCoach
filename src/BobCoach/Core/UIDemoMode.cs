using System.Collections.Generic;

namespace BobCoach.Engine
{
    /// <summary>
    /// UI演示模式：生成合成GameState，覆盖全部UI元素的渲染测试。
    /// F9切换，方向键左右切换场景。
    /// </summary>
    public class UIDemoMode
    {
        private int _scenarioIndex = 0;
        private bool _active = false;
        public bool Active => _active;

        public string ScenarioName
        {
            get
            {
                var names = new[] { "早期冲刺", "中期巡航", "残血保命", "升本紧急", "卖牌标记", "满场决策" };
                return _scenarioIndex < names.Length ? names[_scenarioIndex] : "";
            }
        }

        public void Activate() { _active = true; _scenarioIndex = 0; }
        public void Deactivate() { _active = false; }
        public void Next() { _scenarioIndex = (_scenarioIndex + 1) % 6; }
        public void Prev() { _scenarioIndex = (_scenarioIndex - 1 + 6) % 6; }

        public GameState GetScenario()
        {
            switch (_scenarioIndex)
            {
                case 0: return MakeEarlySprint();
                case 1: return MakeMidCruise();
                case 2: return MakeLowHpConserve();
                case 3: return MakeLevelUpUrgent();
                case 4: return MakeSellCandidate();
                default: return MakeFullBoard();
            }
        }

        // ── 辅助工厂方法 ──

        private static MinionData M(string name, string tribe, int tier, int atk, int hp, int pos = -1, bool gold = false)
        {
            return new MinionData
            {
                CardId = "BG_SIM_" + name.Replace(" ", ""),
                CardName = name,
                Tribe = tribe,
                Tier = tier,
                Attack = atk,
                Health = hp,
                Position = pos,
                Golden = gold,
                Cost = 3,
            };
        }

        private static MinionData Spell(string name, int tier, int cost)
        {
            return new MinionData
            {
                CardId = "BG_SPELL_" + name.Replace(" ", ""),
                CardName = name,
                Tier = tier,
                Cost = cost,
                IsSpell = true,
            };
        }

        // ── 场景0: 早期冲刺 (Turn 4, 健康, 无流派方向, 测试兜底) ──
        private static GameState MakeEarlySprint()
        {
            return new GameState
            {
                Turn = 4, Gold = 7, MaxGold = 8, TavernTier = 2,
                Health = 34, MaxHealth = 34, Phase = "shop",
                HeroCardId = "TB_BaconShop_HERO_41",
                HeroName = "米尔菲丝·风橡",
                BoardMinions = new List<MinionData> {
                    M("微型木乃伊", "MECHANICAL", 1, 2, 3, 0),
                    M("鱼人猎潮者", "MURLOC", 1, 2, 1, 1),
                },
                ShopMinions = new List<MinionData> {
                    M("麦田傀儡", "MECHANICAL", 2, 2, 3),
                    M("爆爆机器人", "MECHANICAL", 2, 2, 2),
                    M("龙人军官", "DRAGON", 1, 2, 3),
                    Spell("铸币", 2, 2),
                },
                HandMinions = new List<MinionData> {
                    M("偏折机器人", "MECHANICAL", 3, 3, 2),
                    M("巡游者", "MURLOC", 1, 2, 1),
                },
            };
        }

        // ── 场景1: 中期巡航 (Turn 8, 场面成型, 流派明确) ──
        private static GameState MakeMidCruise()
        {
            return new GameState
            {
                Turn = 8, Gold = 8, MaxGold = 9, TavernTier = 3,
                Health = 28, MaxHealth = 34, Phase = "shop",
                HeroCardId = "BG20_HERO_101",
                HeroName = "凯瑞尔·罗姆",
                BoardMinions = new List<MinionData> {
                    M("钴制卫士", "MECHANICAL", 3, 5, 4, 0),
                    M("回收机器人", "MECHANICAL", 3, 3, 6, 1),
                    M("安保巡游者", "MECHANICAL", 3, 3, 5, 2),
                    M("麦田傀儡", "MECHANICAL", 2, 4, 4, 3, true),
                    M("吵吵模组", "MECHANICAL", 3, 2, 3, 4),
                },
                ShopMinions = new List<MinionData> {
                    M("偏折机器人", "MECHANICAL", 3, 3, 2),
                    M("滑油机器人", "MECHANICAL", 4, 2, 4),
                    M("死神4000型", "MECHANICAL", 6, 6, 7),
                    M("坑道爆破师", "", 3, 4, 3),
                    M("狗狗机器人", "MECHANICAL", 1, 1, 2),
                },
                HandMinions = new List<MinionData> {
                    M("机械蛋", "MECHANICAL", 3, 0, 5),
                    M("伊瑟拉", "DRAGON", 5, 4, 12),
                },
            };
        }

        // ── 场景2: 残血保命 (Turn 13, 低血量, Conserve配速) ──
        private static GameState MakeLowHpConserve()
        {
            return new GameState
            {
                Turn = 13, Gold = 10, MaxGold = 10, TavernTier = 5,
                Health = 8, MaxHealth = 34, Phase = "shop",
                HeroCardId = "BG20_HERO_101",
                HeroName = "凯瑞尔·罗姆",
                BoardMinions = new List<MinionData> {
                    M("死神4000型", "MECHANICAL", 6, 8, 7, 0),
                    M("滑油机器人", "MECHANICAL", 4, 3, 5, 1),
                    M("偏折机器人", "MECHANICAL", 3, 5, 3, 2),
                    M("钴制卫士", "MECHANICAL", 3, 7, 6, 3),
                    M("回收机器人", "MECHANICAL", 3, 4, 8, 4),
                    M("安保巡游者", "MECHANICAL", 3, 4, 5, 5),
                },
                ShopMinions = new List<MinionData> {
                    M("欧米茄破坏者", "MECHANICAL", 6, 6, 6),
                    M("金刚刃牙兽", "MECHANICAL", 4, 3, 3),
                    M("废旧螺栓机甲", "MECHANICAL", 4, 2, 3),
                    M("伊瑟拉", "DRAGON", 5, 4, 12),
                    M("阿格姆·棘咒", "", 5, 4, 6),
                    Spell("发现一个随从", 3, 3),
                },
            };
        }

        // ── 场景3: 升本紧急 (Turn 6, 9费, 升4本正合适) ──
        private static GameState MakeLevelUpUrgent()
        {
            return new GameState
            {
                Turn = 6, Gold = 9, MaxGold = 9, TavernTier = 3,
                Health = 30, MaxHealth = 34, Phase = "shop",
                HeroCardId = "BG20_HERO_101",
                HeroName = "凯瑞尔·罗姆",
                LastUpgradeTurn = 4,
                BoardMinions = new List<MinionData> {
                    M("麦田傀儡", "MECHANICAL", 2, 3, 4, 0),
                    M("钴制卫士", "MECHANICAL", 3, 5, 4, 1),
                    M("偏折机器人", "MECHANICAL", 3, 3, 3, 2),
                    M("回收机器人", "MECHANICAL", 3, 2, 5, 3),
                },
                ShopMinions = new List<MinionData> {
                    M("爆爆机器人", "MECHANICAL", 2, 3, 2),
                    M("龙人军官", "DRAGON", 1, 2, 3),
                    M("微型木乃伊", "MECHANICAL", 1, 2, 3),
                    Spell("刷新", 1, 1),
                },
            };
        }

        // ── 场景4: 卖牌标记 (Turn 10, 场面7张, 最弱随从该卖了) ──
        private static GameState MakeSellCandidate()
        {
            return new GameState
            {
                Turn = 10, Gold = 8, MaxGold = 10, TavernTier = 4,
                Health = 22, MaxHealth = 34, Phase = "shop",
                HeroCardId = "BG20_HERO_101",
                HeroName = "凯瑞尔·罗姆",
                BoardMinions = new List<MinionData> {
                    M("死神4000型", "MECHANICAL", 6, 8, 7, 0),
                    M("滑油机器人", "MECHANICAL", 4, 3, 5, 1),
                    M("偏折机器人", "MECHANICAL", 3, 5, 3, 2),
                    M("钴制卫士", "MECHANICAL", 3, 7, 6, 3),
                    M("微型木乃伊", "MECHANICAL", 1, 2, 3, 4),   // 最弱，建议出售
                    M("回收机器人", "MECHANICAL", 3, 4, 8, 5),
                    M("安保巡游者", "MECHANICAL", 3, 4, 5, 6),
                },
                ShopMinions = new List<MinionData> {
                    M("金刚刃牙兽", "MECHANICAL", 4, 3, 3),
                    M("欧米茄破坏者", "MECHANICAL", 6, 6, 6),
                    M("废旧螺栓机甲", "MECHANICAL", 4, 2, 3),
                    M("坑道爆破师", "", 3, 4, 3),
                },
            };
        }

        // ── 场景5: 满场决策 (Turn 12, 满场, 商店高质量卡) ──
        private static GameState MakeFullBoard()
        {
            return new GameState
            {
                Turn = 12, Gold = 10, MaxGold = 10, TavernTier = 5,
                Health = 18, MaxHealth = 34, Phase = "shop",
                HeroCardId = "BG20_HERO_101",
                HeroName = "凯瑞尔·罗姆",
                BoardMinions = new List<MinionData> {
                    M("死神4000型", "MECHANICAL", 6, 10, 8, 0),
                    M("滑油机器人", "MECHANICAL", 4, 4, 6, 1, true),
                    M("偏折机器人", "MECHANICAL", 3, 6, 4, 2, true),
                    M("钴制卫士", "MECHANICAL", 3, 8, 7, 3),
                    M("欧米茄破坏者", "MECHANICAL", 6, 6, 6, 4),
                    M("金刚刃牙兽", "MECHANICAL", 4, 4, 4, 5),
                    M("回收机器人", "MECHANICAL", 3, 5, 9, 6),
                },
                ShopMinions = new List<MinionData> {
                    M("死神4000型", "MECHANICAL", 6, 6, 7),
                    M("欧米茄破坏者", "MECHANICAL", 6, 8, 8),
                    M("火车王", "", 5, 6, 2),
                    M("瑞文戴尔", "", 5, 1, 7),
                    M("坑道爆破师", "", 3, 4, 3),
                    M("伊瑟拉", "DRAGON", 5, 4, 12),
                },
                HandMinions = new List<MinionData> {
                    M("钴制卫士", "MECHANICAL", 3, 5, 4),
                    M("滑油机器人", "MECHANICAL", 4, 2, 4),
                },
            };
        }
    }
}
