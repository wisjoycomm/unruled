using System;
using System.Collections.Generic;
using NUnit.Framework;
using Unruled.Core.Simulation;

namespace Unruled.Tests.Core.Simulation
{
    public class SimClockTests
    {
        private sealed class RecordingHandler : ICommandHandler
        {
            public readonly List<string> Log = new List<string>();

            public void Handle(in DelayedPlayerCommand command, long tick) =>
                Log.Add($"cmd:{command.TargetEntityId}@{tick}");
        }

        private sealed class RecordingSystem : ISimSystem
        {
            private readonly string _name;
            private readonly List<string> _log;

            public RecordingSystem(string name, List<string> log)
            {
                _name = name;
                _log = log;
            }

            public void OnTick(long tick) => _log.Add($"{_name}@{tick}");
        }

        private static DelayedPlayerCommand Cmd(int target, long issued, long execute) => new DelayedPlayerCommand
        {
            IssuedAtTick = issued,
            ExecuteAtTick = execute,
            PlayerId = 1,
            CommandType = CommandType.Move,
            TargetEntityId = target
        };

        [Test]
        public void SimTick_ConvertsUnits()
        {
            var tick = new SimTick(2 * SimTick.TicksPerDay + 3 * SimTick.TicksPerHour);
            Assert.AreEqual(2, tick.ToDays());
            Assert.AreEqual(51, tick.ToHours());
            Assert.AreEqual(51 * 60, tick.ToMinutes());
            Assert.IsTrue(new SimTick(5) < new SimTick(6));
        }

        [Test]
        public void Advance_IncrementsCurrentTick()
        {
            var clock = new SimClock(new RecordingHandler());
            clock.Advance(3_600);
            Assert.AreEqual(3_600, clock.CurrentTick);
        }

        [Test]
        public void Commands_ExecuteOnTheirTick_InTickThenEnqueueOrder()
        {
            var handler = new RecordingHandler();
            var clock = new SimClock(handler);
            clock.Enqueue(Cmd(target: 1, issued: 0, execute: 15));
            clock.Enqueue(Cmd(target: 2, issued: 0, execute: 8));
            clock.Enqueue(Cmd(target: 3, issued: 0, execute: 15));

            clock.Advance(7);
            Assert.IsEmpty(handler.Log);

            clock.Advance(8);
            CollectionAssert.AreEqual(new[] { "cmd:2@8", "cmd:1@15", "cmd:3@15" }, handler.Log);
            Assert.AreEqual(0, clock.PendingCommandCount);
        }

        [Test]
        public void Commands_RunBeforeSystems_SystemsInRegistrationOrder()
        {
            var handler = new RecordingHandler();
            var clock = new SimClock(handler);
            var log = handler.Log;
            clock.Register(new RecordingSystem("a", log));
            clock.Register(new RecordingSystem("b", log));
            clock.Enqueue(Cmd(target: 9, issued: 0, execute: 1));

            clock.Advance(2);

            CollectionAssert.AreEqual(new[] { "cmd:9@1", "a@1", "b@1", "a@2", "b@2" }, log);
        }

        [Test]
        public void Enqueue_RejectsCommandsNotInTheFuture()
        {
            var clock = new SimClock(new RecordingHandler(), startTick: 100);
            Assert.Throws<ArgumentException>(() => clock.Enqueue(Cmd(1, issued: 100, execute: 100)));
            Assert.Throws<ArgumentException>(() => clock.Enqueue(Cmd(1, issued: 120, execute: 110)));
        }

        [Test]
        public void SameInputs_ProduceSameSequence()
        {
            List<string> Run()
            {
                var handler = new RecordingHandler();
                var clock = new SimClock(handler);
                for (int i = 0; i < 50; i++)
                    clock.Enqueue(Cmd(i, issued: 0, execute: 1 + (i * 7) % 13));
                clock.Advance(20);
                return handler.Log;
            }

            CollectionAssert.AreEqual(Run(), Run());
        }
    }
}
