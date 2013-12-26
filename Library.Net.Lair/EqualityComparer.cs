using System;
using System.Collections.Generic;

namespace Library.Net.Lair
{
    class ByteArrayEqualityComparer : IEqualityComparer<byte[]>
    {
        public bool Equals(byte[] x, byte[] y)
        {
            if (x == null && y == null) return true;
            if ((x == null) != (y == null)) return false;
            if (object.ReferenceEquals(x, y)) return true;

            return Unsafe.Equals(x, y);
        }

        public int GetHashCode(byte[] value)
        {
            if (value.Length >= 4) return BitConverter.ToInt32(value, 0) & 0x7FFFFFFF;
            else if (value.Length >= 2) return BitConverter.ToUInt16(value, 0);
            else return value[0];
        }
    }
}
