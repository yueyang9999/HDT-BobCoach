using System.Collections.Generic;

namespace BobCoach.Engine
{
    /// <summary>
    /// 面板生命周期状态机 — 替代分散的 TTL 变量和多重条件判断。
    /// 统一控制饰品面板和发现面板的显隐生命周期。
    /// </summary>
    public enum PanelPhase
    {
        /// <summary>无面板（默认）</summary>
        Idle,
        /// <summary>面板显示中</summary>
        Active,
        /// <summary>滞回防闪烁（暂不隐藏，等稳定后再决策）</summary>
        Fading,
        /// <summary>已过期，下次DispatchRender时清除</summary>
        Expired,
    }

    /// <summary>
    /// 单个面板的状态机上下文
    /// </summary>
    public struct PanelState
    {
        public PanelPhase Phase;
        /// <summary>进入当前Phase的时间（DateTime ticks）</summary>
        public long PhaseEnteredTicks;
        /// <summary>面板创建时所在的回合</summary>
        public int CreatedTurn;
        /// <summary>滞回时长（ticks），Phase=Fading时生效</summary>
        public long HysteresisTicks;

        public bool IsVisible => Phase == PanelPhase.Active || Phase == PanelPhase.Fading;

        public PanelState(PanelPhase phase, int turn, long hysteresisMs)
        {
            Phase = phase;
            PhaseEnteredTicks = System.DateTime.UtcNow.Ticks;
            CreatedTurn = turn;
            HysteresisTicks = hysteresisMs * 10000; // ms → ticks
        }

        /// <summary>Transition to a new phase</summary>
        public void Transition(PanelPhase newPhase)
        {
            Phase = newPhase;
            PhaseEnteredTicks = System.DateTime.UtcNow.Ticks;
        }
    }

    /// <summary>
    /// 决策模式：
    ///   Meta — 上分模式，客观卡牌强度评分
    ///   Personal — 个性化模式，融入玩家种族/关键词偏好
    /// </summary>
    public enum DecisionMode
    {
        Meta,
        Personal
    }

    /// <summary>
    /// 游戏情境类型(ContextDetector判定)。
    /// </summary>
    public enum SituationType
    {
        POWER_CURVE,       // 碾压领先: power_ratio≥1.4, HP充足
        STANDARD,          // 标准均势: 0.7≤power_ratio<1.4
        UNDER_PRESSURE,    // 受压追赶: power_ratio<0.7, 血量有压力
        DESPERATE,         // 绝境保命: HP≤10或战力比<0.35
        ECON_TURN          // 经济爆发: 金币充裕, 刷三连/发现
    }

    /// <summary>
    /// 酒馆战棋游戏状态快照，由 GameStateExtractor 填充，供决策引擎消费。
    /// </summary>
    public class GameState
    {
        public bool GameActive;
        public bool IsDuos;
        public string HeroCardId = "";
        public string HeroName = "";
        public string AnomalyId = "";       // 兼容字段: 当前主畸变ID
        public AnomalyContext AnomalyContext = AnomalyContext.Empty;
        public EffectiveGameRules EffectiveRules = EffectiveGameRules.Default;
        public ActiveTrinketContext ActiveTrinketContext = ActiveTrinketContext.Empty;
        public int PlayerId = 1;
        public int Gold;
        public int MaxGold;
        public int TavernTier = 1;
        public int TavernUpgradeCost = -1; // HDT升级按钮实时COST；缺失时为-1
        public int Health = 30;
        public HashSet<string> AvailableTribes = new HashSet<string>(); // 本局可用种族(动态检测)
        public int MaxHealth = 30;
        public int Armor = 0;
        public int Turn = 1;
        public string Phase = "shop";
        public bool FrozenShop;
        public int FreeRefreshCount;
        public int LastUpgradeTurn = 1;  // 升级到当前本的那一回合（用于计算升本折扣）
        public bool FirstPurchaseUsedThisTurn;       // 本回合首张卡牌购买已使用(随从或法术)
        public bool FirstMinionPurchaseUsedThisTurn; // 本回合首个随从购买已使用
        public bool FirstBuyUsedThisTurn             // 兼容旧调用：只映射首个随从状态
        {
            get { return FirstMinionPurchaseUsedThisTurn; }
            set { FirstMinionPurchaseUsedThisTurn = value; }
        }
        public HashSet<string> ClaimedScheduledGrantOccurrences = new HashSet<string>();
        public HashSet<string> ObservedStartResourceExpectations = new HashSet<string>();
        public HashSet<string> MismatchedStartResourceExpectations = new HashSet<string>();
        public List<PurchaseRewardExpectation> PendingPurchaseRewardExpectations =
            new List<PurchaseRewardExpectation>();
        public HashSet<string> ClaimedPurchaseRewardOccurrences = new HashSet<string>();
        public List<TavernUpgradeOccurrence> TavernUpgradeOccurrences =
            new List<TavernUpgradeOccurrence>();
        public List<PrizeDiscoverExpectation> PendingPrizeDiscoverExpectations =
            new List<PrizeDiscoverExpectation>();
        public HashSet<string> ClaimedPrizeDiscoverOccurrences = new HashSet<string>();
        public List<TurnStartCardGrantExpectation> TurnStartCardGrantOccurrences =
            new List<TurnStartCardGrantExpectation>();
        public List<TurnStartCardGrantExpectation> PendingTurnStartCardGrantExpectations =
            new List<TurnStartCardGrantExpectation>();
        public HashSet<string> ClaimedTurnStartCardGrantOccurrences = new HashSet<string>();
        public List<SharedTurnEventExpectation> SharedTurnEventOccurrences =
            new List<SharedTurnEventExpectation>();
        public List<SharedTurnEventExpectation> PendingSharedTurnEvents =
            new List<SharedTurnEventExpectation>();
        public List<SharedTurnEventOutcome> ObservedSharedTurnEventOutcomes =
            new List<SharedTurnEventOutcome>();
        public List<SharedCardVoteOccurrence> SharedCardVoteOccurrences =
            new List<SharedCardVoteOccurrence>();
        public List<SharedCardVoteOccurrence> PendingSharedCardVoteSelections =
            new List<SharedCardVoteOccurrence>();
        public List<SharedCardVoteSelection> ObservedSharedCardVoteSelections =
            new List<SharedCardVoteSelection>();
        public List<SharedCardGrantExpectation> SharedCardGrantExpectations =
            new List<SharedCardGrantExpectation>();
        public List<SharedCardGrantObservation> ObservedLocalSharedCardGrants =
            new List<SharedCardGrantObservation>();
        public List<HeroIdentityExpectation> HeroIdentityExpectations =
            new List<HeroIdentityExpectation>();
        public List<SecondHeroPowerChoiceExpectation> SecondHeroPowerChoiceExpectations =
            new List<SecondHeroPowerChoiceExpectation>();
        public List<SecondHeroPowerChoiceBatchObservation> ObservedSecondHeroPowerChoiceBatches =
            new List<SecondHeroPowerChoiceBatchObservation>();
        public List<SecondHeroPowerChoiceSelection> ObservedSecondHeroPowerChoiceSelections =
            new List<SecondHeroPowerChoiceSelection>();
        public List<SecondHeroPowerEntityObservation> ObservedSecondHeroPowerEntities =
            new List<SecondHeroPowerEntityObservation>();
        public List<SimulatedTeammateGoldTransfer> SimulatedTeammateGoldTransfers =
            new List<SimulatedTeammateGoldTransfer>();
        public List<ObservedTeammateGoldTransfer> ObservedTeammateGoldTransfers =
            new List<ObservedTeammateGoldTransfer>();
        public bool ReplenishingShopActive; // 补牌型酒馆: 辐光护手/时空扭曲物色新人等, UI按实际可见槽位映射

        /// <summary>游戏实际招募位数(基于酒馆等级, 含法术位)</summary>
        public int ShopSlotCount
        {
            get
            {
                if (TavernTier <= 1) return 4;      // T1: 3随从+1法术
                if (TavernTier <= 3) return 5;      // T2-T3: 4随从+1法术
                if (TavernTier <= 5) return 6;      // T4-T5: 5随从+1法术
                return 7;                            // T6: 6随从+1法术
            }
        }

        public List<MinionData> BoardMinions = new List<MinionData>();
        public List<MinionData> HandMinions = new List<MinionData>();
        public List<MinionData> ShopMinions = new List<MinionData>();
        public List<HeroOption> HeroOptions = new List<HeroOption>();
        public List<TrinketOption> TrinketOffer = new List<TrinketOption>();
        public List<TrinketOption> DiscoverOptions = new List<TrinketOption>();
        public bool DiscoverGatePassed = false;  // 本帧发现gate是否通过(用于Power.log路径交叉校验)

        // 面板生命周期状态机由 BobCoachPlugin 持久持有(_trinketPanelState/_discoverPanelState)。
        // GameState 每帧重建, 不能持有跨帧状态 — 这是状态机曾长期失效的根因。
        public int HeroPowerCost = 1;
        public string HeroPowerCardId = "";
        public string HeroPowerType = "Passive";  // Passive/Active/Conditional/Aura
        public bool HeroPowerExhausted;            // 技能已使用/耗尽(EXHAUSTED)
        public bool HasSecondHeroPower;            // 实体检测: 玩家拥有≥2个英雄技能(狼王/米罗克/畸变)
        public int ExhaustedHeroPowerCount;        // 玩家全部技能实体中EXHAUSTED的数量(检测第二技能使用)
        public int HeroPowerUnlockTurn = 1;        // 英雄技能解锁回合(沙德沃克=3, 多数=1)
        public int HeroPowerUnlockTier = 1;        // 英雄技能解锁酒馆等级(米尔菲丝=4, 典狱长=2)

        /// <summary>最大板面槽位: 辛达苟萨被动=6, 其他=7</summary>
        public int MaxBoardSlots
        {
            get
            {
                if (HeroCardId != null && HeroCardId.Contains("HERO_27")) return 6;
                return 7;
            }
        }
        public string HeroPowerSpecial = "";       // 技能特殊规则(如trigger_battlecry_free)
        public bool HeroPowerHasDiscover;          // 技能含\"发现\"机制(伊莉斯/米尔菲丝/阿莱克丝塔萨等)
        public List<HeroPowerState> HeroPowers = new List<HeroPowerState>();
        public List<string> ActiveTrinkets = new List<string>();
        // Node桥接字段
        public int _nodeHandIdx = -1;
        public string _nodeHandReason = "";
        public bool _nodeHeroPower;
        public List<OpponentData> Opponents = new List<OpponentData>();
    }

    public class MinionData
    {
        public string CardId = "";
        public string CardName = "";
        public int EntityId;
        public int Attack;
        public int Health;
        public int Tier;
        public int Position;
        public bool Golden;
        public bool Taunt;
        public bool DivineShield;
        public bool Windfury;
        public bool MegaWindfury;
        public bool Stealth;
        public bool Reborn;
        public bool Poisonous;
        public bool Venomous;
        public bool Cleave;
        public bool Overkill;
        public int AvengeCount;        // 复仇(N), 0=无复仇
        public string CardText = "";  // 卡牌描述文本, 用于顺劈/超杀关键词检测
        public string Tribe = "";  // 单部落或逗号分隔多部落(如"龙,海盗")
        public bool IsSpell;
        public bool GrantsStats;
        public int Cost = -1; // -1=未知；法术>=0为已解析费用，随从购买价由有效规则计算

        /// <summary>部落匹配: 支持逗号分隔多部落字段</summary>
        public static bool TribeMatches(string tribeField, string target)
        {
            if (string.IsNullOrEmpty(tribeField) || string.IsNullOrEmpty(target)) return false;
            if (tribeField == target) return true;
            return System.Array.IndexOf(tribeField.Split(','), target) >= 0;
        }

        /// <summary>枚举Tribe字段中的所有部落(单部落或多部落)</summary>
        public static string[] GetTribesArray(string tribeField)
        {
            if (string.IsNullOrEmpty(tribeField)) return new string[0];
            return tribeField.Split(',');
        }
        public bool IsFrozen; // 休眠/封锁状态(玛维技能等), 当前回合不可使用
    }

    public class HeroOption
    {
        public string CardId = "";
        public string HeroName = "";
        public int EntityId;
    }

    public class TrinketOption
    {
        public string CardId = "";
        public string TrinketName = "";
        public string Description = "";
        public int EntityId;
        public bool IsLesser;
        public int Tier;          // 发现选项: 卡牌星级
        public int Attack;        // 发现选项: 攻击力
        public int Health;        // 发现选项: 生命值
        public List<string> Tribes = new List<string>();
    }

    public class OpponentData
    {
        public int ControllerId;
        public string HeroCardId = "";
        public string HeroName = "";
        public int Health = 30;
        public int TavernTier = 1;
        public bool Alive = true;
        public List<MinionData> BoardMinions = new List<MinionData>();
    }
}
