using System;
using System.Collections.Generic;
using Unruled.Core.AI;
using Unruled.Core.Board;
using Unruled.Core.Ecs;
using Unruled.Core.Economy;
using Unruled.Core.Galaxy;
using Unruled.Core.Military;
using Unruled.Core.Persistence;
using Unruled.Core.Research;
using Unruled.Core.Simulation;
using Unruled.Core.SystemFactions;

namespace Unruled.Core.Net
{
    /// <summary>
    /// What one player may see of a world, as the body of a <see cref="ViewFrame"/>:
    /// <c>[tick, the player's orders in flight [command...], channels [[upserts, removals]...]]</c>, one channel per
    /// component store in a fixed, append-only order.
    /// On the server, <see cref="Write"/> builds a full view or the change since the last one it wrote. The player sees:
    /// their own empire in full (sites' stock, fleets, wallet, loans, research, orders, charts); the systems they hold a
    /// chart of (sites and owners, lanes, weather, deposits, buildings, the Citadel); rival and THE SYSTEM's fleets only
    /// where they have eyes (a site they own or a fleet of theirs is docked at, or a radar reveal), never their stock or
    /// routes; rivals' HQs, standings and shields but not their wallets, stock, research or AI; the market (prices, the
    /// order book without its owners) and THE SYSTEM's public bulletins.
    /// On the client, <see cref="Apply"/> writes a received body into a mirror <see cref="SimulationWorld"/> that is never
    /// stepped, so the views keep reading the same stores they read in a local game.
    /// </summary>
    public sealed class PlayerView
    {
        private readonly SimulationWorld _sim;
        private readonly int _playerId;
        private readonly List<ViewChannel> _channels = new List<ViewChannel>();
        private readonly MsgPackWriter _body = new MsgPackWriter(16 * 1024);
        private readonly MsgPackWriter _scratch = new MsgPackWriter(16 * 1024);
        private readonly HashSet<EntityId> _eyes = new HashSet<EntityId>(); // sites the player sees fleets at
        private DelayedPlayerCommand[] _pending = new DelayedPlayerCommand[8];
        private bool _wroteOnce;

        // Channel indexes whose change needs a rebuilt index on the client.
        private readonly int _laneChannel;
        private readonly int _systemChannel;
        private readonly int _chartChannel;

        /// <summary>Entities added or replaced by the last write, and entities dropped from the view.</summary>
        public int LastUpserts { get; private set; }
        public int LastRemovals { get; private set; }

        public int PlayerId => _playerId;

        public PlayerView(SimulationWorld sim, int playerId)
        {
            _sim = sim ?? throw new ArgumentNullException(nameof(sim));
            if (playerId <= 0) throw new ArgumentOutOfRangeException(nameof(playerId));
            _playerId = playerId;
            int me = playerId;

            // Board.
            Add(sim.Sites, ComponentFormatters.Write, ComponentFormatters.ReadSiteInfo, (EntityId e, in SiteInfo v) => Knows(v.ClusterId));
            _laneChannel = _channels.Count;
            Add(sim.Starlanes, ComponentFormatters.Write, ComponentFormatters.ReadStarlaneEdge,
                (EntityId e, in StarlaneEdge v) => KnowsSite(v.SiteA) || KnowsSite(v.SiteB));
            Add(sim.LaneWeather, ComponentFormatters.Write, ComponentFormatters.ReadLaneWeather, (EntityId e, in LaneWeather v) => KnowsLane(e));
            Add(sim.StormFronts, ComponentFormatters.Write, ComponentFormatters.ReadStormFront, (EntityId e, in StormFront v) => Knows(v.ClusterId));
            Add(sim.Weather, ComponentFormatters.Write, ComponentFormatters.ReadSiteWeather, (EntityId e, in SiteWeather v) => KnowsSite(e));

            // Economy at sites and on fleets: stock only for the player's own sites and fleets, and THE SYSTEM's shop.
            Add(sim.Stockpiles, ComponentFormatters.Write, ComponentFormatters.ReadStockpile, (EntityId e, in Stockpile v) => OwnsHolder(e));
            Add(sim.Deposits, ComponentFormatters.Write, ComponentFormatters.ReadMineralDeposit, (EntityId e, in MineralDeposit v) => KnowsSite(v.Site));
            Add(sim.Refineries, ComponentFormatters.Write, ComponentFormatters.ReadRefinery, (EntityId e, in Refinery v) => KnowsSite(v.Site),
                (EntityId e, in Refinery v) => SiteOwner(v.Site) == me ? v : new Refinery { Site = v.Site, Output = v.Output, State = v.State });
            Add(sim.Quarantines, ComponentFormatters.Write, ComponentFormatters.ReadSiteQuarantine, (EntityId e, in SiteQuarantine v) => KnowsSite(e));

            // Players: every HQ (rivals' core base only when its system is charted), relays in charted systems.
            Add(sim.Players, ComponentFormatters.Write, ComponentFormatters.ReadPlayerHq, null,
                (EntityId e, in PlayerHq v) => v.PlayerId == me ? v
                    : new PlayerHq { PlayerId = v.PlayerId, CoreBase = KnowsSite(v.CoreBase) ? v.CoreBase : default });
            Add(sim.Relays, ComponentFormatters.Write, ComponentFormatters.ReadRelayStation, (EntityId e, in RelayStation v) => KnowsSite(e));

            // Fleets: own in full; others only where the player has eyes, never their stock, route or debts.
            Add(sim.Fleets, ComponentFormatters.Write, ComponentFormatters.ReadFleetUnit, (EntityId e, in FleetUnit v) => SeesFleet(e));
            Add(sim.FleetMotions, ComponentFormatters.Write, ComponentFormatters.ReadFleetMotion, (EntityId e, in FleetMotion v) => SeesFleet(e));
            Add(sim.FleetRoutes, ComponentFormatters.Write, ComponentFormatters.ReadFleetRoute, (EntityId e, in FleetRoute v) => OwnsFleet(e));
            Add(sim.FleetSensors, ComponentFormatters.Write, ComponentFormatters.ReadFleetSensors, (EntityId e, in FleetSensors v) => SeesFleet(e),
                (EntityId e, in FleetSensors v) => OwnsFleet(e) ? v : new FleetSensors { RevealedUntilTick = v.RevealedUntilTick });
            Add(sim.FleetReadiness, ComponentFormatters.Write, ComponentFormatters.ReadFleetReadiness, (EntityId e, in FleetReadiness v) => SeesFleet(e));
            Add(sim.FleetCombat, ComponentFormatters.Write, ComponentFormatters.ReadFleetCombatState, (EntityId e, in FleetCombatState v) => SeesFleet(e),
                (EntityId e, in FleetCombatState v) => OwnsFleet(e) ? v : new FleetCombatState
                {
                    Focus = v.Focus, Rof = v.Rof, TemperatureMilli = v.TemperatureMilli, IsOverheated = v.IsOverheated,
                    IsVenting = v.IsVenting, EngagedUntilTick = v.EngagedUntilTick, FiredAtTick = v.FiredAtTick,
                    RaidsConvoys = v.RaidsConvoys
                });
            Add(sim.Relocations, ComponentFormatters.Write, ComponentFormatters.ReadCapitalRelocation, (EntityId e, in CapitalRelocation v) => IsMine(e));
            Add(sim.RadarScans, ComponentFormatters.Write, ComponentFormatters.ReadRadarScan, (EntityId e, in RadarScan v) => v.OwnerPlayerId == me);

            // Money and standing.
            Add(sim.Wallets, ComponentFormatters.Write, ComponentFormatters.ReadWallet, (EntityId e, in Wallet v) => v.OwnerPlayerId == me);
            Add(sim.Standings, ComponentFormatters.Write, ComponentFormatters.ReadPlayerStanding);
            Add(sim.WtoOrders, ComponentFormatters.Write, ComponentFormatters.ReadWtoOrder, null,
                (EntityId e, in WtoOrder v) => v.OwnerPlayerId == me ? v : new WtoOrder
                {
                    Item = v.Item, Side = v.Side, PriceCents = v.PriceCents, Remaining = v.Remaining, PlacedTick = v.PlacedTick
                });
            Add(sim.ItemMarkets, ComponentFormatters.Write, ComponentFormatters.ReadItemMarket);
            Add(sim.Contracts, ComponentFormatters.Write, ComponentFormatters.ReadWtoContract,
                (EntityId e, in WtoContract v) => v.BuyerPlayerId == me || v.SellerPlayerId == me);
            Add(sim.ContractCargo, ComponentFormatters.Write, ComponentFormatters.ReadContractCargo, (EntityId e, in ContractCargo v) => SeesFleet(e));
            Add(sim.StolenGoods, ComponentFormatters.Write, ComponentFormatters.ReadStolenGoods, (EntityId e, in StolenGoods v) => OwnsHolder(e));

            // THE SYSTEM.
            Add(sim.Sanctions, ComponentFormatters.Write, ComponentFormatters.ReadSystemSanction, (EntityId e, in SystemSanction v) => IsMine(e));
            Add(sim.Bulletins, ComponentFormatters.Write, ComponentFormatters.ReadSystemBulletin);
            Add(sim.AiControllers, ComponentFormatters.Write, ComponentFormatters.ReadAiController, (EntityId e, in AiController v) => IsMine(e));
            Add(sim.TechProgress, ComponentFormatters.Write, ComponentFormatters.ReadTechProgress, (EntityId e, in TechProgress v) => IsMine(e));
            Add(sim.ShipOrders, ComponentFormatters.Write, ComponentFormatters.ReadShipBuildOrder, (EntityId e, in ShipBuildOrder v) => v.OwnerPlayerId == me);
            Add(sim.JumpGates, ComponentFormatters.Write, ComponentFormatters.ReadJumpGate,
                (EntityId e, in JumpGate v) => v.OwnerPlayerId == me || KnowsSite(v.HomeSite) || KnowsSite(v.FarSite));
            Add(sim.ConvoyRoutes, ComponentFormatters.Write, ComponentFormatters.ReadConvoyRoute, (EntityId e, in ConvoyRoute v) => OwnsFleet(e));
            Add(sim.Citadels, ComponentFormatters.Write, ComponentFormatters.ReadSystemCitadel, (EntityId e, in SystemCitadel v) => KnowsSite(e));
            Add(sim.CitadelAssaults, ComponentFormatters.Write, ComponentFormatters.ReadCitadelAssault, (EntityId e, in CitadelAssault v) => IsMine(e));
            Add(sim.VictoryRecords, ComponentFormatters.Write, ComponentFormatters.ReadVictoryRecord);
            Add(sim.DirectorateSeats, ComponentFormatters.Write, ComponentFormatters.ReadDirectorateSeat);
            Add(sim.Loans, ComponentFormatters.Write, ComponentFormatters.ReadLoan, (EntityId e, in Loan v) => IsMine(e));

            // Galaxy: charted systems, their names, the player's own charts, the chart shop.
            _systemChannel = _channels.Count;
            Add(sim.StarSystems, ComponentFormatters.Write, ComponentFormatters.ReadStarSystem, (EntityId e, in StarSystem v) => Knows(v.ClusterId));
            Add(sim.SystemNames, ComponentFormatters.Write, ComponentFormatters.ReadSystemName, (EntityId e, in SystemName v) => KnowsSystem(e));
            _chartChannel = _channels.Count;
            Add(sim.SystemCharts, ComponentFormatters.Write, ComponentFormatters.ReadSystemChart, (EntityId e, in SystemChart v) => v.PlayerId == me);
            Add(sim.ChartListings, ComponentFormatters.Write, ComponentFormatters.ReadChartListing);
            Add(sim.Presences, ComponentFormatters.Write, ComponentFormatters.ReadPlayerPresence, null,
                (EntityId e, in PlayerPresence v) => IsMine(e) ? v : new PlayerPresence
                {
                    IsNewcomer = v.IsNewcomer, ShieldFromTick = v.ShieldFromTick, ShieldUntilTick = v.ShieldUntilTick
                });
        }

        /// <summary>
        /// The view now: in full the first time and when <paramref name="full"/> is set (a snapshot), otherwise only
        /// what changed since the last write (a delta). The bytes are valid until the next write.
        /// </summary>
        public ReadOnlySpan<byte> Write(bool full)
        {
            full |= !_wroteOnce;
            _wroteOnce = true;
            FindEyes();
            _body.Rewind(0);
            _body.WriteArrayHeader(3);
            _body.Write(_sim.Clock.CurrentTick);

            int own = 0;
            for (int i = 0; i < _sim.Clock.PendingCommandCount; i++)
                if (_sim.Clock.PendingAt(i).PlayerId == _playerId)
                    own++;
            _body.WriteArrayHeader(own);
            for (int i = 0; i < _sim.Clock.PendingCommandCount; i++)
            {
                var command = _sim.Clock.PendingAt(i);
                if (command.PlayerId == _playerId)
                    ComponentFormatters.Write(_body, in command);
            }

            int upserts = 0, removals = 0;
            _body.WriteArrayHeader(_channels.Count);
            for (int i = 0; i < _channels.Count; i++)
                _channels[i].Write(_body, _scratch, full, ref upserts, ref removals);
            LastUpserts = upserts;
            LastRemovals = removals;
            return _body.WrittenSpan;
        }

        /// <summary>
        /// Applies a body written by a server's view of this player to this view's world (a client mirror): the clock
        /// jumps to the body's tick and the player's orders in flight replace the queue. A snapshot replaces every
        /// store; a delta changes only what it lists. Channels a newer server appended are skipped.
        /// </summary>
        public void Apply(ReadOnlyMemory<byte> body, bool full)
        {
            var r = new MsgPackReader(body);
            int keys = r.ReadArrayHeader();
            if (keys < 3) throw new FormatException($"A view body has {keys} keys; expected 3.");
            long tick = r.ReadInt64();
            int pending = r.ReadArrayHeader();
            if (_pending.Length < pending)
                _pending = new DelayedPlayerCommand[Math.Max(pending, _pending.Length * 2)];
            for (int i = 0; i < pending; i++)
                _pending[i] = ComponentFormatters.ReadCommand(r);
            _sim.Clock.Mirror(tick, _pending, pending);

            int channels = r.ReadArrayHeader();
            bool lanes = false, galaxy = false;
            for (int i = 0; i < channels; i++)
            {
                if (i >= _channels.Count)
                {
                    r.Skip(1);
                    continue;
                }
                bool changed = _channels[i].Apply(r, full);
                lanes |= changed && i == _laneChannel;
                galaxy |= changed && (i == _systemChannel || i == _chartChannel);
            }
            r.Skip(keys - 3);
            if (!r.End) throw new FormatException("A view body has bytes after its end.");
            if (lanes)
                _sim.Graph.Rebuild(_sim.Starlanes);
            if (galaxy)
                _sim.Galaxy.RebuildIndex();
        }

        private void Add<T>(ComponentStore<T> store, ComponentWriter<T> write, ComponentReader<T> read,
            ViewFilter<T> shows = null, ViewMask<T> mask = null) where T : struct =>
            _channels.Add(new ViewChannel<T>(store, write, read, shows, mask));

        // Sites the player has eyes on: its own sites and the sites its fleets are docked at.
        private void FindEyes()
        {
            _eyes.Clear();
            for (int i = 0; i < _sim.Sites.Count; i++)
                if (_sim.Sites.At(i).OwnerPlayerId == _playerId)
                    _eyes.Add(_sim.Sites.EntityAt(i));
            for (int i = 0; i < _sim.Fleets.Count; i++)
            {
                if (_sim.Fleets.At(i).OwnerPlayerId != _playerId)
                    continue;
                var fleet = _sim.Fleets.EntityAt(i);
                if (_sim.FleetMotions.Has(fleet) && _sim.FleetMotions.Get(fleet).Lane.IsNull)
                    _eyes.Add(_sim.FleetMotions.Get(fleet).Site);
            }
        }

        private bool Knows(int clusterId) => _sim.Galaxy.Knows(_playerId, clusterId);

        private bool KnowsSite(EntityId site) => _sim.Sites.Has(site) && Knows(_sim.Sites.Get(site).ClusterId);

        private bool KnowsLane(EntityId lane)
        {
            if (!_sim.Starlanes.Has(lane)) return false;
            ref var edge = ref _sim.Starlanes.Get(lane);
            return KnowsSite(edge.SiteA) || KnowsSite(edge.SiteB);
        }

        private bool KnowsSystem(EntityId system) => _sim.StarSystems.Has(system) && Knows(_sim.StarSystems.Get(system).ClusterId);

        private int SiteOwner(EntityId site) => _sim.Sites.Has(site) ? _sim.Sites.Get(site).OwnerPlayerId : 0;

        private bool IsMine(EntityId hq) => _sim.Players.Has(hq) && _sim.Players.Get(hq).PlayerId == _playerId;

        private bool OwnsFleet(EntityId fleet) => _sim.Fleets.Has(fleet) && _sim.Fleets.Get(fleet).OwnerPlayerId == _playerId;

        // A stockpile's holder: a site, a fleet, or THE SYSTEM's shop (its stock is public through the market).
        private bool OwnsHolder(EntityId holder) =>
            holder == _sim.WtoFeePool || OwnsFleet(holder) || (_sim.Sites.Has(holder) && _sim.Sites.Get(holder).OwnerPlayerId == _playerId);

        private bool SeesFleet(EntityId fleet)
        {
            if (!_sim.Fleets.Has(fleet)) return false;
            if (_sim.Fleets.Get(fleet).OwnerPlayerId == _playerId) return true;
            if (_sim.FleetSensors.Has(fleet) && _sim.Clock.CurrentTick < _sim.FleetSensors.Get(fleet).RevealedUntilTick) return true;
            if (!_sim.FleetMotions.Has(fleet)) return false;
            ref var motion = ref _sim.FleetMotions.Get(fleet);
            return motion.Lane.IsNull && _eyes.Contains(motion.Site);
        }
    }
}
