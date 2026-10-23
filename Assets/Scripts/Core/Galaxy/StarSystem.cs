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
    }

    /// <summary>
    /// ECS component on a <see cref="StarSystem"/> entity: the name its owner gave it, up to <see cref="MaxLength"/>
    /// ASCII characters (A–Z, 0–9, space, hyphen). Absent = shown by its catalog code. Owned by
    /// <see cref="GalaxySystem"/>.
    /// </summary>
    public unsafe struct SystemName
    {
        public const int MaxLength = 20;
        public const int MinLength = 3;

        private fixed byte _chars[MaxLength];
        public int Length;
        public int RenamedByPlayerId;
        public long RenamedTick;

        public char CharAt(int i)
        {
            if (i < 0 || i >= Length) throw new System.ArgumentOutOfRangeException(nameof(i));
            return (char)_chars[i];
        }

        /// <summary>Whether <paramref name="text"/> is a legal name: 3–20 of A–Z, 0–9, space or hyphen, not blank-edged.</summary>
        public static bool IsValid(string text)
        {
            if (text == null || text.Length < MinLength || text.Length > MaxLength)
                return false;
            if (text[0] == ' ' || text[text.Length - 1] == ' ')
                return false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                bool ok = (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == ' ' || c == '-';
                if (!ok)
                    return false;
            }
            return true;
        }

        /// <summary>Builds a name from legal text; throws on anything <see cref="IsValid"/> rejects.</summary>
        public static SystemName From(string text, int playerId, long tick)
        {
            if (!IsValid(text)) throw new System.ArgumentException("Not a legal system name.", nameof(text));
            var name = new SystemName { Length = text.Length, RenamedByPlayerId = playerId, RenamedTick = tick };
            for (int i = 0; i < text.Length; i++)
                name._chars[i] = (byte)text[i];
            return name;
        }

        /// <summary>Copies the characters into <paramref name="buffer"/> (at least <see cref="MaxLength"/> long).</summary>
        public int CopyTo(char[] buffer)
        {
            for (int i = 0; i < Length; i++)
                buffer[i] = (char)_chars[i];
            return Length;
        }

        internal void SetChar(int i, char c)
        {
            if (i < 0 || i >= MaxLength) throw new System.ArgumentOutOfRangeException(nameof(i));
            _chars[i] = (byte)c;
        }
    }
}
