using System;
using System.Collections.Generic;
using Unruled.Core.Ecs;
using Unruled.Core.Persistence;

namespace Unruled.Core.Net
{
    internal delegate void ComponentWriter<T>(MsgPackWriter w, in T value);
    internal delegate T ComponentReader<T>(MsgPackReader r);
    internal delegate bool ViewFilter<T>(EntityId entity, in T value);
    internal delegate T ViewMask<T>(EntityId entity, in T value);

    /// <summary>One component store as a <see cref="PlayerView"/> carries it.</summary>
    internal abstract class ViewChannel
    {
        /// <summary>Writes <c>[upserts [[entity, component]...], removals [entity...]]</c>.</summary>
        public abstract void Write(MsgPackWriter body, MsgPackWriter scratch, bool full, ref int upserts, ref int removals);

        /// <summary>Applies what <see cref="Write"/> wrote; true when the store changed.</summary>
        public abstract bool Apply(MsgPackReader r, bool full);
    }

    /// <summary>
    /// The view of one <see cref="ComponentStore{T}"/>: the components the filter shows (masked if a mask is set). A
    /// delta holds only those whose encoding changed since the last write (by a 64-bit hash of it) and the entities
    /// no longer shown.
    /// </summary>
    internal sealed class ViewChannel<T> : ViewChannel where T : struct
    {
        private readonly ComponentStore<T> _store;
        private readonly ComponentWriter<T> _write;
        private readonly ComponentReader<T> _read;
        private readonly ViewFilter<T> _shows;
        private readonly ViewMask<T> _mask;
        private Dictionary<EntityId, ulong> _sent = new Dictionary<EntityId, ulong>();
        private Dictionary<EntityId, ulong> _next = new Dictionary<EntityId, ulong>();
        private readonly List<EntityId> _removed = new List<EntityId>();

        public ViewChannel(ComponentStore<T> store, ComponentWriter<T> write, ComponentReader<T> read,
            ViewFilter<T> shows = null, ViewMask<T> mask = null)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _write = write ?? throw new ArgumentNullException(nameof(write));
            _read = read ?? throw new ArgumentNullException(nameof(read));
            _shows = shows;
            _mask = mask;
        }

        public override void Write(MsgPackWriter body, MsgPackWriter scratch, bool full, ref int upserts, ref int removals)
        {
            if (full)
                _sent.Clear();
            _next.Clear();
            scratch.Rewind(0);
            int count = 0;
            for (int i = 0; i < _store.Count; i++)
            {
                var entity = _store.EntityAt(i);
                ref var value = ref _store.At(i);
                if (_shows != null && !_shows(entity, in value))
                    continue;
                int mark = scratch.Length;
                scratch.WriteArrayHeader(2);
                ComponentFormatters.WriteEntity(scratch, entity);
                if (_mask != null)
                {
                    var masked = _mask(entity, in value);
                    _write(scratch, in masked);
                }
                else
                    _write(scratch, in value);
                ulong hash = Hash(scratch.WrittenSpan.Slice(mark));
                _next[entity] = hash;
                if (_sent.TryGetValue(entity, out ulong old) && old == hash)
                    scratch.Rewind(mark);
                else
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
