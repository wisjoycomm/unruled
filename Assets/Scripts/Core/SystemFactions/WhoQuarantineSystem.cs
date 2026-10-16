using System;
using Unruled.Core.Ecs;
using Unruled.Core.Economy;
using Unruled.Core.Simulation;

namespace Unruled.Core.SystemFactions
{
    /// <summary>
    /// WHO watch (GDD §2.2 A.4): when a site's RAW_WATER or ITEM_OXYGEN stays at 0 for more than the starvation
    /// window, exports are locked for the quarantine period. A site still starving when it ends is locked again.
    /// </summary>
    public sealed class WhoQuarantineSystem : ISimSystem
    {
        private readonly ComponentStore<SiteQuarantine> _quarantines;
        private readonly ComponentStore<Stockpile> _stockpiles;
        private readonly SystemFactionBalance _balance;

        public WhoQuarantineSystem(
            ComponentStore<SiteQuarantine> quarantines, ComponentStore<Stockpile> stockpiles, SystemFactionBalance balance)
        {
            _quarantines = quarantines ?? throw new ArgumentNullException(nameof(quarantines));
            _stockpiles = stockpiles ?? throw new ArgumentNullException(nameof(stockpiles));
            _balance = balance ?? throw new ArgumentNullException(nameof(balance));
        }

        public void OnTick(long tick)
        {
            for (int i = 0; i < _quarantines.Count; i++)
            {
                ref var quarantine = ref _quarantines.At(i);
                ref var stock = ref _stockpiles.Get(_quarantines.EntityAt(i));

                bool starved = stock.Get(ItemId.RawWater) == 0 || stock.Get(ItemId.ItemOxygen) == 0;
                if (!starved)
                {
                    quarantine.StarvedSinceTick = 0;
                    continue;
                }

                if (quarantine.StarvedSinceTick == 0)
                {
                    quarantine.StarvedSinceTick = tick;
                    continue;
                }

                if (tick - quarantine.StarvedSinceTick > _balance.WhoStarvationTicks && !quarantine.IsExportLocked(tick))
                {
                    quarantine.QuarantineUntilTick = tick + _balance.WhoQuarantineTicks;
                    quarantine.StarvedSinceTick = tick;
                }
            }
        }
    }
}
