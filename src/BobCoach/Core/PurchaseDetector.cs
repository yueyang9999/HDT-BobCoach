using System;
using System.Collections.Generic;
using System.Linq;

namespace BobCoach.Engine
{
    public enum ShopChangeType { None, Purchased, Refreshed }

    /// <summary>
    /// 检测购买/刷新事件，为平滑重绘提供触发器。
    /// 通过对比上一帧商店实体数量和ID，推断玩家操作类型。
    /// </summary>
    public class PurchaseDetector
    {
        private int _lastShopCount;
        private List<int> _lastShopIds = new List<int>();

        public ShopChangeType DetectChange(int currentCount, List<int> currentIds)
        {
            if (currentIds == null) currentIds = new List<int>();

            var previousIds = new HashSet<int>(_lastShopIds);
            bool hasNewEntity = currentIds.Any(id => !previousIds.Contains(id));
            bool sameEntities = currentIds.Count == _lastShopIds.Count
                && currentIds.All(previousIds.Contains);

            // 数量减少 → 购买了卡牌
            // A purchase is a strict subset of the previous shop. A partial refresh can also
            // have fewer entities, so newly observed IDs must take precedence over the count.
            if (currentCount < _lastShopCount && !hasNewEntity)
            {
                _lastShopCount = currentCount;
                _lastShopIds = new List<int>(currentIds);
                return ShopChangeType.Purchased;
            }

            // ID 全变 → 刷新了
            if (currentIds.Count > 0 && !sameEntities)
            {
                _lastShopCount = currentCount;
                _lastShopIds = new List<int>(currentIds);
                return ShopChangeType.Refreshed;
            }

            return ShopChangeType.None;
        }

        public void Reset()
        {
            _lastShopCount = 0;
            _lastShopIds.Clear();
        }

        public void Snapshot(int count, List<int> ids)
        {
            _lastShopCount = count;
            _lastShopIds = ids != null ? new List<int>(ids) : new List<int>();
        }
    }
}
