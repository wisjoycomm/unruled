using NUnit.Framework;
using Unruled.Core.Board;
using Unruled.Core.Ecs;
using Unruled.Core.Military;
using Unruled.Core.Simulation;

namespace Unruled.Tests.Core.Military
{
    /// <summary>
    /// Hand-built map:  S0 - S1 - S2 - S3   (highways)   and   S0 - S4 - S5 - S2   (bypasses), plus isolated S6.
    /// Player 1's core base is S0.
    /// </summary>
    public class SignalLatencySolverTests
    {
        private const int Me = 1;
        private const int Rival = 2;

        private SimulationWorld _sim;
        private EntityId[] _s;
        private EntityId _laneS1S2;
        private EntityId _laneS2S3;
        private EntityId _hq;
        private LatencyBalance _b;

        [SetUp]
        public void SetUp()
        {
            _sim = new SimulationWorld(DefaultGameBalance.Create(), worldSeed: 1);
            _b = _sim.Balance.Latency;
            _s = new EntityId[7];
            for (int i = 0; i < _s.Length; i++)
            {
                _s[i] = _sim.World.Create();
                _sim.Sites.Add(_s[i], new SiteInfo { PosX = i * 200 });
            }
            Lane(0, 1, StarlaneTier.StaHighway);
            _laneS1S2 = Lane(1, 2, StarlaneTier.StaHighway);
            _laneS2S3 = Lane(2, 3, StarlaneTier.StaHighway);
            Lane(0, 4, StarlaneTier.OuterRimBypass);
            Lane(4, 5, StarlaneTier.OuterRimBypass);
            Lane(5, 2, StarlaneTier.OuterRimBypass);
            _sim.Graph.Rebuild(_sim.Starlanes);

            _hq = _sim.World.Create();
            _sim.Players.Add(_hq, new PlayerHq { PlayerId = Me, CoreBase = _s[0] });
        }

        private EntityId Lane(int a, int b, StarlaneTier tier)
        {
            var lane = _sim.World.Create();
            _sim.Starlanes.Add(lane, new StarlaneEdge { SiteA = _s[a], SiteB = _s[b], Tier = tier, Length = 200 });
            return lane;
        }

        private int ToSite(int index, int player = Me)
        {
            Assert.AreEqual(LatencyStatus.Ok, _sim.Latency.TryComputeToSite(player, _s[index], out int latency));
            return latency;
        }

        private void Relay(int site, int owner = Me)
        {
            var relay = _s[site];
            _sim.Relays.Add(relay, new RelayStation { OwnerPlayerId = owner });
        }

        private EntityId Fleet(EntityId site, EntityId lane, int ewShips, int owner = Me)
        {
            var fleet = _sim.CreateFleet(new FleetUnit { OwnerPlayerId = owner, TechTier = 1, EwShips = ewShips }, _s[0]);
            _sim.FleetMotions.Get(fleet) = new FleetMotion { Site = site, Lane = lane };
            return fleet;
        }

        [Test]
        public void AtCoreBase_IsBaseLatency()
        {
            Assert.AreEqual(_b.BaseTicks, ToSite(0));
        }

        [Test]
        public void NoRelays_CostsFullHopEach()
        {
            Assert.AreEqual(_b.BaseTicks + 1 * _b.HopTicks, ToSite(1));
            Assert.AreEqual(_b.BaseTicks + 2 * _b.HopTicks, ToSite(2));
            Assert.AreEqual(_b.BaseTicks + 3 * _b.HopTicks, ToSite(3));
        }

        [Test]
        public void RelayChain_CostsRelayedHops_OnlyForItsOwner()
        {
            Relay(1);
            Relay(2);
            Relay(3);
            Assert.AreEqual(_b.BaseTicks + 3 * _b.RelayedHopTicks, ToSite(3));

            var rivalHq = _sim.World.Create();
            _sim.Players.Add(rivalHq, new PlayerHq { PlayerId = Rival, CoreBase = _s[0] });
            Assert.AreEqual(_b.BaseTicks + 3 * _b.HopTicks, ToSite(3, Rival));
        }

        [Test]
        public void TakesTheFastestPath_NotTheFewestHops()
        {
            Relay(4);
            Relay(5);
            Relay(2);
            // Highway: 35 + 8 = 43. Relayed bypass chain: 8 + 8 + 8 = 24.
            Assert.AreEqual(_b.BaseTicks + 3 * _b.RelayedHopTicks, ToSite(2));
        }

        [Test]
        public void EwFleets_RelayTheirSiteOrLane_EmptyFleetsDoNot()
        {
            Fleet(_s[1], default, ewShips: 0);
            Assert.AreEqual(_b.BaseTicks + _b.HopTicks, ToSite(1));

            Fleet(_s[1], default, ewShips: 2);
            Assert.AreEqual(_b.BaseTicks + _b.RelayedHopTicks, ToSite(1));

            Fleet(default, _laneS1S2, ewShips: 1);
            Assert.AreEqual(_b.BaseTicks + 2 * _b.RelayedHopTicks, ToSite(2));
        }

        [Test]
        public void TargetOnALane_IsNearerEndPlusOneHop()
        {
            Assert.AreEqual(LatencyStatus.Ok, _sim.Latency.TryComputeToLane(Me, _laneS1S2, out int latency));
            Assert.AreEqual(_b.BaseTicks + 2 * _b.HopTicks, latency);
        }

        [Test]
        public void CapitalConvoyInTransit_MultipliesLatency()
        {
            _sim.Players.Get(_hq).IsCapitalConvoyInTransit = true;
            Assert.AreEqual((_b.BaseTicks + 2 * _b.HopTicks) * _b.RelocationMultiplier, ToSite(2));
        }

        [Test]
        public void RadioBlackout_BlocksTheTarget()
        {
            _sim.Weather.Add(_s[2], new SiteWeather { RadioBlackoutLevel = (byte)(_b.BlackoutRadioLevel - 1) });
            Assert.AreEqual(LatencyStatus.Ok, _sim.Latency.TryComputeToSite(Me, _s[2], out _));

            _sim.Weather.Get(_s[2]).RadioBlackoutLevel = _b.BlackoutRadioLevel;
            Assert.AreEqual(LatencyStatus.Blackout, _sim.Latency.TryComputeToSite(Me, _s[2], out _));
        }

        [Test]
        public void IsolatedSite_IsUnreachable_UnknownPlayer_HasNoHq()
        {
            Assert.AreEqual(LatencyStatus.Unreachable, _sim.Latency.TryComputeToSite(Me, _s[6], out _));
            Assert.AreEqual(LatencyStatus.NoHq, _sim.Latency.TryComputeToSite(99, _s[1], out _));
        }

        [Test]
        public void Issuer_QueuesOrderAtLatency_AndRefusesBlockedOrders()
        {
            var fleet = Fleet(_s[2], default, ewShips: 0); // no ships, so the move costs no fuel

            var order = new DelayedPlayerCommand
            {
                PlayerId = Me, CommandType = CommandType.Move, Target = fleet, ParamEntity = _laneS2S3
            };
            Assert.AreEqual(IssueResult.Issued, _sim.Issuer.Issue(order, out long executeAt));
            Assert.AreEqual(_b.BaseTicks + 2 * _b.HopTicks, executeAt);

            _sim.Clock.Advance(executeAt - 1);
            Assert.AreEqual(_s[2], _sim.FleetMotions.Get(fleet).Site, "not departed before the order arrives");
            _sim.Clock.Advance(1);
            Assert.AreEqual(_laneS2S3, _sim.FleetMotions.Get(fleet).Lane);

            order.Target = _s[6];
            Assert.AreEqual(IssueResult.Unreachable, _sim.Issuer.Issue(order, out _));
            order.Target = _hq;
            Assert.AreEqual(IssueResult.UnknownTarget, _sim.Issuer.Issue(order, out _));
            Assert.AreEqual(0, _sim.Clock.PendingCommandCount);
        }
    }
}
