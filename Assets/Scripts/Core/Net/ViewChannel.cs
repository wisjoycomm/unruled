using System;
using System.Collections.Generic;
using Unruled.Core.Ecs;
using Unruled.Core.Persistence;

namespace Unruled.Core.Net
{
    internal delegate void ComponentWriter<T>(MsgPackWriter w, in T value);
    internal delegate T ComponentReader<T>(MsgPackReader r);

    /// <summary>Which form of a component one player sees: -1 hidden, 0 in full, 1.. a masked form (see <see cref="ViewMask{T}"/>).</summary>
    internal delegate int ViewForm<T>(EntityId entity, in T value);

    /// <summary>A masked form of a component. It may depend on the form and the component, never on the player, so one
    /// encoding serves every player who sees that form.</summary>
    internal delegate T ViewMask<T>(int form, in T value);

    /// <summary>One component store as a <see cref="PlayerView"/> carries it.</summary>
    internal abstract class ViewChannel
    {
        /// <summary>Writes <c>[upserts [[entity, component]...], removals [entity...]]</c>.</summary>
        /// <param name="interest">What the view looks for; null scans the whole store.</param>
        public abstract void Write(MsgPackWriter body, MsgPackWriter scratch, ViewInterest interest, List<int> candidates,
            bool full, ref int upserts, ref int removals);

        /// <summary>Applies what <see cref="Write"/> wrote; true when the store changed.</summary>
        public abstract bool Apply(MsgPackReader r, bool full);
    }

    /// <summary>
    /// The view of one <see cref="ComponentStore{T}"/>: the components the form function shows, in the form it picks.
    /// With keys (see <see cref="ViewIndex"/>), a view visits only the components under the keys it holds; the form
    /// function still decides, so the keys only have to cover everyone who might see a component.
    /// Each (component, form) is encoded once per hub pass and shared by every view of the world; a view only compares
    /// the encoding's 64-bit hash with the one it last sent and copies the bytes when they differ. A delta holds those
    /// and the entities no longer shown.
    /// </summary>
    internal sealed class ViewChannel<T> : ViewChannel where T : struct
    {
        private readonly ComponentStore<T> _store;
        private readonly ComponentWriter<T> _write;
        private readonly ComponentReader<T> _read;
        private readonly ViewForm<T> _form;
        private readonly ViewMask<T> _mask;
        private readonly PlayerViewHub _hub;
        private readonly EncodedCache _cache;
        private readonly ViewKeys<T> _keys;
        private readonly ViewIndex _index;
        private Dictionary<EntityId, ulong> _sent = new Dictionary<EntityId, ulong>();
        private Dictionary<EntityId, ulong> _next = new Dictionary<EntityId, ulong>();
        private readonly List<EntityId> _removed = new List<EntityId>();

        public ViewChannel(PlayerViewHub hub, int index, ComponentStore<T> store, ComponentWriter<T> write,
            ComponentReader<T> read, ViewForm<T> form, int forms, ViewMask<T> mask, ViewKeys<T> keys)
        {
            _keys = keys;
            _index = keys != null ? hub.Index(index) : null;
            _hub = hub ?? throw new ArgumentNullException(nameof(hub));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _write = write ?? throw new ArgumentNullException(nameof(write));
            _read = read ?? throw new ArgumentNullException(nameof(read));
            if (forms < 1 || (forms > 1 && mask == null)) throw new ArgumentException("Masked forms need a mask.", nameof(forms));
            _form = form;
            _mask = mask;
            _cache = hub.Cache(index, forms);
        }

        public override void Write(MsgPackWriter body, MsgPackWriter scratch, ViewInterest interest, List<int> candidates,
            bool full, ref int upserts, ref int removals)
        {
            if (full)
                _sent.Clear();
            _next.Clear();
            scratch.Rewind(0);
            _cache.Begin(_hub.Epoch, _store.Count);
            bool indexed = _keys != null && interest != null;
            if (indexed)
            {
                BuildIndex();
                interest.Collect(_index, candidates);
            }
            int visits = indexed ? candidates.Count : _store.Count;
            int count = 0;
            for (int v = 0; v < visits; v++)
            {
                int i = indexed ? candidates[v] : v;
                var entity = _store.EntityAt(i);
                ref var value = ref _store.At(i);
                int form = _form == null ? 0 : _form(entity, in value);
                if (form < 0)
                    continue;
                if (!_cache.TryGet(form, i, out ulong hash))
                    hash = Encode(form, i, entity, in value);
                _next[entity] = hash;
                if (_sent.TryGetValue(entity, out ulong old) && old == hash)
                    continue;
                scratch.WriteRaw(_cache.Bytes(form, i));
                count++;
            }

            _removed.Clear();
            foreach (var sent in _sent)
                if (!_next.ContainsKey(sent.Key))
                    _removed.Add(sent.Key);

            body.WriteArrayHeader(2);
            body.WriteArrayHeader(count);
            body.WriteRaw(scratch.WrittenSpan);
            body.WriteArrayHeader(_removed.Count);
            for (int i = 0; i < _removed.Count; i++)
                ComponentFormatters.WriteEntity(body, _removed[i]);
            upserts += count;
            removals += _removed.Count;

            var swap = _sent;
            _sent = _next;
            _next = swap;
        }

        // Once per pass, shared by every view: each component's keys.
        private void BuildIndex()
        {
            if (_index.Epoch == _hub.Epoch)
                return;
            _index.Begin(_hub.Epoch);
            for (int i = 0; i < _store.Count; i++)
            {
                _index.At(i);
                _keys(_store.EntityAt(i), in _store.At(i), _index);
            }
            _index.Seal();
        }

        private ulong Encode(int form, int slot, EntityId entity, in T value)
        {
            var w = _cache.BeginPut(form);
            int mark = w.Length;
            w.WriteArrayHeader(2);
            ComponentFormatters.WriteEntity(w, entity);
            if (form == 0)
                _write(w, in value);
            else
            {
                var masked = _mask(form, in value);
                _write(w, in masked);
            }
            ulong hash = Hash(w.WrittenSpan.Slice(mark));
            _cache.EndPut(form, slot, mark, hash);
            return hash;
        }

        public override bool Apply(MsgPackReader r, bool full)
        {
            int keys = r.ReadArrayHeader();
            if (full)
                ((IComponentStore)_store).Clear();
            int upserts = keys > 0 ? r.ReadArrayHeader() : 0;
            for (int i = 0; i < upserts; i++)
            {
                int fields = r.ReadArrayHeader();
                if (fields < 2) throw new FormatException("View entry needs an entity and a component.");
                var entity = ComponentFormatters.ReadEntity(r);
                if (entity.IsNull) throw new FormatException("View entry for the null entity.");
                _store.Set(entity, _read(r));
                if (fields > 2) r.Skip(fields - 2);
            }
            int removals = keys > 1 ? r.ReadArrayHeader() : 0;
            for (int i = 0; i < removals; i++)
            {
                var entity = ComponentFormatters.ReadEntity(r);
                if (_store.Has(entity))
                    _store.Remove(entity);
            }
            if (keys > 2) r.Skip(keys - 2);
            return full || upserts > 0 || removals > 0;
        }

        // FNV-1a, 64-bit.
        private static ulong Hash(ReadOnlySpan<byte> bytes)
        {
            ulong hash = 14695981039346656037UL;
            for (int i = 0; i < bytes.Length; i++)
            {
                hash ^= bytes[i];
                hash *= 1099511628211UL;
            }
            return hash;
        }
    }
}
