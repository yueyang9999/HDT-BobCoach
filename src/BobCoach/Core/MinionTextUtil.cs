namespace BobCoach.Engine
{
    /// <summary>
    /// 随从文本关键词判定工具。
    /// 1.2.16-fix I-20260907-19(沙德沃克 105334, rank3): HP_022 奇诡尖啸需"友方战吼随从"
    /// 目标, 通用 hasValidTarget 只查板面/商店非空会把无战吼随从的局面误判可施放。
    /// CardText 来源: GameStateExtractor.EntityToMinion(与顺劈/超杀检测同源)。
    /// 纯函数(无 MinionData 依赖), 便于独立单测; 集合遍历由调用方(BobCoachPlugin)完成。
    /// </summary>
    public static class MinionTextUtil
    {
        /// <summary>单卡文本是否含"战吼/Battlecry"关键词。</summary>
        public static bool TextHasBattlecry(string cardText)
        {
            if (string.IsNullOrEmpty(cardText)) return false;
            return cardText.IndexOf("战吼", System.StringComparison.Ordinal) >= 0
                || cardText.IndexOf("Battlecry", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
