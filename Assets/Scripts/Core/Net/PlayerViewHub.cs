using System;
using System.Collections.Generic;
using Unruled.Core.Persistence;
using Unruled.Core.Simulation;

namespace Unruled.Core.Net
{
    /// <summary>
    /// What every <see cref="PlayerView"/> of one world shares: each component's encoding, made once per pass and
    /// reused by every player who sees it (in the form that player is allowed: full, or masked for a rival).
    /// It also indexes, once per pass, who might see what (<see cref="ViewIndex"/> per channel) and each player's
    /// interest (charted systems, the sites they have eyes on, their orders in flight), so a view visits only the
    /// components near it instead of the whole world.
    /// The host calls <see cref="BeginPass"/> after the world changes (after its ticks, before writing views); between
    /// two calls the world must not change. A view built without a hub keeps a private one and begins a pass on every
    /// write, so it is always correct, only slower.
    /// </summary>
    public sealed class PlayerViewHub
    {
        private EncodedCache[] _caches = new EncodedCache[48];
        private ViewIndex[] _indexes = new ViewIndex[48];
        private readonly ViewIndex _charts; // player -> charted cluster ids
        private readonly ViewIndex _eyes; // player -> entity indices of the sites they have eyes on
        private readonly ViewIndex _orders; // player -> positions of their orders in the clock's queue
        private int _interestEpoch;

        public PlayerViewHub(SimulationWorld sim)
        {
            Sim = sim ?? throw new ArgumentNullException(nameof(sim));
            _charts = new ViewIndex(sim);
            _eyes = new ViewIndex(sim);
            _orders = new ViewIndex(sim);
        }

        public SimulationWorld Sim { get; }

        /// <summary>Starts a pass: every cached encoding is out of date.</summary>
        public void BeginPass() => Epoch++;

        internal int Epoch { get; private set; } = 1;

        internal EncodedCache Cache(int channel, int forms)
        {
            if (channel >= _caches.Length)
                Array.Resize(ref _caches, Math.Max(channel + 1, _caches.Length * 2));
            return _caches[channel] ??= new EncodedCache(forms);
        }

        internal ViewIndex Index(int channel)
        {
            if (channel >= _indexes.Length)
                Array.Resize(ref _indexes, Math.Max(channel + 1, _indexes.Length * 2));
            return _indexes[channel] ??= new ViewIndex(Sim);
        }

        /// <summary>Fills <paramref name="into"/> with what <paramref name="playerId"/> looks for this pass.</summary>
        internal void Interest(int playerId, ViewInterest into)
        {
            BuildInterest();
            into.PlayerId = playerId;
            into.Clusters.Clear();
            _charts.Collect(ViewKey.Player, playerId, into.Clusters);
            into.Sites.Clear();
            _eyes.Collect(ViewKey.Player, playerId, into.Sites);
        }

        /// <summary>Positions in the clock's queue of the player's orders in flight, in queue order.</summary>
        internal void Orders(int playerId, List<int> into)
        {
            BuildInterest();
            into.Clear();
            _orders.Collect(ViewKey.Player, playerId, into);
            into.Sort();
        }

        // Once per pass: charts, eyes (own sites, sites own fleets are docked at) and orders, bucketed by player.
        private void BuildInterest()
        {
            if (_interestEpoch == Epoch)
                return;
            _interestEpoch = Epoch;
            var sim = Sim;
            _charts.Begin(Epoch);
            for (int i = 0; i < sim.SystemCharts.Count; i++)
            {
                ref var chart = ref sim.SystemCharts.At(i);
                _charts.At(chart.ClusterId);
                _charts.AddPlayer(chart.PlayerId);
            }
            _charts.Seal();

            _eyes.Begin(Epoch);
            for (int i = 0; i < sim.Sites.Count; i++)
            {
                _eyes.At(sim.Sites.EntityAt(i).Index);
                _eyes.AddPlayer(sim.Sites.At(i).OwnerPlayerId);
            }
            for (int i = 0; i < sim.Fleets.Count; i++)
            {
                var fleet = sim.Fleets.EntityAt(i);
                if (!sim.FleetMotions.Has(fleet))
                    continue;
                ref var motion = ref sim.FleetMotions.Get(fleet);
                if (!motion.Lane.IsNull || motion.Site.IsNull)
                    continue;
                _eyes.At(motion.Site.Index);
                _eyes.AddPlayer(sim.Fleets.At(i).OwnerPlayerId);
            }
            _eyes.Seal();

            _orders.Begin(Epoch);
            for (int i = 0; i < sim.Clock.PendingCommandCount; i++)
            {
                _orders.At(i);
                _orders.AddPlayer(sim.Clock.PendingAt(i).PlayerId);
            }
            _orders.Seal();
        }
    }

    /// <summary>One channel's encodings for the current pass, per form and per dense slot of its store.</summary>
    internal sealed class EncodedCache
    {
        private readonly Form[] _forms;
        private int _epoch;

        private sealed class Form
        {
            public readonly MsgPackWriter Bytes = new MsgPackWriter(16 * 1024);
            public int[] Stamp = Array.Empty<int>();
            public int[] Offset = Array.Empty<int>();
            public int[] Length = Array.Empty<int>();
            public ulong[] Hash = Array.Empty<ulong>();
        }

        public EncodedCache(int forms)
        {
            _forms = new Form[forms];
            for (int f = 0; f < forms; f++)
                _forms[f] = new Form();
        }

        /// <summary>Readies the cache for a pass over a store of <paramref name="slots"/> components.</summary>
        public void Begin(int epoch, int slots)
        {
            if (_epoch == epoch)
                return;
            _epoch = epoch;
            for (int f = 0; f < _forms.Length; f++)
            {
                var form = _forms[f];
                form.Bytes.Rewind(0);
                if (form.Stamp.Length < slots)
                {
                    int size = Math.Max(slots, form.Stamp.Length * 2);
                    form.Stamp = new int[size];
                    form.Offset = new int[size];
                    form.Length = new int[size];
                    form.Hash = new ulong[size];
                }
            }
        }

        public bool TryGet(int form, int slot, out ulong hash)
        {
            var f = _forms[form];
            hash = f.Hash[slot];
            return f.Stamp[slot] == _epoch;
        }

        public MsgPackWriter BeginPut(int form) => _forms[form].Bytes;

        public void EndPut(int form, int slot, int offset, ulong hash)
        {
            var f = _forms[form];
            f.Stamp[slot] = _epoch;
            f.Offset[slot] = offset;
            f.Length[slot] = f.Bytes.Length - offset;
            f.Hash[slot] = hash;
        }

        public ReadOnlySpan<byte> Bytes(int form, int slot)
        {
            var f = _forms[form];
            return f.Bytes.WrittenSpan.Slice(f.Offset[slot], f.Length[slot]);
        }
    }
}
