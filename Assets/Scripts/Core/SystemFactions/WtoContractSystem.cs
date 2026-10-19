using System;
using Unruled.Core.Board;
using Unruled.Core.Ecs;
using Unruled.Core.Economy;
using Unruled.Core.Military;
using Unruled.Core.Simulation;

namespace Unruled.Core.SystemFactions
{
    /// <summary>ECS component: a WTO contract between a matched buyer and seller, on its own entity.</summary>
    public struct WtoContract
    {
        public int BuyerPlayerId;
        public int SellerPlayerId;
        public ItemId Item;
        public int Quantity;
        public long PriceCents;
        public long DepositCents;
        public EntityId BuyerSite;
        public EntityId Convoy;
        public long DeadlineTick;
    }

    /// <summary>WTO contract numbers. PROVISIONAL: the GDD names no delivery deadline.</summary>
    public sealed class WtoContractBalance
    {
        public readonly int DeadlineTravelPercent; // of the planned travel time
        public readonly int DeadlineGraceTicks;

        public WtoContractBalance(int deadlineTravelPercent, int deadlineGraceTicks)
        {
            if (deadlineTravelPercent < 100) throw new ArgumentOutOfRangeException(nameof(deadlineTravelPercent));
            if (deadlineGraceTicks < 0) throw new ArgumentOutOfRangeException(nameof(deadlineGraceTicks));
            DeadlineTravelPercent = deadlineTravelPercent;
            DeadlineGraceTicks = deadlineGraceTicks;
        }
    }

    /// <summary>
    /// Owns <see cref="WtoContract"/> and <see cref="ContractCargo"/>: WTO escrow contracts (GDD §2.2 B, §4.4).
    /// A matched trade launches a Tier-1 cargo convoy of the seller's at the seller's site with the goods, plus the fuel
    /// for its route and the upkeep until the deadline, as far as the seller's site holds them; it is ordered to the
    /// buyer's site and pays tolls like any fleet. The deadline is the planned travel time x the deadline percent plus
    /// the grace. Each tick, settled with the delivered quantity:
    /// <list type="bullet">
    /// <item>Convoy at the buyer's site: the contract goods it still holds are delivered.</item>
    /// <item>Convoy destroyed, or the deadline passed: nothing delivered. A late convoy stays the seller's, goods
    /// included; one still docked at a site of the seller's is unloaded there and disbanded.</item>
    /// </list>
    /// Settling pays the seller the delivered value less their bracket fee (to the WTO fee pool) plus the delivered
    /// share of the deposit; the buyer gets back the undelivered value plus the rest of the deposit as compensation.
    /// A trade between two sites with no route, or whose route fuel the seller's site lacks, is void: goods, value and
    /// deposit go back. Same-site trades deliver
    /// at once.
    /// </summary>
    public sealed class WtoContractSystem : ISimSystem, ITradeSettlement
    {
        /// <summary>Tech tier of the convoys contracts launch, and so of the routes a contract can use.</summary>
        public const byte ConvoyTier = 1;

        private readonly World _world;
        private readonly ComponentStore<WtoContract> _contracts;
        private readonly ComponentStore<ContractCargo> _cargo;
        private readonly ComponentStore<FleetMotion> _motions;
        private readonly ComponentStore<StarlaneEdge> _lanes;
        private readonly ComponentStore<Stockpile> _stockpiles;
        private readonly ComponentStore<SiteInfo> _sites;
        private readonly Ledger _ledger;
        private readonly EntityId _escrow;
        private readonly EntityId _feePool;
        private readonly IMarketRates _rates;
        private readonly FleetFactory _fleets;
        private readonly RoutePlanner _planner;
        private readonly SimClock _clock;
        private readonly FleetBalance _fleetBalance;
        private readonly WtoContractBalance _balance;

        public WtoContractSystem(
            World world, ComponentStore<WtoContract> contracts, ComponentStore<ContractCargo> cargo,
            ComponentStore<FleetMotion> motions, ComponentStore<StarlaneEdge> lanes, ComponentStore<Stockpile> stockpiles,
            ComponentStore<SiteInfo> sites, Ledger ledger, EntityId escrow, EntityId feePool, IMarketRates rates, FleetFactory fleets, RoutePlanner planner,
            SimClock clock, FleetBalance fleetBalance, WtoContractBalance balance)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _contracts = contracts ?? throw new ArgumentNullException(nameof(contracts));
            _cargo = cargo ?? throw new ArgumentNullException(nameof(cargo));
            _motions = motions ?? throw new ArgumentNullException(nameof(motions));
            _lanes = lanes ?? throw new ArgumentNullException(nameof(lanes));
            _stockpiles = stockpiles ?? throw new ArgumentNullException(nameof(stockpiles));
            _sites = sites ?? throw new ArgumentNullException(nameof(sites));
            _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
            if (escrow.IsNull) throw new ArgumentException("WTO escrow wallet required.", nameof(escrow));
            if (feePool.IsNull) throw new ArgumentException("WTO fee pool required.", nameof(feePool));
            _escrow = escrow;
            _feePool = feePool;
            _rates = rates ?? throw new ArgumentNullException(nameof(rates));
            _fleets = fleets ?? throw new ArgumentNullException(nameof(fleets));
            _planner = planner ?? throw new ArgumentNullException(nameof(planner));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _fleetBalance = fleetBalance ?? throw new ArgumentNullException(nameof(fleetBalance));
            _balance = balance ?? throw new ArgumentNullException(nameof(balance));
        }

        public void Open(in TradeMatch trade, long tick)
        {
            var contract = new WtoContract
            {
                BuyerPlayerId = trade.BuyerPlayerId, SellerPlayerId = trade.SellerPlayerId, Item = trade.Item,
                Quantity = trade.Quantity, PriceCents = trade.PriceCents, DepositCents = trade.DepositCents,
                BuyerSite = trade.BuyerSite
            };
            if (trade.SellerSite == trade.BuyerSite)
            {
                _stockpiles.Get(trade.BuyerSite).Add(trade.Item, trade.Quantity);
                Settle(contract, trade.Quantity);
                return;
            }

            var route = default(FleetRoute);
            if (!_planner.TryPlan(trade.SellerSite, trade.BuyerSite, ConvoyTier, ref route))
            {
                Void(in trade);
                return;
            }

            var unit = new FleetUnit { OwnerPlayerId = trade.SellerPlayerId, TechTier = ConvoyTier, CargoConvoys = 1 };
            long travel = 0;
            int fuel = 0;
            for (int i = 0; i < route.Count; i++)
            {
                ref var edge = ref _lanes.Get(route.LaneAt(i));
                travel += _planner.TravelTicks(edge);
                fuel += _planner.FuelFor(unit, edge);
            }
            ref var supplies = ref _stockpiles.Get(trade.SellerSite);
            if (!supplies.Has(ItemId.ItemFuel, fuel))
            {
                Void(in trade);
                return;
            }
            contract.DeadlineTick = tick + travel * _balance.DeadlineTravelPercent / 100 + _balance.DeadlineGraceTicks;

            var convoy = _fleets.Create(unit, trade.SellerSite);
            ref var hold = ref _stockpiles.Get(convoy);
            hold.Add(trade.Item, trade.Quantity);
            supplies = ref _stockpiles.Get(trade.SellerSite);
            Load(ref supplies, ref hold, ItemId.ItemFuel, fuel);
            long intervals = contract.DeadlineTick / _fleetBalance.UpkeepIntervalTicks - tick / _fleetBalance.UpkeepIntervalTicks;
            var upkeep = _fleetBalance.Tier(ConvoyTier).UpkeepPerShipHour;
            for (int k = 0; k < upkeep.Length; k++)
                Load(ref supplies, ref hold, upkeep[k].Item,
                    (int)Math.Min(int.MaxValue, intervals * FleetUpkeepSystem.Due(unit.TotalShips, upkeep[k].Milli, 0)));

            _cargo.Add(convoy, new ContractCargo { Item = trade.Item, Quantity = trade.Quantity });
            contract.Convoy = convoy;
            _contracts.Add(_world.Create(), contract);

            var move = new DelayedPlayerCommand
            {
                IssuedAtTick = tick, ExecuteAtTick = tick + 1, PlayerId = trade.SellerPlayerId,
                CommandType = CommandType.Move, Target = convoy
            };
            move.ParamEntity = trade.BuyerSite;
            _clock.Enqueue(move);
        }

        // Backwards, so destroying a contract never skips one; contracts settle independently of each other.
        public void OnTick(long tick)
        {
            for (int i = _contracts.Count - 1; i >= 0; i--)
            {
                ref var contract = ref _contracts.At(i);
                var entity = _contracts.EntityAt(i);
                var convoy = contract.Convoy;
                if (!_world.IsAlive(convoy))
                {
                    Settle(contract, 0);
                }
                else if (_motions.Get(convoy).Site == contract.BuyerSite)
                {
                    ref var hold = ref _stockpiles.Get(convoy);
                    int delivered = Math.Min(contract.Quantity, hold.Get(contract.Item));
                    hold.Add(contract.Item, -delivered);
                    _stockpiles.Get(contract.BuyerSite).Add(contract.Item, delivered);
                    Settle(contract, delivered);
                    _world.Destroy(convoy);
                }
                else if (tick >= contract.DeadlineTick)
                {
                    Settle(contract, 0);
                    _cargo.Remove(convoy);
                    var site = _motions.Get(convoy).Site;
                    if (!site.IsNull && _sites.Has(site) && _sites.Get(site).OwnerPlayerId == contract.SellerPlayerId)
                        Disband(convoy, site);
                }
                else
                {
                    continue;
                }
                _world.Destroy(entity);
            }
        }

        private void Settle(in WtoContract contract, int delivered)
        {
            long value = contract.PriceCents * delivered;
            long fee = value * _rates.FeeBasisPoints(contract.SellerPlayerId) / 10_000;
            long sellerDeposit = contract.DepositCents * delivered / contract.Quantity;
            Pay(Wallet(contract.SellerPlayerId), value - fee + sellerDeposit);
            Pay(_feePool, fee);
            Pay(Wallet(contract.BuyerPlayerId),
                contract.PriceCents * (contract.Quantity - delivered) + contract.DepositCents - sellerDeposit);
        }

        // A trade no convoy can carry (no route, or the seller's site can't fuel it): goods, value and deposit go back.
        private void Void(in TradeMatch trade)
        {
            _stockpiles.Get(trade.SellerSite).Add(trade.Item, trade.Quantity);
            Pay(Wallet(trade.BuyerPlayerId), trade.PriceCents * trade.Quantity);
            Pay(Wallet(trade.SellerPlayerId), trade.DepositCents);
        }

        // A failed convoy docked at one of the seller's own sites (it never left) goes back into that site's stock rather than idling there as a
        // one-ship fleet that owes upkeep forever.
        private void Disband(EntityId convoy, EntityId site)
        {
            ref var hold = ref _stockpiles.Get(convoy);
            ref var stock = ref _stockpiles.Get(site);
            for (int slot = 0; slot < ItemIds.Count; slot++)
            {
                var item = ItemIds.FromSlot(slot);
                int quantity = hold.Get(item);
                if (quantity > 0)
                    stock.Add(item, quantity);
            }
            _world.Destroy(convoy);
        }

        private static void Load(ref Stockpile from, ref Stockpile to, ItemId item, int wanted)
        {
            int moved = Math.Min(wanted, from.Get(item));
            from.Add(item, -moved);
            to.Add(item, moved);
        }

        private EntityId Wallet(int playerId)
        {
            if (!_ledger.TryFindPlayerWallet(playerId, out var wallet))
                throw new InvalidOperationException($"Player {playerId} has no wallet.");
            return wallet;
        }

        private void Pay(EntityId to, long cents)
        {
            if (!_ledger.Transfer(_escrow, to, cents))
                throw new InvalidOperationException("WTO escrow does not cover its contracts.");
        }
    }
}
