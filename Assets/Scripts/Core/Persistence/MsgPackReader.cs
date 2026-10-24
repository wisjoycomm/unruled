using System;

namespace Unruled.Core.Persistence
{
    /// <summary>
    /// Reads what <see cref="MsgPackWriter"/> writes, accepting any integer encoding, and skips any other MessagePack
    /// value, so keys appended by a newer build can be stepped over. Throws <see cref="FormatException"/> on bad data.
    /// </summary>
    public sealed class MsgPackReader
    {
        private readonly ReadOnlyMemory<byte> _data;
        private int _position;

        public MsgPackReader(ReadOnlyMemory<byte> data) => _data = data;

        public bool End => _position >= _data.Length;

        public bool TryReadNil()
        {
            if (Peek() != 0xc0) return false;
            _position++;
            return true;
        }

        public bool ReadBool()
        {
            byte code = Next();
            if (code == 0xc2) return false;
            if (code == 0xc3) return true;
            throw Unexpected(code, "bool");
        }

        public int ReadArrayHeader()
        {
            byte code = Next();
            if ((code & 0xf0) == 0x90) return code & 0x0f;
            if (code == 0xdc) return (int)BigEndian(2);
            if (code == 0xdd) return checked((int)BigEndian(4));
            throw Unexpected(code, "array");
        }

        public int ReadInt32() => checked((int)ReadInt64());
        public byte ReadByte() => checked((byte)ReadInt64());

        public long ReadInt64()
        {
            byte code = Next();
            if (code <= 0x7f) return code;
            if (code >= 0xe0) return (sbyte)code;
            switch (code)
            {
                case 0xcc: return (long)BigEndian(1);
                case 0xcd: return (long)BigEndian(2);
                case 0xce: return (long)BigEndian(4);
                case 0xcf: return checked((long)BigEndian(8));
                case 0xd0: return (sbyte)BigEndian(1);
                case 0xd1: return (short)BigEndian(2);
                case 0xd2: return (int)BigEndian(4);
                case 0xd3: return (long)BigEndian(8);
                default: throw Unexpected(code, "integer");
            }
        }

        /// <summary>Reads a MessagePack bin into a new array.</summary>
        public byte[] ReadBytes()
        {
            byte code = Next();
            int length;
            if (code == 0xc4) length = (int)BigEndian(1);
            else if (code == 0xc5) length = (int)BigEndian(2);
            else if (code == 0xc6) length = checked((int)BigEndian(4));
            else throw Unexpected(code, "bin");
            if (_position + length > _data.Length) throw new FormatException("Save data ends early.");
            byte[] bytes = _data.Slice(_position, length).ToArray();
            _position += length;
            return bytes;
        }

        public ulong ReadUInt64()
        {
            byte code = Peek();
            if (code == 0xcf)
            {
                _position++;
                return BigEndian(8);
            }
            return checked((ulong)ReadInt64());
        }

        /// <summary>Skips <paramref name="count"/> whole values of any type.</summary>
        public void Skip(int count)
        {
            for (int i = 0; i < count; i++)
                SkipOne();
        }

        private void SkipOne()
        {
            byte code = Next();
            if (code <= 0x7f || code >= 0xe0 || code == 0xc0 || code == 0xc2 || code == 0xc3) return;
            if ((code & 0xf0) == 0x90) { Skip(code & 0x0f); return; }
            if ((code & 0xf0) == 0x80) { Skip(2 * (code & 0x0f)); return; }
            if ((code & 0xe0) == 0xa0) { Advance(code & 0x1f); return; }
            switch (code)
            {
                case 0xcc: case 0xd0: case 0xd4: Advance(1); return;
                case 0xcd: case 0xd1: Advance(2); return;
                case 0xce: case 0xd2: case 0xca: Advance(4); return;
                case 0xcf: case 0xd3: case 0xcb: Advance(8); return;
                case 0xd5: Advance(3); return;
                case 0xd6: Advance(5); return;
                case 0xd7: Advance(9); return;
                case 0xd8: Advance(17); return;
                case 0xc4: case 0xd9: Advance((int)BigEndian(1)); return;
                case 0xc5: case 0xda: Advance((int)BigEndian(2)); return;
                case 0xc6: case 0xdb: Advance(checked((int)BigEndian(4))); return;
                case 0xc7: Advance((int)BigEndian(1) + 1); return;
                case 0xc8: Advance((int)BigEndian(2) + 1); return;
                case 0xc9: Advance(checked((int)BigEndian(4) + 1)); return;
                case 0xdc: Skip((int)BigEndian(2)); return;
                case 0xdd: Skip(checked((int)BigEndian(4))); return;
                case 0xde: Skip(2 * (int)BigEndian(2)); return;
                case 0xdf: Skip(checked(2 * (int)BigEndian(4))); return;
                default: throw Unexpected(code, "value");
            }
        }

        private byte Peek()
        {
            if (_position >= _data.Length) throw new FormatException("Save data ends early.");
            return _data.Span[_position];
        }

        private byte Next()
        {
            byte code = Peek();
            _position++;
            return code;
        }

        private void Advance(int bytes)
        {
            if (bytes < 0 || _position + bytes > _data.Length) throw new FormatException("Save data ends early.");
            _position += bytes;
        }

        private ulong BigEndian(int bytes)
        {
            if (_position + bytes > _data.Length) throw new FormatException("Save data ends early.");
            var span = _data.Span;
            ulong value = 0;
            for (int i = 0; i < bytes; i++)
                value = (value << 8) | span[_position++];
            return value;
        }

        private FormatException Unexpected(byte code, string expected) =>
            new FormatException($"Expected {expected} at byte {_position - 1}, found 0x{code:x2}.");
    }
}
