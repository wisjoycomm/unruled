using System;
using NUnit.Framework;
using Unruled.Core.Persistence;
using Unruled.Core.Simulation;
using Unruled.Core.SystemFactions;

namespace Unruled.Tests.Core.Persistence
{
    public class SaveGameTests
    {
        private static void Play(SimulationWorld sim, long ticks)
        {
            for (long t = 0; t < ticks; t++)
            {
                sim.Clock.Advance(1);
                sim.Ai.Think(sim.Clock.CurrentTick);
            }
        }

        [Test]
        public void LoadedGame_ResavesIdentically_AndRunsInLockstepWithTheOriginal()
        {
            var balance = DefaultGameBalance.Create();
            var original = new SimulationWorld(balance, worldSeed: 4321);
            NewGameSetup.Create(original, 1);
            Play(original, 2 * 3_600);

            byte[] saved = SaveGame.Write(original);
            var loaded = SaveGame.Load(balance, saved);
            CollectionAssert.AreEqual(saved, SaveGame.Write(loaded), "a loaded game saves to the same bytes");
            Assert.AreEqual(original.Clock.CurrentTick, loaded.Clock.CurrentTick);
            Assert.AreEqual(original.World.AliveCount, loaded.World.AliveCount);

            Play(original, 2 * 3_600);
            Play(loaded, 2 * 3_600);
            CollectionAssert.AreEqual(SaveGame.Write(original), SaveGame.Write(loaded),
                "both worlds play on identically");
        }

        [Test]
        public void Load_RefusesASchemaNewerThanThisBuild()
        {
            var balance = DefaultGameBalance.Create();
            byte[] saved = SaveGame.Write(new SimulationWorld(balance, worldSeed: 1));
            Assert.AreEqual(0x96, saved[0], "six top-level keys");
            Assert.AreEqual(SaveGame.SchemaVersion, saved[1]);
            saved[1] = SaveGame.SchemaVersion + 1;
            Assert.Throws<FormatException>(() => SaveGame.Load(balance, saved));
        }

        [Test]
        public void Load_RefusesTruncatedData()
        {
            var balance = DefaultGameBalance.Create();
            byte[] saved = SaveGame.Write(new SimulationWorld(balance, worldSeed: 1));
            Assert.Throws<FormatException>(() => SaveGame.Load(balance, new ReadOnlyMemory<byte>(saved, 0, saved.Length - 3)));
        }

        // Migration rule: a component saved before keys were appended reads them as defaults.
        [Test]
        public void OlderComponentShape_MissingTrailingKeys_ReadsThemAsDefaults()
        {
            var w = new MsgPackWriter();
            w.WriteArrayHeader(2);
            w.Write(70_000);
            w.Write(4);
            var citadel = ComponentFormatters.ReadSystemCitadel(new MsgPackReader(w.ToArray()));

            Assert.AreEqual(70_000, citadel.Integrity);
            Assert.AreEqual(4, citadel.DefenceTier);
            Assert.IsTrue(citadel.Garrison.IsNull);
            Assert.AreEqual(0, citadel.FallenTick);
        }

        [Test]
        public void NewerComponentShape_UnknownTrailingKeys_AreSkipped()
        {
            var w = new MsgPackWriter();
            w.WriteArrayHeader(3);
            w.Write(1_000L);
            w.Write(5);
            w.WriteArrayHeader(2);
            w.Write(7);
            w.WriteNil();
            w.Write(99);
            var r = new MsgPackReader(w.ToArray());
            var record = ComponentFormatters.ReadVictoryRecord(r);

            Assert.AreEqual(1_000, record.DecidedTick);
            Assert.AreEqual(5, record.Seats);
            Assert.AreEqual(99, r.ReadInt32(), "the reader stepped over the unknown key");
            Assert.IsTrue(r.End);
        }

        [Test]
        public void Integers_RoundTripAtEveryEncodingBoundary()
        {
            long[] values =
            {
                0, 1, 127, 128, 255, 256, 65_535, 65_536, uint.MaxValue, (long)uint.MaxValue + 1, long.MaxValue,
                -1, -32, -33, -128, -129, -32_768, -32_769, int.MinValue, (long)int.MinValue - 1, long.MinValue
            };
            var w = new MsgPackWriter(16);
            foreach (long v in values)
                w.Write(v);
            w.Write(ulong.MaxValue);
            w.Write(true);

            var r = new MsgPackReader(w.ToArray());
            foreach (long v in values)
                Assert.AreEqual(v, r.ReadInt64());
            Assert.AreEqual(ulong.MaxValue, r.ReadUInt64());
            Assert.IsTrue(r.ReadBool());
            Assert.IsTrue(r.End);
        }

        [Test]
        public void SavedCitadel_KeepsItsState()
        {
            var balance = DefaultGameBalance.Create();
            var sim = new SimulationWorld(balance, worldSeed: 9);
            NewGameSetup.Create(sim, 1);
            ref SystemCitadel citadel = ref sim.Citadels.At(0);
            citadel.Integrity = 12_345;
            citadel.LastHitTick = 77;

            var loaded = SaveGame.Load(balance, SaveGame.Write(sim));
            Assert.AreEqual(sim.Citadels.EntityAt(0), loaded.Citadels.EntityAt(0));
            Assert.AreEqual(12_345, loaded.Citadels.At(0).Integrity);
            Assert.AreEqual(77, loaded.Citadels.At(0).LastHitTick);
        }
    }
}
