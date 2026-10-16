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

        /// <summary>A logistics fleet: cargo convoys and no warships or tenders.</summary>
        public bool IsConvoyOnly => CargoConvoys > 0 && LineShips == 0 && EwShips == 0 && SupplyTenders == 0;

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

        /// <summary>Removes lost ships of one class (combat only).</summary>
        /// <summary>New ships join the fleet (shipyard deliveries).</summary>
        public void Gain(TargetFocusClass shipClass, int ships)
        {
            if (ships < 0 || ships > int.MaxValue - Count(shipClass)) throw new System.ArgumentOutOfRangeException(nameof(ships));
            switch (shipClass)
            {
                case TargetFocusClass.LineShips: LineShips += ships; break;
                case TargetFocusClass.EwShips: EwShips += ships; break;
                case TargetFocusClass.SupplyTenders: SupplyTenders += ships; break;
                default: CargoConvoys += ships; break;
            }
        }

        public void Lose(TargetFocusClass shipClass, int ships)
        {
            if (ships < 0 || ships > Count(shipClass)) throw new System.ArgumentOutOfRangeException(nameof(ships));
            switch (shipClass)
            {
                case TargetFocusClass.LineShips: LineShips -= ships; break;
                case TargetFocusClass.EwShips: EwShips -= ships; break;
                case TargetFocusClass.SupplyTenders: SupplyTenders -= ships; break;
                default: CargoConvoys -= ships; break;
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

    /// <summary>
    /// ECS component: the lanes a fleet still has to take after its current one (blittable fixed buffers of lane
    /// index and generation). Owned by <see cref="FleetMovementSystem"/>.
    /// </summary>
    public unsafe struct FleetRoute
    {
        public const int MaxLanes = 32;

        private fixed int _laneIndex[MaxLanes];
        private fixed int _laneGeneration[MaxLanes];
        public int Count;
        public int Next; // the lane to depart on at the next site

        public bool HasNext => Next < Count;

        public EntityId LaneAt(int i)
        {
            if (i < 0 || i >= Count) throw new System.ArgumentOutOfRangeException(nameof(i));
            return new EntityId(_laneIndex[i], _laneGeneration[i]);
        }

        public void Append(EntityId lane)
        {
            if (Count == MaxLanes) throw new System.InvalidOperationException("Route is full.");
            _laneIndex[Count] = lane.Index;
            _laneGeneration[Count] = lane.Generation;
            Count++;
        }

        public void Clear()
        {
            Count = 0;
            Next = 0;
        }
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

    /// <summary>
    /// ECS component: salvo combat state (GDD §2.4 C). Supply debts accumulate milli-units until a whole item is due;
    /// <see cref="DamageCarry"/> holds damage (hundredths) not yet enough to destroy a ship.
    /// </summary>
    public struct FleetCombatState
    {
        public TargetFocusClass Focus;
        public RofStance Rof;
        public int TemperatureMilli;
        public bool IsOverheated; // guns locked, ammo cooking off
        public bool IsVenting;
        public long EngagedUntilTick; // pinned on its lane while engaged
        public long FiredAtTick;
        public long DamageCarry;
        public int AmmoDebtMilli;
        public int BatteryDebtMilli;
        public int CoolantDebtMilli;
        public int FuelDebtMilli;
    }

    /// <summary>ECS component: what enemy sensors know about a fleet.</summary>
    public struct FleetSensors
    {
        public long RevealedUntilTick;
        public long RwrWarningTick; // tick a hostile focused scan's RWR alarm fired; 0 = none
    }
}
