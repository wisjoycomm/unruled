using Unruled.Core.Ecs;

namespace Unruled.Core.Military
{
    /// <summary>Ship class a fleet's salvos aim at (GDD §3).</summary>
    public enum TargetFocusClass : byte
    {
        LineShips = 0,
        EwShips = 1,
        SupplyTenders = 2,
        CargoConvoys = 3
    }

    /// <summary>Rate-of-fire stance (GDD §3): 50%, 100% or 150%.</summary>
    public enum RofStance : byte
    {
        Conservation50 = 0,
        Standard100 = 1,
        Overdrive150 = 2
    }

    /// <summary>
    /// ECS component: who owns a fleet, its tech tier and how many ships of each class it has (GDD §3 FleetUnit).
    /// A fleet entity also carries a <see cref="Economy.Stockpile"/> as its cargo hold.
    /// </summary>
    public struct FleetUnit
    {
        public int OwnerPlayerId;
        public byte TechTier; // 1..5
        public int LineShips;
        public int EwShips;
        public int SupplyTenders;
        public int CargoConvoys;

        public int TotalShips => LineShips + EwShips + SupplyTenders + CargoConvoys;

        public int Count(TargetFocusClass shipClass)
        {
            switch (shipClass)
            {
                case TargetFocusClass.LineShips: return LineShips;
                case TargetFocusClass.EwShips: return EwShips;
                case TargetFocusClass.SupplyTenders: return SupplyTenders;
                default: return CargoConvoys;
            }
        }
    }

    /// <summary>
    /// ECS component: where a fleet is. Exactly one of <see cref="Site"/> and <see cref="Lane"/> is set. On a lane the
    /// fleet travels away from <see cref="FromSite"/>; <see cref="TravelledMilli"/> counts milli map units along it.
    /// </summary>
    public struct FleetMotion
    {
        public EntityId Site;
        public EntityId Lane;
        public EntityId FromSite;
        public long TravelledMilli;
        public EntityId ArrivalLane; // lane the fleet last arrived through; null if it started at its site
        public bool IsHolding;
        public bool IsReversing;
    }

    /// <summary>ECS component: mothball and upkeep state of a fleet (tab 03).</summary>
    public struct FleetReadiness
    {
        public bool IsMothballed;
        public long WarmupUntilTick; // reactors restarting after a mothball
        public bool IsUnsupplied; // the last upkeep was not paid

        /// <summary>Whether the fleet may move or fire on this tick.</summary>
        public bool CanAct(long tick) => !IsMothballed && !IsUnsupplied && tick >= WarmupUntilTick;
    }

    /// <summary>ECS component: what enemy sensors know about a fleet.</summary>
    public struct FleetSensors
    {
        public long RevealedUntilTick;
        public long RwrWarningTick; // tick a hostile focused scan's RWR alarm fired; 0 = none
    }
}
