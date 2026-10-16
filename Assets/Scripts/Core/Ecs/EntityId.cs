using System;

namespace Unruled.Core.Ecs
{
    /// <summary>Handle to an entity. Generation 0 is never issued, so <c>default</c> is the null entity.</summary>
    public readonly struct EntityId : IEquatable<EntityId>
    {
        public readonly int Index;
        public readonly int Generation;

        public EntityId(int index, int generation)
        {
            Index = index;
            Generation = generation;
        }

        public bool IsNull => Generation == 0;

        public bool Equals(EntityId other) => Index == other.Index && Generation == other.Generation;
        public override bool Equals(object obj) => obj is EntityId other && Equals(other);
        public override int GetHashCode() => (Index * 397) ^ Generation;
        public override string ToString() => $"{Index}:{Generation}";

        public static bool operator ==(EntityId a, EntityId b) => a.Equals(b);
        public static bool operator !=(EntityId a, EntityId b) => !a.Equals(b);
    }
}
