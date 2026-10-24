using System;

namespace Unruled.Core.Persistence
{
    /// <summary>
    /// Writes the subset of the MessagePack format the save game and the wire use: nil, bool, integers (smallest
    /// encoding), bin and arrays. Objects are arrays indexed by their <c>[Key(n)]</c>, so the bytes stay readable by MessagePack-CSharp.
    /// </summary>
    public sealed class MsgPackWriter
    {
        private byte[] _buffer;

        public int Length { get; private set; }

        public MsgPackWriter(int initialCapacity = 4096)
        {
            if (initialCapacity < 16) throw new ArgumentOutOfRangeException(nameof(initialCapacity));
            _buffer = new byte[initialCapacity];
        }

        public byte[] ToArray()
        {
            var bytes = new byte[Length];
            Array.Copy(_buffer, bytes, Length);
            return bytes;
        }

        public void WriteNil() => Byte(0xc0);

        public void Write(bool value) => Byte(value ? (byte)0xc3 : (byte)0xc2);

        public void WriteArrayHeader(int count)
        {
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
            if (count <= 15)
                Byte((byte)(0x90 | count));
            else if (count <= ushort.MaxValue)
            {
                Byte(0xdc);
                BigEndian((ulong)count, 2);
            }
            else
            {
                Byte(0xdd);
                BigEndian((ulong)count, 4);
            }
        }

        public void Write(int value) => Write((long)value);

        public void Write(long value)
        {
            if (value >= 0)
            {
                Write((ulong)value);
                return;
            }
            if (value >= -32)
                Byte((byte)(sbyte)value);
            else if (value >= sbyte.MinValue)
            {
                Byte(0xd0);
                BigEndian((ulong)value, 1);
            }
            else if (value >= short.MinValue)
            {
                Byte(0xd1);
                BigEndian((ulong)value, 2);
            }
            else if (value >= int.MinValue)
            {
                Byte(0xd2);
                BigEndian((ulong)value, 4);
            }
            else
            {
                Byte(0xd3);
                BigEndian((ulong)value, 8);
            }
        }

        public void Write(ulong value)
        {
            if (value <= 0x7f)
                Byte((byte)value);
            else if (value <= byte.MaxValue)
            {
                Byte(0xcc);
                BigEndian(value, 1);
            }
            else if (value <= ushort.MaxValue)
            {
                Byte(0xcd);
                BigEndian(value, 2);
            }
            else if (value <= uint.MaxValue)
            {
                Byte(0xce);
                BigEndian(value, 4);
            }
            else
            {
                Byte(0xcf);
                BigEndian(value, 8);
            }
        }

        /// <summary>Writes a MessagePack bin (raw bytes).</summary>
        public void Write(ReadOnlySpan<byte> bytes)
        {
            if (bytes.Length <= byte.MaxValue)
            {
                Byte(0xc4);
                BigEndian((ulong)bytes.Length, 1);
            }
            else if (bytes.Length <= ushort.MaxValue)
            {
                Byte(0xc5);
                BigEndian((ulong)bytes.Length, 2);
            }
            else
            {
                Byte(0xc6);
                BigEndian((ulong)bytes.Length, 4);
            }
            Ensure(bytes.Length);
            bytes.CopyTo(new Span<byte>(_buffer, Length, bytes.Length));
            Length += bytes.Length;
        }

        private void Byte(byte value)
        {
            Ensure(1);
            _buffer[Length++] = value;
        }

        private void BigEndian(ulong value, int bytes)
        {
            Ensure(bytes);
            for (int i = bytes - 1; i >= 0; i--)
                _buffer[Length++] = (byte)(value >> (i * 8));
        }

        private void Ensure(int extra)
        {
            if (Length + extra <= _buffer.Length) return;
            int size = _buffer.Length;
            while (size < Length + extra) size *= 2;
            Array.Resize(ref _buffer, size);
        }
    }
}
