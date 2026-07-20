using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace BobCoach.Engine
{
    internal interface ICardClassificationSource
    {
        bool TryGet(string cardId, out CardClassifier.CardClassification classification);
    }

    /// <summary>
    /// 机制驱动卡牌分类器：从 mechanics + 文本语义理解卡牌功能，无需硬编码 CardId。
    /// 新版本来临时，只要 mechanic 关键词和文本模式不变，分类自动生效。
    /// </summary>
    public class CardClassifier
    {
#if BOBCOACH_LEGACY_CLASSIFICATION_GENERATOR
        private Dictionary<string, CardSemanticsData> _semantics;
        private Dictionary<string, string> _cardTexts;      // CardId → text_cn
        private Dictionary<string, List<string>> _cardMechanics; // CardId → mechanics list
        private Dictionary<string, CardClassification> _cache;
#endif
        private readonly ICardClassificationSource _source;

        public CardClassifier() { }

        internal CardClassifier(ICardClassificationSource source)
        {
            _source = source;
        }

#if BOBCOACH_LEGACY_CLASSIFICATION_GENERATOR
        private sealed class ClassificationRegistryRow
        {
            public string cardId { get; set; }
            public string primaryRole { get; set; }
            public List<string> allRoles { get; set; }
            public bool isCoreCombo { get; set; }
            public bool requiresPartner { get; set; }
            public string partnerMechanic { get; set; }
            public float economyValue { get; set; }
            public float combatValue { get; set; }
            public float growthValue { get; set; }
        }
#endif

#if BOBCOACH_LEGACY_CLASSIFICATION_GENERATOR
        public void SetSemantics(Dictionary<string, CardSemanticsData> semantics)
        {
            _semantics = semantics;
        }
#endif

#if BOBCOACH_LEGACY_CLASSIFICATION_GENERATOR
        public bool LoadClassificationRegistry(string json)
        {
            var empty = new Dictionary<string, CardClassification>(StringComparer.Ordinal);
            if (string.IsNullOrWhiteSpace(json))
            {
                _cache = empty;
                return false;
            }

            try
            {
                var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                var rows = serializer.Deserialize<List<ClassificationRegistryRow>>(json);
                if (rows == null || rows.Count != 1265)
                {
                    _cache = empty;
                    return false;
                }

                var next = new Dictionary<string, CardClassification>(StringComparer.Ordinal);
                foreach (var row in rows)
                {
                    if (row == null || string.IsNullOrEmpty(row.cardId)
                        || next.ContainsKey(row.cardId))
                        throw new InvalidDataException("invalid or duplicate classification CardId");

                    CardRole primaryRole;
                    if (!Enum.TryParse(row.primaryRole, false, out primaryRole)
                        || !Enum.IsDefined(typeof(CardRole), primaryRole))
                        throw new InvalidDataException("unknown primary role on " + row.cardId);

                    var roles = new List<CardRole>();
                    var roleNames = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var roleName in row.allRoles ?? new List<string>())
                    {
                        CardRole role;
                        if (string.IsNullOrEmpty(roleName) || !roleNames.Add(roleName)
                            || !Enum.TryParse(roleName, false, out role)
                            || !Enum.IsDefined(typeof(CardRole), role))
                            throw new InvalidDataException("invalid role on " + row.cardId);
                        roles.Add(role);
                    }

                    if (float.IsNaN(row.economyValue) || float.IsInfinity(row.economyValue)
                        || float.IsNaN(row.combatValue) || float.IsInfinity(row.combatValue)
                        || float.IsNaN(row.growthValue) || float.IsInfinity(row.growthValue))
                        throw new InvalidDataException("invalid classification value on " + row.cardId);

                    next.Add(row.cardId, new CardClassification
                    {
                        PrimaryRole = primaryRole,
                        AllRoles = roles,
                        IsCoreCombo = row.isCoreCombo,
                        RequiresPartner = row.requiresPartner,
                        PartnerMechanic = row.partnerMechanic ?? "",
                        EconomyValue = row.economyValue,
                        CombatValue = row.combatValue,
                        GrowthValue = row.growthValue,
                    });
                }

                _cache = next;
                _cardTexts = null;
                _cardMechanics = null;
                return true;
            }
            catch (Exception ex)
            {
                _cache = empty;
                ClassifierLog("CardClassifier.LoadClassificationRegistry failed: " + ex.Message);
                return false;
            }
        }

        /// <summary>从 cards.json 加载文本和 mechanics 用于分类</summary>
        public void LoadCardData(string cardsJson)
        {
            if (string.IsNullOrEmpty(cardsJson)) return;
            _cardTexts = new Dictionary<string, string>();
            _cardMechanics = new Dictionary<string, List<string>>();
            _cache = new Dictionary<string, CardClassification>();

            try
            {
                int pos = 0;
                while (pos < cardsJson.Length)
                {
                    var idStart = cardsJson.IndexOf("\"str_id\"", pos);
                    if (idStart < 0) break;
                    idStart = cardsJson.IndexOf('"', idStart + 9) + 1;
                    var idEnd = cardsJson.IndexOf('"', idStart);
                    if (idStart <= 0 || idEnd < 0) break;
                    var cardId = cardsJson.Substring(idStart, idEnd - idStart);

                    // Extract text_cn
                    string textCn = "";
                    var textStart = cardsJson.IndexOf("\"text_cn\"", idEnd);
                    if (textStart >= 0)
                    {
                        textStart = cardsJson.IndexOf('"', textStart + 10) + 1;
                        var textEnd = cardsJson.IndexOf('"', textStart);
                        if (textStart > 0 && textEnd > textStart)
                            textCn = cardsJson.Substring(textStart, textEnd - textStart);
                    }
                    _cardTexts[cardId] = textCn;

                    // Extract mechanics array
                    var mechanics = new List<string>();
                    var mechStart = cardsJson.IndexOf("\"mechanics\"", idEnd);
                    if (mechStart >= 0 && mechStart < textStart)
                    {
                        var bracketStart = cardsJson.IndexOf('[', mechStart);
                        var bracketEnd = cardsJson.IndexOf(']', bracketStart);
                        if (bracketStart >= 0 && bracketEnd > bracketStart)
                        {
                            var mechStr = cardsJson.Substring(bracketStart + 1, bracketEnd - bracketStart - 1);
                            foreach (var part in mechStr.Split(','))
                            {
                                var cleaned = part.Trim().Trim('"').Trim();
                                if (!string.IsNullOrEmpty(cleaned))
                                    mechanics.Add(cleaned);
                            }
                        }
                    }
                    _cardMechanics[cardId] = mechanics;

                    pos = idEnd + 1;
                }
            }
            catch (Exception ex) { ClassifierLog("CardClassifier.LoadCardData failed: " + ex.Message); }

            // 预分类所有卡牌
            foreach (var kv in _cardTexts)
            {
                List<string> mechs;
                _cardMechanics.TryGetValue(kv.Key, out mechs);
                _cache[kv.Key] = Classify(kv.Key, kv.Value, mechs);
            }
        }
#endif

        /// <summary>判断是否为经济卡（无需硬编码CardId）</summary>
        public bool IsEconomyCard(string cardId)
        {
            var classification = GetClassification(cardId);
            return classification.HasValue && classification.Value.EconomyValue >= 0.5f;
        }

#if BOBCOACH_LEGACY_CLASSIFICATION_GENERATOR
        /// <summary>判断是否需要配合卡（combo组件，单拍价值低）</summary>
        public bool IsComboPieceNeedingPartner(string cardId)
        {
            if (string.IsNullOrEmpty(cardId) || _cache == null) return false;
            CardClassification c;
            return _cache.TryGetValue(cardId, out c) && c.RequiresPartner;
        }
#endif

        /// <summary>判断是否为放大器（铜须/附魔师类）</summary>
        public bool IsAmplifier(string cardId)
        {
            var classification = GetClassification(cardId);
            return classification.HasValue && classification.Value.IsCoreCombo;
        }

        /// <summary>获取卡牌分类</summary>
        public CardClassification? GetClassification(string cardId)
        {
            if (string.IsNullOrEmpty(cardId)) return null;
            if (_source != null)
            {
                CardClassification derived;
                return _source.TryGet(cardId, out derived)
                    ? (CardClassification?)derived : null;
            }
#if BOBCOACH_LEGACY_CLASSIFICATION_GENERATOR
            if (_cache == null) return null;
            CardClassification c;
            return _cache.TryGetValue(cardId, out c) ? (CardClassification?)c : null;
#else
            return null;
#endif
        }

        // ── 分类枚举 ──

        public enum CardRole
        {
            Unknown,
            Economy,        // 提供铸币/经济
            Amplifier,      // 放大器（加倍触发/额外触发）
            SpellSource,    // 法术来源（获取/发现法术）
            SpellCopier,    // 法术复制（获取上一个法术的复制）
            Battlecry,      // 战吼
            Deathrattle,    // 亡语
            EndOfTurn,      // 回合结束效果
            StartOfCombat,  // 战斗开始时
            Poisonous,      // 剧毒/烈毒
            DivineShield,   // 圣盾
            Reborn,         // 复生
            Windfury,       // 风怒
            Taunt,          // 嘲讽
            Charge,         // 进击 (攻击时触发效果或立即攻击)
            Avenge,         // 复仇 (友方随从死亡N次后触发)
            Magnetic,       // 磁力 (可吸附到机械上)
            Summon,         // 召唤 (战斗中召唤其他随从)
            Scaling,        // 成长型（永久增属性）
            ValueGenerator, // 资源生成（发现/获取卡牌）
        }

        public struct CardClassification
        {
            public CardRole PrimaryRole;
#if BOBCOACH_LEGACY_CLASSIFICATION_GENERATOR || BOBCOACH_CLASSIFICATION_AUDIT
            public List<CardRole> AllRoles;
            public bool RequiresPartner;  // 需要配合卡才能发挥价值
            public string PartnerMechanic; // 需要的配合机制（如 "BATTLECRY" → 铜须）
#endif
            public bool IsCoreCombo;      // 是否为强力 combo 引擎（如铜须/附魔师）
            public float EconomyValue;    // 经济价值（铸币等价）
            public float CombatValue;     // 即时战力值
            public float GrowthValue;     // 成长潜力
        }

        // ── 文本模式（中文关键词匹配，跨版本通用）──

        private static readonly (string Pattern, CardRole Role, float Weight)[] TextPatterns = new[]
        {
            // Economy
            (@"出售得.*\d+.*铸币", CardRole.Economy, 1.0f),
            (@"出售.*得.*铸币", CardRole.Economy, 0.8f),
            (@"获得.*铸币", CardRole.Economy, 0.6f),
            (@"铸币.*上限.*提高", CardRole.Economy, 0.5f),
            (@"刷新.*免费|免费.*刷新", CardRole.Economy, 0.5f),
            (@"消耗生命值.*而非铸币", CardRole.Economy, 0.4f),

            // Amplifier (放大器: 触发N次=不叠加, 额外触发=可叠加, 均归为Amplifier)
            (@"额外触发", CardRole.Amplifier, 1.0f),    // "额外触发" → 可叠加放大器(如瑞文)
            (@"触发两次", CardRole.Amplifier, 1.0f),    // "触发两次" → 不叠加放大器(如铜须/达卡莱)
            (@"施放两次", CardRole.Amplifier, 1.0f),    // "施放两次" → 不叠加放大器(如巴琳达/私掠者)
            (@"触发.*次", CardRole.Amplifier, 0.7f),
            (@"会额外施放", CardRole.Amplifier, 0.8f),

            // Spell source/copier
            (@"获取.*法术.*复制|获取.*上一个.*法术", CardRole.SpellCopier, 1.0f),
            (@"复制.*法术|法术.*复制", CardRole.SpellCopier, 0.8f),
            (@"获取.*法术|发现.*法术|随机获取.*法术", CardRole.SpellSource, 0.7f),

            // Poisonous & Venomous (区分: 剧毒攻防都有,烈毒仅攻击)
            (@"剧毒", CardRole.Poisonous, 1.0f),
            (@"烈毒", CardRole.Poisonous, 0.8f),

            // Scaling growth
            (@"永久.*获得|永久.*提升|在本局对战中.*获得", CardRole.Scaling, 0.8f),
            (@"每当你.*获得.*\+|每当你.*使.*获得.*\+", CardRole.Scaling, 0.7f),
            (@"回合结束时.*获得.*\+", CardRole.Scaling, 0.5f),

            // Value generator
            (@"发现.*随从|获取.*随从牌|随机获取.*随从", CardRole.ValueGenerator, 0.8f),
            (@"获取一张|随机获取一张|发现一张", CardRole.ValueGenerator, 0.6f),

            // Charge (进击: 攻击时触发效果/立即攻击/风怒相关)
            (@"进击", CardRole.Charge, 1.0f),
            (@"攻击时.*触发|攻击时.*获得", CardRole.Charge, 0.7f),

            // Avenge (复仇: N个友方随从死亡后触发)
            (@"复仇", CardRole.Avenge, 1.0f),
            (@"复仇\s*\(\s*\d+\s*\)", CardRole.Avenge, 1.0f),

            // Magnetic (磁力: 可吸附到机械)
            (@"磁力", CardRole.Magnetic, 1.0f),
            (@"可吸附|磁力吸附", CardRole.Magnetic, 0.8f),

            // Summon (召唤: 战斗中生成随从, 早期场面铺展+理财价值)
            (@"召唤.*个.*随从|召唤一只|召唤一个|召唤两个|召唤三个", CardRole.Summon, 0.95f),
            (@"战吼.*召唤|亡语.*召唤|战斗.*召唤|出售.*召唤", CardRole.Summon, 0.85f),
        };

        // ── 公开方法 ──

        /// <summary>根据卡牌ID和文本分类（需要外部传入文本和mechanics）</summary>
        public CardClassification Classify(string cardId, string textCn, List<string> mechanics)
        {
            var result = new CardClassification
            {
#if BOBCOACH_LEGACY_CLASSIFICATION_GENERATOR || BOBCOACH_CLASSIFICATION_AUDIT
                AllRoles = new List<CardRole>(),
                RequiresPartner = false,
#endif
                IsCoreCombo = false,
                EconomyValue = 0f,
                CombatValue = 0.3f,
                GrowthValue = 0.1f,
            };

            if (string.IsNullOrEmpty(cardId)) return result;

            var roles = new List<(CardRole Role, float Confidence)>();

            // 1. 从文本模式匹配
            if (!string.IsNullOrEmpty(textCn))
            {
                foreach (var (pattern, role, weight) in TextPatterns)
                {
                    if (Regex.IsMatch(textCn, pattern))
                    {
                        roles.Add((role, weight));
                    }
                }
            }

            // 2. 从 mechanics 关键词匹配
            if (mechanics != null)
            {
                foreach (var mech in mechanics)
                {
                    switch (mech)
                    {
                        case "BATTLECRY": roles.Add((CardRole.Battlecry, 1.0f)); break;
                        case "DEATHRATTLE": roles.Add((CardRole.Deathrattle, 1.0f)); break;
                        case "END_OF_TURN": roles.Add((CardRole.EndOfTurn, 0.8f)); break;
                        case "DIVINE_SHIELD": roles.Add((CardRole.DivineShield, 1.0f)); break;
                        case "REBORN": roles.Add((CardRole.Reborn, 1.0f)); break;
                        case "WINDFURY": roles.Add((CardRole.Windfury, 0.8f)); break;
                        case "TAUNT": roles.Add((CardRole.Taunt, 0.5f)); break;
                        case "VENOMOUS":
                        case "POISONOUS": roles.Add((CardRole.Poisonous, 1.0f)); break;
                        case "CHARGE":
                        case "RUSH": roles.Add((CardRole.Charge, 1.0f)); break;
                        case "AVENGE": roles.Add((CardRole.Avenge, 1.0f)); break;
                        case "MAGNETIC":
                        case "MODULAR": roles.Add((CardRole.Magnetic, 1.0f)); break;
                        case "SUMMON":
                        case "DEATHRATTLE_SUMMON": roles.Add((CardRole.Summon, 0.9f)); break;
                    }
                }
            }

#if BOBCOACH_LEGACY_CLASSIFICATION_GENERATOR
            // 3. 仅开发生成链：从旧语义数据库补充并重现5B.3C签名。
            if (_semantics != null && _semantics.TryGetValue(cardId, out var sem))
            {
                if (sem.Mechanics != null)
                {
                    foreach (var m in sem.Mechanics)
                    {
                        if (m == "BATTLECRY") roles.Add((CardRole.Battlecry, 1.0f));
                        if (m == "DEATHRATTLE") roles.Add((CardRole.Deathrattle, 1.0f));
                        if (m == "END_OF_TURN" || m == "TRIGGER_VISUAL") roles.Add((CardRole.EndOfTurn, 0.8f));
                    }
                }

                // Amplifier detection: 仅已知放大器机制才设 IsCoreCombo
                // 非放大器机制(如DIVINE_SHIELD_REFRESH)不应误判
                if (!string.IsNullOrEmpty(sem.ProvidesMechanic))
                {
                    var amplifierMechanics = new HashSet<string>
                    {
                        "BATTLECRY", "TRIGGER_BATTLECRY_EXTRA",
                        "DEATHRATTLE", "TRIGGER_DEATHRATTLE_EXTRA",
                        "END_OF_TURN", "TRIGGER_END_OF_TURN_EXTRA",
                        "SUMMON_EXTRA", "COPY_DEATHRATTLE",
                        "CAST_SPELL_EXTRA", "TRIGGER_CAST_SPELL_EXTRA",
                    };
                    if (amplifierMechanics.Contains(sem.ProvidesMechanic))
                    {
                        result.IsCoreCombo = true;
                        roles.Add((CardRole.Amplifier, 1.0f));
                        result.PartnerMechanic = sem.ProvidesMechanic;
                    }
                }

                // Combo需求检测: 如果此卡需要某种放大器
                if (sem.Combos != null && sem.Combos.Count > 0)
                {
                    result.RequiresPartner = true;
                    foreach (var (mech, _) in sem.Combos)
                    {
                        if (!string.IsNullOrEmpty(mech))
                            result.PartnerMechanic = mech;
                    }
                }
            }
#endif

            // 4. 合并结果
            if (roles.Count > 0)
            {
                var best = roles.OrderByDescending(r => r.Confidence).First();
                result.PrimaryRole = best.Role;
#if BOBCOACH_LEGACY_CLASSIFICATION_GENERATOR || BOBCOACH_CLASSIFICATION_AUDIT
                result.AllRoles = roles.Select(r => r.Role).Distinct().ToList();
#endif
            }
            else
            {
                result.PrimaryRole = CardRole.Unknown;
            }

            // 5. 文本高置信放大器兜底: 语义数据未覆盖时仍设IsCoreCombo
            if (!result.IsCoreCombo && roles.Any(r => r.Role == CardRole.Amplifier && r.Confidence >= 0.9f))
            {
                result.IsCoreCombo = true;
            }

            // 6. 计算数值
            result.EconomyValue = roles.Where(r => r.Role == CardRole.Economy).Sum(r => r.Confidence * 0.5f);
            result.CombatValue = CalculateCombatValue(
                roles.Select(item => item.Role).Distinct());
            result.GrowthValue = roles.Where(r => r.Role == CardRole.Scaling).Sum(r => r.Confidence * 0.3f)
                + (result.IsCoreCombo ? 0.4f : 0f);

            return result;
        }

        private float CalculateCombatValue(IEnumerable<CardRole> roles)
        {
            var allRoles = new HashSet<CardRole>(roles);
            float v = 0.3f;
            if (allRoles.Contains(CardRole.Poisonous)) v += 0.5f;       // 剧毒无视身材
            if (allRoles.Contains(CardRole.DivineShield)) v += 0.3f;   // 圣盾=多一条命
            if (allRoles.Contains(CardRole.Reborn)) v += 0.35f;        // 复生≈多一条命
            if (allRoles.Contains(CardRole.Windfury)) v += 0.2f;        // 风怒=额外攻击
            if (allRoles.Contains(CardRole.Taunt)) v += 0.1f;           // 嘲讽=保护后排
            if (allRoles.Contains(CardRole.Charge)) v += 0.15f;         // 进击=先手优势
            if (allRoles.Contains(CardRole.Magnetic)) v += 0.1f;        // 磁力=可吸附增效
            return Math.Min(1.5f, v);
        }

        private static void ClassifierLog(string msg)
        {
            try
            {
                var dir = BobCoachDataPaths.Root;
                Directory.CreateDirectory(dir);
                var line = string.Format("[{0:O}] [CardClassifier] {1}\n", DateTime.UtcNow, msg);
                File.AppendAllText(Path.Combine(dir, "bob_coach.log"), line, System.Text.Encoding.UTF8);
            }
            catch { }
        }
    }
}
