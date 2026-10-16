using System;

namespace Unruled.Core.Simulation
{
    /// <summary>One simulation tick equals one real second. Stored as a 64-bit integer to avoid float drift.</summary>
    public readonly struct SimTick : IComparable<SimTick>, IEquatable<SimTick>
    {
        public const long TicksPerMinute = 60L;
        public const long TicksPerHour = 3_600L;
        public const long TicksPerDay = 86_400L;

        public readonly long Value;

        public SimTick(long value) => Value = value;

        public long ToMinutes() => Value / TicksPerMinute;
        public long ToHours() => Value / TicksPerHour;
        public long ToDays() => Value / TicksPerDay;

        public int CompareTo(SimTick other) => Value.CompareTo(other.Value);
        public bool Equals(SimTick other) => Value == other.Value;
        public override bool Equals(object obj) => obj is SimTick other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public override string ToString() => Value.ToString();

        public static bool operator ==(SimTick a, SimTick b) => a.Value == b.Value;
        public static bool operator !=(SimTick a, SimTick b) => a.Value != b.Value;
        public static bool operator <(SimTick a, SimTick b) => a.Value < b.Value;
        public static bool operator >(SimTick a, SimTick b) => a.Value > b.Value;
        public static bool operator <=(SimTick a, SimTick b) => a.Value <= b.Value;
        public static bool operator >=(SimTick a, SimTick b) => a.Value >= b.Value;
    }
}
