using System;
using NUnit.Framework;
using System.Collections.Generic;
using Unruled.Core.Ecs;
using Unruled.Core.Military;
using Unruled.Core.Net;
using Unruled.Core.Simulation;

namespace Unruled.Tests.Core.Net
{
    public class PlayerViewTests
    {
        private const int Me = 1;
        private const ulong Seed = 2024;

        private SimulationWorld _server;
        private SimulationWorld _client;
        private PlayerView _serverView;
        private PlayerView _clientView;
        private NewGame _game;

        [SetUp]
        public void SetUp()
        {
            var balance = DefaultGameBalance.Create();
            _server = new SimulationWorld(balance, Seed);
            _game = NewGameSetup.Create(_server, Me);
            _client = new SimulationWorld(balance, Seed);
            _serverView = new PlayerView(_server, Me);
            _clientView = new PlayerView(_client, Me);
        }

        private void Send(bool full) => _clientView.Apply(_serverView.Write(full).ToArray(), full);

        private void Play(long ticks)
        {
            for (long t = 0; t < ticks; t++)
            {
                _server.Clock.Advance(1);
                _server.Ai.Think(_server.Clock.CurrentTick);
            }
        }

        private EntityId RivalFleet(out int rival)
        {
            for (int i = 0; i < _server.Fleets.Count; i++)
            {
                rival = _server.Fleets.At(i).OwnerPlayerId;
                if (rival > 0 && rival != Me)
                    return _server.Fleets.EntityAt(i);
            }
            throw new InvalidOperationException("No rival fleet.");
        }

        private EntityId RivalCore(int rival)
        {
            for (int i = 0; i < _server.Players.Count; i++)
                if (_server.Players.At(i).PlayerId == rival)
                    return _server.Players.At(i).CoreBase;
            throw new InvalidOperationException("No rival HQ.");
        }

        [Test]
        public void Snapshot_ShowsMyEmpire_AndHidesRivalFleetsStockWalletAndResearch()
        {
            Send(full: true);
            var fleet = RivalFleet(out int rival);
            var rivalCore = RivalCore(rival);

            Assert.IsTrue(_client.Fleets.Has(_game.Fleet));
            Assert.IsTrue(_client.Stockpiles.Has(_game.CoreBase));
            Assert.IsTrue(_client.Stockpiles.Has(_game.Fleet));
            Assert.IsTrue(_client.Ledger.TryFindPlayerWallet(Me, out _));
            Assert.IsTrue(_client.TechProgress.Has(_game.PlayerHq));
            Assert.AreEqual(_server.Clock.CurrentTick, _client.Clock.CurrentTick);

            Assert.IsFalse(_client.Fleets.Has(fleet), "a rival fleet at its own home is out of sight");
            Assert.IsFalse(_client.FleetMotions.Has(fleet));
            Assert.IsFalse(_client.Stockpiles.Has(rivalCore), "a rival's stock never appears");
            Assert.IsFalse(_client.Ledger.TryFindPlayerWallet(rival, out _), "a rival's wallet never appears");
            for (int i = 0; i < _client.Wallets.Count; i++)
                Assert.AreEqual(Me, _client.Wallets.At(i).OwnerPlayerId);
            for (int i = 0; i < _client.TechProgress.Count; i++)
                Assert.AreEqual(_game.PlayerHq, _client.TechProgress.EntityAt(i));
            Assert.AreEqual(0, _client.AiControllers.Count, "rivals' AI stays on the server");
            for (int i = 0; i < _client.Sites.Count; i++)
                Assert.IsTrue(_server.Galaxy.Knows(Me, _client.Sites.At(i).ClusterId), "only charted systems' sites");
        }

        [Test]
        public void RivalFleet_DockedAtMySite_IsSeen_ButNotItsHoldOrRoute_AndDropsWhenItLeaves()
        {
            Send(full: true);
            var fleet = RivalFleet(out _);
            ref var motion = ref _server.FleetMotions.Get(fleet);
            var home = motion.Site;
            motion.Site = _game.CoreBase;
            Send(full: false);

            Assert.IsTrue(_client.Fleets.Has(fleet));
            Assert.AreEqual(_game.CoreBase, _client.FleetMotions.Get(fleet).Site);
            Assert.IsFalse(_client.Stockpiles.Has(fleet), "a rival's hold never appears");
            Assert.IsFalse(_client.FleetRoutes.Has(fleet));
            Assert.AreEqual(0, _client.FleetCombat.Get(fleet).AmmoDebtMilli);

            _server.FleetMotions.Get(fleet).Site = home;
            Send(full: false);
            Assert.IsFalse(_client.Fleets.Has(fleet));
            Assert.IsFalse(_client.FleetMotions.Has(fleet));
        }

        [Test]
        public void Deltas_KeepTheMirrorEqualToAFreshSnapshot_OverAGameHour()
        {
            Send(full: true);
            int snapshotBytes = _serverView.Write(full: true).Length;
            _clientView.Apply(_serverView.Write(full: true).ToArray(), true);
            long deltaBytes = 0;
            int maxDelta = 0;
            const int Ticks = 3_600;
            for (int t = 0; t < Ticks; t++)
            {
                Play(1);
                byte[] delta = _serverView.Write(full: false).ToArray();
                deltaBytes += delta.Length;
                maxDelta = Math.Max(maxDelta, delta.Length);
                _clientView.Apply(delta, false);
            }
            TestContext.WriteLine($"snapshot {snapshotBytes} B; delta avg {deltaBytes / Ticks} B/tick, max {maxDelta} B");

            byte[] expected = new PlayerView(_server, Me).Write(full: true).ToArray();
            byte[] mirrored = new PlayerView(_client, Me).Write(full: true).ToArray();
            CollectionAssert.AreEqual(expected, mirrored, "the mirror built from deltas shows what a fresh snapshot shows");
            Assert.AreEqual(_server.Clock.CurrentTick, _client.Clock.CurrentTick);
        }

        [Test]
        public void UnchangedWorld_SendsAnEmptyDelta()
        {
            Send(full: true);
            _serverView.Write(full: false);
            Assert.AreEqual(0, _serverView.LastUpserts);
            Assert.AreEqual(0, _serverView.LastRemovals);
        }

        [Test]
        public void ViewsSharingAHub_WriteTheSameBytes_AsViewsWithTheirOwn()
        {
            var hub = new PlayerViewHub(_server);
            var shared = new System.Collections.Generic.List<PlayerView>();
            var alone = new System.Collections.Generic.List<PlayerView>();
            for (int i = 0; i < _server.Players.Count; i++)
            {
                int id = _server.Players.At(i).PlayerId;
                shared.Add(new PlayerView(hub, id));
                alone.Add(new PlayerView(_server, id));
            }
            for (int t = 0; t < 600; t++)
            {
                Play(1);
                hub.BeginPass();
                for (int v = 0; v < shared.Count; v++)
                {
                    bool full = t % 200 == 0;
                    byte[] a = shared[v].Write(full).ToArray();
                    byte[] b = alone[v].Write(full).ToArray();
                    CollectionAssert.AreEqual(b, a, $"player {shared[v].PlayerId} at tick {_server.Clock.CurrentTick}");
                }
            }
        }

        private sealed class Outbox : ICommandSink
        {
            public readonly List<DelayedPlayerCommand> Sent = new List<DelayedPlayerCommand>();
            public void Send(in DelayedPlayerCommand command) => Sent.Add(command);
        }

        [Test]
        public void MirrorOrders_GoToTheServer_AndComeBackInFlightThroughTheView()
        {
            Send(full: true);
            var outbox = new Outbox();
            _client.Issuer.SendTo(outbox);
            var order = new DelayedPlayerCommand
            {
                PlayerId = Me, CommandType = CommandType.SetRof, Target = _game.Fleet, ParamA = (int)RofStance.Conservation50
            };
            Assert.AreEqual(IssueResult.Issued, _client.Issuer.Issue(order, out _));
            Assert.AreEqual(1, outbox.Sent.Count);
            Assert.AreEqual(0, _client.Clock.PendingCommandCount, "the mirror's clock never queues an order itself");

            Assert.AreEqual(IssueResult.Issued, _server.Issuer.Issue(outbox.Sent[0], out long lands));
            Send(full: false);
            Assert.AreEqual(1, _client.Clock.PendingCommandCount);
            Assert.AreEqual(lands, _client.Clock.PendingAt(0).ExecuteAtTick);

            Play(lands - _server.Clock.CurrentTick);
            Send(full: false);
            Assert.AreEqual(0, _client.Clock.PendingCommandCount);
            Assert.AreEqual(RofStance.Conservation50, _client.FleetCombat.Get(_game.Fleet).Rof);
        }
    }
}
