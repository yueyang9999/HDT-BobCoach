using System.Text.RegularExpressions;

namespace BobCoach.Engine
{
    /// <summary>
    /// “能使”系列规则：识别能生成属性值酒馆法术的卡，以及能让这类法术/元素额外加属性的卡。
    /// 这类卡通常以“能使随从获得属性值的酒馆法术/元素”为共同文本锚点。
    /// </summary>
    public static class StatGiftRuleEvaluator
    {
        private const string GeneratorAnchor =
            "能使随从获得属性值的酒馆法术";
        private const string ElementAnchor =
            "能使随从获得属性值的元素";
        private const string AmplifierAnchor =
            "额外获得";

        public static bool IsStatGiftGeneratorText(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            return text.Contains(GeneratorAnchor)
                || text.Contains(ElementAnchor)
                || text.Contains("随机获取一张能使随从获得属性值的酒馆法术牌");
        }

        public static bool IsStatGiftAmplifierText(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            if (text.Contains("你的能使随从获得属性值的")
                && text.Contains(AmplifierAnchor))
                return true;
            if (text.Contains("酒馆法术使随从额外获得")
                || text.Contains("酒馆法术在本局对战中使随从额外获得")
                || text.Contains("能使随从获得属性值的元素额外使随从获得"))
                return true;
            return false;
        }

        public static bool TryGetAmplifierBonus(
            string text,
            out int attackBonus,
            out int healthBonus)
        {
            attackBonus = 0;
            healthBonus = 0;
            if (string.IsNullOrEmpty(text)) return false;

            var both = Regex.Match(
                text,
                @"额外(?:使随从获得|获得)\+(\d+)/\+(\d+)");
            if (both.Success
                && int.TryParse(both.Groups[1].Value, out attackBonus)
                && int.TryParse(both.Groups[2].Value, out healthBonus))
                return true;

            var atk = Regex.Match(
                text,
                @"额外获得\+(\d+)攻击力");
            var hp = Regex.Match(
                text,
                @"额外获得\+(\d+)生命值");
            if (atk.Success && int.TryParse(atk.Groups[1].Value, out attackBonus))
            {
                healthBonus = 0;
                return true;
            }
            if (hp.Success && int.TryParse(hp.Groups[1].Value, out healthBonus))
            {
                attackBonus = 0;
                return true;
            }
            return false;
        }

        public static double GetAmplifierStrength(string text)
        {
            int attackBonus;
            int healthBonus;
            if (TryGetAmplifierBonus(text, out attackBonus, out healthBonus))
                return 1.0 + (attackBonus + healthBonus) * 0.15;
            return 1.0;
        }
    }
}
