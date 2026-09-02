using System;
using System.Collections.Generic;

namespace BobCoach.Engine
{
    /// <summary>
    /// 可复现的轻量 PRNG（splitmix64）。同一种子 → 完全相同的序列，
    /// 且与 .NET 版本无关（不依赖 Random 的实现细节）。
    ///
    /// 继承 System.Random 以便无缝替换所有接受 Random 的现有签名
    /// （如 CombatContext 构造函数、CombatEffects 的 ctx.Rng）。
    /// 基类内部状态不会被使用——所有随机输出都走 splitmix64。
    ///
    /// 用途：决策评估管线（MCTS rollout、战斗模拟、卡池采样）的随机源，
    /// 使同一输入状态产出完全一致的推荐（消除 0.6% 级自噪声地板）。
    /// 不同状态通过 StableSeeds 派生不同种子，保持评估多样性。
    /// </summary>
    public sealed class DeterministicRng : Random
    {
        private ulong _state;

        public DeterministicRng(int seed)
            : base(seed)
        {
            // 用种子初始化状态，保证不同种子不同序列
            _state = (ulong)(uint)seed + 0x9E3779B97F4A7C15UL;
        }

        private ulong NextUlong()
        {
            ulong z = (_state += 0x9E3779B97F4A7C15UL);
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        /// <summary>0 .. int.MaxValue（含），语义与 Random.Next() 一致。</summary>
        public override int Next()
        {
            return (int)(NextUlong() & 0x7FFFFFFFUL);
        }

        /// <summary>0 .. max-1（max &lt;= 0 时返回 0），语义与 Random.Next(max) 一致。</summary>
        public override int Next(int max)
        {
            if (max <= 0) return 0;
            return (int)(NextUlong() % (ulong)max);
        }

        /// <summary>[0.0, 1.0)，语义与 Random.NextDouble() 一致。</summary>
        public override double NextDouble()
        {
            return (NextUlong() >> 11) * (1.0 / (1UL << 53));
        }

        protected override double Sample()
        {
            return NextDouble();
        }

        public bool NextBool()
        {
            return (NextUlong() & 1UL) == 1UL;
        }
    }

    /// <summary>
    /// 稳定种子派生：从状态字段组合出确定性种子。
    /// 使用 FNV-1a 风格混合，与 .NET string.GetHashCode 的实现无关
    /// （跨 .NET 版本稳定）。
    /// </summary>
    public static class StableSeeds
    {
        private const uint FnvOffset = 2166136261;
        private const uint FnvPrime = 16777619;

        /// <summary>把若干 int 字段混合为一个种子。</summary>
        public static int Mix(params int[] parts)
        {
            unchecked
            {
                uint hash = FnvOffset;
                if (parts != null)
                {
                    foreach (var part in parts)
                    {
                        hash ^= (uint)part;
                        hash *= FnvPrime;
                        hash ^= hash >> 13;
                        hash *= FnvPrime;
                    }
                }
                return (int)hash;
            }
        }

        /// <summary>字符串的 FNV-1a 哈希（稳定、与 .NET 版本无关）。</summary>
        public static int HashString(string value)
        {
            unchecked
            {
                uint hash = FnvOffset;
                if (value != null)
                {
                    foreach (var ch in value)
                    {
                        hash ^= ch;
                        hash *= FnvPrime;
                    }
                }
                return (int)hash;
            }
        }

        /// <summary>MinionData 列表 → 稳定种子（规模 + 卡牌 id 哈希）。</summary>
        private static int HashBoard(IList<MinionData> board)
        {
            int seed = 0;
            if (board != null)
            {
                seed = Mix(board.Count, HashString("board"));
                foreach (var m in board)
                {
                    if (m == null) continue;
                    seed = Mix(seed, HashString(m.CardId), m.Attack, m.Health, m.Golden ? 1 : 0);
                }
            }
            return seed;
        }

        /// <summary>从 GameState 派生决策评估种子（MCTS 等）。</summary>
        public static int FromGameState(GameState state)
        {
            if (state == null) return Mix(0x4D435453);
            int boardSeed = HashBoard(state.BoardMinions);
            int shopSeed = HashBoard(state.ShopMinions);
            int handSeed = HashBoard(state.HandMinions);
            return Mix(
                0x4D435453,
                state.Turn,
                state.Gold,
                state.LiveGold,
                state.TavernTier,
                state.Health,
                state.Armor,
                state.FreeRefreshCount,
                state.FrozenShop ? 1 : 0,
                state.LastUpgradeTurn,
                state.PlayerId,
                HashString(state.HeroCardId),
                HashString(state.HeroPowerCardId),
                (state.BoardMinions != null ? state.BoardMinions.Count : 0),
                (state.ShopMinions != null ? state.ShopMinions.Count : 0),
                (state.HandMinions != null ? state.HandMinions.Count : 0),
                boardSeed,
                shopSeed,
                handSeed);
        }

        /// <summary>从战斗双方板面派生战斗模拟种子。</summary>
        public static int FromCombat(
            IList<MinionData> playerBoard,
            IList<MinionData> opponentBoard,
            int playerTier,
            int opponentTier,
            int turn,
            string playerHeroCardId,
            string opponentHeroCardId)
        {
            return Mix(
                0x434F4D42,
                turn,
                playerTier,
                opponentTier,
                HashString(playerHeroCardId),
                HashString(opponentHeroCardId),
                HashBoard(playerBoard),
                HashBoard(opponentBoard));
        }
    }
}
