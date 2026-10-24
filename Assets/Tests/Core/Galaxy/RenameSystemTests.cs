using NUnit.Framework;
using Unruled.Core.Ecs;
using Unruled.Core.Galaxy;
using Unruled.Core.Military;
using Unruled.Core.Persistence;
using Unruled.Core.Simulation;
using Unruled.Core.SystemFactions;

namespace Unruled.Tests.Core.Galaxy
{
    public class RenameSystemTests
    {
        private SimulationWorld _sim;
        private NewGame _game;
        private EntityId _system;

        [SetUp]
        public void SetUp()
        {
            _sim = new SimulationWorld(DefaultGameBalance.Create(), worldSeed: 12);
            _game = NewGameSetup.Create(_sim, 1);
            _sim.Clock.Advance(GalaxySystem.OwnerRefreshTicks); // owners are worked out
            _system = _sim.Galaxy.SystemOf(_sim.Sites.Get(_game.CoreBase).ClusterId);
            Assert.AreEqual(1, _sim.StarSystems.Get(_system).OwnerPlayerId, "the player holds its home system");
        }

        private IssueResult Rename(string text, int playerId = 1, EntityId site = default)
        {
            Assert.IsTrue(SystemName.TryEncode(text, out int a, out int b, out int c), text);
            var command = new DelayedPlayerCommand
            {
                PlayerId = playerId, CommandType = CommandType.RenameSystem, Target = site.IsNull ? _game.CoreBase : site,
                ParamA = a, ParamB = b, ParamC = c
            };
            var result = _sim.Issuer.Issue(command, out long at);
            if (result == IssueResult.Issued)
                _sim.Clock.Advance(at - _sim.Clock.CurrentTick + 1);
            return result;
        }

        private string NameOf(EntityId system)
        {
            if (!_sim.SystemNames.Has(system))
                return null;
            var chars = new char[SystemName.Capacity];
            return new string(chars, 0, _sim.SystemNames.Get(system).CopyTo(chars));
        }


        [TestCase("NEW EDEN")]
        [TestCase("K-9")]
        [TestCase("ABCDEFGHIJKLMNOPQR")]
        [TestCase("A1 B2-C3 D4")]
        public void Encode_ThenDecode_GivesTheSameName(string text)
        {
            Assert.IsTrue(SystemName.TryEncode(text, out int a, out int b, out int c));
            Assert.IsTrue(SystemName.TryDecode(a, b, c, 3, 77, out var name));
            var chars = new char[SystemName.Capacity];
            Assert.AreEqual(text, new string(chars, 0, name.CopyTo(chars)));
            Assert.AreEqual(3, name.RenamedByPlayerId);
            Assert.AreEqual(77, name.RenamedTick);
        }

        [TestCase("ABCDEFGHIJKLMNOPQRS")] // 19: over the 18 three params carry
        [TestCase("new eden")]
        [TestCase("AB")]
        [TestCase("EDGE ")]
        public void Encode_RefusesIllegalNames(string text)
        {
            Assert.IsFalse(SystemName.TryEncode(text, out _, out _, out _));
        }

        [Test]
        public void Decode_RefusesGarbageParams()
        {
            Assert.IsFalse(SystemName.TryDecode(0, 0, 0, 1, 0, out _), "empty");
            Assert.IsFalse(SystemName.TryDecode(-1, 0, 0, 1, 0, out _), "out of range");
            Assert.IsFalse(SystemName.TryDecode(1, 0, 1, 1, 0, out _), "a gap inside the name");
            Assert.IsFalse(SystemName.TryDecode(37 + 39 * 1 + 39 * 39 * 2, 0, 0, 1, 0, out _), "\" AB\": leading space");
        }

        [Test]
        public void Owner_RenamesTheirSystem_AndTheStaAnnouncesIt()
        {
            int bulletins = _sim.Bulletins.Count;
            Assert.AreEqual(IssueResult.Issued, Rename("NEW EDEN"));
            Assert.AreEqual("NEW EDEN", NameOf(_system));
            Assert.AreEqual(1, _sim.SystemNames.Get(_system).RenamedByPlayerId);
            Assert.AreEqual(bulletins + 1, _sim.Bulletins.Count);
            var bulletin = _sim.Bulletins.At(_sim.Bulletins.Count - 1);
            Assert.AreEqual(BulletinKind.SystemRenamed, bulletin.Kind);
            Assert.AreEqual(SystemAgency.Sta, bulletin.Agency);
            Assert.AreEqual(_system, bulletin.Site);

            Rename("SECOND HOME");
            Assert.AreEqual("SECOND HOME", NameOf(_system), "an owner may rename again");
        }

        [Test]
        public void NonOwner_CannotRename()
        {
            int rivalCluster = 1;
            var rivalSystem = _sim.Galaxy.SystemOf(rivalCluster);
            Assert.AreEqual(2, _sim.StarSystems.Get(rivalSystem).OwnerPlayerId);
            var rivalSite = _sim.Galaxy.SiteAtOrbit(rivalCluster, 0);
            // The rival's home is out of signal reach, so the order is queued straight on the clock.
            Assert.IsTrue(SystemName.TryEncode("MINE NOW", out int a, out int b, out int c));
            _sim.Clock.Enqueue(new DelayedPlayerCommand
            {
                PlayerId = 1, CommandType = CommandType.RenameSystem, Target = rivalSite, ParamA = a, ParamB = b,
                ParamC = c, ExecuteAtTick = _sim.Clock.CurrentTick + 1
            });
            _sim.Clock.Advance(1);
            Assert.IsNull(NameOf(rivalSystem));
        }

        [Test]
        public void Rename_SurvivesASaveAndLoad()
        {
            Rename("NEW EDEN");
            var loaded = SaveGame.Load(_sim.Balance, SaveGame.Write(_sim));
            var chars = new char[SystemName.Capacity];
            var name = loaded.SystemNames.Get(_system);
            Assert.AreEqual("NEW EDEN", new string(chars, 0, name.CopyTo(chars)));
        }
    }
}
