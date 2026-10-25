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
    /// routes; the HQ, standing and shield of rivals whose home system they hold a chart of (rivals they have met), never
    /// rivals' wallets, stock, research or AI; the market (prices, the
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
        private readonly HashSet<int> _eyes = new HashSet<int>(); // entity indices of the sites the player sees fleets at
        private readonly ViewInterest _interest = new ViewInterest();
        private readonly List<int> _candidates = new List<int>();
        private bool _fullScan; // tests: visit every component instead of the indexed ones
        private DelayedPlayerCommand[] _pending = new DelayedPlayerCommand[8];
        private bool _wroteOnce;
        private readonly PlayerViewHub _hub;
        private readonly bool _ownHub; // a private hub: begin a pass on every write

        // Channel indexes whose change needs a rebuilt index on the client.
        private readonly int _laneChannel;
        private readonly int _systemChannel;
        private readonly int _chartChannel;

        /// <summary>Entities added or replaced by the last write, and entities dropped from the view.</summary>
        public int LastUpserts { get; private set; }
        public int LastRemovals { get; private set; }

        public int PlayerId => _playerId;

        /// <summary>Tests: a view that visits every component of every store, as before the index, to compare with.</summary>
        internal static PlayerView FullScan(SimulationWorld sim, int playerId)
        {
            var view = new PlayerView(sim, playerId);
            view._fullScan = true;
            return view;
        }

        /// <summary>A view with its own hub: correct on its own, as when a client applies what the server sent.</summary>
        public PlayerView(SimulationWorld sim, int playerId) : this(new PlayerViewHub(sim), playerId, true)
        {
        }

        /// <summary>
        /// A server's view of one player, sharing encodings with every other view on <paramref name="hub"/>. The host
        /// calls <see cref="PlayerViewHub.BeginPass"/> once after each change of the world, before writing the views.
        /// </summary>
        public PlayerView(PlayerViewHub hub, int playerId) : this(hub, playerId, false)
        {
        }

        private PlayerView(PlayerViewHub hub, int playerId, bool ownHub)
        {
            _hub = hub ?? throw new ArgumentNullException(nameof(hub));
            _ownHub = ownHub;
            var sim = hub.Sim;
            _sim = sim;
            if (playerId <= 0) throw new ArgumentOutOfRangeException(nameof(playerId));
            _playerId = playerId;
            int me = playerId;

            // Board.
            Add(sim.Sites, ComponentFormatters.Write, ComponentFormatters.ReadSiteInfo, (EntityId e, in SiteInfo v) => Show(Knows(v.ClusterId)),
                (EntityId e, in SiteInfo v, ViewIndex k) => k.AddCluster(v.ClusterId));
            _laneChannel = _channels.Count;
            Add(sim.Starlanes, ComponentFormatters.Write, ComponentFormatters.ReadStarlaneEdge,
                (EntityId e, in StarlaneEdge v) => Show(KnowsSite(v.SiteA) || KnowsSite(v.SiteB)),
                (EntityId e, in StarlaneEdge v, ViewIndex k) => { k.AddSiteCluster(v.SiteA); k.AddSiteCluster(v.SiteB); });
            Add(sim.LaneWeather, ComponentFormatters.Write, ComponentFormatters.ReadLaneWeather, (EntityId e, in LaneWeather v) => Show(KnowsLane(e)),
                (EntityId e, in LaneWeather v, ViewIndex k) => LaneKeys(e, k));
            Add(sim.StormFronts, ComponentFormatters.Write, ComponentFormatters.ReadStormFront, (EntityId e, in StormFront v) => Show(Knows(v.ClusterId)),
                (EntityId e, in StormFront v, ViewIndex k) => k.AddCluster(v.ClusterId));
            Add(sim.Weather, ComponentFormatters.Write, ComponentFormatters.ReadSiteWeather, (EntityId e, in SiteWeather v) => Show(KnowsSite(e)),
                (EntityId e, in SiteWeather v, ViewIndex k) => k.AddSiteCluster(e));

            // Economy at sites and on fleets: stock only for the player's own sites and fleets, and THE SYSTEM's shop.
            Add(sim.Stockpiles, ComponentFormatters.Write, ComponentFormatters.ReadStockpile, (EntityId e, in Stockpile v) => Show(OwnsHolder(e)),
                (EntityId e, in Stockpile v, ViewIndex k) => HolderKeys(e, k));
            Add(sim.Deposits, ComponentFormatters.Write, ComponentFormatters.ReadMineralDeposit, (EntityId e, in MineralDeposit v) => Show(KnowsSite(v.Site)),
                (EntityId e, in MineralDeposit v, ViewIndex k) => k.AddSiteCluster(v.Site));
            Add(sim.Refineries, ComponentFormatters.Write, ComponentFormatters.ReadRefinery,
                (EntityId e, in Refinery v) => !KnowsSite(v.Site) ? Hidden : SiteOwner(v.Site) == me ? Full : Rival,
                (EntityId e, in Refinery v, ViewIndex k) => k.AddSiteCluster(v.Site), 2,
                (int f, in Refinery v) => new Refinery { Site = v.Site, Output = v.Output, State = v.State });
            Add(sim.Quarantines, ComponentFormatters.Write, ComponentFormatters.ReadSiteQuarantine, (EntityId e, in SiteQuarantine v) => Show(KnowsSite(e)),
                (EntityId e, in SiteQuarantine v, ViewIndex k) => k.AddSiteCluster(e));

            // Players: every HQ (rivals' core base only when its system is charted), relays in charted systems.
            Add(sim.Players, ComponentFormatters.Write, ComponentFormatters.ReadPlayerHq,
                (EntityId e, in PlayerHq v) => v.PlayerId == me ? Full : KnowsSite(v.CoreBase) ? Full : Hidden,
                (EntityId e, in PlayerHq v, ViewIndex k) => { k.AddPlayer(v.PlayerId); k.AddSiteCluster(v.CoreBase); });
            Add(sim.Relays, ComponentFormatters.Write, ComponentFormatters.ReadRelayStation, (EntityId e, in RelayStation v) => Show(KnowsSite(e)),
                (EntityId e, in RelayStation v, ViewIndex k) => k.AddSiteCluster(e));

            // Fleets: own in full; others only where the player has eyes, never their stock, route or debts.
            Add(sim.Fleets, ComponentFormatters.Write, ComponentFormatters.ReadFleetUnit, (EntityId e, in FleetUnit v) => Show(SeesFleet(e)),
                (EntityId e, in FleetUnit v, ViewIndex k) => FleetKeys(e, k));
            Add(sim.FleetMotions, ComponentFormatters.Write, ComponentFormatters.ReadFleetMotion, (EntityId e, in FleetMotion v) => Show(SeesFleet(e)),
                (EntityId e, in FleetMotion v, ViewIndex k) => FleetKeys(e, k));
            Add(sim.FleetRoutes, ComponentFormatters.Write, ComponentFormatters.ReadFleetRoute, (EntityId e, in FleetRoute v) => Show(OwnsFleet(e)),
                (EntityId e, in FleetRoute v, ViewIndex k) => k.AddFleetOwner(e));
            Add(sim.FleetSensors, ComponentFormatters.Write, ComponentFormatters.ReadFleetSensors,
                (EntityId e, in FleetSensors v) => OwnsFleet(e) ? Full : SeesFleet(e) ? Rival : Hidden,
                (EntityId e, in FleetSensors v, ViewIndex k) => FleetKeys(e, k), 2,
                (int f, in FleetSensors v) => new FleetSensors { RevealedUntilTick = v.RevealedUntilTick });
            Add(sim.FleetReadiness, ComponentFormatters.Write, ComponentFormatters.ReadFleetReadiness, (EntityId e, in FleetReadiness v) => Show(SeesFleet(e)),
                (EntityId e, in FleetReadiness v, ViewIndex k) => FleetKeys(e, k));
            Add(sim.FleetCombat, ComponentFormatters.Write, ComponentFormatters.ReadFleetCombatState,
                (EntityId e, in FleetCombatState v) => OwnsFleet(e) ? Full : SeesFleet(e) ? Rival : Hidden,
                (EntityId e, in FleetCombatState v, ViewIndex k) => FleetKeys(e, k), 2,
                (int f, in FleetCombatState v) => new FleetCombatState
                {
                    Focus = v.Focus, Rof = v.Rof, TemperatureMilli = v.TemperatureMilli, IsOverheated = v.IsOverheated,
                    IsVenting = v.IsVenting, EngagedUntilTick = v.EngagedUntilTick, FiredAtTick = v.FiredAtTick,
                    RaidsConvoys = v.RaidsConvoys
                });
            Add(sim.Relocations, ComponentFormatters.Write, ComponentFormatters.ReadCapitalRelocation, (EntityId e, in CapitalRelocation v) => Show(IsMine(e)),
                (EntityId e, in CapitalRelocation v, ViewIndex k) => k.AddHqOwner(e));
            Add(sim.RadarScans, ComponentFormatters.Write, ComponentFormatters.ReadRadarScan, (EntityId e, in RadarScan v) => Show(v.OwnerPlayerId == me),
                (EntityId e, in RadarScan v, ViewIndex k) => k.AddPlayer(v.OwnerPlayerId));

            // Money and standing.
            Add(sim.Wallets, ComponentFormatters.Write, ComponentFormatters.ReadWallet, (EntityId e, in Wallet v) => Show(v.OwnerPlayerId == me),
                (EntityId e, in Wallet v, ViewIndex k) => k.AddPlayer(v.OwnerPlayerId));
            Add(sim.Standings, ComponentFormatters.Write, ComponentFormatters.ReadPlayerStanding,
                (EntityId e, in PlayerStanding v) => Show(Met(e)),
                (EntityId e, in PlayerStanding v, ViewIndex k) => HqKeys(e, k));
            Add(sim.WtoOrders, ComponentFormatters.Write, ComponentFormatters.ReadWtoOrder,
                (EntityId e, in WtoOrder v) => v.OwnerPlayerId == me ? Full : Rival,
                PublicKeys<WtoOrder>(), 2,
                (int f, in WtoOrder v) => new WtoOrder
                {
                    Item = v.Item, Side = v.Side, PriceCents = v.PriceCents, Remaining = v.Remaining, PlacedTick = v.PlacedTick
                });
            Add<ItemMarket>(sim.ItemMarkets, ComponentFormatters.Write, ComponentFormatters.ReadItemMarket, null,
                PublicKeys<ItemMarket>());
            Add(sim.Contracts, ComponentFormatters.Write, ComponentFormatters.ReadWtoContract,
                (EntityId e, in WtoContract v) => Show(v.BuyerPlayerId == me || v.SellerPlayerId == me),
                (EntityId e, in WtoContract v, ViewIndex k) => { k.AddPlayer(v.BuyerPlayerId); k.AddPlayer(v.SellerPlayerId); });
            Add(sim.ContractCargo, ComponentFormatters.Write, ComponentFormatters.ReadContractCargo, (EntityId e, in ContractCargo v) => Show(SeesFleet(e)),
                (EntityId e, in ContractCargo v, ViewIndex k) => FleetKeys(e, k));
            Add(sim.StolenGoods, ComponentFormatters.Write, ComponentFormatters.ReadStolenGoods, (EntityId e, in StolenGoods v) => Show(OwnsHolder(e)),
                (EntityId e, in StolenGoods v, ViewIndex k) => HolderKeys(e, k));

            // THE SYSTEM.
            Add(sim.Sanctions, ComponentFormatters.Write, ComponentFormatters.ReadSystemSanction, (EntityId e, in SystemSanction v) => Show(IsMine(e)),
                (EntityId e, in SystemSanction v, ViewIndex k) => k.AddHqOwner(e));
            Add<SystemBulletin>(sim.Bulletins, ComponentFormatters.Write, ComponentFormatters.ReadSystemBulletin, null,
                PublicKeys<SystemBulletin>());
            Add(sim.AiControllers, ComponentFormatters.Write, ComponentFormatters.ReadAiController, (EntityId e, in AiController v) => Show(IsMine(e)),
                (EntityId e, in AiController v, ViewIndex k) => k.AddHqOwner(e));
            Add(sim.TechProgress, ComponentFormatters.Write, ComponentFormatters.ReadTechProgress, (EntityId e, in TechProgress v) => Show(IsMine(e)),
                (EntityId e, in TechProgress v, ViewIndex k) => k.AddHqOwner(e));
            Add(sim.ShipOrders, ComponentFormatters.Write, ComponentFormatters.ReadShipBuildOrder, (EntityId e, in ShipBuildOrder v) => Show(v.OwnerPlayerId == me),
                (EntityId e, in ShipBuildOrder v, ViewIndex k) => k.AddPlayer(v.OwnerPlayerId));
            Add(sim.JumpGates, ComponentFormatters.Write, ComponentFormatters.ReadJumpGate,
                (EntityId e, in JumpGate v) => Show(v.OwnerPlayerId == me || KnowsSite(v.HomeSite) || KnowsSite(v.FarSite)),
                (EntityId e, in JumpGate v, ViewIndex k) => { k.AddPlayer(v.OwnerPlayerId); k.AddSiteCluster(v.HomeSite); k.AddSiteCluster(v.FarSite); });
            Add(sim.ConvoyRoutes, ComponentFormatters.Write, ComponentFormatters.ReadConvoyRoute, (EntityId e, in ConvoyRoute v) => Show(OwnsFleet(e)),
                (EntityId e, in ConvoyRoute v, ViewIndex k) => k.AddFleetOwner(e));
            Add(sim.Citadels, ComponentFormatters.Write, ComponentFormatters.ReadSystemCitadel, (EntityId e, in SystemCitadel v) => Show(KnowsSite(e)),
                (EntityId e, in SystemCitadel v, ViewIndex k) => k.AddSiteCluster(e));
            Add(sim.CitadelAssaults, ComponentFormatters.Write, ComponentFormatters.ReadCitadelAssault, (EntityId e, in CitadelAssault v) => Show(IsMine(e)),
                (EntityId e, in CitadelAssault v, ViewIndex k) => k.AddHqOwner(e));
            Add<VictoryRecord>(sim.VictoryRecords, ComponentFormatters.Write, ComponentFormatters.ReadVictoryRecord, null,
                PublicKeys<VictoryRecord>());
            Add<DirectorateSeat>(sim.DirectorateSeats, ComponentFormatters.Write, ComponentFormatters.ReadDirectorateSeat, null,
                PublicKeys<DirectorateSeat>());
            Add(sim.Loans, ComponentFormatters.Write, ComponentFormatters.ReadLoan, (EntityId e, in Loan v) => Show(IsMine(e)),
                (EntityId e, in Loan v, ViewIndex k) => k.AddHqOwner(e));

            // Galaxy: charted systems, their names, the player's own charts, the chart shop.
            _systemChannel = _channels.Count;
            Add(sim.StarSystems, ComponentFormatters.Write, ComponentFormatters.ReadStarSystem, (EntityId e, in StarSystem v) => Show(Knows(v.ClusterId)),
                (EntityId e, in StarSystem v, ViewIndex k) => k.AddCluster(v.ClusterId));
            Add(sim.SystemNames, ComponentFormatters.Write, ComponentFormatters.ReadSystemName, (EntityId e, in SystemName v) => Show(KnowsSystem(e)),
                (EntityId e, in SystemName v, ViewIndex k) => SystemKeys(e, k));
            _chartChannel = _channels.Count;
            Add(sim.SystemCharts, ComponentFormatters.Write, ComponentFormatters.ReadSystemChart, (EntityId e, in SystemChart v) => Show(v.PlayerId == me),
                (EntityId e, in SystemChart v, ViewIndex k) => k.AddPlayer(v.PlayerId));
            Add<ChartListing>(sim.ChartListings, ComponentFormatters.Write, ComponentFormatters.ReadChartListing, null,
                PublicKeys<ChartListing>());
            Add(sim.Presences, ComponentFormatters.Write, ComponentFormatters.ReadPlayerPresence,
                (EntityId e, in PlayerPresence v) => IsMine(e) ? Full : Met(e) ? Rival : Hidden,
                (EntityId e, in PlayerPresence v, ViewIndex k) => HqKeys(e, k), 2,
                (int f, in PlayerPresence v) => new PlayerPresence
                {
                    IsNewcomer = v.IsNewcomer, ShieldFromTick = v.ShieldFromTick, ShieldUntilTick = v.ShieldUntilTick
                });
        }

        // Forms of a component (see ViewForm): hidden, in full, or a rival's masked form.
        private const int Hidden = -1;
        private const int Full = 0;
        private const int Rival = 1;

        private static int Show(bool shown) => shown ? Full : Hidden;

        // Keys (see ViewIndex): who might see a component. The form functions above still decide.
        private static ViewKeys<T> PublicKeys<T>() where T : struct => (EntityId e, in T v, ViewIndex k) => k.AddPublic();

        // A player's HQ-held components: its owner, and whoever holds a chart of its core base's system.
        private void HqKeys(EntityId hq, ViewIndex k)
        {
            if (!_sim.Players.Has(hq)) return;
            ref var player = ref _sim.Players.Get(hq);
            k.AddPlayer(player.PlayerId);
            k.AddSiteCluster(player.CoreBase);
        }

        // The player itself, or a rival whose home system the player holds a chart of.
        private bool Met(EntityId hq) => IsMine(hq) || (_sim.Players.Has(hq) && KnowsSite(_sim.Players.Get(hq).CoreBase));

        private void LaneKeys(EntityId lane, ViewIndex k)
        {
            if (!_sim.Starlanes.Has(lane)) return;
            ref var edge = ref _sim.Starlanes.Get(lane);
            k.AddSiteCluster(edge.SiteA);
            k.AddSiteCluster(edge.SiteB);
        }

        private void HolderKeys(EntityId holder, ViewIndex k)
        {
            if (holder == _sim.WtoFeePool)
                k.AddPublic();
            k.AddFleetOwner(holder);
            k.AddSiteOwner(holder);
        }

        // A fleet: its owner, everyone while a radar reveal lasts, and whoever has eyes on the site it is docked at.
        private void FleetKeys(EntityId fleet, ViewIndex k)
        {
            k.AddFleetOwner(fleet);
            if (_sim.FleetSensors.Has(fleet) && _sim.Clock.CurrentTick < _sim.FleetSensors.Get(fleet).RevealedUntilTick)
                k.AddRevealed();
            if (!_sim.FleetMotions.Has(fleet)) return;
            ref var motion = ref _sim.FleetMotions.Get(fleet);
            if (motion.Lane.IsNull)
                k.AddSite(motion.Site);
        }

        private void SystemKeys(EntityId system, ViewIndex k)
        {
            if (_sim.StarSystems.Has(system))
                k.AddCluster(_sim.StarSystems.Get(system).ClusterId);
        }

        /// <summary>
        /// The view now: in full the first time and when <paramref name="full"/> is set (a snapshot), otherwise only
        /// what changed since the last write (a delta). The bytes are valid until the next write.
        /// </summary>
        public ReadOnlySpan<byte> Write(bool full)
        {
            full |= !_wroteOnce;
            _wroteOnce = true;
            if (_ownHub)
                _hub.BeginPass();
            FindEyes();
            _body.Rewind(0);
            _body.WriteArrayHeader(3);
            _body.Write(_sim.Clock.CurrentTick);

            _hub.Orders(_playerId, _candidates);
            _body.WriteArrayHeader(_candidates.Count);
            for (int i = 0; i < _candidates.Count; i++)
            {
                var command = _sim.Clock.PendingAt(_candidates[i]);
                ComponentFormatters.Write(_body, in command);
            }

            var interest = _fullScan ? null : _interest;
            int upserts = 0, removals = 0;
            _body.WriteArrayHeader(_channels.Count);
            for (int i = 0; i < _channels.Count; i++)
                _channels[i].Write(_body, _scratch, interest, _candidates, full, ref upserts, ref removals);
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
            ViewForm<T> form, ViewKeys<T> keys, int forms = 1, ViewMask<T> mask = null) where T : struct =>
            _channels.Add(new ViewChannel<T>(_hub, _channels.Count, store, write, read, form, forms, mask, keys));

        // Sites the player has eyes on (its own sites and the sites its fleets are docked at) and its charted systems,
        // from the hub's per-pass index.
        private void FindEyes()
        {
            _hub.Interest(_playerId, _interest);
            _eyes.Clear();
            for (int i = 0; i < _interest.Sites.Count; i++)
                _eyes.Add(_interest.Sites[i]);
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
            return motion.Lane.IsNull && !motion.Site.IsNull && _sim.Sites.Has(motion.Site) && _eyes.Contains(motion.Site.Index);
        }
    }
}
