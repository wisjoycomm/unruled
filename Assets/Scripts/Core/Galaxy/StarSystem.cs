namespace Unruled.Core.Galaxy
{
    /// <summary>
    /// ECS component: a catalog star that has been materialised into a simulated system (its sites carry
    /// <see cref="ClusterId"/>). Owned by <see cref="GalaxySystem"/>.
    /// </summary>
    public struct StarSystem
    {
        public long StarId; // packed Galaxy.StarId; 0 = a system from before the catalog (schema 5 and older)
        public int ClusterId;
        public int OwnerPlayerId; // the player holding the most planets here; 0 = nobody
        public long MaterialisedTick;
        public int X; // galaxy position, copied from the catalog (or laid out for pre-catalog systems)
        public int Y;
        public StarClass Class;
        public int FirstSurveyorPlayerId; // the player whose survey materialised it; 0 = setup or before charts
    }

    /// <summary>
    /// ECS component on a <see cref="StarSystem"/> entity: the name its owner gave it, up to <see cref="MaxLength"/>
    /// ASCII characters (A–Z, 0–9, space, hyphen). Absent = shown by its catalog code. Owned by
    /// <see cref="GalaxySystem"/>.
    /// </summary>
    public unsafe struct SystemName
    {
        /// <summary>Characters the stored buffer holds (part of the save shape).</summary>
        public const int Capacity = 20;
        /// <summary>Longest legal name: what three int command params carry (6 base-39 characters each).</summary>
        public const int MaxLength = 18;
        public const int MinLength = 3;

        private const int CharsPerParam = 6;
        private const uint Radix = 39; // 0 = end, 1–26 A–Z, 27–36 0–9, 37 space, 38 hyphen

        private fixed byte _chars[Capacity];
        public int Length;
        public int RenamedByPlayerId;
        public long RenamedTick;

        public char CharAt(int i)
        {
            if (i < 0 || i >= Length) throw new System.ArgumentOutOfRangeException(nameof(i));
            return (char)_chars[i];
        }

        /// <summary>Whether <paramref name="text"/> is a legal name: 3–18 of A–Z, 0–9, space or hyphen, not blank-edged.</summary>
        public static bool IsValid(string text)
        {
            if (text == null || text.Length < MinLength || text.Length > MaxLength)
                return false;
            if (text[0] == ' ' || text[text.Length - 1] == ' ')
                return false;
            for (int i = 0; i < text.Length; i++)
                if (Code(text[i]) == 0)
                    return false;
            return true;
        }

        /// <summary>
        /// Packs a legal name into three command params (<see cref="CommandType.RenameSystem"/>), six characters
        /// each in base 39, so a name crosses the network without a string. False for an illegal name.
        /// </summary>
        public static bool TryEncode(string text, out int paramA, out int paramB, out int paramC)
        {
            paramA = paramB = paramC = 0;
            if (!IsValid(text))
                return false;
            paramA = Pack(text, 0);
            paramB = Pack(text, CharsPerParam);
            paramC = Pack(text, 2 * CharsPerParam);
            return true;
        }

        /// <summary>Unpacks three command params into a name; false unless they decode to a legal one.</summary>
        public static bool TryDecode(int paramA, int paramB, int paramC, int playerId, long tick, out SystemName name)
        {
            name = new SystemName { RenamedByPlayerId = playerId, RenamedTick = tick };
            bool ended = false;
            if (!Unpack(ref name, (uint)paramA, ref ended) || !Unpack(ref name, (uint)paramB, ref ended) ||
                !Unpack(ref name, (uint)paramC, ref ended))
                return false;
            return name.Length >= MinLength && name._chars[0] != ' ' && name._chars[name.Length - 1] != ' ';
        }

        private static int Pack(string text, int start)
        {
            uint value = 0;
            for (int i = start + CharsPerParam - 1; i >= start; i--)
                value = value * Radix + (i < text.Length ? Code(text[i]) : 0u);
            return (int)value;
        }

        // Appends one param's characters; a 0 ends the name, and nothing but 0s may follow it.
        private static bool Unpack(ref SystemName name, uint value, ref bool ended)
        {
            for (int i = 0; i < CharsPerParam; i++)
            {
                uint code = value % Radix;
                value /= Radix;
                if (code == 0)
                {
                    ended = true;
                    continue;
                }
                if (ended)
                    return false;
                name.SetChar(name.Length++, Char(code));
            }
            return value == 0;
        }

        private static uint Code(char c) =>
            c >= 'A' && c <= 'Z' ? (uint)(c - 'A' + 1)
            : c >= '0' && c <= '9' ? (uint)(c - '0' + 27)
            : c == ' ' ? 37u
            : c == '-' ? 38u
            : 0u;

        private static char Char(uint code) =>
            code <= 26 ? (char)('A' + code - 1) : code <= 36 ? (char)('0' + code - 27) : code == 37 ? ' ' : '-';

        /// <summary>Builds a name from legal text; throws on anything <see cref="IsValid"/> rejects.</summary>
        public static SystemName From(string text, int playerId, long tick)
        {
            if (!IsValid(text)) throw new System.ArgumentException("Not a legal system name.", nameof(text));
            var name = new SystemName { Length = text.Length, RenamedByPlayerId = playerId, RenamedTick = tick };
            for (int i = 0; i < text.Length; i++)
                name._chars[i] = (byte)text[i];
            return name;
        }

        /// <summary>Copies the characters into <paramref name="buffer"/> (at least <see cref="Capacity"/> long).</summary>
        public int CopyTo(char[] buffer)
        {
            for (int i = 0; i < Length; i++)
                buffer[i] = (char)_chars[i];
            return Length;
        }

        internal void SetChar(int i, char c)
        {
            if (i < 0 || i >= Capacity) throw new System.ArgumentOutOfRangeException(nameof(i));
            _chars[i] = (byte)c;
        }
    }
}
